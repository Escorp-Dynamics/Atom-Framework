#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, MA0051, MA0084, S1117, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Bgra32.
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants (Bgra32)

    /// <summary>Реализованные ускорители для Cmyk ↔ Bgra32.</summary>
    private const HardwareAcceleration Bgra32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в Cmyk (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromBgra32(Bgra32 bgra) => FromRgb24(new Rgb24(bgra.R, bgra.G, bgra.B));

    /// <summary>Конвертирует Cmyk в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32()
    {
        var rgb = ToRgb24();
        return new Bgra32(rgb.B, rgb.G, rgb.R, 255);
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ Bgra32)

    /// <summary>Пакетная конвертация Bgra32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination) =>
        FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → Cmyk с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromBgra32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination) =>
        ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Bgra32 с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ToBgra32(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
            {
                ToBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToBgra32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations (Bgra32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgra32Core(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromBgra32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromBgra32Sse41(source, destination);
                break;
            default:
                FromBgra32Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToBgra32Core(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToBgra32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToBgra32Sse41(source, destination);
                break;
            default:
                ToBgra32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing (Bgra32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgra32Parallel(Bgra32* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgra32Core(new ReadOnlySpan<Bgra32>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgra32Parallel(Cmyk* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgra32Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Bgra32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementations (Bgra32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgra32Scalar(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgra32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgra32Scalar(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToBgra32();
        }
    }

    #endregion

    #region SSE41 Implementation (Bgra32)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Sse41(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination)
    {
        fixed (Bgra32* srcPtr = source)
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

            // Маски для BGRA32 (B=0, G=1, R=2, A=3)
            var shuffleR = CmykSse41Vectors.ShuffleBgra32R;
            var shuffleG = CmykSse41Vectors.ShuffleBgra32G;
            var shuffleB = CmykSse41Vectors.ShuffleBgra32B;

            // 4 пикселя за итерацию (16 байт BGRA → 16 байт CMYK)
            while (i + 4 <= count)
            {
                var bgra16 = Sse2.LoadVector128(src);

                var rBytes = Ssse3.Shuffle(bgra16, shuffleR);
                var gBytes = Ssse3.Shuffle(bgra16, shuffleG);
                var bBytes = Ssse3.Shuffle(bgra16, shuffleB);

                var rF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(rBytes)), inv255F);
                var gF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(gBytes)), inv255F);
                var bF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(bBytes)), inv255F);

                var maxRgb = Sse.Max(Sse.Max(rF, gF), bF);
                var kF = Sse.Subtract(oneF, maxRgb);

                // invK = 1 / max (без Newton-Raphson: rcpps даёт ~12 бит точности, достаточно для 8-бит результата)
                var invK = Sse.Reciprocal(Sse.Max(maxRgb, epsilonF));

                var cF = Sse.Multiply(Sse.Subtract(maxRgb, rF), invK);
                var mF = Sse.Multiply(Sse.Subtract(maxRgb, gF), invK);
                var yF = Sse.Multiply(Sse.Subtract(maxRgb, bF), invK);

                cF = Sse.Multiply(cF, c255F);
                mF = Sse.Multiply(mF, c255F);
                yF = Sse.Multiply(yF, c255F);
                kF = Sse.Multiply(kF, c255F);

                var cBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(cF).AsByte(), packMask);
                var mBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(mF).AsByte(), packMask);
                var yBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(yF).AsByte(), packMask);
                var kBytesOut = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(kF).AsByte(), packMask);

                // SIMD интерлив CMYK: [C0 C1 ...] + [M0 M1 ...] → [C0 M0 C1 M1 ...]
                var cm = Sse2.UnpackLow(cBytesOut, mBytesOut);                      // [C0 M0 C1 M1 C2 M2 C3 M3 ...]
                var yk = Sse2.UnpackLow(yBytesOut, kBytesOut);                      // [Y0 K0 Y1 K1 Y2 K2 Y3 K3 ...]
                var cmyk = Sse2.UnpackLow(cm.AsUInt16(), yk.AsUInt16()).AsByte();   // [C0 M0 Y0 K0 ...]
                Sse2.Store(dst, cmyk);

                src += 16;
                dst += 16;
                i += 4;
            }

            while (i < count)
            {
                var bgra = new Bgra32(src[0], src[1], src[2], src[3]);
                var cmyk = FromBgra32(bgra);
                dst[0] = cmyk.C; dst[1] = cmyk.M; dst[2] = cmyk.Y; dst[3] = cmyk.K;
                src += 4;
                dst += 4;
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Sse41(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
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
            var allFF = CmykSse41Vectors.AllFF;

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
                var aBytesOut = allFF;

                // SIMD интерлив BGRA32: [B0 B1 ...] + [G0 G1 ...] → [B0 G0 B1 G1 ...]
                var bg = Sse2.UnpackLow(bBytesOut, gBytesOut);                      // [B0 G0 B1 G1 B2 G2 B3 G3 ...]
                var ra = Sse2.UnpackLow(rBytesOut, aBytesOut);                      // [R0 A0 R1 A1 R2 A2 R3 A3 ...]
                var bgra = Sse2.UnpackLow(bg.AsUInt16(), ra.AsUInt16()).AsByte();   // [B0 G0 R0 A0 ...]
                Sse2.Store(dst, bgra);

                src += 16;
                dst += 16;
                i += 4;
            }

            while (i < count)
            {
                var cmyk = new Cmyk(src[0], src[1], src[2], src[3]);
                var bgra = cmyk.ToBgra32();
                dst[0] = bgra.B; dst[1] = bgra.G; dst[2] = bgra.R; dst[3] = bgra.A;
                src += 4;
                dst += 4;
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Bgra32)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Avx2(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination)
    {
        fixed (Bgra32* srcPtr = source)
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
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            var shuffleR = CmykSse41Vectors.ShuffleBgra32R;
            var shuffleG = CmykSse41Vectors.ShuffleBgra32G;
            var shuffleB = CmykSse41Vectors.ShuffleBgra32B;

            // 8 пикселей за итерацию (32 байт BGRA → 32 байт CMYK)
            while (i + 8 <= count)
            {
                var bgraLo = Sse2.LoadVector128(src);
                var bgraHi = Sse2.LoadVector128(src + 16);

                var rLoB = Ssse3.Shuffle(bgraLo, shuffleR);
                var gLoB = Ssse3.Shuffle(bgraLo, shuffleG);
                var bLoB = Ssse3.Shuffle(bgraLo, shuffleB);
                var rHiB = Ssse3.Shuffle(bgraHi, shuffleR);
                var gHiB = Ssse3.Shuffle(bgraHi, shuffleG);
                var bHiB = Ssse3.Shuffle(bgraHi, shuffleB);

                var rI = Vector256.Create(Sse41.ConvertToVector128Int32(rLoB), Sse41.ConvertToVector128Int32(rHiB));
                var gI = Vector256.Create(Sse41.ConvertToVector128Int32(gLoB), Sse41.ConvertToVector128Int32(gHiB));
                var bI = Vector256.Create(Sse41.ConvertToVector128Int32(bLoB), Sse41.ConvertToVector128Int32(bHiB));

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

                var cLoOut = Ssse3.Shuffle(cI2.GetLower().AsByte(), packMask);
                var mLoOut = Ssse3.Shuffle(mI2.GetLower().AsByte(), packMask);
                var yLoOut = Ssse3.Shuffle(yI2.GetLower().AsByte(), packMask);
                var kLoOut = Ssse3.Shuffle(kI2.GetLower().AsByte(), packMask);
                var cHiOut = Ssse3.Shuffle(cI2.GetUpper().AsByte(), packMask);
                var mHiOut = Ssse3.Shuffle(mI2.GetUpper().AsByte(), packMask);
                var yHiOut = Ssse3.Shuffle(yI2.GetUpper().AsByte(), packMask);
                var kHiOut = Ssse3.Shuffle(kI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив CMYK (первые 4 пикселя)
                var cmLo = Sse2.UnpackLow(cLoOut, mLoOut);
                var ykLo = Sse2.UnpackLow(yLoOut, kLoOut);
                var cmykLo = Sse2.UnpackLow(cmLo.AsUInt16(), ykLo.AsUInt16()).AsByte();
                Sse2.Store(dst, cmykLo);

                // SIMD интерлив CMYK (следующие 4 пикселя)
                var cmHi = Sse2.UnpackLow(cHiOut, mHiOut);
                var ykHi = Sse2.UnpackLow(yHiOut, kHiOut);
                var cmykHi = Sse2.UnpackLow(cmHi.AsUInt16(), ykHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, cmykHi);

                src += 32;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback
            if (i + 4 <= count)
            {
                FromBgra32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                var bgra = new Bgra32(src[0], src[1], src[2], src[3]);
                var cmyk = FromBgra32(bgra);
                dst[0] = cmyk.C; dst[1] = cmyk.M; dst[2] = cmyk.Y; dst[3] = cmyk.K;
                src += 4;
                dst += 4;
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Avx2(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykAvx2Vectors.OneF;
            var c255F = CmykAvx2Vectors.C255F;
            var inv255F = CmykAvx2Vectors.Inv255F;
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;
            var allFF = CmykSse41Vectors.AllFF;

            while (i + 8 <= count)
            {
                var cmykLo = Sse2.LoadVector128(src);
                var cmykHi = Sse2.LoadVector128(src + 16);

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

                var rLoOut = Ssse3.Shuffle(rI2.GetLower().AsByte(), packMask);
                var gLoOut = Ssse3.Shuffle(gI2.GetLower().AsByte(), packMask);
                var bLoOut = Ssse3.Shuffle(bI2.GetLower().AsByte(), packMask);
                var rHiOut = Ssse3.Shuffle(rI2.GetUpper().AsByte(), packMask);
                var gHiOut = Ssse3.Shuffle(gI2.GetUpper().AsByte(), packMask);
                var bHiOut = Ssse3.Shuffle(bI2.GetUpper().AsByte(), packMask);
                var aBytesOut = allFF;

                // SIMD интерлив BGRA (первые 4 пикселя)
                var bgLo = Sse2.UnpackLow(bLoOut, gLoOut);
                var raLo = Sse2.UnpackLow(rLoOut, aBytesOut);
                var bgraLo = Sse2.UnpackLow(bgLo.AsUInt16(), raLo.AsUInt16()).AsByte();
                Sse2.Store(dst, bgraLo);

                // SIMD интерлив BGRA (следующие 4 пикселя)
                var bgHi = Sse2.UnpackLow(bHiOut, gHiOut);
                var raHi = Sse2.UnpackLow(rHiOut, aBytesOut);
                var bgraHi = Sse2.UnpackLow(bgHi.AsUInt16(), raHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, bgraHi);

                src += 32;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback
            if (i + 4 <= count)
            {
                ToBgra32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                var cmyk = new Cmyk(src[0], src[1], src[2], src[3]);
                var bgra = cmyk.ToBgra32();
                dst[0] = bgra.B; dst[1] = bgra.G; dst[2] = bgra.R; dst[3] = bgra.A;
                src += 4;
                dst += 4;
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators (Bgra32)

    /// <summary>Явная конвертация Bgra32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явная конвертация Cmyk → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(Cmyk cmyk) => cmyk.ToBgra32();

    #endregion
}
