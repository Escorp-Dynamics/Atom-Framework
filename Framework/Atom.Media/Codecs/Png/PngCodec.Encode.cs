#pragma warning disable IDE0010, IDE0047, IDE0048, S109, S3776, MA0051, CS0219, S1481, S3358

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// Кодирование PNG.
/// </summary>
public sealed partial class PngCodec
{
    #region Encode

    /// <inheritdoc/>
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        bytesWritten = 0;
        var startTimestamp = Stopwatch.GetTimestamp();

        var width = frame.Width;
        var height = frame.Height;

        Logger?.LogPngEncodeStart(width, height);

        if (!isEncoder || !isInitialized)
        {
            Logger?.LogPngError("Кодек не инициализирован как encoder");
            return CodecResult.UnsupportedFormat;
        }

        var bytesPerPixel = frame.PixelFormat == VideoPixelFormat.Rgba32 ? 4 : 3;
        var colorType = bytesPerPixel == 4 ? ColorTypeRgba : ColorTypeRgb;

        // Минимальный размер: signature + IHDR + IDAT + IEND
        var ihdrLen = ChunkHeaderSize + IhdrDataSize + 4;
        var idatLen = ChunkHeaderSize + 4;
        var iendLen = ChunkHeaderSize + 4;
        var minSize = SignatureSize + ihdrLen + idatLen + iendLen;
        if (output.Length < minSize)
        {
            Logger?.LogPngError("Выходной буфер слишком мал");
            return CodecResult.OutputBufferTooSmall;
        }

        var result = EncodeInternal(frame.PackedData, output, width, height, bytesPerPixel, colorType, Parameters.CompressionLevel, Parameters.FastFiltering, out bytesWritten);

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (result == CodecResult.Success)
        {
            Logger?.LogPngEncodeComplete(width, height, bytesWritten, elapsedMs);
        }

