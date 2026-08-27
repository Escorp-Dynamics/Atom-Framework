#pragma warning disable CA2213

using System.Buffers.Binary;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.IO.Compression.Huffman;
using Atom.IO.Compression.Zstd;

namespace Atom.IO.Compression;

/// <summary>
/// Внутренний декодер Zstd: RAW/RLE блоки, skippable‑фреймы, Content Checksum. Без сторонних библиотек.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal sealed class ZstdDecoder([NotNull] System.IO.Stream input, IZstdDictionaryProvider? dictionaryProvider = null) : IDisposable
{
    private readonly System.IO.Stream stream = input;
    private readonly IZstdDictionaryProvider? dictProvider = dictionaryProvider;

    // Состояние кадра
    private bool inFrame;
    private bool hasChecksum;

    // Состояние блока
    private ZstdBlockKind blockKind = ZstdBlockKind.None;
    private int blockRemaining;
    private byte rleValue;
    private bool lastBlock;

    private XxHash64 hash = new();
    private bool isDisposed;

    // Окно истории (ring buffer) для матчей; будет использовано для Compressed-блоков
    private int windowSize;
    private int winPos;
    private int winFill;
    private bool isWindowInitialized;
    private Span<byte> WindowSpan => workspace.GetWindowSpan(windowSize);

    // История повторных смещений между Compressed-блоками
    private uint rep1 = 1, rep2 = 4, rep3 = 8;

    // Очередь готового вывода (для Compressed-блоков)
    private int pendingPos;
    private int pendingLen;
    private readonly ZstdDecoderWorkspace workspace = ZstdDecoderWorkspacePool.Rent();
    private bool workspaceReturned;

    // FSE таблицы для Repeat_Mode (переиспользуются между Compressed-блоками)
    private ref ZstdDecoderWorkspace.SequenceTableBlock LlTables => ref workspace.LiteralLengthTables;
    private ref ZstdDecoderWorkspace.SequenceTableBlock MlTables => ref workspace.MatchLengthTables;
    private ref ZstdDecoderWorkspace.SequenceTableBlock OfTables => ref workspace.OffsetTables;
    // Признак «таблица уже построена» и её Accuracy_Log хранит сам блок рабочего пространства
    // (SequenceTableBlock.HasTable/TableLog) — отдельно для каждого из трёх символьных классов.

    // Huffman таблица литералов (для Treeless)
    private bool hasHuffTable;
    // Словарь (отложенное применение после инициализации окна)
    private ReadOnlyMemory<byte> pendingDictContent;
    private bool hasPendingDict;
    private uint dictRep1, dictRep2, dictRep3;

    // Буферизированный ввод для снижения количества Read()
    private int inPos, inLen;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> dst)
    {
        if (dst.Length == 0) return 0;

        var written = 0;
        while (written < dst.Length)
        {
            if (pendingLen > 0)
            {
                written += DrainPending(dst[written..]);
                continue;
            }

            if (!EnsureBlockReady())
            {
                break;
            }

            if (blockKind == ZstdBlockKind.None)
            {
                continue;
            }

            var target = dst[written..];
            written += blockKind switch
            {
                ZstdBlockKind.Raw => CopyRawBlock(target),
                ZstdBlockKind.Rle => EmitRleBlock(target),
                _ => throw new NotSupportedException("Compressed blocks are not supported yet"),
            };
        }

        return written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DrainPending(Span<byte> destination)
    {
        var pendingSpan = workspace.GetPendingSpan();
        var toCopy = Math.Min(destination.Length, pendingLen);
        pendingSpan.Slice(pendingPos, toCopy).CopyTo(destination[..toCopy]);
        pendingPos += toCopy;
        pendingLen -= toCopy;
        if (pendingLen == 0) pendingPos = 0;
        return toCopy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureBlockReady()
    {
        while (true)
        {
            if (blockKind != ZstdBlockKind.None && blockRemaining > 0) return true;

            // Compressed-блок кладёт ВЕСЬ свой выход в pending и не оставляет blockRemaining.
            // Без этой проверки цикл сразу читал следующий блок и затирал ещё не выданные данные,
            // а на последнем блоке возвращал false — весь распакованный кадр терялся.
            // Раньше это не проявлялось: собственный кодировщик compressed-блоков не пишет.
            if (pendingLen > 0) return true;

            if (!inFrame)
            {
                if (!TryStartNextFrame()) return false;
                continue;
            }

            // false при живом кадре означает усечённый заголовок блока; false с закрытым кадром —
            // что кадр только что завершился и надо попробовать следующий.
            if (!TryBeginNextBlock() && inFrame) return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CopyRawBlock(Span<byte> destination)
    {
        var toRead = Math.Min(blockRemaining, destination.Length);
        var span = destination[..toRead];
        if (!ReadExact(span)) throw new InvalidDataException("Unexpected EOF inside RAW block");
        if (hasChecksum) hash.Update(span);
        AppendToWindow(span);

        blockRemaining -= toRead;
        if (blockRemaining == 0 && lastBlock) FinishFrame();

        return toRead;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EmitRleBlock(Span<byte> destination)
    {
        var toWrite = Math.Min(blockRemaining, destination.Length);
        var target = destination[..toWrite];
        target.Fill(rleValue);
        if (hasChecksum) hash.UpdateRepeat(rleValue, toWrite);
        AppendRepeatToWindow(rleValue, toWrite);

        blockRemaining -= toWrite;
        if (blockRemaining == 0 && lastBlock) FinishFrame();

        return toWrite;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(Read(dst.Span));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;
        ReleaseWorkspace();
    }

    private void ReleaseWorkspace()
    {
        if (workspaceReturned) return;
        workspace.Reset();
        ZstdDecoderWorkspacePool.Return(workspace);
        workspaceReturned = true;
        inPos = 0;
        inLen = 0;
    }

    // ------------ Внутренности ------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryStartNextFrame()
    {
        Span<byte> header = stackalloc byte[4];
        Span<byte> lengthBuffer = stackalloc byte[4];
        while (TryReadFrameMagic(header, out var magic))
        {
            if (TryBeginFrame(magic)) return true;
            if (TrySkipSkippableFrame(magic, lengthBuffer)) continue;
            throw new InvalidDataException("Unknown magic");
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadFrameMagic(Span<byte> header, out uint magic)
    {
        if (!ReadExact(header))
        {
            magic = 0;
            return false;
        }

        magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginFrame(uint magic)
    {
        if (magic != ZstdStream.FrameMagic) return false;

        ParseFrameHeader();
        InitializeFrameState();
        return true;
    }

    private void InitializeFrameState()
    {
        inFrame = true;

        // ★ Окно истории поднимается СРАЗУ на старте кадра. Раньше оно поднималось лениво,
        // в первом compressed-блоке, и InitFrameWindow обнулял winPos/winFill — то есть
        // выбрасывал всё, что успели дописать предшествующие RAW- и RLE-блоки. Совпадение,
        // уходящее в такую историю, читало мусор (а с проверкой границ — отвергалось).
        InitFrameWindow();
        if (hasChecksum) hash = new XxHash64();
        rep1 = 1; rep2 = 4; rep3 = 8;
        if (!hasPendingDict) return;

        // Содержимое словаря — это история ПЕРЕД первым байтом кадра, поэтому оно кладётся
        // в окно сразу. Раньше оно дописывалось лениво, в первом compressed-блоке: если кадр
        // начинался с RAW- или RLE-блока, словарь ложился в окно уже ПОСЛЕ его данных.
        AppendToWindow(pendingDictContent.Span);
        hasPendingDict = false;

        if (dictRep1 == 0 || dictRep2 == 0 || dictRep3 == 0) return;
        rep1 = dictRep1;
        rep2 = dictRep2;
        rep3 = dictRep3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySkipSkippableFrame(uint magic, Span<byte> lengthBuffer)
    {
        if ((magic & 0xFFFFFFF0u) != ZstdStream.SkippableBase) return false;
        if (!ReadExact(lengthBuffer)) throw new InvalidDataException("Truncated skippable size");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);
        SkipBytes(size);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseFrameHeader()
    {
        Span<byte> buffer = stackalloc byte[14];
        var descriptor = ReadFrameDescriptor(buffer);
        var fcsId = (descriptor >> 6) & 0x3;
        var singleSegment = ((descriptor >> 5) & 1) != 0;
        var hasDictionary = (descriptor & 0x3) != 0;

        if (!singleSegment)
        {
            windowSize = ReadWindowSize(buffer);
        }

        if (hasDictionary)
        {
            ReadAndApplyDictionary(descriptor & 0x3, buffer);
        }

        var frameContentSize = ReadFrameContentSize(fcsId, singleSegment, buffer);
        if (singleSegment)
        {
            windowSize = ClampSingleSegmentWindow(frameContentSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadFrameDescriptor(Span<byte> buffer)
    {
        if (!ReadExact(buffer[..1])) throw new InvalidDataException("Truncated FHD");
        var descriptor = buffer[0];
        var reservedBit = (descriptor >> 3) & 1;
        hasChecksum = ((descriptor >> 2) & 1) != 0;
        if (reservedBit != 0) throw new InvalidDataException("Reserved bit must be 0");
        return descriptor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadWindowSize(Span<byte> buffer)
    {
        if (!ReadExact(buffer[..1])) throw new InvalidDataException("Truncated WD");
        return DecodeWindowDescriptor(buffer[0]);
    }

    private void ReadAndApplyDictionary(int dictionaryDescriptor, Span<byte> buffer)
    {
        var size = dictionaryDescriptor switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            _ => throw new InvalidDataException("Invalid dictionary id descriptor"),
        };

        if (!ReadExact(buffer[..size])) throw new InvalidDataException("Truncated DID");
        var did = size switch
        {
            1 => buffer[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]),
            _ => 0u,
        };

        if (dictProvider is null) throw new NotSupportedException("Dictionary frames are not supported");
        if (!dictProvider.TryGet(did, out var dictBytes)) throw new InvalidDataException("Dictionary not found for DID");
        ParseDictionary(dictBytes, did);
    }

    private ulong ReadFrameContentSize(int fcsId, bool singleSegment, Span<byte> buffer)
    {
        if (singleSegment)
        {
            var bytesToRead = GetSingleSegmentFcsLength(fcsId);
            return ReadFrameSizeValue(buffer, bytesToRead);
        }

        if (fcsId == 0) return 0;
        var multiSegmentLength = GetMultiSegmentFcsLength(fcsId);
        return ReadFrameSizeValue(buffer, multiSegmentLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampSingleSegmentWindow(ulong frameSize)
    {
        var window = (long)frameSize;
        if (window < 1024) window = 1024;
        if (window > ZstdStream.MaxWindowSize) window = ZstdStream.MaxWindowSize;
        return (int)window;
    }

    private static int GetSingleSegmentFcsLength(int fcsId) => fcsId switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new InvalidDataException("Invalid FCS size"),
    };

    private static int GetMultiSegmentFcsLength(int fcsId) => fcsId switch
    {
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new InvalidDataException("Invalid FCS size"),
    };

    private ulong ReadFrameSizeValue(Span<byte> buffer, int length)
    {
        if (length <= 0) throw new InvalidDataException("Invalid FCS size");
        if (!ReadExact(buffer[..length])) throw new InvalidDataException("Truncated FCS");

        var value = length switch
        {
            1 => buffer[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]) + 256u,
            4 => BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]),
            8 => BinaryPrimitives.ReadUInt64LittleEndian(buffer[..8]),
            _ => 0u,
        };

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginNextBlock()
    {
        Span<byte> h = stackalloc byte[3];
        if (!ReadExact(h)) return false;
        var header = (uint)(h[0] | (h[1] << 8) | (h[2] << 16));
        lastBlock = (header & 1) != 0;
        var type = (int)((header >> 1) & 0x3);
        var size = (int)(header >> 3);

        switch (type)
        {
            case 0:
                blockKind = ZstdBlockKind.Raw; blockRemaining = size; break;
            case 1:
                {
                    Span<byte> v = stackalloc byte[1];
                    if (!ReadExact(v)) throw new InvalidDataException("Truncated RLE byte");
                    rleValue = v[0]; blockKind = ZstdBlockKind.Rle; blockRemaining = size; break;
                }
            case 2:
                DecompressCompressedBlock(size);
                blockKind = ZstdBlockKind.None; // вся распаковка в pending
                if (lastBlock) FinishFrame();
                return true;
            default:
                throw new InvalidDataException("Invalid block type");
        }

        if (blockRemaining == 0 && lastBlock)
        {
            FinishFrame();
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishFrame()
    {
        if (hasChecksum)
        {
            Span<byte> c = stackalloc byte[4];
            if (!ReadExact(c)) throw new InvalidDataException("Truncated checksum");
            var got = BinaryPrimitives.ReadUInt32LittleEndian(c);
            var exp = unchecked((uint)hash.Digest());
            if (got != exp) throw new InvalidDataException("Content checksum mismatch");
        }

        inFrame = false;
        blockKind = ZstdBlockKind.None;
        blockRemaining = 0;
        lastBlock = false;
        hasPendingDict = false;
        // Освобождаем окно истории
        winPos = 0;
        winFill = 0;
        windowSize = 0;
        isWindowInitialized = false;
        // Сбрасываем таблицы: Repeat_Mode в новом кадре не имеет права смотреть на предыдущий.
        workspace.HuffmanTable.Reset();
        LlTables.Reset();
        MlTables.Reset();
        OfTables.Reset();
        hasHuffTable = false;

        // ★ inPos/inLen НЕ сбрасываются: во входном буфере лежат уже прочитанные из потока байты,
        // которые принадлежат СЛЕДУЮЩЕМУ кадру. Раньше они выбрасывались, и склейка нескольких
        // кадров (а также пропускаемые кадры между ними) разваливалась с «Unknown magic» —
        // ровно там, где буфер успел захватить хвост за границей кадра.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadExact(Span<byte> dst)
    {
        var remaining = dst.Length;
        var offset = 0;
        var inputBuffer = workspace.GetInputSpan();
        while (remaining > 0)
        {
            var buffered = inLen - inPos;
            if (buffered > 0)
            {
                var take = Math.Min(remaining, buffered);
                inputBuffer.Slice(inPos, take).CopyTo(dst.Slice(offset, take));
                inPos += take;
                offset += take;
                remaining -= take;
                continue;
            }

            if (remaining >= inputBuffer.Length)
            {
                var span = dst.Slice(offset, remaining);
                var read = stream.Read(span);
                if (read <= 0) return false;
                offset += read;
                remaining -= read;
                continue;
            }

            var readCount = stream.Read(inputBuffer);
            if (readCount <= 0) return false;
            inPos = 0;
            inLen = readCount;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipBytes(uint count)
    {
        var left = count;
        var inputBuffer = workspace.GetInputSpan();
        while (left > 0)
        {
            var buffered = inLen - inPos;
            if (buffered > 0)
            {
                var take = Math.Min((int)left, buffered);
                inPos += take;
                left -= (uint)take;
                continue;
            }

            var readCount = stream.Read(inputBuffer);
            if (readCount <= 0) throw new InvalidDataException("Unexpected EOF while skipping");

            if ((uint)readCount > left)
            {
                inPos = (int)left;
                inLen = readCount;
                left = 0;
            }
            else
            {
                left -= (uint)readCount;
                inPos = 0;
                inLen = 0;
            }
        }
    }

    // ---- Compressed Block (Predefined modes only; Literals RAW/RLE only)
    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct LiteralsInfo
    {
        public LiteralsInfo(int headerLength, int sectionLength, int size, bool isRle, byte rleValue, bool usesWorkspace, ReadOnlySpan<byte> rawData)
        {
            HeaderLength = headerLength;
            SectionLength = sectionLength;
            Size = size;
            IsRle = isRle;
            RleValue = rleValue;
            UsesWorkspace = usesWorkspace;
            RawData = rawData;
        }

        public int HeaderLength { get; }
        public int SectionLength { get; }
        public int Size { get; }
        public bool IsRle { get; }
        public byte RleValue { get; }
        public bool UsesWorkspace { get; }
        public ReadOnlySpan<byte> RawData { get; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetLiteralData(in LiteralsInfo info)
        => info.UsesWorkspace ? workspace.GetLiteralsSpan()[..info.Size] : info.RawData;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetLiteralSlice(in LiteralsInfo info, int offset, int length)
        => GetLiteralData(info).Slice(offset, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralsInfo DecodeLiteralsSection(ReadOnlySpan<byte> body, ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock)
    {
        if (body.IsEmpty) throw new InvalidDataException("Empty compressed block body");
        var ltype = body[0] & 0x3;
        return ltype switch
        {
            0 or 1 => DecodeRawOrRleLiterals(body),
            2 => DecodeCompressedLiterals(body, ref huffmanBlock, literalType: 2),
            3 => DecodeCompressedLiterals(body, ref huffmanBlock, literalType: 3),
            _ => throw new InvalidDataException("Unsupported literals section type"),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LiteralsInfo DecodeRawOrRleLiterals(ReadOnlySpan<byte> body)
    {
        ParseLiteralsSectionHeader(body, out var headerLen, out var regeneratedSize, out var isRle);
        if (isRle)
        {
            if (body.Length < headerLen + 1) throw new InvalidDataException("Truncated RLE literals");
            var sectionLength = headerLen + 1;
            return new LiteralsInfo(headerLen, sectionLength, regeneratedSize, isRle: true, body[headerLen], usesWorkspace: false, rawData: []);
        }

        if (body.Length < headerLen + regeneratedSize) throw new InvalidDataException("Truncated RAW literals body");
        var data = body.Slice(headerLen, regeneratedSize);
        return new LiteralsInfo(headerLen, headerLen + regeneratedSize, regeneratedSize, isRle: false, default, usesWorkspace: false, rawData: data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralsInfo DecodeCompressedLiterals(ReadOnlySpan<byte> body, ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock, int literalType)
    {
        ParseCompressedLiteralsHeader(body, out var headerLen, out var regeneratedSize, out var compressedSize, out var isFourStreams);

        // RFC 8878, §3.1.1.2: содержимое блока не превышает 128 КиБ. Раньше значение из заголовка
        // нигде не проверялось, и мусорный размер валился ArgumentOutOfRangeException из Span.Slice
        // вместо диагностируемого InvalidDataException.
        if ((uint)regeneratedSize > ZstdStream.MaxRawBlockSize) throw new InvalidDataException("Literals regenerated size exceeds block limit");
        if (body.Length < headerLen + compressedSize) throw new InvalidDataException("Truncated compressed literals body");

        var payload = body.Slice(headerLen, compressedSize);
        if (payload.IsEmpty) throw new InvalidDataException("Empty compressed literals payload");

        var table = BuildHuffmanTable(payload, literalType, ref huffmanBlock, out var streams);
        var target = workspace.GetLiteralsSpan();
        DecodeHuffmanStreams(streams, isFourStreams, target[..regeneratedSize], in table);
        return new LiteralsInfo(headerLen, headerLen + compressedSize, regeneratedSize, isRle: false, default, usesWorkspace: true, rawData: []);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HuffmanTable BuildHuffmanTable(
        ReadOnlySpan<byte> payload,
        int literalType,
        ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock,
        out ReadOnlySpan<byte> streams)
    {
        if (literalType == 2)
        {
            return BuildHuffmanTableFromPayload(payload, ref huffmanBlock, out streams);
        }

        if (!TryGetHuffmanTable(out var table))
            throw new InvalidDataException("Treeless without prior Huffman table");

        streams = payload;
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HuffmanTable BuildHuffmanTableFromPayload(
        ReadOnlySpan<byte> payload,
        ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock,
        out ReadOnlySpan<byte> streams)
    {
        var headerByte = payload[0];
        if (headerByte < 128)
        {
            var weightsSize = headerByte;
            if (payload.Length < 1 + weightsSize) throw new InvalidDataException("Truncated FSE-compressed weights");
            var weights = payload.Slice(1, weightsSize);
            var table = ZstdWeightsParser.ParseFseWeights(weights, ref huffmanBlock, out var consumedBytes);
            if (consumedBytes != weightsSize) throw new InvalidDataException("Huffman weights size mismatch");
            StoreHuffmanTable();
            streams = payload[(1 + weightsSize)..];
            return table;
        }
        else
        {
            var numberOfWeights = headerByte - 127;
            var bytesNeeded = (numberOfWeights + 1) / 2;
            if (payload.Length < 1 + bytesNeeded) throw new InvalidDataException("Truncated direct weights");
            var weights = payload.Slice(1, bytesNeeded);
            var result = ZstdWeightsParser.ParseDirectWeights(weights, numberOfWeights, ref huffmanBlock);
            StoreHuffmanTable();
            streams = payload[(1 + bytesNeeded)..];
            return result;
        }
    }

    /// <summary>
    /// Декодирует один или четыре потока литералов, сжатых префиксными кодами.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §3.1.1.3.1.6. Разбор самой Jump_Table был верен, а вот раскладка выхода — нет:
    /// каждому потоку передавался остаток буфера, а декодер заполнял его С КОНЦА, поэтому потоки
    /// затирали друг друга. Размер сегмента вычисляется как (Regenerated_Size+3)/4.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecodeHuffmanStreams(ReadOnlySpan<byte> streams, bool isFourStreams, Span<byte> destination, in HuffmanTable table)
    {
        if (!isFourStreams)
        {
            ZstdHuffman.DecodeStream(streams, destination, in table);
            return;
        }

        if (streams.Length < 6) throw new InvalidDataException("Truncated Huffman jump table");
        var s1 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams[..2]);
        var s2 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams.Slice(2, 2));
        var s3 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams.Slice(4, 2));
        var total = streams.Length - 6;
        var s4 = total - (s1 + s2 + s3);
        if (s4 < 0) throw new InvalidDataException("Invalid Huffman streams sizes");

        var off2 = 6 + s1;
        var off3 = off2 + s2;
        var off4 = off3 + s3;

        ZstdHuffman.Decode4Streams(
            streams.Slice(6, s1),
            streams.Slice(off2, s2),
            streams.Slice(off3, s3),
            streams.Slice(off4, s4),
            destination,
            in table);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct SequenceHeader(
        int nbSeq,
        int llMode,
        int ofMode,
        int mlMode,
        ReadOnlySpan<byte> tableData)
    {
        public int NbSeq { get; } = nbSeq;
        public int LlMode { get; } = llMode;
        public int OfMode { get; } = ofMode;
        public int MlMode { get; } = mlMode;

        /// <summary>Описания таблиц и следующий за ними битовый поток.</summary>
        public ReadOnlySpan<byte> TableData { get; } = tableData;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SequenceHeader ParseSequenceHeader(ReadOnlySpan<byte> seq)
    {
        ParseNbSeq(seq, out var nbSeq, out var nbHdrLen);

        // RFC 8878, §3.1.1.3.2.1: при Number_Of_Sequences == 0 секция на этом заканчивается —
        // байта Symbol_Compression_Modes нет. Раньше он требовался безусловно, и блок
        // «только литералы» (секция из единственного 0x00) отвергался как усечённый.
        if (nbSeq == 0) return new SequenceHeader(0, 0, 0, 0, []);

        if (seq.Length < nbHdrLen + 1) throw new InvalidDataException("Truncated modes byte");

        var modes = seq[nbHdrLen];
        var llMode = (modes >> 6) & 0x3;
        var ofMode = (modes >> 4) & 0x3;
        var mlMode = (modes >> 2) & 0x3;

        // ★ Описания трёх таблиц идут ПОДРЯД и переменной длины, поэтому разобрать их можно
        // только одним проходом — по порядку LL, OF, ML. Раньше здесь пропускались лишь
        // однобайтовые описания (RLE), а сжатые объявлялись неподдерживаемыми; из-за этого и
        // граница битового потока считалась в двух местах по-разному.
        var tableData = seq[(nbHdrLen + 1)..];

        return new SequenceHeader(nbSeq, llMode, ofMode, mlMode, tableData);
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceDecoders(int nbSeq)
    {
        public int NbSeq { get; } = nbSeq;
        public FseDecoder DecLL;
        public FseDecoder DecOF;
        public FseDecoder DecML;
        public uint StateLL;
        public uint StateOF;
        public uint StateML;
        public ZstdReverseBitReader BitReader;
    }

    /// <summary>
    /// Готовит три FSE-декодера и обратный битовый поток секции последовательностей.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §3.1.1.3.2.2: режим задаётся ОТДЕЛЬНО для каждого из трёх символьных классов.
    /// Раньше при любом Predefined_Mode перестраивались сразу ВСЕ ТРИ таблицы, поэтому сочетание
    /// вида LL=0, OF=3, ML=0 уничтожало сохранённую с прошлого блока таблицу OF и подменяло её
    /// предопределённой — расхождение данных без единого исключения. Заодно общий признак
    /// «таблицы уже есть» взводился до проверки Repeat_Mode и обезвреживал её.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SequenceDecoders PrepareSequenceDecoders(
        in SequenceHeader header,
        ref ZstdDecoderWorkspace.SequenceTableBlock llTables,
        ref ZstdDecoderWorkspace.SequenceTableBlock mlTables,
        ref ZstdDecoderWorkspace.SequenceTableBlock ofTables)
    {
        // Описания трёх таблиц идут подряд и имеют переменную длину, поэтому разбираются
        // одним проходом строго в порядке LL, OF, ML; битовый поток начинается сразу за последним.
        var tableData = header.TableData;
        var headerOffset = 0;

        var llLog = PrepareSequenceTable(
            header.LlMode, tableData, ref headerOffset,
            ZstdLengthsTables.LL_DefaultNorm, ZstdLengthsTables.LL_AccuracyLog,
            ZstdLengthsTables.LL_MaxAccuracyLog, ZstdLengthsTables.LL_MaxSymbol, ref llTables, "LL");

        var ofLog = PrepareSequenceTable(
            header.OfMode, tableData, ref headerOffset,
            ZstdLengthsTables.OffsetsDefaultNorm, ZstdLengthsTables.OffsetsAccuracyLog,
            ZstdLengthsTables.OffsetsMaxAccuracyLog, ZstdLengthsTables.OffsetsMaxSymbol, ref ofTables, "OF");

        var mlLog = PrepareSequenceTable(
            header.MlMode, tableData, ref headerOffset,
            ZstdLengthsTables.ML_DefaultNorm, ZstdLengthsTables.ML_AccuracyLog,
            ZstdLengthsTables.ML_MaxAccuracyLog, ZstdLengthsTables.ML_MaxSymbol, ref mlTables, "ML");

        var decoders = new SequenceDecoders(header.NbSeq)
        {
            DecLL = FseDecoder.FromTables(llLog, llTables.GetSymbols(), llTables.GetNbBits(), llTables.GetBase()),
            DecOF = FseDecoder.FromTables(ofLog, ofTables.GetSymbols(), ofTables.GetNbBits(), ofTables.GetBase()),
            DecML = FseDecoder.FromTables(mlLog, mlTables.GetSymbols(), mlTables.GetNbBits(), mlTables.GetBase()),
        };

        InitializeDecoderStates(ref decoders, in header, tableData[headerOffset..], llLog, ofLog, mlLog);
        return decoders;
    }

    /// <summary>
    /// Снимает набивку обратного потока и читает начальные состояния в порядке LL, OF, ML.
    /// </summary>
    private static void InitializeDecoderStates(
        ref SequenceDecoders decoders,
        in SequenceHeader header,
        ReadOnlySpan<byte> bitstream,
        int llLog,
        int ofLog,
        int mlLog)
    {
        var reader = new ZstdReverseBitReader(bitstream);
        if (!reader.IsValid) throw new InvalidDataException("Malformed sequences bitstream padding");

        if (!reader.TryReadBits(llLog, out decoders.StateLL))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial LL state (llLog={llLog}, nbSeq={header.NbSeq}, bitstreamLen={bitstream.Length})"));
        if (!reader.TryReadBits(ofLog, out decoders.StateOF))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial OF state (ofLog={ofLog}, nbSeq={header.NbSeq}, bitstreamLen={bitstream.Length})"));
        if (!reader.TryReadBits(mlLog, out decoders.StateML))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial ML state (mlLog={mlLog}, nbSeq={header.NbSeq}, bitstreamLen={bitstream.Length})"));

        decoders.BitReader = reader;
    }

    /// <summary>
    /// Готовит таблицу одного символьного класса согласно её режиму.
    /// </summary>
    /// <returns>Accuracy_Log подготовленной таблицы.</returns>
    private static int PrepareSequenceTable(
        int mode,
        ReadOnlySpan<byte> tableData,
        ref int offset,
        ReadOnlySpan<short> predefinedNorm,
        int predefinedLog,
        int maxAccuracyLog,
        int maxSymbol,
        ref ZstdDecoderWorkspace.SequenceTableBlock tables,
        string tableName)
    {
        switch (mode)
        {
            case 0: // Predefined_Mode
                BuildTableFromNorm(predefinedNorm, predefinedLog, ref tables);
                return predefinedLog;

            case 1: // RLE_Mode
                {
                    if (offset >= tableData.Length) throw new InvalidDataException($"Truncated {tableName} RLE table");
                    var symbol = tableData[offset++];
                    if (symbol > maxSymbol) throw new InvalidDataException($"{tableName} RLE symbol is out of range");

                    // RLE_Mode — это полноценная таблица из одного состояния (Accuracy_Log = 0).
                    // Раньше она нигде не сохранялась, и следующий блок в Repeat_Mode получал
                    // устаревшую FSE- или предопределённую таблицу.
                    BuildRleTable(symbol, ref tables);
                    return 0;
                }

            case 2: // FSE_Compressed_Mode
                {
                    Span<short> norm = stackalloc short[maxSymbol + 1];
                    var consumed = ZstdFseTable.ParseNormalizedCounts(tableData[offset..], maxSymbol, maxAccuracyLog, norm, out var log, out var lastSymbol);
                    offset += consumed;
                    BuildTableFromNorm(norm[..(lastSymbol + 1)], log, ref tables);
                    return log;
                }

            case 3: // Repeat_Mode
                if (!tables.HasTable) throw new InvalidDataException($"Repeat mode without previous {tableName} table");
                return tables.TableLog;

            default:
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unsupported {tableName} mode: {mode}"));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildTableFromNorm(ReadOnlySpan<short> norm, int log, ref ZstdDecoderWorkspace.SequenceTableBlock tables)
    {
        tables.GetWritable(log, out var symbols, out var nbBits, out var baseTable);
        FseDecoder.Build(norm, log, symbols, nbBits, baseTable);
        tables.Commit(log);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildRleTable(byte symbol, ref ZstdDecoderWorkspace.SequenceTableBlock tables)
    {
        tables.GetWritable(0, out var symbols, out var nbBits, out var baseTable);
        symbols[0] = symbol;
        nbBits[0] = 0;
        baseTable[0] = 0;
        tables.Commit(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitLiteralOnlyBlock(in LiteralsInfo literals)
    {
        var pendingSpan = workspace.GetPendingSpan();
        if (literals.Size > pendingSpan.Length) throw new InvalidDataException("Pending buffer overflow");
        var target = pendingSpan[..literals.Size];
        if (literals.IsRle)
        {
            target.Fill(literals.RleValue);
        }
        else
        {
            GetLiteralData(literals).CopyTo(target);
        }

        AppendToWindow(target);
        if (hasChecksum) hash.Update(target);

        pendingPos = 0;
        pendingLen = literals.Size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSequences(ref SequenceDecoders decoders, in LiteralsInfo literals)
    {
        var output = workspace.GetPendingSpan();
        var windowSpan = WindowSpan;
        var outPos = 0;
        var literalPosition = 0;
        ref var bitReader = ref decoders.BitReader;

        var lastIndex = decoders.NbSeq - 1;
        for (var sequenceIndex = 0; sequenceIndex < decoders.NbSeq; sequenceIndex++)
        {
            DecodeSequence(ref decoders, ref bitReader, sequenceIndex, sequenceIndex == lastIndex, out var literalLength, out var matchLength, out var offset);
            outPos = ConsumeLiteralsForSequence(literalLength, literals, ref literalPosition, output, outPos, windowSpan);
            if (matchLength != 0)
            {
                AppendMatch(windowSpan, output, ref outPos, matchLength, offset);
            }
        }

        // RFC 8878, §3.1.1.3.2.1.3: после последней последовательности поток обязан быть исчерпан.
        if (bitReader.AvailableBits != 0) throw new InvalidDataException("Sequences bitstream not fully consumed");

        AppendRemainingLiterals(literals, output, ref outPos, ref literalPosition);

        // Контрольная сумма снимается один раз со всего собранного блока. Раньше xxHash
        // вызывался на каждый прогон литералов и на каждое совпадение, то есть кусками
        // по несколько байт — а на таких кусках инкрементальный буфер xxHash работает побайтно.
        if (hasChecksum) hash.Update(output[..outPos]);

        pendingPos = 0;
        pendingLen = outPos;
    }

    /// <summary>
    /// Декодирует одну последовательность и, если она не последняя, переводит состояния FSE.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §3.1.1.3.2.1.3: состояния обновляются МЕЖДУ последовательностями, то есть
    /// Number_Of_Sequences − 1 раз. Раньше обновление выполнялось безусловно, в том числе после
    /// последней последовательности: бит на него в корректном потоке уже нет, и на каждом
    /// настоящем блоке вылетало «Bitstream underflow while updating LL state».
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecodeSequence(
        ref SequenceDecoders decoders,
        ref ZstdReverseBitReader bitReader,
        int sequenceIndex,
        bool isLast,
        out int literalLength,
        out int matchLength,
        out int offset)
    {
        var llCode = decoders.DecLL.PeekSymbol(decoders.StateLL);
        var mlCode = decoders.DecML.PeekSymbol(decoders.StateML);
        var ofCode = decoders.DecOF.PeekSymbol(decoders.StateOF);

        if (llCode > ZstdLengthsTables.LL_MaxSymbol || mlCode > ZstdLengthsTables.ML_MaxSymbol || ofCode > ZstdLengthsTables.OffsetsMaxSymbol)
        {
            throw new InvalidDataException("Sequence code is out of range");
        }

        var ofExtra = ReadOptionalBits(ref bitReader, ofCode, "OF extra bits", sequenceIndex, decoders.NbSeq);
        var mlAdd = ZstdLengthsTables.MLAddBits[mlCode];
        var mlExtra = ReadOptionalBits(ref bitReader, mlAdd, "ML extra bits", sequenceIndex, decoders.NbSeq);
        var llAdd = ZstdLengthsTables.LLAddBits[llCode];
        var llExtra = ReadOptionalBits(ref bitReader, llAdd, "LL extra bits", sequenceIndex, decoders.NbSeq);

        if (!isLast)
        {
            UpdateDecoderStates(ref decoders, ref bitReader, sequenceIndex);
        }

        literalLength = llCode <= 15 ? llCode : (int)(ZstdLengthsTables.LLBase[llCode] + llExtra);
        matchLength = mlCode <= 31 ? (mlCode + 3) : (int)(ZstdLengthsTables.MLBase[mlCode] + mlExtra);
        offset = ResolveOffset(literalLength, ((ulong)1 << ofCode) + ofExtra);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadOptionalBits(
        ref ZstdReverseBitReader bitReader,
        int bitCount,
        string context,
        int sequenceIndex,
        int sequenceCount)
    {
        if (bitCount == 0)
        {
            return 0;
        }

        if (bitReader.TryReadBits(bitCount, out var value))
        {
            return value;
        }

        throw new InvalidDataException(string.Create(
            CultureInfo.InvariantCulture,
            $"Bitstream underflow while reading sequence {sequenceIndex + 1}/{sequenceCount} ({context})"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ConsumeLiteralsForSequence(
        int literalLength,
        in LiteralsInfo literals,
        ref int literalPosition,
        Span<byte> output,
        int outPos,
        Span<byte> windowSpan)
    {
        if (literalLength == 0)
        {
            return outPos;
        }

        var nextLiteralPosition = literalPosition + literalLength;
        if (nextLiteralPosition > literals.Size)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"LL exceeds literals: litPos={literalPosition}, ll={literalLength}, litRegSize={literals.Size}"));
        }

        if (literals.IsRle)
        {
            EnsureOutputCapacity(output, outPos, literalLength);
            var span = output.Slice(outPos, literalLength);
            span.Fill(literals.RleValue);
            AppendToWindow(span, windowSpan);

            // ★ Курсор литералов обязан двигаться и в RLE-ветке. Раньше он оставался нулевым,
            // и AppendRemainingLiterals дописывал в конец блока ВСЮ секцию литералов повторно —
            // хвост блока затирался Regenerated_Size одинаковых байт. Дефект не был виден,
            // потому что собственный кодировщик RLE-литералы не пишет.
            literalPosition = nextLiteralPosition;
            return outPos + literalLength;
        }

        var source = GetLiteralSlice(literals, literalPosition, literalLength);
        EnsureOutputCapacity(output, outPos, literalLength);
        source.CopyTo(output.Slice(outPos, literalLength));
        AppendToWindow(source, windowSpan);
        literalPosition = nextLiteralPosition;
        return outPos + literalLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateDecoderStates(ref SequenceDecoders decoders, ref ZstdReverseBitReader br, int sequenceIndex)
    {
        // Порядок обновления между последовательностями: LL, ML, OF (RFC 8878, §3.1.1.3.2.1.3).
        var nbLL = decoders.DecLL.PeekNbBits(decoders.StateLL);
        var addLL = 0u;
        if (nbLL != 0 && !br.TryReadBits(nbLL, out addLL))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating LL state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
        decoders.DecLL.UpdateState(ref decoders.StateLL, addLL);

        var nbML = decoders.DecML.PeekNbBits(decoders.StateML);
        var addML = 0u;
        if (nbML != 0 && !br.TryReadBits(nbML, out addML))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating ML state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
        decoders.DecML.UpdateState(ref decoders.StateML, addML);

        var nbOF = decoders.DecOF.PeekNbBits(decoders.StateOF);
        var addOF = 0u;
        if (nbOF != 0 && !br.TryReadBits(nbOF, out addOF))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating OF state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
        decoders.DecOF.UpdateState(ref decoders.StateOF, addOF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ResolveOffset(int ll, ulong offValue)
    {
        if (offValue > 3)
        {
            var raw = offValue - 3;
            if (raw > ZstdStream.MaxWindowSize) throw new InvalidDataException("Offset exceeds maximum window size");

            var offset = (int)raw;
            rep3 = rep2;
            rep2 = rep1;
            rep1 = (uint)offset;
            return offset;
        }

        return ll == 0 ? ResolveRepeatAfterEmptyLiterals(offValue) : ResolveRepeat(offValue);
    }

    /// <summary>
    /// Повторные смещения при ненулевой длине литералов (RFC 8878, §3.1.1.3.2.1.2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ResolveRepeat(ulong offValue)
    {
        if (offValue == 1) return (int)rep1;

        if (offValue == 2)
        {
            var second = (int)rep2;
            (rep2, rep1) = (rep1, rep2);
            return second;
        }

        var third = (int)rep3;
        var temp = rep1;
        rep1 = rep3;
        rep3 = rep2;
        rep2 = temp;
        return third;
    }

    /// <summary>
    /// Повторные смещения при нулевой длине литералов: коды сдвинуты, а третий означает «rep1 − 1».
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ResolveRepeatAfterEmptyLiterals(ulong offValue)
    {
        if (offValue == 1)
        {
            var second = (int)rep2;
            rep2 = rep1;
            rep1 = (uint)second;
            return second;
        }

        if (offValue == 2)
        {
            var third = (int)rep3;
            rep3 = rep2;
            rep2 = rep1;
            rep1 = (uint)third;
            return third;
        }

        var previous = (int)rep1 - 1;
        if (previous == 0) throw new InvalidDataException("offset=0");
        rep3 = rep2;
        rep2 = rep1;
        rep1 = (uint)previous;
        return previous;
    }

    /// <summary>
    /// Дописывает совпадение: сначала берёт из окна не более offset байт, затем размножает их
    /// удвоением прямо в выходном буфере.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RFC 8878, §3.1.1.3.2: при offset меньше длины совпадения оно периодично с периодом offset.
    /// </para>
    /// <para>
    /// Раньше здесь было три ветки. Одна (AppendBufferedMatch, смещения 2,3,4,8,16) копировала
    /// источник длиной filled в приёмник длиной chunk и падала ArgumentException на любом
    /// matchLength меньше 2*filled. Вторая (AppendGenericMatch, все прочие смещения) копировала
    /// порциями не длиннее offset, вызывая на каждую порцию дописывание в окно: совпадение длиной
    /// 300 при offset 5 разбивалось на шестьдесят копирований по пять байт. Теперь порция из окна
    /// берётся один раз, а хвост достраивается удвоением с одним дописыванием в окно.
    /// </para>
    /// </remarks>
    private void AppendMatch(Span<byte> windowSpan, Span<byte> outBuf, ref int outPos, int matchLength, int offset)
    {
        // RFC 8878, §3.1.1.3.2.1.2: смещение не должно уходить дальше уже выданной истории
        // (литералы текущей последовательности к этому моменту уже дописаны в окно).
        if (offset <= 0 || offset > winFill) throw new InvalidDataException("Offset exceeds available history");

        EnsureOutputCapacity(outBuf, outPos, matchLength);
        var destination = outBuf.Slice(outPos, matchLength);

        if (offset == 1)
        {
            destination.Fill(GetFromWindow(1));
        }
        else
        {
            var initial = Math.Min(offset, matchLength);
            CopyFromWindow(windowSpan, offset, destination[..initial]);

            var filled = initial;
            while (filled < matchLength)
            {
                // filled всегда кратно offset, поэтому destination[filled + i] == destination[i].
                var chunk = Math.Min(filled, matchLength - filled);
                destination[..chunk].CopyTo(destination.Slice(filled, chunk));
                filled += chunk;
            }
        }

        AppendToWindow(destination, windowSpan);
        outPos += matchLength;
    }

    /// <summary>
    /// Копирует из кольцевого окна участок, начинающийся за <paramref name="offset"/> байт до
    /// текущей позиции записи, с учётом перехода через край буфера.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyFromWindow(Span<byte> windowSpan, int offset, Span<byte> destination)
    {
        var start = winPos - offset;
        if (start < 0) start += windowSize;

        var first = Math.Min(destination.Length, windowSize - start);
        windowSpan.Slice(start, first).CopyTo(destination[..first]);
        if (first < destination.Length)
        {
            windowSpan[..(destination.Length - first)].CopyTo(destination[first..]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRemainingLiterals(in LiteralsInfo literals, Span<byte> outBuf, ref int outPos, ref int litPos)
    {
        if (litPos >= literals.Size) return;

        if (!literals.IsRle)
        {
            var tailSpan = GetLiteralData(literals)[litPos..];
            EnsureOutputCapacity(outBuf, outPos, tailSpan.Length);
            tailSpan.CopyTo(outBuf.Slice(outPos, tailSpan.Length));
            AppendToWindow(tailSpan);
            outPos += tailSpan.Length;
        }
        else
        {
            var remaining = literals.Size - litPos;
            EnsureOutputCapacity(outBuf, outPos, remaining);
            var dst = outBuf.Slice(outPos, remaining);
            dst.Fill(literals.RleValue);
            AppendToWindow(dst);
            outPos += remaining;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecompressCompressedBlock(int size)
    {
        if (!isWindowInitialized) InitFrameWindow();

        var body = workspace.AcquireCompressed(size);
        if (!ReadExact(body)) throw new InvalidDataException("Truncated compressed block body");
        ref var huffmanBlock = ref workspace.HuffmanTable;

        var literals = DecodeLiteralsSection(body, ref huffmanBlock);
        var seq = body[literals.SectionLength..];
        var header = ParseSequenceHeader(seq);

        if (header.NbSeq == 0)
        {
            EmitLiteralOnlyBlock(literals);
            return;
        }

        ref var llTables = ref LlTables;
        ref var mlTables = ref MlTables;
        ref var ofTables = ref OfTables;
        var decoders = PrepareSequenceDecoders(header, ref llTables, ref mlTables, ref ofTables);

        ExecuteSequences(ref decoders, literals);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseLiteralsSectionHeader(ReadOnlySpan<byte> src, out int headerLen, out int regeneratedSize, out bool isRle)
    {
        if (src.IsEmpty) throw new InvalidDataException("Empty literals header");
        var b0 = src[0];
        var type = b0 & 0x3;
        var sf = (b0 >> 2) & 0x3;
        if (type is 2 or 3) throw new NotSupportedException("Use ParseCompressedLiteralsHeader for compressed literals");
        isRle = type == 1;
        if (sf is 0 or 2)
        {
            regeneratedSize = b0 >> 3;
            headerLen = 1;
        }
        else if (sf == 1)
        {
            if (src.Length < 2) throw new InvalidDataException("Truncated literals header");
            regeneratedSize = (b0 >> 4) + (src[1] << 4);
            headerLen = 2;
        }
        else
        {
            if (src.Length < 3) throw new InvalidDataException("Truncated literals header");
            regeneratedSize = (b0 >> 4) + (src[1] << 4) + (src[2] << 12);
            headerLen = 3;
        }
    }

    /// <summary>
    /// Разбирает заголовок сжатой (Compressed/Treeless) секции литералов.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RFC 8878, §3.1.1.3.1.1: заголовок читается как одно little-endian целое, в котором биты 0-1 —
    /// тип, биты 2-3 — Size_Format, далее подряд Regenerated_Size шириной rBits и сразу за ним
    /// Compressed_Size шириной cBits. Обе величины начинаются не на границе байта.
    /// </para>
    /// <para>
    /// Прежняя версия собирала поля побайтно: Regenerated_Size никогда не маскировался до своей
    /// ширины (для sf=2 получалось 20 бит вместо 14 — например 605730 вместо 15906), а Compressed_Size
    /// начинался со следующей ЦЕЛОЙ байтовой границы, теряя общие с ним биты и захватывая чужой байт
    /// (616 превращалось в 14490 → «Truncated compressed literals body»). Плюс чтение вылезало
    /// на байт за проверенную длину заголовка.
    /// </para>
    /// </remarks>
    private static void ParseCompressedLiteralsHeader(ReadOnlySpan<byte> src, out int headerLen, out int regeneratedSize, out int compressedSize, out bool isFourStreams)
    {
        var sizeFormat = (src[0] >> 2) & 0x3;
        isFourStreams = sizeFormat != 0;

        int sizeBits, headerBytes;
        switch (sizeFormat)
        {
            case 0:
            case 1: sizeBits = 10; headerBytes = 3; break;
            case 2: sizeBits = 14; headerBytes = 4; break;
            default: sizeBits = 18; headerBytes = 5; break;
        }

        if (src.Length < headerBytes) throw new InvalidDataException("Truncated compressed literals header");

        ulong packed = 0;
        for (var i = 0; i < headerBytes; i++)
        {
            packed |= (ulong)src[i] << (i * 8);
        }

        var mask = (1UL << sizeBits) - 1;
        regeneratedSize = (int)((packed >> 4) & mask);
        compressedSize = (int)((packed >> (4 + sizeBits)) & mask);
        headerLen = headerBytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreHuffmanTable() => hasHuffTable = true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetHuffmanTable(out HuffmanTable table)
    {
        if (!hasHuffTable || !workspace.HuffmanTable.HasTable) { table = default; return false; }
        table = workspace.HuffmanTable.ToTable();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseNbSeq(ReadOnlySpan<byte> src, out int nbSeq, out int headerLen)
    {
        if (src.IsEmpty) throw new InvalidDataException("Empty sequences header");
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureOutputCapacity(Span<byte> buffer, int outPos, int toAppend)
    {
        if ((uint)(outPos + toAppend) > (uint)buffer.Length)
            throw new InvalidDataException("Decompressed block exceeds workspace capacity");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeWindowDescriptor(byte wd)
    {
        var exp = wd >> 3;
        var mant = wd & 0x7;
        var baseSize = 1 << (10 + exp);
        var step = baseSize >> 3;
        var size = baseSize + ((long)mant * step);
        if (size < 1024) size = 1024;
        if (size > ZstdStream.MaxWindowSize) size = ZstdStream.MaxWindowSize;
        return (int)size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitFrameWindow()
    {
        if (windowSize <= 0) windowSize = 8 * 1024 * 1024;
        _ = workspace.GetWindowSpan(windowSize);
        winPos = 0;
        winFill = 0;
        isWindowInitialized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendToWindow(ReadOnlySpan<byte> data)
    {
        if (windowSize == 0 || data.IsEmpty) return;
        AppendToWindow(data, WindowSpan);
    }

    /// <summary>
    /// Дописывает данные в кольцевое окно, когда окно уже получено вызывающим кодом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendToWindow(ReadOnlySpan<byte> data, Span<byte> windowSpan)
    {
        if (windowSize == 0 || data.IsEmpty) return;
        var size = windowSize;
        var pos = winPos;
        var len = data.Length;

        var first = Math.Min(len, size - pos);
        data[..first].CopyTo(windowSpan.Slice(pos, first));
        var remaining = len - first;
        if (remaining > 0)
        {
            data.Slice(first, remaining).CopyTo(windowSpan[..remaining]);
        }

        pos += len;
        if (pos >= size) pos -= size;
        winPos = pos;
        var newFill = winFill + len;
        winFill = newFill >= size ? size : newFill;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendOneToWindow(byte b)
    {
        var windowSpan = WindowSpan;
        if (windowSize == 0) return;
        windowSpan[winPos] = b;
        winPos++; if (winPos == windowSize) winPos = 0;
        if (winFill < windowSize) winFill++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRepeatToWindow(byte b, int count)
    {
        if (windowSize == 0 || count <= 0) return;
        var windowSpan = WindowSpan;
        var size = windowSize;
        var pos = winPos;
        var left = count;
        while (left > 0)
        {
            var chunk = Math.Min(left, size - pos);
            windowSpan.Slice(pos, chunk).Fill(b);
            pos += chunk;
            if (pos == size) pos = 0;
            left -= chunk;
        }
        winPos = pos;
        var newFill = winFill + count;
        winFill = newFill >= size ? size : newFill;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetFromWindow(int distance)
    {
        if (windowSize == 0) throw new InvalidOperationException("Window is not initialized");
        var windowSpan = WindowSpan;
        var idx = winPos - distance;
        if (idx < 0) idx += windowSize;
        return windowSpan[idx];
    }

    // ---------- Словари ----------
    private void ParseDictionary(ReadOnlyMemory<byte> dictionaryBytes, uint expectedDid)
    {
        if (TryParseFormattedDictionary(dictionaryBytes, expectedDid))
        {
            return;
        }

        ParseRawDictionary(dictionaryBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryParseFormattedDictionary(ReadOnlyMemory<byte> dictionaryBytes, uint expectedDid)
    {
        var src = dictionaryBytes.Span;
        if (!HasFormattedDictionaryHeader(src))
        {
            return false;
        }

        ValidateDictionaryId(src, expectedDid);
        var position = 8;

        position = ParseDictionaryHuffmanSection(src, position);
        position = ParseDictionarySequenceTables(src, position);
        position = ParseDictionaryRecentOffsets(src, position);

        if (src.Length - position < 8)
        {
            throw new InvalidDataException("Dictionary content too small");
        }

        pendingDictContent = dictionaryBytes[position..];
        hasPendingDict = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasFormattedDictionaryHeader(ReadOnlySpan<byte> src)
    {
        if (src.Length < 8)
        {
            return false;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(src) == 0xEC30A437;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateDictionaryId(ReadOnlySpan<byte> src, uint expectedDid)
    {
        var did = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        if (did != 0 && expectedDid != 0 && did != expectedDid)
        {
            throw new InvalidDataException("DictionaryId mismatch");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionaryHuffmanSection(ReadOnlySpan<byte> src, int position)
    {
        if (position >= src.Length)
        {
            throw new InvalidDataException("Truncated dictionary tables");
        }

        var header = src[position];
        position++;

        ref var huffmanBlock = ref workspace.HuffmanTable;
        if (header < 128)
        {
            var weightsSize = header;
            if (position + weightsSize > src.Length)
            {
                throw new InvalidDataException("Truncated dict Huffman weights");
            }

            _ = ZstdWeightsParser.ParseFseWeights(src.Slice(position, weightsSize), ref huffmanBlock, out var consumed);
            if (consumed != weightsSize)
            {
                throw new InvalidDataException("Dict Huffman weights size mismatch");
            }

            StoreHuffmanTable();
            return position + weightsSize;
        }

        var weightCount = header - 127;
        var bytesNeeded = (weightCount + 1) / 2;
        if (position + bytesNeeded > src.Length)
        {
            throw new InvalidDataException("Truncated dict direct weights");
        }

        _ = ZstdWeightsParser.ParseDirectWeights(src.Slice(position, bytesNeeded), weightCount, ref huffmanBlock);
        StoreHuffmanTable();
        return position + bytesNeeded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionarySequenceTables(ReadOnlySpan<byte> src, int position)
    {
        ref var ofTables = ref OfTables;
        ref var mlTables = ref MlTables;
        ref var llTables = ref LlTables;

        position += ParseDictFseTable(src[position..], ZstdLengthsTables.OffsetsMaxSymbol, ZstdLengthsTables.OffsetsMaxAccuracyLog, ref ofTables);
        position += ParseDictFseTable(src[position..], ZstdLengthsTables.ML_MaxSymbol, ZstdLengthsTables.ML_MaxAccuracyLog, ref mlTables);
        position += ParseDictFseTable(src[position..], ZstdLengthsTables.LL_MaxSymbol, ZstdLengthsTables.LL_MaxAccuracyLog, ref llTables);
        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionaryRecentOffsets(ReadOnlySpan<byte> src, int position)
    {
        if (position + 12 > src.Length)
        {
            throw new InvalidDataException("Truncated dict recent offsets");
        }

        dictRep1 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        dictRep2 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        dictRep3 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        if (dictRep1 == 0 || dictRep2 == 0 || dictRep3 == 0)
        {
            throw new InvalidDataException("Invalid dict recent offsets");
        }

        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseRawDictionary(ReadOnlyMemory<byte> dictionaryBytes)
    {
        if (dictionaryBytes.Length < 8)
        {
            throw new InvalidDataException("Raw dictionary too small");
        }

        pendingDictContent = dictionaryBytes;
        hasPendingDict = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseDictFseTable(ReadOnlySpan<byte> src, int maxSymbol, int maxAccuracyLog, ref ZstdDecoderWorkspace.SequenceTableBlock table)
    {
        Span<short> norm = stackalloc short[maxSymbol + 1];
        var consumed = ZstdFseTable.ParseNormalizedCounts(src, maxSymbol, maxAccuracyLog, norm, out var tableLog, out var lastSymbol);
        BuildTableFromNorm(norm[..(lastSymbol + 1)], tableLog, ref table);
        return consumed;
    }
}
