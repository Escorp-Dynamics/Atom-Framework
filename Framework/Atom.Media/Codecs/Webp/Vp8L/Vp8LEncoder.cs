#pragma warning disable S109, S3776, MA0051, IDE0010, IDE0047, IDE0048, CA1822
#pragma warning disable S2223, S2696

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Atom.IO;
using Atom.IO.Compression.Huffman;

namespace Atom.Media;

/// <summary>
/// Кодировщик VP8L (WebP Lossless) битового потока.
/// </summary>
/// <remarks>
/// <para>
/// Реализация спецификации:
/// https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification
/// </para>
/// <para>
/// Стратегия кодирования:
/// <list type="bullet">
///   <item>LZ77 backward references (hash-chain)</item>
///   <item>Predictor transform (14 режимов, per-tile)</item>
///   <item>Cross-Color transform (per-tile)</item>
///   <item>SubtractGreen transform</item>
///   <item>Color cache (опционально)</item>
///   <item>Один prefix group (без entropy image)</item>
/// </list>
/// </para>
/// </remarks>
internal static class Vp8LEncoder
{
    #region Constants

    /// <summary>Максимальная длина Huffman кода для VP8L (совпадает с TableLog декодера).</summary>
    private const int MaxCodeLength = 11;

    /// <summary>Лог2 таблицы Хаффмана для CL кодов.</summary>
    private const int ClTableLog = 7;

    /// <summary>Количество символов в алфавите длин кодов.</summary>
    private const int NumCodeLengthCodes = 19;

    /// <summary>Размер блока для Predictor/CrossColor transform (2^SizeBits).</summary>
    private const int TransformSizeBits = 3; // 8×8 тайлы

    /// <summary>Размер окна LZ77 hash-chain.</summary>
    private const int Lz77WindowSize = 8192;

    /// <summary>Минимальная длина совпадения для LZ77.</summary>
    private const int Lz77MinMatchLength = 3;

    /// <summary>Максимальная длина совпадения LZ77.</summary>
    private const int Lz77MaxMatchLength = 4096;

    /// <summary>Размер хеш-таблицы LZ77 (степень 2).</summary>
    private const int Lz77HashBits = 16;

    /// <summary>Биты для color cache (0 = отключён).</summary>
    private const int ColorCacheBits = 7;

    /// <summary>Хеш-множитель для color cache (из спецификации).</summary>
    private const uint ColorCacheHashMultiplier = 0x1E35A7BD;

    /// <summary>Размер алфавита green-канала (literals + length prefixes + color cache).</summary>
    private const int GreenAlphabetSize = Vp8LConstants.NumLiteralSymbols
        + Vp8LConstants.NumLengthPrefixCodes + (1 << ColorCacheBits);

    #endregion

#if DEBUG
    // Профилирование: замеры по этапам кодирования (в тиках Stopwatch)
    internal static long EncReadPixelsTicks;
    internal static long EncPredictorTicks;
    internal static long EncPredictorSelectionTicks;
    internal static long EncPredictorApplyTicks;
    internal static long EncCrossColorTicks;
    internal static long EncSubtractGreenTicks;
    internal static long EncLz77Ticks;
    internal static long EncFrequencyTicks;
    internal static long EncHuffmanBuildTicks;
    internal static long EncBitstreamWriteTicks;
    internal static long EncTotalTicks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mark() => System.Diagnostics.Stopwatch.GetTimestamp();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Elapsed(long start) => System.Diagnostics.Stopwatch.GetTimestamp() - start;
#endif

    #region Public API

    /// <summary>
    /// Кодирует VideoFrame в VP8L битовый поток.
    /// </summary>
    /// <param name="frame">Входной кадр (RGB24 или RGBA32).</param>
    /// <param name="output">Выходной буфер для VP8L данных (включая RIFF/WEBP контейнер).</param>
    /// <param name="bytesWritten">Количество записанных байт.</param>
    /// <returns>Результат кодирования.</returns>
    internal static CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;

        var width = frame.Width;
        var height = frame.Height;

        if (width < 1 || width > Vp8LConstants.MaxImageSize ||
            height < 1 || height > Vp8LConstants.MaxImageSize)
        {
            return CodecResult.InvalidData;
        }

        var hasAlpha = frame.PixelFormat == VideoPixelFormat.Rgba32;
        var totalPixels = width * height;

