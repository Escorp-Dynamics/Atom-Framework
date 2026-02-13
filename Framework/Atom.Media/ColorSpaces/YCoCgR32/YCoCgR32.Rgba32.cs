#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Rgba32.
/// YCoCg-R — lossless целочисленное преобразование.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Rgba32.</summary>
    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Rgba32 → YCoCgR32 (lossless, альфа игнорируется).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromRgba32(Rgba32 rgba)
    {
        int r = rgba.R, g = rgba.G, b = rgba.B;

        var co = r - b;
        var t = b + (co >> 1);
        var cg = g - t;
        var y = t + (cg >> 1);

        return new YCoCgR32(y, co, cg);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Rgba32 (lossless, A=255).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32()
    {
        var co = Co;
        var cg = Cg;

        var t = Y - (cg >> 1);
        var g = cg + t;
        var b = t - (co >> 1);
        var r = b + co;

        return new Rgba32((byte)r, (byte)g, (byte)b, 255);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgba32 → YCoCgR32.</summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromRgba32(
        ReadOnlySpan<Rgba32> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Rgba32.</summary>
    public static void ToRgba32(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Rgba32 с явным ускорителем.</summary>
    public static unsafe void ToRgba32(
        ReadOnlySpan<YCoCgR32> source,
        Span<Rgba32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
            {
                ToRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromRgba32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromRgba32Sse41(source, destination);
                break;
            default:
                FromRgba32Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToRgba32Core(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToRgba32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToRgba32Sse41(source, destination);
                break;
            default:
                ToRgba32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromRgba32Parallel(Rgba32* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromRgba32Core(new ReadOnlySpan<Rgba32>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToRgba32Parallel(YCoCgR32* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToRgba32Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Rgba32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Scalar(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromRgba32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Scalar(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToRgba32();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Rgba32 → YCoCgR32.
    /// 4 пикселя за итерацию (16 байт → 16 байт).
    /// </summary>
    /// <remarks>
    /// YCoCg-R Forward:
    /// Co = R - B ([-255, 255]),
    /// t = B + (Co &gt;&gt; 1) ([0, 255]),
    /// Cg = G - t ([-255, 255]),
    /// Y = t + (Cg &gt;&gt; 1) ([0, 255]).
    ///
    /// Упаковка:
    /// CoShifted = Co + 255 ([0, 510]),
    /// CgShifted = Cg + 255 ([0, 510]),
    /// CoHigh = CoShifted &gt;&gt; 1 ([0, 255]),
    /// CgHigh = CgShifted &gt;&gt; 1 ([0, 255]),
    /// Frac = (CoShifted &amp; 1) | ((CgShifted &amp; 1) &lt;&lt; 1).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Sse41(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Статические константы
            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;

            // Shuffle маски для извлечения R, G, B из RGBA (4 пикселя)
            var shuffleR = YCoCgR32Sse41Vectors.ShuffleRgbaToR;
            var shuffleG = YCoCgR32Sse41Vectors.ShuffleRgbaToG;
            var shuffleB = YCoCgR32Sse41Vectors.ShuffleRgbaToB;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей RGBA (16 байт)
                var rgba = Sse2.LoadVector128(src + (i * 4));

                // Извлечение R, G, B как short (zero-extended)
                var r16 = Ssse3.Shuffle(rgba, shuffleR).AsInt16();
                var g16 = Ssse3.Shuffle(rgba, shuffleG).AsInt16();
                var b16 = Ssse3.Shuffle(rgba, shuffleB).AsInt16();

                // YCoCg-R forward transform
                var co = Sse2.Subtract(r16, b16);                      // Co = R - B
                var coSra = Sse2.ShiftRightArithmetic(co, 1);          // Co >> 1 (arithmetic)
                var t = Sse2.Add(b16, coSra);                          // t = B + (Co >> 1)
                var cg = Sse2.Subtract(g16, t);                        // Cg = G - t
                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);          // Cg >> 1
                var y16 = Sse2.Add(t, cgSra);                          // Y = t + (Cg >> 1)

                // Упаковка Co, Cg в 8-bit формат
                var coShifted = Sse2.Add(co, offset255);               // Co + 255 → [0, 510]
                var cgShifted = Sse2.Add(cg, offset255);               // Cg + 255 → [0, 510]
                var coHigh = Sse2.ShiftRightLogical(coShifted, 1);     // CoHigh = (Co + 255) >> 1
                var cgHigh = Sse2.ShiftRightLogical(cgShifted, 1);     // CgHigh = (Cg + 255) >> 1

                // Frac = (CoShifted & 1) | ((CgShifted & 1) << 1)
                var coLsb = Sse2.And(coShifted, one16);                // CoShifted & 1
                var cgLsb = Sse2.And(cgShifted, one16);                // CgShifted & 1
                var frac16 = Sse2.Or(coLsb, Sse2.ShiftLeftLogical(cgLsb, 1));

                // Упаковка 4 значений short → byte для каждого канала
                // Y, CoHigh, CgHigh, Frac все в диапазоне [0, 255]
                var y8 = Sse2.PackUnsignedSaturate(y16, y16);          // 8 байт, используем первые 4
                var coH8 = Sse2.PackUnsignedSaturate(coHigh, coHigh);
                var cgH8 = Sse2.PackUnsignedSaturate(cgHigh, cgHigh);
                var frac8 = Sse2.PackUnsignedSaturate(frac16, frac16);

                // Интерливинг: Y0 CoH0 CgH0 F0 | Y1 CoH1 CgH1 F1 | ...
                // Сначала: Y0 CoH0 Y1 CoH1 Y2 CoH2 Y3 CoH3
                var yCo = Sse2.UnpackLow(y8, coH8);
                // Затем: CgH0 F0 CgH1 F1 CgH2 F2 CgH3 F3
                var cgF = Sse2.UnpackLow(cgH8, frac8);
                // Финальный интерливинг: Y0 CoH0 CgH0 F0 | Y1 CoH1 CgH1 F1 | ...
                var result = Sse2.UnpackLow(yCo.AsInt16(), cgF.AsInt16());

                // Запись 16 байт (4 пикселя YCoCgR32)
                Sse2.Store(dst + (i * 4), result.AsByte());

                i += 4;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = FromRgba32(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Rgba32.
    /// 4 пикселя за итерацию (16 байт → 16 байт).
    /// </summary>
    /// <remarks>
    /// YCoCg-R Inverse:
    /// Co = (CoHigh &lt;&lt; 1) | (Frac &amp; 1) - 255,
    /// Cg = (CgHigh &lt;&lt; 1) | ((Frac &gt;&gt; 1) &amp; 1) - 255,
    /// t = Y - (Cg &gt;&gt; 1),
    /// G = Cg + t,
    /// B = t - (Co &gt;&gt; 1),
    /// R = B + Co.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Sse41(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Sse41Vectors.Offset255;
            var one16 = YCoCgR32Sse41Vectors.One;
            var alpha255 = YCoCgR32Sse41Vectors.Alpha255;

            // Shuffle маски для извлечения Y, CoHigh, CgHigh, Frac
            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;
            var shuffleCoH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCoH;
            var shuffleCgH = YCoCgR32Sse41Vectors.ShuffleYCoCgToCgH;
            var shuffleFrac = YCoCgR32Sse41Vectors.ShuffleYCoCgToFrac;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей YCoCgR32 (16 байт)
                var ycocg = Sse2.LoadVector128(src + (i * 4));

                // Извлечение компонент как short
                var y16 = Ssse3.Shuffle(ycocg, shuffleY).AsInt16();
                var coH16 = Ssse3.Shuffle(ycocg, shuffleCoH).AsInt16();
                var cgH16 = Ssse3.Shuffle(ycocg, shuffleCgH).AsInt16();
                var frac16 = Ssse3.Shuffle(ycocg, shuffleFrac).AsInt16();

                // Восстановление Co и Cg:
                // Co = (CoHigh << 1) | (Frac & 1) - 255
                // Cg = (CgHigh << 1) | ((Frac >> 1) & 1) - 255
                var coLsb = Sse2.And(frac16, one16);                           // Frac & 1
                var cgLsb = Sse2.And(Sse2.ShiftRightLogical(frac16, 1), one16); // (Frac >> 1) & 1

                var coFull = Sse2.Or(Sse2.ShiftLeftLogical(coH16, 1), coLsb);  // (CoHigh << 1) | coLsb
                var cgFull = Sse2.Or(Sse2.ShiftLeftLogical(cgH16, 1), cgLsb);  // (CgHigh << 1) | cgLsb

                var co = Sse2.Subtract(coFull, offset255);                      // Co = coFull - 255
                var cg = Sse2.Subtract(cgFull, offset255);                      // Cg = cgFull - 255

                // YCoCg-R inverse transform
                var cgSra = Sse2.ShiftRightArithmetic(cg, 1);                   // Cg >> 1
                var t = Sse2.Subtract(y16, cgSra);                              // t = Y - (Cg >> 1)
                var g16 = Sse2.Add(cg, t);                                      // G = Cg + t
                var coSra = Sse2.ShiftRightArithmetic(co, 1);                   // Co >> 1
                var b16 = Sse2.Subtract(t, coSra);                              // B = t - (Co >> 1)
                var r16 = Sse2.Add(b16, co);                                    // R = B + Co

                // Упаковка R, G, B в байты
                var r8 = Sse2.PackUnsignedSaturate(r16, r16);
                var g8 = Sse2.PackUnsignedSaturate(g16, g16);
                var b8 = Sse2.PackUnsignedSaturate(b16, b16);

                // Интерливинг: R0 G0 | R1 G1 | R2 G2 | R3 G3
                var rg = Sse2.UnpackLow(r8, g8);
                // B0 0 | B1 0 | B2 0 | B3 0 (нули для альфы, заменим на 255)
                var ba = Sse2.UnpackLow(b8, Vector128<byte>.Zero);
                // R0 G0 B0 0 | R1 G1 B1 0 | R2 G2 B2 0 | R3 G3 B3 0
                var rgba = Sse2.UnpackLow(rg.AsInt16(), ba.AsInt16()).AsByte();

                // Установка альфа = 255
                rgba = Sse2.Or(rgba, alpha255);

                // Запись 16 байт (4 пикселя Rgba32)
                Sse2.Store(dst + (i * 4), rgba);

                i += 4;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = source[i].ToRgba32();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Rgba32 → YCoCgR32.
    /// 8 пикселей за итерацию (32 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Avx2(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var offset255 = YCoCgR32Avx2Vectors.Offset255;
            var one16 = YCoCgR32Avx2Vectors.One;

            // Shuffle маски для извлечения R, G, B (in-lane)
            var shuffleR = YCoCgR32Avx2Vectors.ShuffleRgbaToR;
            var shuffleG = YCoCgR32Avx2Vectors.ShuffleRgbaToG;
            var shuffleB = YCoCgR32Avx2Vectors.ShuffleRgbaToB;

            // 8 пикселей за итерацию
            while (i + 8 <= count)
            {
                // Загрузка 8 пикселей RGBA (32 байт)
                var rgba = Avx.LoadVector256(src + (i * 4));

                // Извлечение R, G, B как short (каждая lane обрабатывает 4 пикселя)
                var r16 = Avx2.Shuffle(rgba, shuffleR).AsInt16();
                var g16 = Avx2.Shuffle(rgba, shuffleG).AsInt16();
                var b16 = Avx2.Shuffle(rgba, shuffleB).AsInt16();

                // YCoCg-R forward
                var co = Avx2.Subtract(r16, b16);
                var coSra = Avx2.ShiftRightArithmetic(co, 1);
                var t = Avx2.Add(b16, coSra);
                var cg = Avx2.Subtract(g16, t);
                var cgSra = Avx2.ShiftRightArithmetic(cg, 1);
                var y16 = Avx2.Add(t, cgSra);

                // Упаковка
                var coShifted = Avx2.Add(co, offset255);
                var cgShifted = Avx2.Add(cg, offset255);
                var coHigh = Avx2.ShiftRightLogical(coShifted, 1);
                var cgHigh = Avx2.ShiftRightLogical(cgShifted, 1);

                var coLsb = Avx2.And(coShifted, one16);
                var cgLsb = Avx2.And(cgShifted, one16);
                var frac16 = Avx2.Or(coLsb, Avx2.ShiftLeftLogical(cgLsb, 1));

                // Упаковка в байты — обрабатываем каждую lane отдельно
                // Lower lane (пиксели 0-3)
                var y8Lo = Sse2.PackUnsignedSaturate(y16.GetLower(), y16.GetLower());
                var coH8Lo = Sse2.PackUnsignedSaturate(coHigh.GetLower(), coHigh.GetLower());
                var cgH8Lo = Sse2.PackUnsignedSaturate(cgHigh.GetLower(), cgHigh.GetLower());
                var frac8Lo = Sse2.PackUnsignedSaturate(frac16.GetLower(), frac16.GetLower());

                var yCoLo = Sse2.UnpackLow(y8Lo, coH8Lo);
                var cgFLo = Sse2.UnpackLow(cgH8Lo, frac8Lo);
                var resultLo = Sse2.UnpackLow(yCoLo.AsInt16(), cgFLo.AsInt16());

                // Upper lane (пиксели 4-7)
                var y8Hi = Sse2.PackUnsignedSaturate(y16.GetUpper(), y16.GetUpper());
                var coH8Hi = Sse2.PackUnsignedSaturate(coHigh.GetUpper(), coHigh.GetUpper());
                var cgH8Hi = Sse2.PackUnsignedSaturate(cgHigh.GetUpper(), cgHigh.GetUpper());
                var frac8Hi = Sse2.PackUnsignedSaturate(frac16.GetUpper(), frac16.GetUpper());

                var yCoHi = Sse2.UnpackLow(y8Hi, coH8Hi);
                var cgFHi = Sse2.UnpackLow(cgH8Hi, frac8Hi);
                var resultHi = Sse2.UnpackLow(yCoHi.AsInt16(), cgFHi.AsInt16());

                // Запись 32 байт
                Sse2.Store(dst + (i * 4), resultLo.AsByte());
                Sse2.Store(dst + (i * 4) + 16, resultHi.AsByte());

                i += 8;
            }

            // SSE fallback для 4 пикселей
            if (i + 4 <= count)
            {
                FromRgba32Sse41(source[i..], destination[i..]);
                return;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = FromRgba32(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Rgba32.
    /// 8 пикселей за итерацию (32 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Avx2(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
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

            // 8 пикселей за итерацию
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

                // Упаковка каждой lane
                var r8Lo = Sse2.PackUnsignedSaturate(r16.GetLower(), r16.GetLower());
                var g8Lo = Sse2.PackUnsignedSaturate(g16.GetLower(), g16.GetLower());
                var b8Lo = Sse2.PackUnsignedSaturate(b16.GetLower(), b16.GetLower());

                var rgLo = Sse2.UnpackLow(r8Lo, g8Lo);
                var baLo = Sse2.UnpackLow(b8Lo, Vector128<byte>.Zero);
                var rgbaLo = Sse2.UnpackLow(rgLo.AsInt16(), baLo.AsInt16()).AsByte();
                rgbaLo = Sse2.Or(rgbaLo, alpha255);

                var r8Hi = Sse2.PackUnsignedSaturate(r16.GetUpper(), r16.GetUpper());
                var g8Hi = Sse2.PackUnsignedSaturate(g16.GetUpper(), g16.GetUpper());
                var b8Hi = Sse2.PackUnsignedSaturate(b16.GetUpper(), b16.GetUpper());

                var rgHi = Sse2.UnpackLow(r8Hi, g8Hi);
                var baHi = Sse2.UnpackLow(b8Hi, Vector128<byte>.Zero);
                var rgbaHi = Sse2.UnpackLow(rgHi.AsInt16(), baHi.AsInt16()).AsByte();
                rgbaHi = Sse2.Or(rgbaHi, alpha255);

                Sse2.Store(dst + (i * 4), rgbaLo);
                Sse2.Store(dst + (i * 4) + 16, rgbaHi);

                i += 8;
            }

            // SSE fallback для 4 пикселей
            if (i + 4 <= count)
            {
                ToRgba32Sse41(source[i..], destination[i..]);
                return;
            }

            // Остаток скалярно
            while (i < count)
            {
                destination[i] = source[i].ToRgba32();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Rgba32 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Rgba32 rgba) => FromRgba32(rgba);

    /// <summary>Явная конвертация YCoCgR32 → Rgba32.</summary>
    public static explicit operator Rgba32(YCoCgR32 ycocg) => ycocg.ToRgba32();

    #endregion
}
