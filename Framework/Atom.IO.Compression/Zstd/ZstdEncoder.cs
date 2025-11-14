#pragma warning disable CA2213, MA0015

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Внутренний энкодер Zstd: корректный FHD (SS/DID/FCS/WD), блоки RAW/RLE, финализация и опциональный Content Checksum.
/// Гарантирует: Last_Block=1 только на последнем блоке; async-пути не блокируют поток.
/// </summary>
internal sealed class ZstdEncoder : IDisposable
{

    // Кэш канонических Huffman-кодов и длин для словаря (treeless), чтобы не пересчитывать на каждый блок
    private int cachedDictHuffMaxBits;
    private int cachedDictHuffAlphabetSize;
    private bool cachedDictHuffReady;
    private readonly System.IO.Stream baseStream;
    private readonly ZstdEncoderSettings settings;
    private readonly bool useChecksum, allowRle;

    private readonly ZstdEncoderWorkspace workspace = ZstdEncoderWorkspacePool.Rent();
    private bool workspaceReturned;

    private XxHash64 hash;  // Контрольная сумма содержимого (XXH64, младшие 4 байта LE в конце кадра).

    private bool isHeaderWritten, isDisposed;

    private int tailLength;

    // Async scratch (переиспользуемые мелкие буферы, без пер-вызывающих аллокаций):
    // убраны мелкие scratch-буферы; работаем через stackalloc/коалесцирование
    // удалено: раньше использовалось для промежуточного буфера сжатого блока
    private bool dictTried;
    // Межблочная история (континуальная, ёмкость до BlockCap)
    private int histLen;
    // Буфер коалесцированных записей для снижения количества системных вызовов
    private int outCount;
    private int compressFailStreak;
    private int compressSkipLeft;

    private readonly int tailCapacity;
    private readonly int literalCapacity;
    private readonly int historyCapacity;
    private readonly int frameWindowSize;

