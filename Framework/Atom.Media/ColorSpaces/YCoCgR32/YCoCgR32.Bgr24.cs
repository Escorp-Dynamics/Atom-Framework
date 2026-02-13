#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Bgr24.
/// YCoCg-R — lossless целочисленное преобразование.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Bgr24.</summary>
    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Bgr24 → YCoCgR32 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromBgr24(Bgr24 bgr)
    {
        int r = bgr.R, g = bgr.G, b = bgr.B;

        var co = r - b;
        var t = b + (co >> 1);
        var cg = g - t;
        var y = t + (cg >> 1);

        return new YCoCgR32(y, co, cg);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Bgr24 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24()
    {
        var co = Co;
        var cg = Cg;

        var t = Y - (cg >> 1);
        var g = cg + t;
        var b = t - (co >> 1);
        var r = b + co;

        return new Bgr24((byte)b, (byte)g, (byte)r);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgr24 → YCoCgR32.</summary>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromBgr24(
        ReadOnlySpan<Bgr24> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Bgr24.</summary>
    public static void ToBgr24(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Bgr24 с явным ускорителем.</summary>
    public static unsafe void ToBgr24(
        ReadOnlySpan<YCoCgR32> source,
        Span<Bgr24> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
            {
                ToBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
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
    private static void ToBgr24Core(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination, HardwareAcceleration selected)
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

    #region Parallel Processing

    private static unsafe void FromBgr24Parallel(Bgr24* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromBgr24Core(new ReadOnlySpan<Bgr24>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToBgr24Parallel(YCoCgR32* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToBgr24Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Bgr24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgr24Scalar(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgr24(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgr24Scalar(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToBgr24();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Bgr24 → YCoCgR32.
    /// 4 пикселя за итерацию (12 байт → 16 байт).
    /// BGR24 layout: B0 G0 R0 | B1 G1 R1 | B2 G2 R2 | B3 G3 R3
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Sse41(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // BGR24: B at 0,3,6,9; G at 1,4,7,10; R at 2,5,8,11
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleBgr24ToB;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleBgr24ToG;
            var shuffleR = YCoCgR32Sse41Vectors.ShuffleBgr24ToR;

            while (i + 4 <= count)
            {
                // Load 12 bytes (safe with unaligned load up to 16 bytes)
                var bgr = Sse2.LoadVector128(src + (i * 3));

                var r16 = Ssse3.Shuffle(bgr, shuffleR).AsInt16();
                var g16 = Ssse3.Shuffle(bgr, shuffleG).AsInt16();
                var b16 = Ssse3.Shuffle(bgr, shuffleB).AsInt16();

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
                destination[i] = FromBgr24(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Bgr24.
    /// 4 пикселя за итерацию (16 байт → 12 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Sse41(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Sse41Vectors.ShuffleYCoCgToFrac;

            // BGR24 output shuffle: B0 G0 R0 B1 G1 R1 B2 G2 R2 B3 G3 R3
            var shuffleBgr24Out = YCoCgR32Sse41Vectors.ShuffleBgr24Out;

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

                var b8 = Sse2.PackUnsignedSaturate(b16, b16);
                var g8 = Sse2.PackUnsignedSaturate(g16, g16);
                var r8 = Sse2.PackUnsignedSaturate(r16, r16);

                // Interleave B, G, R для BGR24 output
                // b8: B0 B1 B2 B3 ...
                // g8: G0 G1 G2 G3 ...
                // r8: R0 R1 R2 R3 ...
                var bg = Sse2.UnpackLow(b8, g8);  // B0 G0 B1 G1 B2 G2 B3 G3
                var rx = Sse2.UnpackLow(r8, Vector128<byte>.Zero);  // R0 0 R1 0 R2 0 R3 0

                // Combine: B0 G0 R0 _ B1 G1 R1 _ B2 G2 R2 _ B3 G3 R3 _
                var bgr32 = Sse2.UnpackLow(bg.AsInt16(), rx.AsInt16()).AsByte();

                // Shuffle to BGR24: B0 G0 R0 B1 G1 R1 B2 G2 R2 B3 G3 R3
                var bgr24 = Ssse3.Shuffle(bgr32, shuffleBgr24Out);

                // Store 12 bytes
                Unsafe.WriteUnaligned(dst + (i * 3), bgr24.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + (i * 3) + 8, bgr24.AsUInt32().GetElement(2));

                i += 4;
            }

            while (i < count)
            {
                destination[i] = source[i].ToBgr24();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Bgr24 → YCoCgR32.
    /// 8 пикселей за итерацию (24 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Avx2(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // BGR24 shuffle masks for SSE
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleBgr24ToB;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleBgr24ToG;
            var shuffleR = YCoCgR32Sse41Vectors.ShuffleBgr24ToR;

            // AVX2: 8 пикселей = 24 байта, но BGR24 имеет 3-byte stride,
            // поэтому обрабатываем как 2x SSE по 4 пикселя
            while (i + 8 <= count)
            {
                // Load pixels 0-3 (12 bytes)
                var bgr0 = Sse2.LoadVector128(src + (i * 3));
                // Load pixels 4-7 (12 bytes)
                var bgr1 = Sse2.LoadVector128(src + (i * 3) + 12);

                // Process first 4 pixels
                var r16Lo = Ssse3.Shuffle(bgr0, shuffleR).AsInt16();
                var g16Lo = Ssse3.Shuffle(bgr0, shuffleG).AsInt16();
                var b16Lo = Ssse3.Shuffle(bgr0, shuffleB).AsInt16();

                var coLo = Sse2.Subtract(r16Lo, b16Lo);
                var coSraLo = Sse2.ShiftRightArithmetic(coLo, 1);
                var tLo = Sse2.Add(b16Lo, coSraLo);
                var cgLo = Sse2.Subtract(g16Lo, tLo);
                var cgSraLo = Sse2.ShiftRightArithmetic(cgLo, 1);
                var y16Lo = Sse2.Add(tLo, cgSraLo);

                var coShiftedLo = Sse2.Add(coLo, offset255);
                var cgShiftedLo = Sse2.Add(cgLo, offset255);
                var coHighLo = Sse2.ShiftRightLogical(coShiftedLo, 1);
                var cgHighLo = Sse2.ShiftRightLogical(cgShiftedLo, 1);
                var coLsbLo = Sse2.And(coShiftedLo, one16);
                var cgLsbLo = Sse2.And(cgShiftedLo, one16);
                var frac16Lo = Sse2.Or(coLsbLo, Sse2.ShiftLeftLogical(cgLsbLo, 1));

                var y8Lo = Sse2.PackUnsignedSaturate(y16Lo, y16Lo);
                var coH8Lo = Sse2.PackUnsignedSaturate(coHighLo, coHighLo);
                var cgH8Lo = Sse2.PackUnsignedSaturate(cgHighLo, cgHighLo);
                var frac8Lo = Sse2.PackUnsignedSaturate(frac16Lo, frac16Lo);
                var yCoLo = Sse2.UnpackLow(y8Lo, coH8Lo);
                var cgFLo = Sse2.UnpackLow(cgH8Lo, frac8Lo);
                var resultLo = Sse2.UnpackLow(yCoLo.AsInt16(), cgFLo.AsInt16());

                // Process second 4 pixels
                var r16Hi = Ssse3.Shuffle(bgr1, shuffleR).AsInt16();
                var g16Hi = Ssse3.Shuffle(bgr1, shuffleG).AsInt16();
                var b16Hi = Ssse3.Shuffle(bgr1, shuffleB).AsInt16();

                var coHi = Sse2.Subtract(r16Hi, b16Hi);
                var coSraHi = Sse2.ShiftRightArithmetic(coHi, 1);
                var tHi = Sse2.Add(b16Hi, coSraHi);
                var cgHi = Sse2.Subtract(g16Hi, tHi);
                var cgSraHi = Sse2.ShiftRightArithmetic(cgHi, 1);
                var y16Hi = Sse2.Add(tHi, cgSraHi);

                var coShiftedHi = Sse2.Add(coHi, offset255);
                var cgShiftedHi = Sse2.Add(cgHi, offset255);
                var coHighHi = Sse2.ShiftRightLogical(coShiftedHi, 1);
                var cgHighHi = Sse2.ShiftRightLogical(cgShiftedHi, 1);
                var coLsbHi = Sse2.And(coShiftedHi, one16);
                var cgLsbHi = Sse2.And(cgShiftedHi, one16);
                var frac16Hi = Sse2.Or(coLsbHi, Sse2.ShiftLeftLogical(cgLsbHi, 1));

                var y8Hi = Sse2.PackUnsignedSaturate(y16Hi, y16Hi);
                var coH8Hi = Sse2.PackUnsignedSaturate(coHighHi, coHighHi);
                var cgH8Hi = Sse2.PackUnsignedSaturate(cgHighHi, cgHighHi);
                var frac8Hi = Sse2.PackUnsignedSaturate(frac16Hi, frac16Hi);
                var yCoHi = Sse2.UnpackLow(y8Hi, coH8Hi);
                var cgFHi = Sse2.UnpackLow(cgH8Hi, frac8Hi);
                var resultHi = Sse2.UnpackLow(yCoHi.AsInt16(), cgFHi.AsInt16());

                Sse2.Store(dst + (i * 4), resultLo.AsByte());
                Sse2.Store(dst + (i * 4) + 16, resultHi.AsByte());

                i += 8;
            }

            if (i + 4 <= count)
            {
                FromBgr24Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromBgr24(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Bgr24.
    /// 8 пикселей за итерацию (32 байт → 24 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Avx2(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Sse41Vectors.ShuffleYCoCgToFrac;

            var shuffleBgr24Out = YCoCgR32Sse41Vectors.ShuffleBgr24Out;

            while (i + 8 <= count)
            {
                // Process 8 pixels as 2x4
                var ycocg0 = Sse2.LoadVector128(src + (i * 4));
                var ycocg1 = Sse2.LoadVector128(src + (i * 4) + 16);

                // First 4 pixels
                var y16Lo = Ssse3.Shuffle(ycocg0, shuffleY).AsInt16();
                var coH16Lo = Ssse3.Shuffle(ycocg0, shuffleCoH).AsInt16();
                var cgH16Lo = Ssse3.Shuffle(ycocg0, shuffleCgH).AsInt16();
                var frac16Lo = Ssse3.Shuffle(ycocg0, shuffleFrac).AsInt16();

                var coLsbLo = Sse2.And(frac16Lo, one16);
                var cgLsbLo = Sse2.And(Sse2.ShiftRightLogical(frac16Lo, 1), one16);
                var coFullLo = Sse2.Or(Sse2.ShiftLeftLogical(coH16Lo, 1), coLsbLo);
                var cgFullLo = Sse2.Or(Sse2.ShiftLeftLogical(cgH16Lo, 1), cgLsbLo);
                var coLo = Sse2.Subtract(coFullLo, offset255);
                var cgLo = Sse2.Subtract(cgFullLo, offset255);

                var cgSraLo = Sse2.ShiftRightArithmetic(cgLo, 1);
                var tLo = Sse2.Subtract(y16Lo, cgSraLo);
                var g16Lo = Sse2.Add(cgLo, tLo);
                var coSraLo = Sse2.ShiftRightArithmetic(coLo, 1);
                var b16Lo = Sse2.Subtract(tLo, coSraLo);
                var r16Lo = Sse2.Add(b16Lo, coLo);

                var b8Lo = Sse2.PackUnsignedSaturate(b16Lo, b16Lo);
                var g8Lo = Sse2.PackUnsignedSaturate(g16Lo, g16Lo);
                var r8Lo = Sse2.PackUnsignedSaturate(r16Lo, r16Lo);
                var bgLo = Sse2.UnpackLow(b8Lo, g8Lo);
                var rxLo = Sse2.UnpackLow(r8Lo, Vector128<byte>.Zero);
                var bgr32Lo = Sse2.UnpackLow(bgLo.AsInt16(), rxLo.AsInt16()).AsByte();
                var bgr24Lo = Ssse3.Shuffle(bgr32Lo, shuffleBgr24Out);

                // Second 4 pixels
                var y16Hi = Ssse3.Shuffle(ycocg1, shuffleY).AsInt16();
                var coH16Hi = Ssse3.Shuffle(ycocg1, shuffleCoH).AsInt16();
                var cgH16Hi = Ssse3.Shuffle(ycocg1, shuffleCgH).AsInt16();
                var frac16Hi = Ssse3.Shuffle(ycocg1, shuffleFrac).AsInt16();

                var coLsbHi = Sse2.And(frac16Hi, one16);
                var cgLsbHi = Sse2.And(Sse2.ShiftRightLogical(frac16Hi, 1), one16);
                var coFullHi = Sse2.Or(Sse2.ShiftLeftLogical(coH16Hi, 1), coLsbHi);
                var cgFullHi = Sse2.Or(Sse2.ShiftLeftLogical(cgH16Hi, 1), cgLsbHi);
                var coHi = Sse2.Subtract(coFullHi, offset255);
                var cgHi = Sse2.Subtract(cgFullHi, offset255);

                var cgSraHi = Sse2.ShiftRightArithmetic(cgHi, 1);
                var tHi = Sse2.Subtract(y16Hi, cgSraHi);
                var g16Hi = Sse2.Add(cgHi, tHi);
                var coSraHi = Sse2.ShiftRightArithmetic(coHi, 1);
                var b16Hi = Sse2.Subtract(tHi, coSraHi);
                var r16Hi = Sse2.Add(b16Hi, coHi);

                var b8Hi = Sse2.PackUnsignedSaturate(b16Hi, b16Hi);
                var g8Hi = Sse2.PackUnsignedSaturate(g16Hi, g16Hi);
                var r8Hi = Sse2.PackUnsignedSaturate(r16Hi, r16Hi);
                var bgHi = Sse2.UnpackLow(b8Hi, g8Hi);
                var rxHi = Sse2.UnpackLow(r8Hi, Vector128<byte>.Zero);
                var bgr32Hi = Sse2.UnpackLow(bgHi.AsInt16(), rxHi.AsInt16()).AsByte();
                var bgr24Hi = Ssse3.Shuffle(bgr32Hi, shuffleBgr24Out);

                // Write 12 + 12 bytes
                Unsafe.WriteUnaligned(dst + (i * 3), bgr24Lo.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + (i * 3) + 8, bgr24Lo.AsUInt32().GetElement(2));
                Unsafe.WriteUnaligned(dst + (i * 3) + 12, bgr24Hi.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + (i * 3) + 20, bgr24Hi.AsUInt32().GetElement(2));

                i += 8;
            }

            if (i + 4 <= count)
            {
                ToBgr24Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = source[i].ToBgr24();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Bgr24 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явная конвертация YCoCgR32 → Bgr24.</summary>
    public static explicit operator Bgr24(YCoCgR32 ycocg) => ycocg.ToBgr24();

    #endregion
}
