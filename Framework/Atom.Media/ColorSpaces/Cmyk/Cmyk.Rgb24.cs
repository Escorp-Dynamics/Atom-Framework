#pragma warning disable CA1000, CA2208, IDE0004, IDE0028, IDE0048, IDE0300, IDE0301, MA0051, MA0084, S1117, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Rgb24.
/// Формула: R = 255 * (1 - C/255) * (1 - K/255)
///          G = 255 * (1 - M/255) * (1 - K/255)
///          B = 255 * (1 - Y/255) * (1 - K/255)
/// </summary>
/// <remarks>
/// Использует int32 fixed-point арифметику Q16 для максимальной производительности.
/// LUT InverseTable содержит 1/x * 65536 для x = 1..255.
/// </remarks>
public readonly partial struct Cmyk
{
    #region Fixed-Point Constants (Q16)

    /// <summary>Q16 масштаб: 65536 = 2^16.</summary>
    private const int Q16 = 65536;

    /// <summary>Q16 половина для округления: 32768 = 2^15.</summary>
    private const int Q16Half = 32768;

    /// <summary>
    /// LUT обратных значений: InverseTable[x] = round(65536 / x) для x = 1..255.
    /// Используется для замены деления на умножение.
    /// </summary>
    private static ReadOnlySpan<ushort> InverseTable =>
    [
        0,      // [0] - не используется (деление на 0)
        65535,  // [1] = 65536/1
        32768,  // [2] = 65536/2
        21845,  // [3] = 65536/3
        16384,  // [4] = 65536/4
        13107,  // [5] = 65536/5
        10923,  // [6] = 65536/6
        9362,   // [7] = 65536/7
        8192,   // [8] = 65536/8
        7282,   // [9] = 65536/9
        6554,   // [10] = 65536/10
        5958,   // [11]
        5461,   // [12]
        5041,   // [13]
        4681,   // [14]
        4369,   // [15]
        4096,   // [16]
        3855,   // [17]
        3641,   // [18]
        3449,   // [19]
        3277,   // [20]
        3121,   // [21]
        2979,   // [22]
        2849,   // [23]
        2731,   // [24]
        2621,   // [25]
        2521,   // [26]
        2427,   // [27]
        2341,   // [28]
        2260,   // [29]
        2185,   // [30]
        2114,   // [31]
        2048,   // [32]
        1986,   // [33]
        1928,   // [34]
        1872,   // [35]
        1820,   // [36]
        1771,   // [37]
        1725,   // [38]
        1680,   // [39]
        1638,   // [40]
        1598,   // [41]
        1560,   // [42]
        1524,   // [43]
        1489,   // [44]
        1456,   // [45]
        1425,   // [46]
        1394,   // [47]
        1365,   // [48]
        1337,   // [49]
        1311,   // [50]
        1285,   // [51]
        1260,   // [52]
        1237,   // [53]
        1214,   // [54]
        1192,   // [55]
        1170,   // [56]
        1150,   // [57]
        1130,   // [58]
        1111,   // [59]
        1092,   // [60]
        1074,   // [61]
        1057,   // [62]
        1040,   // [63]
        1024,   // [64]
        1008,   // [65]
        993,    // [66]
        978,    // [67]
        964,    // [68]
        950,    // [69]
        936,    // [70]
        923,    // [71]
        910,    // [72]
        898,    // [73]
        886,    // [74]
        874,    // [75]
        862,    // [76]
        851,    // [77]
        840,    // [78]
        830,    // [79]
        819,    // [80]
        809,    // [81]
        799,    // [82]
        790,    // [83]
        780,    // [84]
        771,    // [85]
        762,    // [86]
        753,    // [87]
        745,    // [88]
        736,    // [89]
        728,    // [90]
        720,    // [91]
        712,    // [92]
        705,    // [93]
        697,    // [94]
        690,    // [95]
        683,    // [96]
        676,    // [97]
        669,    // [98]
        662,    // [99]
        655,    // [100]
        649,    // [101]
        643,    // [102]
        636,    // [103]
        630,    // [104]
        624,    // [105]
        618,    // [106]
        612,    // [107]
        607,    // [108]
        601,    // [109]
        596,    // [110]
        590,    // [111]
        585,    // [112]
        580,    // [113]
        575,    // [114]
        570,    // [115]
        565,    // [116]
        560,    // [117]
        555,    // [118]
        551,    // [119]
        546,    // [120]
        542,    // [121]
        537,    // [122]
        533,    // [123]
        529,    // [124]
        524,    // [125]
        520,    // [126]
        516,    // [127]
        512,    // [128]
        508,    // [129]
        504,    // [130]
        500,    // [131]
        496,    // [132]
        493,    // [133]
        489,    // [134]
        485,    // [135]
        482,    // [136]
        478,    // [137]
        475,    // [138]
        471,    // [139]
        468,    // [140]
        465,    // [141]
        462,    // [142]
        458,    // [143]
        455,    // [144]
        452,    // [145]
        449,    // [146]
        446,    // [147]
        443,    // [148]
        440,    // [149]
        437,    // [150]
        434,    // [151]
        431,    // [152]
        428,    // [153]
        426,    // [154]
        423,    // [155]
        420,    // [156]
        417,    // [157]
        415,    // [158]
        412,    // [159]
        410,    // [160]
        407,    // [161]
        405,    // [162]
        402,    // [163]
        400,    // [164]
        397,    // [165]
        395,    // [166]
        392,    // [167]
        390,    // [168]
        388,    // [169]
        386,    // [170]
        383,    // [171]
        381,    // [172]
        379,    // [173]
        377,    // [174]
        374,    // [175]
        372,    // [176]
        370,    // [177]
        368,    // [178]
        366,    // [179]
        364,    // [180]
        362,    // [181]
        360,    // [182]
        358,    // [183]
        356,    // [184]
        354,    // [185]
        352,    // [186]
        350,    // [187]
        349,    // [188]
        347,    // [189]
        345,    // [190]
        343,    // [191]
        341,    // [192]
        340,    // [193]
        338,    // [194]
        336,    // [195]
        334,    // [196]
        333,    // [197]
        331,    // [198]
        329,    // [199]
        328,    // [200]
        326,    // [201]
        324,    // [202]
        323,    // [203]
        321,    // [204]
        320,    // [205]
        318,    // [206]
        317,    // [207]
        315,    // [208]
        314,    // [209]
        312,    // [210]
        311,    // [211]
        309,    // [212]
        308,    // [213]
        306,    // [214]
        305,    // [215]
        303,    // [216]
        302,    // [217]
        301,    // [218]
        299,    // [219]
        298,    // [220]
        297,    // [221]
        295,    // [222]
        294,    // [223]
        293,    // [224]
        291,    // [225]
        290,    // [226]
        289,    // [227]
        287,    // [228]
        286,    // [229]
        285,    // [230]
        284,    // [231]
        282,    // [232]
        281,    // [233]
        280,    // [234]
        279,    // [235]
        278,    // [236]
        277,    // [237]
        275,    // [238]
        274,    // [239]
        273,    // [240]
        272,    // [241]
        271,    // [242]
        270,    // [243]
        269,    // [244]
        267,    // [245]
        266,    // [246]
        265,    // [247]
        264,    // [248]
        263,    // [249]
        262,    // [250]
        261,    // [251]
        260,    // [252]
        259,    // [253]
        258,    // [254]
        257,    // [255] = 65536/255
    ];

    #endregion

    #region SIMD Constants (Rgb24)

    /// <summary>Реализованные ускорители для Cmyk ↔ Rgb24.</summary>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (Rgb24)

    /// <summary>Конвертирует Rgb24 в Cmyk (int32 fixed-point Q16) с LOSSLESS round-trip.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromRgb24(Rgb24 rgb)
    {
        // Находим max(R, G, B) = 255 - K (в масштабе 0-255)
        var max = Math.Max(Math.Max(rgb.R, rgb.G), rgb.B);

        // Если max = 0 (чёрный), то C = M = Y = 0, K = 255
        if (max == 0)
            return new Cmyk(0, 0, 0, 255);

        // K = 255 - max
        var k = 255 - max;

        // Получаем 1/max * 65536 из LUT для избежания деления
        var invMax = InverseTable[max];

        // C = (max - R) / max * 255
        // Используя Q16 с FLOOR (без +Q16Half) для LOSSLESS компенсации
        var c0 = Math.Min(((max - rgb.R) * invMax * 255) >> 16, 255);
        var m0 = Math.Min(((max - rgb.G) * invMax * 255) >> 16, 255);
        var y0 = Math.Min(((max - rgb.B) * invMax * 255) >> 16, 255);

        // LOSSLESS компенсация: проверяем round-trip и корректируем ±1
        // r2 = (255 - c0) * max / 255 с округлением
        var invC0 = 255 - c0;
        var invM0 = 255 - m0;
        var invY0 = 255 - y0;

        var rProd = invC0 * max;
        var gProd = invM0 * max;
        var bProd = invY0 * max;

        var r2 = (rProd + 128 + ((rProd + 128) >> 8)) >> 8;
        var g2 = (gProd + 128 + ((gProd + 128) >> 8)) >> 8;
        var b2 = (bProd + 128 + ((bProd + 128) >> 8)) >> 8;

        // Двойная коррекция: +1 если r2 > r, -1 если r2 < r
        var cCorr = ComputeCorrection(r2, rgb.R, c0);
        var mCorr = ComputeCorrection(g2, rgb.G, m0);
        var yCorr = ComputeCorrection(b2, rgb.B, y0);

        var c = c0 + cCorr;
        var m = m0 + mCorr;
        var y = y0 + yCorr;

        return new Cmyk((byte)c, (byte)m, (byte)y, (byte)k);
    }

    /// <summary>Вычисляет коррекцию для LOSSLESS round-trip.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeCorrection(int computed, int original, int value)
    {
        if (computed > original && value < 255)
            return 1;
        if (computed < original && value > 0)
            return -1;
        return 0;
    }

    /// <summary>Конвертирует Cmyk в Rgb24 (int32 fixed-point Q16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24()
    {
        // R = (255 - C) * (255 - K) / 255 с округлением
        // Формула: (x + 128 + ((x + 128) >> 8)) >> 8 даёт round(x/255)

        var invC = 255 - C;
        var invK = 255 - K;
        var invM = 255 - M;
        var invY = 255 - Y;

        var rProd = invC * invK;
        var gProd = invM * invK;
        var bProd = invY * invK;

        // Деление на 255 с правильным округлением: round(x/255)
        // (x + 128 + ((x + 128) >> 8)) >> 8
        var r = (rProd + 128 + ((rProd + 128) >> 8)) >> 8;
        var g = (gProd + 128 + ((gProd + 128) >> 8)) >> 8;
        var b = (bProd + 128 + ((bProd + 128) >> 8)) >> 8;

        return new Rgb24((byte)r, (byte)g, (byte)b);
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ Rgb24)

    /// <summary>Пакетная конвертация Rgb24 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgb24 → Cmyk с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgb24* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgb24(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Rgb24 с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ToRgb24(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Rgb24* dstPtr = destination)
            {
                ToRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToRgb24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations (Rgb24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination, HardwareAcceleration selected)
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
    private static void ToRgb24Core(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination, HardwareAcceleration selected)
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

    #region Parallel Processing (Rgb24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Parallel(Rgb24* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgb24Core(new ReadOnlySpan<Rgb24>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgb24Parallel(Cmyk* source, Rgb24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgb24Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Rgb24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementations (Rgb24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Scalar(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromRgb24(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgb24Scalar(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToRgb24();
        }
    }

    #endregion

    #region SSE41 Implementation (Rgb24)

    /// <summary>
    /// RGB→CMYK с полноценным SIMD.
    /// SSE не имеет Gather, поэтому используем scalar LUT + SIMD математику.
    /// Обрабатываем 4 пикселя за итерацию с разверткой LUT lookup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Sse41(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        fixed (ushort* lutPtr = InverseTable)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var c255 = CmykSse41Vectors.C255I;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var shuffleR = CmykSse41Vectors.ShuffleRgb24R;
            var shuffleG = CmykSse41Vectors.ShuffleRgb24G;
            var shuffleB = CmykSse41Vectors.ShuffleRgb24B;

            // 4 пикселя за итерацию (12 байт RGB24 → 16 байт CMYK)
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей RGB24 = 12 байт (читаем 16, используем 12)
                var rgb12 = Sse2.LoadVector128(src);

                // Деинтерлейс RGB → R, G, B (в младших 4 байтах каждого вектора)
                var rBytes = Ssse3.Shuffle(rgb12, shuffleR);
                var gBytes = Ssse3.Shuffle(rgb12, shuffleG);
                var bBytes = Ssse3.Shuffle(rgb12, shuffleB);

                // Конвертация в int32
                var rI = Sse41.ConvertToVector128Int32(rBytes);
                var gI = Sse41.ConvertToVector128Int32(gBytes);
                var bI = Sse41.ConvertToVector128Int32(bBytes);

                // max = Max(R, G, B)
                var maxRG = Sse41.Max(rI, gI);
                var maxRGB = Sse41.Max(maxRG, bI);

                // K = 255 - max
                var kI = Sse2.Subtract(c255, maxRGB);

                // Scalar LUT lookup для invMax (SSE не имеет Gather)
                var max0 = maxRGB.GetElement(0);
                var max1 = maxRGB.GetElement(1);
                var max2 = maxRGB.GetElement(2);
                var max3 = maxRGB.GetElement(3);

                // Обработка max=0 (чёрный): invMax = 0, C=M=Y=0
                var inv0 = max0 > 0 ? lutPtr[max0] : 0;
                var inv1 = max1 > 0 ? lutPtr[max1] : 0;
                var inv2 = max2 > 0 ? lutPtr[max2] : 0;
                var inv3 = max3 > 0 ? lutPtr[max3] : 0;

                var invMax = Vector128.Create(inv0, inv1, inv2, inv3);

                // C = (max - R) * invMax * 255 / 65536
                // = ((max - R) * invMax * 255 + 32768) >> 16
                var diffR = Sse2.Subtract(maxRGB, rI);
                var diffG = Sse2.Subtract(maxRGB, gI);
                var diffB = Sse2.Subtract(maxRGB, bI);

                // (diff * invMax) — max 254 * 65535 = 16 645 890, fits in int32
                var cProd = Sse41.MultiplyLow(diffR, invMax);
                var mProd = Sse41.MultiplyLow(diffG, invMax);
                var yProd = Sse41.MultiplyLow(diffB, invMax);

                // Формула: C = (diff * invMax * 255 + Q16Half) >> 16
                // Но diff * invMax * 255 overflow int32.
                // Используем 2-шаговое деление с FLOOR для LOSSLESS:
                // Шаг 1: temp = cProd >> 8 (Q8, floor)
                // Шаг 2: C = (temp * 255) >> 8 (финальное масштабирование, floor)
                var c255v = CmykSse41Vectors.C255I;

                // cProd >> 8 — Q8 с floor, max 65535
                var cProd8 = Sse2.ShiftRightArithmetic(cProd, 8);
                var mProd8 = Sse2.ShiftRightArithmetic(mProd, 8);
                var yProd8 = Sse2.ShiftRightArithmetic(yProd, 8);

                // (temp * 255) >> 8 — финальное масштабирование с floor
                var cScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(cProd8, c255v), 8);
                var mScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(mProd8, c255v), 8);
                var yScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(yProd8, c255v), 8);

                // Clamp to 255 (защита от округления)
                cScaled = Sse41.Min(cScaled, c255);
                mScaled = Sse41.Min(mScaled, c255);
                yScaled = Sse41.Min(yScaled, c255);

                // LOSSLESS компенсация: проверяем round-trip и корректируем ±1
                // r2 = (255 - C) * max / 255 с округлением
                var invC = Sse2.Subtract(c255, cScaled);
                var invM = Sse2.Subtract(c255, mScaled);
                var invY = Sse2.Subtract(c255, yScaled);

                var rProdCheck = Sse41.MultiplyLow(invC, maxRGB);
                var gProdCheck = Sse41.MultiplyLow(invM, maxRGB);
                var bProdCheck = Sse41.MultiplyLow(invY, maxRGB);

                // Деление на 255: (x + 128 + ((x + 128) >> 8)) >> 8
                var c128v = CmykSse41Vectors.C128I;
                var rProd128 = Sse2.Add(rProdCheck, c128v);
                var gProd128 = Sse2.Add(gProdCheck, c128v);
                var bProd128 = Sse2.Add(bProdCheck, c128v);

                var r2 = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128, Sse2.ShiftRightArithmetic(rProd128, 8)), 8);
                var g2 = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128, Sse2.ShiftRightArithmetic(gProd128, 8)), 8);
                var b2 = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128, Sse2.ShiftRightArithmetic(bProd128, 8)), 8);

                // Двойная коррекция: +1 если r2 > r, -1 если r2 < r
                var c1 = CmykSse41Vectors.C1I;
                var cZero = Vector128<int>.Zero;

                // +1 коррекция: r2 > r && c < 255
                var maskRGt = Sse2.CompareGreaterThan(r2, rI);
                var maskGGt = Sse2.CompareGreaterThan(g2, gI);
                var maskBGt = Sse2.CompareGreaterThan(b2, bI);
                var maskCLt255 = Sse2.CompareGreaterThan(c255, cScaled);
                var maskMLt255 = Sse2.CompareGreaterThan(c255, mScaled);
                var maskYLt255 = Sse2.CompareGreaterThan(c255, yScaled);
                var addC = Sse2.And(Sse2.And(maskRGt, maskCLt255), c1);
                var addM = Sse2.And(Sse2.And(maskGGt, maskMLt255), c1);
                var addY = Sse2.And(Sse2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция: r2 < r && c > 0
                var maskRLt = Sse2.CompareGreaterThan(rI, r2);
                var maskGLt = Sse2.CompareGreaterThan(gI, g2);
                var maskBLt = Sse2.CompareGreaterThan(bI, b2);
                var maskCGt0 = Sse2.CompareGreaterThan(cScaled, cZero);
                var maskMGt0 = Sse2.CompareGreaterThan(mScaled, cZero);
                var maskYGt0 = Sse2.CompareGreaterThan(yScaled, cZero);
                var subC = Sse2.And(Sse2.And(maskRLt, maskCGt0), c1);
                var subM = Sse2.And(Sse2.And(maskGLt, maskMGt0), c1);
                var subY = Sse2.And(Sse2.And(maskBLt, maskYGt0), c1);

                // Применяем первую коррекцию: c = c + add - sub
                cScaled = Sse2.Subtract(Sse2.Add(cScaled, addC), subC);
                mScaled = Sse2.Subtract(Sse2.Add(mScaled, addM), subM);
                yScaled = Sse2.Subtract(Sse2.Add(yScaled, addY), subY);

                // === ВТОРАЯ ИТЕРАЦИЯ КОРРЕКЦИИ ===
                // 2-step деление теряет точность vs Q16, иногда нужна двойная коррекция
                invC = Sse2.Subtract(c255, cScaled);
                invM = Sse2.Subtract(c255, mScaled);
                invY = Sse2.Subtract(c255, yScaled);

                rProdCheck = Sse41.MultiplyLow(invC, maxRGB);
                gProdCheck = Sse41.MultiplyLow(invM, maxRGB);
                bProdCheck = Sse41.MultiplyLow(invY, maxRGB);

                rProd128 = Sse2.Add(rProdCheck, c128v);
                gProd128 = Sse2.Add(gProdCheck, c128v);
                bProd128 = Sse2.Add(bProdCheck, c128v);

                r2 = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128, Sse2.ShiftRightArithmetic(rProd128, 8)), 8);
                g2 = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128, Sse2.ShiftRightArithmetic(gProd128, 8)), 8);
                b2 = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128, Sse2.ShiftRightArithmetic(bProd128, 8)), 8);

                // +1 коррекция: r2 > r && c < 255
                maskRGt = Sse2.CompareGreaterThan(r2, rI);
                maskGGt = Sse2.CompareGreaterThan(g2, gI);
                maskBGt = Sse2.CompareGreaterThan(b2, bI);
                maskCLt255 = Sse2.CompareGreaterThan(c255, cScaled);
                maskMLt255 = Sse2.CompareGreaterThan(c255, mScaled);
                maskYLt255 = Sse2.CompareGreaterThan(c255, yScaled);
                addC = Sse2.And(Sse2.And(maskRGt, maskCLt255), c1);
                addM = Sse2.And(Sse2.And(maskGGt, maskMLt255), c1);
                addY = Sse2.And(Sse2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция: r2 < r && c > 0
                maskRLt = Sse2.CompareGreaterThan(rI, r2);
                maskGLt = Sse2.CompareGreaterThan(gI, g2);
                maskBLt = Sse2.CompareGreaterThan(bI, b2);
                maskCGt0 = Sse2.CompareGreaterThan(cScaled, cZero);
                maskMGt0 = Sse2.CompareGreaterThan(mScaled, cZero);
                maskYGt0 = Sse2.CompareGreaterThan(yScaled, cZero);
                subC = Sse2.And(Sse2.And(maskRLt, maskCGt0), c1);
                subM = Sse2.And(Sse2.And(maskGLt, maskMGt0), c1);
                subY = Sse2.And(Sse2.And(maskBLt, maskYGt0), c1);

                // Применяем вторую коррекцию
                cScaled = Sse2.Subtract(Sse2.Add(cScaled, addC), subC);
                mScaled = Sse2.Subtract(Sse2.Add(mScaled, addM), subM);
                yScaled = Sse2.Subtract(Sse2.Add(yScaled, addY), subY);

                // Упаковка int32 → byte
                var cOut = Ssse3.Shuffle(cScaled.AsByte(), packMask);
                var mOut = Ssse3.Shuffle(mScaled.AsByte(), packMask);
                var yOut = Ssse3.Shuffle(yScaled.AsByte(), packMask);
                var kOut = Ssse3.Shuffle(kI.AsByte(), packMask);

                // SIMD интерлив CMYK: [C0 C1 ...] + [M0 M1 ...] → [C0 M0 C1 M1 ...]
                var cm = Sse2.UnpackLow(cOut, mOut);
                var yk = Sse2.UnpackLow(yOut, kOut);
                var cmyk = Sse2.UnpackLow(cm.AsUInt16(), yk.AsUInt16()).AsByte();
                Sse2.Store(dst, cmyk);

                src += 12;
                dst += 16;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = FromRgb24(source[i]);
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Sse41(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Q16 integer: R = (255 - C) * (255 - K) / 255
            // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
            var c255 = CmykSse41Vectors.C255I;
            var c128 = CmykSse41Vectors.C128I;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей CMYK = 16 байт
                var cmyk16 = Sse2.LoadVector128(src);

                // Деинтерлейс CMYK → C, M, Y, K (компактно в младших 4 байтах)
                var cBytes = Ssse3.Shuffle(cmyk16, CmykSse41Vectors.ShuffleCmykC);
                var mBytes = Ssse3.Shuffle(cmyk16, CmykSse41Vectors.ShuffleCmykM);
                var yBytes = Ssse3.Shuffle(cmyk16, CmykSse41Vectors.ShuffleCmykY);
                var kBytes = Ssse3.Shuffle(cmyk16, CmykSse41Vectors.ShuffleCmykK);

                // Конвертация в int32
                var cI = Sse41.ConvertToVector128Int32(cBytes);
                var mI = Sse41.ConvertToVector128Int32(mBytes);
                var yI = Sse41.ConvertToVector128Int32(yBytes);
                var kI = Sse41.ConvertToVector128Int32(kBytes);

                // invC = 255 - C, invK = 255 - K
                var invC = Sse2.Subtract(c255, cI);
                var invM = Sse2.Subtract(c255, mI);
                var invY = Sse2.Subtract(c255, yI);
                var invK = Sse2.Subtract(c255, kI);

                // rProd = invC * invK (max 255*255 = 65025, fits in int32)
                var rProd = Sse41.MultiplyLow(invC, invK);
                var gProd = Sse41.MultiplyLow(invM, invK);
                var bProd = Sse41.MultiplyLow(invY, invK);

                // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
                var rProd128 = Sse2.Add(rProd, c128);
                var gProd128 = Sse2.Add(gProd, c128);
                var bProd128 = Sse2.Add(bProd, c128);

                var rI = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128, Sse2.ShiftRightArithmetic(rProd128, 8)), 8);
                var gI = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128, Sse2.ShiftRightArithmetic(gProd128, 8)), 8);
                var bI = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128, Sse2.ShiftRightArithmetic(bProd128, 8)), 8);

                // Упаковка и интерлив RGB24
                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var rBytesOut = Ssse3.Shuffle(rI.AsByte(), packMask);
                var gBytesOut = Ssse3.Shuffle(gI.AsByte(), packMask);
                var bBytesOut = Ssse3.Shuffle(bI.AsByte(), packMask);
                var zeros = Vector128<byte>.Zero;

                // SIMD интерлив RGB → RGBA: [R0 R1 ...] + [G0 G1 ...] + [B0 B1 ...] → [R0 G0 B0 0 R1 G1 B1 0 ...]
                var rg = Sse2.UnpackLow(rBytesOut, gBytesOut);
                var b0 = Sse2.UnpackLow(bBytesOut, zeros);
                var rgba = Sse2.UnpackLow(rg.AsUInt16(), b0.AsUInt16()).AsByte();

                // RGBA32 → RGB24 shuffle
                var rgb = Ssse3.Shuffle(rgba, CmykSse41Vectors.Rgba32ToRgb24Shuffle);

                // Записываем 4 пикселя = 12 байт
                Unsafe.WriteUnaligned(dst, rgb.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 8, rgb.AsUInt32().GetElement(2));

                src += 16;
                dst += 12;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                var cmyk = new Cmyk(src[0], src[1], src[2], src[3]);
                var rgb = cmyk.ToRgb24();
                dst[0] = rgb.R;
                dst[1] = rgb.G;
                dst[2] = rgb.B;
                src += 4;
                dst += 3;
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Rgb24)

    /// <summary>
    /// RGB→CMYK с AVX2 Gather для LUT lookup.
    /// 8 пикселей за итерацию с полноценным SIMD.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx2(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        fixed (ushort* lutPtr = InverseTable)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Нужен int* для GatherVector256
            // InverseTable — ushort[], конвертируем в int на лету через widen
            // Альтернатива: создать int32 версию LUT

            var c255_256 = CmykAvx2Vectors.C255I;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var shuffleR = CmykSse41Vectors.ShuffleRgb24R;
            var shuffleG = CmykSse41Vectors.ShuffleRgb24G;
            var shuffleB = CmykSse41Vectors.ShuffleRgb24B;

            // 8 пикселей за итерацию (24 байт RGB24 → 32 байт CMYK)
            while (i + 8 <= count)
            {
                // Загрузка 8 пикселей RGB24 = 24 байта (через 2x SSE loads)
                var rgb0 = Sse2.LoadVector128(src);       // пиксели 0-3 + часть 4
                var rgb1 = Sse2.LoadVector128(src + 12);  // пиксели 4-7

                // Деинтерлейс RGB → R, G, B (по 4 пикселя)
                var rLoBytes = Ssse3.Shuffle(rgb0, shuffleR);
                var gLoBytes = Ssse3.Shuffle(rgb0, shuffleG);
                var bLoBytes = Ssse3.Shuffle(rgb0, shuffleB);
                var rHiBytes = Ssse3.Shuffle(rgb1, shuffleR);
                var gHiBytes = Ssse3.Shuffle(rgb1, shuffleG);
                var bHiBytes = Ssse3.Shuffle(rgb1, shuffleB);

                // Конвертация в int32 (AVX2 256-bit)
                var rLoI = Sse41.ConvertToVector128Int32(rLoBytes);
                var gLoI = Sse41.ConvertToVector128Int32(gLoBytes);
                var bLoI = Sse41.ConvertToVector128Int32(bLoBytes);
                var rHiI = Sse41.ConvertToVector128Int32(rHiBytes);
                var gHiI = Sse41.ConvertToVector128Int32(gHiBytes);
                var bHiI = Sse41.ConvertToVector128Int32(bHiBytes);

                var rI = Vector256.Create(rLoI, rHiI);
                var gI = Vector256.Create(gLoI, gHiI);
                var bI = Vector256.Create(bLoI, bHiI);

                // max = Max(R, G, B)
                var maxRG = Avx2.Max(rI, gI);
                var maxRGB = Avx2.Max(maxRG, bI);

                // K = 255 - max
                var kI = Avx2.Subtract(c255_256, maxRGB);

                // Scalar LUT lookup (AVX2 Gather требует int32 таблицу, у нас ushort)
                // Развёртка для 8 значений
                var max0 = maxRGB.GetElement(0);
                var max1 = maxRGB.GetElement(1);
                var max2 = maxRGB.GetElement(2);
                var max3 = maxRGB.GetElement(3);
                var max4 = maxRGB.GetElement(4);
                var max5 = maxRGB.GetElement(5);
                var max6 = maxRGB.GetElement(6);
                var max7 = maxRGB.GetElement(7);

                var inv0 = max0 > 0 ? lutPtr[max0] : 0;
                var inv1 = max1 > 0 ? lutPtr[max1] : 0;
                var inv2 = max2 > 0 ? lutPtr[max2] : 0;
                var inv3 = max3 > 0 ? lutPtr[max3] : 0;
                var inv4 = max4 > 0 ? lutPtr[max4] : 0;
                var inv5 = max5 > 0 ? lutPtr[max5] : 0;
                var inv6 = max6 > 0 ? lutPtr[max6] : 0;
                var inv7 = max7 > 0 ? lutPtr[max7] : 0;

                var invMax = Vector256.Create(inv0, inv1, inv2, inv3, inv4, inv5, inv6, inv7);

                // C = (max - R) * invMax / 65536 * 255
                var diffR = Avx2.Subtract(maxRGB, rI);
                var diffG = Avx2.Subtract(maxRGB, gI);
                var diffB = Avx2.Subtract(maxRGB, bI);

                // (diff * invMax) → Q16
                var cProd = Avx2.MultiplyLow(diffR, invMax);
                var mProd = Avx2.MultiplyLow(diffG, invMax);
                var yProd = Avx2.MultiplyLow(diffB, invMax);

                // 2-шаговое деление с FLOOR для LOSSLESS:
                // Шаг 1: temp = cProd >> 8 (Q8, floor)
                // Шаг 2: C = (temp * 255) >> 8 (floor)
                var c255v = CmykAvx2Vectors.C255I;

                var cProd8 = Avx2.ShiftRightArithmetic(cProd, 8);
                var mProd8 = Avx2.ShiftRightArithmetic(mProd, 8);
                var yProd8 = Avx2.ShiftRightArithmetic(yProd, 8);

                var cScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(cProd8, c255v), 8);
                var mScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(mProd8, c255v), 8);
                var yScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(yProd8, c255v), 8);

                // Clamp to 255
                cScaled = Avx2.Min(cScaled, c255_256);
                mScaled = Avx2.Min(mScaled, c255_256);
                yScaled = Avx2.Min(yScaled, c255_256);

                // LOSSLESS компенсация: проверяем round-trip и корректируем ±1
                var c128v = CmykAvx2Vectors.C128I;
                var invC = Avx2.Subtract(c255_256, cScaled);
                var invM = Avx2.Subtract(c255_256, mScaled);
                var invY = Avx2.Subtract(c255_256, yScaled);

                var rProdCheck = Avx2.MultiplyLow(invC, maxRGB);
                var gProdCheck = Avx2.MultiplyLow(invM, maxRGB);
                var bProdCheck = Avx2.MultiplyLow(invY, maxRGB);

                // Деление на 255: (x + 128 + ((x + 128) >> 8)) >> 8
                var rProd128c = Avx2.Add(rProdCheck, c128v);
                var gProd128c = Avx2.Add(gProdCheck, c128v);
                var bProd128c = Avx2.Add(bProdCheck, c128v);

                var r2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128c, Avx2.ShiftRightArithmetic(rProd128c, 8)), 8);
                var g2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128c, Avx2.ShiftRightArithmetic(gProd128c, 8)), 8);
                var b2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128c, Avx2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // Двойная коррекция: +1 если r2 > r, -1 если r2 < r
                var c1 = CmykAvx2Vectors.C1I;
                var cZero = Vector256<int>.Zero;

                // +1 коррекция: r2 > r && c < 255
                var maskRGt = Avx2.CompareGreaterThan(r2, rI);
                var maskGGt = Avx2.CompareGreaterThan(g2, gI);
                var maskBGt = Avx2.CompareGreaterThan(b2, bI);
                var maskCLt255 = Avx2.CompareGreaterThan(c255_256, cScaled);
                var maskMLt255 = Avx2.CompareGreaterThan(c255_256, mScaled);
                var maskYLt255 = Avx2.CompareGreaterThan(c255_256, yScaled);
                var addC = Avx2.And(Avx2.And(maskRGt, maskCLt255), c1);
                var addM = Avx2.And(Avx2.And(maskGGt, maskMLt255), c1);
                var addY = Avx2.And(Avx2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция: r2 < r && c > 0
                var maskRLt = Avx2.CompareGreaterThan(rI, r2);
                var maskGLt = Avx2.CompareGreaterThan(gI, g2);
                var maskBLt = Avx2.CompareGreaterThan(bI, b2);
                var maskCGt0 = Avx2.CompareGreaterThan(cScaled, cZero);
                var maskMGt0 = Avx2.CompareGreaterThan(mScaled, cZero);
                var maskYGt0 = Avx2.CompareGreaterThan(yScaled, cZero);
                var subC = Avx2.And(Avx2.And(maskRLt, maskCGt0), c1);
                var subM = Avx2.And(Avx2.And(maskGLt, maskMGt0), c1);
                var subY = Avx2.And(Avx2.And(maskBLt, maskYGt0), c1);

                // Применяем первую коррекцию: c = c + add - sub
                cScaled = Avx2.Subtract(Avx2.Add(cScaled, addC), subC);
                mScaled = Avx2.Subtract(Avx2.Add(mScaled, addM), subM);
                yScaled = Avx2.Subtract(Avx2.Add(yScaled, addY), subY);

                // === ВТОРАЯ ИТЕРАЦИЯ КОРРЕКЦИИ ===
                // 2-step деление теряет точность vs Q16, иногда нужна двойная коррекция
                invC = Avx2.Subtract(c255_256, cScaled);
                invM = Avx2.Subtract(c255_256, mScaled);
                invY = Avx2.Subtract(c255_256, yScaled);

                rProdCheck = Avx2.MultiplyLow(invC, maxRGB);
                gProdCheck = Avx2.MultiplyLow(invM, maxRGB);
                bProdCheck = Avx2.MultiplyLow(invY, maxRGB);

                rProd128c = Avx2.Add(rProdCheck, c128v);
                gProd128c = Avx2.Add(gProdCheck, c128v);
                bProd128c = Avx2.Add(bProdCheck, c128v);

                r2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128c, Avx2.ShiftRightArithmetic(rProd128c, 8)), 8);
                g2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128c, Avx2.ShiftRightArithmetic(gProd128c, 8)), 8);
                b2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128c, Avx2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // +1 коррекция: r2 > r && c < 255
                maskRGt = Avx2.CompareGreaterThan(r2, rI);
                maskGGt = Avx2.CompareGreaterThan(g2, gI);
                maskBGt = Avx2.CompareGreaterThan(b2, bI);
                maskCLt255 = Avx2.CompareGreaterThan(c255_256, cScaled);
                maskMLt255 = Avx2.CompareGreaterThan(c255_256, mScaled);
                maskYLt255 = Avx2.CompareGreaterThan(c255_256, yScaled);
                addC = Avx2.And(Avx2.And(maskRGt, maskCLt255), c1);
                addM = Avx2.And(Avx2.And(maskGGt, maskMLt255), c1);
                addY = Avx2.And(Avx2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция: r2 < r && c > 0
                maskRLt = Avx2.CompareGreaterThan(rI, r2);
                maskGLt = Avx2.CompareGreaterThan(gI, g2);
                maskBLt = Avx2.CompareGreaterThan(bI, b2);
                maskCGt0 = Avx2.CompareGreaterThan(cScaled, cZero);
                maskMGt0 = Avx2.CompareGreaterThan(mScaled, cZero);
                maskYGt0 = Avx2.CompareGreaterThan(yScaled, cZero);
                subC = Avx2.And(Avx2.And(maskRLt, maskCGt0), c1);
                subM = Avx2.And(Avx2.And(maskGLt, maskMGt0), c1);
                subY = Avx2.And(Avx2.And(maskBLt, maskYGt0), c1);

                // Применяем вторую коррекцию
                cScaled = Avx2.Subtract(Avx2.Add(cScaled, addC), subC);
                mScaled = Avx2.Subtract(Avx2.Add(mScaled, addM), subM);
                yScaled = Avx2.Subtract(Avx2.Add(yScaled, addY), subY);

                // Упаковка int32 → byte (через SSE)
                var cLoOut = Ssse3.Shuffle(cScaled.GetLower().AsByte(), packMask);
                var mLoOut = Ssse3.Shuffle(mScaled.GetLower().AsByte(), packMask);
                var yLoOut = Ssse3.Shuffle(yScaled.GetLower().AsByte(), packMask);
                var kLoOut = Ssse3.Shuffle(kI.GetLower().AsByte(), packMask);
                var cHiOut = Ssse3.Shuffle(cScaled.GetUpper().AsByte(), packMask);
                var mHiOut = Ssse3.Shuffle(mScaled.GetUpper().AsByte(), packMask);
                var yHiOut = Ssse3.Shuffle(yScaled.GetUpper().AsByte(), packMask);
                var kHiOut = Ssse3.Shuffle(kI.GetUpper().AsByte(), packMask);

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

                src += 24;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback для 4+ пикселей
            if (i + 4 <= count)
            {
                FromRgb24Sse41(source[i..], destination[i..]);
                return;
            }

            // Scalar остаток
            while (i < count)
            {
                destination[i] = FromRgb24(source[i]);
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx2(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Q16 integer: R = (255 - C) * (255 - K) / 255
            var c255 = CmykAvx2Vectors.C255I;
            var c128 = CmykAvx2Vectors.C128I;

            // 8 пикселей за итерацию
            while (i + 8 <= count)
            {
                // Загрузка 8 пикселей CMYK = 32 байта (2x SSE loads)
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

                // Конвертация в int32 (AVX2)
                var cI = Vector256.Create(Sse41.ConvertToVector128Int32(cLoB), Sse41.ConvertToVector128Int32(cHiB));
                var mI = Vector256.Create(Sse41.ConvertToVector128Int32(mLoB), Sse41.ConvertToVector128Int32(mHiB));
                var yI = Vector256.Create(Sse41.ConvertToVector128Int32(yLoB), Sse41.ConvertToVector128Int32(yHiB));
                var kI = Vector256.Create(Sse41.ConvertToVector128Int32(kLoB), Sse41.ConvertToVector128Int32(kHiB));

                // invC = 255 - C, invK = 255 - K
                var invC = Avx2.Subtract(c255, cI);
                var invM = Avx2.Subtract(c255, mI);
                var invY = Avx2.Subtract(c255, yI);
                var invK = Avx2.Subtract(c255, kI);

                // rProd = invC * invK (max 255*255 = 65025)
                var rProd = Avx2.MultiplyLow(invC, invK);
                var gProd = Avx2.MultiplyLow(invM, invK);
                var bProd = Avx2.MultiplyLow(invY, invK);

                // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
                var rProd128 = Avx2.Add(rProd, c128);
                var gProd128 = Avx2.Add(gProd, c128);
                var bProd128 = Avx2.Add(bProd, c128);

                var rI2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128, Avx2.ShiftRightArithmetic(rProd128, 8)), 8);
                var gI2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128, Avx2.ShiftRightArithmetic(gProd128, 8)), 8);
                var bI2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128, Avx2.ShiftRightArithmetic(bProd128, 8)), 8);

                // Упаковка и интерлив RGB24
                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var rLoOut = Ssse3.Shuffle(rI2.GetLower().AsByte(), packMask);
                var gLoOut = Ssse3.Shuffle(gI2.GetLower().AsByte(), packMask);
                var bLoOut = Ssse3.Shuffle(bI2.GetLower().AsByte(), packMask);
                var rHiOut = Ssse3.Shuffle(rI2.GetUpper().AsByte(), packMask);
                var gHiOut = Ssse3.Shuffle(gI2.GetUpper().AsByte(), packMask);
                var bHiOut = Ssse3.Shuffle(bI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив RGB24 для первых 4 пикселей
                var zeros = Vector128<byte>.Zero;
                var rgLo = Sse2.UnpackLow(rLoOut, gLoOut);
                var b0Lo = Sse2.UnpackLow(bLoOut, zeros);
                var rgbLo = Sse2.UnpackLow(rgLo.AsUInt16(), b0Lo.AsUInt16()).AsByte();

                var rgb24Shuffle = CmykSse41Vectors.Rgba32ToRgb24Shuffle;
                var rgbLoPackedPre = Ssse3.Shuffle(rgbLo, rgb24Shuffle);
                Unsafe.WriteUnaligned(dst, rgbLoPackedPre.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 8, rgbLoPackedPre.AsUInt32().GetElement(2));

                // SIMD интерлив RGB24 для следующих 4 пикселей
                var rgHi = Sse2.UnpackLow(rHiOut, gHiOut);
                var b0Hi = Sse2.UnpackLow(bHiOut, zeros);
                var rgbHi = Sse2.UnpackLow(rgHi.AsUInt16(), b0Hi.AsUInt16()).AsByte();
                var rgbHiPackedPre = Ssse3.Shuffle(rgbHi, rgb24Shuffle);
                Unsafe.WriteUnaligned(dst + 12, rgbHiPackedPre.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 20, rgbHiPackedPre.AsUInt32().GetElement(2));

                src += 32;
                dst += 24;
                i += 8;
            }

            // Остаток через SSE41
            if (i + 4 <= count)
            {
                ToRgb24Sse41(source[i..], destination[i..]);
                return;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = source[i].ToRgb24();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators (Rgb24)

    /// <summary>Явная конвертация Rgb24 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(Rgb24 rgb) => FromRgb24(rgb);

    /// <summary>Явная конвертация Cmyk → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgb24(Cmyk cmyk) => cmyk.ToRgb24();

    #endregion
}
