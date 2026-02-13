#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, MA0051, MA0084, S1117, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ YCbCr.
/// Прямая SIMD-оптимизированная реализация без промежуточных буферов.
/// </summary>
/// <remarks>
/// Формулы (int32 fixed-point Q16):
/// CMYK → RGB: R = (255-C)(255-K)/255, G = (255-M)(255-K)/255, B = (255-Y)(255-K)/255
/// RGB → YCbCr: Y = 0.299R + 0.587G + 0.114B (Q16 коэффициенты)
///              Cb = 128 - 0.169R - 0.331G + 0.5B
///              Cr = 128 + 0.5R - 0.419G - 0.081B
/// YCbCr → RGB: R = Y + 1.402(Cr-128) (Q16 коэффициенты)
///              G = Y - 0.344(Cb-128) - 0.714(Cr-128)
///              B = Y + 1.772(Cb-128)
/// </remarks>
public readonly partial struct Cmyk
{
    #region Fixed-Point Constants for YCbCr (Q16)

    // ITU-R BT.601 коэффициенты * 65536
    private const int CmykYR = 19595;    // 0.299 * 65536
    private const int CmykYG = 38470;    // 0.587 * 65536
    private const int CmykYB = 7471;     // 0.114 * 65536
    private const int CmykCbR = -11056;  // -0.168736 * 65536
    private const int CmykCbG = -21712;  // -0.331264 * 65536
    private const int CmykCbB = 32768;   // 0.5 * 65536
    private const int CmykCrR = 32768;   // 0.5 * 65536
    private const int CmykCrG = -27440;  // -0.418688 * 65536
    private const int CmykCrB = -5328;   // -0.081312 * 65536

    // Обратные коэффициенты для YCbCr → RGB * 65536
    private const int CmykR_Cr = 91881;  // 1.402 * 65536
    private const int CmykG_Cb = -22553; // -0.344136 * 65536
    private const int CmykG_Cr = -46802; // -0.714136 * 65536
    private const int CmykB_Cb = 116130; // 1.772 * 65536

    #endregion

    #region SIMD Constants (YCbCr)

    /// <summary>Реализованные ускорители для Cmyk ↔ YCbCr.</summary>
    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (YCbCr)

    /// <summary>Конвертирует YCbCr в Cmyk (int32 fixed-point Q16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromYCbCr(YCbCr ycbcr)
    {
        // YCbCr → RGB (int32 fixed-point)
        var y = (int)ycbcr.Y;
        var cb = (int)ycbcr.Cb - 128;
        var cr = (int)ycbcr.Cr - 128;

        // R = Y + 1.402 * Cr
        var r = y + ((CmykR_Cr * cr + Q16Half) >> 16);
        // G = Y - 0.344 * Cb - 0.714 * Cr
        var g = y + ((CmykG_Cb * cb + CmykG_Cr * cr + Q16Half) >> 16);
        // B = Y + 1.772 * Cb
        var b = y + ((CmykB_Cb * cb + Q16Half) >> 16);

        // Clamp RGB to 0-255
        r = Math.Clamp(r, 0, 255);
        g = Math.Clamp(g, 0, 255);
        b = Math.Clamp(b, 0, 255);

        // RGB → CMYK (int32 fixed-point Q16)
        var max = Math.Max(Math.Max(r, g), b);

        if (max == 0)
            return new Cmyk(0, 0, 0, 255);

        var k = 255 - max;
        var invMax = InverseTable[max];

        // C = (max - R) * 255 / max = ((max - R) * invMax * 255 + 32768) >> 16
        // Math.Min нужен для защиты от переполнения при округлении (256 → 255)
        var c = Math.Min((((max - r) * invMax * 255) + Q16Half) >> 16, 255);
        var m = Math.Min((((max - g) * invMax * 255) + Q16Half) >> 16, 255);
        var yc = Math.Min((((max - b) * invMax * 255) + Q16Half) >> 16, 255);

        return new Cmyk((byte)c, (byte)m, (byte)yc, (byte)k);
    }

    /// <summary>Конвертирует Cmyk в YCbCr (int32 fixed-point Q16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr()
    {
        // CMYK → RGB (int32 fixed-point)
        var invC = 255 - C;
        var invM = 255 - M;
        var invY = 255 - Y;
        var invK = 255 - K;

        // R = invC * invK / 255
        var rProd = invC * invK;
        var gProd = invM * invK;
        var bProd = invY * invK;

        // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
        var r = (rProd + 128 + ((rProd + 128) >> 8)) >> 8;
        var g = (gProd + 128 + ((gProd + 128) >> 8)) >> 8;
        var b = (bProd + 128 + ((bProd + 128) >> 8)) >> 8;

        // RGB → YCbCr (int32 fixed-point Q16)
        var yCalc = ((CmykYR * r) + (CmykYG * g) + (CmykYB * b) + Q16Half) >> 16;
        var cbCalc = (((CmykCbR * r) + (CmykCbG * g) + (CmykCbB * b) + Q16Half) >> 16) + 128;
        var crCalc = (((CmykCrR * r) + (CmykCrG * g) + (CmykCrB * b) + Q16Half) >> 16) + 128;

        return new YCbCr(
            (byte)Math.Clamp(yCalc, 0, 255),
            (byte)Math.Clamp(cbCalc, 0, 255),
            (byte)Math.Clamp(crCalc, 0, 255));
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ YCbCr)

    /// <summary>Пакетная конвертация YCbCr → Cmyk.</summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Cmyk с явным ускорителем.</summary>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromYCbCrCore(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → YCbCr.</summary>
    public static void ToYCbCr(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → YCbCr с явным ускорителем.</summary>
    public static unsafe void ToYCbCr(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
            {
                ToYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToYCbCrCore(source, destination, selected);
    }

    #endregion

    #region Core Implementations (YCbCr)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromYCbCrSse41(source, destination);
                break;
            default:
                FromYCbCrScalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrCore(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToYCbCrSse41(source, destination);
                break;
            default:
                ToYCbCrScalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing (YCbCr)

    private static unsafe void FromYCbCrParallel(YCbCr* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    private static unsafe void ToYCbCrParallel(Cmyk* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToYCbCrCore(new ReadOnlySpan<Cmyk>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementations (YCbCr)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromYCbCr(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToYCbCr();
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Константы для YCbCr → RGB (из кеша)
            var c128F = CmykSse41Vectors.C128F;
            var c255F = CmykSse41Vectors.C255F;
            var zeroF = CmykSse41Vectors.ZeroF;
            var oneF = CmykSse41Vectors.OneF;
            var inv255F = CmykSse41Vectors.Inv255F;
            var epsilonF = CmykSse41Vectors.EpsilonF;

            // YCbCr → RGB коэффициенты (из кеша)
            var coeff_cr_r = CmykSse41Vectors.YCbCrCrToR;
            var coeff_cb_g = CmykSse41Vectors.YCbCrCbToG;
            var coeff_cr_g = CmykSse41Vectors.YCbCrCrToG;
            var coeff_cb_b = CmykSse41Vectors.YCbCrCbToB;

            var shuffleY = CmykSse41Vectors.ShuffleRgb24R;  // позиции 0, 3, 6, 9
            var shuffleCb = CmykSse41Vectors.ShuffleRgb24G; // позиции 1, 4, 7, 10
            var shuffleCr = CmykSse41Vectors.ShuffleRgb24B; // позиции 2, 5, 8, 11
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            // 4 пикселя за итерацию (12 байт YCbCr → 16 байт CMYK)
            while (i + 4 <= count)
            {
                var ycbcr12 = Sse2.LoadVector128(src);

                var yBytes = Ssse3.Shuffle(ycbcr12, shuffleY);
                var cbBytes = Ssse3.Shuffle(ycbcr12, shuffleCb);
                var crBytes = Ssse3.Shuffle(ycbcr12, shuffleCr);

                var yF = Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(yBytes));
                var cbF = Sse.Subtract(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(cbBytes)), c128F);
                var crF = Sse.Subtract(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(crBytes)), c128F);

                // YCbCr → RGB
                var rF = Sse.Add(yF, Sse.Multiply(coeff_cr_r, crF));
                var gF = Sse.Add(Sse.Add(yF, Sse.Multiply(coeff_cb_g, cbF)), Sse.Multiply(coeff_cr_g, crF));
                var bF = Sse.Add(yF, Sse.Multiply(coeff_cb_b, cbF));

                // Clamp 0-255
                rF = Sse.Min(Sse.Max(rF, zeroF), c255F);
                gF = Sse.Min(Sse.Max(gF, zeroF), c255F);
                bF = Sse.Min(Sse.Max(bF, zeroF), c255F);

                // RGB → CMYK (нормализация)
                rF = Sse.Multiply(rF, inv255F);
                gF = Sse.Multiply(gF, inv255F);
                bF = Sse.Multiply(bF, inv255F);

                var maxRgb = Sse.Max(Sse.Max(rF, gF), bF);
                var kF = Sse.Subtract(oneF, maxRgb);

                // invMax = 1 / max (без Newton-Raphson: rcpps даёт ~12 бит точности, достаточно для 8-бит результата)
                var maxSafe = Sse.Max(maxRgb, epsilonF);
                var invMax = Sse.Reciprocal(maxSafe);

                var cF = Sse.Multiply(Sse.Subtract(maxRgb, rF), invMax);
                var mF = Sse.Multiply(Sse.Subtract(maxRgb, gF), invMax);
                var ycF = Sse.Multiply(Sse.Subtract(maxRgb, bF), invMax);

                // Масштабирование
                cF = Sse.Multiply(cF, c255F);
                mF = Sse.Multiply(mF, c255F);
                ycF = Sse.Multiply(ycF, c255F);
                kF = Sse.Multiply(kF, c255F);

                // Упаковка в байты
                var cBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(cF).AsByte(), packMask);
                var mBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(mF).AsByte(), packMask);
                var ycBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(ycF).AsByte(), packMask);
                var kBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(kF).AsByte(), packMask);

                // SIMD интерлив CMYK
                var cm = Sse2.UnpackLow(cBytes, mBytes);
                var yk = Sse2.UnpackLow(ycBytes, kBytes);
                var cmyk = Sse2.UnpackLow(cm.AsUInt16(), yk.AsUInt16()).AsByte();
                Sse2.Store(dst, cmyk);

                src += 12;
                dst += 16;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = FromYCbCr(source[i]);
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrSse41(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykSse41Vectors.OneF;
            var c255F = CmykSse41Vectors.C255F;
            var inv255F = CmykSse41Vectors.Inv255F;
            var c128F = CmykSse41Vectors.C128F;
            var zeroF = CmykSse41Vectors.ZeroF;

            // RGB → YCbCr коэффициенты (из кеша)
            var coeff_y_r = CmykSse41Vectors.RgbToYR;
            var coeff_y_g = CmykSse41Vectors.RgbToYG;
            var coeff_y_b = CmykSse41Vectors.RgbToYB;
            var coeff_cb_r = CmykSse41Vectors.RgbToCbR;
            var coeff_cb_g = CmykSse41Vectors.RgbToCbG;
            var coeff_cb_b = CmykSse41Vectors.RgbToCbB;
            var coeff_cr_r = CmykSse41Vectors.RgbToCrR;
            var coeff_cr_g = CmykSse41Vectors.RgbToCrG;
            var coeff_cr_b = CmykSse41Vectors.RgbToCrB;

            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;
            var packMask = CmykSse41Vectors.PackInt32ToByte;

            // 4 пикселя за итерацию (16 байт CMYK → 12 байт YCbCr)
            while (i + 4 <= count)
            {
                var cmyk16 = Sse2.LoadVector128(src);

                var cBytes = Ssse3.Shuffle(cmyk16, shuffleC);
                var mBytes = Ssse3.Shuffle(cmyk16, shuffleM);
                var yBytes = Ssse3.Shuffle(cmyk16, shuffleY);
                var kBytes = Ssse3.Shuffle(cmyk16, shuffleK);

                // Нормализация и CMYK → RGB
                var cF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(cBytes)), inv255F);
                var mF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(mBytes)), inv255F);
                var ycF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(yBytes)), inv255F);
                var kF = Sse.Multiply(Sse2.ConvertToVector128Single(Sse41.ConvertToVector128Int32(kBytes)), inv255F);

                var oneMinusK = Sse.Subtract(oneF, kF);
                var rF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, cF), oneMinusK), c255F);
                var gF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, mF), oneMinusK), c255F);
                var bF = Sse.Multiply(Sse.Multiply(Sse.Subtract(oneF, ycF), oneMinusK), c255F);

                // RGB → YCbCr
                var yValF = Sse.Add(Sse.Add(Sse.Multiply(coeff_y_r, rF), Sse.Multiply(coeff_y_g, gF)), Sse.Multiply(coeff_y_b, bF));
                var cbF = Sse.Add(c128F, Sse.Add(Sse.Add(Sse.Multiply(coeff_cb_r, rF), Sse.Multiply(coeff_cb_g, gF)), Sse.Multiply(coeff_cb_b, bF)));
                var crF = Sse.Add(c128F, Sse.Add(Sse.Add(Sse.Multiply(coeff_cr_r, rF), Sse.Multiply(coeff_cr_g, gF)), Sse.Multiply(coeff_cr_b, bF)));

                // Clamp 0-255
                yValF = Sse.Min(Sse.Max(yValF, zeroF), c255F);
                cbF = Sse.Min(Sse.Max(cbF, zeroF), c255F);
                crF = Sse.Min(Sse.Max(crF, zeroF), c255F);

                // Упаковка в байты
                var yOutBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(yValF).AsByte(), packMask);
                var cbOutBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(cbF).AsByte(), packMask);
                var crOutBytes = Ssse3.Shuffle(Sse2.ConvertToVector128Int32(crF).AsByte(), packMask);
                var zeros = Vector128<byte>.Zero;

                // SIMD интерлив YCbCr (3 байта на пиксель) → через RGBA shuffle
                var yCb = Sse2.UnpackLow(yOutBytes, cbOutBytes);
                var cr0 = Sse2.UnpackLow(crOutBytes, zeros);
                var yCbCr0 = Sse2.UnpackLow(yCb.AsUInt16(), cr0.AsUInt16()).AsByte();

                // Shuffle RGBA32 → RGB24: [Y0 Cb0 Cr0 0 Y1 Cb1 Cr1 0 ...] → [Y0 Cb0 Cr0 Y1 Cb1 Cr1 ...]
                var rgb24Shuffle = CmykSse41Vectors.Rgba32ToRgb24Shuffle;
                var yCbCr = Ssse3.Shuffle(yCbCr0, rgb24Shuffle);

                Unsafe.WriteUnaligned(dst, yCbCr.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 8, yCbCr.AsUInt32().GetElement(2));

                src += 16;
                dst += 12;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = source[i].ToYCbCr();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var c128F = CmykAvx2Vectors.C128F;
            var c255F = CmykAvx2Vectors.C255F;
            var zeroF = CmykAvx2Vectors.ZeroF;
            var oneF = CmykAvx2Vectors.OneF;
            var inv255F = CmykAvx2Vectors.Inv255F;
            var epsilonF = CmykAvx2Vectors.EpsilonF;

            // YCbCr → RGB коэффициенты (из кеша)
            var coeff_cr_r = CmykAvx2Vectors.YCbCrCrToR;
            var coeff_cb_g = CmykAvx2Vectors.YCbCrCbToG;
            var coeff_cr_g = CmykAvx2Vectors.YCbCrCrToG;
            var coeff_cb_b = CmykAvx2Vectors.YCbCrCbToB;

            var shuffleY = CmykSse41Vectors.ShuffleRgb24R;
            var shuffleCb = CmykSse41Vectors.ShuffleRgb24G;
            var shuffleCr = CmykSse41Vectors.ShuffleRgb24B;

            // 8 пикселей за итерацию (24 байт YCbCr → 32 байт CMYK)
            while (i + 8 <= count)
            {
                // Пиксели 0-3: байты 0-11, пиксели 4-7: байты 12-23
                var ycbcr0 = Sse2.LoadVector128(src);       // байты 0-15 (используем 0-11)
                var ycbcr1 = Sse2.LoadVector128(src + 12);  // байты 12-27 (используем 0-11)

                var yBytes0 = Ssse3.Shuffle(ycbcr0, shuffleY);
                var cbBytes0 = Ssse3.Shuffle(ycbcr0, shuffleCb);
                var crBytes0 = Ssse3.Shuffle(ycbcr0, shuffleCr);
                // Для пикселей 4-7: теперь ycbcr1 загружен с src+12, позиции те же
                var yBytes1 = Ssse3.Shuffle(ycbcr1, shuffleY);
                var cbBytes1 = Ssse3.Shuffle(ycbcr1, shuffleCb);
                var crBytes1 = Ssse3.Shuffle(ycbcr1, shuffleCr);

                var yI = Vector256.Create(Sse41.ConvertToVector128Int32(yBytes0), Sse41.ConvertToVector128Int32(yBytes1));
                var cbI = Vector256.Create(Sse41.ConvertToVector128Int32(cbBytes0), Sse41.ConvertToVector128Int32(cbBytes1));
                var crI = Vector256.Create(Sse41.ConvertToVector128Int32(crBytes0), Sse41.ConvertToVector128Int32(crBytes1));

                var yF = Avx.ConvertToVector256Single(yI);
                var cbF = Avx.Subtract(Avx.ConvertToVector256Single(cbI), c128F);
                var crF = Avx.Subtract(Avx.ConvertToVector256Single(crI), c128F);

                // YCbCr → RGB
                var rF = Avx.Add(yF, Avx.Multiply(coeff_cr_r, crF));
                var gF = Avx.Add(Avx.Add(yF, Avx.Multiply(coeff_cb_g, cbF)), Avx.Multiply(coeff_cr_g, crF));
                var bF = Avx.Add(yF, Avx.Multiply(coeff_cb_b, cbF));

                // Clamp 0-255
                rF = Avx.Min(Avx.Max(rF, zeroF), c255F);
                gF = Avx.Min(Avx.Max(gF, zeroF), c255F);
                bF = Avx.Min(Avx.Max(bF, zeroF), c255F);

                // RGB → CMYK
                rF = Avx.Multiply(rF, inv255F);
                gF = Avx.Multiply(gF, inv255F);
                bF = Avx.Multiply(bF, inv255F);

                var maxRgb = Avx.Max(Avx.Max(rF, gF), bF);
                var kF = Avx.Subtract(oneF, maxRgb);

                // invMax = 1 / max (без Newton-Raphson: rcpps даёт ~12 бит точности, достаточно для 8-бит результата)
                var maxSafe = Avx.Max(maxRgb, epsilonF);
                var invMax = Avx.Reciprocal(maxSafe);

                var cF = Avx.Multiply(Avx.Subtract(maxRgb, rF), invMax);
                var mF = Avx.Multiply(Avx.Subtract(maxRgb, gF), invMax);
                var ycF = Avx.Multiply(Avx.Subtract(maxRgb, bF), invMax);

                cF = Avx.Multiply(cF, c255F);
                mF = Avx.Multiply(mF, c255F);
                ycF = Avx.Multiply(ycF, c255F);
                kF = Avx.Multiply(kF, c255F);

                var cI2 = Avx.ConvertToVector256Int32(cF);
                var mI2 = Avx.ConvertToVector256Int32(mF);
                var ycI2 = Avx.ConvertToVector256Int32(ycF);
                var kI2 = Avx.ConvertToVector256Int32(kF);

                // Упаковка и интерлив CMYK (8 пикселей = 32 байта)
                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var cLoB = Ssse3.Shuffle(cI2.GetLower().AsByte(), packMask);
                var mLoB = Ssse3.Shuffle(mI2.GetLower().AsByte(), packMask);
                var ycLoB = Ssse3.Shuffle(ycI2.GetLower().AsByte(), packMask);
                var kLoB = Ssse3.Shuffle(kI2.GetLower().AsByte(), packMask);
                var cHiB = Ssse3.Shuffle(cI2.GetUpper().AsByte(), packMask);
                var mHiB = Ssse3.Shuffle(mI2.GetUpper().AsByte(), packMask);
                var ycHiB = Ssse3.Shuffle(ycI2.GetUpper().AsByte(), packMask);
                var kHiB = Ssse3.Shuffle(kI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив первые 4 пикселя
                var cmLo = Sse2.UnpackLow(cLoB, mLoB);
                var ykLo = Sse2.UnpackLow(ycLoB, kLoB);
                var cmykLo = Sse2.UnpackLow(cmLo.AsUInt16(), ykLo.AsUInt16()).AsByte();
                Sse2.Store(dst, cmykLo);

                // SIMD интерлив следующие 4 пикселя
                var cmHi = Sse2.UnpackLow(cHiB, mHiB);
                var ykHi = Sse2.UnpackLow(ycHiB, kHiB);
                var cmykHi = Sse2.UnpackLow(cmHi.AsUInt16(), ykHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, cmykHi);

                src += 24;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback
            if (i < count)
            {
                FromYCbCrSse41(source[i..], destination[i..]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrAvx2(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var oneF = CmykAvx2Vectors.OneF;
            var c255F = CmykAvx2Vectors.C255F;
            var inv255F = CmykAvx2Vectors.Inv255F;
            var c128F = CmykAvx2Vectors.C128F;
            var zeroF = CmykAvx2Vectors.ZeroF;

            // RGB → YCbCr коэффициенты (из кеша)
            var coeff_y_r = CmykAvx2Vectors.RgbToYR;
            var coeff_y_g = CmykAvx2Vectors.RgbToYG;
            var coeff_y_b = CmykAvx2Vectors.RgbToYB;
            var coeff_cb_r = CmykAvx2Vectors.RgbToCbR;
            var coeff_cb_g = CmykAvx2Vectors.RgbToCbG;
            var coeff_cb_b = CmykAvx2Vectors.RgbToCbB;
            var coeff_cr_r = CmykAvx2Vectors.RgbToCrR;
            var coeff_cr_g = CmykAvx2Vectors.RgbToCrG;
            var coeff_cr_b = CmykAvx2Vectors.RgbToCrB;

            // 8 пикселей за итерацию (32 байт CMYK → 24 байт YCbCr)
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
                var ycLoB = Ssse3.Shuffle(cmykLo, shuffleY);
                var kLoB = Ssse3.Shuffle(cmykLo, shuffleK);
                var cHiB = Ssse3.Shuffle(cmykHi, shuffleC);
                var mHiB = Ssse3.Shuffle(cmykHi, shuffleM);
                var ycHiB = Ssse3.Shuffle(cmykHi, shuffleY);
                var kHiB = Ssse3.Shuffle(cmykHi, shuffleK);

                var cI = Vector256.Create(Sse41.ConvertToVector128Int32(cLoB), Sse41.ConvertToVector128Int32(cHiB));
                var mI = Vector256.Create(Sse41.ConvertToVector128Int32(mLoB), Sse41.ConvertToVector128Int32(mHiB));
                var ycI = Vector256.Create(Sse41.ConvertToVector128Int32(ycLoB), Sse41.ConvertToVector128Int32(ycHiB));
                var kI = Vector256.Create(Sse41.ConvertToVector128Int32(kLoB), Sse41.ConvertToVector128Int32(kHiB));

                var cF = Avx.Multiply(Avx.ConvertToVector256Single(cI), inv255F);
                var mF = Avx.Multiply(Avx.ConvertToVector256Single(mI), inv255F);
                var ycF = Avx.Multiply(Avx.ConvertToVector256Single(ycI), inv255F);
                var kF = Avx.Multiply(Avx.ConvertToVector256Single(kI), inv255F);

                var oneMinusK = Avx.Subtract(oneF, kF);
                var rF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, cF), oneMinusK), c255F);
                var gF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, mF), oneMinusK), c255F);
                var bF = Avx.Multiply(Avx.Multiply(Avx.Subtract(oneF, ycF), oneMinusK), c255F);

                // RGB → YCbCr
                var yValF = Avx.Add(Avx.Add(Avx.Multiply(coeff_y_r, rF), Avx.Multiply(coeff_y_g, gF)), Avx.Multiply(coeff_y_b, bF));
                var cbF = Avx.Add(c128F, Avx.Add(Avx.Add(Avx.Multiply(coeff_cb_r, rF), Avx.Multiply(coeff_cb_g, gF)), Avx.Multiply(coeff_cb_b, bF)));
                var crF = Avx.Add(c128F, Avx.Add(Avx.Add(Avx.Multiply(coeff_cr_r, rF), Avx.Multiply(coeff_cr_g, gF)), Avx.Multiply(coeff_cr_b, bF)));

                // Clamp 0-255
                yValF = Avx.Min(Avx.Max(yValF, zeroF), c255F);
                cbF = Avx.Min(Avx.Max(cbF, zeroF), c255F);
                crF = Avx.Min(Avx.Max(crF, zeroF), c255F);

                var yI2 = Avx.ConvertToVector256Int32(yValF);
                var cbI2 = Avx.ConvertToVector256Int32(cbF);
                var crI2 = Avx.ConvertToVector256Int32(crF);

                var packMask = CmykSse41Vectors.PackInt32ToByte;
                var yLoOut = Ssse3.Shuffle(yI2.GetLower().AsByte(), packMask);
                var cbLoOut = Ssse3.Shuffle(cbI2.GetLower().AsByte(), packMask);
                var crLoOut = Ssse3.Shuffle(crI2.GetLower().AsByte(), packMask);
                var yHiOut = Ssse3.Shuffle(yI2.GetUpper().AsByte(), packMask);
                var cbHiOut = Ssse3.Shuffle(cbI2.GetUpper().AsByte(), packMask);
                var crHiOut = Ssse3.Shuffle(crI2.GetUpper().AsByte(), packMask);

                // SIMD интерлив YCbCr для первых 4 пикселей
                var zeros = Vector128<byte>.Zero;
                var yCbLo = Sse2.UnpackLow(yLoOut, cbLoOut);
                var cr0Lo = Sse2.UnpackLow(crLoOut, zeros);
                var ycbcrLo = Sse2.UnpackLow(yCbLo.AsUInt16(), cr0Lo.AsUInt16()).AsByte();

                var rgb24Shuffle = CmykSse41Vectors.Rgba32ToRgb24Shuffle;
                var rgb03 = Ssse3.Shuffle(ycbcrLo, rgb24Shuffle);
                Unsafe.WriteUnaligned(dst, rgb03.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 8, rgb03.AsUInt32().GetElement(2));

                // SIMD интерлив YCbCr для следующих 4 пикселей
                var yCbHi = Sse2.UnpackLow(yHiOut, cbHiOut);
                var cr0Hi = Sse2.UnpackLow(crHiOut, zeros);
                var ycbcrHi = Sse2.UnpackLow(yCbHi.AsUInt16(), cr0Hi.AsUInt16()).AsByte();
                var rgb47 = Ssse3.Shuffle(ycbcrHi, rgb24Shuffle);
                Unsafe.WriteUnaligned(dst + 12, rgb47.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(dst + 20, rgb47.AsUInt32().GetElement(2));

                src += 32;
                dst += 24;
                i += 8;
            }

            // SSE41 fallback
            if (i < count)
            {
                ToYCbCrSse41(source[i..], destination[i..]);
            }
        }
    }

    #endregion

    #region Conversion Operators (YCbCr)

    /// <summary>Явная конвертация YCbCr → Cmyk.</summary>
    public static explicit operator Cmyk(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явная конвертация Cmyk → YCbCr.</summary>
    public static explicit operator YCbCr(Cmyk cmyk) => cmyk.ToYCbCr();

    #endregion
}