    private Span<byte> TailBuffer => workspace.TailSpan[..tailCapacity];
    private Memory<byte> TailBufferMemory => workspace.TailMemory[..tailCapacity];
    private Span<byte> LiteralBuffer => workspace.LiteralSpan[..literalCapacity];
    private Span<byte> HistoryBuffer => workspace.GetHistorySpan(historyCapacity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdEncoder([NotNull] System.IO.Stream output, in ZstdEncoderSettings settings)
    {
        baseStream = output;
        this.settings = settings;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.settings.BlockCap);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(this.settings.BlockCap, ZstdStream.MaxRawBlockSize);

        var levelParams = GetParamsForLevel(this.settings.CompressionLevel);
        var userWindow = this.settings.WindowSize;
        if (userWindow <= 0) userWindow = 8 * 1024 * 1024;
        var maxWindowForLevel = 1 << levelParams.WindowLog;
        frameWindowSize = Math.Min(Math.Min(userWindow, ZstdStream.MaxWindowSize), maxWindowForLevel);
        if (frameWindowSize < 1024) frameWindowSize = 1024;

        var blockCapacity = Math.Min(this.settings.BlockCap, ZstdStream.MaxRawBlockSize);
        blockCapacity = Math.Min(blockCapacity, frameWindowSize);
        tailCapacity = Math.Min(blockCapacity, workspace.TailSpan.Length);
        if (tailCapacity < blockCapacity)
            throw new InvalidOperationException("Tail workspace is too small for configured block size");

        literalCapacity = Math.Min(blockCapacity, workspace.LiteralSpan.Length);
        if (literalCapacity < blockCapacity)
            throw new InvalidOperationException("Literal workspace is too small for configured block size");

        if (settings.UseInterBlockHistory && settings.IsCompressedBlocksEnabled)
        {
            historyCapacity = frameWindowSize;
            _ = workspace.GetHistorySpan(historyCapacity);
        }
        else
        {
            historyCapacity = 0;
        }

        var requiredHashEntries = 1 << levelParams.HashLog;
        _ = workspace.GetHashSpan(requiredHashEntries);

        if (this.settings.IsSingleSegment && this.settings.FrameContentSize is null)
            throw new InvalidOperationException("При SingleSegment=true необходимо задать FrameContentSize по спецификации");

        useChecksum = this.settings.IsContentChecksumEnabled;
        hash = new XxHash64();
        allowRle = settings.CompressionLevel > 0;
        if (settings.UseDictHuffmanForLiterals || settings.UseDictTablesForSequences)
            TryParseDictNorms();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureHeader()
    {
        if (isHeaderWritten) return;
        isHeaderWritten = true;

        Span<byte> header = stackalloc byte[32];
        var written = BuildFrameHeader(header);
        baseStream.Write(header[..written]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureHeaderAsync()
    {
        if (isHeaderWritten) return;
        isHeaderWritten = true;

        await EnsureAsyncCapacityAsync(32).ConfigureAwait(false);
        Span<byte> header = stackalloc byte[32];
        var written = BuildFrameHeader(header);
        await WriteCoalescedAsync(header[..written]).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BuildFrameHeader(Span<byte> destination)
    {
        var offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], ZstdStream.FrameMagic);
        offset += 4;

        var descriptor = CreateFrameHeaderDescriptor();
        destination[offset++] = descriptor.Descriptor;

        if (!descriptor.IsSingleSegment)
        {
            destination[offset++] = EncodeWindowDescriptor(frameWindowSize);
        }

        offset = WriteDictionaryId(destination, offset, descriptor.DictionaryFieldSize);

        if (descriptor.FrameSizeBytes > 0)
        {
            offset = WriteFrameContentSize(destination, offset, descriptor);
        }

        return offset;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FrameHeaderDescriptor(byte descriptor, byte dictionaryFieldSize, bool isSingleSegment, int frameSizeBytes, ulong frameSize)
    {
        public byte Descriptor { get; } = descriptor;
        public byte DictionaryFieldSize { get; } = dictionaryFieldSize;
        public bool IsSingleSegment { get; } = isSingleSegment;
        public int FrameSizeBytes { get; } = frameSizeBytes;
        public ulong FrameSize { get; } = frameSize;
    }

    private FrameHeaderDescriptor CreateFrameHeaderDescriptor()
    {
        var singleSegment = settings.IsSingleSegment;
        var checksumFlag = (byte)(useChecksum ? 1 : 0);
        var dictionaryFieldSize = settings.DictionaryId.HasValue ? ComputeDidFieldSize(settings.DictionaryId.Value) : (byte)0;

        var frameSizeBytes = 0;
        ulong frameSizeValue = 0;
        if (settings.FrameContentSize.HasValue)
        {
            frameSizeValue = settings.FrameContentSize.Value;
            var pick = PickFcsSize(frameSizeValue, singleSegment);
            frameSizeBytes = pick;
        }

        var fcsField = ComputeFcsField(singleSegment, frameSizeBytes);
        var descriptor = (byte)((fcsField << 6) |
                                ((singleSegment ? 1 : 0) << 5) |
                                (0 << 4) |
                                (0 << 3) |
                                (checksumFlag << 2) |
                                (dictionaryFieldSize & 0x3));

        return new FrameHeaderDescriptor(descriptor, dictionaryFieldSize, singleSegment, frameSizeBytes, frameSizeValue);
    }

    private static byte ComputeFcsField(bool singleSegment, int frameSizeBytes)
    {
        if (frameSizeBytes == 0) return 0;
        return singleSegment
            ? (byte)(frameSizeBytes switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => 0 })
            : (byte)(frameSizeBytes switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 });
    }

    private int WriteDictionaryId(Span<byte> destination, int offset, byte dictionaryFieldSize)
    {
        if (dictionaryFieldSize == 0) return offset;
        var id = settings.DictionaryId!.Value;
        switch (dictionaryFieldSize)
        {
            case 1:
                destination[offset++] = (byte)id;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)id);
                offset += 2;
                break;
            case 3:
                BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], id);
                offset += 4;
                break;
        }

        return offset;
    }

    private static int WriteFrameContentSize(Span<byte> destination, int offset, FrameHeaderDescriptor descriptor)
    {
        var size = descriptor.FrameSize;
        switch (descriptor.FrameSizeBytes)
        {
            case 1:
                destination[offset++] = (byte)size;
                break;
            case 2:
                {
                    var value = checked((ushort)(size - 256));
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
                    offset += 2;
                    break;
                }
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], (uint)size);
                offset += 4;
                break;
            case 8:
                BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], size);
                offset += 8;
                break;
        }

        return offset;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteOneBlock(ReadOnlySpan<byte> data, bool last)
    {
        System.Diagnostics.Debug.Assert((uint)data.Length <= (uint)tailCapacity, "Block exceeds tail capacity");

        if (TryWriteRleBlock(data, last))
        {
            return;
        }

        if (TryWriteCompressedBlock(data, last))
        {
            return;
        }

        WriteRawBlock(data, last);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteRleBlock(ReadOnlySpan<byte> data, bool last)
    {
        if (!allowRle || !TryDetectRle(data, out var value))
        {
            return false;
        }

        Span<byte> header = stackalloc byte[3];
        WriteBlockHeaderTo(header, blockType: 1, blockSize: data.Length, last: last);
        WriteCoalesced(header);

        WriteCoalesced(value);

        if (useChecksum)
        {
            hash.UpdateRepeat(value, data.Length);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteCompressedBlock(ReadOnlySpan<byte> data, bool last)
    {
        if (!CanAttemptCompression(data))
        {
            return false;
        }

        var required = 3 + data.Length;
        if (!TryPrepareOutputSpan(required))
        {
            RegisterCompressionFailure();
            return false;
        }

        var buffer = workspace.OutputSpan;
        var target = buffer.Slice(outCount, required);
        var prefix = GetPrefix();
        var written = CompressBlock(data, prefix, target, settings.CompressionLevel, out var usedLocal, last);

        if (written > 0 && usedLocal == data.Length)
        {
            outCount += written;
            if (useChecksum)
            {
                hash.Update(data);
            }
            UpdateHistory(data);
            compressFailStreak = 0;
            return true;
        }

        RegisterCompressionFailure();
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRawBlock(ReadOnlySpan<byte> data, bool last)
    {
        Span<byte> header = stackalloc byte[3];
        WriteBlockHeaderTo(header, blockType: 0, blockSize: data.Length, last: last);
        WriteCoalesced(header);
        WriteCoalesced(data);
        if (useChecksum)
        {
            hash.Update(data);
        }
        UpdateHistory(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask WriteOneBlockAsync(ReadOnlyMemory<byte> data, bool last, CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.Assert((uint)data.Length <= (uint)tailCapacity, "Block exceeds tail capacity");

        if (await TryWriteRleBlockAsync(data, last, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await TryWriteCompressedBlockAsync(data, last, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await WriteRawBlockAsync(data, last, cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryWriteRleBlockAsync(ReadOnlyMemory<byte> data, bool last, CancellationToken cancellationToken)
    {
        if (!allowRle || !TryDetectRle(data.Span, out var value))
        {
            return false;
        }

        await EnsureAsyncCapacityAsync(3, cancellationToken).ConfigureAwait(false);
        Span<byte> header = stackalloc byte[3];
        WriteBlockHeaderTo(header, blockType: 1, blockSize: data.Length, last: last);
        await WriteCoalescedAsync(header).ConfigureAwait(false);

        await EnsureAsyncCapacityAsync(1, cancellationToken).ConfigureAwait(false);
        await WriteCoalescedAsync(value).ConfigureAwait(false);

        if (useChecksum)
        {
            hash.UpdateRepeat(value, data.Length);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryWriteCompressedBlockAsync(ReadOnlyMemory<byte> data, bool last, CancellationToken cancellationToken)
    {
        if (!CanAttemptCompression(data.Span))
        {
            return false;
        }

        var required = 3 + data.Length;
        if (!await TryPrepareOutputSpanAsync(required, cancellationToken).ConfigureAwait(false))
        {
            RegisterCompressionFailure();
            return false;
        }

        var buffer = workspace.OutputSpan;
        var target = buffer.Slice(outCount, required);
        var prefix = GetPrefix();
        var span = data.Span;
        var written = CompressBlock(span, prefix, target, settings.CompressionLevel, out var usedLocal, last);

        if (written > 0 && usedLocal == span.Length)
        {
            outCount += written;
            if (useChecksum)
            {
                hash.Update(span);
            }
            UpdateHistory(span);
            compressFailStreak = 0;
            return true;
        }

        RegisterCompressionFailure();
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask WriteRawBlockAsync(ReadOnlyMemory<byte> data, bool last, CancellationToken cancellationToken)
    {
        await EnsureAsyncCapacityAsync(3, cancellationToken).ConfigureAwait(false);
        Span<byte> header = stackalloc byte[3];
        WriteBlockHeaderTo(header, blockType: 0, blockSize: data.Length, last: last);
        await WriteCoalescedAsync(header).ConfigureAwait(false);
        await WriteCoalescedAsync(data, cancellationToken).ConfigureAwait(false);
        if (useChecksum)
        {
            hash.Update(data.Span);
        }
        UpdateHistory(data.Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanAttemptCompression(ReadOnlySpan<byte> data)
    {
        if (!settings.IsCompressedBlocksEnabled || settings.CompressionLevel <= 0)
        {
            return false;
        }

        if (compressSkipLeft > 0)
        {
            compressSkipLeft--;
            return false;
        }

        return IsLikelyCompressible(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPrepareOutputSpan(int requiredBytes)
    {
        var buffer = workspace.OutputSpan;
        if (requiredBytes > buffer.Length)
        {
            return false;
        }

        if (outCount > buffer.Length - requiredBytes)
        {
            FlushOutput();
            buffer = workspace.OutputSpan;
            if (requiredBytes > buffer.Length)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryPrepareOutputSpanAsync(int requiredBytes, CancellationToken token)
    {
        var buffer = workspace.OutputSpan;
        if (requiredBytes > buffer.Length)
        {
            return false;
        }

        if (outCount > buffer.Length - requiredBytes)
        {
            await FlushOutputAsync(token).ConfigureAwait(false);
            buffer = workspace.OutputSpan;
            if (requiredBytes > buffer.Length)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterCompressionFailure()
    {
        dictTried = true;
        compressFailStreak++;
        compressSkipLeft = Math.Min(8, compressFailStreak * 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask WriteCoalescedAsync(ReadOnlyMemory<byte> data, CancellationToken token)
    {
        if (data.IsEmpty) return;

        var buffer = workspace.OutputSpan;
        System.Diagnostics.Debug.Assert((uint)outCount <= (uint)buffer.Length);
        if (outCount > 0 && outCount + data.Length > buffer.Length)
        {
            await FlushOutputAsync(token).ConfigureAwait(false);
            buffer = workspace.OutputSpan;
            System.Diagnostics.Debug.Assert((uint)outCount <= (uint)buffer.Length);
        }

        if (outCount == 0 && data.Length > buffer.Length)
        {
            await baseStream.WriteAsync(data, token).ConfigureAwait(false);
            return;
        }

        data.Span.CopyTo(buffer.Slice(outCount, data.Length));
        outCount += data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteCoalescedAsync(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return ValueTask.CompletedTask;

        var buffer = workspace.OutputSpan;
        System.Diagnostics.Debug.Assert((uint)outCount <= (uint)buffer.Length);
        if (outCount > 0 && outCount + data.Length > buffer.Length)
        {
            throw new InvalidOperationException("Insufficient space for coalesced write. Call EnsureAsyncCapacityAsync before writing spans.");
        }

        if (outCount == 0 && data.Length > buffer.Length)
        {
            baseStream.Write(data);
            return ValueTask.CompletedTask;
        }

        data.CopyTo(buffer.Slice(outCount, data.Length));
        outCount += data.Length;
        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteCoalescedAsync(byte value) => WriteCoalescedAsync(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask EnsureAsyncCapacityAsync(int requiredBytes, CancellationToken token = default)
    {
        if (requiredBytes <= 0) return ValueTask.CompletedTask;

        var buffer = workspace.OutputSpan;
        System.Diagnostics.Debug.Assert((uint)outCount <= (uint)buffer.Length);
        if (outCount > 0 && outCount + requiredBytes > buffer.Length)
        {
            return FlushOutputAsync(token);
        }

        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask FlushOutputAsync(CancellationToken token)
    {
        if (outCount == 0) return;
        await baseStream.WriteAsync(workspace.OutputMemory[..outCount], token).ConfigureAwait(false);
        outCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushTail(bool last)
    {
        System.Diagnostics.Debug.Assert((uint)tailLength <= (uint)tailCapacity);
        if (tailLength == 0) return;
        WriteOneBlock(TailBuffer[..tailLength], last);
        tailLength = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask FlushTailAsync(bool last, CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.Assert((uint)tailLength <= (uint)tailCapacity);
        if (tailLength == 0) return;
        var slice = TailBufferMemory[..tailLength];
        await WriteOneBlockAsync(slice, last, cancellationToken).ConfigureAwait(false);
        tailLength = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBlockHeader(int blockType, int blockSize, bool last)
    {
        if ((uint)blockSize > (uint)settings.BlockCap) throw new InvalidDataException("Block_Size превышает Block_Maximum_Size");

        var header = (uint)((blockSize << 3) | (blockType << 1) | (last ? 1 : 0));
        Span<byte> h = [(byte)(header & 0xFF), (byte)((header >> 8) & 0xFF), (byte)((header >> 16) & 0xFF)];
        baseStream.Write(h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // Компрессия пишет прямо в рабочий выходной буфер — отдельный cmpScratch не нужен
    private static void EnsureCmpScratch(int _)
    {
        // Intentionally left empty: compression reuses the shared output buffer, so no scratch space is required.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetPrefix()
    {
        if (!dictTried && !settings.DictionaryContent.IsEmpty)
            return settings.DictionaryContent.Span;
        if (settings.UseInterBlockHistory && histLen > 0)
        {
            System.Diagnostics.Debug.Assert(histLen <= historyCapacity);
            return HistoryBuffer[..histLen];
        }
        return [];
    }

    // Дешёвая эвристика «стоит ли пробовать сжатие»: ищем повторы 4-байтовых паттернов в начале блока
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLikelyCompressible(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64) return false;
        var scan = data.Length > 2048 ? data[..2048] : data;
        ulong bloom1 = 0, bloom2 = 0;
        var dup = 0;
        for (var i = 0; i + 4 <= scan.Length; i += 8)
        {
            var v = Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in scan[i]));
            // два простых 32‑битных хеша (избегаем overflow под checked)
            var x = unchecked(v * 2654435761u);              // Knuth
            var y = unchecked((v ^ (v >> 16)) * 2246822519u); // xxh32‑like
            var b1 = (int)((x >> 26) & 63);
            var b2 = (int)((y >> 26) & 63);
            var m1 = 1ul << b1; var m2 = 1ul << b2;
            var hit = ((bloom1 & m1) != 0) || ((bloom2 & m2) != 0);
            if (hit && ++dup >= 2) return true;
            bloom1 |= m1; bloom2 |= m2;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureHistory(int minimum)
    {
        if (!settings.UseInterBlockHistory || !settings.IsCompressedBlocksEnabled) return;
        if (historyCapacity <= 0)
        {
            histLen = 0;
            return;
        }
        if (minimum <= 0) minimum = Math.Min(historyCapacity, ZstdStream.MaxRawBlockSize);
        if (minimum > historyCapacity) throw new InvalidOperationException("History workspace too small");
        _ = workspace.GetHistorySpan(historyCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateHistory(ReadOnlySpan<byte> data)
    {
        // История нужна только для сжатых блоков. При RAW/RLE её можно не вести — это снижает GC‑давление.
        if (!settings.UseInterBlockHistory || !settings.IsCompressedBlocksEnabled || data.IsEmpty) return;

        var cap = historyCapacity;
        if (cap <= 0) { histLen = 0; return; }

        if (histLen > cap) histLen = cap;

        var history = HistoryBuffer;

        if (data.Length >= cap)
        {
            var tail = data.Slice(data.Length - cap, cap);
            tail.CopyTo(history[..cap]);
            histLen = cap;
            return;
        }

        var newLen = histLen + data.Length;
        if (newLen <= cap)
        {
            data.CopyTo(history.Slice(histLen, data.Length));
            histLen = newLen;
            return;
        }

        var overflow = newLen - cap;
        history[overflow..histLen].CopyTo(history);
        histLen -= overflow;
        data.CopyTo(history.Slice(histLen, data.Length));
        histLen += data.Length;
    }

    /// <summary>
    /// Запись несжатых данных. Энкодер сам решит, писать RAW либо RLE-блок.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> src)
    {
        EnsureHeader();

        if (src.Length == 0) return;

        var buffer = TailBuffer;
        var blockCap = tailCapacity;

        while (!src.IsEmpty)
        {
            System.Diagnostics.Debug.Assert((uint)tailLength <= (uint)blockCap);
            if (tailLength == blockCap)
            {
                FlushTail(last: false);
            }

            var available = blockCap - tailLength;
            var toCopy = Math.Min(available, src.Length);
            src[..toCopy].CopyTo(buffer.Slice(tailLength, toCopy));
            tailLength += toCopy;
            src = src[toCopy..];

            if (tailLength == blockCap)
            {
                FlushTail(last: false);
            }
        }
    }

    /// <summary>
    /// Запись несжатых данных. Энкодер сам решит, писать RAW либо RLE-блок.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> src, CancellationToken cancellationToken = default)
    {
        await EnsureHeaderAsync().ConfigureAwait(false);

        if (src.Length == 0) return;

        var blockCap = tailCapacity;
        var mem = src;

        while (!mem.IsEmpty)
        {
            System.Diagnostics.Debug.Assert((uint)tailLength <= (uint)blockCap);
            if (tailLength == blockCap)
            {
                await FlushTailAsync(last: false, cancellationToken).ConfigureAwait(false);
            }

            var available = blockCap - tailLength;
            var toCopy = Math.Min(available, mem.Length);
            mem[..toCopy].Span.CopyTo(TailBufferMemory.Span[tailLength..(tailLength + toCopy)]);
            tailLength += toCopy;
            mem = mem[toCopy..];

            if (tailLength == blockCap)
            {
                await FlushTailAsync(last: false, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        // Гарантируем наличие FHD даже при пустом вводе.
        EnsureHeader();

        // Если данных не было вообще — мы обязаны выдать хотя бы один блок (RFC: «Each frame must have at least 1 block»).
        if (tailLength == 0)
        {
            // Пишем пустой RAW‑блок как последний.
            Span<byte> hb = stackalloc byte[3];
            WriteBlockHeaderTo(hb, blockType: 0 /*RAW*/, blockSize: 0, last: true);
            WriteCoalesced(hb);
        }
        else
        {
            FlushTail(last: true);
        }

        if (useChecksum)
        {
            var h = hash.Digest();
            Span<byte> c = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(c, (uint)h); // LE
            WriteCoalesced(c);
        }

        // Сбросить коалесцированный буфер в поток
        FlushOutput();

        ReleaseWorkspace();
    }

    private void ReleaseWorkspace()
    {
        if (workspaceReturned) return;
        workspace.Reset();
        ZstdEncoderWorkspacePool.Return(workspace);
        workspaceReturned = true;
        tailLength = 0;
        histLen = 0;
        outCount = 0;
        compressFailStreak = 0;
        compressSkipLeft = 0;
        dictTried = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBlockHeaderTo(Span<byte> dst3, int blockType, int blockSize, bool last)
    {
        if ((uint)blockSize > (uint)settings.BlockCap) throw new InvalidDataException("Block_Size превышает Block_Maximum_Size");
        var header = (uint)((blockSize << 3) | (blockType << 1) | (last ? 1 : 0));
        dst3[0] = (byte)(header & 0xFF);
        dst3[1] = (byte)((header >> 8) & 0xFF);
        dst3[2] = (byte)((header >> 16) & 0xFF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCoalesced(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;

        var buffer = workspace.OutputSpan;
        if (outCount > 0 && outCount + data.Length > buffer.Length)
        {
            FlushOutput();
            buffer = workspace.OutputSpan;
        }

        if (outCount == 0 && data.Length > buffer.Length)
        {
            baseStream.Write(data);
            return;
        }

        data.CopyTo(buffer.Slice(outCount, data.Length));
        outCount += data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCoalesced(byte value) => WriteCoalesced(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushOutput()
    {
        if (outCount == 0) return;
        System.Diagnostics.Debug.Assert((uint)outCount <= (uint)workspace.OutputSpan.Length);
        baseStream.Write(workspace.OutputSpan[..outCount]);
        outCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDetectRle(ReadOnlySpan<byte> src, out byte value)
    {
        if (src.IsEmpty)
        {
            value = 0;
            return true;
        }

        value = src[0];
        if (Vector.IsHardwareAccelerated && src.Length >= Vector<byte>.Count)
        {
            return TryDetectRleVector(src, value);
        }

        return TryDetectRleScalar(src[1..], value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDetectRleVector(ReadOnlySpan<byte> src, byte value)
    {
        var vecFill = new Vector<byte>(value);
        var step = Vector<byte>.Count;
        var i = 0;
        while (i <= src.Length - step)
        {
            var v = new Vector<byte>(src.Slice(i, step));
            if (!Vector.EqualsAll(v, vecFill))
            {
                return false;
            }
            i += step;
        }

        if (i >= src.Length)
        {
            return true;
        }

        return TryDetectRleScalar(src[i..], value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDetectRleScalar(ReadOnlySpan<byte> src, byte value)
    {
        for (var i = 0; i < src.Length; i++)
        {
            if (src[i] != value)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte EncodeWindowDescriptor(int desired)
    {
        // Клэмп по спецификации
        if (desired < 1024) desired = 1024;
        // Верхнюю границу оставляем большой (3.75 TB), но нам достаточно 8 MiB по умолчанию.

        // Перебираем экспоненту, подбирая минимальный WD >= desired
        for (var exp = 0; exp <= 31; exp++)
        {
            var windowLog = 10 + exp;
            var baseSize = 1 << windowLog;
            var step = baseSize >> 3; // /8

            if (desired <= baseSize) return (byte)(exp << 3);

            var mantissa = (desired - baseSize + step - 1) / step;
            if (mantissa <= 7) return (byte)((exp << 3) | mantissa);
        }

        // fallback (не должен сработать в реальных пределах)
        return 0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ComputeDidFieldSize(uint did)
    {
        if (did <= 0xFF) return 1;
        if (did <= 0xFFFF) return 2;
        return 3; // 4 байта
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PickFcsSize(ulong size, bool singleSegment)
    {
        if (singleSegment)
        {
            if (size <= 0xFF) return 1;
            if (size <= (256UL + 0xFFFF)) return 2; // особое кодирование (size-256) в ushort
            if (size <= 0xFFFFFFFFUL) return 4;
            return 8;
        }
        if (size < 256) return 4; // формат не допускает кодирование <256 в 2 байта (size-256)
        if (size <= (256UL + 0xFFFF)) return 2;
        if (size <= 0xFFFFFFFFUL) return 4;
        return 8;
    }

    /// <summary>
    /// Заголовок блока (3 байта, LE): LastBit | BlockType(2) | Size(21). BlockType=2 (Compressed).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBlockHeaderCompressed(Span<byte> dst, bool isLast, int blockSize)
    {
        var header = (uint)((isLast ? 1 : 0) | (2 << 1) | (blockSize << 3));
        // little-endian 3-байта:
        dst[0] = (byte)(header & 0xFF);
        dst[1] = (byte)((header >> 8) & 0xFF);
        dst[2] = (byte)((header >> 16) & 0xFF);
    }

    /// <summary>
    /// Записать секцию литералов (RAW/RLE) в формате RFC 8878.
    /// Возвращает полный размер секции (заголовок + тело).
    /// Поддерживаются три «семейства» размеров для RAW/RLE:
    ///  - короткий:   ≤  31  (1 байт заголовка)
    ///  - средний:   ≤ 4095  (2 байта заголовка)
    ///  - длинный:   ≤ 1&lt;&lt;20-1 (20 бит, 3 байта заголовка)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteLiteralsSection(ReadOnlySpan<byte> lits, Span<byte> dst)
    {
        if (lits.IsEmpty)
        {
            dst[0] = 0;
            return 1;
        }

        return IsRleLiteralBlock(lits)
            ? WriteRleLiterals(lits, dst)
            : WriteRawLiterals(lits, dst);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRleLiteralBlock(ReadOnlySpan<byte> literals)
    {
        var first = literals[0];
        for (var i = 1; i < literals.Length; i++)
        {
            if (literals[i] != first)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteRleLiterals(ReadOnlySpan<byte> literals, Span<byte> destination)
    {
        const int TypeRle = 1;
        var size = literals.Length;
        var value = literals[0];

        if (size <= 31)
        {
            destination[0] = (byte)(((size & 0x1F) << 3) | TypeRle);
            destination[1] = value;
            return 2;
        }

        if (size <= 4095)
        {
            destination[0] = (byte)(((size & 0x0F) << 4) | (1 << 2) | TypeRle);
            destination[1] = (byte)((size >> 4) & 0xFF);
            destination[2] = value;
            return 3;
        }

        destination[0] = (byte)(((size & 0x0F) << 4) | (3 << 2) | TypeRle);
        destination[1] = (byte)((size >> 4) & 0xFF);
        destination[2] = (byte)((size >> 12) & 0xFF);
        destination[3] = value;
        return 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteRawLiterals(ReadOnlySpan<byte> literals, Span<byte> destination)
    {
        const int TypeRaw = 0;
        var size = literals.Length;

        if (size <= 31)
        {
            destination[0] = (byte)(((size & 0x1F) << 3) | TypeRaw);
            literals.CopyTo(destination[1..]);
            return 1 + size;
        }

        if (size <= 4095)
        {
            destination[0] = (byte)(((size & 0x0F) << 4) | (1 << 2) | TypeRaw);
            destination[1] = (byte)((size >> 4) & 0xFF);
            literals.CopyTo(destination[2..]);
            return 2 + size;
        }

        destination[0] = (byte)(((size & 0x0F) << 4) | (3 << 2) | TypeRaw);
        destination[1] = (byte)((size >> 4) & 0xFF);
        destination[2] = (byte)((size >> 12) & 0xFF);
        literals.CopyTo(destination[3..]);
        return 3 + size;
    }

    internal static ZstdMatchParams GetParamsForLevel(int level)
    {
        // Базовая (стабильная) сетка (без переключателей среды)
        if (level <= 1) return new ZstdMatchParams(windowLog: 19, hashLog: 17, searchDepth: 2, targetLength: 24);
        if (level <= 5) return new ZstdMatchParams(windowLog: 19, hashLog: 17, searchDepth: 4, targetLength: 32);
        if (level <= 9) return new ZstdMatchParams(windowLog: 23, hashLog: 20, searchDepth: 6, targetLength: 48);
        if (level <= 15) return new ZstdMatchParams(windowLog: 24, hashLog: 21, searchDepth: 12, targetLength: 64);
        if (level <= 19) return new ZstdMatchParams(windowLog: 25, hashLog: 22, searchDepth: 20, targetLength: 96);
        return new ZstdMatchParams(windowLog: 26, hashLog: 23, searchDepth: 32, targetLength: 128);
    }

    /// <summary>
    /// Сжать один блок src в dst как Compressed Block.
    /// Возвращает размер блока (включая 3-байтовый заголовок), или 0 если выгоднее RAW/RLE (в этом случае вызывающий пусть запишет RAW/RLE).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CompressBlock(ReadOnlySpan<byte> src, ReadOnlySpan<byte> prefix, Span<byte> dst, int compressionLevel, out int decompressedSizeUsed, bool isLast)
    {
        decompressedSizeUsed = 0;
        if (src.Length == 0)
        {
            return 0;
        }

        var parameters = GetParamsForLevel(compressionLevel);
        if (!TryPrepareBlockData(src, prefix, in parameters, out var sequences, out var literals, out var consumed))
        {
            return 0;
        }

        if (!settings.IsCompressedBlocksEnabled)
        {
            return 0;
        }

        var body = dst[3..];
        if (!TryEncodeLiterals(literals, body, out var literalBytes))
        {
            return 0;
        }

        if (!TryEncodeSequenceSection(sequences, body, literalBytes, out var sequenceBytes))
        {
            return 0;
        }

        var blockSize = literalBytes + sequenceBytes;
        if (blockSize >= consumed)
        {
            return 0;
        }

        WriteBlockHeaderCompressed(dst, isLast, blockSize);
        decompressedSizeUsed = consumed;
        return 3 + blockSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPrepareBlockData(
        ReadOnlySpan<byte> src,
        ReadOnlySpan<byte> prefix,
        in ZstdMatchParams parameters,
        out ReadOnlySpan<ZstdSeq> sequences,
        out ReadOnlySpan<byte> literals,
        out int consumed)
    {
        var blockMax = Math.Min(src.Length, 128 * 1024);
        var block = src[..blockMax];

        var sequenceCapacity = (blockMax / 4) + 16;
        var sequenceSpan = workspace.SequenceSpan;
        if (sequenceCapacity > sequenceSpan.Length) throw new InvalidOperationException("Sequence workspace too small");
        var sequenceView = sequenceSpan[..sequenceCapacity];

        var literalSpan = LiteralBuffer;
        if (blockMax > literalSpan.Length) throw new InvalidOperationException("Literal workspace too small");
        var literalView = literalSpan[..blockMax];

        var hashSize = 1 << parameters.HashLog;
        var hashSpan = workspace.GetHashSpan(hashSize);

        var (sequenceCount, literalSize, used) = ZstdMatcher.BuildSequences(block, prefix, sequenceView, literalView, parameters, hashSpan);
        if (!IsSequenceExtractionValid(sequenceView, sequenceCount, literalSize, used))
        {
            sequences = default;
            literals = default;
            consumed = 0;
            return false;
        }

        sequences = sequenceView[..sequenceCount];
        literals = literalView[..literalSize];
        consumed = used;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSequenceExtractionValid(ReadOnlySpan<ZstdSeq> sequences, int sequenceCount, int literalSize, int consumed)
    {
        if (sequenceCount <= 1)
        {
            return false;
        }

        var sumLl = 0;
        var sumMl = 0;
        for (var i = 0; i < sequenceCount; i++)
        {
            var sequence = sequences[i];
            sumLl += sequence.LL;
            sumMl += sequence.ML;
        }

        return sumLl == literalSize && sumLl + sumMl == consumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryEncodeLiterals(ReadOnlySpan<byte> literals, Span<byte> body, out int written)
    {
        if (hasDictHuff)
        {
            var huffmanSize = TryWriteHuffmanLiterals(literals, body);
            if (huffmanSize > 0)
            {
                written = huffmanSize;
                return true;
            }
        }

        written = WriteLiteralsSection(literals, body);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryEncodeSequenceSection(ReadOnlySpan<ZstdSeq> sequences, Span<byte> body, int literalsSize, out int written)
    {
        var destination = body[literalsSize..];
        var available = destination.Length;

        if (settings.UseDictTablesForSequences && TryGetDictNorms(out var llNorm, out var llLog, out var mlNorm, out var mlLog, out var ofNorm, out var ofLog))
        {
            var dictEstimate = EstimateSequencesSize(sequences, llNorm, llLog, mlNorm, mlLog, ofNorm, ofLog);
            var predefEstimate = EstimateSequencesSize(sequences, [], 0, [], 0, [], 0);
            var useDict = ShouldPreferDictionary(dictEstimate, predefEstimate);
            var selectedEstimate = useDict ? dictEstimate : predefEstimate;

            if (!IsEstimateWithinRange(selectedEstimate, available))
            {
                written = 0;
                return false;
            }

            written = useDict
                ? ZstdSeqEncoder.WriteSequences(sequences, destination, llNorm, llLog, mlNorm, mlLog, ofNorm, ofLog)
                : ZstdSeqEncoder.WriteSequences(sequences, destination);

            return ValidateSequenceEncodingResult(sequences, destination, ref written);
        }

        var estimate = EstimateSequencesSize(sequences, [], 0, [], 0, [], 0);
        if (!IsEstimateWithinRange(estimate, available))
        {
            written = 0;
            return false;
        }

        written = ZstdSeqEncoder.WriteSequences(sequences, destination);
        return ValidateSequenceEncodingResult(sequences, destination, ref written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidateSequenceEncodingResult(ReadOnlySpan<ZstdSeq> sequences, Span<byte> destination, ref int written)
    {
        if (written < 0)
        {
            written = 0;
            return false;
        }
#if DEBUG
        if (!ValidateSequenceSection(sequences, destination[..written]))
        {
            written = 0;
            return false;
        }
#endif
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldPreferDictionary(int dictEstimate, int predefEstimate)
    {
        if (dictEstimate <= 0)
        {
            return false;
        }

        if (predefEstimate <= 0)
        {
            return true;
        }

        return dictEstimate <= predefEstimate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEstimateWithinRange(int estimate, int available) => estimate > 0 && estimate <= available;

    // Оценка размера секции последовательностей без фактической записи (в байтах, включая nbSeq+modes)
    private static int EstimateSequencesSize(
        ReadOnlySpan<ZstdSeq> seqs,
        ReadOnlySpan<short> llNorm, int llLog,
        ReadOnlySpan<short> mlNorm, int mlLog,
        ReadOnlySpan<short> ofNorm, int ofLog)
    {
        var headerLen = CalculateSequencesHeaderLength(seqs.Length);

        var usePredefLL = llNorm.IsEmpty;
        var usePredefML = mlNorm.IsEmpty;
        var usePredefOF = ofNorm.IsEmpty;

        if (usePredefLL) llLog = ZstdLengthsTables.LL_AccuracyLog;
        if (usePredefML) mlLog = ZstdLengthsTables.ML_AccuracyLog;
        if (usePredefOF) ofLog = ZstdLengthsTables.OffsetsAccuracyLog;

        Span<ushort> stLL = stackalloc ushort[1 << llLog];
        Span<FseSymbolTransform> ttLL = stackalloc FseSymbolTransform[36];
        Span<ushort> stML = stackalloc ushort[1 << mlLog];
        Span<FseSymbolTransform> ttML = stackalloc FseSymbolTransform[53];
        Span<ushort> stOF = stackalloc ushort[1 << ofLog];
        Span<FseSymbolTransform> ttOF = stackalloc FseSymbolTransform[ZstdLengthsTables.OffsetsMaxN + 1];

        var llContext = CreateSequenceFseState(
            usePredefLL ? ZstdLengthsTables.LL_DefaultNorm : llNorm,
            llLog,
            stLL,
            ttLL);

        var mlContext = CreateSequenceFseState(
            usePredefML ? ZstdLengthsTables.ML_DefaultNorm : mlNorm,
            mlLog,
            stML,
            ttML);

        var ofContext = CreateSequenceFseState(
            usePredefOF ? ZstdLengthsTables.OffsetsDefaultNorm : ofNorm,
            ofLog,
            stOF,
            ttOF);

        if (!TryEstimateBitCount(seqs, ref llContext, ref mlContext, ref ofContext, out var bits))
        {
            return -1;
        }

        bits += (uint)llContext.Log + (uint)mlContext.Log + (uint)ofContext.Log;
        var bitstreamBytes = (int)((bits + 8) >> 3);
        return headerLen + bitstreamBytes;
    }

    private static int CalculateSequencesHeaderLength(int nbSeq)
    {
        if (nbSeq == 0) return 2;
        if (nbSeq < 128) return 2;
        if (nbSeq < 0x7F00) return 3;
        return 4;
    }

    private static SequenceFseState CreateSequenceFseState(
        ReadOnlySpan<short> norm,
        int accuracyLog,
        Span<ushort> stateTable,
        Span<FseSymbolTransform> transforms)
    {
        var compressor = FseCompressor.Build(norm, accuracyLog, stateTable, transforms);
        var initialState = unchecked(compressor.InitState(0) + (uint)(1 << accuracyLog));
        return new SequenceFseState(stateTable, transforms, accuracyLog, initialState);
    }

    private static bool TryEstimateBitCount(
        ReadOnlySpan<ZstdSeq> seqs,
        ref SequenceFseState llContext,
        ref SequenceFseState mlContext,
        ref SequenceFseState ofContext,
        out ulong bits)
    {
        bits = 0;
        for (var i = seqs.Length - 1; i >= 0; i--)
        {
            var sequence = seqs[i];
            ZstdSeqEncoder.GetLLCodeBits(sequence.LL, out var llCode, out _, out var llBits);
            ZstdSeqEncoder.GetMLCodeBits(sequence.ML, out var mlCode, out _, out var mlBits);
            ZstdSeqEncoder.GetOFCodeBits(in sequence, out var ofCode, out _, out var ofBits);

            if (!TryAdvanceState(ref ofContext, ofCode, ref bits)) return false;
            if (!TryAdvanceState(ref mlContext, mlCode, ref bits)) return false;
            if (!TryAdvanceState(ref llContext, llCode, ref bits)) return false;

            bits += (uint)llBits + (uint)mlBits + (uint)ofBits;
        }

        return true;
    }

    private static bool TryAdvanceState(ref SequenceFseState context, int code, ref ulong bits)
    {
        if ((uint)code >= (uint)context.Transforms.Length) return false;

        var transform = context.Transforms[code];
        var sum = unchecked(context.State + transform.DeltaNbBits);
        var nb = sum >> 16;
        bits += nb;

        var interval = context.State >> (int)nb;
        var cappedInterval = interval > int.MaxValue ? int.MaxValue : (int)interval;
        var index = (long)transform.DeltaFindState + cappedInterval;
        if (index < 0 || index >= context.StateTable.Length) return false;
        context.State = context.StateTable[(int)index];
        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceFseState
    {
        public SequenceFseState(Span<ushort> stateTable, Span<FseSymbolTransform> transforms, int log, uint initialState)
        {
            StateTable = stateTable;
            Transforms = transforms;
            Log = log;
            State = initialState;
        }

        public Span<ushort> StateTable { get; }
        public Span<FseSymbolTransform> Transforms { get; }
        public int Log { get; }
        public uint State { get; set; }
    }
    // ------- Парсинг норм FSE из словаря (форматированный словарь) -------
    private int dictLlLength, dictMlLength, dictOfLength;
    private int dictLlLog, dictMlLog, dictOfLog;
    private bool hasDictNorms;
    private int dictHuffSymbolCount;
    private int dictHuffMaxBits;
    private bool hasDictHuff;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetDictNorms(out ReadOnlySpan<short> llNorm, out int llLog, out ReadOnlySpan<short> mlNorm, out int mlLog, out ReadOnlySpan<short> ofNorm, out int ofLog)
    {
        if (hasDictNorms && dictLlLength > 0 && dictMlLength > 0 && dictOfLength > 0)
        {
            llNorm = workspace.DictionaryLiteralNormSpan[..dictLlLength]; llLog = dictLlLog;
            mlNorm = workspace.DictionaryMatchNormSpan[..dictMlLength]; mlLog = dictMlLog;
            ofNorm = workspace.DictionaryOffsetsNormSpan[..dictOfLength]; ofLog = dictOfLog;
            return true;
        }
        llNorm = default; llLog = 0; mlNorm = default; mlLog = 0; ofNorm = default; ofLog = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorLog2(int v) => 31 - BitOperations.LeadingZeroCount((uint)v);

    private static int ParseFseNorm(ReadOnlySpan<byte> src, int maxSymbol, Span<short> destination, out int tableLog, out int length)
    {
        if (destination.Length < maxSymbol + 1)
        {
            throw new ArgumentException("Destination span too small for FSE norm", nameof(destination));
        }

        var reader = new ForwardBitReader(src);
        tableLog = (int)reader.ReadBits(4) + 5;
        var remaining = 1 << tableLog;
        var symbol = 0;

        while (remaining > 0 && symbol <= maxSymbol)
        {
            var value = ReadNormalizedCode(ref reader, remaining);
            if (TryHandleNormalizedSpecialValue(ref reader, value, destination, ref symbol, maxSymbol, ref remaining))
            {
                continue;
            }

            var probability = (int)value - 1;
            destination[symbol++] = (short)probability;
            remaining -= probability;
        }

        length = symbol;
        return reader.BytesConsumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadNormalizedCode(ref ForwardBitReader reader, int remaining)
    {
        var range = remaining + 1;
        var bits = FloorLog2(range);
        var threshold = (1 << (bits + 1)) - range;
        var value = reader.ReadBits(bits);
        if (value >= (uint)threshold)
        {
            value = ((value << 1) | reader.ReadBits(1)) - (uint)threshold;
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryHandleNormalizedSpecialValue(
        ref ForwardBitReader reader,
        uint value,
        Span<short> destination,
        ref int symbol,
        int maxSymbol,
        ref int remaining)
    {
        switch (value)
        {
            case 0:
                destination[symbol++] = -1;
                remaining -= 1;
                return true;
            case 1:
                destination[symbol++] = 0;
                var zeroRun = ReadZeroRun(ref reader);
                AppendZeroRun(destination, ref symbol, maxSymbol, zeroRun);
                return true;
            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadZeroRun(ref ForwardBitReader reader)
    {
        var run = 0;
        while (true)
        {
            var next = (int)reader.ReadBits(2);
            run += next;
            if (next != 3)
            {
                break;
            }
        }

        return run;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendZeroRun(Span<short> destination, ref int symbol, int maxSymbol, int count)
    {
        while (count-- > 0 && symbol <= maxSymbol)
        {
            destination[symbol++] = 0;
        }
    }

    private void TryParseDictNorms()
    {
        if (hasDictNorms || settings.DictionaryContent.IsEmpty)
        {
            return;
        }

        ResetDictionaryNormState();

        var src = settings.DictionaryContent.Span;
        if (!TryInitializeDictionaryNormParsing(src, out var position))
        {
            return;
        }

        position = ParseDictionaryHuffmanForEncoder(src, position);
        ParseDictionaryNormTables(src, ref position);
        hasDictNorms = dictLlLength > 0 && dictMlLength > 0 && dictOfLength > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetDictionaryNormState()
    {
        dictLlLength = dictMlLength = dictOfLength = 0;
        dictHuffSymbolCount = 0;
        dictHuffMaxBits = 0;
        hasDictHuff = false;
        cachedDictHuffReady = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryInitializeDictionaryNormParsing(ReadOnlySpan<byte> src, out int position)
    {
        if (src.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(src) != 0xEC30A437)
        {
            position = 0;
            return false;
        }

        position = 8;
        return position < src.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionaryHuffmanForEncoder(ReadOnlySpan<byte> src, int position)
    {
        if (position >= src.Length)
        {
            return position;
        }

        var header = src[position];
        position++;

        var destination = workspace.DictionaryHuffNbSpan;
        if (header < 128)
        {
            var weightsSize = header;
            if (position + weightsSize > src.Length)
            {
                return src.Length;
            }

            if (TryParseDictHuffDirect(src.Slice(position, weightsSize), destination, out var symbolCount, out var maxBits))
            {
                dictHuffSymbolCount = symbolCount;
                dictHuffMaxBits = maxBits;
                hasDictHuff = symbolCount > 0;
            }

            return position + weightsSize;
        }

        var bytesNeeded = (header - 127 + 1) / 2;
        if (position + bytesNeeded > src.Length)
        {
            return src.Length;
        }

        if (TryParseDictHuffFse(src.Slice(position, bytesNeeded), destination, out var count, out var maxBitsFse))
        {
            dictHuffSymbolCount = count;
            dictHuffMaxBits = maxBitsFse;
            hasDictHuff = count > 0;
        }

        return position + bytesNeeded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseDictionaryNormTables(ReadOnlySpan<byte> src, ref int position)
    {
        var ofSpan = workspace.DictionaryOffsetsNormSpan;
        var mlSpan = workspace.DictionaryMatchNormSpan;
        var llSpan = workspace.DictionaryLiteralNormSpan;

        var offsetsBytes = ParseFseNorm(src[position..], ZstdLengthsTables.OffsetsMaxN, ofSpan, out dictOfLog, out dictOfLength);
        position += offsetsBytes;

        var mlBytes = ParseFseNorm(src[position..], 52, mlSpan, out dictMlLog, out dictMlLength);
        position += mlBytes;

        _ = ParseFseNorm(src[position..], 35, llSpan, out dictLlLog, out dictLlLength);
    }

    private static bool TryParseDictHuffDirect(ReadOnlySpan<byte> weights4bit, Span<byte> destination, out int symbolCount, out int maxBits)
    {
        var numberOfWeights = (weights4bit.Length * 2) - 1;
        if (numberOfWeights <= 0 || destination.Length < numberOfWeights + 1)
        {
            symbolCount = 0;
            maxBits = 0;
            return false;
        }

        Span<byte> weights = stackalloc byte[256];
        for (var i = 0; i < numberOfWeights; i++)
        {
            var b = weights4bit[i >> 1];
            weights[i] = (byte)(((i & 1) == 0) ? (b >> 4) : (b & 0xF));
        }

        var sum = 0;
        for (var i = 0; i < numberOfWeights; i++)
        {
            var w = weights[i];
            if (w != 0) sum += 1 << (w - 1);
        }

        var pow2 = 1;
        while (pow2 <= sum) pow2 <<= 1;
        var rem = pow2 - sum;
        if (rem <= 0)
        {
            symbolCount = 0;
            maxBits = 0;
            return false;
        }

        var weightLast = BitOperations.Log2((uint)rem) + 1;
        weights[numberOfWeights] = (byte)weightLast;
        maxBits = BitOperations.Log2((uint)pow2);

        var count = numberOfWeights + 1;
        for (var i = 0; i < count; i++)
        {
            var w = weights[i];
            destination[i] = (byte)(w == 0 ? 0 : (maxBits + 1 - w));
        }

        symbolCount = count;
        return true;
    }

    private static bool TryParseDictHuffFse(ReadOnlySpan<byte> src, Span<byte> destination, out int symbolCount, out int maxBits)
    {
        Span<short> norm = stackalloc short[16];
        if (!TryParseWeightNormalization(src, norm, out var lastSymbol, out var tableLog, out var headerBytes))
        {
            symbolCount = 0;
            maxBits = 0;
            return false;
        }

        var body = src[headerBytes..];
        Span<byte> weights = stackalloc byte[256];
        if (!TryDecodeWeights(body, norm[..(lastSymbol + 1)], tableLog, weights, out var weightCount))
        {
            symbolCount = 0;
            maxBits = 0;
            return false;
        }

        return TryFinalizeWeights(weights, weightCount, destination, out symbolCount, out maxBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseWeightNormalization(
        ReadOnlySpan<byte> src,
        Span<short> norm,
        out int lastSymbol,
        out int tableLog,
        out int headerBytes)
    {
        var reader = new ForwardBitReader(src);
        tableLog = (int)reader.ReadBits(4) + 5;
        var remaining = 1 << tableLog;
        var symbol = 0;

        while (remaining > 0 && symbol <= 15)
        {
            var value = ReadNormalizedCode(ref reader, remaining);
            if (TryHandleNormalizedSpecialValue(ref reader, value, norm, ref symbol, 15, ref remaining))
            {
                continue;
            }

            var probability = (int)value - 1;
            norm[symbol++] = (short)probability;
            remaining -= probability;
        }

        lastSymbol = symbol - 1;
        if (lastSymbol < 0)
        {
            headerBytes = 0;
            return false;
        }

        headerBytes = reader.BytesConsumed;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDecodeWeights(
        ReadOnlySpan<byte> body,
        ReadOnlySpan<short> norm,
        int tableLog,
        Span<byte> weights,
        out int weightCount)
    {
        var tableSize = 1 << tableLog;
        if (tableSize > 1024)
        {
            weightCount = 0;
            return false;
        }

        Span<byte> symbols = stackalloc byte[tableSize];
        Span<byte> nb = stackalloc byte[tableSize];
        Span<ushort> baseArr = stackalloc ushort[tableSize];
        var decoder = FseDecoder.Build(norm, tableLog, symbols, nb, baseArr);

        var reader = new ForwardBitReader(body);
        var state1 = reader.ReadBits(tableLog);
        var state2 = reader.ReadBits(tableLog);

        weightCount = 0;
        while (true)
        {
            if (weightCount >= weights.Length)
            {
                return false;
            }

            weights[weightCount++] = decoder.PeekSymbol(state1);
            var bitsNeeded1 = decoder.PeekNbBits(state1);
            var additional1 = bitsNeeded1 != 0 ? reader.ReadBits(bitsNeeded1) : 0u;
            decoder.UpdateState(ref state1, additional1);

            if (weightCount >= 255 || reader.BytesConsumed >= body.Length)
            {
                break;
            }

            if (weightCount >= weights.Length)
            {
                return false;
            }

            weights[weightCount++] = decoder.PeekSymbol(state2);
            var bitsNeeded2 = decoder.PeekNbBits(state2);
            var additional2 = bitsNeeded2 != 0 ? reader.ReadBits(bitsNeeded2) : 0u;
            decoder.UpdateState(ref state2, additional2);

            if (reader.BytesConsumed >= body.Length)
            {
                break;
            }
        }

        return weightCount > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryFinalizeWeights(Span<byte> weights, int weightCount, Span<byte> destination, out int symbolCount, out int maxBits)
    {
        var sum = 0;
        for (var i = 0; i < weightCount; i++)
        {
            var weight = weights[i];
            if (weight != 0)
            {
                sum += 1 << (weight - 1);
            }
        }

        var pow2 = 1;
        while (pow2 <= sum)
        {
            pow2 <<= 1;
        }

        var remainder = pow2 - sum;
        if (remainder <= 0 || weightCount >= weights.Length || destination.Length < weightCount + 1)
        {
            symbolCount = 0;
            maxBits = 0;
            return false;
        }

        weights[weightCount++] = (byte)(BitOperations.Log2((uint)remainder) + 1);
        maxBits = BitOperations.Log2((uint)pow2);
        symbolCount = weightCount;

        for (var i = 0; i < symbolCount; i++)
        {
            var weight = weights[i];
            destination[i] = (byte)(weight == 0 ? 0 : (maxBits + 1 - weight));
        }

        return true;
    }

    // Построить и закэшировать канонические коды/длины для словарного Huffman (treeless single-stream)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCachedDictHuff()
    {
        if (!hasDictHuff || dictHuffSymbolCount <= 0 || dictHuffMaxBits <= 0)
        {
            cachedDictHuffReady = false; return;
        }
        var nb = workspace.DictionaryHuffNbSpan[..dictHuffSymbolCount];
        if (cachedDictHuffReady && cachedDictHuffAlphabetSize == nb.Length && cachedDictHuffMaxBits == dictHuffMaxBits)
            return;

        var maxBits = dictHuffMaxBits;
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var s = 0; s < nb.Length; s++) { var l = nb[s]; if (l != 0) blCount[l]++; }
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++) { code = (code + blCount[bits - 1]) << 1; nextCode[bits] = code; }
        var codesSpan = workspace.DictionaryHuffCodesSpan[..nb.Length];
        var lensSpan = workspace.DictionaryHuffLensSpan[..nb.Length];
        for (var s = 0; s < nb.Length; s++)
        {
            var l = nb[s];
            if (l == 0) { codesSpan[s] = 0; lensSpan[s] = 0; continue; }
            var c = nextCode[l]++;
            codesSpan[s] = ReverseBits(c, l);
            lensSpan[s] = l;
        }
        cachedDictHuffAlphabetSize = nb.Length;
        cachedDictHuffMaxBits = maxBits;
        cachedDictHuffReady = true;
    }

    // Try to write literals via dict Huffman single-stream treeless header (sf=0), else return 0
    private int TryWriteHuffmanLiterals(ReadOnlySpan<byte> lits, Span<byte> dst)
    {
        if (lits.IsEmpty)
            return 0;

        if (!TryPrepareDictHuffman(out var codes, out var lens, out var maxBits))
            return 0;

        if (lits.Length > 1023)
        {
            var fourSize = TryWriteHuffmanLiteralsFour(lits, dst, codes, lens, maxBits);
            return fourSize > 0 ? fourSize : 0;
        }

        var fourCandidateSize = TryEncodeFourStream(lits, codes, lens, maxBits, out var fourBuffer);
        var singleSize = TryEncodeSingleStream(lits, dst, codes, lens);

        if (singleSize <= 0)
        {
            return CopyFourCandidate(fourBuffer, fourCandidateSize, dst);
        }

        if (fourCandidateSize > 0 && fourCandidateSize < singleSize)
        {
            return CopyFourCandidate(fourBuffer, fourCandidateSize, dst);
        }

        return singleSize;
    }

    private bool TryPrepareDictHuffman(out ReadOnlySpan<uint> codes, out ReadOnlySpan<byte> lens, out int maxBits)
    {
        codes = default;
        lens = default;
        maxBits = 0;

        if (!hasDictHuff || dictHuffSymbolCount <= 0 || dictHuffMaxBits <= 0)
            return false;

        EnsureCachedDictHuff();
        if (!cachedDictHuffReady)
            return false;

        var codesSpan = workspace.DictionaryHuffCodesSpan[..cachedDictHuffAlphabetSize];
        var lensSpan = workspace.DictionaryHuffLensSpan[..cachedDictHuffAlphabetSize];

        codes = codesSpan;
        lens = lensSpan;
        maxBits = cachedDictHuffMaxBits;
        return true;
    }

    private static int TryEncodeSingleStream(ReadOnlySpan<byte> literals, Span<byte> destination, ReadOnlySpan<uint> codes, ReadOnlySpan<byte> lens)
    {
        if (destination.Length < 3) return 0;
        var writer = new LittleEndianBitWriter(destination[3..]);
        for (var i = literals.Length - 1; i >= 0; i--)
        {
            var symbol = literals[i];
            if (symbol >= lens.Length || lens[symbol] == 0) return 0;
            if (!writer.TryWriteBits(codes[symbol], lens[symbol])) return 0;
        }

        if (!writer.TryFinishWithOnePadding()) return 0;
        var payloadSize = writer.BytesWritten;
        if (payloadSize > 1023) return 0;

        WriteSingleStreamHeader(destination, literals.Length, payloadSize);
        return 3 + payloadSize;
    }

    private static void WriteSingleStreamHeader(Span<byte> destination, int regeneratedSize, int compressedSize)
    {
        destination[0] = (byte)(((regeneratedSize & 0xF) << 4) | 3);
        destination[1] = (byte)((((regeneratedSize >> 4) & 0x3F) << 2) | (compressedSize & 0x3));
        destination[2] = (byte)(compressedSize >> 2);
    }

    private int TryEncodeFourStream(ReadOnlySpan<byte> literals, ReadOnlySpan<uint> codes, ReadOnlySpan<byte> lens, int maxBits, out Span<byte> buffer)
    {
        buffer = default;
        if (literals.Length < 256) return 0;

        var estimate = EstimateFourSectionSize(literals.Length, maxBits);
        if (estimate <= 0) return 0;

        buffer = workspace.GetDictFourSpan(estimate);
        var size = TryWriteHuffmanLiteralsFour(literals, buffer, codes, lens, maxBits);
        if (size <= 0)
        {
            buffer = default;
            return 0;
        }

        return size;
    }

    private static int CopyFourCandidate(Span<byte> buffer, int size, Span<byte> destination)
    {
        if (size <= 0) return 0;
        buffer[..size].CopyTo(destination);
        return size;
    }

    private static int EstimateFourSectionSize(int literalCount, int maxBits)
    {
        if (literalCount <= 0) return 0;
        var q = literalCount / 4;
        var r = literalCount % 4;
        var l1 = q + (r > 0 ? 1 : 0);
        var l2 = q + (r > 1 ? 1 : 0);
        var l3 = q + (r > 2 ? 1 : 0);
        var l4 = q;
        static int WorstBytes(int count, int bits) => Math.Max(1, (((count * bits) + 7) / 8) + 8);
        var worst1 = WorstBytes(l1, maxBits);
        var worst2 = WorstBytes(l2, maxBits);
        var worst3 = WorstBytes(l3, maxBits);
        var worst4 = WorstBytes(l4, maxBits);
        // максимальный заголовок: SF=3 (5 байт) + jump table 6 байт
        return 5 + 6 + worst1 + worst2 + worst3 + worst4;
    }

    private static FourStreamLayout CreateFourStreamLayout(int literalCount)
    {
        var q = literalCount / 4;
        var r = literalCount % 4;
        return new FourStreamLayout(
            q + (r > 0 ? 1 : 0),
            q + (r > 1 ? 1 : 0),
            q + (r > 2 ? 1 : 0),
            q);
    }

    private int TryWriteHuffmanLiteralsFour(ReadOnlySpan<byte> lits, Span<byte> dst, ReadOnlySpan<uint> codes, ReadOnlySpan<byte> lens, int maxBits)
    {
        if (lits.IsEmpty) return 0;

        var layout = CreateFourStreamLayout(lits.Length);
        var buffers = new FourStreamBuffers(workspace, layout, maxBits);
        var writes = EncodeFourStreams(lits, layout, buffers, codes, lens);
        if (!writes.IsValid) return 0;

        var totalStreams = writes.TotalBytes;
        if (!TrySelectFourHeader(lits.Length, totalStreams, out var header))
            return 0;

        var required = header.HeaderLength + 6 + totalStreams;
        if (dst.Length < required) return 0;

        var headerLength = WriteFourHeader(dst, lits.Length, totalStreams, in header);
        WriteFourJumpTable(dst.Slice(headerLength, 6), in writes);
        var payload = dst.Slice(headerLength + 6, totalStreams);
        var written = WriteFourPayload(payload, buffers, in writes);
        return headerLength + 6 + written;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FourStreamLayout(int first, int second, int third, int fourth)
    {
        public int First { get; } = first;
        public int Second { get; } = second;
        public int Third { get; } = third;
        public int Fourth { get; } = fourth;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct FourStreamBuffers
    {
        public FourStreamBuffers(ZstdEncoderWorkspace workspace, FourStreamLayout layout, int maxBits)
        {
            var worst1 = EstimateWorst(layout.First, maxBits);
            var worst2 = EstimateWorst(layout.Second, maxBits);
            var worst3 = EstimateWorst(layout.Third, maxBits);
            var worst4 = EstimateWorst(layout.Fourth, maxBits);

            TotalWorst = worst1 + worst2 + worst3 + worst4;
            Scratch = workspace.GetDictStreamSpan(TotalWorst);

            Buffer1 = Scratch[..worst1];
            Buffer2 = Scratch[worst1..(worst1 + worst2)];
            Buffer3 = Scratch[(worst1 + worst2)..(worst1 + worst2 + worst3)];
            Buffer4 = Scratch[(worst1 + worst2 + worst3)..];
        }

        public Span<byte> Scratch { get; }
        public Span<byte> Buffer1 { get; }
        public Span<byte> Buffer2 { get; }
        public Span<byte> Buffer3 { get; }
        public Span<byte> Buffer4 { get; }
        public int TotalWorst { get; }

        private static int EstimateWorst(int count, int bits)
        {
            if (count <= 0) return 0;
            return Math.Max(1, (((count * bits) + 7) / 8) + 8);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FourStreamWriteResult(int w1, int w2, int w3, int w4)
    {
        public int W1 { get; } = w1;
        public int W2 { get; } = w2;
        public int W3 { get; } = w3;
        public int W4 { get; } = w4;

        public int TotalBytes => Math.Max(W1, 0) + Math.Max(W2, 0) + Math.Max(W3, 0) + Math.Max(W4, 0);
        public bool IsValid => W1 >= 0 && W2 >= 0 && W3 >= 0 && W4 >= 0;
    }

    private static FourStreamWriteResult EncodeFourStreams(
        ReadOnlySpan<byte> literals,
        FourStreamLayout layout,
        FourStreamBuffers buffers,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lens)
    {
        var offset = 0;
        var w1 = EncodeStream(literals[..layout.First], buffers.Buffer1, codes, lens);
        if (w1 < 0) return new FourStreamWriteResult(-1, -1, -1, -1);

        offset += layout.First;
        var w2 = EncodeStream(literals.Slice(offset, layout.Second), buffers.Buffer2, codes, lens);
        if (w2 < 0) return new FourStreamWriteResult(-1, -1, -1, -1);

        offset += layout.Second;
        var w3 = EncodeStream(literals.Slice(offset, layout.Third), buffers.Buffer3, codes, lens);
        if (w3 < 0) return new FourStreamWriteResult(-1, -1, -1, -1);

        offset += layout.Third;
        var w4 = EncodeStream(literals.Slice(offset, layout.Fourth), buffers.Buffer4, codes, lens);
        if (w4 < 0) return new FourStreamWriteResult(-1, -1, -1, -1);

        return new FourStreamWriteResult(w1, w2, w3, w4);
    }

    private static int EncodeStream(ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<uint> codes, ReadOnlySpan<byte> lens)
    {
        if (source.IsEmpty) return 0;
        var written = EncodeHuffStream(source, destination, codes, lens);
        return written > 0 ? written : -1;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FourHeaderInfo(int streamFlag, int regeneratedBits, int compressedBits)
    {
        public int StreamFlag { get; } = streamFlag;
        public int RegeneratedBits { get; } = regeneratedBits;
        public int CompressedBits { get; } = compressedBits;

        public int HeaderLength
        {
            get
            {
                var regenBytes = Math.Max(0, (RegeneratedBits - 4 + 7) / 8);
                var compBytes = (CompressedBits + 7) / 8;
                return 1 + regenBytes + compBytes;
            }
        }
    }

    private static bool TrySelectFourHeader(int regeneratedSize, int totalCompressed, out FourHeaderInfo header)
    {
        if (regeneratedSize <= 1023 && totalCompressed <= 1023)
        {
            header = new FourHeaderInfo(streamFlag: 1, regeneratedBits: 10, compressedBits: 10);
            return true;
        }

        if (regeneratedSize <= 16383 && totalCompressed <= 16383)
        {
            header = new FourHeaderInfo(streamFlag: 2, regeneratedBits: 14, compressedBits: 14);
            return true;
        }

        if (regeneratedSize <= 262143 && totalCompressed <= 262143)
        {
            header = new FourHeaderInfo(streamFlag: 3, regeneratedBits: 18, compressedBits: 18);
            return true;
        }

        header = default;
        return false;
    }

    private static int WriteFourHeader(Span<byte> destination, int regeneratedSize, int totalCompressed, in FourHeaderInfo header)
    {
        destination[0] = (byte)(((regeneratedSize & 0xF) << 4) | (header.StreamFlag << 2) | 3);
        var index = 1;
        var bitPos = 4;
        var remainingRegen = header.RegeneratedBits - 4;
        while (remainingRegen > 0)
        {
            destination[index++] = (byte)((regeneratedSize >> bitPos) & 0xFF);
            bitPos += 8;
            remainingRegen -= 8;
        }

        var written = 0;
        while (written < header.CompressedBits)
        {
            destination[index++] = (byte)((totalCompressed >> written) & 0xFF);
            written += 8;
        }
        return index;
    }

    private static void WriteFourJumpTable(Span<byte> destination, in FourStreamWriteResult writes)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)writes.W1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)writes.W2);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)writes.W3);
    }

    private static int WriteFourPayload(Span<byte> destination, FourStreamBuffers buffers, in FourStreamWriteResult writes)
    {
        var offset = 0;
        if (writes.W1 > 0) { buffers.Buffer1[..writes.W1].CopyTo(destination[offset..]); offset += writes.W1; }
        if (writes.W2 > 0) { buffers.Buffer2[..writes.W2].CopyTo(destination[offset..]); offset += writes.W2; }
        if (writes.W3 > 0) { buffers.Buffer3[..writes.W3].CopyTo(destination[offset..]); offset += writes.W3; }
        if (writes.W4 > 0) { buffers.Buffer4[..writes.W4].CopyTo(destination[offset..]); offset += writes.W4; }
        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeHuffStream(ReadOnlySpan<byte> src, Span<byte> dst, ReadOnlySpan<uint> codes, ReadOnlySpan<byte> lens)
    {
        var writer = new LittleEndianBitWriter(dst);
        for (var i = src.Length - 1; i >= 0; i--)
        {
            var s = src[i];
            if (s >= lens.Length || lens[s] == 0) return 0;
            if (!writer.TryWriteBits(codes[s], lens[s])) return 0;
        }
        if (!writer.TryFinishWithOnePadding()) return 0;
        return writer.BytesWritten;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReverseBits(int v, int n)
    {
        // Reverse n LSBs of v (n ≤ 11)
        var x = (uint)v;
        x = ((x & 0x5555u) << 1) | ((x >> 1) & 0x5555u);
        x = ((x & 0x3333u) << 2) | ((x >> 2) & 0x3333u);
        x = ((x & 0x0F0Fu) << 4) | ((x >> 4) & 0x0F0Fu);
        x = ((x & 0x00FFu) << 8) | ((x >> 8) & 0x00FFu);
        return x >> (16 - n);
    }

#if DEBUG
    private static bool ValidateSequenceSection(ReadOnlySpan<ZstdSeq> seqs, ReadOnlySpan<byte> section)
    {
        if (seqs.IsEmpty)
        {
            return section.Length >= 2 && section[0] == 0;
        }

        if (!TryReadSequenceHeader(section, seqs.Length, out var header))
            return false;

        if (!AreSequenceModesSupported(section[header.HeaderLength]))
            return false;

        var bitstream = section[(header.HeaderLength + 1)..];
        if (bitstream.IsEmpty) return false;

        return TryValidateSequences(seqs, header.NbSeq, bitstream);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct SequenceSectionHeader(int nbSeq, int headerLength)
    {
        public int NbSeq { get; } = nbSeq;
        public int HeaderLength { get; } = headerLength;
    }

    private static bool TryReadSequenceHeader(ReadOnlySpan<byte> section, int expectedCount, out SequenceSectionHeader header)
    {
        header = default;
        if (section.Length < 2) return false;

        ParseNbSeqHeader(section, out var nbSeq, out var headerLen);
        if (nbSeq != expectedCount) return false;
        if (section.Length < headerLen + 1) return false;

        header = new SequenceSectionHeader(nbSeq, headerLen);
        return true;
    }

    private static bool AreSequenceModesSupported(byte modes)
    {
        var llMode = (modes >> 6) & 0x3;
        var ofMode = (modes >> 4) & 0x3;
        var mlMode = (modes >> 2) & 0x3;
        return llMode == 0 && ofMode == 0 && mlMode == 0;
    }

    private static bool TryValidateSequences(ReadOnlySpan<ZstdSeq> seqs, int nbSeq, ReadOnlySpan<byte> bitstream)
    {
        var decLL = FseDecoder.FromTables(ZstdLengthsTables.LL_AccuracyLog,
            ZstdPredef.LLSym, ZstdPredef.LLNb, ZstdPredef.LLBase);
        var decOF = FseDecoder.FromTables(ZstdLengthsTables.OffsetsAccuracyLog,
            ZstdPredef.OFSym, ZstdPredef.OFNb, ZstdPredef.OFBase);
        var decML = FseDecoder.FromTables(ZstdLengthsTables.ML_AccuracyLog,
            ZstdPredef.MLSym, ZstdPredef.MLNb, ZstdPredef.MLBase);

        var reader = new LittleEndianReverseBitReader(bitstream);
        if (!reader.TrySkipPadding()) return false;

        if (!reader.TryReadBits(ZstdLengthsTables.LL_AccuracyLog, out var stateLL)) return false;
        if (!reader.TryReadBits(ZstdLengthsTables.OffsetsAccuracyLog, out var stateOF)) return false;
        if (!reader.TryReadBits(ZstdLengthsTables.ML_AccuracyLog, out var stateML)) return false;

        var context = new SequenceValidationContext(decLL, decOF, decML, stateLL, stateOF, stateML);
        for (var i = 0; i < nbSeq; i++)
        {
            if (!context.TryProcessSequence(seqs[i], ref reader)) return false;
        }

        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceValidationContext
    {
        private readonly FseDecoder decLL;
        private readonly FseDecoder decOF;
        private readonly FseDecoder decML;
        private uint stateLL;
        private uint stateOF;
        private uint stateML;
        private uint rep1;
        private uint rep2;
        private uint rep3;

        public SequenceValidationContext(FseDecoder decLL, FseDecoder decOF, FseDecoder decML, uint stateLL, uint stateOF, uint stateML)
        {
            this.decLL = decLL;
            this.decOF = decOF;
            this.decML = decML;
            this.stateLL = stateLL;
            this.stateOF = stateOF;
            this.stateML = stateML;
            rep1 = 1;
            rep2 = 4;
            rep3 = 8;
        }

        public bool TryProcessSequence(ZstdSeq sequence, ref LittleEndianReverseBitReader reader)
        {
            var llCode = decLL.PeekSymbol(stateLL);
            var mlCode = decML.PeekSymbol(stateML);
            var ofCode = decOF.PeekSymbol(stateOF);

            if (!TryReadExtraBits(ofCode, ref reader, out var ofExtra)) return false;
            if (!TryReadExtraBits(ZstdLengthsTables.MLAddBits[mlCode], ref reader, out var mlExtra)) return false;
            if (!TryReadExtraBits(ZstdLengthsTables.LLAddBits[llCode], ref reader, out var llExtra)) return false;

            if (!TryUpdateState(decLL, ref stateLL, ref reader)) return false;
            if (!TryUpdateState(decML, ref stateML, ref reader)) return false;
            if (!TryUpdateState(decOF, ref stateOF, ref reader)) return false;

            var literalLength = llCode <= 15 ? llCode : (int)(ZstdLengthsTables.LLBase[llCode] + llExtra);
            var matchLength = mlCode <= 31 ? (mlCode + 3) : (int)(ZstdLengthsTables.MLBase[mlCode] + mlExtra);
            if (literalLength != sequence.LL || matchLength != sequence.ML)
                return false;

            if (!TryResolveOffset(literalLength, ofCode, ofExtra, out var offset, out var repKind))
                return false;

            return MatchesSequenceDescriptor(sequence, offset, repKind);
        }

        private static bool TryUpdateState(FseDecoder decoder, ref uint state, ref LittleEndianReverseBitReader reader)
        {
            var nbBits = decoder.PeekNbBits(state);
            if (nbBits != 0)
            {
                if (!reader.TryReadBits(nbBits, out var additional))
                    return false;
                decoder.UpdateState(ref state, additional);
            }
            else
            {
                decoder.UpdateState(ref state, 0);
            }

            return true;
        }

        private bool TryResolveOffset(int literalLength, int ofCode, uint ofExtra, out int offset, out RepKind repKind)
        {
            var ofValue = (1u << ofCode) + ofExtra;
            if (ofValue > 3)
            {
                return HandleLongOffset(ofValue, out offset, out repKind);
            }

            return literalLength == 0
                ? TryResolveRepeatOffsetWithoutLiterals(ofValue, out offset, out repKind)
                : TryResolveRepeatOffsetWithLiterals(ofValue, out offset, out repKind);
        }

        private bool HandleLongOffset(uint ofValue, out int offset, out RepKind repKind)
        {
            offset = (int)(ofValue - 3);
            repKind = RepKind.None;
            rep3 = rep2;
            rep2 = rep1;
            rep1 = (uint)offset;
            return true;
        }

        private bool TryResolveRepeatOffsetWithoutLiterals(uint ofValue, out int offset, out RepKind repKind)
        {
            switch (ofValue)
            {
                case 1:
                    offset = (int)rep2;
                    rep2 = rep1;
                    rep1 = (uint)offset;
                    repKind = RepKind.Rep2;
                    return true;
                case 2:
                    offset = (int)rep3;
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = (uint)offset;
                    repKind = RepKind.Rep3;
                    return true;
                default:
                    offset = (int)rep1 - 1;
                    if (offset <= 0)
                    {
                        repKind = RepKind.None;
                        return false;
                    }

                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = (uint)offset;
                    repKind = RepKind.Rep1Minus1;
                    return true;
            }
        }

        private bool TryResolveRepeatOffsetWithLiterals(uint ofValue, out int offset, out RepKind repKind)
        {
            switch (ofValue)
            {
                case 1:
                    offset = (int)rep1;
                    repKind = RepKind.Rep1;
                    return true;
                case 2:
                    offset = (int)rep2;
                    (rep2, rep1) = (rep1, rep2);
                    repKind = RepKind.Rep2;
                    return true;
                case 3:
                    offset = (int)rep3;
                    var temp = rep1;
                    rep1 = rep3;
                    rep3 = rep2;
                    rep2 = temp;
                    repKind = RepKind.Rep3;
                    return true;
                default:
                    offset = 0;
                    repKind = RepKind.None;
                    return false;
            }
        }

        private static bool TryReadExtraBits(int count, ref LittleEndianReverseBitReader reader, out uint value)
        {
            if (count <= 0)
            {
                value = 0;
                return true;
            }

            return reader.TryReadBits(count, out value);
        }

        private static bool MatchesSequenceDescriptor(ZstdSeq sequence, int offset, RepKind actualRep)
        {
            if (sequence.Rep == RepKind.None)
            {
                return actualRep == RepKind.None && offset == sequence.Offset;
            }

            return actualRep == sequence.Rep && offset == sequence.Offset;
        }
    }


    private static void ParseNbSeqHeader(ReadOnlySpan<byte> src, out int nbSeq, out int headerLen)
    {
        var b0 = src[0];
        if (b0 == 0) { nbSeq = 0; headerLen = 1; return; }
        if (b0 < 128) { nbSeq = b0; headerLen = 1; return; }
        if (b0 < 255)
        {
            if (src.Length < 2) throw new InvalidDataException("Truncated nbSeq");
            nbSeq = ((b0 - 0x80) << 8) + src[1]; headerLen = 2; return;
        }
        if (src.Length < 3) throw new InvalidDataException("Truncated nbSeq");
        nbSeq = 0x7F00 + src[1] + (src[2] << 8); headerLen = 3;
    }
#endif
}
