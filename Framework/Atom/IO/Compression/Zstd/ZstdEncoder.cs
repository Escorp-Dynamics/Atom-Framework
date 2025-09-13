#pragma warning disable CA2213

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Внутренний энкодер Zstd: корректный FHD (SS/DID/FCS/WD), блоки RAW/RLE, финализация и опциональный Content Checksum.
/// Гарантирует: Last_Block=1 только на последнем блоке; async-пути не блокируют поток.
/// </summary>
internal sealed class ZstdEncoder : IDisposable
{
    private readonly System.IO.Stream baseStream;
    private readonly ZstdEncoderSettings settings;
    private readonly bool useChecksum, allowRle;

    private XxHash64 hash;  // Контрольная сумма содержимого (XXH64, младшие 4 байта LE в конце кадра).

    private bool isHeaderWritten, isDisposed;

    // «Хвост» для отложенной записи последнего блока (чтобы корректно выставить Last_Block=1).
    private byte[]? tail;
    private int tailLength;

    // Async scratch (переиспользуемые мелкие буферы, без пер-вызывающих аллокаций):
    private byte[]? hdrScratch;   // до 14 байт (макс. размер Frame Header)
    private byte[]? bhScratch;    // ровно 3 байта (Block Header)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdEncoder([NotNull] System.IO.Stream output, in ZstdEncoderSettings settings)
    {
        baseStream = output;
        this.settings = settings;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.settings.BlockCap);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(this.settings.BlockCap, ZstdStream.MaxRawBlockSize);

        if (this.settings.IsSingleSegment && this.settings.FrameContentSize is null)
            throw new InvalidOperationException("При SingleSegment=true необходимо задать FrameContentSize по спецификации");