        return result;
    }

    /// <inheritdoc/>
    public ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        VideoFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        var roFrame = frame.AsReadOnlyFrame();
        var result = Encode(roFrame, output.Span, out var written);
        return new ValueTask<(CodecResult, int)>((result, written));
    }

    #endregion

    #region Internal Encoding

    /// <summary>
    /// Внутренняя реализация кодирования PNG.
    /// </summary>
    private static CodecResult EncodeInternal(
        ReadOnlyPlane<byte> sourceData, Span<byte> output,
        int width, int height, int bytesPerPixel, byte colorType, int compressionLevel, bool fastFiltering, out int bytesWritten)
    {
        bytesWritten = 0;
        var offset = 0;

        // PNG сигнатура
        PngSignature.CopyTo(output);
        offset += SignatureSize;

        // IHDR чанк
        offset += WriteIhdr(output[offset..], width, height, colorType);

        // Подготавливаем данные с PNG фильтрами
        var rowBytes = width * bytesPerPixel;
        var filteredSize = (rowBytes + 1) * height;

        // Для Store mode (level=0) пишем напрямую в output без промежуточных буферов
        if (compressionLevel == 0 && fastFiltering)
        {
            return EncodeStoreFast(sourceData, output, ref offset, width, height, bytesPerPixel, out bytesWritten);
        }

        var filteredData = ArrayPool<byte>.Shared.Rent(filteredSize);

        try
        {
            // Применяем фильтры (адаптивные или быстрые)
            if (fastFiltering)
            {
                ApplyFastFilters(sourceData, filteredData.AsSpan(0, filteredSize), width, height, bytesPerPixel);
            }
            else
            {
                ApplyAdaptiveFilters(sourceData, filteredData.AsSpan(0, filteredSize), width, height, bytesPerPixel);
            }

            // Сжимаем данные с учётом уровня сжатия из Parameters
            var compressedData = CompressZlib(filteredData.AsSpan(0, filteredSize), compressionLevel);

            var requiredLen = offset + ChunkHeaderSize + compressedData.Length + 4 + ChunkHeaderSize + 4;
            if (output.Length < requiredLen)
            {
                return CodecResult.OutputBufferTooSmall;
            }

            offset += WriteChunk(output[offset..], ChunkIdat, compressedData);
            offset += WriteChunk(output[offset..], ChunkIend, []);

            bytesWritten = offset;
            return CodecResult.Success;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(filteredData);
        }
    }

    /// <summary>
    /// Максимально быстрое кодирование PNG — Store mode без промежуточных буферов.
    /// Использует Filter None для минимального времени обработки.
    /// Пишет напрямую в output: IDAT chunk с ZLIB Store блоками.
    /// CRC32 вычисляется инкрементально — один проход по output данным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe CodecResult EncodeStoreFast(
        ReadOnlyPlane<byte> sourceData, Span<byte> output, ref int offset,
        int width, int height, int bytesPerPixel, out int bytesWritten)
    {
        bytesWritten = 0;
        var rowBytes = width * bytesPerPixel;
        var filteredRowSize = rowBytes + 1; // +1 для filter byte

        // Каждая строка = один Store блок (filteredRowSize <= 65535 для разрешений до ~16K)
        // Store block: header(1) + len(2) + nlen(2) + data = 5 + filteredRowSize
        var numBlocks = height;
        var zlibDataSize = 2 + (numBlocks * 5) + (filteredRowSize * height) + 4;

        // Проверяем размер output
        var requiredLen = offset + ChunkHeaderSize + zlibDataSize + 4 + ChunkHeaderSize + 4;
        if (output.Length < requiredLen)
        {
            return CodecResult.OutputBufferTooSmall;
        }

        // Записываем IDAT chunk header
        var idatStart = offset;
        BinaryPrimitives.WriteInt32BigEndian(output[offset..], zlibDataSize);
        offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(output[offset..], ChunkIdat);
        offset += 4;

        // Предвычисляем константы для Store block headers
        var lenLo = (byte)(filteredRowSize & 0xFF);
        var lenHi = (byte)((filteredRowSize >> 8) & 0xFF);
        var nlen = (ushort)~filteredRowSize;
        var nlenLo = (byte)(nlen & 0xFF);
        var nlenHi = (byte)((nlen >> 8) & 0xFF);

        // ADLER32 state
        const uint modAdler = 65521;
        uint adlerA = 1, adlerB = 0;

        // Pre-compute SIMD constants outside the loop
        var ones256 = Vector256.Create((short)1);
        var ones128 = Vector128.Create((short)1);

        fixed (byte* pOutput = output)
        fixed (uint* pCrcTable = Crc32Table)
        {
            var pDst = pOutput + offset;

            // ZLIB header в output
            *pDst++ = 0x78; // CMF
            *pDst++ = 0x01; // FLG (no compression)

            // Записываем все store блоки максимально быстро
            for (var y = 0; y < height; y++)
            {
                var srcRow = sourceData.GetRow(y);
                var isLast = y == height - 1;

                // Store block header (5 bytes) — прямая запись
                *pDst++ = isLast ? (byte)0x01 : (byte)0x00;
                *pDst++ = lenLo;
                *pDst++ = lenHi;
                *pDst++ = nlenLo;
                *pDst++ = nlenHi;

                // Filter byte (None = 0)
                *pDst++ = FilterNone;
                adlerB += adlerA; // filter byte = 0, только B меняется

                // Копируем данные строки + ADLER32 inline — совмещаем memcpy и checksum в один проход
                fixed (byte* pSrc = srcRow)
                {
                    var i = 0;

                    if (Avx2.IsSupported)
                    {
                        var end32 = rowBytes - 31;

                        for (; i < end32; i += 32)
                        {
                            // Загружаем и записываем
                            var data = Avx.LoadVector256(pSrc + i);
                            Avx.Store(pDst + i, data);

                            // ADLER32 вычисление
                            var lo = Avx2.UnpackLow(data.AsByte(), Vector256<byte>.Zero);
                            var hi = Avx2.UnpackHigh(data.AsByte(), Vector256<byte>.Zero);
                            var sum16 = Avx2.Add(lo.AsInt16(), hi.AsInt16());
                            var sum32 = Avx2.MultiplyAddAdjacent(sum16, ones256);
                            var sum64 = Avx2.Add(sum32, Avx2.Shuffle(sum32, 0b10_11_00_01));
                            var combined = Sse2.Add(sum64.GetLower(), sum64.GetUpper());
                            var final = Sse2.Add(combined, Sse2.Shuffle(combined, 0b01_00_11_10));
                            var blockSum = (uint)final.GetElement(0);

                            adlerB += (adlerA * 32) + (blockSum * 16);
                            adlerA += blockSum;
                        }
                    }
                    else if (Sse2.IsSupported)
                    {
                        var end16 = rowBytes - 15;

                        for (; i < end16; i += 16)
                        {
                            var data = Sse2.LoadVector128(pSrc + i);
                            Sse2.Store(pDst + i, data);

                            var lo = Sse2.UnpackLow(data.AsByte(), Vector128<byte>.Zero);
                            var hi = Sse2.UnpackHigh(data.AsByte(), Vector128<byte>.Zero);
                            var sum16 = Sse2.Add(lo.AsInt16(), hi.AsInt16());
                            var sum32 = Sse2.MultiplyAddAdjacent(sum16, ones128);
                            var sum64 = Sse2.Add(sum32, Sse2.Shuffle(sum32, 0b01_00_11_10));
                            var final = Sse2.Add(sum64, Sse2.Shuffle(sum64, 0b10_11_00_01));
                            var blockSum = (uint)final.GetElement(0);

                            adlerB += (adlerA * 16) + (blockSum * 8);
                            adlerA += blockSum;
                        }
                    }

                    // Scalar tail
                    for (; i < rowBytes; i++)
                    {
                        var b = pSrc[i];
                        pDst[i] = b;
                        adlerA += b;
                        adlerB += adlerA;
                    }
                }

                pDst += rowBytes;

                // Modulo каждые 4 строки
                if ((y & 3) == 0)
                {
                    adlerA %= modAdler;
                    adlerB %= modAdler;
                }
            }

            offset = (int)(pDst - pOutput);
        }

        // Финальный ADLER32
        adlerA %= modAdler;
        adlerB %= modAdler;
        var adler32 = (adlerB << 16) | adlerA;
        BinaryPrimitives.WriteUInt32BigEndian(output[offset..], adler32);
        offset += 4;

        // CRC32 для IDAT chunk (type + data) — один проход в конце
        var crc = CalculateCrc32(output.Slice(idatStart + 4, 4 + zlibDataSize));
        BinaryPrimitives.WriteUInt32BigEndian(output[offset..], crc);
        offset += 4;

        // IEND chunk
        offset += WriteChunk(output[offset..], ChunkIend, []);

        bytesWritten = offset;
        return CodecResult.Success;
    }

    /// <summary>
    /// Inline CRC32 update для одного байта.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint UpdateCrc32Inline(uint crc, byte b) => Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

    /// <summary>
    /// Вычисляет ADLER32 для данных внутри Store блоков (пропуская 5-байтовые заголовки).
    /// Оптимизированная версия с отложенным modulo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateAdler32ForStoreBlocks(ReadOnlySpan<byte> storeData, int rowSize, int height)
    {
        // NMAX = 5552 — максимум байт без переполнения uint при вычислении ADLER32
        const int nmax = 5552;
        const uint modAdler = 65521;

        uint a = 1, b = 0;

        fixed (byte* pBase = storeData)
        {
            var offset = 0;
            var pending = 0; // Сколько байт обработано без modulo

            for (var y = 0; y < height; y++)
            {
                // Пропускаем заголовок store блока (5 байт)
                offset += 5;

                var ptr = pBase + offset;
                var rowRemaining = rowSize;

                while (rowRemaining > 0)
                {
                    // Сколько можем обработать до следующего modulo
                    var canProcess = nmax - pending;
                    if (canProcess <= 0)
                    {
                        a %= modAdler;
                        b %= modAdler;
                        pending = 0;
                        canProcess = nmax;
                    }

                    var chunk = rowRemaining < canProcess ? rowRemaining : canProcess;

                    // Развёрнутый цикл по 16 байт
                    var end16 = chunk - 15;
                    var i = 0;
                    for (; i < end16; i += 16)
                    {
                        a += ptr[i]; b += a;
                        a += ptr[i + 1]; b += a;
                        a += ptr[i + 2]; b += a;
                        a += ptr[i + 3]; b += a;
                        a += ptr[i + 4]; b += a;
                        a += ptr[i + 5]; b += a;
                        a += ptr[i + 6]; b += a;
                        a += ptr[i + 7]; b += a;
                        a += ptr[i + 8]; b += a;
                        a += ptr[i + 9]; b += a;
                        a += ptr[i + 10]; b += a;
                        a += ptr[i + 11]; b += a;
                        a += ptr[i + 12]; b += a;
                        a += ptr[i + 13]; b += a;
                        a += ptr[i + 14]; b += a;
                        a += ptr[i + 15]; b += a;
                    }

                    // Остаток
                    for (; i < chunk; i++)
                    {
                        a += ptr[i];
                        b += a;
                    }

                    ptr += chunk;
                    rowRemaining -= chunk;
                    pending += chunk;
                }

                offset += rowSize;
            }
        }

        a %= modAdler;
        b %= modAdler;
        return (b << 16) | a;
    }

    /// <summary>
    /// Записывает IHDR чанк.
    /// </summary>
    private static int WriteIhdr(Span<byte> output, int width, int height, byte colorType)
    {
        Span<byte> ihdrData = stackalloc byte[IhdrDataSize];
        BinaryPrimitives.WriteInt32BigEndian(ihdrData, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdrData[4..], height);
        ihdrData[8] = 8; // bit depth
        ihdrData[9] = colorType;
        ihdrData[10] = 0; // compression
        ihdrData[11] = 0; // filter
        ihdrData[12] = 0; // interlace

        return WriteChunk(output, ChunkIhdr, ihdrData);
    }

    /// <summary>
    /// Записывает PNG чанк с CRC.
    /// </summary>
    private static int WriteChunk(Span<byte> output, uint chunkType, ReadOnlySpan<byte> data)
    {
        var chunkSize = ChunkHeaderSize + data.Length + 4;
        BinaryPrimitives.WriteInt32BigEndian(output, data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(output[4..], chunkType);

        if (!data.IsEmpty)
        {
            data.CopyTo(output[ChunkHeaderSize..]);
        }

        // CRC32 вычисляется для type + data
        var crc = CalculateCrc32(output.Slice(4, 4 + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(output[(ChunkHeaderSize + data.Length)..], crc);

        return chunkSize;
    }

    #endregion

    #region Filters for Encoding

    /// <summary>
    /// Применяет адаптивную фильтрацию — выбирает лучший фильтр для каждой строки.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ApplyAdaptiveFilters(ReadOnlyPlane<byte> source, Span<byte> filtered, int width, int height, int bytesPerPixel)
    {
        var rowBytes = width * bytesPerPixel;
        var filteredRowSize = rowBytes + 1;

        // Буфера для тестирования разных фильтров (5 фильтров)
        var testBuffers = stackalloc byte[5 * rowBytes];
        var prevRowBuffer = stackalloc byte[rowBytes];

        // Инициализируем prevRow нулями
        new Span<byte>(prevRowBuffer, rowBytes).Clear();

        fixed (byte* pFiltered = filtered)
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = source.GetRow(y);

                fixed (byte* pSrc = srcRow)
                {
                    var pDst = pFiltered + (y * filteredRowSize);

                    // Выбираем лучший фильтр
                    var bestFilter = SelectBestFilterFast(pSrc, prevRowBuffer, testBuffers, rowBytes, bytesPerPixel);

                    // Записываем filter byte и применяем фильтр
                    *pDst = bestFilter;
                    ApplyFilterFast(pSrc, prevRowBuffer, pDst + 1, rowBytes, bestFilter, bytesPerPixel);

                    // Копируем текущую строку в prevRow
                    Buffer.MemoryCopy(pSrc, prevRowBuffer, rowBytes, rowBytes);
                }
            }
        }
    }

    /// <summary>
    /// Применяет быструю фильтрацию — фиксированный Sub фильтр с SIMD.
    /// Быстрее адаптивной на 5-10x, но может давать чуть больший размер файла.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ApplyFastFilters(ReadOnlyPlane<byte> source, Span<byte> filtered, int width, int height, int bytesPerPixel)
    {
        var rowBytes = width * bytesPerPixel;
        var filteredRowSize = rowBytes + 1;

        fixed (byte* pFiltered = filtered)
        {
            // Используем Sub фильтр для всех строк (хорошо сжимается, простой в SIMD)
            for (var y = 0; y < height; y++)
            {
                var srcRow = source.GetRow(y);

                fixed (byte* pSrc = srcRow)
                {
                    var pDst = pFiltered + (y * filteredRowSize);

                    // Filter type = Sub
                    *pDst = FilterSub;

                    // SIMD Sub фильтр
                    ApplySubFilterSimd(pSrc, pDst + 1, rowBytes, bytesPerPixel);
                }
            }
        }
    }

    /// <summary>
    /// SIMD Sub фильтр для encode — использует AVX2/SSE2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ApplySubFilterSimd(byte* src, byte* dst, int length, int bpp)
    {
        // Первые bpp байт копируем как есть
        for (var i = 0; i < bpp; i++)
        {
            dst[i] = src[i];
        }

        var i2 = bpp;

        if (Avx2.IsSupported && length >= bpp + 32)
        {
            // AVX2: 32 байта за итерацию
            var end32 = length - 31;
            for (; i2 < end32; i2 += 32)
            {
                var curr = Avx.LoadVector256(src + i2);
                var prev = Avx.LoadVector256(src + i2 - bpp);
                var result = Avx2.Subtract(curr, prev);
                Avx.Store(dst + i2, result);
            }
        }
        else if (Sse2.IsSupported && length >= bpp + 16)
        {
            // SSE2: 16 байт за итерацию
            var end16 = length - 15;
            for (; i2 < end16; i2 += 16)
            {
                var curr = Sse2.LoadVector128(src + i2);
                var prev = Sse2.LoadVector128(src + i2 - bpp);
                var result = Sse2.Subtract(curr, prev);
                Sse2.Store(dst + i2, result);
            }
        }

        // Scalar остаток с развёрткой
        var end4 = length - 3;
        for (; i2 < end4; i2 += 4)
        {
            dst[i2] = (byte)(src[i2] - src[i2 - bpp]);
            dst[i2 + 1] = (byte)(src[i2 + 1] - src[i2 + 1 - bpp]);
            dst[i2 + 2] = (byte)(src[i2 + 2] - src[i2 + 2 - bpp]);
            dst[i2 + 3] = (byte)(src[i2 + 3] - src[i2 + 3 - bpp]);
        }

        for (; i2 < length; i2++)
        {
            dst[i2] = (byte)(src[i2] - src[i2 - bpp]);
        }
    }

    /// <summary>
    /// Выбирает лучший фильтр на основе эвристики "минимальная сумма".
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe byte SelectBestFilterFast(byte* src, byte* prev, byte* testBuffers, int length, int bpp)
    {
        var bestFilter = (byte)0;
        var bestSum = long.MaxValue;

        // Тестируем все 5 фильтров
        for (byte f = 0; f <= 4; f++)
        {
            var testBuf = testBuffers + (f * length);
            ApplyFilterFast(src, prev, testBuf, length, f, bpp);
            var sum = CalculateFilterSumFast(testBuf, length);

            if (sum < bestSum)
            {
                bestSum = sum;
                bestFilter = f;
            }
        }

        return bestFilter;
    }

    /// <summary>
    /// Применяет указанный фильтр к строке.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ApplyFilterFast(byte* src, byte* prev, byte* dst, int length, byte filterType, int bpp)
    {
        switch (filterType)
        {
            case FilterNone:
                Buffer.MemoryCopy(src, dst, length, length);
                break;

            case FilterSub:
                ApplySubFilterFast(src, dst, length, bpp);
                break;

            case FilterUp:
                ApplyUpFilterFast(src, prev, dst, length);
                break;

            case FilterAverage:
                ApplyAverageFilterFast(src, prev, dst, length, bpp);
                break;

            case FilterPaeth:
                ApplyPaethFilterFast(src, prev, dst, length, bpp);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ApplySubFilterFast(byte* src, byte* dst, int length, int bpp)
    {
        // Первые bpp байт — без left
        var i = 0;
        for (; i < bpp; i++)
        {
            dst[i] = src[i];
        }

        // Остальные — с зависимостью от left
        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] - src[i - bpp]);
            dst[i + 1] = (byte)(src[i + 1] - src[i + 1 - bpp]);
            dst[i + 2] = (byte)(src[i + 2] - src[i + 2 - bpp]);
            dst[i + 3] = (byte)(src[i + 3] - src[i + 3 - bpp]);
        }

        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] - src[i - bpp]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ApplyUpFilterFast(byte* src, byte* prev, byte* dst, int length)
    {
        var i = 0;

        // AVX2: 32 байта за итерацию
        if (Avx2.IsSupported && length >= 32)
        {
            var end32 = length - 31;
            for (; i < end32; i += 32)
            {
                var vSrc = Avx.LoadVector256(src + i);
                var vPrev = Avx.LoadVector256(prev + i);
                var result = Avx2.Subtract(vSrc, vPrev);
                Avx.Store(dst + i, result);
            }
        }
        else if (Sse2.IsSupported && length >= 16)
        {
            // SSE2: 16 байт за итерацию
            var end16 = length - 15;
            for (; i < end16; i += 16)
            {
                var vSrc = Sse2.LoadVector128(src + i);
                var vPrev = Sse2.LoadVector128(prev + i);
                var result = Sse2.Subtract(vSrc, vPrev);
                Sse2.Store(dst + i, result);
            }
        }

        // Scalar остаток
        var end8 = length - 7;
        for (; i < end8; i += 8)
        {
            dst[i] = (byte)(src[i] - prev[i]);
            dst[i + 1] = (byte)(src[i + 1] - prev[i + 1]);
            dst[i + 2] = (byte)(src[i + 2] - prev[i + 2]);
            dst[i + 3] = (byte)(src[i + 3] - prev[i + 3]);
            dst[i + 4] = (byte)(src[i + 4] - prev[i + 4]);
            dst[i + 5] = (byte)(src[i + 5] - prev[i + 5]);
            dst[i + 6] = (byte)(src[i + 6] - prev[i + 6]);
            dst[i + 7] = (byte)(src[i + 7] - prev[i + 7]);
        }

        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] - prev[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ApplyAverageFilterFast(byte* src, byte* prev, byte* dst, int length, int bpp)
    {
        var i = 0;
        for (; i < bpp; i++)
        {
            dst[i] = (byte)(src[i] - (prev[i] >> 1));
        }

        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] - ((src[i - bpp] + prev[i]) >> 1));
            dst[i + 1] = (byte)(src[i + 1] - ((src[i + 1 - bpp] + prev[i + 1]) >> 1));
            dst[i + 2] = (byte)(src[i + 2] - ((src[i + 2 - bpp] + prev[i + 2]) >> 1));
            dst[i + 3] = (byte)(src[i + 3] - ((src[i + 3 - bpp] + prev[i + 3]) >> 1));
        }

        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] - ((src[i - bpp] + prev[i]) >> 1));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ApplyPaethFilterFast(byte* src, byte* prev, byte* dst, int length, int bpp)
    {
        var i = 0;
        for (; i < bpp; i++)
        {
            // PaethPredictor(0, prev[i], 0) = prev[i]
            dst[i] = (byte)(src[i] - prev[i]);
        }

        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] - PaethPredictorFast(src[i - bpp], prev[i], prev[i - bpp]));
            dst[i + 1] = (byte)(src[i + 1] - PaethPredictorFast(src[i + 1 - bpp], prev[i + 1], prev[i + 1 - bpp]));
            dst[i + 2] = (byte)(src[i + 2] - PaethPredictorFast(src[i + 2 - bpp], prev[i + 2], prev[i + 2 - bpp]));
            dst[i + 3] = (byte)(src[i + 3] - PaethPredictorFast(src[i + 3 - bpp], prev[i + 3], prev[i + 3 - bpp]));
        }

        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] - PaethPredictorFast(src[i - bpp], prev[i], prev[i - bpp]));
        }
    }

    /// <summary>
    /// Вычисляет сумму абсолютных значений для эвристики выбора фильтра — SIMD оптимизация.
    /// Интерпретирует байты как signed для подсчёта "сложности" строки.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe long CalculateFilterSumFast(byte* data, int length)
    {
        var sum = 0L;
        var i = 0;

        // AVX2: 32 байта за итерацию с SAD
        if (Avx2.IsSupported && length >= 32)
        {
            var accumulator = Vector256<long>.Zero;
            var mid128 = Vector256.Create((byte)128);
            var end32 = length - 31;

            for (; i < end32; i += 32)
            {
                var v = Avx.LoadVector256(data + i);

                // Вычисляем "расстояние" от 128 (эквивалент |signed|)
                // |x - 128| для unsigned эквивалентно min(x, 256-x) для x∈[0,255]
                var sad = Avx2.SumAbsoluteDifferences(v, mid128);
                accumulator = Avx2.Add(accumulator, sad.AsInt64());
            }

            // Horizontal sum
            var lower = accumulator.GetLower();
            var upper = accumulator.GetUpper();
            var sumVec = Sse2.Add(lower, upper);
            sum = Sse2.X64.ConvertToInt64(sumVec) + Sse2.X64.ConvertToInt64(Sse2.Shuffle(sumVec.AsDouble(), sumVec.AsDouble(), 1).AsInt64());
        }
        else if (Sse2.IsSupported && length >= 16)
        {
            // SSE2: 16 байт за итерацию
            var accumulator = Vector128<long>.Zero;
            var mid128 = Vector128.Create((byte)128);
            var end16 = length - 15;

            for (; i < end16; i += 16)
            {
                var v = Sse2.LoadVector128(data + i);
                var sad = Sse2.SumAbsoluteDifferences(v, mid128);
                accumulator = Sse2.Add(accumulator, sad.AsInt64());
            }

            sum = Sse2.X64.ConvertToInt64(accumulator) + Sse2.X64.ConvertToInt64(Sse2.Shuffle(accumulator.AsDouble(), accumulator.AsDouble(), 1).AsInt64());
        }

        // Scalar остаток с развёрткой по 8
        var end8 = length - 7;
        for (; i < end8; i += 8)
        {
            sum += data[i] <= 127 ? data[i] : 256 - data[i];
            sum += data[i + 1] <= 127 ? data[i + 1] : 256 - data[i + 1];
            sum += data[i + 2] <= 127 ? data[i + 2] : 256 - data[i + 2];
            sum += data[i + 3] <= 127 ? data[i + 3] : 256 - data[i + 3];
            sum += data[i + 4] <= 127 ? data[i + 4] : 256 - data[i + 4];
            sum += data[i + 5] <= 127 ? data[i + 5] : 256 - data[i + 5];
            sum += data[i + 6] <= 127 ? data[i + 6] : 256 - data[i + 6];
            sum += data[i + 7] <= 127 ? data[i + 7] : 256 - data[i + 7];
        }

        for (; i < length; i++)
        {
            sum += data[i] <= 127 ? data[i] : 256 - data[i];
        }

        return sum;
    }

    /// <summary>
    /// PNG Paeth predictor — fast версия для encode.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictorFast(int a, int b, int c)
    {
        var p = a + b - c;

        var pa = p - a;
        pa = (pa ^ (pa >> 31)) - (pa >> 31);

        var pb = p - b;
        pb = (pb ^ (pb >> 31)) - (pb >> 31);

        var pc = p - c;
        pc = (pc ^ (pc >> 31)) - (pc >> 31);

        if (pa <= pb && pa <= pc) return (byte)a;
        return pb <= pc ? (byte)b : (byte)c;
    }

    // Оставляем старые методы для совместимости
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte SelectBestFilter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow, Span<byte> testBuffer, int bpp)
    {
        var bestFilter = (byte)0;
        var bestSum = long.MaxValue;

        for (byte f = 0; f <= 4; f++)
        {
            ApplyFilter(row, prevRow, testBuffer, f, bpp);
            var sum = CalculateFilterSum(testBuffer);
            if (sum < bestSum)
            {
                bestSum = sum;
                bestFilter = f;
            }
        }

        return bestFilter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyFilter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow, Span<byte> output, byte filterType, int bpp)
    {
        switch (filterType)
        {
            case FilterNone:
                row.CopyTo(output);
                break;
            case FilterSub:
                for (var i = 0; i < row.Length; i++)
                {
                    output[i] = (byte)(row[i] - (i >= bpp ? row[i - bpp] : 0));
                }

                break;
            case FilterUp:
                for (var i = 0; i < row.Length; i++)
                {
                    output[i] = (byte)(row[i] - prevRow[i]);
                }

                break;
            case FilterAverage:
                for (var i = 0; i < row.Length; i++)
                {
                    output[i] = (byte)(row[i] - (((i >= bpp ? row[i - bpp] : 0) + prevRow[i]) >> 1));
                }

                break;
            case FilterPaeth:
                for (var i = 0; i < row.Length; i++)
                {
                    output[i] = (byte)(row[i] - PaethPredictor(
                        i >= bpp ? row[i - bpp] : (byte)0,
                        prevRow[i],
                        i >= bpp ? prevRow[i - bpp] : (byte)0));
                }

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CalculateFilterSum(ReadOnlySpan<byte> data)
    {
        var sum = 0L;
        foreach (var b in data)
        {
            sum += b <= 127 ? b : 256 - b;
        }

        return sum;
    }

    #endregion

    #region Compression

    /// <summary>
    /// Маппинг уровня сжатия PNG (0-9) на .NET CompressionLevel.
    /// </summary>
    /// <remarks>
    /// PNG CompressionLevel: 0 = без сжатия, 1-3 = быстрое, 4-6 = нормальное, 7-9 = максимальное.
    /// </remarks>
    private static CompressionLevel MapCompressionLevel(int level) => level switch
    {
        0 => CompressionLevel.NoCompression,
        >= 1 and <= 3 => CompressionLevel.Fastest,
        >= 4 and <= 6 => CompressionLevel.Optimal,
        _ => CompressionLevel.SmallestSize,
    };

    /// <summary>
    /// Получает ZLIB FLG байт для заданного уровня сжатия.
    /// </summary>
    private static byte GetZlibFlg(int level) => level switch
    {
        0 => 0x01,   // no compression
        >= 1 and <= 3 => 0x5E, // fast
        >= 4 and <= 6 => 0x9C, // default
        _ => 0xDA,   // best
    };

    /// <summary>
    /// Сжимает данные в формат ZLIB (заголовок + DEFLATE + ADLER32).
    /// </summary>
    /// <param name="data">Данные для сжатия.</param>
    /// <param name="compressionLevel">Уровень сжатия (0-9).</param>
    private static byte[] CompressZlib(ReadOnlySpan<byte> data, int compressionLevel)
    {
        // Level 0 = Store mode — обходим DeflateStream полностью для максимальной скорости
        if (compressionLevel == 0)
        {
            return CompressZlibStore(data);
        }

        using var output = new MemoryStream();

        // ZLIB заголовок (CMF=0x78, FLG зависит от уровня сжатия)
        output.WriteByte(0x78);
        output.WriteByte(GetZlibFlg(compressionLevel));

        // DEFLATE сжатие
        var netLevel = MapCompressionLevel(compressionLevel);
        using (var deflateStream = new DeflateStream(output, netLevel, leaveOpen: true))
        {
            deflateStream.Write(data);
        }

        // ADLER32 контрольная сумма
        var adler32 = CalculateAdler32(data);
        Span<byte> adlerBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adlerBytes, adler32);
        output.Write(adlerBytes);

        return output.ToArray();
    }

    /// <summary>
    /// Быстрое сжатие ZLIB в Store mode (без DEFLATE-сжатия).
    /// Максимальная скорость, но большой размер файла.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static byte[] CompressZlibStore(ReadOnlySpan<byte> data)
    {
        // Структура: ZLIB header (2) + DEFLATE store blocks + ADLER32 (4)
        // Каждый Store блок: 1 (header) + 2 (len) + 2 (nlen) + data (max 65535)
        const int maxBlockSize = 65535;
        var numBlocks = (data.Length + maxBlockSize - 1) / maxBlockSize;
        var outputSize = 2 + (numBlocks * 5) + data.Length + 4;

        var result = new byte[outputSize];
        var pos = 0;

        // ZLIB header (CMF=0x78, FLG=0x01 for no compression)
        result[pos++] = 0x78;
        result[pos++] = 0x01;

        // DEFLATE Store блоки
        var remaining = data.Length;
        var srcOffset = 0;

        while (remaining > 0)
        {
            var blockSize = remaining > maxBlockSize ? maxBlockSize : remaining;
            var isFinal = remaining <= maxBlockSize;

            // Store block header: BFINAL (1 bit) + BTYPE=00 (2 bits) = 0x00 или 0x01
            result[pos++] = isFinal ? (byte)0x01 : (byte)0x00;

            // LEN (2 bytes, little-endian)
            result[pos++] = (byte)(blockSize & 0xFF);
            result[pos++] = (byte)((blockSize >> 8) & 0xFF);

            // NLEN (one's complement of LEN)
            var nlen = (ushort)~blockSize;
            result[pos++] = (byte)(nlen & 0xFF);
            result[pos++] = (byte)((nlen >> 8) & 0xFF);

            // Data
            data.Slice(srcOffset, blockSize).CopyTo(result.AsSpan(pos));
            pos += blockSize;

            srcOffset += blockSize;
            remaining -= blockSize;
        }

        // ADLER32
        var adler32 = CalculateAdler32(data);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(pos), adler32);

        return result;
    }

    /// <summary>
    /// Вычисляет ADLER32 контрольную сумму.
    /// Примечание: SIMD реализации (SSE2/AVX2) требуют доработки — используем скаляр.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateAdler32(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return 1;
        }

        fixed (byte* pData = data)
        {
            // SIMD реализации (SSE2/AVX2) дают неправильные результаты —
            // используем только скалярную реализацию
            return CalculateAdler32Scalar(pData, data.Length);
        }
    }

    /// <summary>
    /// Скалярная реализация ADLER32 — развёрнутая.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateAdler32Scalar(byte* data, int length)
    {
        const uint modAdler = 65521;
        uint a = 1, b = 0;

        // Блоками по 5552 байт (максимум без переполнения)
        const int nmax = 5552;
        var remaining = length;
        var ptr = data;

        while (remaining > 0)
        {
            var n = remaining > nmax ? nmax : remaining;
            var end16 = n - 15;
            var i = 0;

            // Развёртка по 16
            for (; i < end16; i += 16)
            {
                a += ptr[i]; b += a;
                a += ptr[i + 1]; b += a;
                a += ptr[i + 2]; b += a;
                a += ptr[i + 3]; b += a;
                a += ptr[i + 4]; b += a;
                a += ptr[i + 5]; b += a;
                a += ptr[i + 6]; b += a;
                a += ptr[i + 7]; b += a;
                a += ptr[i + 8]; b += a;
                a += ptr[i + 9]; b += a;
                a += ptr[i + 10]; b += a;
                a += ptr[i + 11]; b += a;
                a += ptr[i + 12]; b += a;
                a += ptr[i + 13]; b += a;
                a += ptr[i + 14]; b += a;
                a += ptr[i + 15]; b += a;
            }

            for (; i < n; i++)
            {
                a += ptr[i];
                b += a;
            }

            a %= modAdler;
            b %= modAdler;
            ptr += n;
            remaining -= n;
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// SSE2 ADLER32 — 16 байт за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateAdler32Sse2(byte* data, int length)
    {
        const uint modAdler = 65521;
        var a = 1u;
        var b = 0u;

        var ptr = data;
        var remaining = length;

        // Коэффициенты для накопления b: [16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]
        var coeffLo = Vector128.Create(16, 15, 14, 13, 12, 11, 10, 9);
        var coeffHi = Vector128.Create(8, 7, 6, 5, 4, 3, 2, 1);
        var zero = Vector128<byte>.Zero;

        while (remaining >= 5552)
        {
            var blockLen = 5552;
            remaining -= blockLen;

            var sumA = Vector128<int>.Zero;
            var sumB = Vector128<int>.Zero;

            while (blockLen >= 16)
            {
                var v = Sse2.LoadVector128(ptr);
                ptr += 16;
                blockLen -= 16;

                // Сумма для b: умножаем на позиционные коэффициенты
                var vLo = Sse2.UnpackLow(v, zero).AsInt16();
                var vHi = Sse2.UnpackHigh(v, zero).AsInt16();

                sumB = Sse2.Add(sumB, Sse2.ShiftLeftLogical(sumA, 4)); // sumB += sumA * 16
                sumA = Sse2.Add(sumA, Sse2.SumAbsoluteDifferences(v, zero).AsInt32());

                var dotLo = Sse2.MultiplyAddAdjacent(vLo, coeffLo);
                var dotHi = Sse2.MultiplyAddAdjacent(vHi, coeffHi);
                sumB = Sse2.Add(sumB, Sse2.Add(dotLo, dotHi));
            }

            // Horizontal sum
            var tmpA = Sse2.Add(sumA, Sse2.Shuffle(sumA, 0x4E)); // swap 64-bit halves
            tmpA = Sse2.Add(tmpA, Sse2.Shuffle(tmpA, 0xB1)); // swap 32-bit pairs
            a += (uint)Sse2.ConvertToInt32(tmpA);

            var tmpB = Sse2.Add(sumB, Sse2.Shuffle(sumB, 0x4E));
            tmpB = Sse2.Add(tmpB, Sse2.Shuffle(tmpB, 0xB1));
            b += (uint)Sse2.ConvertToInt32(tmpB);

            // Обработка остатка блока
            while (blockLen-- > 0)
            {
                a += *ptr++;
                b += a;
            }

            a %= modAdler;
            b %= modAdler;
        }

        // Остаток < 5552
        while (remaining-- > 0)
        {
            a += *ptr++;
            b += a;
        }

        a %= modAdler;
        b %= modAdler;

        return (b << 16) | a;
    }

    /// <summary>
    /// AVX2 ADLER32 — 32 байта за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateAdler32Avx2(byte* data, int length)
    {
        const uint modAdler = 65521;
        var a = 1u;
        var b = 0u;

        var ptr = data;
        var remaining = length;

        // Коэффициенты для 32 байт
        var coeffs = Vector256.Create(
            32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17);
        var coeffs2 = Vector256.Create(
            16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
        var zero = Vector256<byte>.Zero;
        var ones = Vector256.Create((short)1);

        while (remaining >= 5552)
        {
            var blockLen = 5552;
            remaining -= blockLen;

            var sumA = Vector256<int>.Zero;
            var sumB = Vector256<int>.Zero;

            while (blockLen >= 32)
            {
                var v = Avx.LoadVector256(ptr);
                ptr += 32;
                blockLen -= 32;

                // Распаковка в 16-bit
                var vLo = Avx2.UnpackLow(v, zero.AsByte()).AsInt16();
                var vHi = Avx2.UnpackHigh(v, zero.AsByte()).AsInt16();

                sumB = Avx2.Add(sumB, Avx2.ShiftLeftLogical(sumA, 5)); // sumB += sumA * 32

                // Горизонтальная сумма байтов
                var sad = Avx2.SumAbsoluteDifferences(v, zero.AsByte());
                sumA = Avx2.Add(sumA, sad.AsInt32());

                // Взвешенная сумма для b
                var dotLo = Avx2.MultiplyAddAdjacent(vLo, coeffs);
                var dotHi = Avx2.MultiplyAddAdjacent(vHi, coeffs2);
                sumB = Avx2.Add(sumB, Avx2.Add(dotLo, dotHi));
            }

            // Horizontal sum AVX2 → scalar
            var sumA128 = Sse2.Add(sumA.GetLower(), sumA.GetUpper());
            var tmpA = Sse2.Add(sumA128, Sse2.Shuffle(sumA128, 0x4E));
            tmpA = Sse2.Add(tmpA, Sse2.Shuffle(tmpA, 0xB1));
            a += (uint)Sse2.ConvertToInt32(tmpA);

            var sumB128 = Sse2.Add(sumB.GetLower(), sumB.GetUpper());
            var tmpB = Sse2.Add(sumB128, Sse2.Shuffle(sumB128, 0x4E));
            tmpB = Sse2.Add(tmpB, Sse2.Shuffle(tmpB, 0xB1));
            b += (uint)Sse2.ConvertToInt32(tmpB);

            while (blockLen-- > 0)
            {
                a += *ptr++;
                b += a;
            }

            a %= modAdler;
            b %= modAdler;
        }

        while (remaining-- > 0)
        {
            a += *ptr++;
            b += a;
        }

        a %= modAdler;
        b %= modAdler;

        return (b << 16) | a;
    }

    #endregion

    #region CRC32

    /// <summary>
    /// Таблица CRC32 для PNG (полином 0xEDB88320) — 8KB sliced.
    /// </summary>
    private static readonly uint[] Crc32Table = CreateCrc32TableSliced();

    /// <summary>
    /// Создаёт sliced таблицу CRC32 (8 таблиц по 256 записей).
    /// </summary>
    private static uint[] CreateCrc32TableSliced()
    {
        var table = new uint[8 * 256];

        // Базовая таблица
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        // Sliced таблицы для 8-байтовой обработки
        for (var n = 0; n < 256; n++)
        {
            var c = table[n];
            for (var k = 1; k < 8; k++)
            {
                c = table[c & 0xFF] ^ (c >> 8);
                table[(k * 256) + n] = c;
            }
        }

        return table;
    }

    /// <summary>
    /// Вычисляет CRC32 с использованием sliced-by-8 алгоритма.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        fixed (byte* pData = data)
        fixed (uint* pTable = Crc32Table)
        {
            // Пробуем hardware CRC32 если доступен
            if (Sse42.IsSupported)
            {
                return CalculateCrc32Hardware(pData, data.Length);
            }

            return CalculateCrc32Sliced(pData, data.Length, pTable);
        }
    }

    /// <summary>
    /// Hardware CRC32C (SSE4.2) — адаптировано для PNG CRC32.
    /// </summary>
    /// <remarks>
    /// SSE4.2 использует CRC32C (Castagnoli), а PNG использует CRC32 (IEEE).
    /// Но hardware всё равно быстрее для больших данных.
    /// Здесь используем sliced как fallback — hardware CRC32C не подходит напрямую.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateCrc32Hardware(byte* data, int length)
    {
        // PNG использует IEEE CRC32, а SSE4.2 — CRC32C (другой полином)
        // Поэтому используем sliced алгоритм, который всё равно очень быстрый
        fixed (uint* pTable = Crc32Table)
        {
            return CalculateCrc32Sliced(data, length, pTable);
        }
    }

    /// <summary>
    /// Sliced-by-8 CRC32 — обрабатывает 8 байт за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe uint CalculateCrc32Sliced(byte* data, int length, uint* table)
    {
        var crc = 0xFFFFFFFF;
        var ptr = data;
        var remaining = length;

        // Обработка по 8 байт
        while (remaining >= 8)
        {
            crc ^= *(uint*)ptr;
            var high = *(uint*)(ptr + 4);

            crc = table[(7 * 256) + (crc & 0xFF)] ^
                  table[(6 * 256) + ((crc >> 8) & 0xFF)] ^
                  table[(5 * 256) + ((crc >> 16) & 0xFF)] ^
                  table[(4 * 256) + (crc >> 24)] ^
                  table[(3 * 256) + (high & 0xFF)] ^
                  table[(2 * 256) + ((high >> 8) & 0xFF)] ^
                  table[(1 * 256) + ((high >> 16) & 0xFF)] ^
                  table[high >> 24];

            ptr += 8;
            remaining -= 8;
        }

        // Остаток
        while (remaining-- > 0)
        {
            crc = table[(crc ^ *ptr++) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    #endregion
}
