#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, MA0051, MA0084, S1117, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Bgr24.
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants (Bgr24)

    /// <summary>Реализованные ускорители для Cmyk ↔ Bgr24.</summary>
    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromBgr24(Bgr24 bgr) => FromRgb24(new Rgb24(bgr.R, bgr.G, bgr.B));

    /// <summary>Конвертирует Cmyk в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24()
    {
        var rgb = ToRgb24();
        return new Bgr24(rgb.B, rgb.G, rgb.R);
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ Bgr24)

    /// <summary>Пакетная конвертация Bgr24 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → Cmyk с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Bgr24 с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ToBgr24(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
            {
                ToBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromBgr24Sse41(source, destination);
                break;
            default:
                FromBgr24Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToBgr24Core(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToBgr24Sse41(source, destination);
                break;
            default:
                ToBgr24Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgr24Parallel(Bgr24* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgr24Core(new ReadOnlySpan<Bgr24>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgr24Parallel(Cmyk* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgr24Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Bgr24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementations (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgr24Scalar(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgr24(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgr24Scalar(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToBgr24();
        }
    }

    #endregion

    #region SSE41 Implementation (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Sse41(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykSse41Vectors.OneF;
            var c255F = CmykSse41Vectors.C255F;
            var inv255F = CmykSse41Vectors.Inv255F;
            var epsilonF = CmykSse41Vectors.EpsilonF;
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            // Маски для BGR24 деинтерлива (B=0, G=1, R=2)
            var shuffleR = CmykSse41Vectors.ShuffleBgr24R;
            var shuffleG = CmykSse41Vectors.ShuffleBgr24G;
            var shuffleB = CmykSse41Vectors.ShuffleBgr24B;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей BGR24 = 12 байт
                var bgr12 = Sse2.LoadVector128(src);

                // Деинтерлейс BGR24 → R, G, B
                var rBytes = Ssse3.Shuffle(bgr12, shuffleR);
                var gBytes = Ssse3.Shuffle(bgr12, shuffleG);
                var bBytes = Ssse3.Shuffle(bgr12, shuffleB);

                // Конвертация в float
                var rF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(rBytes)), inv255F);
                var gF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(gBytes)), inv255F);
                var bF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(bBytes)), inv255F);

                // k = 1 - max(r, g, b)
                var maxRgb = Sse.Max(Sse.Max(rF, gF), bF);
                var kF = Sse.Subtract(oneF, maxRgb);

                // invK = 1 / max (без Newton-Raphson: rcpps даёт ~12 бит точности, достаточно для 8-бит результата)
                var invK = Sse.Reciprocal(Sse.Max(maxRgb, epsilonF));

                // c, m, y = (max - component) / max
                var cF = Sse.Multiply(Sse.Subtract(maxRgb, rF), invK);
                var mF = Sse.Multiply(Sse.Subtract(maxRgb, gF), invK);
                var yF = Sse.Multiply(Sse.Subtract(maxRgb, bF), invK);

                // Масштабирование в 0-255
                cF = Sse.Multiply(cF, c255F);
                mF = Sse.Multiply(mF, c255F);
                yF = Sse.Multiply(yF, c255F);
                kF = Sse.Multiply(kF, c255F);

                // Конвертация и упаковка
                var cBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(cF).AsByte(), packMask);
                var mBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(mF).AsByte(), packMask);
                var yBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(yF).AsByte(), packMask);
                var kBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(kF).AsByte(), packMask);

                // SIMD интерлив CMYK
                var cm = Sse2.UnpackLow(cBytes, mBytesOut);
                var yk = Sse2.UnpackLow(yBytesOut, kBytesOut);
                var cmyk = Sse2.UnpackLow(cm.AsUInt16(), yk.AsUInt16()).AsByte();
                Sse2.Store(dst, cmyk);

                src += 12;
                dst += 16;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                var bgr = new Bgr24(src[0], src[1], src[2]);
                var cmyk = FromBgr24(bgr);
                dst[0] = cmyk.C; dst[1] = cmyk.M; dst[2] = cmyk.Y; dst[3] = cmyk.K;
                src += 3;
                dst += 4;
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Sse41(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykSse41Vectors.OneF;
            var c255F = CmykSse41Vectors.C255F;
            var inv255F = CmykSse41Vectors.Inv255F;
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;

            while (i + 4 <= count)
            {
                var cmyk16 = Sse2.LoadVector128(src);

                var cBytes = Ssse3.Shuffle(cmyk16, shuffleC);
                var mBytes = Ssse3.Shuffle(cmyk16, shuffleM);
                var yBytes = Ssse3.Shuffle(cmyk16, shuffleY);
                var kBytes = Ssse3.Shuffle(cmyk16, shuffleK);

                var cF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(cBytes)), inv255F);
                var mF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(mBytes)), inv255F);
                var yF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(yBytes)), inv255F);
                var kF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(kBytes)), inv255F);

                var oneMinusK = Sse.Subtract(oneF, kF);
                var rF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, cF), oneMinusK), c255F);
                var gF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, mF), oneMinusK), c255F);
                var bF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, yF), oneMinusK), c255F);

                var rBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(rF).AsByte(), packMask);
                var gBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(gF).AsByte(), packMask);
                var bBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(bF).AsByte(), packMask);

                // SIMD интерлив BGR24: [B0 B1 ...] + [G0 G1 ...] → [B0 G0 B1 G1 ...]
                var zeros = Vector128<byte>.Zero;
                var bg = Sse2.UnpackLow(bBytesOut, gBytesOut);           // B0G0 B1G1 B2G2 B3G3 ...
                var r0 = Sse2.UnpackLow(rBytesOut, zeros);               // R0 0 R1 0 R2 0 R3 0 ...
                var bgr0 = Sse2.UnpackLow(bg.AsUInt16(), r0.AsUInt16()).AsByte(); // B0G0R00 B1G1R10 B2G2R20 B3G3R30

                // Shuffle для упаковки BGRA→BGR24: извлекаем 0,1,2, 4,5,6, 8,9,10, 12,13,14
                var bgr24Packed = Ssse3.Shuffle(bgr0, CmykSse41Vectors.Rgba32ToRgb24Shuffle);
                Unsafe.WriteUnaligned(dst, bgr24Packed.AsUInt64().GetElement(0));     // 8 байт
                Unsafe.WriteUnaligned(dst + 8, bgr24Packed.AsUInt32().GetElement(2)); // 4 байта

                src += 16;
                dst += 12;
                i += 4;
            }

            while (i < count)
            {
                var cmyk = new Cmyk(src[0], src[1], src[2], src[3]);
                var bgr = cmyk.ToBgr24();
                dst[0] = bgr.B; dst[1] = bgr.G; dst[2] = bgr.R;
                src += 4;
                dst += 3;
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Avx2(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykAvx2Vectors.OneF;
            var c255F = CmykAvx2Vectors.C255F;
            var inv255F = CmykAvx2Vectors.Inv255F;
            var epsilonF = CmykAvx2Vectors.EpsilonF;

            // 8 пикселей за итерацию (24 байт BGR → 32 байт CMYK)
            while (i + 8 <= count)
            {
                // Загружаем 24 байта BGR24 (читаем 32, используем 24)
                var bgr24 = Avx.LoadVector256(src);

                // Извлекаем R, G, B через gather-like операции (8 пикселей)
                // BGR24: B0 G0 R0 B1 G1 R1 B2 G2 R2 B3 G3 R3 B4 G4 R4 B5 G5 R5 B6 G6 R6 B7 G7 R7
                // Позиции R: 2, 5, 8, 11, 14, 17, 20, 23
                // Позиции G: 1, 4, 7, 10, 13, 16, 19, 22
                // Позиции B: 0, 3, 6, 9, 12, 15, 18, 21

                // SSE обработка по 4 пикселя (AVX2 VPSHUFB работает in-lane)
                var lo = bgr24.GetLower();  // пиксели 0-3 + часть 4
                var hi = Sse2.LoadVector128(src + 12);  // пиксели 4-7

                var shuffleR = CmykSse41Vectors.ShuffleBgr24R;
                var shuffleG = CmykSse41Vectors.ShuffleBgr24G;
                var shuffleB = CmykSse41Vectors.ShuffleBgr24B;

                var rLo = Ssse3.Shuffle(lo, shuffleR);
                var gLo = Ssse3.Shuffle(lo, shuffleG);
                var bLo = Ssse3.Shuffle(lo, shuffleB);
                var rHi = Ssse3.Shuffle(hi, shuffleR);
                var gHi = Ssse3.Shuffle(hi, shuffleG);
                var bHi = Ssse3.Shuffle(hi, shuffleB);

                // Конвертация в float (256-bit)
                var rLoI = Sse41.ConvertToVector128Int32(rLo);
                var gLoI = Sse41.ConvertToVector128Int32(gLo);
                var bLoI = Sse41.ConvertToVector128Int32(bLo);
                var rHiI = Sse41.ConvertToVector128Int32(rHi);
                var gHiI = Sse41.ConvertToVector128Int32(gHi);
                var bHiI = Sse41.ConvertToVector128Int32(bHi);

                var rI = Vector256.Create(rLoI, rHiI);
                var gI = Vector256.Create(gLoI, gHiI);
                var bI = Vector256.Create(bLoI, bHiI);

                var rF = Avx.Multiply(Avx.ConvertToVector256Single(rI), inv255F);
                var gF = Avx.Multiply(Avx.ConvertToVector256Single(gI), inv255F);
                var bF = Avx.Multiply(Avx.ConvertToVector256Single(bI), inv255F);

                var maxRgb = Avx.Max(Avx.Max(rF, gF), bF);
                var kF = Avx.Subtract(oneF, maxRgb);

                // invK = 1 / max (без Newton-Raphson: rcpps даёт ~12 бит точности, достаточно для 8-бит результата)
                var invK = Avx.Reciprocal(Avx.Max(maxRgb, epsilonF));

                var cF = Avx.Multiply(Avx.Subtract(maxRgb, rF), invK);
                var mF = Avx.Multiply(Avx.Subtract(maxRgb, gF), invK);
                var yF = Avx.Multiply(Avx.Subtract(maxRgb, bF), invK);

                cF = Avx.Multiply(cF, c255F);
                mF = Avx.Multiply(mF, c255F);
                yF = Avx.Multiply(yF, c255F);
                kF = Avx.Multiply(kF, c255F);

                var cI2 = Avx.ConvertToVector256Int32(cF);
                var mI2 = Avx.ConvertToVector256Int32(mF);
                var yI2 = Avx.ConvertToVector256Int32(yF);
                var kI2 = Avx.ConvertToVector256Int32(kF);

                // Упаковка и интерлив CMYK (8 пикселей = 32 байта)
                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var cLoB = Ssse3.Shuffle(cI2.GetLower().AsByte(), packMask);
                var mLoB = Ssse3.Shuffle(mI2.GetLower().AsByte(), packMask);
                var yLoB = Ssse3.Shuffle(yI2.GetLower().AsByte(), packMask);
                var kLoB = Ssse3.Shuffle(kI2.GetLower().AsByte(), packMask);
                var cHiB = Ssse3.Shuffle(cI2.GetUpper().AsByte(), packMask);
                var mHiB = Ssse3.Shuffle(mI2.GetUpper().AsByte(), packMask);
                var yHiB = Ssse3.Shuffle(yI2.GetUpper().AsByte(), packMask);
                var kHiB = Ssse3.Shuffle(kI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив первые 4 пикселя
                var cmLo = Sse2.UnpackLow(cLoB, mLoB);
                var ykLo = Sse2.UnpackLow(yLoB, kLoB);
                var cmykLo = Sse2.UnpackLow(cmLo.AsUInt16(), ykLo.AsUInt16()).AsByte();
                Sse2.Store(dst, cmykLo);

                // SIMD интерлив следующие 4 пикселя
                var cmHi = Sse2.UnpackLow(cHiB, mHiB);
                var ykHi = Sse2.UnpackLow(yHiB, kHiB);
                var cmykHi = Sse2.UnpackLow(cmHi.AsUInt16(), ykHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, cmykHi);

                src += 24;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback для остатка 4+ пикселей
            if (i + 4 <= count)
            {
                FromBgr24Sse41(source[i..], destination[i..]);
                return;
            }

            // Scalar для остатка
            while (i < count)
            {
                var bgr = new Bgr24(src[0], src[1], src[2]);
                var cmyk = FromBgr24(bgr);
                dst[0] = cmyk.C; dst[1] = cmyk.M; dst[2] = cmyk.Y; dst[3] = cmyk.K;
                src += 3;
                dst += 4;
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Avx2(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykAvx2Vectors.OneF;
            var c255F = CmykAvx2Vectors.C255F;
            var inv255F = CmykAvx2Vectors.Inv255F;

            // 8 пикселей за итерацию
            while (i + 8 <= count)
            {
                var cmykLo = Sse2.LoadVector128(src);
                var cmykHi = Sse2.LoadVector128(src + 16);

                var shuffleC = CmykSse41Vectors.ShuffleCmykC;
                var shuffleM = CmykSse41Vectors.ShuffleCmykM;
                var shuffleY = CmykSse41Vectors.ShuffleCmykY;
                var shuffleK = CmykSse41Vectors.ShuffleCmykK;

                var cLoB = Ssse3.Shuffle(cmykLo, shuffleC);
                var mLoB = Ssse3.Shuffle(cmykLo, shuffleM);
                var yLoB = Ssse3.Shuffle(cmykLo, shuffleY);
                var kLoB = Ssse3.Shuffle(cmykLo, shuffleK);
                var cHiB = Ssse3.Shuffle(cmykHi, shuffleC);
                var mHiB = Ssse3.Shuffle(cmykHi, shuffleM);
                var yHiB = Ssse3.Shuffle(cmykHi, shuffleY);
                var kHiB = Ssse3.Shuffle(cmykHi, shuffleK);

                var cI = Vector256.Create(Sse41.ConvertToVector128Int32(cLoB), Sse41.ConvertToVector128Int32(cHiB));
                var mI = Vector256.Create(Sse41.ConvertToVector128Int32(mLoB), Sse41.ConvertToVector128Int32(mHiB));
                var yI = Vector256.Create(Sse41.ConvertToVector128Int32(yLoB), Sse41.ConvertToVector128Int32(yHiB));
                var kI = Vector256.Create(Sse41.ConvertToVector128Int32(kLoB), Sse41.ConvertToVector128Int32(kHiB));

                var cF = Avx.Multiply(Avx.ConvertToVector256Single(cI), inv255F);
                var mF = Avx.Multiply(Avx.ConvertToVector256Single(mI), inv255F);
                var yF = Avx.Multiply(Avx.ConvertToVector256Single(yI), inv255F);
                var kF = Avx.Multiply(Avx.ConvertToVector256Single(kI), inv255F);

                var oneMinusK = Avx.Subtract(oneF, kF);
                var rF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, cF), oneMinusK), c255F);
                var gF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, mF), oneMinusK), c255F);
                var bF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, yF), oneMinusK), c255F);

                var rI2 = Avx.ConvertToVector256Int32(rF);
                var gI2 = Avx.ConvertToVector256Int32(gF);
                var bI2 = Avx.ConvertToVector256Int32(bF);

                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var rLoOut = Ssse3.Shuffle(rI2.GetLower().AsByte(), packMask);
                var gLoOut = Ssse3.Shuffle(gI2.GetLower().AsByte(), packMask);
                var bLoOut = Ssse3.Shuffle(bI2.GetLower().AsByte(), packMask);
                var rHiOut = Ssse3.Shuffle(rI2.GetUpper().AsByte(), packMask);
                var gHiOut = Ssse3.Shuffle(gI2.GetUpper().AsByte(), packMask);
                var bHiOut = Ssse3.Shuffle(bI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив BGR24 для первых 4 пикселей
                // Нужно: B0G0R0 B1G1R1 B2G2R2 B3G3R3 (12 байт)
                var zeros = Vector128<byte>.Zero;
                var bgLo = Sse2.UnpackLow(bLoOut, gLoOut);           // B0G0 B1G1 B2G2 B3G3 ...
                var r0Lo = Sse2.UnpackLow(rLoOut, zeros);             // R0 0 R1 0 R2 0 R3 0 ...
                var bgrLo = Sse2.UnpackLow(bgLo.AsUInt16(), r0Lo.AsUInt16()).AsByte(); // B0G0R00 B1G1R10 ...

                // Shuffle для упаковки BGR24: извлекаем байты 0,1,2, 4,5,6, 8,9,10, 12,13,14
                var bgr24Shuffle = CmykSse41Vectors.Rgba32ToRgb24Shuffle;
                var bgrLoPackedPre = Ssse3.Shuffle(bgrLo, bgr24Shuffle);
                Unsafe.WriteUnaligned(dst, bgrLoPackedPre.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 8, bgrLoPackedPre.AsUInt32().GetElement(2));

                // SIMD интерлив BGR24 для следующих 4 пикселей
                var bgHi = Sse2.UnpackLow(bHiOut, gHiOut);
                var r0Hi = Sse2.UnpackLow(rHiOut, zeros);
                var bgrHi = Sse2.UnpackLow(bgHi.AsUInt16(), r0Hi.AsUInt16()).AsByte();
                var bgrHiPackedPre = Ssse3.Shuffle(bgrHi, bgr24Shuffle);
                Unsafe.WriteUnaligned(dst + 12, bgrHiPackedPre.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 20, bgrHiPackedPre.AsUInt32().GetElement(2));

                src += 32;
                dst += 24;
                i += 8;
            }

            // SSE41 fallback для остатка
            if (i + 4 <= count)
            {
                ToBgr24Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                var cmyk = new Cmyk(src[0], src[1], src[2], src[3]);
                var bgr = cmyk.ToBgr24();
                dst[0] = bgr.B; dst[1] = bgr.G; dst[2] = bgr.R;
                src += 4;
                dst += 3;
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators (Bgr24)

    /// <summary>Явная конвертация Bgr24 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явная конвертация Cmyk → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Cmyk cmyk) => cmyk.ToBgr24();

    #endregion
}