        // 1. Конвертируем VideoFrame → ARGB uint[] буфер
        var pixelPool = ArrayPool<uint>.Shared.Rent(totalPixels);
        try
        {
#if DEBUG
            var tTotal = Mark();
            var tPhase = Mark();
#endif
            var pixels = pixelPool.AsSpan(0, totalPixels);
            ReadFramePixels(frame, pixels, hasAlpha);
#if DEBUG
            EncReadPixelsTicks = Elapsed(tPhase);
#endif

            var result = EncodePixels(pixels, width, height, hasAlpha, output, out bytesWritten);
#if DEBUG
            EncTotalTicks = Elapsed(tTotal);
#endif
            return result;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(pixelPool);
        }
    }

    /// <summary>
    /// Копирует пиксели кадра в managed буфер (вызывать из основного потока — ref struct).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CopyFramePixels(in ReadOnlyVideoFrame frame, uint[] destination, int totalPixels, bool hasAlpha) =>
        ReadFramePixels(frame, destination.AsSpan(0, totalPixels), hasAlpha);

    /// <summary>
    /// Кодирует из предварительно скопированного пиксельного буфера (можно вызывать из любого потока).
    /// </summary>
    internal static CodecResult EncodeFromPixels(
        uint[] pixels, int width, int height, bool hasAlpha,
        byte[] output, out int bytesWritten)
    {
        return EncodePixels(pixels.AsSpan(0, width * height), width, height, hasAlpha,
                           output.AsSpan(), out bytesWritten);
    }

    /// <summary>
    /// Основной pipeline кодирования пиксельных данных.
    /// </summary>
    private static CodecResult EncodePixels(
        Span<uint> pixels, int width, int height, bool hasAlpha,
        Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;
        var totalPixels = width * height;
        var blockSize = 1 << TransformSizeBits;
        var tilesPerRow = Vp8LTransforms.DivRoundUp(width, blockSize);
        var tilesPerCol = Vp8LTransforms.DivRoundUp(height, blockSize);
        var totalTiles = tilesPerRow * tilesPerCol;

        // 2. Применяем Predictor transform (до SubtractGreen!)
        var predictorImagePool = ArrayPool<uint>.Shared.Rent(totalTiles);
        var colorImagePool = ArrayPool<uint>.Shared.Rent(totalTiles);
        try
        {
            var predictorImage = predictorImagePool.AsSpan(0, totalTiles);
            var colorImage = colorImagePool.AsSpan(0, totalTiles);
            predictorImage.Clear();
            colorImage.Clear();

#if DEBUG
            var tPhase = Mark();
#endif
            // Predictor: выбираем лучший режим per-tile и применяем forward
            ForwardPredictorTransform(pixels, width, height, predictorImage);
#if DEBUG
            EncPredictorTicks = Elapsed(tPhase);
            tPhase = Mark();
#endif

            // Cross-Color: вычисляем коэффициенты per-tile и применяем forward
            ForwardCrossColorTransform(pixels, width, height, TransformSizeBits, colorImage);
#if DEBUG
            EncCrossColorTicks = Elapsed(tPhase);
            tPhase = Mark();
#endif

            // SubtractGreen (всегда полезен после predictor + cross-color)
            Vp8LTransforms.ForwardSubtractGreen(pixels);
#if DEBUG
            EncSubtractGreenTicks = Elapsed(tPhase);
#endif

            // 3. LZ77 + Frequency: hash-chain + частоты за один проход
            var lz77Pool = ArrayPool<Lz77Token>.Shared.Rent(totalPixels);
            try
            {
                var tokens = lz77Pool.AsSpan(0, totalPixels);

                var greenFreqPool = ArrayPool<uint>.Shared.Rent(GreenAlphabetSize);
                var redFreqPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                var blueFreqPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                var alphaFreqPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                var distFreqPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeDistance);
                try
                {
                    var greenFreq = greenFreqPool.AsSpan(0, GreenAlphabetSize);
                    var redFreq = redFreqPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                    var blueFreq = blueFreqPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                    var alphaFreq = alphaFreqPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                    var distFreq = distFreqPool.AsSpan(0, Vp8LConstants.AlphabetSizeDistance);
                    greenFreq.Clear();
                    redFreq.Clear();
                    blueFreq.Clear();
                    alphaFreq.Clear();
                    distFreq.Clear();

#if DEBUG
                    tPhase = Mark();
#endif
                    var tokenCount = FindLz77MatchesAndFrequencies(
                        pixels, tokens, width,
                        32 - ColorCacheBits, 1 << ColorCacheBits,
                        greenFreq, redFreq, blueFreq, alphaFreq, distFreq);
#if DEBUG
                    EncLz77Ticks = Elapsed(tPhase);
                    EncFrequencyTicks = 0;
#endif

                    // 5. Строим Huffman коды (pooled для избежания GC аллокаций)
                    var greenClPool = ArrayPool<byte>.Shared.Rent(GreenAlphabetSize);
                    var redClPool = ArrayPool<byte>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var blueClPool = ArrayPool<byte>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var alphaClPool = ArrayPool<byte>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var distClPool = ArrayPool<byte>.Shared.Rent(Vp8LConstants.AlphabetSizeDistance);
                    var greenCodesPool = ArrayPool<uint>.Shared.Rent(GreenAlphabetSize);
                    var redCodesPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var blueCodesPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var alphaCodesPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeColor);
                    var distCodesPool = ArrayPool<uint>.Shared.Rent(Vp8LConstants.AlphabetSizeDistance);
                    try
                    {
                        var greenCodeLengths = greenClPool.AsSpan(0, GreenAlphabetSize);
                        var redCodeLengths = redClPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var blueCodeLengths = blueClPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var alphaCodeLengths = alphaClPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var distCodeLengths = distClPool.AsSpan(0, Vp8LConstants.AlphabetSizeDistance);

#if DEBUG
                        tPhase = Mark();
#endif
                        HuffmanTreeBuilder.BuildFromFrequencies(greenFreq, greenCodeLengths, MaxCodeLength);
                        HuffmanTreeBuilder.BuildFromFrequencies(redFreq, redCodeLengths, MaxCodeLength);
                        HuffmanTreeBuilder.BuildFromFrequencies(blueFreq, blueCodeLengths, MaxCodeLength);
                        HuffmanTreeBuilder.BuildFromFrequencies(alphaFreq, alphaCodeLengths, MaxCodeLength);
                        HuffmanTreeBuilder.BuildFromFrequencies(distFreq, distCodeLengths, MaxCodeLength);

                        var greenCodes = greenCodesPool.AsSpan(0, GreenAlphabetSize);
                        var redCodes = redCodesPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var blueCodes = blueCodesPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var alphaCodes = alphaCodesPool.AsSpan(0, Vp8LConstants.AlphabetSizeColor);
                        var distCodes = distCodesPool.AsSpan(0, Vp8LConstants.AlphabetSizeDistance);

                        HuffmanTreeBuilder.BuildEncodeCodes(greenCodeLengths, greenCodes, lsbFirst: true);
                        HuffmanTreeBuilder.BuildEncodeCodes(redCodeLengths, redCodes, lsbFirst: true);
                        HuffmanTreeBuilder.BuildEncodeCodes(blueCodeLengths, blueCodes, lsbFirst: true);
                        HuffmanTreeBuilder.BuildEncodeCodes(alphaCodeLengths, alphaCodes, lsbFirst: true);
                        HuffmanTreeBuilder.BuildEncodeCodes(distCodeLengths, distCodes, lsbFirst: true);
#if DEBUG
                        EncHuffmanBuildTicks = Elapsed(tPhase);
#endif

                        // 6. Кодируем VP8L битовый поток
#if DEBUG
                        tPhase = Mark();
#endif
                        var maxBitstreamSize = (totalPixels * 10) + 4096;
                        var bitstreamBuffer = ArrayPool<byte>.Shared.Rent(maxBitstreamSize);
                        try
                        {
                            var writer = new BitWriter(bitstreamBuffer.AsSpan(0, maxBitstreamSize), lsbFirst: true);

                            // Записываем transforms: Predictor → CrossColor → SubtractGreen
                            WriteTransforms(ref writer, TransformSizeBits,
                                predictorImage, tilesPerRow, tilesPerCol,
                                colorImage);

                            // Color cache
                            writer.WriteBit(bit: true); // use_color_cache = 1
                            writer.WriteBits(ColorCacheBits, 4);

                            // Один prefix group
                            writer.WriteBit(bit: false); // use_meta = 0

                            // 5 Huffman таблиц
                            WriteHuffmanTable(ref writer, greenCodeLengths, GreenAlphabetSize);
                            WriteHuffmanTable8(ref writer, redCodeLengths);
                            WriteHuffmanTable8(ref writer, blueCodeLengths);
                            WriteHuffmanTable8(ref writer, alphaCodeLengths);
                            WriteHuffmanTable(ref writer, distCodeLengths, Vp8LConstants.AlphabetSizeDistance);

                            ClearSingleSymbolCodes(greenCodeLengths, greenCodes);
                            ClearSingleSymbolCodes(redCodeLengths, redCodes);
                            ClearSingleSymbolCodes(blueCodeLengths, blueCodes);
                            ClearSingleSymbolCodes(alphaCodeLengths, alphaCodes);
                            ClearSingleSymbolCodes(distCodeLengths, distCodes);

                            // Записываем LZ77 + literal данные
                            WriteLz77Data(
                                ref writer, pixels, tokens, tokenCount,
                                greenCodeLengths, greenCodes,
                                redCodeLengths, redCodes,
                                blueCodeLengths, blueCodes,
                                alphaCodeLengths, alphaCodes,
                                distCodeLengths, distCodes);

                            writer.Flush();
#if DEBUG
                            EncBitstreamWriteTicks = Elapsed(tPhase);
#endif
                            var bitstreamSize = writer.BytesWritten;

                            return WriteContainer(output, width, height, hasAlpha,
                                bitstreamBuffer.AsSpan(0, bitstreamSize), out bytesWritten);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bitstreamBuffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(greenClPool);
                        ArrayPool<byte>.Shared.Return(redClPool);
                        ArrayPool<byte>.Shared.Return(blueClPool);
                        ArrayPool<byte>.Shared.Return(alphaClPool);
                        ArrayPool<byte>.Shared.Return(distClPool);
                        ArrayPool<uint>.Shared.Return(greenCodesPool);
                        ArrayPool<uint>.Shared.Return(redCodesPool);
                        ArrayPool<uint>.Shared.Return(blueCodesPool);
                        ArrayPool<uint>.Shared.Return(alphaCodesPool);
                        ArrayPool<uint>.Shared.Return(distCodesPool);
                    }
                }
                finally
                {
                    ArrayPool<uint>.Shared.Return(greenFreqPool);
                    ArrayPool<uint>.Shared.Return(redFreqPool);
                    ArrayPool<uint>.Shared.Return(blueFreqPool);
                    ArrayPool<uint>.Shared.Return(alphaFreqPool);
                    ArrayPool<uint>.Shared.Return(distFreqPool);
                }
            }
            finally
            {
                ArrayPool<Lz77Token>.Shared.Return(lz77Pool);
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(predictorImagePool);
            ArrayPool<uint>.Shared.Return(colorImagePool);
        }
    }

    #endregion

    #region LZ77 Token

    /// <summary>
    /// Токен LZ77: либо литерал, либо backward reference.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Lz77Token
    {
        /// <summary>true = backward reference, false = literal.</summary>
        internal bool IsBackRef;

        /// <summary>Длина совпадения (для backward reference).</summary>
        internal int Length;

        /// <summary>Расстояние в пикселях (для backward reference).</summary>
        internal int Distance;
    }

    #endregion

    #region Frame Conversion

    /// <summary>
    /// Конвертирует VideoFrame в внутренний ARGB буфер.
    /// SIMD-оптимизация для RGBA32: Vector128 byte shuffle (swap R↔B).
    /// </summary>
    private static void ReadFramePixels(in ReadOnlyVideoFrame frame, Span<uint> pixels, bool hasAlpha)
    {
        var width = frame.Width;
        var height = frame.Height;
        var sourceData = frame.PackedData;
        ref var srcBase = ref MemoryMarshal.GetReference(sourceData.Data);
        var srcStride = sourceData.Stride;
        ref var dstBase = ref MemoryMarshal.GetReference(pixels);

        if (hasAlpha)
        {
            // RGBA32: каждый пиксель 4 байта [R,G,B,A] → uint32 ARGB [B,G,R,A] (little-endian)
            // Нужно поменять R↔B в каждом 4-байтовом блоке
            for (var y = 0; y < height; y++)
            {
                ref var srcRef = ref Unsafe.Add(ref srcBase, y * srcStride);
                ref var dstRef = ref Unsafe.Add(ref dstBase, y * width);
                var x = 0;

                if (Vector256.IsHardwareAccelerated)
                {
                    var shuffleMask256 = Vector256.Create(
                        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,
                        18, 17, 16, 19, 22, 21, 20, 23, 26, 25, 24, 27, 30, 29, 28, 31);

                    for (; x + 8 <= width; x += 8)
                    {
                        var src = Vector256.LoadUnsafe(ref srcRef, (nuint)(x * 4));
                        var shuffled = Vector256.Shuffle(src, shuffleMask256);
                        shuffled.AsUInt32().StoreUnsafe(ref dstRef, (nuint)x);
                    }
                }

                if (Vector128.IsHardwareAccelerated)
                {
                    var shuffleMask = Vector128.Create(
                        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

                    for (; x + 4 <= width; x += 4)
                    {
                        var src = Vector128.LoadUnsafe(ref srcRef, (nuint)(x * 4));
                        var shuffled = Vector128.Shuffle(src, shuffleMask);
                        shuffled.AsUInt32().StoreUnsafe(ref dstRef, (nuint)x);
                    }
                }

                for (; x < width; x++)
                {
                    var srcIdx = x * 4;
                    var r = Unsafe.Add(ref srcRef, srcIdx);
                    var g = Unsafe.Add(ref srcRef, srcIdx + 1);
                    var b = Unsafe.Add(ref srcRef, srcIdx + 2);
                    var a = Unsafe.Add(ref srcRef, srcIdx + 3);
                    Unsafe.Add(ref dstRef, x) = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
        else
        {
            // RGB24: каждый пиксель 3 байта [R,G,B] → uint32 ARGB с alpha=0xFF
            for (var y = 0; y < height; y++)
            {
                ref var srcRef = ref Unsafe.Add(ref srcBase, y * srcStride);
                ref var dstRef = ref Unsafe.Add(ref dstBase, y * width);
                var x = 0;

                if (Vector256.IsHardwareAccelerated)
                {
                    // 8px/iter: два Vector128 shuffle + один Vector256 store
                    var shuffleMask = Vector128.Create(
                        2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80);
                    var alphaMask128 = Vector128.Create(0xFF000000u);

                    for (; x + 8 <= width; x += 8)
                    {
                        var byteOff = x * 3;

                        var lo1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref srcRef, byteOff));
                        var hi1 = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref srcRef, byteOff + 8));
                        var r1 = Vector128.Shuffle(Vector128.Create(lo1, hi1).AsByte(), shuffleMask).AsUInt32()
                                 | alphaMask128;

                        var lo2 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref srcRef, byteOff + 12));
                        var hi2 = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref srcRef, byteOff + 20));
                        var r2 = Vector128.Shuffle(Vector128.Create(lo2, hi2).AsByte(), shuffleMask).AsUInt32()
                                 | alphaMask128;

                        Vector256.Create(r1, r2).StoreUnsafe(ref dstRef, (nuint)x);
                    }
                }

                if (Vector128.IsHardwareAccelerated)
                {
                    var shuffleMask = Vector128.Create(
                        2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80);
                    var alphaMask = Vector128.Create(0xFF000000u);

                    for (; x + 4 <= width; x += 4)
                    {
                        var byteOff = x * 3;
                        var lo = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref srcRef, byteOff));
                        var hi = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref srcRef, byteOff + 8));
                        var src = Vector128.Create(lo, hi).AsByte();
                        var shuffled = Vector128.Shuffle(src, shuffleMask);
                        var withAlpha = shuffled.AsUInt32() | alphaMask;
                        withAlpha.StoreUnsafe(ref dstRef, (nuint)x);
                    }
                }

                for (; x < width; x++)
                {
                    var srcIdx = x * 3;
                    var r = Unsafe.Add(ref srcRef, srcIdx);
                    var g = Unsafe.Add(ref srcRef, srcIdx + 1);
                    var b = Unsafe.Add(ref srcRef, srcIdx + 2);
                    Unsafe.Add(ref dstRef, x) = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }
    }

    #endregion

    #region Forward Transforms

    /// <summary>
    /// Forward Predictor (2-pass): выбирает лучший режим per-tile и применяет forward prediction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass 1: для каждого тайла оценивает все 14 режимов предсказания по L1-стоимости
    /// на оригинальных пикселях и выбирает режим с минимальной стоимостью.
    /// </para>
    /// <para>
    /// Pass 2: применяет вычитание предсказания bottom-to-top, right-to-left — это гарантирует,
    /// что все соседи (left, top, topLeft, topRight) ещё не затронуты и содержат оригинальные значения.
    /// </para>
    /// </remarks>
    private static void ForwardPredictorTransform(
        Span<uint> pixels, int width, int height, Span<uint> predictorImage)
    {
        var blockSize = 1 << TransformSizeBits;
        var tilesPerRow = Vp8LTransforms.DivRoundUp(width, blockSize);
        var tilesPerCol = Vp8LTransforms.DivRoundUp(height, blockSize);

        // Pass 1: выбираем лучший режим для каждого тайла на оригинальных пикселях (parallel)
#if DEBUG
        var tSub = Mark();
#endif
        unsafe
        {
            fixed (uint* pixPtr = pixels)
            fixed (uint* predPtr = predictorImage)
            {
                var pp = pixPtr;
                var pd = predPtr;
                var w = width;
                var h = height;
                var bs = blockSize;
                var tpr = tilesPerRow;
                var totalPx = w * h;

                Parallel.For(0, tilesPerCol, ty =>
                {
                    var pixSpan = new ReadOnlySpan<uint>(pp, totalPx);
                    for (var tx = 0; tx < tpr; tx++)
                    {
                        var bestMode = ChooseBestPredictorMode(pixSpan, w, h, tx, ty, bs);
                        pd[ty * tpr + tx] = (uint)bestMode << 8;
                    }
                });
            }
        }
#if DEBUG
        EncPredictorSelectionTicks = Elapsed(tSub);
        tSub = Mark();
#endif

        // Pass 2: применяем forward prediction bottom-to-top, right-to-left.
        // Тайл-батчевая обработка: SIMD для режимов 0,1,2,7, скаляр для остальных.
        ref var pxRef = ref MemoryMarshal.GetReference(pixels);

        for (var y = height - 1; y >= 1; y--)
        {
            var rowOffset = y * width;
            var tileY = y >> TransformSizeBits;

            var x = width - 1;
            while (x >= 1)
            {
                var tileX = x >> TransformSizeBits;
                var tileStartX = Math.Max(tileX << TransformSizeBits, 1);
                var mode = (int)((predictorImage[tileY * tilesPerRow + tileX] >> 8) & 0xF);
                var count = x - tileStartX + 1;

                if (Vector128.IsHardwareAccelerated && count >= 4 && mode is 0 or 1 or 2 or 3 or 4 or 7 or 8 or 9 or 11)
                {
                    var basePos = rowOffset + tileStartX;
                    var remaining = count;
                    var fefe256 = Vector256.Create(0xFEFEFEFEu);
                    var fefe128 = Vector128.Create(0xFEFEFEFEu);

                    // Vector256: 8 пикселей за итерацию (полная строка тайла 8×8)
                    if (Vector256.IsHardwareAccelerated)
                    {
                        while (remaining >= 8)
                        {
                            remaining -= 8;
                            var pos = (nuint)(uint)(basePos + remaining);
                            var cur = Vector256.LoadUnsafe(ref pxRef, pos);
                            var predicted = ComputePred256(ref pxRef, pos, (uint)width, mode, fefe256);

                            var result = (cur.AsByte() - predicted.AsByte()).AsUInt32();
                            Vector256.StoreUnsafe(result, ref pxRef, pos);
                        }
                    }

                    // Vector128: 4 пикселя за итерацию
                    while (remaining >= 4)
                    {
                        remaining -= 4;
                        var pos = (nuint)(uint)(basePos + remaining);
                        var cur = Vector128.LoadUnsafe(ref pxRef, pos);
                        var predicted = ComputePred128(ref pxRef, pos, (uint)width, mode, fefe128);

                        var result = (cur.AsByte() - predicted.AsByte()).AsUInt32();
                        Vector128.StoreUnsafe(result, ref pxRef, pos);
                    }

                    // Скалярный хвост (0..3 крайних левых пикселя тайла)
                    for (var xi = tileStartX + remaining - 1; xi >= tileStartX; xi--)
                    {
                        var pos = rowOffset + xi;
                        var predicted = Vp8LPredictors.Predict(mode, pixels[pos - 1], pixels[pos - width],
                            pixels[pos - width - 1],
                            xi < width - 1 ? pixels[pos - width + 1] : pixels[rowOffset - width]);
                        pixels[pos] = SubArgb(pixels[pos], predicted);
                    }
                }
                else
                {
                    // Скалярный путь для mode=11 и прочих
                    for (var xi = x; xi >= tileStartX; xi--)
                    {
                        var pos = rowOffset + xi;
                        var predicted = Vp8LPredictors.Predict(mode, pixels[pos - 1], pixels[pos - width],
                            pixels[pos - width - 1],
                            xi < width - 1 ? pixels[pos - width + 1] : pixels[rowOffset - width]);
                        pixels[pos] = SubArgb(pixels[pos], predicted);
                    }
                }

                x = tileStartX - 1;
            }

            // x=0: декодер hardcodes mode=2 (T)
            pixels[rowOffset] = SubArgb(pixels[rowOffset], pixels[rowOffset - width]);
        }

        // y=0: декодер hardcodes mode=1 (L)
        for (var x = width - 1; x >= 1; x--)
        {
            pixels[x] = SubArgb(pixels[x], pixels[x - 1]);
        }

        // (0,0): декодер hardcodes predicted = 0xFF000000
        if (pixels.Length > 0)
        {
            pixels[0] = SubArgb(pixels[0], 0xFF000000);
        }
#if DEBUG
        EncPredictorApplyTicks = Elapsed(tSub);
#endif
    }

    /// <summary>
    /// Быстрый набор режимов предсказания для реалтайм-кодирования.
    /// 1=L, 2=T, 7=Avg(L,T), 0=Black, 11=Select — порядок по вероятности (L/T первые).
    /// </summary>
    private static ReadOnlySpan<byte> FastPredictorModes => [1, 2, 7, 0, 11];

    /// <summary>
    /// Выбирает лучший из FastPredictorModes для данного тайла.
    /// Fused 5-mode evaluation: один проход по пикселям, все режимы скорятся одновременно.
    /// </summary>
    private static int ChooseBestPredictorMode(
        ReadOnlySpan<uint> pixels, int width, int height,
        int tileX, int tileY, int blockSize)
    {
        var startX = tileX * blockSize;
        var startY = tileY * blockSize;
        var endX = Math.Min(startX + blockSize, width);
        var endY = Math.Min(startY + blockSize, height);

        var hasInterior = startY > 0 || endY > 1;
        if (!hasInterior) return 1;

        var yStart = Math.Max(startY, 1);
        var xStart = Math.Max(startX, 1);
        var rowWidth = endX - xStart;
        var yStep = endY - yStart > 4 ? 2 : 1;

        // Fused 5-mode Vector256: modes [1=L, 2=T, 7=Avg(L,T), 0=Black, 11=Select]
        // Один проход — 5 cost-аккумуляторов, общие загрузки пикселей, vectorized Select
        if (Vector256.IsHardwareAccelerated && rowWidth >= 8)
        {
            ref var pxRef = ref MemoryMarshal.GetReference(pixels);
            var fefe = Vector256.Create(0xFEFEFEFEu);
            var black = Vector256.Create(0xFF000000u);
            var ff = Vector256.Create(0xFFu);

            var cL = Vector256<uint>.Zero;
            var cT = Vector256<uint>.Zero;
            var cA = Vector256<uint>.Zero;
            var cB = Vector256<uint>.Zero;
            var cS = Vector256<uint>.Zero;

            for (var y = yStart; y < endY; y += yStep)
            {
                var rowBase = (nuint)(uint)(y * width + xStart);
                var w = (nuint)(uint)width;

                for (var xi = (nuint)0; xi + 8 <= (nuint)rowWidth; xi += 8)
                {
                    var pos = rowBase + xi;
                    var cur = Vector256.LoadUnsafe(ref pxRef, pos);
                    var left = Vector256.LoadUnsafe(ref pxRef, pos - 1);
                    var top = Vector256.LoadUnsafe(ref pxRef, pos - w);
                    var tl = Vector256.LoadUnsafe(ref pxRef, pos - w - 1);

                    var curB = cur.AsByte();
                    var leftB = left.AsByte();
                    var topB = top.AsByte();
                    var tlB = tl.AsByte();

                    // Mode 1: L
                    AccCostV256(curB, leftB, ref cL);
                    // Mode 2: T
                    AccCostV256(curB, topB, ref cT);
                    // Mode 7: Avg(L,T)
                    AccCostV256(curB, Avg2V256(left, top, fefe).AsByte(), ref cA);
                    // Mode 0: Black
                    AccCostV256(curB, black.AsByte(), ref cB);
                    // Mode 11: Select — per-pixel SAD determines L or T
                    var absTvTL = Vector256.Abs((topB - tlB).AsSByte()).AsByte().AsUInt32();
                    var sadTv = (absTvTL & ff) + ((absTvTL >>> 8) & ff) + ((absTvTL >>> 16) & ff) + (absTvTL >>> 24);
                    var absLvTL = Vector256.Abs((leftB - tlB).AsSByte()).AsByte().AsUInt32();
                    var sadLv = (absLvTL & ff) + ((absLvTL >>> 8) & ff) + ((absLvTL >>> 16) & ff) + (absLvTL >>> 24);
                    var selMask = Vector256.LessThan(sadTv.AsInt32(), sadLv.AsInt32());
                    var predSel = Vector256.ConditionalSelect(selMask, left.AsInt32(), top.AsInt32()).AsUInt32();
                    AccCostV256(curB, predSel.AsByte(), ref cS);
                }
            }

            var costL = (long)Vector256.Sum(cL);
            var costT = (long)Vector256.Sum(cT);
            var costA = (long)Vector256.Sum(cA);
            var costB = (long)Vector256.Sum(cB);
            var costS = (long)Vector256.Sum(cS);

            var bestCost = costL; var bestMode = 1;
            if (costT < bestCost) { bestCost = costT; bestMode = 2; }
            if (costA < bestCost) { bestCost = costA; bestMode = 7; }
            if (costB < bestCost) { bestCost = costB; bestMode = 0; }
            if (costS < bestCost) { bestMode = 11; }

            return bestMode;
        }

        // Скалярный fallback (левый край: rowWidth < 8)
        var bestModeSc = 1;
        var bestCostSc = long.MaxValue;
        var modes = FastPredictorModes;

        for (var mi = 0; mi < modes.Length; mi++)
        {
            var mode = modes[mi];
            long cost = 0;
            var pruned = false;

            for (var y = yStart; y < endY; y += yStep)
            {
                var rowOffset = y * width;
                for (var x = xStart; x < endX; x++)
                {
                    var pos = rowOffset + x;
                    var predicted = Vp8LPredictors.Predict(mode, pixels[pos - 1], pixels[pos - width],
                        pixels[pos - width - 1],
                        x < width - 1 ? pixels[pos - width + 1] : pixels[rowOffset - width]);
                    var residual = SubArgb(pixels[pos], predicted);
                    cost += AbsComponent((byte)(residual >> 24));
                    cost += AbsComponent((byte)(residual >> 16));
                    cost += AbsComponent((byte)(residual >> 8));
                    cost += AbsComponent((byte)residual);
                }

                if (cost >= bestCostSc) { pruned = true; break; }
            }

            if (!pruned && cost < bestCostSc)
            {
                bestCostSc = cost;
                bestModeSc = mode;
                if (cost == 0) break;
            }
        }

        return bestModeSc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccCostV256(Vector256<byte> cur, Vector256<byte> pred, ref Vector256<uint> cost)
    {
        var abs = Vector256.Abs((cur - pred).AsSByte()).AsByte();
        var lo = Vector256.WidenLower(abs);
        var hi = Vector256.WidenUpper(abs);
        var s = lo + hi;
        cost += Vector256.WidenLower(s) + Vector256.WidenUpper(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> ComputePred256(
        ref uint pxRef, nuint pos, uint width, int mode, Vector256<uint> fefe)
    {
        return mode switch
        {
            0 => Vector256.Create(0xFF000000u),
            1 => Vector256.LoadUnsafe(ref pxRef, pos - 1),
            2 => Vector256.LoadUnsafe(ref pxRef, pos - width),
            3 => Vector256.LoadUnsafe(ref pxRef, pos - width + 1),
            4 => Vector256.LoadUnsafe(ref pxRef, pos - width - 1),
            7 => Avg2V256(
                Vector256.LoadUnsafe(ref pxRef, pos - 1),
                Vector256.LoadUnsafe(ref pxRef, pos - width), fefe),
            8 => Avg2V256(
                Vector256.LoadUnsafe(ref pxRef, pos - width - 1),
                Vector256.LoadUnsafe(ref pxRef, pos - width), fefe),
            9 => Avg2V256(
                Vector256.LoadUnsafe(ref pxRef, pos - width),
                Vector256.LoadUnsafe(ref pxRef, pos - width + 1), fefe),
            11 => SelectV256(ref pxRef, pos, width),
            _ => Avg2V256(
                Vector256.LoadUnsafe(ref pxRef, pos - width),
                Vector256.LoadUnsafe(ref pxRef, pos - width + 1), fefe),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ComputePred128(
        ref uint pxRef, nuint pos, uint width, int mode, Vector128<uint> fefe)
    {
        return mode switch
        {
            0 => Vector128.Create(0xFF000000u),
            1 => Vector128.LoadUnsafe(ref pxRef, pos - 1),
            2 => Vector128.LoadUnsafe(ref pxRef, pos - width),
            3 => Vector128.LoadUnsafe(ref pxRef, pos - width + 1),
            4 => Vector128.LoadUnsafe(ref pxRef, pos - width - 1),
            7 => Avg2V128(
                Vector128.LoadUnsafe(ref pxRef, pos - 1),
                Vector128.LoadUnsafe(ref pxRef, pos - width), fefe),
            8 => Avg2V128(
                Vector128.LoadUnsafe(ref pxRef, pos - width - 1),
                Vector128.LoadUnsafe(ref pxRef, pos - width), fefe),
            9 => Avg2V128(
                Vector128.LoadUnsafe(ref pxRef, pos - width),
                Vector128.LoadUnsafe(ref pxRef, pos - width + 1), fefe),
            11 => SelectV128(ref pxRef, pos, width),
            _ => Avg2V128(
                Vector128.LoadUnsafe(ref pxRef, pos - width),
                Vector128.LoadUnsafe(ref pxRef, pos - width + 1), fefe),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> SelectV256(ref uint pxRef, nuint pos, uint width)
    {
        var left = Vector256.LoadUnsafe(ref pxRef, pos - 1);
        var top = Vector256.LoadUnsafe(ref pxRef, pos - width);
        var tl = Vector256.LoadUnsafe(ref pxRef, pos - width - 1);
        var ff = Vector256.Create(0xFFu);
        // Exact int32 arithmetic matching scalar Select: distL = SAD(T, TL), distT = SAD(L, TL)
        var topA = top >>> 24;
        var topR = (top >>> 16) & ff;
        var topG = (top >>> 8) & ff;
        var topB = top & ff;
        var tlA = tl >>> 24;
        var tlR = (tl >>> 16) & ff;
        var tlG = (tl >>> 8) & ff;
        var tlB = tl & ff;
        var leftA = left >>> 24;
        var leftR = (left >>> 16) & ff;
        var leftG = (left >>> 8) & ff;
        var leftB = left & ff;
        var distL = Vector256.Abs((topA - tlA).AsInt32()) + Vector256.Abs((topR - tlR).AsInt32())
                  + Vector256.Abs((topG - tlG).AsInt32()) + Vector256.Abs((topB - tlB).AsInt32());
        var distT = Vector256.Abs((leftA - tlA).AsInt32()) + Vector256.Abs((leftR - tlR).AsInt32())
                  + Vector256.Abs((leftG - tlG).AsInt32()) + Vector256.Abs((leftB - tlB).AsInt32());
        var selMask = Vector256.LessThan(distL, distT);
        return Vector256.ConditionalSelect(selMask, left.AsInt32(), top.AsInt32()).AsUInt32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> SelectV128(ref uint pxRef, nuint pos, uint width)
    {
        var left = Vector128.LoadUnsafe(ref pxRef, pos - 1);
        var top = Vector128.LoadUnsafe(ref pxRef, pos - width);
        var tl = Vector128.LoadUnsafe(ref pxRef, pos - width - 1);
        var ff = Vector128.Create(0xFFu);
        var topA = top >>> 24;
        var topR = (top >>> 16) & ff;
        var topG = (top >>> 8) & ff;
        var topB = top & ff;
        var tlA = tl >>> 24;
        var tlR = (tl >>> 16) & ff;
        var tlG = (tl >>> 8) & ff;
        var tlB = tl & ff;
        var leftA = left >>> 24;
        var leftR = (left >>> 16) & ff;
        var leftG = (left >>> 8) & ff;
        var leftB = left & ff;
        var distL = Vector128.Abs((topA - tlA).AsInt32()) + Vector128.Abs((topR - tlR).AsInt32())
                  + Vector128.Abs((topG - tlG).AsInt32()) + Vector128.Abs((topB - tlB).AsInt32());
        var distT = Vector128.Abs((leftA - tlA).AsInt32()) + Vector128.Abs((leftR - tlR).AsInt32())
                  + Vector128.Abs((leftG - tlG).AsInt32()) + Vector128.Abs((leftB - tlB).AsInt32());
        var selMask = Vector128.LessThan(distL, distT);
        return Vector128.ConditionalSelect(selMask, left.AsInt32(), top.AsInt32()).AsUInt32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> Avg2V256(Vector256<uint> a, Vector256<uint> b, Vector256<uint> fefe) =>
        (a & b) + (((a ^ b) & fefe) >>> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Avg2V128(Vector128<uint> a, Vector128<uint> b, Vector128<uint> fefe) =>
        (a & b) + (((a ^ b) & fefe) >>> 1);

    /// <summary>
    /// Абсолютное значение байта как знакового числа (signed byte interpretation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AbsComponent(byte v)
    {
        // Cast to int before negation to avoid OverflowException on -128
        var s = (int)(sbyte)v;
        return s < 0 ? -s : s;
    }

    /// <summary>
    /// Forward CrossColor: вычисляет коэффициенты и применяет обратное преобразование каналов.
    /// </summary>
    private static void ForwardCrossColorTransform(
        Span<uint> pixels, int width, int height, int sizeBits, Span<uint> colorImage)
    {
        var blockSize = 1 << sizeBits;
        var tilesPerRow = Vp8LTransforms.DivRoundUp(width, blockSize);
        var tilesPerCol = Vp8LTransforms.DivRoundUp(height, blockSize);

        unsafe
        {
            fixed (uint* pixPtr = pixels)
            fixed (uint* ciPtr = colorImage)
            {
                var pp = pixPtr;
                var ci = ciPtr;
                var w = width;
                var h = height;
                var bs = blockSize;
                var tpr = tilesPerRow;
                var totalPx = w * h;

                Parallel.For(0, tilesPerCol, ty =>
                {
                    var pixSpan = new Span<uint>(pp, totalPx);
                    ref var ccPixRef = ref MemoryMarshal.GetReference(pixSpan);

                    for (var tx = 0; tx < tpr; tx++)
                    {
                        var startX = tx * bs;
                        var startY = ty * bs;
                        var endX = Math.Min(startX + bs, w);
                        var endY = Math.Min(startY + bs, h);

                        ForwardCrossColorTile(
                            ref ccPixRef, ci, w, startX, startY, endX, endY,
                            ty * tpr + tx);
                    }
                });
            }
        }
    }

    private static unsafe void ForwardCrossColorTile(
        ref uint ccPixRef, uint* colorImage, int width,
        int startX, int startY, int endX, int endY,
        int tileIndex)
    {
        // Реалтайм: субсэмплинг через строку для вычисления коэффициентов
        var ccYStep = endY - startY > 4 ? 2 : 1;
        long sumGG = 0, sumGR = 0, sumGB = 0, sumRR = 0, sumRB = 0;
        var tileWidth = endX - startX;

        if (Vector256.IsHardwareAccelerated && tileWidth >= 8)
        {
            var mask = Vector256.Create(0xFFu);
            var signExt = Vector256.Create(0x80);
            var vGG = Vector256<int>.Zero;
            var vGR = Vector256<int>.Zero;
            var vGB = Vector256<int>.Zero;
            var vRR = Vector256<int>.Zero;
            var vRB = Vector256<int>.Zero;

            for (var y = startY; y < endY; y += ccYStep)
            {
                var rowPos = (nuint)(uint)(y * width + startX);
                for (var xi = (nuint)0; xi + 8 <= (nuint)tileWidth; xi += 8)
                {
                    var px = Vector256.LoadUnsafe(ref ccPixRef, rowPos + xi);
                    var sg = (((px >>> 8) & mask).AsInt32() ^ signExt) - signExt;
                    var sr = (((px >>> 16) & mask).AsInt32() ^ signExt) - signExt;
                    var sb = ((px & mask).AsInt32() ^ signExt) - signExt;

                    vGG += sg * sg;
                    vGR += sg * sr;
                    vGB += sg * sb;
                    vRR += sr * sr;
                    vRB += sr * sb;
                }
            }

            sumGG = Vector256.Sum(vGG);
            sumGR = Vector256.Sum(vGR);
            sumGB = Vector256.Sum(vGB);
            sumRR = Vector256.Sum(vRR);
            sumRB = Vector256.Sum(vRB);
        }
        else
        {
            for (var y = startY; y < endY; y += ccYStep)
            {
                var rowBase = y * width;
                for (var x = startX; x < endX; x++)
                {
                    var argb = Unsafe.Add(ref ccPixRef, rowBase + x);
                    var g = (sbyte)(byte)((argb >> 8) & 0xFF);
                    var r = (sbyte)(byte)((argb >> 16) & 0xFF);
                    var b = (sbyte)(byte)(argb & 0xFF);
                    sumGG += g * g;
                    sumGR += g * r;
                    sumGB += g * b;
                    sumRR += r * r;
                    sumRB += r * b;
                }
            }
        }

        // Least-squares коэффициенты (без L1 refinement — для реалтайма)
        var greenToRed = (sbyte)(sumGG > 0 ? Math.Clamp((sumGR * 32 + sumGG / 2) / sumGG, -128, 127) : 0);
        var greenToBlue = (sbyte)(sumGG > 0 ? Math.Clamp((sumGB * 32 + sumGG / 2) / sumGG, -128, 127) : 0);
        var redToBlue = (sbyte)(sumRR > 0 ? Math.Clamp((sumRB * 32 + sumRR / 2) / sumRR, -128, 127) : 0);

        // Сохраняем в colorImage
        colorImage[tileIndex] = 0xFF000000u
            | ((uint)(byte)redToBlue << 16)
            | ((uint)(byte)greenToBlue << 8)
            | (byte)greenToRed;

        // Применяем forward cross-color (вычитаем корреляцию) — SIMD + scalar
        for (var y = startY; y < endY; y++)
        {
            var rowPos = y * width + startX;
            var xi = 0;

            if (Vector256.IsHardwareAccelerated)
            {
                var w0xFF = Vector256.Create(0xFFu);
                var w0x80 = Vector256.Create(0x80u);
                var wGA = Vector256.Create(0xFF00FF00u);
                var wGTR = Vector256.Create((int)greenToRed);
                var wGTB = Vector256.Create((int)greenToBlue);
                var wRTB = Vector256.Create((int)redToBlue);

                for (; xi + 8 <= tileWidth; xi += 8)
                {
                    var pos = (nuint)(uint)(rowPos + xi);
                    var px = Vector256.LoadUnsafe(ref ccPixRef, pos);

                    var g = (px >>> 8) & w0xFF;
                    var r = (px >>> 16) & w0xFF;
                    var b = px & w0xFF;

                    var sg = (g ^ w0x80).AsInt32() - w0x80.AsInt32();
                    var sr = (r ^ w0x80).AsInt32() - w0x80.AsInt32();

                    var dR = (wGTR * sg) >> 5;
                    var dGB = (wGTB * sg) >> 5;
                    var dRB = (wRTB * sr) >> 5;

                    var newR = (r.AsInt32() - dR) & w0xFF.AsInt32();
                    var newB = (b.AsInt32() - dGB - dRB) & w0xFF.AsInt32();

                    var result = (px & wGA) | (newR.AsUInt32() << 16) | newB.AsUInt32();
                    Vector256.StoreUnsafe(result, ref ccPixRef, pos);
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                var v0xFF = Vector128.Create(0xFFu);
                var v0x80 = Vector128.Create(0x80u);
                var vGA = Vector128.Create(0xFF00FF00u);
                var vGTR = Vector128.Create((int)greenToRed);
                var vGTB = Vector128.Create((int)greenToBlue);
                var vRTB = Vector128.Create((int)redToBlue);

                for (; xi + 4 <= tileWidth; xi += 4)
                {
                    var pos = (nuint)(uint)(rowPos + xi);
                    var px = Vector128.LoadUnsafe(ref ccPixRef, pos);

                    var g = (px >>> 8) & v0xFF;
                    var r = (px >>> 16) & v0xFF;
                    var b = px & v0xFF;

                    var sg = (g ^ v0x80).AsInt32() - v0x80.AsInt32();
                    var sr = (r ^ v0x80).AsInt32() - v0x80.AsInt32();

                    var dR = (vGTR * sg) >> 5;
                    var dGB = (vGTB * sg) >> 5;
                    var dRB = (vRTB * sr) >> 5;

                    var newR = (r.AsInt32() - dR) & v0xFF.AsInt32();
                    var newB = (b.AsInt32() - dGB - dRB) & v0xFF.AsInt32();

                    var result = (px & vGA) | (newR.AsUInt32() << 16) | newB.AsUInt32();
                    Vector128.StoreUnsafe(result, ref ccPixRef, pos);
                }
            }

            for (; xi < tileWidth; xi++)
            {
                var pos = rowPos + xi;
                var argb = Unsafe.Add(ref ccPixRef, pos);
                var green = (int)((argb >> 8) & 0xFF);
                var red = (int)((argb >> 16) & 0xFF);
                var blue = (int)(argb & 0xFF);

                var signedGreen = (sbyte)(byte)green;
                var origSignedRed = (sbyte)(byte)red;
                red -= ColorTransformDelta(greenToRed, signedGreen);
                red &= 0xFF;
                blue -= ColorTransformDelta(greenToBlue, signedGreen);
                blue -= ColorTransformDelta(redToBlue, origSignedRed);
                blue &= 0xFF;

                Unsafe.Add(ref ccPixRef, pos) = (argb & 0xFF00FF00) | ((uint)red << 16) | (uint)blue;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ColorTransformDelta(sbyte t, sbyte c) => (t * c) >> 5;

    #endregion

    #region LZ77 Matching

    /// <summary>
    /// Fused LZ77 hash-chain + frequency collection за один проход.
    /// Находит backward references И одновременно собирает частоты символов + color cache simulation.
    /// </summary>
    /// <returns>Количество токенов.</returns>
    private static int FindLz77MatchesAndFrequencies(
        ReadOnlySpan<uint> pixels, Span<Lz77Token> tokens, int imageWidth,
        int colorCacheHashShift, int colorCacheSize,
        Span<uint> greenFreq, Span<uint> redFreq, Span<uint> blueFreq, Span<uint> alphaFreq,
        Span<uint> distFreq)
    {
        var totalPixels = pixels.Length;
        if (totalPixels < Lz77MinMatchLength)
        {
            var count = 0;
            for (var i = 0; i < totalPixels; i++)
            {
                var argb = pixels[i];
                greenFreq[(int)((argb >> 8) & 0xFF)]++;
                redFreq[(int)((argb >> 16) & 0xFF)]++;
                blueFreq[(int)(argb & 0xFF)]++;
                alphaFreq[(int)((argb >> 24) & 0xFF)]++;
                tokens[count++] = new Lz77Token { IsBackRef = false, Distance = -1 };
            }

            return count;
        }

        var hashTableSize = 1 << Lz77HashBits;
        var hashPool = ArrayPool<int>.Shared.Rent(hashTableSize);
        var chainPool = ArrayPool<int>.Shared.Rent(Lz77WindowSize);
        var cachePool = ArrayPool<uint>.Shared.Rent(colorCacheSize);
        try
        {
            var hashTable = hashPool.AsSpan(0, hashTableSize);
            var chain = chainPool.AsSpan(0, Lz77WindowSize);
            var cache = cachePool.AsSpan(0, colorCacheSize);
            hashTable.Fill(-1);
            chain.Fill(-1);
            cache.Clear();

            ref var hashRef = ref MemoryMarshal.GetReference(hashTable);
            ref var chainRef = ref MemoryMarshal.GetReference(chain);
            ref var pixRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(pixels));
            ref var tokRef = ref MemoryMarshal.GetReference(tokens);
            ref var cacheBase = ref MemoryMarshal.GetReference(cache);
            ref var gfBase = ref MemoryMarshal.GetReference(greenFreq);
            ref var rfBase = ref MemoryMarshal.GetReference(redFreq);
            ref var bfBase = ref MemoryMarshal.GetReference(blueFreq);
            ref var afBase = ref MemoryMarshal.GetReference(alphaFreq);
            ref var dfBase = ref MemoryMarshal.GetReference(distFreq);

            var tokenCount = 0;
            var pos = 0;

            while (pos < totalPixels)
            {
                var bestLength = 0;
                var bestDistance = 0;

                if (pos + Lz77MinMatchLength <= totalPixels)
                {
                    var h = HashPixels(ref pixRef, pos);
                    var candidate = Unsafe.Add(ref hashRef, h);
                    var maxChainLength = 8; // shallow chain for real-time

                    const int chainMask = Lz77WindowSize - 1;
                    while (candidate >= 0 && maxChainLength > 0)
                    {
                        var distance = pos - candidate;
                        if (distance > Lz77WindowSize) break;
                        var nextCandidate = Unsafe.Add(ref chainRef, candidate & chainMask);

                        // Prefetch данных следующего кандидата параллельно с match comparison текущего
                        if (Sse.IsSupported && nextCandidate >= 0)
                        {
                            unsafe
                            {
                                Sse.Prefetch0(Unsafe.AsPointer(ref Unsafe.Add(ref pixRef, nextCandidate)));
                            }
                        }

                        // Quick first-pixel reject — eliminates ~80% of non-matching candidates
                        if (Unsafe.Add(ref pixRef, candidate) != Unsafe.Add(ref pixRef, pos))
                        {
                            candidate = nextCandidate;
                            maxChainLength--;
                            continue;
                        }

                        var matchLen = 0;
                        var maxLen = Math.Min(Lz77MaxMatchLength, totalPixels - pos);

                        // SIMD: Vector256 (8 пикселей), fallback Vector128 (4 пикселя)
                        if (Vector256.IsHardwareAccelerated)
                        {
                            var simdDone = false;
                            while (matchLen + 8 <= maxLen)
                            {
                                var c = Vector256.LoadUnsafe(ref pixRef, (uint)(candidate + matchLen));
                                var p = Vector256.LoadUnsafe(ref pixRef, (uint)(pos + matchLen));
                                var mask = Vector256.ExtractMostSignificantBits(Vector256.Equals(c.AsByte(), p.AsByte()));
                                if (mask == 0xFFFFFFFFu)
                                {
                                    matchLen += 8;
                                }
                                else
                                {
                                    matchLen += BitOperations.TrailingZeroCount(~mask) / 4;
                                    simdDone = true;
                                    break;
                                }
                            }

                            if (!simdDone)
                            {
                                while (matchLen < maxLen && Unsafe.Add(ref pixRef, candidate + matchLen) == Unsafe.Add(ref pixRef, pos + matchLen))
                                {
                                    matchLen++;
                                }
                            }
                        }
                        else if (Vector128.IsHardwareAccelerated)
                        {
                            var simdDone = false;
                            while (matchLen + 4 <= maxLen)
                            {
                                var c = Vector128.LoadUnsafe(ref pixRef, (uint)(candidate + matchLen));
                                var p = Vector128.LoadUnsafe(ref pixRef, (uint)(pos + matchLen));
                                var mask = Vector128.ExtractMostSignificantBits(Vector128.Equals(c.AsByte(), p.AsByte()));
                                if (mask == 0xFFFF)
                                {
                                    matchLen += 4;
                                }
                                else
                                {
                                    matchLen += BitOperations.TrailingZeroCount(~mask) / 4;
                                    simdDone = true;
                                    break;
                                }
                            }

                            if (!simdDone)
                            {
                                while (matchLen < maxLen && Unsafe.Add(ref pixRef, candidate + matchLen) == Unsafe.Add(ref pixRef, pos + matchLen))
                                {
                                    matchLen++;
                                }
                            }
                        }
                        else
                        {
                            while (matchLen < maxLen && Unsafe.Add(ref pixRef, candidate + matchLen) == Unsafe.Add(ref pixRef, pos + matchLen))
                            {
                                matchLen++;
                            }
                        }

                        if (matchLen >= Lz77MinMatchLength && matchLen > bestLength)
                        {
                            bestLength = matchLen;
                            bestDistance = distance;
                            if (matchLen >= 8 || matchLen == maxLen) break;
                        }

                        candidate = nextCandidate;
                        maxChainLength--;
                    }

                    Unsafe.Add(ref chainRef, pos & chainMask) = Unsafe.Add(ref hashRef, h);
                    Unsafe.Add(ref hashRef, h) = pos;
                }

                if (bestLength >= Lz77MinMatchLength)
                {
                    // Back-reference: frequency + distance code + cache update
                    Vp8LConstants.ValueToPrefixCode(bestLength, out var lenPrefix, out _, out _);
                    Unsafe.Add(ref gfBase, Vp8LConstants.NumLiteralSymbols + lenPrefix)++;

                    var distCode = PixelOffsetToDistanceCode(bestDistance, imageWidth);
                    Vp8LConstants.ValueToPrefixCode(distCode, out var distPrefix, out _, out _);
                    Unsafe.Add(ref dfBase, distPrefix)++;

                    for (var j = 0; j < bestLength; j++)
                    {
                        var pixel = Unsafe.Add(ref pixRef, pos + j);
                        var cIdx = (int)((ColorCacheHashMultiplier * pixel) >> colorCacheHashShift);
                        Unsafe.Add(ref cacheBase, cIdx) = pixel;
                    }

                    Unsafe.Add(ref tokRef, tokenCount++) = new Lz77Token
                    {
                        IsBackRef = true,
                        Length = bestLength,
                        Distance = distCode,
                    };

                    pos += bestLength;
                }
                else
                {
                    // Literal: cache check + frequency
                    var argb = Unsafe.Add(ref pixRef, pos);
                    var cacheIdx = (int)((ColorCacheHashMultiplier * argb) >> colorCacheHashShift);

                    if (Unsafe.Add(ref cacheBase, cacheIdx) == argb && pos > 0)
                    {
                        Unsafe.Add(ref gfBase, Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes + cacheIdx)++;
                        Unsafe.Add(ref tokRef, tokenCount++) = new Lz77Token { IsBackRef = false, Distance = cacheIdx };
                    }
                    else
                    {
                        Unsafe.Add(ref gfBase, (int)((argb >> 8) & 0xFF))++;
                        Unsafe.Add(ref rfBase, (int)((argb >> 16) & 0xFF))++;
                        Unsafe.Add(ref bfBase, (int)(argb & 0xFF))++;
                        Unsafe.Add(ref afBase, (int)((argb >> 24) & 0xFF))++;
                        Unsafe.Add(ref tokRef, tokenCount++) = new Lz77Token { IsBackRef = false, Distance = -1 };
                    }

                    Unsafe.Add(ref cacheBase, cacheIdx) = argb;
                    pos++;
                }
            }

            return tokenCount;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(hashPool);
            ArrayPool<int>.Shared.Return(chainPool);
            ArrayPool<uint>.Shared.Return(cachePool);
        }
    }

    /// <summary>
    /// Хеш-функция для 2 пикселей (LZ77 match probing): ref-based (без bounds check).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashPixels(ref uint pixRef, int pos)
    {
        var hash = Unsafe.Add(ref pixRef, pos) * 0x1E35A7BDu;
        hash ^= Unsafe.Add(ref pixRef, pos + 1) * 0x85EBCA6Bu;
        return (int)(hash >> (32 - Lz77HashBits));
    }

    #endregion

    #region Transform Writing

    /// <summary>
    /// Записывает transforms: Predictor → CrossColor → SubtractGreen.
    /// Порядок в битовом потоке обратный порядку применения при декодировании.
    /// </summary>
    private static void WriteTransforms(
        ref BitWriter writer, int sizeBits,
        ReadOnlySpan<uint> predictorImage, int tilesPerRow, int tilesPerCol,
        ReadOnlySpan<uint> colorImage)
    {
        // Transform 1: Predictor
        writer.WriteBit(bit: true); // transform_present = 1
        writer.WriteBits(Vp8LConstants.TransformPredictor, 2); // transform_type = 0
        writer.WriteBits((uint)(sizeBits - 2), 3); // size_bits — декодер прибавит 2
        // Записываем predictor sub-image (кодирование как mini-image)
        WriteSubImage(ref writer, predictorImage, tilesPerRow, tilesPerCol);

        // Transform 2: CrossColor
        writer.WriteBit(bit: true); // transform_present = 1
        writer.WriteBits(Vp8LConstants.TransformColor, 2); // transform_type = 1
        writer.WriteBits((uint)(sizeBits - 2), 3); // size_bits — декодер прибавит 2
        WriteSubImage(ref writer, colorImage, tilesPerRow, tilesPerCol);

        // Transform 3: SubtractGreen
        writer.WriteBit(bit: true); // transform_present = 1
        writer.WriteBits(Vp8LConstants.TransformSubtractGreen, 2); // transform_type = 2

        // Конец преобразований
        writer.WriteBit(bit: false); // transform_present = 0
    }

    /// <summary>
    /// Записывает sub-image (predictor или cross-color) как мини VP8L изображение.
    /// </summary>
    private static void WriteSubImage(ref BitWriter writer, ReadOnlySpan<uint> image, int width, int height)
    {
        var totalPixels = width * height;

        // Для sub-image: собираем частоты по 4 каналам (partial histograms + SIMD merge)
        Span<uint> gFreq = stackalloc uint[256];
        Span<uint> rFreq = stackalloc uint[256];
        Span<uint> bFreq = stackalloc uint[256];
        Span<uint> aFreq = stackalloc uint[256];

        // Вторые partial-гистограммы для снижения pipeline-столлов при совпадении бинов
        Span<uint> g1 = stackalloc uint[256];
        Span<uint> r1 = stackalloc uint[256];
        Span<uint> b1 = stackalloc uint[256];
        Span<uint> a1 = stackalloc uint[256];

        ref var imgRef = ref MemoryMarshal.GetReference(image);
        ref var gBase0 = ref MemoryMarshal.GetReference(gFreq);
        ref var rBase0 = ref MemoryMarshal.GetReference(rFreq);
        ref var bBase0 = ref MemoryMarshal.GetReference(bFreq);
        ref var aBase0 = ref MemoryMarshal.GetReference(aFreq);
        ref var gBase1 = ref MemoryMarshal.GetReference(g1);
        ref var rBase1 = ref MemoryMarshal.GetReference(r1);
        ref var bBase1 = ref MemoryMarshal.GetReference(b1);
        ref var aBase1 = ref MemoryMarshal.GetReference(a1);

        var idx = 0;
        for (; idx + 2 <= totalPixels; idx += 2)
        {
            var p0 = Unsafe.Add(ref imgRef, idx);
            var p1 = Unsafe.Add(ref imgRef, idx + 1);

            Unsafe.Add(ref gBase0, (int)((p0 >> 8) & 0xFF))++;
            Unsafe.Add(ref gBase1, (int)((p1 >> 8) & 0xFF))++;
            Unsafe.Add(ref rBase0, (int)((p0 >> 16) & 0xFF))++;
            Unsafe.Add(ref rBase1, (int)((p1 >> 16) & 0xFF))++;
            Unsafe.Add(ref bBase0, (int)(p0 & 0xFF))++;
            Unsafe.Add(ref bBase1, (int)(p1 & 0xFF))++;
            Unsafe.Add(ref aBase0, (int)((p0 >> 24) & 0xFF))++;
            Unsafe.Add(ref aBase1, (int)((p1 >> 24) & 0xFF))++;
        }

        for (; idx < totalPixels; idx++)
        {
            var argb = Unsafe.Add(ref imgRef, idx);
            Unsafe.Add(ref gBase0, (int)((argb >> 8) & 0xFF))++;
            Unsafe.Add(ref rBase0, (int)((argb >> 16) & 0xFF))++;
            Unsafe.Add(ref bBase0, (int)(argb & 0xFF))++;
            Unsafe.Add(ref aBase0, (int)((argb >> 24) & 0xFF))++;
        }

        // SIMD merge partial → primary гистограммы
        if (Vector256.IsHardwareAccelerated)
        {
            for (var j = 0; j < 256; j += 8)
            {
                var off = (nuint)(uint)j;
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref gBase0, off) + Vector256.LoadUnsafe(ref gBase1, off), ref gBase0, off);
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref rBase0, off) + Vector256.LoadUnsafe(ref rBase1, off), ref rBase0, off);
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref bBase0, off) + Vector256.LoadUnsafe(ref bBase1, off), ref bBase0, off);
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref aBase0, off) + Vector256.LoadUnsafe(ref aBase1, off), ref aBase0, off);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            for (var j = 0; j < 256; j += 4)
            {
                var off = (nuint)(uint)j;
                Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref gBase0, off) + Vector128.LoadUnsafe(ref gBase1, off), ref gBase0, off);
                Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref rBase0, off) + Vector128.LoadUnsafe(ref rBase1, off), ref rBase0, off);
                Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref bBase0, off) + Vector128.LoadUnsafe(ref bBase1, off), ref bBase0, off);
                Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref aBase0, off) + Vector128.LoadUnsafe(ref aBase1, off), ref aBase0, off);
            }
        }
        else
        {
            for (var j = 0; j < 256; j++)
            {
                Unsafe.Add(ref gBase0, j) += Unsafe.Add(ref gBase1, j);
                Unsafe.Add(ref rBase0, j) += Unsafe.Add(ref rBase1, j);
                Unsafe.Add(ref bBase0, j) += Unsafe.Add(ref bBase1, j);
                Unsafe.Add(ref aBase0, j) += Unsafe.Add(ref aBase1, j);
            }
        }

        // Строим Huffman коды для 5 таблиц (green без LZ77, distance пустой)
        var greenSize = Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes;
        // Расширяем green частоты до полного алфавита
        Span<uint> greenFreq = stackalloc uint[greenSize];
        gFreq[..256].CopyTo(greenFreq);

        var greenCL = new byte[greenSize];
        var redCL = new byte[256];
        var blueCL = new byte[256];
        var alphaCL = new byte[256];
        var distCL = new byte[Vp8LConstants.AlphabetSizeDistance];

        HuffmanTreeBuilder.BuildFromFrequencies(greenFreq, greenCL, MaxCodeLength);
        HuffmanTreeBuilder.BuildFromFrequencies(rFreq, redCL, MaxCodeLength);
        HuffmanTreeBuilder.BuildFromFrequencies(bFreq, blueCL, MaxCodeLength);
        HuffmanTreeBuilder.BuildFromFrequencies(aFreq, alphaCL, MaxCodeLength);

        var greenCodes = new uint[greenSize];
        var redCodes = new uint[256];
        var blueCodes = new uint[256];
        var alphaCodes = new uint[256];

        HuffmanTreeBuilder.BuildEncodeCodes(greenCL, greenCodes, lsbFirst: true);
        HuffmanTreeBuilder.BuildEncodeCodes(redCL, redCodes, lsbFirst: true);
        HuffmanTreeBuilder.BuildEncodeCodes(blueCL, blueCodes, lsbFirst: true);
        HuffmanTreeBuilder.BuildEncodeCodes(alphaCL, alphaCodes, lsbFirst: true);

        // Sub-image: no color cache (use_meta не записывается — это только для основного изображения)
        writer.WriteBit(bit: false); // use_color_cache = 0

        // 5 Huffman таблиц
        WriteHuffmanTable(ref writer, greenCL, greenSize);
        WriteHuffmanTable8(ref writer, redCL);
        WriteHuffmanTable8(ref writer, blueCL);
        WriteHuffmanTable8(ref writer, alphaCL);
        WriteHuffmanTable8(ref writer, distCL);

        ClearSingleSymbolCodes(greenCL, greenCodes);
        ClearSingleSymbolCodes(redCL, redCodes);
        ClearSingleSymbolCodes(blueCL, blueCodes);
        ClearSingleSymbolCodes(alphaCL, alphaCodes);

        // Записываем пиксели sub-image (unroll 2x + Unsafe refs)
        ref var imgRef2 = ref MemoryMarshal.GetReference(image);
        ref var glRef = ref MemoryMarshal.GetArrayDataReference(greenCL);
        ref var gcRef = ref MemoryMarshal.GetArrayDataReference(greenCodes);
        ref var rlRef = ref MemoryMarshal.GetArrayDataReference(redCL);
        ref var rcRef = ref MemoryMarshal.GetArrayDataReference(redCodes);
        ref var blRef = ref MemoryMarshal.GetArrayDataReference(blueCL);
        ref var bcRef = ref MemoryMarshal.GetArrayDataReference(blueCodes);
        ref var alRef = ref MemoryMarshal.GetArrayDataReference(alphaCL);
        ref var acRef = ref MemoryMarshal.GetArrayDataReference(alphaCodes);

        var i2 = 0;
        for (; i2 + 2 <= totalPixels; i2 += 2)
        {
            var argb0 = Unsafe.Add(ref imgRef2, i2);
            var argb1 = Unsafe.Add(ref imgRef2, i2 + 1);

            var green0 = (int)((argb0 >> 8) & 0xFF);
            var red0 = (int)((argb0 >> 16) & 0xFF);
            var green1 = (int)((argb1 >> 8) & 0xFF);
            var red1 = (int)((argb1 >> 16) & 0xFF);

            var greenLen0 = (int)Unsafe.Add(ref glRef, green0);
            var redLen0 = (int)Unsafe.Add(ref rlRef, red0);
            writer.WriteBitsLsb(
                Unsafe.Add(ref gcRef, green0) | (Unsafe.Add(ref rcRef, red0) << greenLen0),
                greenLen0 + redLen0);

            var blue0 = (int)(argb0 & 0xFF);
            var alpha0 = (int)((argb0 >> 24) & 0xFF);
            var blueLen0 = (int)Unsafe.Add(ref blRef, blue0);
            var alphaLen0 = (int)Unsafe.Add(ref alRef, alpha0);
            writer.WriteBitsLsb(
                Unsafe.Add(ref bcRef, blue0) | (Unsafe.Add(ref acRef, alpha0) << blueLen0),
                blueLen0 + alphaLen0);

            var greenLen1 = (int)Unsafe.Add(ref glRef, green1);
            var redLen1 = (int)Unsafe.Add(ref rlRef, red1);
            writer.WriteBitsLsb(
                Unsafe.Add(ref gcRef, green1) | (Unsafe.Add(ref rcRef, red1) << greenLen1),
                greenLen1 + redLen1);

            var blue1 = (int)(argb1 & 0xFF);
            var alpha1 = (int)((argb1 >> 24) & 0xFF);
            var blueLen1 = (int)Unsafe.Add(ref blRef, blue1);
            var alphaLen1 = (int)Unsafe.Add(ref alRef, alpha1);
            writer.WriteBitsLsb(
                Unsafe.Add(ref bcRef, blue1) | (Unsafe.Add(ref acRef, alpha1) << blueLen1),
                blueLen1 + alphaLen1);
        }

        for (; i2 < totalPixels; i2++)
        {
            var argb = Unsafe.Add(ref imgRef2, i2);
            var green = (int)((argb >> 8) & 0xFF);
            var red = (int)((argb >> 16) & 0xFF);
            var blue = (int)(argb & 0xFF);
            var alpha = (int)((argb >> 24) & 0xFF);

            var greenLen = (int)Unsafe.Add(ref glRef, green);
            var redLen = (int)Unsafe.Add(ref rlRef, red);
            writer.WriteBitsLsb(
                Unsafe.Add(ref gcRef, green) | (Unsafe.Add(ref rcRef, red) << greenLen),
                greenLen + redLen);

            var blueLen = (int)Unsafe.Add(ref blRef, blue);
            var alphaLen = (int)Unsafe.Add(ref alRef, alpha);
            writer.WriteBitsLsb(
                Unsafe.Add(ref bcRef, blue) | (Unsafe.Add(ref acRef, alpha) << blueLen),
                blueLen + alphaLen);
        }
    }

    #endregion

    #region Huffman Table Writing

    /// <summary>
    /// Обнуляет codeLengths и codes для таблиц с единственным символом.
    /// VP8L simple code с 1 символом: декодер возвращает символ без чтения бит из потока.
    /// </summary>
    private static void ClearSingleSymbolCodes(Span<byte> codeLengths, Span<uint> codes)
    {
        var nonzeroCount = 0;
        var sym = -1;
        for (var i = 0; i < codeLengths.Length; i++)
        {
            if (codeLengths[i] != 0)
            {
                nonzeroCount++;
                sym = i;
                if (nonzeroCount > 1) return; // 2+ символов — кодлены корректны
            }
        }

        if (nonzeroCount == 1)
        {
            codeLengths[sym] = 0;
            codes[sym] = 0;
        }
    }

    /// <summary>
    /// Записывает Huffman таблицу (до 280 символов, для Green+Length).
    /// </summary>
    private static void WriteHuffmanTable(ref BitWriter writer, ReadOnlySpan<byte> codeLengths, int alphabetSize)
    {
        // Проверяем: можно ли использовать simple code?
        var nonzeroCount = 0;
        var sym0 = -1;
        var sym1 = -1;

        for (var i = 0; i < alphabetSize; i++)
        {
            if (codeLengths[i] != 0)
            {
                nonzeroCount++;
                if (nonzeroCount == 1) sym0 = i;
                else if (nonzeroCount == 2) sym1 = i;
                else break;
            }
        }

        if (nonzeroCount <= 2)
        {
            WriteSimpleHuffmanCode(ref writer, nonzeroCount, sym0, sym1);
            return;
        }

        WriteNormalHuffmanCode(ref writer, codeLengths, alphabetSize);
    }

    /// <summary>
    /// Записывает 8-bit Huffman таблицу (256 символов, для R/B/A/Dist).
    /// </summary>
    private static void WriteHuffmanTable8(ref BitWriter writer, ReadOnlySpan<byte> codeLengths) =>
        WriteHuffmanTable(ref writer, codeLengths, codeLengths.Length);

    /// <summary>
    /// Записывает simple Huffman code (1-2 символа).
    /// </summary>
    private static void WriteSimpleHuffmanCode(ref BitWriter writer, int numSymbols, int sym0, int sym1)
    {
        writer.WriteBit(bit: true); // is_simple = 1

        if (numSymbols <= 1)
        {
            // 1 символ
            writer.WriteBits(0, 1); // num_symbols - 1 = 0
            var symbolVal = numSymbols == 0 ? 0 : sym0;
            var is8bit = symbolVal > 1;
            writer.WriteBit(bit: is8bit); // first_symbol_bits_used: 0=1bit, 1=8bit
            writer.WriteBits((uint)symbolVal, is8bit ? 8 : 1);
        }
        else
        {
            // 2 символа
            writer.WriteBits(1, 1); // num_symbols - 1 = 1
            var is8bit = sym0 > 1;
            writer.WriteBit(bit: is8bit);
            writer.WriteBits((uint)sym0, is8bit ? 8 : 1);
            writer.WriteBits((uint)sym1, 8);
        }
    }

    /// <summary>
    /// Записывает normal Huffman code (полная таблица через CL мета-таблицу).
    /// </summary>
    private static void WriteNormalHuffmanCode(ref BitWriter writer, ReadOnlySpan<byte> codeLengths, int alphabetSize)
    {
        writer.WriteBit(bit: false); // is_simple = 0

        // Кодируем code lengths через symbols 0-18 (как в DEFLATE)
        var clSymbols = new byte[alphabetSize * 2]; // RLE может расширить
        var clExtra = new int[alphabetSize * 2];
        var clCount = EncodeCodeLengths(codeLengths[..alphabetSize], clSymbols, clExtra);

        // Собираем частоты CL символов
        Span<uint> clFreq = stackalloc uint[NumCodeLengthCodes];
        for (var i = 0; i < clCount; i++)
        {
            clFreq[clSymbols[i]]++;
        }

        // Строим Huffman коды для CL символов
        Span<byte> clCodeLengths = stackalloc byte[NumCodeLengthCodes];
        HuffmanTreeBuilder.BuildFromFrequencies(clFreq, clCodeLengths, ClTableLog);

        Span<uint> clCodes = stackalloc uint[NumCodeLengthCodes];
        HuffmanTreeBuilder.BuildEncodeCodes(clCodeLengths, clCodes, lsbFirst: true);

        // Определяем numCodeLengthCodes: сколько CL записей нужно (по порядку CodeLengthCodeOrder)
        var numClCodes = NumCodeLengthCodes;
        while (numClCodes > 4 && clCodeLengths[Vp8LConstants.CodeLengthCodeOrder[numClCodes - 1]] == 0)
        {
            numClCodes--;
        }

        // Записываем: numCodeLengthCodes - 4 (4 бита)
        writer.WriteBitsLsb((uint)(numClCodes - 4), 4);

        // Записываем CL code lengths (по 3 бита каждый, в CodeLengthCodeOrder)
        for (var i = 0; i < numClCodes; i++)
        {
            writer.WriteBitsLsb(clCodeLengths[Vp8LConstants.CodeLengthCodeOrder[i]], 3);
        }

        // Записываем max_symbol если нужно
        // Для baseline: max_symbol == alphabetSize, не пишем (бит = 0)
        writer.WriteBit(bit: false); // has_max_symbol = 0

        // Записываем code lengths основного алфавита через CL Huffman коды
        for (var i = 0; i < clCount; i++)
        {
            var sym = clSymbols[i];
            writer.WriteBitsLsb(clCodes[sym], clCodeLengths[sym]);

            // Extra bits для символов 16, 17, 18
            if (sym == 16)
            {
                writer.WriteBitsLsb((uint)clExtra[i], 2); // repeat count - 3
            }
            else if (sym == 17)
            {
                writer.WriteBitsLsb((uint)clExtra[i], 3); // zero count - 3
            }
            else if (sym == 18)
            {
                writer.WriteBitsLsb((uint)clExtra[i], 7); // zero count - 11
            }
        }
    }

    /// <summary>
    /// Кодирует массив code lengths в символы 0-18 (RLE-кодирование, как в DEFLATE).
    /// </summary>
    /// <returns>Количество CL символов.</returns>
    private static int EncodeCodeLengths(ReadOnlySpan<byte> codeLengths, Span<byte> clSymbols, Span<int> clExtra)
    {
        var count = 0;
        var i = 0;
        byte prevLength = 0;

        while (i < codeLengths.Length)
        {
            var length = codeLengths[i];

            if (length == 0)
            {
                // Считаем количество подряд идущих нулей
                var zeroRun = 0;
                while (i + zeroRun < codeLengths.Length && codeLengths[i + zeroRun] == 0)
                {
                    zeroRun++;
                }

                while (zeroRun > 0)
                {
                    if (zeroRun >= 11)
                    {
                        // Symbol 18: 11-138 нулей
                        var repeat = Math.Min(zeroRun, 138);
                        clSymbols[count] = 18;
                        clExtra[count] = repeat - 11;
                        count++;
                        zeroRun -= repeat;
                        i += repeat;
                    }
                    else if (zeroRun >= 3)
                    {
                        // Symbol 17: 3-10 нулей
                        var repeat = Math.Min(zeroRun, 10);
                        clSymbols[count] = 17;
                        clExtra[count] = repeat - 3;
                        count++;
                        zeroRun -= repeat;
                        i += repeat;
                    }
                    else
                    {
                        // Одиночные нули — как литерал 0
                        clSymbols[count] = 0;
                        clExtra[count] = 0;
                        count++;
                        zeroRun--;
                        i++;
                    }
                }

                prevLength = 0;
            }
            else if (length == prevLength && i > 0)
            {
                // Symbol 16: повтор предыдущей длины 3-6 раз
                var repeatRun = 0;
                while (i + repeatRun < codeLengths.Length && codeLengths[i + repeatRun] == prevLength)
                {
                    repeatRun++;
                }

                if (repeatRun >= 3)
                {
                    while (repeatRun >= 3)
                    {
                        var repeat = Math.Min(repeatRun, 6);
                        clSymbols[count] = 16;
                        clExtra[count] = repeat - 3;
                        count++;
                        repeatRun -= repeat;
                        i += repeat;
                    }

                    // Оставшиеся < 3 — как литералы
                    while (repeatRun > 0)
                    {
                        clSymbols[count] = length;
                        clExtra[count] = 0;
                        count++;
                        repeatRun--;
                        i++;
                    }
                }
                else
                {
                    // Короткий повтор — литерал
                    clSymbols[count] = length;
                    clExtra[count] = 0;
                    count++;
                    prevLength = length;
                    i++;
                }
            }
            else
            {
                // Литеральная длина кода (1-15)
                clSymbols[count] = length;
                clExtra[count] = 0;
                count++;
                prevLength = length;
                i++;
            }
        }

        return count;
    }

    #endregion

    #region Pixel Data Writing

    /// <summary>
    /// Записывает LZ77 + literal данные в битовый поток.
    /// Использует предвычисленные distCode и cache hit/miss из CollectLz77Frequencies
    /// (backref: Distance = distCode, literal: Distance ≥ 0 = cache hit cacheIdx, -1 = miss).
    /// </summary>
    private static void WriteLz77Data(
        ref BitWriter writer,
        ReadOnlySpan<uint> pixels,
        ReadOnlySpan<Lz77Token> tokens, int tokenCount,
        ReadOnlySpan<byte> greenLengths, ReadOnlySpan<uint> greenCodes,
        ReadOnlySpan<byte> redLengths, ReadOnlySpan<uint> redCodes,
        ReadOnlySpan<byte> blueLengths, ReadOnlySpan<uint> blueCodes,
        ReadOnlySpan<byte> alphaLengths, ReadOnlySpan<uint> alphaCodes,
        ReadOnlySpan<byte> distLengths, ReadOnlySpan<uint> distCodes)
    {
        ref var pxRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(pixels));
        ref var tokRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(tokens));
        ref var glRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(greenLengths));
        ref var gcRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(greenCodes));
        ref var rlRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(redLengths));
        ref var rcRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(redCodes));
        ref var blRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(blueLengths));
        ref var bcRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(blueCodes));
        ref var alRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(alphaLengths));
        ref var acRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(alphaCodes));
        ref var dlRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(distLengths));
        ref var dcRef = ref Unsafe.AsRef(in MemoryMarshal.GetReference(distCodes));

        var pixelPos = 0;
        for (var i = 0; i < tokenCount; i++)
        {
            ref readonly var token = ref Unsafe.Add(ref tokRef, i);

            if (token.IsBackRef)
            {
                // Length: мержим Huffman code + extra bits в один write (max 11+6=17 бит)
                Vp8LConstants.ValueToPrefixCode(token.Length, out var lenPrefix, out var lenExtra, out var lenExtraBits);
                var greenSym = Vp8LConstants.NumLiteralSymbols + lenPrefix;
                var lenCodeLen = (int)Unsafe.Add(ref glRef, greenSym);
                writer.WriteBitsLsb(
                    Unsafe.Add(ref gcRef, greenSym) | ((uint)lenExtra << lenCodeLen),
                    lenCodeLen + lenExtraBits);

                // Distance: мержим Huffman code + extra bits в один write (max 11+11=22 бит)
                Vp8LConstants.ValueToPrefixCode(token.Distance, out var distPrefix, out var distExtra, out var distExtraBits);
                var distCodeLen = (int)Unsafe.Add(ref dlRef, distPrefix);
                writer.WriteBitsLsb(
                    Unsafe.Add(ref dcRef, distPrefix) | ((uint)distExtra << distCodeLen),
                    distCodeLen + distExtraBits);

                pixelPos += token.Length;
            }
            else
            {
                // Cache hit/miss — предвычислено в CollectLz77Frequencies
                var cacheIdx = token.Distance;
                if (cacheIdx >= 0)
                {
                    // Color cache hit
                    var cacheSym = Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes + cacheIdx;
                    writer.WriteBitsLsb(Unsafe.Add(ref gcRef, cacheSym), Unsafe.Add(ref glRef, cacheSym));
                }
                else
                {
                    // Литеральный пиксель — мержим все 4 канала в 1–2 WriteBitsLsb
                    var argb = Unsafe.Add(ref pxRef, pixelPos);
                    var green = (int)((argb >> 8) & 0xFF);
                    var red = (int)((argb >> 16) & 0xFF);
                    var blue = (int)(argb & 0xFF);
                    var alpha = (int)((argb >> 24) & 0xFF);

                    var greenLen = (int)Unsafe.Add(ref glRef, green);
                    var redLen = (int)Unsafe.Add(ref rlRef, red);
                    var blueLen = (int)Unsafe.Add(ref blRef, blue);
                    var alphaLen = (int)Unsafe.Add(ref alRef, alpha);
                    var totalLen = greenLen + redLen + blueLen + alphaLen;

                    if (totalLen <= 32)
                    {
                        var bits = Unsafe.Add(ref gcRef, green)
                            | (Unsafe.Add(ref rcRef, red) << greenLen)
                            | (Unsafe.Add(ref bcRef, blue) << (greenLen + redLen))
                            | (Unsafe.Add(ref acRef, alpha) << (greenLen + redLen + blueLen));
                        writer.WriteBitsLsb(bits, totalLen);
                    }
                    else
                    {
                        writer.WriteBitsLsb(
                            Unsafe.Add(ref gcRef, green) | (Unsafe.Add(ref rcRef, red) << greenLen),
                            greenLen + redLen);
                        writer.WriteBitsLsb(
                            Unsafe.Add(ref bcRef, blue) | (Unsafe.Add(ref acRef, alpha) << blueLen),
                            blueLen + alphaLen);
                    }
                }

                pixelPos++;
            }
        }
    }

    #endregion

    #region Container Writing

    /// <summary>
    /// Записывает RIFF/WEBP контейнер + VP8L chunk.
    /// </summary>
    private static CodecResult WriteContainer(
        Span<byte> output, int width, int height, bool hasAlpha,
        ReadOnlySpan<byte> bitstream, out int bytesWritten)
    {
        var bitstreamSize = bitstream.Length;
        var vp8lChunkDataSize = Vp8LConstants.HeaderSize + bitstreamSize;
        var riffFileSize = 4 + 8 + vp8lChunkDataSize + (vp8lChunkDataSize & 1);
        var totalOutputSize = 8 + riffFileSize;

        if (output.Length < totalOutputSize)
        {
            bytesWritten = 0;
            return CodecResult.OutputBufferTooSmall;
        }

        var pos = 0;

        WriteUInt32Le(output, ref pos, 0x46464952); // "RIFF"
        WriteUInt32Le(output, ref pos, (uint)riffFileSize);
        WriteUInt32Le(output, ref pos, 0x50424557); // "WEBP"
        WriteUInt32Le(output, ref pos, 0x4C385056); // "VP8L"
        WriteUInt32Le(output, ref pos, (uint)vp8lChunkDataSize);

        output[pos++] = Vp8LConstants.Signature;

        var packed = (uint)(width - 1)
                   | ((uint)(height - 1) << 14)
                   | (hasAlpha ? 1u << 28 : 0u)
                   | ((uint)Vp8LConstants.FormatVersion << 29);

        WriteUInt32Le(output, ref pos, packed);

        bitstream.CopyTo(output[pos..]);
        pos += bitstreamSize;

        if ((vp8lChunkDataSize & 1) != 0)
            output[pos++] = 0;

        bytesWritten = pos;
        return CodecResult.Success;
    }

    #endregion

    #region Distance Coding

    /// <summary>
    /// Статическая таблица обратного маппинга (dx, dy) → code для distance map.
    /// Индексация: [dy * 17 + (dx + 8)], dx ∈ [-8..8], dy ∈ [0..8].
    /// Значение 0 = нет в distance map.
    /// </summary>
    private static ReadOnlySpan<byte> ReverseDistanceMap =>
    [
        // dy=0: dx=-8..-1, 0, 1..8
        0,   0,   0,   0,   0,   0,   0,   0,   0,   2,   0,   0,   0,   0,   0,   0,   0,
        // dy=1: dx=-8..8
        0,  28,  18,  10,   4,   8,  20,  32,  96,   1,   3,   9,  17,  27,  97,   0,   0,
        // dy=2: dx=-8..8
        0,  56,  22,  12,   8,  42,  30,  58,  98,   5,   7,  41,  21,  55,   0,   0,   0,
        // dy=3: dx=-8..8
        0,  74,  24,  20,  36,  38,  50,  72,  99,  13,  15,  37,  35,  49,  73,   0,   0,
        // dy=4: dx=-8..8
        0,  80,  70,  32,  46,  60,  62,  78,  92,  23,  25,  59,  45,  69,  79,   0,   0,
        // dy=5: dx=-8..8
        0,  90,  52,  44,  48,  64,  86,  88, 100,  33,  39,  85,  47,  51,  89,   0,   0,
        // dy=6: dx=-8..8
        0, 112,  82,  54,  76,  84, 104, 110, 106,  49,  53, 103,  75,  81, 111,   0,   0,
        // dy=7: dx=-8..8
        0, 114,  84,  68,  80, 102,  94, 108, 116,  65,  67, 101,  93,  83, 107,   0,   0,
        // dy=8: (all entries from distance map with dy=8 are only positive dx)
        0,   0,   0,   0,   0, 120, 119, 118, 117,  96,  98,   0,   0,   0,   0,   0,   0,
    ];

    /// <summary>
    /// Конвертирует пиксельное смещение в VP8L distance code.
    /// O(1) через статическую reverse lookup таблицу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PixelOffsetToDistanceCode(int pixelOffset, int imageWidth)
    {
        var dy = pixelOffset / imageWidth;
        var dx = pixelOffset - dy * imageWidth;

        // Check 2D distance map (dx ∈ [-8..8], dy ∈ [0..8])
        if ((uint)dy <= 8 && dx >= -8 && dx <= 8)
        {
            var code = ReverseDistanceMap[dy * 17 + dx + 8];
            if (code != 0)
            {
                // Verify the code actually maps to this pixel offset
                var distMap = Vp8LConstants.DistanceMap;
                var mapIdx = (code - 1) * 2;
                var checkDist = distMap[mapIdx] + distMap[mapIdx + 1] * imageWidth;
                if (checkDist < 1) checkDist = 1;
                if (checkDist == pixelOffset) return code;
            }
        }

        return pixelOffset + Vp8LConstants.NumDistanceMapEntries;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Покомпонентное вычитание двух ARGB пикселей (mod 256 на каждый канал).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SubArgb(uint a, uint b)
    {
        var alpha = (((a >> 24) - (b >> 24)) & 0xFF) << 24;
        var red = (((a >> 16) - (b >> 16)) & 0xFF) << 16;
        var green = (((a >> 8) - (b >> 8)) & 0xFF) << 8;
        var blue = (a - b) & 0xFF;
        return alpha | red | green | blue;
    }

    /// <summary>
    /// Записывает 32-bit значение в little-endian.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt32Le(Span<byte> buffer, ref int pos, uint value)
    {
        buffer[pos] = (byte)value;
        buffer[pos + 1] = (byte)(value >> 8);
        buffer[pos + 2] = (byte)(value >> 16);
        buffer[pos + 3] = (byte)(value >> 24);
        pos += 4;
    }

    #endregion
}