        useChecksum = this.settings.IsContentChecksumEnabled;
        hash = new XxHash64();
        allowRle = settings.CompressionLevel > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureHeader()
    {
        if (isHeaderWritten) return;
        isHeaderWritten = true;

        // 1) Magic
        Span<byte> hdr = stackalloc byte[14]; // максимум
        var p = 0;
        Unsafe.WriteUnaligned(ref hdr[p], ZstdStream.FrameMagic); p += 4; // LE

        // 2) Frame Header Descriptor (FHD)
        byte fcsf; // 2 бита
        var ss = (byte)(settings.IsSingleSegment ? 1 : 0);
        var checksum = (byte)(useChecksum ? 1 : 0);
        byte didf; // 2 бита

        // DID
        var hasDid = settings.DictionaryId.HasValue;
        didf = hasDid ? ComputeDidFieldSize(settings.DictionaryId!.Value) : (byte)0;

        // FCSF
        var hasFcs = settings.FrameContentSize.HasValue;

        if (settings.IsSingleSegment)
        {
            // При SS=1 FCS обязателен; FCSF=0 означает «1 байт», иначе 2/4/8.
            var fcsSize = PickFcsSize(settings.FrameContentSize!.Value, singleSegment: true);
            fcsf = fcsSize switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => 0 };
        }
        else
        {
            // При SS=0 FCS опционален; FCSF=0 => нет FCS, иначе 2/4/8/1? (для SS=0 «1 байт» возникает только при SS=1)
            if (!hasFcs)
            {
                fcsf = 0;
            }
            else
            {
                var fsz = PickFcsSize(settings.FrameContentSize!.Value, singleSegment: false);
                fcsf = fsz switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 }; // 1 байт при SS=0 не используется
            }
        }

        var fhd =
            (byte)((fcsf << 6) |
                   (ss << 5) |
                   (0 << 4) | // unused=0
                   (0 << 3) | // reserved=0 (RFC требует 0)
                   (checksum << 2) |
                   (didf & 0x3));

        hdr[p++] = fhd;

        // 3) Window_Descriptor (если SS=0)
        if (!settings.IsSingleSegment)
        {
            hdr[p++] = EncodeWindowDescriptor(settings.WindowSize);
        }

        // 4) Dictionary_ID (если есть)
        if (hasDid)
        {
            var did = settings.DictionaryId!.Value;
            switch (didf)
            {
                case 1: hdr[p++] = (byte)did; break;
                case 2:
                    Unsafe.WriteUnaligned(ref hdr[p], (ushort)did); p += 2;
                    break;
                case 3:
                    Unsafe.WriteUnaligned(ref hdr[p], did); p += 4;
                    break;
            }
        }

        // 5) Frame_Content_Size (если надо)
        if (settings.IsSingleSegment)
        {
            var size = settings.FrameContentSize!.Value;
            var fsz = PickFcsSize(size, singleSegment: true);
            switch (fsz)
            {
                case 1: hdr[p++] = (byte)size; break;
                case 2:
                    // при 2 байтах пишется (size - 256) LE по спецификации
                    var v = checked((ushort)(size - 256));
                    Unsafe.WriteUnaligned(ref hdr[p], v); p += 2;
                    break;
                case 4:
                    Unsafe.WriteUnaligned(ref hdr[p], (uint)size); p += 4;
                    break;
                case 8:
                    Unsafe.WriteUnaligned(ref hdr[p], size); p += 8;
                    break;
            }
        }
        else if (hasFcs)
        {
            var size = settings.FrameContentSize!.Value;
            var fsz = PickFcsSize(size, singleSegment: false);
            switch (fsz)
            {
                case 2:
                    {
                        var v = checked((ushort)(size - 256));
                        Unsafe.WriteUnaligned(ref hdr[p], v); p += 2;
                        break;
                    }
                case 4:
                    Unsafe.WriteUnaligned(ref hdr[p], (uint)size); p += 4;
                    break;
                case 8:
                    Unsafe.WriteUnaligned(ref hdr[p], size); p += 8;
                    break;
            }
        }

        baseStream.Write(hdr[..p]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteOneBlock(ReadOnlySpan<byte> data, bool last)
    {
        // Выбор типа: RLE если все байты равны
        if (allowRle && TryDetectRle(data, out var val))
        {
            WriteBlockHeader(blockType: 1 /*RLE*/, blockSize: data.Length, last: last);
            // RLE‑контент — ровно 1 байт
            Span<byte> b = [val];
            baseStream.Write(b);
            hash.UpdateRepeat(val, data.Length);
            return;
        }

        // Попытка сжать как Compressed Block (если выгодно)
        if (settings.CompressionLevel > 0)
        {
            var pool = ArrayPool<byte>.Shared;
            var tmp = pool.Rent(3 + data.Length); // верхняя оценка под [BH(3)+Body]

            try
            {
                var span = tmp.AsSpan(0, 3 + data.Length);
                var written = CompressBlock(data, span, settings.CompressionLevel, out var used, last);

                if (written > 0 && used == data.Length)
                {
                    baseStream.Write(span[..written]); // уже содержит заголовок блока
                    hash.Update(data);                 // checksum считается по несжатым данным
                    return;
                }
            }
            finally
            {
                pool.Return(tmp, clearArray: false);
            }
        }

        // RAW
        WriteBlockHeader(blockType: 0 /*RAW*/, blockSize: data.Length, last: last);
        baseStream.Write(data);
        hash.Update(data);
    }

    private async ValueTask WriteOneBlockAsync(ReadOnlyMemory<byte> data, bool last, CancellationToken cancellationToken)
    {
        EnsureHeaderAsyncScratch();

        if (allowRle && TryDetectRle(data.Span, out var val))
        {
            WriteBlockHeaderTo(bhScratch!, blockType: 1, blockSize: data.Length, last: last);
            await baseStream.WriteAsync(bhScratch.AsMemory(0, 3), cancellationToken).ConfigureAwait(false);
            bhScratch![0] = val; // переиспользуем первый байт
            await baseStream.WriteAsync(bhScratch.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            hash.UpdateRepeat(val, data.Length);
            return;
        }

        // Попытка сжать как Compressed Block (если выгодно)
        if (settings.CompressionLevel > 0)
        {
            var pool = ArrayPool<byte>.Shared;
            var tmp = pool.Rent(3 + data.Length);

            try
            {
                var mem = tmp.AsMemory(0, 3 + data.Length);
                var written = CompressBlock(data.Span, mem.Span, settings.CompressionLevel, out var used, last);

                if (written > 0 && used == data.Length)
                {
                    await baseStream.WriteAsync(mem[..written], cancellationToken).ConfigureAwait(false);
                    hash.Update(data.Span);
                    return;
                }
            }
            finally
            {
                pool.Return(tmp, clearArray: false);
            }
        }

        WriteBlockHeaderTo(bhScratch!, blockType: 0, blockSize: data.Length, last: last);
        await baseStream.WriteAsync(bhScratch.AsMemory(0, 3), cancellationToken).ConfigureAwait(false);
        await baseStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        hash.Update(data.Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushTail(bool last)
    {
        WriteOneBlock(tail!.AsSpan(0, tailLength), last);
        tailLength = 0;
    }

    private ValueTask FlushTailAsync(bool last, CancellationToken cancellationToken) => WriteOneBlockAsync(tail!.AsMemory(0, tailLength), last, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBlockHeader(int blockType, int blockSize, bool last)
    {
        // Ограничение размера блока строгое
        if ((uint)blockSize > (uint)settings.BlockCap) throw new InvalidDataException("Block_Size превышает Block_Maximum_Size");

        var header = (uint)((blockSize << 3) | (blockType << 1) | (last ? 1 : 0));
        Span<byte> h = [(byte)(header & 0xFF), (byte)((header >> 8) & 0xFF), (byte)((header >> 16) & 0xFF)];
        baseStream.Write(h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBlockHeaderTo(byte[] scratch3, int blockType, int blockSize, bool last)
    {
        if ((uint)blockSize > (uint)settings.BlockCap) throw new InvalidDataException("Block_Size превышает Block_Maximum_Size");

        var header = (uint)((blockSize << 3) | (blockType << 1) | (last ? 1 : 0));
        scratch3[0] = (byte)(header & 0xFF);
        scratch3[1] = (byte)((header >> 8) & 0xFF);
        scratch3[2] = (byte)((header >> 16) & 0xFF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureHeaderAsyncScratch()
    {
        hdrScratch ??= GC.AllocateUninitializedArray<byte>(14);
        bhScratch ??= GC.AllocateUninitializedArray<byte>(3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RentTailIfNeeded()
    {
        if (tail is null || tail.Length < settings.BlockCap)
            tail = ArrayPool<byte>.Shared.Rent(settings.BlockCap);
    }

    /// <summary>
    /// Запись несжатых данных. Энкодер сам решит, писать RAW либо RLE-блок.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> src)
    {
        EnsureHeader();

        if (src.Length == 0) return;

        // Если есть ранее отложенный «хвост», а сейчас пришли новые данные — тот хвост уже точно НЕ последний.
        // Сбрасываем его как обычный (Last=0).
        if (tailLength > 0)
        {
            FlushTail(last: false);
            tailLength = 0;
        }

        // Основной поток: пишем полные блоки напрямую, остаток удерживаем как «хвост».
        var blockCap = settings.BlockCap;

        while (src.Length > blockCap)
        {
            var chunk = src[..blockCap];
            WriteOneBlock(chunk, last: false);
            src = src[blockCap..];
        }

        // Остаток (<= blockCap) — это кандидат на последний блок: удерживаем во «хвосте».
        if (src.Length > 0)
        {
            RentTailIfNeeded();
            src.CopyTo(tail!);
            tailLength = src.Length;
        }
    }

    /// <summary>
    /// Запись несжатых данных. Энкодер сам решит, писать RAW либо RLE-блок.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> src, CancellationToken cancellationToken = default)
    {
        EnsureHeaderAsyncScratch(); // готовим scratch для async
        EnsureHeader();

        if (src.Length == 0) return;

        if (tailLength > 0)
        {
            await FlushTailAsync(last: false, cancellationToken).ConfigureAwait(false);
            tailLength = 0;
        }

        var blockCap = settings.BlockCap;
        var mem = src;

        while (mem.Length > blockCap)
        {
            var chunk = mem[..blockCap];
            await WriteOneBlockAsync(chunk, last: false, cancellationToken).ConfigureAwait(false);
            mem = mem[blockCap..];
        }

        if (mem.Length > 0)
        {
            RentTailIfNeeded();
            mem.Span.CopyTo(tail!);
            tailLength = mem.Length;
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        // Гарантируем наличие FHD даже при пустом вводе.
        EnsureHeader();

        // Если данных не было вообще — мы обязаны выдать хотя бы один блок (RFC: «Each frame must have at least 1 block»).
        if (tailLength == 0)
        {
            // Пишем пустой RAW‑блок как последний.
            WriteBlockHeader(blockType: 0 /*RAW*/, blockSize: 0, last: true);
        }
        else
        {
            FlushTail(last: true);
        }

        if (useChecksum)
        {
            var h = hash.Digest();
            Span<byte> c = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(c, (uint)h); // LE
            baseStream.Write(c);
        }

        if (tail is not null) ArrayPool<byte>.Shared.Return(tail, clearArray: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDetectRle(ReadOnlySpan<byte> src, out byte value)
    {
        if (src.Length == 0)
        {
            value = 0;
            return true;
        }

        value = src[0];
        // Быстрый линейный проход; можно ускорить SIMD, но это уже микропрофайлингом.
        for (var i = 1; i < src.Length; i++)
            if (src[i] != value) return false;

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
        else
        {
            if (size <= (256UL + 0xFFFF)) return 2; // при SS=0 вариант 1 байт не используется
            if (size <= 0xFFFFFFFFUL) return 4;
            return 8;
        }
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
        // Пустые литералы — 1 байт заголовка, RAW, size=0
        if (lits.IsEmpty) { dst[0] = 0; return 1; }

        // Проверяем RLE (все байты равны)
        var isRle = true;
        var b0 = lits[0];

        for (var i = 1; i < lits.Length; i++)
        {
            if (lits[i] != b0)
            {
                isRle = false;
                break;
            }
        }

        if (isRle)
        {
            var sz = lits.Length;
            // type=RLE = 01b
            if (sz <= 31)
            {
                // [00..01|size(5)]
                dst[0] = (byte)(1 | (sz << 2));
                dst[1] = b0; // 1 байт значения
                return 2;
            }
            else if (sz <= 4095)
            {
                // [00..01|10|size(12)]
                var val = 1 | (2 << 2) | (sz << 4);
                dst[0] = (byte)val;
                dst[1] = (byte)(val >> 8);
                dst[2] = b0; // 1 байт значения
                return 3;
            }
            else
            {
                // длинный: [00..01|11|size(20)] -> ровно 3 байта заголовка
                var val = 1 | (3 << 2) | ((sz & 0xFFFFF) << 4);
                dst[0] = (byte)val;
                dst[1] = (byte)(val >> 8);
                dst[2] = (byte)(val >> 16);   // << ВАЖНО: только 3 байта заголовка
                dst[3] = b0;                  // 1 байт значения
                return 4;
            }
        }
        else
        {
            var sz = lits.Length;
            // type=RAW = 00b
            if (sz <= 31)
            {
                dst[0] = (byte)(sz << 2);
                // тело идёт сразу после 1‑го байта заголовка
                lits.CopyTo(dst[1..]);
                return 1 + sz;
            }
            else if (sz <= 4095)
            {
                var val = (2 << 2) | (sz << 4);
                dst[0] = (byte)val;
                dst[1] = (byte)(val >> 8);
                // тело после 2 байт заголовка
                lits.CopyTo(dst[2..]);
                return 2 + sz;
            }
            else
            {
                // длинный RAW: 3‑байтовый заголовок (20‑битный размер)
                var val = (3 << 2) | ((sz & 0xFFFFF) << 4);
                dst[0] = (byte)val;
                dst[1] = (byte)(val >> 8);
                dst[2] = (byte)(val >> 16);   // << только 3 байта заголовка
                                              // тело после 3 байт
                lits.CopyTo(dst[3..]);
                return 3 + sz;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ZstdMatchParams GetParamsForLevel(int level)
    {
        // Упрощённая (но практичная) сетка: быстрые уровни 1..5, средние 6..12, высокие 13..19, экстремум 20..22
        // Подбиралось для низкой когнитивной сложности и предсказуемой нагрузки.
        if (level <= 1) return new ZstdMatchParams(windowLog: 20, hashLog: 18, searchDepth: 2, targetLength: 24);
        if (level <= 5) return new ZstdMatchParams(windowLog: 22, hashLog: 19, searchDepth: 4, targetLength: 32);
        if (level <= 9) return new ZstdMatchParams(windowLog: 24, hashLog: 20, searchDepth: 6, targetLength: 48);
        if (level <= 15) return new ZstdMatchParams(windowLog: 25, hashLog: 21, searchDepth: 12, targetLength: 64);
        if (level <= 19) return new ZstdMatchParams(windowLog: 26, hashLog: 22, searchDepth: 20, targetLength: 96);
        // 20..22
        return new ZstdMatchParams(windowLog: 27, hashLog: 23, searchDepth: 32, targetLength: 128);
    }

    /// <summary>
    /// Сжать один блок src в dst как Compressed Block.
    /// Возвращает размер блока (включая 3-байтовый заголовок), или 0 если выгоднее RAW/RLE (в этом случае вызывающий пусть запишет RAW/RLE).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompressBlock(ReadOnlySpan<byte> src, Span<byte> dst, int compressionLevel, out int decompressedSizeUsed, bool isLast)
    {
        decompressedSizeUsed = 0;
        if (src.Length == 0) return 0;

        var p = GetParamsForLevel(compressionLevel);

        // Ограничим содержимое блока 128K (правило формата)
        var blockMax = Math.Min(src.Length, 128 * 1024);
        var block = src[..blockMax];

        // Массив команд и буфер литералов (верхняя оценка: в худшем случае — все байты как литералы)
        // Но важно: литералы в "compressed block" — это только литералы до матчей; хвост блока не включаем.
        var seqArray = ArrayPool<ZstdSeq>.Shared.Rent((blockMax / 4) + 16); // эвристика
        var litArray = ArrayPool<byte>.Shared.Rent(blockMax);
        var seqs = seqArray.AsSpan();
        var literals = litArray.AsSpan();

        try
        {

            var (seqCount, litSize, consumed) = ZstdMatcher.BuildSequences(block, seqs, literals, p);

            // Нет последовательностей => этот блок лучше оформить как "только литералы" (nbSeq=0).
            // Т.к. у нас есть полноценный путь RAW/RLE, возвращаем 0 — пусть вызывающий выберет RAW/RLE.
            if (seqCount == 0) return 0;

            // Собираем Compressed Block:
            // [Block Header(3)] [Literals_Section] [Sequences_Section]
            var body = dst[3..];

            // ---- Literals_Section: выберем Raw или RLE, что компактнее
            var litHeaderAndData = WriteLiteralsSection(literals[..litSize], body);

            // ---- Sequences_Section:
            var seqSectionSize = ZstdSeqEncoder.WriteSequences(seqs[..seqCount], body[litHeaderAndData..]);

            var blockSize = litHeaderAndData + seqSectionSize;

            // Правило: Compressed Size < Decompressed Size (иначе бессмысленно)
            if (blockSize >= consumed) return 0; // пусть вызывающий запишет RAW

            // Записать заголовок блока (3 байта, LE): Last=0 здесь не знаем, ставит вызывающий снаружи; BlockType=2 (Compressed), Size=blockSize
            WriteBlockHeaderCompressed(dst, isLast, blockSize);

            decompressedSizeUsed = consumed;
            return 3 + blockSize;
        }
        finally
        {
            ArrayPool<ZstdSeq>.Shared.Return(seqArray, clearArray: true);
            ArrayPool<byte>.Shared.Return(litArray, clearArray: false);
        }
    }
}