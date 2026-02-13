#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Bgra32.
/// YCoCg-R — lossless целочисленное преобразование.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Bgra32.</summary>
    private const HardwareAcceleration Bgra32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Bgra32 → YCoCgR32 (lossless, альфа игнорируется).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromBgra32(Bgra32 bgra)
    {
        int r = bgra.R, g = bgra.G, b = bgra.B;

        var co = r - b;
        var t = b + (co >> 1);
        var cg = g - t;
        var y = t + (cg >> 1);

        return new YCoCgR32(y, co, cg);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Bgra32 (lossless, A=255).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32()
    {
        var co = Co;
        var cg = Cg;

        var t = Y - (cg >> 1);
        var g = cg + t;
        var b = t - (co >> 1);
        var r = b + co;

        return new Bgra32((byte)b, (byte)g, (byte)r, 255);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgra32 → YCoCgR32.</summary>
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination) =>
        FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromBgra32(
        ReadOnlySpan<Bgra32> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromBgra32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Bgra32.</summary>
    public static void ToBgra32(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination) =>
        ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Bgra32 с явным ускорителем.</summary>
    public static unsafe void ToBgra32(
        ReadOnlySpan<YCoCgR32> source,
        Span<Bgra32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
            {
                ToBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToBgra32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgra32Core(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
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
    private static void ToBgra32Core(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination, HardwareAcceleration selected)
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

    #region Parallel Processing

    private static unsafe void FromBgra32Parallel(Bgra32* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromBgra32Core(new ReadOnlySpan<Bgra32>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToBgra32Parallel(YCoCgR32* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToBgra32Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Bgra32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgra32Scalar(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgra32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgra32Scalar(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToBgra32();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Bgra32 → YCoCgR32.
    /// 4 пикселя за итерацию (16 байт → 16 байт).
    /// BGRA layout: B0 G0 R0 A0 | B1 G1 R1 A1 | ...
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Sse41(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // Shuffle для BGRA: B=0, G=1, R=2, A=3
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleBgraToB;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleBgraToG;
            var shuffleR = YCoCgR32Sse41Vectors.ShuffleBgraToR;

            while (i + 4 <= count)
            {
                var bgra = Sse2.LoadVector128(src + (i * 4));

                var r16 = Ssse3.Shuffle(bgra, shuffleR).AsInt16();
                var g16 = Ssse3.Shuffle(bgra, shuffleG).AsInt16();
                var b16 = Ssse3.Shuffle(bgra, shuffleB).AsInt16();

                var co = Sse2.Subtract(r16, b16);
                var coSra = Sse2.ShiftRightArithmetic(co, 1);
                var t = Sse2.Add(b16, coSra);
                var cg = Sse2.Subtract(g16, t);
                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);
                var y16 = Sse2.Add(t, cgSra);

                var coShifted = Sse2.Add(co, offset255);
                var cgShifted = Sse2.Add(cg, offset255);
                var coHigh = Sse2.ShiftRightLogical(coShifted, 1);
                var cgHigh = Sse2.ShiftRightLogical(cgShifted, 1);

                var coLsb = Sse2.And(coShifted, one16);
                var cgLsb = Sse2.And(cgShifted, one16);
                var frac16 = Sse2.Or(coLsb, Sse2.ShiftLeftLogical(cgLsb, 1));

                var y8 = Sse2.PackUnsignedSaturate(y16, y16);
                var coH8 = Sse2.PackUnsignedSaturate(coHigh, coHigh);
                var cgH8 = Sse2.PackUnsignedSaturate(cgHigh, cgHigh);
                var frac8 = Sse2.PackUnsignedSaturate(frac16, frac16);

                var yCo = Sse2.UnpackLow(y8, coH8);
                var cgF = Sse2.UnpackLow(cgH8, frac8);
                var result = Sse2.UnpackLow(yCo.AsInt16(), cgF.AsInt16());

                Sse2.Store(dst + (i * 4), result.AsByte());

                i += 4;
            }

            while (i < count)
            {
                destination[i] = FromBgra32(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Bgra32.
    /// 4 пикселя за итерацию (16 байт → 16 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Sse41(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;
            var alpha255 = YCoCgR32Sse41Vectors.Alpha255;

            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Sse41Vectors.ShuffleYCoCgToFrac;

            while (i + 4 <= count)
            {
                var ycocg = Sse2.LoadVector128(src + (i * 4));

                var y16 = Ssse3.Shuffle(ycocg, shuffleY).AsInt16();
                var coH16 = Ssse3.Shuffle(ycocg, shuffleCoH).AsInt16();
                var cgH16 = Ssse3.Shuffle(ycocg, shuffleCgH).AsInt16();
                var frac16 = Ssse3.Shuffle(ycocg, shuffleFrac).AsInt16();

                var coLsb = Sse2.And(frac16, one16);
                var cgLsb = Sse2.And(Sse2.ShiftRightLogical(frac16, 1), one16);

                var coFull = Sse2.Or(Sse2.ShiftLeftLogical(coH16, 1), coLsb);
                var cgFull = Sse2.Or(Sse2.ShiftLeftLogical(cgH16, 1), cgLsb);

                var co = Sse2.Subtract(coFull, offset255);
                var cg = Sse2.Subtract(cgFull, offset255);

                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);
                var t = Sse2.Subtract(y16, cgSra);
                var g16 = Sse2.Add(cg, t);
                var coSra = Sse2.ShiftRightArithmetic(co, 1);
                var b16 = Sse2.Subtract(t, coSra);
                var r16 = Sse2.Add(b16, co);

                var r8 = Sse2.PackUnsignedSaturate(r16, r16);
                var g8 = Sse2.PackUnsignedSaturate(g16, g16);
                var b8 = Sse2.PackUnsignedSaturate(b16, b16);

                // BGRA: B G R A
                var bg = Sse2.UnpackLow(b8, g8);
                var ra = Sse2.UnpackLow(r8, Vector128<byte>.Zero);
                var bgra = Sse2.UnpackLow(bg.AsInt16(), ra.AsInt16()).AsByte();
                bgra = Sse2.Or(bgra, alpha255);

                Sse2.Store(dst + (i * 4), bgra);

                i += 4;
            }

            while (i < count)
            {
                destination[i] = source[i].ToBgra32();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Bgra32 → YCoCgR32.
    /// 8 пикселей за итерацию (32 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Avx2(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Avx2Vectors.Offset255;
            var one16 = YCoCgR32Avx2Vectors.One;

            // BGRA layout: B=pos0, G=pos1, R=pos2 (swap B↔R vs RGBA)
            var shuffleB = YCoCgR32Avx2Vectors.ShuffleRgbaToR; // B at pos 0
            var shuffleG = YCoCgR32Avx2Vectors.ShuffleRgbaToG; // G at pos 1
            var shuffleR = YCoCgR32Avx2Vectors.ShuffleRgbaToB; // R at pos 2

            while (i + 8 <= count)
            {
                var bgra = Avx.LoadVector256(src + (i * 4));

                var r16 = Avx2.Shuffle(bgra, shuffleR).AsInt16();
                var g16 = Avx2.Shuffle(bgra, shuffleG).AsInt16();
                var b16 = Avx2.Shuffle(bgra, shuffleB).AsInt16();

                var co = Avx2.Subtract(r16, b16);
                var coSra = Avx2.ShiftRightArithmetic(co, 1);
                var t = Avx2.Add(b16, coSra);
                var cg = Avx2.Subtract(g16, t);
                var cgSra = Avx2.ShiftRightArithmetic(cg, 1);
                var y16 = Avx2.Add(t, cgSra);

                var coShifted = Avx2.Add(co, offset255);
                var cgShifted = Avx2.Add(cg, offset255);
                var coHigh = Avx2.ShiftRightLogical(coShifted, 1);
                var cgHigh = Avx2.ShiftRightLogical(cgShifted, 1);

                var coLsb = Avx2.And(coShifted, one16);
                var cgLsb = Avx2.And(cgShifted, one16);
                var frac16 = Avx2.Or(coLsb, Avx2.ShiftLeftLogical(cgLsb, 1));

                // Lower lane
                var y8Lo = Sse2.PackUnsignedSaturate(y16.GetLower(), y16.GetLower());
                var coH8Lo = Sse2.PackUnsignedSaturate(coHigh.GetLower(), coHigh.GetLower());
                var cgH8Lo = Sse2.PackUnsignedSaturate(cgHigh.GetLower(), cgHigh.GetLower());
                var frac8Lo = Sse2.PackUnsignedSaturate(frac16.GetLower(), frac16.GetLower());
                var yCoLo = Sse2.UnpackLow(y8Lo, coH8Lo);
                var cgFLo = Sse2.UnpackLow(cgH8Lo, frac8Lo);
                var resultLo = Sse2.UnpackLow(yCoLo.AsInt16(), cgFLo.AsInt16());

                // Upper lane
                var y8Hi = Sse2.PackUnsignedSaturate(y16.GetUpper(), y16.GetUpper());
                var coH8Hi = Sse2.PackUnsignedSaturate(coHigh.GetUpper(), coHigh.GetUpper());
                var cgH8Hi = Sse2.PackUnsignedSaturate(cgHigh.GetUpper(), cgHigh.GetUpper());
                var frac8Hi = Sse2.PackUnsignedSaturate(frac16.GetUpper(), frac16.GetUpper());
                var yCoHi = Sse2.UnpackLow(y8Hi, coH8Hi);
                var cgFHi = Sse2.UnpackLow(cgH8Hi, frac8Hi);
                var resultHi = Sse2.UnpackLow(yCoHi.AsInt16(), cgFHi.AsInt16());

                Sse2.Store(dst + (i * 4), resultLo.AsByte());
                Sse2.Store(dst + (i * 4) + 16, resultHi.AsByte());

                i += 8;
            }

            if (i + 4 <= count)
            {
                FromBgra32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromBgra32(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Bgra32.
    /// 8 пикселей за итерацию (32 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Avx2(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Avx2Vectors.Offset255;
            var one16 = YCoCgR32Avx2Vectors.One;
            var alpha255 = YCoCgR32Sse41Vectors.Alpha255;

            var shuffleY = YCoCgR32Avx2Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Avx2Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Avx2Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Avx2Vectors.ShuffleYCoCgToFrac;

            while (i + 8 <= count)
            {
                var ycocg = Avx.LoadVector256(src + (i * 4));

                var y16 = Avx2.Shuffle(ycocg, shuffleY).AsInt16();
                var coH16 = Avx2.Shuffle(ycocg, shuffleCoH).AsInt16();
                var cgH16 = Avx2.Shuffle(ycocg, shuffleCgH).AsInt16();
                var frac16 = Avx2.Shuffle(ycocg, shuffleFrac).AsInt16();

                var coLsb = Avx2.And(frac16, one16);
                var cgLsb = Avx2.And(Avx2.ShiftRightLogical(frac16, 1), one16);

                var coFull = Avx2.Or(Avx2.ShiftLeftLogical(coH16, 1), coLsb);
                var cgFull = Avx2.Or(Avx2.ShiftLeftLogical(cgH16, 1), cgLsb);

                var co = Avx2.Subtract(coFull, offset255);
                var cg = Avx2.Subtract(cgFull, offset255);

                var cgSra = Avx2.ShiftRightArithmetic(cg, 1);
                var t = Avx2.Subtract(y16, cgSra);
                var g16 = Avx2.Add(cg, t);
                var coSra = Avx2.ShiftRightArithmetic(co, 1);
                var b16 = Avx2.Subtract(t, coSra);
                var r16 = Avx2.Add(b16, co);

                // Lower lane - BGRA
                var r8Lo = Sse2.PackUnsignedSaturate(r16.GetLower(), r16.GetLower());
                var g8Lo = Sse2.PackUnsignedSaturate(g16.GetLower(), g16.GetLower());
                var b8Lo = Sse2.PackUnsignedSaturate(b16.GetLower(), b16.GetLower());
                var bgLo = Sse2.UnpackLow(b8Lo, g8Lo);
                var raLo = Sse2.UnpackLow(r8Lo, Vector128<byte>.Zero);
                var bgraLo = Sse2.UnpackLow(bgLo.AsInt16(), raLo.AsInt16()).AsByte();
                bgraLo = Sse2.Or(bgraLo, alpha255);

                // Upper lane - BGRA
                var r8Hi = Sse2.PackUnsignedSaturate(r16.GetUpper(), r16.GetUpper());
                var g8Hi = Sse2.PackUnsignedSaturate(g16.GetUpper(), g16.GetUpper());
                var b8Hi = Sse2.PackUnsignedSaturate(b16.GetUpper(), b16.GetUpper());
                var bgHi = Sse2.UnpackLow(b8Hi, g8Hi);
                var raHi = Sse2.UnpackLow(r8Hi, Vector128<byte>.Zero);
                var bgraHi = Sse2.UnpackLow(bgHi.AsInt16(), raHi.AsInt16()).AsByte();
                bgraHi = Sse2.Or(bgraHi, alpha255);

                Sse2.Store(dst + (i * 4), bgraLo);
                Sse2.Store(dst + (i * 4) + 16, bgraHi);

                i += 8;
            }

            if (i + 4 <= count)
            {
                ToBgra32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = source[i].ToBgra32();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Bgra32 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явная конвертация YCoCgR32 → Bgra32.</summary>
    public static explicit operator Bgra32(YCoCgR32 ycocg) => ycocg.ToBgra32();

    #endregion
}
