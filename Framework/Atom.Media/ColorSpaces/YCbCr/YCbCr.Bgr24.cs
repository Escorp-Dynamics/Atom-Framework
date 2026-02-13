#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Bgr24.
/// Прямая SIMD реализация без промежуточных буферов.
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromBgr24(Bgr24 bgr) => FromRgb24(new Rgb24(bgr.R, bgr.G, bgr.B));

    /// <summary>Конвертирует YCbCr в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24()
    {
        var rgb = Rgb24.FromYCbCr(this);
        return new Bgr24(rgb.B, rgb.G, rgb.R);
    }

    #endregion

    #region Batch Conversion (YCbCr → Bgr24)

    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    /// <summary>Пакетная конвертация YCbCr → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Bgr24 с явным указанием ускорителя.</summary>
    public static unsafe void ToBgr24(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
                ToBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToBgr24Core(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToBgr24Sse41(source, destination);
                break;
            default:
                ToBgr24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToBgr24Parallel(YCbCr* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgr24Core(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Bgr24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Bgr24 → YCbCr)

    /// <summary>Пакетная конвертация Bgr24 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → YCbCr с явным указанием ускорителя.</summary>
    public static unsafe void FromBgr24(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
                FromBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromBgr24Sse41(source, destination);
                break;
            default:
                FromBgr24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromBgr24Parallel(Bgr24* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgr24Core(new ReadOnlySpan<Bgr24>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation (Bgr24)

    // Q16 константы для YCbCr → RGB (BT.601)
    private const int SC1402 = 91881;   // 1.402 * 65536
    private const int SC0344 = 22554;   // 0.344136 * 65536
    private const int SC0714 = 46802;   // 0.714136 * 65536
    private const int SC1772 = 116130;  // 1.772 * 65536

    // Q16 константы для RGB → YCbCr (BT.601)
    private const int SCYR = 19595;     // 0.299 * 65536
    private const int SCYG = 38470;     // 0.587 * 65536
    private const int SCYB = 7471;      // 0.114 * 65536
    private const int SCCbR = -11056;   // -0.168736 * 65536
    private const int SCCbG = -21712;   // -0.331264 * 65536
    private const int SCCbB = 32768;    // 0.5 * 65536
    private const int SCCrR = 32768;    // 0.5 * 65536
    private const int SCCrG = -27440;   // -0.418688 * 65536
    private const int SCCrB = -5328;    // -0.081312 * 65536
    private const int SCHalf = 32768;   // 0.5 * 65536

    // Предвычисленные смещения для Cb/Cr (убираем -128 из runtime)
    // Cb_offset = 128 * SC1772 = 128 * 116130 = 14,864,640
    // Cr_offset = 128 * SC1402 = 128 * 91881 = 11,760,768
    // CbCr_offset_G = 128 * (SC0344 + SC0714) = 128 * 69356 = 8,877,568
    private const int SCBOffset = 14864640;  // 128 * SC1772
    private const int SCROffset = 11760768;  // 128 * SC1402
    private const int SCGOffset = 8877568;   // 128 * (SC0344 + SC0714)

    /// <summary>
    /// Clamp с использованием Math.Clamp (компилятор генерирует оптимальный код).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int x) => (byte)Math.Clamp(x, 0, 255);

    /// <summary>
    /// Оптимизированная скалярная конвертация YCbCr → Bgr24.
    /// Branchless clamp, предвычисленные смещения, loop unrolling 8x.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Scalar(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Константы для упрощения формул:
            // HalfMinusBOffset = SCHalf - SCBOffset = 32768 - 14864640 = -14831872
            // HalfPlusGOffset = SCHalf + SCGOffset = 32768 + 8877568 = 8910336
            // HalfMinusROffset = SCHalf - SCROffset = 32768 - 11760768 = -11728000
            const int HalfMinusBOffset = -14831872;
            const int HalfPlusGOffset = 8910336;
            const int HalfMinusROffset = -11728000;

            // 8 пикселей за итерацию с локальными переменными для Y/Cb/Cr
            while (count >= 8)
            {
                // Пиксель 0: кешируем Y в Q16 и Cb/Cr
                int y0 = src[0] << 16, cb0 = src[1], cr0 = src[2];
                dst[0] = ClampByte((y0 + (SC1772 * cb0) + HalfMinusBOffset) >> 16);
                dst[1] = ClampByte((y0 - (SC0344 * cb0) - (SC0714 * cr0) + HalfPlusGOffset) >> 16);
                dst[2] = ClampByte((y0 + (SC1402 * cr0) + HalfMinusROffset) >> 16);

                // Пиксель 1
                int y1 = src[3] << 16, cb1 = src[4], cr1 = src[5];
                dst[3] = ClampByte((y1 + (SC1772 * cb1) + HalfMinusBOffset) >> 16);
                dst[4] = ClampByte((y1 - (SC0344 * cb1) - (SC0714 * cr1) + HalfPlusGOffset) >> 16);
                dst[5] = ClampByte((y1 + (SC1402 * cr1) + HalfMinusROffset) >> 16);

                // Пиксель 2
                int y2 = src[6] << 16, cb2 = src[7], cr2 = src[8];
                dst[6] = ClampByte((y2 + (SC1772 * cb2) + HalfMinusBOffset) >> 16);
                dst[7] = ClampByte((y2 - (SC0344 * cb2) - (SC0714 * cr2) + HalfPlusGOffset) >> 16);
                dst[8] = ClampByte((y2 + (SC1402 * cr2) + HalfMinusROffset) >> 16);

                // Пиксель 3
                int y3 = src[9] << 16, cb3 = src[10], cr3 = src[11];
                dst[9] = ClampByte((y3 + (SC1772 * cb3) + HalfMinusBOffset) >> 16);
                dst[10] = ClampByte((y3 - (SC0344 * cb3) - (SC0714 * cr3) + HalfPlusGOffset) >> 16);
                dst[11] = ClampByte((y3 + (SC1402 * cr3) + HalfMinusROffset) >> 16);

                // Пиксель 4
                int y4 = src[12] << 16, cb4 = src[13], cr4 = src[14];
                dst[12] = ClampByte((y4 + (SC1772 * cb4) + HalfMinusBOffset) >> 16);
                dst[13] = ClampByte((y4 - (SC0344 * cb4) - (SC0714 * cr4) + HalfPlusGOffset) >> 16);
                dst[14] = ClampByte((y4 + (SC1402 * cr4) + HalfMinusROffset) >> 16);

                // Пиксель 5
                int y5 = src[15] << 16, cb5 = src[16], cr5 = src[17];
                dst[15] = ClampByte((y5 + (SC1772 * cb5) + HalfMinusBOffset) >> 16);
                dst[16] = ClampByte((y5 - (SC0344 * cb5) - (SC0714 * cr5) + HalfPlusGOffset) >> 16);
                dst[17] = ClampByte((y5 + (SC1402 * cr5) + HalfMinusROffset) >> 16);

                // Пиксель 6
                int y6 = src[18] << 16, cb6 = src[19], cr6 = src[20];
                dst[18] = ClampByte((y6 + (SC1772 * cb6) + HalfMinusBOffset) >> 16);
                dst[19] = ClampByte((y6 - (SC0344 * cb6) - (SC0714 * cr6) + HalfPlusGOffset) >> 16);
                dst[20] = ClampByte((y6 + (SC1402 * cr6) + HalfMinusROffset) >> 16);

                // Пиксель 7
                int y7 = src[21] << 16, cb7 = src[22], cr7 = src[23];
                dst[21] = ClampByte((y7 + (SC1772 * cb7) + HalfMinusBOffset) >> 16);
                dst[22] = ClampByte((y7 - (SC0344 * cb7) - (SC0714 * cr7) + HalfPlusGOffset) >> 16);
                dst[23] = ClampByte((y7 + (SC1402 * cr7) + HalfMinusROffset) >> 16);

                src += 24;
                dst += 24;
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                int y = src[0] << 16, cb = src[1], cr = src[2];
                dst[0] = ClampByte((y + (SC1772 * cb) + HalfMinusBOffset) >> 16);
                dst[1] = ClampByte((y - (SC0344 * cb) - (SC0714 * cr) + HalfPlusGOffset) >> 16);
                dst[2] = ClampByte((y + (SC1402 * cr) + HalfMinusROffset) >> 16);
                src += 3;
                dst += 3;
                count--;
            }
        }
    }

    /// <summary>
    /// Оптимизированная скалярная конвертация Bgr24 → YCbCr.
    /// Loop unrolling 8x, кеширование RGB значений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Scalar(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Предвычисленная константа: SCHalf + Offset128Q16 = 32768 + 8388608 = 8421376
            const int CbCrOffset = 8421376;

            // 8 пикселей за итерацию с кешированием RGB
            while (count >= 8)
            {
                // Пиксель 0: кешируем B,G,R
                int b0 = src[0], g0 = src[1], r0 = src[2];
                dst[0] = (byte)(((SCYR * r0) + (SCYG * g0) + (SCYB * b0) + SCHalf) >> 16);
                dst[1] = ClampByte(((SCCbR * r0) + (SCCbG * g0) + (SCCbB * b0) + CbCrOffset) >> 16);
                dst[2] = ClampByte(((SCCrR * r0) + (SCCrG * g0) + (SCCrB * b0) + CbCrOffset) >> 16);

                // Пиксель 1
                int b1 = src[3], g1 = src[4], r1 = src[5];
                dst[3] = (byte)(((SCYR * r1) + (SCYG * g1) + (SCYB * b1) + SCHalf) >> 16);
                dst[4] = ClampByte(((SCCbR * r1) + (SCCbG * g1) + (SCCbB * b1) + CbCrOffset) >> 16);
                dst[5] = ClampByte(((SCCrR * r1) + (SCCrG * g1) + (SCCrB * b1) + CbCrOffset) >> 16);

                // Пиксель 2
                int b2 = src[6], g2 = src[7], r2 = src[8];
                dst[6] = (byte)(((SCYR * r2) + (SCYG * g2) + (SCYB * b2) + SCHalf) >> 16);
                dst[7] = ClampByte(((SCCbR * r2) + (SCCbG * g2) + (SCCbB * b2) + CbCrOffset) >> 16);
                dst[8] = ClampByte(((SCCrR * r2) + (SCCrG * g2) + (SCCrB * b2) + CbCrOffset) >> 16);

                // Пиксель 3
                int b3 = src[9], g3 = src[10], r3 = src[11];
                dst[9] = (byte)(((SCYR * r3) + (SCYG * g3) + (SCYB * b3) + SCHalf) >> 16);
                dst[10] = ClampByte(((SCCbR * r3) + (SCCbG * g3) + (SCCbB * b3) + CbCrOffset) >> 16);
                dst[11] = ClampByte(((SCCrR * r3) + (SCCrG * g3) + (SCCrB * b3) + CbCrOffset) >> 16);

                // Пиксель 4
                int b4 = src[12], g4 = src[13], r4 = src[14];
                dst[12] = (byte)(((SCYR * r4) + (SCYG * g4) + (SCYB * b4) + SCHalf) >> 16);
                dst[13] = ClampByte(((SCCbR * r4) + (SCCbG * g4) + (SCCbB * b4) + CbCrOffset) >> 16);
                dst[14] = ClampByte(((SCCrR * r4) + (SCCrG * g4) + (SCCrB * b4) + CbCrOffset) >> 16);

                // Пиксель 5
                int b5 = src[15], g5 = src[16], r5 = src[17];
                dst[15] = (byte)(((SCYR * r5) + (SCYG * g5) + (SCYB * b5) + SCHalf) >> 16);
                dst[16] = ClampByte(((SCCbR * r5) + (SCCbG * g5) + (SCCbB * b5) + CbCrOffset) >> 16);
                dst[17] = ClampByte(((SCCrR * r5) + (SCCrG * g5) + (SCCrB * b5) + CbCrOffset) >> 16);

                // Пиксель 6
                int b6 = src[18], g6 = src[19], r6 = src[20];
                dst[18] = (byte)(((SCYR * r6) + (SCYG * g6) + (SCYB * b6) + SCHalf) >> 16);
                dst[19] = ClampByte(((SCCbR * r6) + (SCCbG * g6) + (SCCbB * b6) + CbCrOffset) >> 16);
                dst[20] = ClampByte(((SCCrR * r6) + (SCCrG * g6) + (SCCrB * b6) + CbCrOffset) >> 16);

                // Пиксель 7
                int b7 = src[21], g7 = src[22], r7 = src[23];
                dst[21] = (byte)(((SCYR * r7) + (SCYG * g7) + (SCYB * b7) + SCHalf) >> 16);
                dst[22] = ClampByte(((SCCbR * r7) + (SCCbG * g7) + (SCCbB * b7) + CbCrOffset) >> 16);
                dst[23] = ClampByte(((SCCrR * r7) + (SCCrG * g7) + (SCCrB * b7) + CbCrOffset) >> 16);

                src += 24;
                dst += 24;
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                int b = src[0], g = src[1], r = src[2];
                dst[0] = (byte)(((SCYR * r) + (SCYG * g) + (SCYB * b) + SCHalf) >> 16);
                dst[1] = ClampByte(((SCCbR * r) + (SCCbG * g) + (SCCbB * b) + CbCrOffset) >> 16);
                dst[2] = ClampByte(((SCCrR * r) + (SCCrG * g) + (SCCrB * b) + CbCrOffset) >> 16);
                src += 3;
                dst += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr → Bgr24)

    /// <summary>
    /// SSE41: YCbCr → Bgr24. Q16 int32 арифметика.
    /// 16 пикселей за итерацию (оптимизировано).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Sse41(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы (кешируем в регистрах)
            var c1402 = YCbCrSse41Vectors.C1402;
            var c0344 = YCbCrSse41Vectors.C0344;
            var c0714 = YCbCrSse41Vectors.C0714;
            var c1772 = YCbCrSse41Vectors.C1772;
            var c128 = YCbCrSse41Vectors.C128;
            var half = YCbCrSse41Vectors.Half;

            // 16 пикселей за итерацию (2 блока по 8)
            while (count >= 16)
            {
                // === Блок 0: пиксели 0-7 ===
                DeinterleaveYCbCr8(srcByte, out var yVec0, out var cbVec0, out var crVec0);

                // Пиксели 0-3
                var y0 = Sse41.ConvertToVector128Int32(yVec0);
                var cb0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec0), c128);
                var cr0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec0), c128);

                var r0 = Sse2.Add(y0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, cr0), half), 16));
                var g0 = Sse2.Subtract(y0, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cb0), Sse2.Add(Sse41.MultiplyLow(c0714, cr0), half)), 16));
                var b0 = Sse2.Add(y0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cb0), half), 16));

                // Пиксели 4-7
                var y1 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec0, 4));
                var cb1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec0, 4)), c128);
                var cr1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec0, 4)), c128);

                var r1 = Sse2.Add(y1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, cr1), half), 16));
                var g1 = Sse2.Subtract(y1, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cb1), Sse2.Add(Sse41.MultiplyLow(c0714, cr1), half)), 16));
                var b1 = Sse2.Add(y1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cb1), half), 16));

                // === Блок 1: пиксели 8-15 ===
                DeinterleaveYCbCr8(srcByte + 24, out var yVec1, out var cbVec1, out var crVec1);

                // Пиксели 8-11
                var y2 = Sse41.ConvertToVector128Int32(yVec1);
                var cb2 = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec1), c128);
                var cr2 = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec1), c128);

                var r2 = Sse2.Add(y2, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, cr2), half), 16));
                var g2 = Sse2.Subtract(y2, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cb2), Sse2.Add(Sse41.MultiplyLow(c0714, cr2), half)), 16));
                var b2 = Sse2.Add(y2, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cb2), half), 16));

                // Пиксели 12-15
                var y3 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec1, 4));
                var cb3 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec1, 4)), c128);
                var cr3 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec1, 4)), c128);

                var r3 = Sse2.Add(y3, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, cr3), half), 16));
                var g3 = Sse2.Subtract(y3, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cb3), Sse2.Add(Sse41.MultiplyLow(c0714, cr3), half)), 16));
                var b3 = Sse2.Add(y3, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cb3), half), 16));

                // Pack int32 → int16 → byte (оптимизировано: один проход)
                var rShort01 = Sse2.PackSignedSaturate(r0, r1);
                var rShort23 = Sse2.PackSignedSaturate(r2, r3);
                var rBytes = Sse2.PackUnsignedSaturate(rShort01, rShort23);

                var gShort01 = Sse2.PackSignedSaturate(g0, g1);
                var gShort23 = Sse2.PackSignedSaturate(g2, g3);
                var gBytes = Sse2.PackUnsignedSaturate(gShort01, gShort23);

                var bShort01 = Sse2.PackSignedSaturate(b0, b1);
                var bShort23 = Sse2.PackSignedSaturate(b2, b3);
                var bBytes = Sse2.PackUnsignedSaturate(bShort01, bShort23);

                // Интерливинг BGR (16 пикселей)
                InterleaveBgr16(dstByte, bBytes, gBytes, rBytes);

                srcByte += 48;
                dstByte += 48;
                count -= 16;
            }

            // 8 пикселей (fallback)
            while (count >= 8)
            {
                DeinterleaveYCbCr8(srcByte, out var yVec, out var cbVec, out var crVec);

                var yLo = Sse41.ConvertToVector128Int32(yVec);
                var cbLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec), c128);
                var crLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec), c128);

                var rLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crLo), half), 16));
                var gLo = Sse2.Subtract(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cbLo), Sse2.Add(Sse41.MultiplyLow(c0714, crLo), half)), 16));
                var bLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo), half), 16));

                var yHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec, 4));
                var cbHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec, 4)), c128);
                var crHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec, 4)), c128);

                var rHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi), half), 16));
                var gHi = Sse2.Subtract(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(
                    Sse41.MultiplyLow(c0344, cbHi), Sse2.Add(Sse41.MultiplyLow(c0714, crHi), half)), 16));
                var bHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi), half), 16));

                var rBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo, rHi), Sse2.PackSignedSaturate(rLo, rHi));
                var gBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo, gHi), Sse2.PackSignedSaturate(gLo, gHi));
                var bBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo, bHi), Sse2.PackSignedSaturate(bLo, bHi));

                InterleaveBgr8(dstByte, bBytes, gBytes, rBytes);

                srcByte += 24;
                dstByte += 24;
                count -= 8;
            }

            if (count > 0)
                ToBgr24Scalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Bgr24>(dstByte, count));
        }
    }

    #endregion

    #region SSE41 Implementation (Bgr24 → YCbCr)

    /// <summary>
    /// SSE41: Bgr24 → YCbCr. Q16 int32 арифметика.
    /// 16 пикселей за итерацию (оптимизировано).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Sse41(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы (кешируем в регистрах)
            var cYR = YCbCrSse41Vectors.CYR;
            var cYG = YCbCrSse41Vectors.CYG;
            var cYB = YCbCrSse41Vectors.CYB;
            var cCbR = YCbCrSse41Vectors.CCbR;
            var cCbG = YCbCrSse41Vectors.CCbG;
            var cCbB = YCbCrSse41Vectors.CCbB;
            var cCrR = YCbCrSse41Vectors.CCrR;
            var cCrG = YCbCrSse41Vectors.CCrG;
            var cCrB = YCbCrSse41Vectors.CCrB;
            var c128 = YCbCrSse41Vectors.C128;
            var half = YCbCrSse41Vectors.Half;

            // 16 пикселей за итерацию (2 блока по 8)
            while (count >= 16)
            {
                // === Блок 0: пиксели 0-7 ===
                DeinterleaveBgr8(srcByte, out var bVec0, out var gVec0, out var rVec0);

                // Пиксели 0-3
                var r0 = Sse41.ConvertToVector128Int32(rVec0);
                var g0 = Sse41.ConvertToVector128Int32(gVec0);
                var b0 = Sse41.ConvertToVector128Int32(bVec0);

                var y0 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, r0), Sse41.MultiplyLow(cYG, g0)), Sse41.MultiplyLow(cYB, b0)), half), 16);
                var cb0 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, r0), Sse41.MultiplyLow(cCbG, g0)), Sse41.MultiplyLow(cCbB, b0)), half), 16), c128);
                var cr0 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, r0), Sse41.MultiplyLow(cCrG, g0)), Sse41.MultiplyLow(cCrB, b0)), half), 16), c128);

                // Пиксели 4-7
                var r1 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec0, 4));
                var g1 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec0, 4));
                var b1 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec0, 4));

                var y1 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, r1), Sse41.MultiplyLow(cYG, g1)), Sse41.MultiplyLow(cYB, b1)), half), 16);
                var cb1 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, r1), Sse41.MultiplyLow(cCbG, g1)), Sse41.MultiplyLow(cCbB, b1)), half), 16), c128);
                var cr1 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, r1), Sse41.MultiplyLow(cCrG, g1)), Sse41.MultiplyLow(cCrB, b1)), half), 16), c128);

                // === Блок 1: пиксели 8-15 ===
                DeinterleaveBgr8(srcByte + 24, out var bVec1, out var gVec1, out var rVec1);

                // Пиксели 8-11
                var r2 = Sse41.ConvertToVector128Int32(rVec1);
                var g2 = Sse41.ConvertToVector128Int32(gVec1);
                var b2 = Sse41.ConvertToVector128Int32(bVec1);

                var y2 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, r2), Sse41.MultiplyLow(cYG, g2)), Sse41.MultiplyLow(cYB, b2)), half), 16);
                var cb2 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, r2), Sse41.MultiplyLow(cCbG, g2)), Sse41.MultiplyLow(cCbB, b2)), half), 16), c128);
                var cr2 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, r2), Sse41.MultiplyLow(cCrG, g2)), Sse41.MultiplyLow(cCrB, b2)), half), 16), c128);

                // Пиксели 12-15
                var r3 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec1, 4));
                var g3 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec1, 4));
                var b3 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec1, 4));

                var y3 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, r3), Sse41.MultiplyLow(cYG, g3)), Sse41.MultiplyLow(cYB, b3)), half), 16);
                var cb3 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, r3), Sse41.MultiplyLow(cCbG, g3)), Sse41.MultiplyLow(cCbB, b3)), half), 16), c128);
                var cr3 = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, r3), Sse41.MultiplyLow(cCrG, g3)), Sse41.MultiplyLow(cCrB, b3)), half), 16), c128);

                // Pack int32 → int16 → byte (оптимизировано: один проход)
                var yShort01 = Sse2.PackSignedSaturate(y0, y1);
                var yShort23 = Sse2.PackSignedSaturate(y2, y3);
                var yBytes = Sse2.PackUnsignedSaturate(yShort01, yShort23);

                var cbShort01 = Sse2.PackSignedSaturate(cb0, cb1);
                var cbShort23 = Sse2.PackSignedSaturate(cb2, cb3);
                var cbBytes = Sse2.PackUnsignedSaturate(cbShort01, cbShort23);

                var crShort01 = Sse2.PackSignedSaturate(cr0, cr1);
                var crShort23 = Sse2.PackSignedSaturate(cr2, cr3);
                var crBytes = Sse2.PackUnsignedSaturate(crShort01, crShort23);

                // Интерливинг YCbCr (16 пикселей)
                InterleaveYCbCr16(dstByte, yBytes, cbBytes, crBytes);

                srcByte += 48;
                dstByte += 48;
                count -= 16;
            }

            // 8 пикселей (fallback)
            while (count >= 8)
            {
                DeinterleaveBgr8(srcByte, out var bVec, out var gVec, out var rVec);

                var rLo = Sse41.ConvertToVector128Int32(rVec);
                var gLo = Sse41.ConvertToVector128Int32(gVec);
                var bLo = Sse41.ConvertToVector128Int32(bVec);

                var yLo = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, rLo), Sse41.MultiplyLow(cYG, gLo)), Sse41.MultiplyLow(cYB, bLo)), half), 16);
                var cbLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, rLo), Sse41.MultiplyLow(cCbG, gLo)), Sse41.MultiplyLow(cCbB, bLo)), half), 16), c128);
                var crLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, rLo), Sse41.MultiplyLow(cCrG, gLo)), Sse41.MultiplyLow(cCrB, bLo)), half), 16), c128);

                var rHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec, 4));
                var gHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec, 4));
                var bHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec, 4));

                var yHi = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cYR, rHi), Sse41.MultiplyLow(cYG, gHi)), Sse41.MultiplyLow(cYB, bHi)), half), 16);
                var cbHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCbR, rHi), Sse41.MultiplyLow(cCbG, gHi)), Sse41.MultiplyLow(cCbB, bHi)), half), 16), c128);
                var crHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cCrR, rHi), Sse41.MultiplyLow(cCrG, gHi)), Sse41.MultiplyLow(cCrB, bHi)), half), 16), c128);

                var yBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(yLo, yHi), Sse2.PackSignedSaturate(yLo, yHi));
                var cbBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(cbLo, cbHi), Sse2.PackSignedSaturate(cbLo, cbHi));
                var crBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(crLo, crHi), Sse2.PackSignedSaturate(crLo, crHi));

                InterleaveYCbCr8(dstByte, yBytes, cbBytes, crBytes);

                srcByte += 24;
                dstByte += 24;
                count -= 8;
            }

            if (count > 0)
                FromBgr24Scalar(new ReadOnlySpan<Bgr24>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr → Bgr24)

    /// <summary>
    /// AVX2: YCbCr → Bgr24. Q16 int32 арифметика.
    /// 32 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Avx2(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы
            var c1402 = YCbCrAvx2Vectors.C1402;
            var c0344 = YCbCrAvx2Vectors.C0344;
            var c0714 = YCbCrAvx2Vectors.C0714;
            var c1772 = YCbCrAvx2Vectors.C1772;
            var c128 = YCbCrAvx2Vectors.C128;
            var half = YCbCrAvx2Vectors.Half;

            // 32 пикселя за итерацию (4 блока по 8 пикселей)
            while (count >= 32)
            {
                Sse.Prefetch0(srcByte + 192);

                // Блок 0-1: первые 16 пикселей
                DeinterleaveYCbCr16(srcByte, out var yBytes01, out var cbBytes01, out var crBytes01);

                // Пиксели 0-7
                var y0 = Avx2.ConvertToVector256Int32(yBytes01);
                var cb0 = Avx2.Subtract(Avx2.ConvertToVector256Int32(cbBytes01), c128);
                var cr0 = Avx2.Subtract(Avx2.ConvertToVector256Int32(crBytes01), c128);

                var r0 = Avx2.Add(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr0), half), 16));
                var g0 = Avx2.Subtract(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb0), Avx2.MultiplyLow(c0714, cr0)), half), 16));
                var b0 = Avx2.Add(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb0), half), 16));

                // Пиксели 8-15
                var y1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(yBytes01, 8));
                var cb1 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(cbBytes01, 8)), c128);
                var cr1 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(crBytes01, 8)), c128);

                var r1 = Avx2.Add(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr1), half), 16));
                var g1 = Avx2.Subtract(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb1), Avx2.MultiplyLow(c0714, cr1)), half), 16));
                var b1 = Avx2.Add(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb1), half), 16));

                // Блок 2-3: следующие 16 пикселей
                DeinterleaveYCbCr16(srcByte + 48, out var yBytes23, out var cbBytes23, out var crBytes23);

                var y2 = Avx2.ConvertToVector256Int32(yBytes23);
                var cb2 = Avx2.Subtract(Avx2.ConvertToVector256Int32(cbBytes23), c128);
                var cr2 = Avx2.Subtract(Avx2.ConvertToVector256Int32(crBytes23), c128);

                var r2 = Avx2.Add(y2, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr2), half), 16));
                var g2 = Avx2.Subtract(y2, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb2), Avx2.MultiplyLow(c0714, cr2)), half), 16));
                var b2 = Avx2.Add(y2, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb2), half), 16));

                var y3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(yBytes23, 8));
                var cb3 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(cbBytes23, 8)), c128);
                var cr3 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(crBytes23, 8)), c128);

                var r3 = Avx2.Add(y3, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr3), half), 16));
                var g3 = Avx2.Subtract(y3, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb3), Avx2.MultiplyLow(c0714, cr3)), half), 16));
                var b3 = Avx2.Add(y3, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb3), half), 16));

                // Pack int32 → int16 → byte с permute для исправления порядка lanes
                var rShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(r0, r1).AsInt64(), 0b11_01_10_00).AsInt16();
                var rShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(r2, r3).AsInt64(), 0b11_01_10_00).AsInt16();
                var rBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(rShort01, rShort23).AsInt64(), 0b11_01_10_00).AsByte();

                var gShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(g0, g1).AsInt64(), 0b11_01_10_00).AsInt16();
                var gShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(g2, g3).AsInt64(), 0b11_01_10_00).AsInt16();
                var gBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(gShort01, gShort23).AsInt64(), 0b11_01_10_00).AsByte();

                var bShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(b0, b1).AsInt64(), 0b11_01_10_00).AsInt16();
                var bShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(b2, b3).AsInt64(), 0b11_01_10_00).AsInt16();
                var bBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(bShort01, bShort23).AsInt64(), 0b11_01_10_00).AsByte();

                // Interleave BGR и запись (32 пикселя = 96 байт)
                InterleaveBgr16(dstByte, bBytes.GetLower(), gBytes.GetLower(), rBytes.GetLower());
                InterleaveBgr16(dstByte + 48, bBytes.GetUpper(), gBytes.GetUpper(), rBytes.GetUpper());

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток через SSE41
            if (count >= 8)
                ToBgr24Sse41(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Bgr24>(dstByte, count));
            else if (count > 0)
                ToBgr24Scalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Bgr24>(dstByte, count));
        }
    }

    #endregion

    #region AVX2 Implementation (Bgr24 → YCbCr)

    /// <summary>
    /// AVX2: Bgr24 → YCbCr. Q16 int32 арифметика.
    /// 32 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Avx2(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы
            var cYR = YCbCrAvx2Vectors.CYR;
            var cYG = YCbCrAvx2Vectors.CYG;
            var cYB = YCbCrAvx2Vectors.CYB;
            var cCbR = YCbCrAvx2Vectors.CCbR;
            var cCbG = YCbCrAvx2Vectors.CCbG;
            var cCbB = YCbCrAvx2Vectors.CCbB;
            var cCrR = YCbCrAvx2Vectors.CCrR;
            var cCrG = YCbCrAvx2Vectors.CCrG;
            var cCrB = YCbCrAvx2Vectors.CCrB;
            var c128 = YCbCrAvx2Vectors.C128;
            var half = YCbCrAvx2Vectors.Half;

            // 32 пикселя за итерацию (4 блока по 8 пикселей)
            while (count >= 32)
            {
                Sse.Prefetch0(srcByte + 384);

                // Блок 0-1: первые 16 пикселей
                DeinterleaveBgr16(srcByte, out var bBytes01, out var gBytes01, out var rBytes01);

                // Пиксели 0-7
                var r0 = Avx2.ConvertToVector256Int32(rBytes01);
                var g0 = Avx2.ConvertToVector256Int32(gBytes01);
                var b0 = Avx2.ConvertToVector256Int32(bBytes01);

                var y0 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r0), Avx2.MultiplyLow(cYG, g0)), Avx2.MultiplyLow(cYB, b0)), half), 16);
                var cb0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r0), Avx2.MultiplyLow(cCbG, g0)), Avx2.MultiplyLow(cCbB, b0)), half), 16), c128);
                var cr0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r0), Avx2.MultiplyLow(cCrG, g0)), Avx2.MultiplyLow(cCrB, b0)), half), 16), c128);

                // Пиксели 8-15
                var r1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(rBytes01, 8));
                var g1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(gBytes01, 8));
                var b1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(bBytes01, 8));

                var y1 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r1), Avx2.MultiplyLow(cYG, g1)), Avx2.MultiplyLow(cYB, b1)), half), 16);
                var cb1 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r1), Avx2.MultiplyLow(cCbG, g1)), Avx2.MultiplyLow(cCbB, b1)), half), 16), c128);
                var cr1 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r1), Avx2.MultiplyLow(cCrG, g1)), Avx2.MultiplyLow(cCrB, b1)), half), 16), c128);

                // Блок 2-3: следующие 16 пикселей
                DeinterleaveBgr16(srcByte + 48, out var bBytes23, out var gBytes23, out var rBytes23);

                var r2 = Avx2.ConvertToVector256Int32(rBytes23);
                var g2 = Avx2.ConvertToVector256Int32(gBytes23);
                var b2 = Avx2.ConvertToVector256Int32(bBytes23);

                var y2 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r2), Avx2.MultiplyLow(cYG, g2)), Avx2.MultiplyLow(cYB, b2)), half), 16);
                var cb2 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r2), Avx2.MultiplyLow(cCbG, g2)), Avx2.MultiplyLow(cCbB, b2)), half), 16), c128);
                var cr2 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r2), Avx2.MultiplyLow(cCrG, g2)), Avx2.MultiplyLow(cCrB, b2)), half), 16), c128);

                var r3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(rBytes23, 8));
                var g3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(gBytes23, 8));
                var b3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(bBytes23, 8));

                var y3 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r3), Avx2.MultiplyLow(cYG, g3)), Avx2.MultiplyLow(cYB, b3)), half), 16);
                var cb3 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r3), Avx2.MultiplyLow(cCbG, g3)), Avx2.MultiplyLow(cCbB, b3)), half), 16), c128);
                var cr3 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r3), Avx2.MultiplyLow(cCrG, g3)), Avx2.MultiplyLow(cCrB, b3)), half), 16), c128);

                // Pack int32 → int16 → byte с permute для исправления порядка lanes
                var yShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(y0, y1).AsInt64(), 0b11_01_10_00).AsInt16();
                var yShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(y2, y3).AsInt64(), 0b11_01_10_00).AsInt16();
                var yBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(yShort01, yShort23).AsInt64(), 0b11_01_10_00).AsByte();

                var cbShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(cb0, cb1).AsInt64(), 0b11_01_10_00).AsInt16();
                var cbShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(cb2, cb3).AsInt64(), 0b11_01_10_00).AsInt16();
                var cbBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(cbShort01, cbShort23).AsInt64(), 0b11_01_10_00).AsByte();

                var crShort01 = Avx2.Permute4x64(Avx2.PackSignedSaturate(cr0, cr1).AsInt64(), 0b11_01_10_00).AsInt16();
                var crShort23 = Avx2.Permute4x64(Avx2.PackSignedSaturate(cr2, cr3).AsInt64(), 0b11_01_10_00).AsInt16();
                var crBytes = Avx2.Permute4x64(Avx2.PackUnsignedSaturate(crShort01, crShort23).AsInt64(), 0b11_01_10_00).AsByte();

                // Interleave YCbCr и запись (32 пикселя = 96 байт)
                InterleaveYCbCr16(dstByte, yBytes.GetLower(), cbBytes.GetLower(), crBytes.GetLower());
                InterleaveYCbCr16(dstByte + 48, yBytes.GetUpper(), cbBytes.GetUpper(), crBytes.GetUpper());

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток через SSE41
            if (count >= 8)
                FromBgr24Sse41(new ReadOnlySpan<Bgr24>(srcByte, count), new Span<YCbCr>(dstByte, count));
            else if (count > 0)
                FromBgr24Scalar(new ReadOnlySpan<Bgr24>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    #endregion

    #region BGR24 Deinterleave/Interleave Helpers

    /// <summary>Деинтерливинг BGR (8 пикселей, 24 байта) → B, G, R.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveBgr8(byte* src, out Vector128<byte> b, out Vector128<byte> g, out Vector128<byte> r)
    {
        var bytes0 = Vector128.Load(src);
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();

        // BGR24: B0G0R0 B1G1R1 B2G2R2 B3G3R3 B4G4R4 B5G5R5 B6G6R6 B7G7R7
        // Используем те же маски что и для RGB, но меняем B и R местами
        b = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleR0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleR1));
        g = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleG0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleG1));
        r = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleB0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleB1));
    }

    /// <summary>Деинтерливинг BGR (16 пикселей, 48 байт) → B, G, R (Vector128).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveBgr16(byte* src, out Vector128<byte> b, out Vector128<byte> g, out Vector128<byte> r)
    {
        DeinterleaveBgr8(src, out var b0, out var g0, out var r0);
        DeinterleaveBgr8(src + 24, out var b1, out var g1, out var r1);

        b = Sse2.UnpackLow(b0.AsInt64(), b1.AsInt64()).AsByte();
        g = Sse2.UnpackLow(g0.AsInt64(), g1.AsInt64()).AsByte();
        r = Sse2.UnpackLow(r0.AsInt64(), r1.AsInt64()).AsByte();
    }

    /// <summary>Интерливинг B, G, R → BGR (8 пикселей, 24 байта).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveBgr8(byte* dst, Vector128<byte> b, Vector128<byte> g, Vector128<byte> r)
    {
        // B0G0 B1G1 B2G2 B3G3 B4G4 B5G5 B6G6 B7G7
        var bg = Sse2.UnpackLow(b, g);

        // Первые 16 байт: B0G0R0 B1G1R1 B2G2R2 B3G3R3 B4G4R4 B5_
        var out0 = Sse2.Or(
            Ssse3.Shuffle(bg, YCbCrSse41Vectors.YCbToYCbCrShuffleMask0),
            Ssse3.Shuffle(r, YCbCrSse41Vectors.CrToYCbCrShuffleMask0));
        out0.Store(dst);

        // Оставшиеся 8 байт: _G5R5 B6G6R6 B7G7R7
        var out1 = Sse2.Or(
            Ssse3.Shuffle(bg, YCbCrSse41Vectors.YCbToYCbCrShuffleMask1),
            Ssse3.Shuffle(r, YCbCrSse41Vectors.CrToYCbCrShuffleMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    /// <summary>Интерливинг B, G, R → BGR (16 пикселей, 48 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveBgr16(byte* dst, Vector128<byte> b, Vector128<byte> g, Vector128<byte> r)
    {
        InterleaveBgr8(dst, b, g, r);
        InterleaveBgr8(dst + 24, Sse2.ShiftRightLogical128BitLane(b, 8),
                                 Sse2.ShiftRightLogical128BitLane(g, 8),
                                 Sse2.ShiftRightLogical128BitLane(r, 8));
    }

    /// <summary>Деинтерливинг YCbCr (8 пикселей, 24 байта) → Y, Cb, Cr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr8(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        var bytes0 = Vector128.Load(src);
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();

        y = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleR0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleR1));
        cb = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleG0),
                     Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleG1));
        cr = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleB0),
                     Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleB1));
    }

    /// <summary>Деинтерливинг YCbCr (16 пикселей, 48 байт) → Y, Cb, Cr (Vector128).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr16(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        DeinterleaveYCbCr8(src, out var y0, out var cb0, out var cr0);
        DeinterleaveYCbCr8(src + 24, out var y1, out var cb1, out var cr1);

        y = Sse2.UnpackLow(y0.AsInt64(), y1.AsInt64()).AsByte();
        cb = Sse2.UnpackLow(cb0.AsInt64(), cb1.AsInt64()).AsByte();
        cr = Sse2.UnpackLow(cr0.AsInt64(), cr1.AsInt64()).AsByte();
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgr24 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явное преобразование YCbCr → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(YCbCr ycbcr) => ycbcr.ToBgr24();

    #endregion
}
