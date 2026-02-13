#pragma warning disable CA1000, CA2208, IDE0004, IDE0005, IDE0032, IDE0042, IDE0045, IDE0047, IDE0048, IDE0054, IDE0074, MA0051, MA0084, S1117, S3776, S4136

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Rgb24.
/// Основной алгоритм HSV ↔ RGB с LUT-оптимизациями.
/// </summary>
public readonly partial struct Hsv
{
    #region LUT Tables

    /// <summary>
    /// LUT для HSV→RGB: для каждого H (0-255) хранит (region, f).
    /// region = (h * 6) >> 8 (0-5), f = (h * 6) &amp; 255 (фракция 0-255).
    /// Используется симметричный масштаб с forward конверсией для минимизации round-trip ошибок.
    /// </summary>
    private static readonly (byte region, byte f)[] HueLut = CreateHueLut();

    /// <summary>
    /// LUT для RGB→HSV: reciprocal256[delta] = (256 &lt;&lt; 16) / delta для fixed-point деления.
    /// Используется для вычисления h6 = diff * 256 / delta через (diff * reciprocal256[delta]) &gt;&gt; 16.
    /// reciprocal256[0] = 0 (защита от div/0, delta=0 обрабатывается отдельно).
    /// </summary>
    private static readonly int[] Reciprocal256Lut = CreateReciprocal256Lut();

    /// <summary>
    /// LUT для RGB→HSV Saturation: reciprocal255[max] = (255 &lt;&lt; 16) / max для fixed-point деления.
    /// Позволяет вычислить S = (delta * 255) / max через (delta * reciprocal255[max]) >> 16.
    /// reciprocal255[0] = 0 (защита от div/0, max=0 означает чёрный цвет, S=0).
    /// </summary>
    private static readonly int[] Reciprocal255Lut = CreateReciprocal255Lut();

    /// <summary>
    /// LUT для lossless деления: recip24[x] = round(2^24 / x).
    /// Используется: a / b = (a * recip24[b] + 2^23) >> 24 — точное деление с округлением.
    /// recip24[0] = 0 (защита от деления на 0).
    /// </summary>
    private static readonly long[] Recip24Lut = CreateRecip24Lut();

    /// <summary>Получает (region, f) из HueLut для указанного H.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (byte region, byte f) GetHueLut(byte h) => HueLut[h];

    /// <summary>Получает reciprocal для деления на 255: (255 &lt;&lt; 16) / value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetReciprocal255(int value) => Reciprocal255Lut[value];

    /// <summary>Получает reciprocal для деления на 256: (256 &lt;&lt; 16) / value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetReciprocal256(int value) => Reciprocal256Lut[value];

    /// <summary>Получает указатель на Reciprocal255Lut для SIMD gather.</summary>
    internal static int[] Reciprocal255LutArray => Reciprocal255Lut;

    /// <summary>Получает указатель на Reciprocal256Lut для SIMD gather.</summary>
    internal static int[] Reciprocal256LutArray => Reciprocal256Lut;

    /// <summary>Получает указатель на Recip24Lut для SIMD gather.</summary>
    internal static long[] Recip24LutArray => Recip24Lut;

    private static (byte region, byte f)[] CreateHueLut()
    {
        // Симметричный масштаб: h*6 даёт 0-1530, region = h*6 >> 8, f = h*6 & 255
        // Это согласовано с forward конверсией для минимизации round-trip ошибок
        var lut = new (byte, byte)[256];
        for (var h = 0; h < 256; h++)
        {
            var h6 = h * 6;
            lut[h] = ((byte)(h6 >> 8), (byte)(h6 & 255));
        }
        return lut;
    }

    private static int[] CreateReciprocal256Lut()
    {
        // reciprocal256[delta] = (256 << 16) / delta
        // Используется для: diff * 256 / delta ≈ (diff * reciprocal256[delta]) >> 16
        // delta=0 -> 0 (защита, delta=0 обрабатывается через blend mask)
        var lut = new int[256];
        lut[0] = 0;
        for (var delta = 1; delta < 256; delta++)
            lut[delta] = (256 << 16) / delta;
        return lut;
    }

    private static int[] CreateReciprocal255Lut()
    {
        // reciprocal255[max] = (255 << 16) / max
        // Используется для: (delta * 255) / max ≈ (delta * reciprocal255[max]) >> 16
        // max=0 -> 0 (защита, max=0 означает чёрный цвет, S=0 обрабатывается через blend)
        var lut = new int[256];
        lut[0] = 0;
        for (var max = 1; max < 256; max++)
            lut[max] = (255 << 16) / max;
        return lut;
    }

    private static long[] CreateRecip24Lut()
    {
        // recip24[x] = round(2^24 / x) для точного деления
        // Используется: a / b = (a * recip24[b] + 2^23) >> 24
        // b=0 -> 0 (защита от деления на 0)
        var lut = new long[256];
        lut[0] = 0;
        for (var x = 1; x < 256; x++)
            lut[x] = (long)Math.Round((double)(1L << 24) / x);
        return lut;
    }

    #endregion

    #region Fixed-Point Division Constants

    // ═══════════════════════════════════════════════════════════════════════════
    // Константы для замены деления на умножение с fixed-point арифметикой
    // Используется техника: x / d ≈ (x * M) >> shift, где M = (1 << shift) / d
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reciprocal для деления на 1536: M = (1 &lt;&lt; 24) / 1536 = 10923 (точно).
    /// x / 1536 ≈ (x * 10923) >> 24 с погрешностью &lt; 0.5 для x &lt; 1536.
    /// Для точного округления: (x * 10923 + 8192) >> 24.
    /// </summary>
    private const int Reciprocal1536 = 10923; // (1 << 24) / 1536 = 10922.67, округляем вверх

    /// <summary>
    /// Reciprocal для деления на 255: M = (1 &lt;&lt; 16) / 255 = 257.
    /// x / 255 ≈ (x * 257 + 128) >> 16 для x &lt; 65536.
    /// Альтернатива: (x + 1 + ((x + 1) >> 8)) >> 8 — точнее для малых x.
    /// </summary>
    private const int Reciprocal255Const = 257;

    #endregion

    #region Single Pixel Conversion (Rgb24)

    /// <summary>
    /// Конвертирует Rgb24 в Hsv (4-байтовый lossless формат).
    /// <para>
    /// Алгоритм:
    /// <list type="bullet">
    ///   <item>V = max(R, G, B) — напрямую 8 бит</item>
    ///   <item>S8 = квантование от S16 = delta * 65535 / max</item>
    ///   <item>H16 = sector + 10923 * diff / delta</item>
    /// </list>
    /// </para>
    /// Round-trip error: max 0 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromRgb24(Rgb24 rgb)
    {
        int r = rgb.R;
        int g = rgb.G;
        int b = rgb.B;

        // Branchless min/max
        var max = r;
        var min = r;
        if (g > max) max = g;
        if (b > max) max = b;
        if (g < min) min = g;
        if (b < min) min = b;

        var delta = max - min;

        // V = max напрямую (8 бит)
        var v8 = (byte)max;

        if (max == 0)
            return new Hsv(0, 0, 0);

        if (delta == 0)
            return new Hsv(0, 0, v8);

        // S16 = (delta * 65535 + max/2) / max через LUT
        var recip = Recip24Lut[max];
        var s16 = (int)(((long)delta * 65535 * recip + (1L << 23)) >> 24);
        if (s16 > 65535) s16 = 65535;

        // S8 = S16 / 257 с округлением
        var s8 = (byte)(((s16 * 16711936L) + (1L << 31)) >> 32);

        // H16 через LUT деление
        var recipDelta = Recip24Lut[delta];
        int h16;
        if (max == r)
        {
            var diff = g - b;
            if (diff >= 0)
                h16 = (int)(((long)10923 * diff * recipDelta + (1L << 23)) >> 24);
            else
                h16 = 65536 + (int)(((long)10923 * diff * recipDelta - (1L << 23)) >> 24);
        }
        else if (max == g)
        {
            var diff = b - r;
            if (diff >= 0)
                h16 = 21845 + (int)(((long)10923 * diff * recipDelta + (1L << 23)) >> 24);
            else
                h16 = 21845 + (int)(((long)10923 * diff * recipDelta - (1L << 23)) >> 24);
        }
        else
        {
            var diff = r - g;
            if (diff >= 0)
                h16 = 43691 + (int)(((long)10923 * diff * recipDelta + (1L << 23)) >> 24);
            else
                h16 = 43691 + (int)(((long)10923 * diff * recipDelta - (1L << 23)) >> 24);
        }

        h16 &= 0xFFFF; // wrap around

        return new Hsv((ushort)h16, s8, v8);
    }

    /// <summary>
    /// Конвертирует Hsv в Rgb24.
    /// <para>
    /// 4-байтовый lossless формат: H16 (0-65535) + S8 (0-255) + V8 (0-255).
    /// Ключевое открытие: S16 восстанавливается через delta = S8 * V8 / 255.
    /// </para>
    /// Round-trip error: max 0 (lossless).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24()
    {
        var h16 = (int)H;
        var s8 = (int)S;
        var v8 = (int)V;

        // Grayscale: S=0 или V=0
        if (s8 == 0 || v8 == 0)
            return new Rgb24((byte)v8, (byte)v8, (byte)v8);

        // ═══════════════════════════════════════════════════════════════
        // КЛЮЧЕВОЕ ОТКРЫТИЕ: восстановление delta и S16 для lossless
        // ═══════════════════════════════════════════════════════════════

        // Шаг 1: Восстановить delta из S8 и V8
        // delta = S8 * V8 / 255 (через fixed-point: * 32897 >> 23)
        var delta = (s8 * v8 * 32897 + (1 << 22)) >> 23;

        // Шаг 2: Восстановить S16 из delta через LUT
        // S16 = delta * 65535 / V8
        var recipV = Recip24Lut[v8];
        var s16 = (int)(((long)delta * 65535 * recipV + (1L << 23)) >> 24);
        if (s16 > 65535) s16 = 65535;

        // V16 = V8 * 257 (расширение до 16 бит)
        var v16 = v8 * 257;

        // h6 = (h16 * 6) >> 16, sector 0..5
        var h6 = (h16 * 6) >> 16;
        if (h6 > 5) h6 = 5;

        // frac16 = (h16 * 6) - (h6 << 16), фракция 0..65535
        var frac16 = (h16 * 6) - (h6 << 16);

        // ═══════════════════════════════════════════════════════════════
        // p/q/t вычисления через 64-bit арифметику для точности
        // ═══════════════════════════════════════════════════════════════

        // p16 = v16 * (65535 - s16) / 65535
        var p16 = (int)(((long)v16 * (65535 - s16) * 65537L + (1L << 31)) >> 32);

        // sf = s16 * frac16 / 65535
        var sf = (int)(((long)s16 * frac16 * 65537L + (1L << 31)) >> 32);
        // q16 = v16 * (65535 - sf) / 65535
        var q16 = (int)(((long)v16 * (65535 - sf) * 65537L + (1L << 31)) >> 32);

        // sfInv = s16 * (65535 - frac16) / 65535
        var sfInv = (int)(((long)s16 * (65535 - frac16) * 65537L + (1L << 31)) >> 32);
        // t16 = v16 * (65535 - sfInv) / 65535
        var t16 = (int)(((long)v16 * (65535 - sfInv) * 65537L + (1L << 31)) >> 32);

        // 16-bit → 8-bit: X8 = (X16 * 16711936 + 2^31) >> 32
        var p8 = (int)(((long)p16 * 16711936L + (1L << 31)) >> 32);
        var q8 = (int)(((long)q16 * 16711936L + (1L << 31)) >> 32);
        var t8 = (int)(((long)t16 * 16711936L + (1L << 31)) >> 32);

        return h6 switch
        {
            0 => new Rgb24((byte)v8, (byte)t8, (byte)p8),
            1 => new Rgb24((byte)q8, (byte)v8, (byte)p8),
            2 => new Rgb24((byte)p8, (byte)v8, (byte)t8),
            3 => new Rgb24((byte)p8, (byte)q8, (byte)v8),
            4 => new Rgb24((byte)t8, (byte)p8, (byte)v8),
            _ => new Rgb24((byte)v8, (byte)p8, (byte)q8),
        };
    }

    #endregion

    #region SIMD Constants

    // ═══════════════════════════════════════════════════════════════════════════
    // Константы для симметричного алгоритма h6 = 0-1536
    // Все векторы вынесены в HsvSse41Vectors / HsvAvx2Vectors (Vectors.cs)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Реализованные ускорители для конвертации HSV ↔ RGB24.
    /// ВРЕМЕННО отключено: SIMD использует h6=1536, scalar — h6=1530.
    /// Ожидает переписывания SIMD на симметричный h6=1530 алгоритм для lossless.
    /// </summary>
    private const HardwareAcceleration HsvRgb24Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Batch Conversion (Hsv ↔ Rgb24)

    /// <summary>
    /// Пакетная конвертация Rgb24 → Hsv.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Hsv> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Hsv с явным указанием ускорителя.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvRgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            FromRgb24Parallel(source, destination, selected);
            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Hsv → Rgb24.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Hsv> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Hsv → Rgb24 с явным указанием ускорителя.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Hsv> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvRgb24Implemented, source.Length);

        // DEBUG: временно поднят порог для диагностики SIMD
        if (source.Length >= 100_000_000 && ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            ToRgb24Parallel(source, destination, selected);
            return;
        }

        ToRgb24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementation (Rgb24) — Scalar Only

    /// <summary>
    /// Scalar-only реализация Rgb24 → Hsv.
    /// <para>
    /// SIMD временно отключён: требует полной переработки для 4-байтового формата Hsv
    /// (ushort H + byte S + byte V). Текущий SIMD код использовал неправильный 3-байтовый
    /// формат (byte H + byte S + byte V) и некорректное H8 вместо H16.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        // Игнорируем selected — всегда scalar (SIMD отключён)
        _ = selected;

        fixed (Rgb24* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = FromRgb24(*src++);
        }
    }

    #endregion

    #region Core Implementation (Hsv → Rgb24) — Scalar Only

    /// <summary>
    /// Scalar-only реализация Hsv → Rgb24.
    /// <para>
    /// SIMD временно отключён: требует полной переработки для 4-байтового формата Hsv
    /// (ushort H + byte S + byte V). Текущий SIMD код использовал неправильный 3-байтовый
    /// формат (byte H + byte S + byte V) и некорректное H8 вместо H16.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Core(ReadOnlySpan<Hsv> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        // Игнорируем selected — всегда scalar (SIMD отключён)
        _ = selected;

        fixed (Hsv* srcPtr = source)
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

    #region Parallel (Rgb24)

    private static unsafe void FromRgb24Parallel(ReadOnlySpan<Rgb24> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Rgb24* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                FromRgb24Core(new ReadOnlySpan<Rgb24>(src + start, size), new Span<Hsv>(dst + start, size), selected);
            });
        }
    }

    private static unsafe void ToRgb24Parallel(ReadOnlySpan<Hsv> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Hsv* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                ToRgb24Core(new ReadOnlySpan<Hsv>(src + start, size), new Span<Rgb24>(dst + start, size), selected);
            });
        }
    }

    #endregion

    #region Conversion Operators (Rgb24)

    /// <summary>Явное преобразование Rgb24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Rgb24 rgb) => FromRgb24(rgb);

    /// <summary>Явное преобразование Hsv → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgb24(Hsv hsv) => hsv.ToRgb24();

    #endregion

    #region SIMD Helpers

    /// <summary>
    /// Деинтерлейс RGB24: 8 пикселей (24 байта) → отдельные R, G, B векторы (по 8 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void DeinterleaveRgb8(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        // Загружаем 24 байта: bytes0 = [0..15], bytes1 = [16..23]
        var bytes0 = Vector128.Load(src);                           // байты 0-15
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();   // байты 16-23

        // Shuffle для извлечения компонентов (результат в младших 8 байтах)
        r = Sse2.Or(Ssse3.Shuffle(bytes0, HsvSse41Vectors.ShuffleR0),
                    Ssse3.Shuffle(bytes1, HsvSse41Vectors.ShuffleR1));
        g = Sse2.Or(Ssse3.Shuffle(bytes0, HsvSse41Vectors.ShuffleG0),
                    Ssse3.Shuffle(bytes1, HsvSse41Vectors.ShuffleG1));
        b = Sse2.Or(Ssse3.Shuffle(bytes0, HsvSse41Vectors.ShuffleB0),
                    Ssse3.Shuffle(bytes1, HsvSse41Vectors.ShuffleB1));
    }

    /// <summary>
    /// Interleave R, G, B → R0G0B0 R1G1B1 ... (8 пикселей = 24 байта).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgb8(byte* dst, Vector128<byte> r, Vector128<byte> g, Vector128<byte> b)
    {
        // R0G0R1G1R2G2R3G3R4G4R5G5R6G6R7G7
        var rg = Sse2.UnpackLow(r, g);

        // Первые 16 байт: R0G0B0 R1G1B1 R2G2B2 R3G3B3 R4G4B4 R5_
        var out0 = Sse2.Or(
            Ssse3.Shuffle(rg, HsvSse41Vectors.HsvToHsvMask0),
            Ssse3.Shuffle(b, HsvSse41Vectors.VToHsvMask0));
        out0.Store(dst);

        // Оставшиеся 8 байт: _G5B5 R6G6B6 R7G7B7
        var out1 = Sse2.Or(
            Ssse3.Shuffle(rg, HsvSse41Vectors.HsvToHsvMask1),
            Ssse3.Shuffle(b, HsvSse41Vectors.VToHsvMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    /// <summary>
    /// Деинтерлейс HSV: 4 пикселя (12 байт) → H, S, V (по 4 байта в младших позициях).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveHsv4(byte* src, out Vector128<byte> h, out Vector128<byte> s, out Vector128<byte> v)
    {
        // Загружаем 12 байт: H0S0V0 H1S1V1 H2S2V2 H3S3V3
        var bytes = Vector128.Load(src);  // байты 0-15 (последние 4 игнорируем)

        // Кешируем shuffle маски для извлечения H, S, V
        var shuffleH = HsvSse41Vectors.Shuffle3ByteToChannel0;
        var shuffleS = HsvSse41Vectors.Shuffle3ByteToChannel1;
        var shuffleV = HsvSse41Vectors.Shuffle3ByteToChannel2;

        h = Ssse3.Shuffle(bytes, shuffleH);
        s = Ssse3.Shuffle(bytes, shuffleS);
        v = Ssse3.Shuffle(bytes, shuffleV);
    }

    /// <summary>
    /// Interleave R, G, B → R0G0B0 R1G1B1 R2G2B2 R3G3B3 (4 пикселя = 12 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgb4(byte* dst, Vector128<byte> r, Vector128<byte> g, Vector128<byte> b)
    {
        // r = [R0 R1 R2 R3 ...], g = [G0 G1 G2 G3 ...], b = [B0 B1 B2 B3 ...]
        // Нужно: R0G0B0 R1G1B1 R2G2B2 R3G3B3 (12 байт)

        // Кешируем interleave маску
        var shuffleRgb = HsvSse41Vectors.Shuffle3ByteInterleave;

        // Объединяем R, G, B в один вектор: R0R1R2R3 G0G1G2G3 B0B1B2B3
        var rgb = Sse2.Or(Sse2.Or(r, Sse2.ShiftLeftLogical128BitLane(g, 4)), Sse2.ShiftLeftLogical128BitLane(b, 8));
        var result = Ssse3.Shuffle(rgb, shuffleRgb);

        // Записываем 12 байт через overlapping SIMD stores
        Sse2.StoreLow((double*)dst, result.AsDouble());         // байты 0-7
        Sse2.StoreLow((double*)(dst + 4), Sse2.ShiftRightLogical128BitLane(result, 4).AsDouble()); // байты 4-11 (перекрытие)
    }

    /// <summary>
    /// Деинтерлейс RGB24: 4 пикселя (12 байт) → R, G, B (по 4 байта в младших позициях).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb4(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        // Загружаем 12 байт: R0G0B0 R1G1B1 R2G2B2 R3G3B3
        var bytes = Vector128.Load(src);  // байты 0-15 (последние 4 игнорируем)

        // Кешируем shuffle маски для извлечения R, G, B
        var shuffleR = HsvSse41Vectors.Shuffle3ByteToChannel0;
        var shuffleG = HsvSse41Vectors.Shuffle3ByteToChannel1;
        var shuffleB = HsvSse41Vectors.Shuffle3ByteToChannel2;

        r = Ssse3.Shuffle(bytes, shuffleR);
        g = Ssse3.Shuffle(bytes, shuffleG);
        b = Ssse3.Shuffle(bytes, shuffleB);
    }

    /// <summary>
    /// Interleave H, S, V → H0S0V0 H1S1V1 H2S2V2 H3S3V3 (4 пикселя = 12 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveHsv4(byte* dst, Vector128<byte> h, Vector128<byte> s, Vector128<byte> v)
    {
        // h = [H0 H1 H2 H3 ...], s = [S0 S1 S2 S3 ...], v = [V0 V1 V2 V3 ...]
        // Нужно: H0S0V0 H1S1V1 H2S2V2 H3S3V3 (12 байт)

        // Кешируем interleave маску
        var shuffleHsv = HsvSse41Vectors.Shuffle3ByteInterleave;

        // Объединяем H, S, V в один вектор: H0H1H2H3 S0S1S2S3 V0V1V2V3
        var hsv = Sse2.Or(Sse2.Or(h, Sse2.ShiftLeftLogical128BitLane(s, 4)), Sse2.ShiftLeftLogical128BitLane(v, 8));
        var result = Ssse3.Shuffle(hsv, shuffleHsv);

        // Записываем 12 байт через overlapping SIMD stores
        Sse2.StoreLow((double*)dst, result.AsDouble());         // байты 0-7
        Sse2.StoreLow((double*)(dst + 4), Sse2.ShiftRightLogical128BitLane(result, 4).AsDouble()); // байты 4-11 (перекрытие)
    }

    #endregion
}
