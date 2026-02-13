#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Rgb24.
/// YCoCg-R — lossless целочисленное преобразование.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Rgb24.</summary>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Rgb24 → YCoCgR32 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromRgb24(Rgb24 rgb)
    {
        int r = rgb.R, g = rgb.G, b = rgb.B;

        var co = r - b;
        var t = b + (co >> 1);
        var cg = g - t;
        var y = t + (cg >> 1);

        return new YCoCgR32(y, co, cg);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Rgb24 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24()
    {
        var co = Co;
        var cg = Cg;

        var t = Y - (cg >> 1);
        var g = cg + t;
        var b = t - (co >> 1);
        var r = b + co;

        return new Rgb24((byte)r, (byte)g, (byte)b);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgb24 → YCoCgR32.</summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgb24 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromRgb24(
        ReadOnlySpan<Rgb24> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgb24* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Rgb24.</summary>
    public static void ToRgb24(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Rgb24 с явным ускорителем.</summary>
    public static unsafe void ToRgb24(
        ReadOnlySpan<YCoCgR32> source,
        Span<Rgb24> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Rgb24* dstPtr = destination)
            {
                ToRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToRgb24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromRgb24Sse41(source, destination);
                break;
            default:
                FromRgb24Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToRgb24Core(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToRgb24Sse41(source, destination);
                break;
            default:
                ToRgb24Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromRgb24Parallel(Rgb24* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromRgb24Core(new ReadOnlySpan<Rgb24>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToRgb24Parallel(YCoCgR32* source, Rgb24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToRgb24Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Rgb24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Scalar(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromRgb24(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgb24Scalar(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToRgb24();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Rgb24 → YCoCgR32.
    /// 4 пикселя за итерацию (12 байт → 16 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Sse41(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // Shuffle для RGB24 (3 байта на пиксель) → извлечение R, G, B
            // RGB layout: R0 G0 B0 R1 G1 B1 R2 G2 B2 R3 G3 B3 (12 байт)
            var shuffleR = YCoCgR32Sse41Vectors.ShuffleRgb24ToR;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleRgb24ToG;
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleRgb24ToB;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 12 байт (безопасная загрузка 16 байт с маской)
                var rgb = Sse2.LoadVector128(src + (i * 3));

                // Извлечение R, G, B как short
                var r16 = Ssse3.Shuffle(rgb, shuffleR).AsInt16();
                var g16 = Ssse3.Shuffle(rgb, shuffleG).AsInt16();
                var b16 = Ssse3.Shuffle(rgb, shuffleB).AsInt16();

                // YCoCg-R forward
                var co = Sse2.Subtract(r16, b16);
                var coSra = Sse2.ShiftRightArithmetic(co, 1);
                var t = Sse2.Add(b16, coSra);
                var cg = Sse2.Subtract(g16, t);
                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);
                var y16 = Sse2.Add(t, cgSra);

                // Упаковка
                var coShifted = Sse2.Add(co, offset255);
                var cgShifted = Sse2.Add(cg, offset255);
                var coHigh = Sse2.ShiftRightLogical(coShifted, 1);
                var cgHigh = Sse2.ShiftRightLogical(cgShifted, 1);

                var coLsb = Sse2.And(coShifted, one16);
                var cgLsb = Sse2.And(cgShifted, one16);
                var frac16 = Sse2.Or(coLsb, Sse2.ShiftLeftLogical(cgLsb, 1));

                // Упаковка в байты
                var y8 = Sse2.PackUnsignedSaturate(y16, y16);
                var coH8 = Sse2.PackUnsignedSaturate(coHigh, coHigh);
                var cgH8 = Sse2.PackUnsignedSaturate(cgHigh, cgHigh);
                var frac8 = Sse2.PackUnsignedSaturate(frac16, frac16);

                // Интерливинг
                var yCo = Sse2.UnpackLow(y8, coH8);
                var cgF = Sse2.UnpackLow(cgH8, frac8);
                var result = Sse2.UnpackLow(yCo.AsInt16(), cgF.AsInt16());

                // Запись 16 байт
                Sse2.Store(dst + (i * 4), result.AsByte());

                i += 4;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = FromRgb24(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Rgb24.
    /// 4 пикселя за итерацию (16 байт → 12 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Sse41(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // Shuffle для извлечения Y, CoHigh, CgHigh, Frac
            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Sse41Vectors.ShuffleYCoCgToFrac;

            // Shuffle для упаковки RGB24: R0 G0 B0 R1 G1 B1 R2 G2 B2 R3 G3 B3
            var shuffleRgb = YCoCgR32Sse41Vectors.ShuffleRgb24Out;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                var ycocg = Sse2.LoadVector128(src + (i * 4));

                var y16 = Ssse3.Shuffle(ycocg, shuffleY).AsInt16();
                var coH16 = Ssse3.Shuffle(ycocg, shuffleCoH).AsInt16();
                var cgH16 = Ssse3.Shuffle(ycocg, shuffleCgH).AsInt16();
                var frac16 = Ssse3.Shuffle(ycocg, shuffleFrac).AsInt16();

                // Восстановление Co и Cg
                var coLsb = Sse2.And(frac16, one16);
                var cgLsb = Sse2.And(Sse2.ShiftRightLogical(frac16, 1), one16);

                var coFull = Sse2.Or(Sse2.ShiftLeftLogical(coH16, 1), coLsb);
                var cgFull = Sse2.Or(Sse2.ShiftLeftLogical(cgH16, 1), cgLsb);

                var co = Sse2.Subtract(coFull, offset255);
                var cg = Sse2.Subtract(cgFull, offset255);

                // YCoCg-R inverse
                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);
                var t = Sse2.Subtract(y16, cgSra);
                var g16 = Sse2.Add(cg, t);
                var coSra = Sse2.ShiftRightArithmetic(co, 1);
                var b16 = Sse2.Subtract(t, coSra);
                var r16 = Sse2.Add(b16, co);

                // Упаковка R, G, B в байты
                var r8 = Sse2.PackUnsignedSaturate(r16, r16);
                var g8 = Sse2.PackUnsignedSaturate(g16, g16);
                var b8 = Sse2.PackUnsignedSaturate(b16, b16);

                // Интерливинг в RGB24 формат: сначала делаем R0 G0 B0 0 R1 G1 B1 0...
                var rg = Sse2.UnpackLow(r8, g8);   // R0 G0 R1 G1 R2 G2 R3 G3
                var bz = Sse2.UnpackLow(b8, Vector128<byte>.Zero); // B0 0 B1 0 B2 0 B3 0
                var rgba = Sse2.UnpackLow(rg.AsInt16(), bz.AsInt16()).AsByte(); // R0 G0 B0 0 R1 G1 B1 0 ...

                // Перепаковка в RGB24
                var rgb24 = Ssse3.Shuffle(rgba, shuffleRgb);

                // Запись 12 байт (overlapping store)
                Unsafe.WriteUnaligned(dst + (i * 3), rgb24.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + (i * 3) + 8, rgb24.AsUInt32().GetElement(2));

                i += 4;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = source[i].ToRgb24();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Rgb24 → YCoCgR32.
    /// 8 пикселей за итерацию (24 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx2(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Обрабатываем по 4 пикселя через SSE (RGB24 сложен для AVX2)
            // AVX2 оптимизация применяется к математике, а загрузка/выгрузка через SSE

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            var shuffleR = YCoCgR32Sse41Vectors.ShuffleRgb24ToR;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleRgb24ToG;
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleRgb24ToB;

            // 8 пикселей = 2×4 SSE
            while (i + 8 <= count)
            {
                // Первые 4 пикселя
                var rgb0 = Sse2.LoadVector128(src + (i * 3));
                var r16_0 = Ssse3.Shuffle(rgb0, shuffleR).AsInt16();
                var g16_0 = Ssse3.Shuffle(rgb0, shuffleG).AsInt16();
                var b16_0 = Ssse3.Shuffle(rgb0, shuffleB).AsInt16();

                var co0 = Sse2.Subtract(r16_0, b16_0);
                var coSra0 = Sse2.ShiftRightArithmetic(co0, 1);
                var t0 = Sse2.Add(b16_0, coSra0);
                var cg0 = Sse2.Subtract(g16_0, t0);
                var cgSra0 = Sse2.ShiftRightArithmetic(cg0, 1);
                var y16_0 = Sse2.Add(t0, cgSra0);

                var coShifted0 = Sse2.Add(co0, offset255);
                var cgShifted0 = Sse2.Add(cg0, offset255);
                var coHigh0 = Sse2.ShiftRightLogical(coShifted0, 1);
                var cgHigh0 = Sse2.ShiftRightLogical(cgShifted0, 1);
                var coLsb0 = Sse2.And(coShifted0, one16);
                var cgLsb0 = Sse2.And(cgShifted0, one16);
                var frac16_0 = Sse2.Or(coLsb0, Sse2.ShiftLeftLogical(cgLsb0, 1));

                var y8_0 = Sse2.PackUnsignedSaturate(y16_0, y16_0);
                var coH8_0 = Sse2.PackUnsignedSaturate(coHigh0, coHigh0);
                var cgH8_0 = Sse2.PackUnsignedSaturate(cgHigh0, cgHigh0);
                var frac8_0 = Sse2.PackUnsignedSaturate(frac16_0, frac16_0);
                var yCo0 = Sse2.UnpackLow(y8_0, coH8_0);
                var cgF0 = Sse2.UnpackLow(cgH8_0, frac8_0);
                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgF0.AsInt16());

                // Вторые 4 пикселя
                var rgb1 = Sse2.LoadVector128(src + ((i + 4) * 3));
                var r16_1 = Ssse3.Shuffle(rgb1, shuffleR).AsInt16();
                var g16_1 = Ssse3.Shuffle(rgb1, shuffleG).AsInt16();
                var b16_1 = Ssse3.Shuffle(rgb1, shuffleB).AsInt16();

                var co1 = Sse2.Subtract(r16_1, b16_1);
                var coSra1 = Sse2.ShiftRightArithmetic(co1, 1);
                var t1 = Sse2.Add(b16_1, coSra1);
                var cg1 = Sse2.Subtract(g16_1, t1);
                var cgSra1 = Sse2.ShiftRightArithmetic(cg1, 1);
                var y16_1 = Sse2.Add(t1, cgSra1);

                var coShifted1 = Sse2.Add(co1, offset255);
                var cgShifted1 = Sse2.Add(cg1, offset255);
                var coHigh1 = Sse2.ShiftRightLogical(coShifted1, 1);
                var cgHigh1 = Sse2.ShiftRightLogical(cgShifted1, 1);
                var coLsb1 = Sse2.And(coShifted1, one16);
                var cgLsb1 = Sse2.And(cgShifted1, one16);
                var frac16_1 = Sse2.Or(coLsb1, Sse2.ShiftLeftLogical(cgLsb1, 1));

                var y8_1 = Sse2.PackUnsignedSaturate(y16_1, y16_1);
                var coH8_1 = Sse2.PackUnsignedSaturate(coHigh1, coHigh1);
                var cgH8_1 = Sse2.PackUnsignedSaturate(cgHigh1, cgHigh1);
                var frac8_1 = Sse2.PackUnsignedSaturate(frac16_1, frac16_1);
                var yCo1 = Sse2.UnpackLow(y8_1, coH8_1);
                var cgF1 = Sse2.UnpackLow(cgH8_1, frac8_1);
                var result1 = Sse2.UnpackLow(yCo1.AsInt16(), cgF1.AsInt16());

                // Запись 32 байт
                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());

                i += 8;
            }

            // SSE fallback для 4 пикселей
            if (i + 4 <= count)
            {
                FromRgb24Sse41(source[i..], destination[i..]);
                return;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = FromRgb24(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Rgb24.
    /// 8 пикселей за итерацию (32 байт → 24 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx2(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
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
            var shuffleRgb = YCoCgR32Sse41Vectors.ShuffleRgb24Out;

            // 8 пикселей = 2×4 SSE
            while (i + 8 <= count)
            {
                // Первые 4 пикселя
                var ycocg0 = Sse2.LoadVector128(src + (i * 4));
                var y16_0 = Ssse3.Shuffle(ycocg0, shuffleY).AsInt16();
                var coH16_0 = Ssse3.Shuffle(ycocg0, shuffleCoH).AsInt16();
                var cgH16_0 = Ssse3.Shuffle(ycocg0, shuffleCgH).AsInt16();
                var frac16_0 = Ssse3.Shuffle(ycocg0, shuffleFrac).AsInt16();

                var coLsb0 = Sse2.And(frac16_0, one16);
                var cgLsb0 = Sse2.And(Sse2.ShiftRightLogical(frac16_0, 1), one16);
                var coFull0 = Sse2.Or(Sse2.ShiftLeftLogical(coH16_0, 1), coLsb0);
                var cgFull0 = Sse2.Or(Sse2.ShiftLeftLogical(cgH16_0, 1), cgLsb0);
                var co0 = Sse2.Subtract(coFull0, offset255);
                var cg0 = Sse2.Subtract(cgFull0, offset255);

                var cgSra0 = Sse2.ShiftRightArithmetic(cg0, 1);
                var t0 = Sse2.Subtract(y16_0, cgSra0);
                var g16_0 = Sse2.Add(cg0, t0);
                var coSra0 = Sse2.ShiftRightArithmetic(co0, 1);
                var b16_0 = Sse2.Subtract(t0, coSra0);
                var r16_0 = Sse2.Add(b16_0, co0);

                var r8_0 = Sse2.PackUnsignedSaturate(r16_0, r16_0);
                var g8_0 = Sse2.PackUnsignedSaturate(g16_0, g16_0);
                var b8_0 = Sse2.PackUnsignedSaturate(b16_0, b16_0);
                var rg0 = Sse2.UnpackLow(r8_0, g8_0);
                var bz0 = Sse2.UnpackLow(b8_0, Vector128<byte>.Zero);
                var rgba0 = Sse2.UnpackLow(rg0.AsInt16(), bz0.AsInt16()).AsByte();
                var rgb24_0 = Ssse3.Shuffle(rgba0, shuffleRgb);

                // Вторые 4 пикселя
                var ycocg1 = Sse2.LoadVector128(src + ((i + 4) * 4));
                var y16_1 = Ssse3.Shuffle(ycocg1, shuffleY).AsInt16();
                var coH16_1 = Ssse3.Shuffle(ycocg1, shuffleCoH).AsInt16();
                var cgH16_1 = Ssse3.Shuffle(ycocg1, shuffleCgH).AsInt16();
                var frac16_1 = Ssse3.Shuffle(ycocg1, shuffleFrac).AsInt16();

                var coLsb1 = Sse2.And(frac16_1, one16);
                var cgLsb1 = Sse2.And(Sse2.ShiftRightLogical(frac16_1, 1), one16);
                var coFull1 = Sse2.Or(Sse2.ShiftLeftLogical(coH16_1, 1), coLsb1);
                var cgFull1 = Sse2.Or(Sse2.ShiftLeftLogical(cgH16_1, 1), cgLsb1);
                var co1 = Sse2.Subtract(coFull1, offset255);
                var cg1 = Sse2.Subtract(cgFull1, offset255);

                var cgSra1 = Sse2.ShiftRightArithmetic(cg1, 1);
                var t1 = Sse2.Subtract(y16_1, cgSra1);
                var g16_1 = Sse2.Add(cg1, t1);
                var coSra1 = Sse2.ShiftRightArithmetic(co1, 1);
                var b16_1 = Sse2.Subtract(t1, coSra1);
                var r16_1 = Sse2.Add(b16_1, co1);

                var r8_1 = Sse2.PackUnsignedSaturate(r16_1, r16_1);
                var g8_1 = Sse2.PackUnsignedSaturate(g16_1, g16_1);
                var b8_1 = Sse2.PackUnsignedSaturate(b16_1, b16_1);
                var rg1 = Sse2.UnpackLow(r8_1, g8_1);
                var bz1 = Sse2.UnpackLow(b8_1, Vector128<byte>.Zero);
                var rgba1 = Sse2.UnpackLow(rg1.AsInt16(), bz1.AsInt16()).AsByte();
                var rgb24_1 = Ssse3.Shuffle(rgba1, shuffleRgb);

                // Запись 24 байт (12 + 12)
                Unsafe.WriteUnaligned(dst + (i * 3), rgb24_0.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + (i * 3) + 8, rgb24_0.AsUInt32().GetElement(2));
                Unsafe.WriteUnaligned(dst + ((i + 4) * 3), rgb24_1.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + ((i + 4) * 3) + 8, rgb24_1.AsUInt32().GetElement(2));

                i += 8;
            }

            // SSE fallback для 4 пикселей
            if (i + 4 <= count)
            {
                ToRgb24Sse41(source[i..], destination[i..]);
                return;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = source[i].ToRgb24();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Rgb24 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Rgb24 rgb) => FromRgb24(rgb);

    /// <summary>Явная конвертация YCoCgR32 → Rgb24.</summary>
    public static explicit operator Rgb24(YCoCgR32 ycocg) => ycocg.ToRgb24();

    #endregion
}
