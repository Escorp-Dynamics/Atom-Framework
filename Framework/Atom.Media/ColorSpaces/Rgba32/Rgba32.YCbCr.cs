#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Прямая SIMD конвертация Rgba32 ↔ YCbCr (без промежуточного буфера).
/// </summary>
public readonly partial struct Rgba32
{
    #region ITU-R BT.601 Constants (Q16)

    // RGB → YCbCr
    private const int CYR = 19595;    // 0.299 * 65536
    private const int CYG = 38470;    // 0.587 * 65536
    private const int CYB = 7471;     // 0.114 * 65536
    private const int CCbR = -11056;  // -0.168736 * 65536
    private const int CCbG = -21712;  // -0.331264 * 65536
    private const int CCbB = 32768;   // 0.5 * 65536
    private const int CCrR = 32768;   // 0.5 * 65536
    private const int CCrG = -27440;  // -0.418688 * 65536
    private const int CCrB = -5328;   // -0.081312 * 65536

    // YCbCr → RGB
    private const int C1402 = 91881;   // 1.402 * 65536
    private const int C0344 = 22554;   // 0.344136 * 65536
    private const int C0714 = 46802;   // 0.714136 * 65536
    private const int C1772 = 116130;  // 1.772 * 65536
    private const int HalfQ16 = 32768;    // 0.5 * 65536

    // Предвычисленные смещения для YCbCr → RGB (убираем -128 из runtime)
    // Cb_offset = 128 * C1772 = 128 * 116130 = 14,864,640
    // Cr_offset = 128 * C1402 = 128 * 91881 = 11,760,768
    // CbCr_offset_G = 128 * (C0344 + C0714) = 128 * 69356 = 8,877,568
    private const int CBOffset = 14864640;  // 128 * C1772
    private const int CROffset = 11760768;  // 128 * C1402
    private const int CGOffset = 8877568;   // 128 * (C0344 + C0714)

    /// <summary>
    /// Branchless clamp с использованием Math.Clamp (компилятор генерирует оптимальный код).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int x) => (byte)Math.Clamp(x, 0, 255);

    #endregion

    #region Single Pixel Conversion (YCbCr)

    /// <summary>Конвертирует YCbCr в Rgba32 (A = 255). Прямая формула BT.601.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromYCbCr(YCbCr ycbcr)
    {
        var y = (int)ycbcr.Y;
        var cb = ycbcr.Cb - 128;
        var cr = ycbcr.Cr - 128;

        var rCalc = y + (((C1402 * cr) + HalfQ16) >> 16);
        var gCalc = y - (((C0344 * cb) + (C0714 * cr) + HalfQ16) >> 16);
        var bCalc = y + (((C1772 * cb) + HalfQ16) >> 16);

        return new(ClampByte(rCalc), ClampByte(gCalc), ClampByte(bCalc), 255);
    }

    /// <summary>Конвертирует Rgba32 в YCbCr (игнорирует альфа-канал). Прямая формула BT.601.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr()
    {
        var rVal = (int)R;
        var gVal = (int)G;
        var bVal = (int)B;

        var yCalc = ((CYR * rVal) + (CYG * gVal) + (CYB * bVal) + HalfQ16) >> 16;
        var cbCalc = (((CCbR * rVal) + (CCbG * gVal) + (CCbB * bVal) + HalfQ16) >> 16) + 128;
        var crCalc = (((CCrR * rVal) + (CCrG * gVal) + (CCrB * bVal) + HalfQ16) >> 16) + 128;

        return new(ClampByte(yCalc), ClampByte(cbCalc), ClampByte(crCalc));
    }

    #endregion

    #region Batch Conversion (Rgba32 → YCbCr)

    /// <summary>
    /// Реализованные ускорители для конвертации Rgba32 ↔ YCbCr.
    /// </summary>
    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    /// <summary>
    /// Пакетная конвертация Rgba32 → YCbCr с прямым SIMD.
    /// </summary>
    public static void ToYCbCr(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → YCbCr с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Rgba32.</param>
    /// <param name="destination">Целевой буфер YCbCr.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void ToYCbCr(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        if (source.Length > destination.Length)
            throw new ArgumentException("Destination buffer is too small", nameof(destination));

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        // Параллельная обработка для буферов >= 1024 пикселей
        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
            {
                ToYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToYCbCrCore(source, destination, selected);
    }

    /// <summary>Однопоточная SIMD конвертация Rgba32 → YCbCr с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrCore(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 16:
                ToYCbCrAvx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToYCbCrSse41(source, destination);
                break;
            default:
                ToYCbCrScalar(source, destination);
                break;
        }
    }

    /// <summary>Параллельная конвертация Rgba32 → YCbCr с выбранным ускорителем.</summary>
    private static unsafe void ToYCbCrParallel(Rgba32* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);

            ToYCbCrCore(new ReadOnlySpan<Rgba32>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (YCbCr → Rgba32)

    /// <summary>
    /// Пакетная конвертация YCbCr → Rgba32 с прямым SIMD.
    /// </summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация YCbCr → Rgba32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер YCbCr.</param>
    /// <param name="destination">Целевой буфер Rgba32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        if (source.Length > destination.Length)
            throw new ArgumentException("Destination buffer is too small", nameof(destination));

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        // Параллельная обработка для буферов >= 1024 пикселей
        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
            {
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromYCbCrCore(source, destination, selected);
    }

    /// <summary>Однопоточная SIMD конвертация YCbCr → Rgba32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 16:
                FromYCbCrAvx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromYCbCrSse41(source, destination);
                break;
            default:
                FromYCbCrScalar(source, destination);
                break;
        }
    }

    /// <summary>Параллельная конвертация YCbCr → Rgba32 с выбранным ускорителем.</summary>
    private static unsafe void FromYCbCrParallel(YCbCr* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);

            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Rgba32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Предвычисленная константа: Half + (128 << 16) = 32768 + 8388608 = 8421376
            const int CbCrOffset = 8421376;

            // 8 пикселей за итерацию (32 байт input → 24 байт output)
            while (count >= 8)
            {
                // Пиксель 0
                int r0 = src[0], g0 = src[1], b0 = src[2];
                dst[0] = (byte)(((CYR * r0) + (CYG * g0) + (CYB * b0) + HalfQ16) >> 16);
                dst[1] = ClampByte(((CCbR * r0) + (CCbG * g0) + (CCbB * b0) + CbCrOffset) >> 16);
                dst[2] = ClampByte(((CCrR * r0) + (CCrG * g0) + (CCrB * b0) + CbCrOffset) >> 16);

                // Пиксель 1
                int r1 = src[4], g1 = src[5], b1 = src[6];
                dst[3] = (byte)(((CYR * r1) + (CYG * g1) + (CYB * b1) + HalfQ16) >> 16);
                dst[4] = ClampByte(((CCbR * r1) + (CCbG * g1) + (CCbB * b1) + CbCrOffset) >> 16);
                dst[5] = ClampByte(((CCrR * r1) + (CCrG * g1) + (CCrB * b1) + CbCrOffset) >> 16);

                // Пиксель 2
                int r2 = src[8], g2 = src[9], b2 = src[10];
                dst[6] = (byte)(((CYR * r2) + (CYG * g2) + (CYB * b2) + HalfQ16) >> 16);
                dst[7] = ClampByte(((CCbR * r2) + (CCbG * g2) + (CCbB * b2) + CbCrOffset) >> 16);
                dst[8] = ClampByte(((CCrR * r2) + (CCrG * g2) + (CCrB * b2) + CbCrOffset) >> 16);

                // Пиксель 3
                int r3 = src[12], g3 = src[13], b3 = src[14];
                dst[9] = (byte)(((CYR * r3) + (CYG * g3) + (CYB * b3) + HalfQ16) >> 16);
                dst[10] = ClampByte(((CCbR * r3) + (CCbG * g3) + (CCbB * b3) + CbCrOffset) >> 16);
                dst[11] = ClampByte(((CCrR * r3) + (CCrG * g3) + (CCrB * b3) + CbCrOffset) >> 16);

                // Пиксель 4
                int r4 = src[16], g4 = src[17], b4 = src[18];
                dst[12] = (byte)(((CYR * r4) + (CYG * g4) + (CYB * b4) + HalfQ16) >> 16);
                dst[13] = ClampByte(((CCbR * r4) + (CCbG * g4) + (CCbB * b4) + CbCrOffset) >> 16);
                dst[14] = ClampByte(((CCrR * r4) + (CCrG * g4) + (CCrB * b4) + CbCrOffset) >> 16);

                // Пиксель 5
                int r5 = src[20], g5 = src[21], b5 = src[22];
                dst[15] = (byte)(((CYR * r5) + (CYG * g5) + (CYB * b5) + HalfQ16) >> 16);
                dst[16] = ClampByte(((CCbR * r5) + (CCbG * g5) + (CCbB * b5) + CbCrOffset) >> 16);
                dst[17] = ClampByte(((CCrR * r5) + (CCrG * g5) + (CCrB * b5) + CbCrOffset) >> 16);

                // Пиксель 6
                int r6 = src[24], g6 = src[25], b6 = src[26];
                dst[18] = (byte)(((CYR * r6) + (CYG * g6) + (CYB * b6) + HalfQ16) >> 16);
                dst[19] = ClampByte(((CCbR * r6) + (CCbG * g6) + (CCbB * b6) + CbCrOffset) >> 16);
                dst[20] = ClampByte(((CCrR * r6) + (CCrG * g6) + (CCrB * b6) + CbCrOffset) >> 16);

                // Пиксель 7
                int r7 = src[28], g7 = src[29], b7 = src[30];
                dst[21] = (byte)(((CYR * r7) + (CYG * g7) + (CYB * b7) + HalfQ16) >> 16);
                dst[22] = ClampByte(((CCbR * r7) + (CCbG * g7) + (CCbB * b7) + CbCrOffset) >> 16);
                dst[23] = ClampByte(((CCrR * r7) + (CCrG * g7) + (CCrB * b7) + CbCrOffset) >> 16);

                src += 32; // 8 пикселей × 4 байта = 32 байта
                dst += 24; // 8 пикселей × 3 байта = 24 байта
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                int r = src[0], g = src[1], b = src[2];
                dst[0] = (byte)(((CYR * r) + (CYG * g) + (CYB * b) + HalfQ16) >> 16);
                dst[1] = ClampByte(((CCbR * r) + (CCbG * g) + (CCbB * b) + CbCrOffset) >> 16);
                dst[2] = ClampByte(((CCrR * r) + (CCrG * g) + (CCrB * b) + CbCrOffset) >> 16);
                src += 4;
                dst += 3;
                count--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Предвычисленные константы (убираем -128 и +HalfQ16 из runtime)
            // HalfMinusBOffset = HalfQ16 - CBOffset = 32768 - 14864640 = -14831872
            // HalfPlusGOffset = HalfQ16 + CGOffset = 32768 + 8877568 = 8910336
            // HalfMinusROffset = HalfQ16 - CROffset = 32768 - 11760768 = -11728000
            const int HalfMinusBOffset = -14831872;
            const int HalfPlusGOffset = 8910336;
            const int HalfMinusROffset = -11728000;

            // 8 пикселей за итерацию (24 байт input → 32 байт output)
            while (count >= 8)
            {
                // Пиксель 0: Y в Q16, raw Cb/Cr без -128
                int y0 = src[0] << 16, cb0 = src[1], cr0 = src[2];
                dst[0] = ClampByte((y0 + (C1402 * cr0) + HalfMinusROffset) >> 16);
                dst[1] = ClampByte((y0 - (C0344 * cb0) - (C0714 * cr0) + HalfPlusGOffset) >> 16);
                dst[2] = ClampByte((y0 + (C1772 * cb0) + HalfMinusBOffset) >> 16);
                dst[3] = 255;

                // Пиксель 1
                int y1 = src[3] << 16, cb1 = src[4], cr1 = src[5];
                dst[4] = ClampByte((y1 + (C1402 * cr1) + HalfMinusROffset) >> 16);
                dst[5] = ClampByte((y1 - (C0344 * cb1) - (C0714 * cr1) + HalfPlusGOffset) >> 16);
                dst[6] = ClampByte((y1 + (C1772 * cb1) + HalfMinusBOffset) >> 16);
                dst[7] = 255;

                // Пиксель 2
                int y2 = src[6] << 16, cb2 = src[7], cr2 = src[8];
                dst[8] = ClampByte((y2 + (C1402 * cr2) + HalfMinusROffset) >> 16);
                dst[9] = ClampByte((y2 - (C0344 * cb2) - (C0714 * cr2) + HalfPlusGOffset) >> 16);
                dst[10] = ClampByte((y2 + (C1772 * cb2) + HalfMinusBOffset) >> 16);
                dst[11] = 255;

                // Пиксель 3
                int y3 = src[9] << 16, cb3 = src[10], cr3 = src[11];
                dst[12] = ClampByte((y3 + (C1402 * cr3) + HalfMinusROffset) >> 16);
                dst[13] = ClampByte((y3 - (C0344 * cb3) - (C0714 * cr3) + HalfPlusGOffset) >> 16);
                dst[14] = ClampByte((y3 + (C1772 * cb3) + HalfMinusBOffset) >> 16);
                dst[15] = 255;

                // Пиксель 4
                int y4 = src[12] << 16, cb4 = src[13], cr4 = src[14];
                dst[16] = ClampByte((y4 + (C1402 * cr4) + HalfMinusROffset) >> 16);
                dst[17] = ClampByte((y4 - (C0344 * cb4) - (C0714 * cr4) + HalfPlusGOffset) >> 16);
                dst[18] = ClampByte((y4 + (C1772 * cb4) + HalfMinusBOffset) >> 16);
                dst[19] = 255;

                // Пиксель 5
                int y5 = src[15] << 16, cb5 = src[16], cr5 = src[17];
                dst[20] = ClampByte((y5 + (C1402 * cr5) + HalfMinusROffset) >> 16);
                dst[21] = ClampByte((y5 - (C0344 * cb5) - (C0714 * cr5) + HalfPlusGOffset) >> 16);
                dst[22] = ClampByte((y5 + (C1772 * cb5) + HalfMinusBOffset) >> 16);
                dst[23] = 255;

                // Пиксель 6
                int y6 = src[18] << 16, cb6 = src[19], cr6 = src[20];
                dst[24] = ClampByte((y6 + (C1402 * cr6) + HalfMinusROffset) >> 16);
                dst[25] = ClampByte((y6 - (C0344 * cb6) - (C0714 * cr6) + HalfPlusGOffset) >> 16);
                dst[26] = ClampByte((y6 + (C1772 * cb6) + HalfMinusBOffset) >> 16);
                dst[27] = 255;

                // Пиксель 7
                int y7 = src[21] << 16, cb7 = src[22], cr7 = src[23];
                dst[28] = ClampByte((y7 + (C1402 * cr7) + HalfMinusROffset) >> 16);
                dst[29] = ClampByte((y7 - (C0344 * cb7) - (C0714 * cr7) + HalfPlusGOffset) >> 16);
                dst[30] = ClampByte((y7 + (C1772 * cb7) + HalfMinusBOffset) >> 16);
                dst[31] = 255;

                src += 24; // 8 пикселей × 3 байта = 24 байта
                dst += 32; // 8 пикселей × 4 байта = 32 байта
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                int y = src[0] << 16, cb = src[1], cr = src[2];
                dst[0] = ClampByte((y + (C1402 * cr) + HalfMinusROffset) >> 16);
                dst[1] = ClampByte((y - (C0344 * cb) - (C0714 * cr) + HalfPlusGOffset) >> 16);
                dst[2] = ClampByte((y + (C1772 * cb) + HalfMinusBOffset) >> 16);
                dst[3] = 255;
                src += 3;
                dst += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Rgba32 → YCbCr, Q16 int32 for precision)

    /// <summary>
    /// AVX2 прямая конвертация Rgba32 → YCbCr — 32 пикселя за итерацию.
    /// RGBA (128 байт) → YCbCr (96 байт).
    /// Использует Q16 int32 с единым округлением для точности как у scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrAvx2(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы для YCbCr (как scalar)
            var cYR = YCbCrAvx2Vectors.CYR;       // 0.299 × 65536 = 19595
            var cYG = YCbCrAvx2Vectors.CYG;       // 0.587 × 65536 = 38470
            var cYB = YCbCrAvx2Vectors.CYB;       // 0.114 × 65536 = 7471
            var cCbR = YCbCrAvx2Vectors.CCbR;     // -0.168736 × 65536 = -11059
            var cCbG = YCbCrAvx2Vectors.CCbG;     // -0.331264 × 65536 = -21709
            var cCbB = YCbCrAvx2Vectors.CCbB;     // 0.5 × 65536 = 32768
            var cCrR = YCbCrAvx2Vectors.CCrR;     // 0.5 × 65536 = 32768
            var cCrG = YCbCrAvx2Vectors.CCrG;     // -0.418688 × 65536 = -27439
            var cCrB = YCbCrAvx2Vectors.CCrB;     // -0.081312 × 65536 = -5329
            var c128 = YCbCrAvx2Vectors.C128;
            var half = YCbCrAvx2Vectors.Half;

            // Кешированные shuffle маски для RGBA
            var shuffleR = Rgba32Sse41Vectors.Rgba32ShuffleR;
            var shuffleG = Rgba32Sse41Vectors.Rgba32ShuffleG;
            var shuffleB = Rgba32Sse41Vectors.Rgba32ShuffleB;

            // 32 пикселя за итерацию (4 блока по 8 int32)
            while (count >= 32)
            {
                Sse.Prefetch0(srcByte + 256);

                // Первые 16 пикселей
                DeinterleaveRgba16(srcByte, shuffleR, shuffleG, shuffleB, out var r0Bytes, out var g0Bytes, out var b0Bytes);

                // Пиксели 0-7 (int32)
                var r0 = Avx2.ConvertToVector256Int32(r0Bytes);
                var g0 = Avx2.ConvertToVector256Int32(g0Bytes);
                var b0 = Avx2.ConvertToVector256Int32(b0Bytes);

                // Y = (CYR*R + CYG*G + CYB*B + Half) >> 16
                var y0 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r0), Avx2.MultiplyLow(cYG, g0)), Avx2.MultiplyLow(cYB, b0)), half), 16);
                // Cb = ((CCbR*R + CCbG*G + CCbB*B + Half) >> 16) + 128
                var cb0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r0), Avx2.MultiplyLow(cCbG, g0)), Avx2.MultiplyLow(cCbB, b0)), half), 16), c128);
                // Cr = ((CCrR*R + CCrG*G + CCrB*B + Half) >> 16) + 128
                var cr0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r0), Avx2.MultiplyLow(cCrG, g0)), Avx2.MultiplyLow(cCrB, b0)), half), 16), c128);

                // Пиксели 8-15 (int32)
                var r1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(r0Bytes, 8));
                var g1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(g0Bytes, 8));
                var b1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(b0Bytes, 8));

                var y1 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r1), Avx2.MultiplyLow(cYG, g1)), Avx2.MultiplyLow(cYB, b1)), half), 16);
                var cb1 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r1), Avx2.MultiplyLow(cCbG, g1)), Avx2.MultiplyLow(cCbB, b1)), half), 16), c128);
                var cr1 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r1), Avx2.MultiplyLow(cCrG, g1)), Avx2.MultiplyLow(cCrB, b1)), half), 16), c128);

                // Вторые 16 пикселей
                DeinterleaveRgba16(srcByte + 64, shuffleR, shuffleG, shuffleB, out var r2Bytes, out var g2Bytes, out var b2Bytes);

                var r2 = Avx2.ConvertToVector256Int32(r2Bytes);
                var g2 = Avx2.ConvertToVector256Int32(g2Bytes);
                var b2 = Avx2.ConvertToVector256Int32(b2Bytes);

                var y2 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r2), Avx2.MultiplyLow(cYG, g2)), Avx2.MultiplyLow(cYB, b2)), half), 16);
                var cb2 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r2), Avx2.MultiplyLow(cCbG, g2)), Avx2.MultiplyLow(cCbB, b2)), half), 16), c128);
                var cr2 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r2), Avx2.MultiplyLow(cCrG, g2)), Avx2.MultiplyLow(cCrB, b2)), half), 16), c128);

                var r3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(r2Bytes, 8));
                var g3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(g2Bytes, 8));
                var b3 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(b2Bytes, 8));

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

                // Interleave и запись (32 пикселя = 96 байт)
                InterleaveYCbCr16(dstByte, yBytes.GetLower(), cbBytes.GetLower(), crBytes.GetLower());
                InterleaveYCbCr16(dstByte + 48, yBytes.GetUpper(), cbBytes.GetUpper(), crBytes.GetUpper());

                srcByte += 128;
                dstByte += 96;
                count -= 32;
            }

            // Остаток через scalar
            if (count > 0)
                ToYCbCrScalar(new ReadOnlySpan<Rgba32>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    /// <summary>Извлекает R, G, B из 16 RGBA пикселей (64 байта).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgba16(
        byte* src,
        Vector128<byte> shuffleR, Vector128<byte> shuffleG, Vector128<byte> shuffleB,
        out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        // Загружаем 4 блока по 16 байт (по 4 пикселя каждый)
        var rgba0 = Sse2.LoadVector128(src);
        var rgba1 = Sse2.LoadVector128(src + 16);
        var rgba2 = Sse2.LoadVector128(src + 32);
        var rgba3 = Sse2.LoadVector128(src + 48);

        // Извлекаем R, G, B из каждого блока (4 байта на блок)
        var r0 = Ssse3.Shuffle(rgba0, shuffleR);
        var r1 = Ssse3.Shuffle(rgba1, shuffleR);
        var r2 = Ssse3.Shuffle(rgba2, shuffleR);
        var r3 = Ssse3.Shuffle(rgba3, shuffleR);

        var g0 = Ssse3.Shuffle(rgba0, shuffleG);
        var g1 = Ssse3.Shuffle(rgba1, shuffleG);
        var g2 = Ssse3.Shuffle(rgba2, shuffleG);
        var g3 = Ssse3.Shuffle(rgba3, shuffleG);

        var b0 = Ssse3.Shuffle(rgba0, shuffleB);
        var b1 = Ssse3.Shuffle(rgba1, shuffleB);
        var b2 = Ssse3.Shuffle(rgba2, shuffleB);
        var b3 = Ssse3.Shuffle(rgba3, shuffleB);

        // Объединяем: 4+4+4+4 = 16 байт
        r = Sse2.Or(Sse2.Or(r0, Sse2.ShiftLeftLogical128BitLane(r1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(r2, 8), Sse2.ShiftLeftLogical128BitLane(r3, 12)));
        g = Sse2.Or(Sse2.Or(g0, Sse2.ShiftLeftLogical128BitLane(g1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(g2, 8), Sse2.ShiftLeftLogical128BitLane(g3, 12)));
        b = Sse2.Or(Sse2.Or(b0, Sse2.ShiftLeftLogical128BitLane(b1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(b2, 8), Sse2.ShiftLeftLogical128BitLane(b3, 12)));
    }

    /// <summary>Interleave Y, Cb, Cr → YCbCr (16 пикселей, 48 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr16(byte* dst, Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr)
    {
        InterleaveYCbCr8(dst, y, cb, cr);
        InterleaveYCbCr8(dst + 24,
            Sse2.ShiftRightLogical128BitLane(y, 8),
            Sse2.ShiftRightLogical128BitLane(cb, 8),
            Sse2.ShiftRightLogical128BitLane(cr, 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr8(byte* dst, Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr)
    {
        var ycb = Sse2.UnpackLow(y, cb);
        var out0 = Sse2.Or(
            Ssse3.Shuffle(ycb, Rgba32Sse41Vectors.YCbToRgbaShuffleMask0),
            Ssse3.Shuffle(cr, Rgba32Sse41Vectors.CrToRgbaShuffleMask0));
        out0.Store(dst);

        var out1 = Sse2.Or(
            Ssse3.Shuffle(ycb, Rgba32Sse41Vectors.YCbToRgbaShuffleMask1),
            Ssse3.Shuffle(cr, Rgba32Sse41Vectors.CrToRgbaShuffleMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    #endregion

    #region AVX2 Implementation (YCbCr → Rgba32, Q16 int32 for precision)

    /// <summary>
    /// AVX2 прямая конвертация YCbCr → Rgba32 — 32 пикселя за итерацию.
    /// YCbCr (96 байт) → RGBA (128 байт).
    /// Использует Q16 int32 с единым округлением для точности как у scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы (как scalar)
            var c1402 = YCbCrAvx2Vectors.C1402;   // 1.402 × 65536 = 91881
            var c0344 = YCbCrAvx2Vectors.C0344;   // 0.344136 × 65536 = 22554
            var c0714 = YCbCrAvx2Vectors.C0714;   // 0.714136 × 65536 = 46802
            var c1772 = YCbCrAvx2Vectors.C1772;   // 1.772 × 65536 = 116130
            var c128 = YCbCrAvx2Vectors.C128;
            var half = YCbCrAvx2Vectors.Half;
            var alpha = Rgba32Sse41Vectors.YCbCrAlpha255Mask;

            // 32 пикселя за итерацию (4 блока по 8 int32)
            while (count >= 32)
            {
                Sse.Prefetch0(srcByte + 192);

                // Первые 16 пикселей
                DeinterleaveYCbCr16(srcByte, out var yBytes01, out var cbBytes01, out var crBytes01);

                // Пиксели 0-7 (int32)
                var y0 = Avx2.ConvertToVector256Int32(yBytes01);
                var cb0 = Avx2.Subtract(Avx2.ConvertToVector256Int32(cbBytes01), c128);
                var cr0 = Avx2.Subtract(Avx2.ConvertToVector256Int32(crBytes01), c128);

                // R = Y + ((C1402 * Cr + Half) >> 16)
                var r0 = Avx2.Add(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr0), half), 16));
                // G = Y - ((C0344 * Cb + C0714 * Cr + Half) >> 16)
                var g0 = Avx2.Subtract(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb0), Avx2.MultiplyLow(c0714, cr0)), half), 16));
                // B = Y + ((C1772 * Cb + Half) >> 16)
                var b0 = Avx2.Add(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb0), half), 16));

                // Пиксели 8-15 (int32)
                var y1 = Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(yBytes01, 8));
                var cb1 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(cbBytes01, 8)), c128);
                var cr1 = Avx2.Subtract(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(crBytes01, 8)), c128);

                var r1 = Avx2.Add(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr1), half), 16));
                var g1 = Avx2.Subtract(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb1), Avx2.MultiplyLow(c0714, cr1)), half), 16));
                var b1 = Avx2.Add(y1, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1772, cb1), half), 16));

                // Вторые 16 пикселей
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

                // Interleave и запись (32 пикселя = 128 байт RGBA)
                InterleaveRgba16(dstByte, rBytes.GetLower(), gBytes.GetLower(), bBytes.GetLower(), alpha);
                InterleaveRgba16(dstByte + 64, rBytes.GetUpper(), gBytes.GetUpper(), bBytes.GetUpper(), alpha);

                srcByte += 96;
                dstByte += 128;
                count -= 32;
            }

            // Остаток через scalar
            if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgba32>(dstByte, count));
        }
    }

    /// <summary>Deinterleave YCbCr → Y, Cb, Cr (16 пикселей из 48 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr16(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        DeinterleaveYCbCr8(src, out var y0, out var cb0, out var cr0);
        DeinterleaveYCbCr8(src + 24, out var y1, out var cb1, out var cr1);

        y = Sse2.UnpackLow(y0.AsUInt64(), y1.AsUInt64()).AsByte();
        cb = Sse2.UnpackLow(cb0.AsUInt64(), cb1.AsUInt64()).AsByte();
        cr = Sse2.UnpackLow(cr0.AsUInt64(), cr1.AsUInt64()).AsByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr8(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        var bytes0 = Sse2.LoadVector128(src);
        var bytes1 = Sse2.LoadScalarVector128((ulong*)(src + 16)).AsByte();

        y = Sse2.Or(
            Ssse3.Shuffle(bytes0, Rgba32Sse41Vectors.RgbaDeinterleaveY0),
            Ssse3.Shuffle(bytes1, Rgba32Sse41Vectors.RgbaDeinterleaveY1));
        cb = Sse2.Or(
            Ssse3.Shuffle(bytes0, Rgba32Sse41Vectors.RgbaDeinterleaveCb0),
            Ssse3.Shuffle(bytes1, Rgba32Sse41Vectors.RgbaDeinterleaveCb1));
        cr = Sse2.Or(
            Ssse3.Shuffle(bytes0, Rgba32Sse41Vectors.RgbaDeinterleaveCr0),
            Ssse3.Shuffle(bytes1, Rgba32Sse41Vectors.RgbaDeinterleaveCr1));
    }

    /// <summary>Interleave R, G, B, A → RGBA (16 пикселей, 64 байта).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgba16(byte* dst, Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, Vector128<byte> a)
    {
        // Interleave в 4 блока по 4 пикселя
        var rg_lo = Sse2.UnpackLow(r, g);   // R0G0R1G1R2G2R3G3...
        var ba_lo = Sse2.UnpackLow(b, a);   // B0A0B1A1B2A2B3A3...
        var rg_hi = Sse2.UnpackHigh(r, g);
        var ba_hi = Sse2.UnpackHigh(b, a);

        // RGBA пиксели
        var rgba0 = Sse2.UnpackLow(rg_lo.AsInt16(), ba_lo.AsInt16()).AsByte();  // Пиксели 0-3
        var rgba1 = Sse2.UnpackHigh(rg_lo.AsInt16(), ba_lo.AsInt16()).AsByte(); // Пиксели 4-7
        var rgba2 = Sse2.UnpackLow(rg_hi.AsInt16(), ba_hi.AsInt16()).AsByte();  // Пиксели 8-11
        var rgba3 = Sse2.UnpackHigh(rg_hi.AsInt16(), ba_hi.AsInt16()).AsByte(); // Пиксели 12-15

        rgba0.Store(dst);
        rgba1.Store(dst + 16);
        rgba2.Store(dst + 32);
        rgba3.Store(dst + 48);
    }

    #endregion

    #region SSE4.1 Implementation

    /// <summary>
    /// SSE4.1 версия Rgba32 → YCbCr — 4 пикселя за итерацию.
    /// Использует Q16 fixed-point с Vector128&lt;int&gt;.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrSse41(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы
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

            var shuffleR = Rgba32Sse41Vectors.Rgba32ShuffleR;
            var shuffleG = Rgba32Sse41Vectors.Rgba32ShuffleG;
            var shuffleB = Rgba32Sse41Vectors.Rgba32ShuffleB;

            // 8 пикселей за итерацию (2 блока по 4)
            while (count >= 8)
            {
                // Загружаем 8 RGBA пикселей (32 байта)
                var rgba0 = Sse2.LoadVector128(srcByte);
                var rgba1 = Sse2.LoadVector128(srcByte + 16);

                // Извлекаем R, G, B (8 байт каждый)
                var rBytes = Sse2.Or(Ssse3.Shuffle(rgba0, shuffleR), Sse2.ShiftLeftLogical128BitLane(Ssse3.Shuffle(rgba1, shuffleR), 4));
                var gBytes = Sse2.Or(Ssse3.Shuffle(rgba0, shuffleG), Sse2.ShiftLeftLogical128BitLane(Ssse3.Shuffle(rgba1, shuffleG), 4));
                var bBytes = Sse2.Or(Ssse3.Shuffle(rgba0, shuffleB), Sse2.ShiftLeftLogical128BitLane(Ssse3.Shuffle(rgba1, shuffleB), 4));

                // Обработка первых 4 пикселей (lo)
                var rLo = Sse41.ConvertToVector128Int32(rBytes);
                var gLo = Sse41.ConvertToVector128Int32(gBytes);
                var bLo = Sse41.ConvertToVector128Int32(bBytes);

                var yLo = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cYR, rLo), Sse41.MultiplyLow(cYG, gLo)), Sse41.MultiplyLow(cYB, bLo)), half), 16);
                var cbLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCbR, rLo), Sse41.MultiplyLow(cCbG, gLo)), Sse41.MultiplyLow(cCbB, bLo)), half), 16), c128);
                var crLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCrR, rLo), Sse41.MultiplyLow(cCrG, gLo)), Sse41.MultiplyLow(cCrB, bLo)), half), 16), c128);

                // Обработка следующих 4 пикселей (hi)
                var rHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rBytes, 4));
                var gHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gBytes, 4));
                var bHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bBytes, 4));

                var yHi = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cYR, rHi), Sse41.MultiplyLow(cYG, gHi)), Sse41.MultiplyLow(cYB, bHi)), half), 16);
                var cbHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCbR, rHi), Sse41.MultiplyLow(cCbG, gHi)), Sse41.MultiplyLow(cCbB, bHi)), half), 16), c128);
                var crHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCrR, rHi), Sse41.MultiplyLow(cCrG, gHi)), Sse41.MultiplyLow(cCrB, bHi)), half), 16), c128);

                // Pack int32 → int16 → uint8
                var yPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(yLo, yHi), Sse2.PackSignedSaturate(yLo, yHi));
                var cbPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(cbLo, cbHi), Sse2.PackSignedSaturate(cbLo, cbHi));
                var crPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(crLo, crHi), Sse2.PackSignedSaturate(crLo, crHi));

                InterleaveYCbCr8(dstByte, yPacked, cbPacked, crPacked);

                srcByte += 32;
                dstByte += 24;
                count -= 8;
            }

            if (count > 0)
                ToYCbCrScalar(new ReadOnlySpan<Rgba32>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    /// <summary>
    /// SSE4.1 версия YCbCr → Rgba32 — 16 пикселей за итерацию.
    /// Использует Q16 fixed-point с Vector128&lt;int&gt;.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы для обратной конвертации
            var c1402 = YCbCrSse41Vectors.C1402;
            var c0344 = YCbCrSse41Vectors.C0344;
            var c0714 = YCbCrSse41Vectors.C0714;
            var c1772 = YCbCrSse41Vectors.C1772;
            var c128 = YCbCrSse41Vectors.C128;
            var half = YCbCrSse41Vectors.Half;
            var alpha = Rgba32Sse41Vectors.YCbCrAlpha255Mask;

            // 16 пикселей за итерацию (48 байт YCbCr → 64 байта RGBA)
            while (count >= 16)
            {
                // === Первые 8 пикселей ===
                DeinterleaveYCbCr8(srcByte, out var yVec0, out var cbVec0, out var crVec0);

                var yLo0 = Sse41.ConvertToVector128Int32(yVec0);
                var cbLo0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec0), c128);
                var crLo0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec0), c128);

                var rLo0 = Sse2.Add(yLo0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crLo0), half), 16));
                var gLo0 = Sse2.Subtract(yLo0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo0), Sse41.MultiplyLow(c0714, crLo0)), half), 16));
                var bLo0 = Sse2.Add(yLo0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo0), half), 16));

                var yHi0 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec0, 4));
                var cbHi0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec0, 4)), c128);
                var crHi0 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec0, 4)), c128);

                var rHi0 = Sse2.Add(yHi0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi0), half), 16));
                var gHi0 = Sse2.Subtract(yHi0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi0), Sse41.MultiplyLow(c0714, crHi0)), half), 16));
                var bHi0 = Sse2.Add(yHi0, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi0), half), 16));

                var rPacked0 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo0, rHi0), Sse2.PackSignedSaturate(rLo0, rHi0));
                var gPacked0 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo0, gHi0), Sse2.PackSignedSaturate(gLo0, gHi0));
                var bPacked0 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo0, bHi0), Sse2.PackSignedSaturate(bLo0, bHi0));

                // === Вторые 8 пикселей ===
                DeinterleaveYCbCr8(srcByte + 24, out var yVec1, out var cbVec1, out var crVec1);

                var yLo1 = Sse41.ConvertToVector128Int32(yVec1);
                var cbLo1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec1), c128);
                var crLo1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec1), c128);

                var rLo1 = Sse2.Add(yLo1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crLo1), half), 16));
                var gLo1 = Sse2.Subtract(yLo1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo1), Sse41.MultiplyLow(c0714, crLo1)), half), 16));
                var bLo1 = Sse2.Add(yLo1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo1), half), 16));

                var yHi1 = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec1, 4));
                var cbHi1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec1, 4)), c128);
                var crHi1 = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec1, 4)), c128);

                var rHi1 = Sse2.Add(yHi1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi1), half), 16));
                var gHi1 = Sse2.Subtract(yHi1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi1), Sse41.MultiplyLow(c0714, crHi1)), half), 16));
                var bHi1 = Sse2.Add(yHi1, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi1), half), 16));

                var rPacked1 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo1, rHi1), Sse2.PackSignedSaturate(rLo1, rHi1));
                var gPacked1 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo1, gHi1), Sse2.PackSignedSaturate(gLo1, gHi1));
                var bPacked1 = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo1, bHi1), Sse2.PackSignedSaturate(bLo1, bHi1));

                // Interleave R, G, B, A → RGBA (16 пикселей = 64 байта)
                var rg0 = Sse2.UnpackLow(rPacked0, gPacked0);
                var ba0 = Sse2.UnpackLow(bPacked0, alpha);
                var rgba0_0 = Sse2.UnpackLow(rg0.AsInt16(), ba0.AsInt16()).AsByte();
                var rgba0_1 = Sse2.UnpackHigh(rg0.AsInt16(), ba0.AsInt16()).AsByte();

                var rg1 = Sse2.UnpackLow(rPacked1, gPacked1);
                var ba1 = Sse2.UnpackLow(bPacked1, alpha);
                var rgba1_0 = Sse2.UnpackLow(rg1.AsInt16(), ba1.AsInt16()).AsByte();
                var rgba1_1 = Sse2.UnpackHigh(rg1.AsInt16(), ba1.AsInt16()).AsByte();

                rgba0_0.Store(dstByte);
                rgba0_1.Store(dstByte + 16);
                rgba1_0.Store(dstByte + 32);
                rgba1_1.Store(dstByte + 48);

                srcByte += 48;
                dstByte += 64;
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
                var gLo = Sse2.Subtract(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo), Sse41.MultiplyLow(c0714, crLo)), half), 16));
                var bLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo), half), 16));

                var yHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec, 4));
                var cbHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec, 4)), c128);
                var crHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec, 4)), c128);

                var rHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi), half), 16));
                var gHi = Sse2.Subtract(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi), Sse41.MultiplyLow(c0714, crHi)), half), 16));
                var bHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi), half), 16));

                var rPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo, rHi), Sse2.PackSignedSaturate(rLo, rHi));
                var gPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo, gHi), Sse2.PackSignedSaturate(gLo, gHi));
                var bPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo, bHi), Sse2.PackSignedSaturate(bLo, bHi));

                var rg = Sse2.UnpackLow(rPacked, gPacked);
                var ba = Sse2.UnpackLow(bPacked, alpha);
                var rgba0 = Sse2.UnpackLow(rg.AsInt16(), ba.AsInt16()).AsByte();
                var rgba1 = Sse2.UnpackHigh(rg.AsInt16(), ba.AsInt16()).AsByte();

                rgba0.Store(dstByte);
                rgba1.Store(dstByte + 16);

                srcByte += 24;
                dstByte += 32;
                count -= 8;
            }

            if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgba32>(dstByte, count));
        }
    }

    #endregion

    #region AVX512BW Implementation (YCbCr)

    /// <summary>
    /// AVX512BW версия Rgba32 → YCbCr — 16 пикселей за итерацию.
    /// Использует AVX2 внутренний цикл с широким store.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrAvx512(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы (такие же как в SSE41)
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

            var shuffleR = Rgba32Sse41Vectors.Rgba32ShuffleR;
            var shuffleG = Rgba32Sse41Vectors.Rgba32ShuffleG;
            var shuffleB = Rgba32Sse41Vectors.Rgba32ShuffleB;

            // 16 пикселей за итерацию (64 байта RGBA → 48 байт YCbCr)
            while (count >= 16)
            {
                // Обрабатываем 2 группы по 8 пикселей
                for (var g = 0; g < 2; g++)
                {
                    var rgba0 = Sse2.LoadVector128(srcByte + (g * 32));
                    var rgba1 = Sse2.LoadVector128(srcByte + (g * 32) + 16);

                    var r0 = Ssse3.Shuffle(rgba0, shuffleR);
                    var g0 = Ssse3.Shuffle(rgba0, shuffleG);
                    var b0 = Ssse3.Shuffle(rgba0, shuffleB);
                    var r1 = Ssse3.Shuffle(rgba1, shuffleR);
                    var g1 = Ssse3.Shuffle(rgba1, shuffleG);
                    var b1 = Ssse3.Shuffle(rgba1, shuffleB);

                    var rVec = Sse2.Or(r0, Sse2.ShiftLeftLogical128BitLane(r1, 4));
                    var gVec = Sse2.Or(g0, Sse2.ShiftLeftLogical128BitLane(g1, 4));
                    var bVec = Sse2.Or(b0, Sse2.ShiftLeftLogical128BitLane(b1, 4));

                    // Первые 4 пикселя
                    var rLo = Sse41.ConvertToVector128Int32(rVec);
                    var gLo = Sse41.ConvertToVector128Int32(gVec);
                    var bLo = Sse41.ConvertToVector128Int32(bVec);

                    var yLo = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cYR, rLo), Sse41.MultiplyLow(cYG, gLo)), Sse41.MultiplyLow(cYB, bLo)), half), 16);
                    var cbLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCbR, rLo), Sse41.MultiplyLow(cCbG, gLo)), Sse41.MultiplyLow(cCbB, bLo)), half), 16), c128);
                    var crLo = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCrR, rLo), Sse41.MultiplyLow(cCrG, gLo)), Sse41.MultiplyLow(cCrB, bLo)), half), 16), c128);

                    // Следующие 4 пикселя
                    var rHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec, 4));
                    var gHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec, 4));
                    var bHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec, 4));

                    var yHi = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cYR, rHi), Sse41.MultiplyLow(cYG, gHi)), Sse41.MultiplyLow(cYB, bHi)), half), 16);
                    var cbHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCbR, rHi), Sse41.MultiplyLow(cCbG, gHi)), Sse41.MultiplyLow(cCbB, bHi)), half), 16), c128);
                    var crHi = Sse2.Add(Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(cCrR, rHi), Sse41.MultiplyLow(cCrG, gHi)), Sse41.MultiplyLow(cCrB, bHi)), half), 16), c128);

                    // Pack и interleave
                    var yPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(yLo, yHi), Sse2.PackSignedSaturate(yLo, yHi));
                    var cbPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(cbLo, cbHi), Sse2.PackSignedSaturate(cbLo, cbHi));
                    var crPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(crLo, crHi), Sse2.PackSignedSaturate(crLo, crHi));

                    var ycb = Sse2.UnpackLow(yPacked, cbPacked);
                    var ycbcr0 = Ssse3.Shuffle(ycb, Rgba32Sse41Vectors.YCbToRgbaShuffleMask0);
                    var cr0 = Ssse3.Shuffle(crPacked, Rgba32Sse41Vectors.CrToRgbaShuffleMask0);
                    var out0 = Sse2.Or(ycbcr0, cr0);

                    var ycbcr1 = Ssse3.Shuffle(ycb, Rgba32Sse41Vectors.YCbToRgbaShuffleMask1);
                    var cr1 = Ssse3.Shuffle(crPacked, Rgba32Sse41Vectors.CrToRgbaShuffleMask1);
                    var out1 = Sse2.Or(ycbcr1, cr1);

                    out0.Store(dstByte + (g * 24));
                    Sse2.StoreLow((double*)(dstByte + (g * 24) + 16), out1.AsDouble());
                }

                srcByte += 64;
                dstByte += 48;
                count -= 16;
            }

            if (count >= 8)
                ToYCbCrSse41(new ReadOnlySpan<Rgba32>(srcByte, count), new Span<YCbCr>(dstByte, count));
            else if (count > 0)
                ToYCbCrScalar(new ReadOnlySpan<Rgba32>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    /// <summary>
    /// AVX512BW версия YCbCr → Rgba32 — 16 пикселей за итерацию.
    /// Использует SSE41 внутренний цикл с широким store.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx512(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            var c1402 = YCbCrSse41Vectors.C1402;
            var c0344 = YCbCrSse41Vectors.C0344;
            var c0714 = YCbCrSse41Vectors.C0714;
            var c1772 = YCbCrSse41Vectors.C1772;
            var c128 = YCbCrSse41Vectors.C128;
            var half = YCbCrSse41Vectors.Half;
            var alpha = Rgba32Sse41Vectors.YCbCrAlpha255Mask;

            // 16 пикселей за итерацию (48 байт YCbCr → 64 байта RGBA)
            while (count >= 16)
            {
                // Обрабатываем 2 группы по 8 пикселей
                for (var g = 0; g < 2; g++)
                {
                    DeinterleaveYCbCr8(srcByte + (g * 24), out var yVec, out var cbVec, out var crVec);

                    var yLo = Sse41.ConvertToVector128Int32(yVec);
                    var cbLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec), c128);
                    var crLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec), c128);

                    var rLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crLo), half), 16));
                    var gLo = Sse2.Subtract(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo), Sse41.MultiplyLow(c0714, crLo)), half), 16));
                    var bLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo), half), 16));

                    var yHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec, 4));
                    var cbHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec, 4)), c128);
                    var crHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec, 4)), c128);

                    var rHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi), half), 16));
                    var gHi = Sse2.Subtract(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi), Sse41.MultiplyLow(c0714, crHi)), half), 16));
                    var bHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi), half), 16));

                    var rPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo, rHi), Sse2.PackSignedSaturate(rLo, rHi));
                    var gPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo, gHi), Sse2.PackSignedSaturate(gLo, gHi));
                    var bPacked = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo, bHi), Sse2.PackSignedSaturate(bLo, bHi));

                    var rg = Sse2.UnpackLow(rPacked, gPacked);
                    var ba = Sse2.UnpackLow(bPacked, alpha);
                    var rgba0 = Sse2.UnpackLow(rg.AsInt16(), ba.AsInt16()).AsByte();
                    var rgba1 = Sse2.UnpackHigh(rg.AsInt16(), ba.AsInt16()).AsByte();

                    rgba0.Store(dstByte + (g * 32));
                    rgba1.Store(dstByte + (g * 32) + 16);
                }

                srcByte += 48;
                dstByte += 64;
                count -= 16;
            }

            if (count >= 8)
                FromYCbCrSse41(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgba32>(dstByte, count));
            else if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgba32>(dstByte, count));
        }
    }

    #endregion

    #region Conversion Operators (YCbCr)

    /// <summary>Явная конвертация из YCbCr в Rgba32.</summary>
    public static explicit operator Rgba32(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явная конвертация из Rgba32 в YCbCr.</summary>
    public static explicit operator YCbCr(Rgba32 rgba) => rgba.ToYCbCr();

    #endregion
}
