#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Прямая SIMD конвертация Bgra32 ↔ YCbCr (без промежуточного буфера).
/// Использует BT.601 Q16 fixed-point математику.
/// </summary>
public readonly partial struct Bgra32
{
    #region ITU-R BT.601 Constants (Q16) for YCbCr

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
    private const int HalfQ16 = 32768; // 0.5 * 65536

    /// <summary>
    /// Branchless clamp с использованием Math.Clamp (компилятор генерирует оптимальный код).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int x) => (byte)Math.Clamp(x, 0, 255);

    #endregion

    #region Single Pixel Conversion (YCbCr)

    /// <summary>Конвертирует YCbCr в Bgra32 (A = 255). Прямая формула BT.601.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromYCbCr(YCbCr ycbcr)
    {
        var y = (int)ycbcr.Y;
        var cb = ycbcr.Cb - 128;
        var cr = ycbcr.Cr - 128;

        var rCalc = y + (((C1402 * cr) + HalfQ16) >> 16);
        var gCalc = y - (((C0344 * cb) + (C0714 * cr) + HalfQ16) >> 16);
        var bCalc = y + (((C1772 * cb) + HalfQ16) >> 16);

        // Bgra32: B, G, R, A
        return new(ClampByte(bCalc), ClampByte(gCalc), ClampByte(rCalc), 255);
    }

    /// <summary>Конвертирует Bgra32 в YCbCr (игнорирует альфа-канал). Прямая формула BT.601.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr()
    {
        // Bgra32: B=field0, G=field1, R=field2, A=field3
        var rVal = (int)R;
        var gVal = (int)G;
        var bVal = (int)B;

        var yCalc = ((CYR * rVal) + (CYG * gVal) + (CYB * bVal) + HalfQ16) >> 16;
        var cbCalc = (((CCbR * rVal) + (CCbG * gVal) + (CCbB * bVal) + HalfQ16) >> 16) + 128;
        var crCalc = (((CCrR * rVal) + (CCrG * gVal) + (CCrB * bVal) + HalfQ16) >> 16) + 128;

        return new(ClampByte(yCalc), ClampByte(cbCalc), ClampByte(crCalc));
    }

    #endregion

    #region Batch Conversion (Bgra32 → YCbCr)

    /// <summary>
    /// Реализованные ускорители для конвертации Bgra32 ↔ YCbCr.
    /// </summary>
    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    /// <summary>
    /// Пакетная конвертация Bgra32 → YCbCr с прямым SIMD.
    /// </summary>
    public static void ToYCbCr(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → YCbCr с явным указанием ускорителя.
    /// </summary>
    public static unsafe void ToYCbCr(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        if (source.Length > destination.Length)
            throw new ArgumentException("Destination buffer is too small", nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
            {
                ToYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrCore(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
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

    private static unsafe void ToYCbCrParallel(Bgra32* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);

            ToYCbCrCore(new ReadOnlySpan<Bgra32>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (YCbCr → Bgra32)

    /// <summary>
    /// Пакетная конвертация YCbCr → Bgra32 с прямым SIMD.
    /// </summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация YCbCr → Bgra32 с явным указанием ускорителя.
    /// </summary>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        if (source.Length > destination.Length)
            throw new ArgumentException("Destination buffer is too small", nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
            {
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
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

    private static unsafe void FromYCbCrParallel(YCbCr* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);

            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Bgra32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Предвычисленная константа: HalfQ16 + (128 << 16) = 32768 + 8388608 = 8421376
            const int CbCrOffset = 8421376;

            // 8 пикселей за итерацию (32 байт BGRA → 24 байт YCbCr)
            while (count >= 8)
            {
                // Пиксель 0: BGRA порядок — B=0, G=1, R=2, A=3
                int b0 = src[0], g0 = src[1], r0 = src[2];
                dst[0] = (byte)(((CYR * r0) + (CYG * g0) + (CYB * b0) + HalfQ16) >> 16);
                dst[1] = ClampByte(((CCbR * r0) + (CCbG * g0) + (CCbB * b0) + CbCrOffset) >> 16);
                dst[2] = ClampByte(((CCrR * r0) + (CCrG * g0) + (CCrB * b0) + CbCrOffset) >> 16);

                // Пиксель 1
                int b1 = src[4], g1 = src[5], r1 = src[6];
                dst[3] = (byte)(((CYR * r1) + (CYG * g1) + (CYB * b1) + HalfQ16) >> 16);
                dst[4] = ClampByte(((CCbR * r1) + (CCbG * g1) + (CCbB * b1) + CbCrOffset) >> 16);
                dst[5] = ClampByte(((CCrR * r1) + (CCrG * g1) + (CCrB * b1) + CbCrOffset) >> 16);

                // Пиксель 2
                int b2 = src[8], g2 = src[9], r2 = src[10];
                dst[6] = (byte)(((CYR * r2) + (CYG * g2) + (CYB * b2) + HalfQ16) >> 16);
                dst[7] = ClampByte(((CCbR * r2) + (CCbG * g2) + (CCbB * b2) + CbCrOffset) >> 16);
                dst[8] = ClampByte(((CCrR * r2) + (CCrG * g2) + (CCrB * b2) + CbCrOffset) >> 16);

                // Пиксель 3
                int b3 = src[12], g3 = src[13], r3 = src[14];
                dst[9] = (byte)(((CYR * r3) + (CYG * g3) + (CYB * b3) + HalfQ16) >> 16);
                dst[10] = ClampByte(((CCbR * r3) + (CCbG * g3) + (CCbB * b3) + CbCrOffset) >> 16);
                dst[11] = ClampByte(((CCrR * r3) + (CCrG * g3) + (CCrB * b3) + CbCrOffset) >> 16);

                // Пиксель 4
                int b4 = src[16], g4 = src[17], r4 = src[18];
                dst[12] = (byte)(((CYR * r4) + (CYG * g4) + (CYB * b4) + HalfQ16) >> 16);
                dst[13] = ClampByte(((CCbR * r4) + (CCbG * g4) + (CCbB * b4) + CbCrOffset) >> 16);
                dst[14] = ClampByte(((CCrR * r4) + (CCrG * g4) + (CCrB * b4) + CbCrOffset) >> 16);

                // Пиксель 5
                int b5 = src[20], g5 = src[21], r5 = src[22];
                dst[15] = (byte)(((CYR * r5) + (CYG * g5) + (CYB * b5) + HalfQ16) >> 16);
                dst[16] = ClampByte(((CCbR * r5) + (CCbG * g5) + (CCbB * b5) + CbCrOffset) >> 16);
                dst[17] = ClampByte(((CCrR * r5) + (CCrG * g5) + (CCrB * b5) + CbCrOffset) >> 16);

                // Пиксель 6
                int b6 = src[24], g6 = src[25], r6 = src[26];
                dst[18] = (byte)(((CYR * r6) + (CYG * g6) + (CYB * b6) + HalfQ16) >> 16);
                dst[19] = ClampByte(((CCbR * r6) + (CCbG * g6) + (CCbB * b6) + CbCrOffset) >> 16);
                dst[20] = ClampByte(((CCrR * r6) + (CCrG * g6) + (CCrB * b6) + CbCrOffset) >> 16);

                // Пиксель 7
                int b7 = src[28], g7 = src[29], r7 = src[30];
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
                int b = src[0], g = src[1], r = src[2];
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
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Предвычисленные смещения для обратного преобразования (Q16)
            // R = Y + 1.402*Cr → без Cb, смещение: HalfQ16 - 128*C1402 = 32768 - 11796480 = -11763712
            // G = Y - 0.344*Cb - 0.714*Cr → смещение: HalfQ16 + 128*C0344 + 128*C0714 = 32768 + 2883584 + 5996544 = 8912896
            // B = Y + 1.772*Cb → без Cr, смещение: HalfQ16 - 128*C1772 = 32768 - 14864640 = -14831872
            const int HalfMinusROffset = -11763712;
            const int HalfPlusGOffset = 8912896;
            const int HalfMinusBOffset = -14831872;

            // 8 пикселей за итерацию (24 байт YCbCr → 32 байт BGRA)
            while (count >= 8)
            {
                // Пиксель 0
                int y0 = src[0], cb0 = src[1], cr0 = src[2];
                dst[2] = ClampByte(y0 + (((C1402 * cr0) + HalfMinusROffset) >> 16));  // R
                dst[1] = ClampByte(y0 - (((C0344 * cb0) + (C0714 * cr0) - HalfPlusGOffset) >> 16));  // G
                dst[0] = ClampByte(y0 + (((C1772 * cb0) + HalfMinusBOffset) >> 16));  // B
                dst[3] = 255;  // A

                // Пиксель 1
                int y1 = src[3], cb1 = src[4], cr1 = src[5];
                dst[6] = ClampByte(y1 + (((C1402 * cr1) + HalfMinusROffset) >> 16));
                dst[5] = ClampByte(y1 - (((C0344 * cb1) + (C0714 * cr1) - HalfPlusGOffset) >> 16));
                dst[4] = ClampByte(y1 + (((C1772 * cb1) + HalfMinusBOffset) >> 16));
                dst[7] = 255;

                // Пиксель 2
                int y2 = src[6], cb2 = src[7], cr2 = src[8];
                dst[10] = ClampByte(y2 + (((C1402 * cr2) + HalfMinusROffset) >> 16));
                dst[9] = ClampByte(y2 - (((C0344 * cb2) + (C0714 * cr2) - HalfPlusGOffset) >> 16));
                dst[8] = ClampByte(y2 + (((C1772 * cb2) + HalfMinusBOffset) >> 16));
                dst[11] = 255;

                // Пиксель 3
                int y3 = src[9], cb3 = src[10], cr3 = src[11];
                dst[14] = ClampByte(y3 + (((C1402 * cr3) + HalfMinusROffset) >> 16));
                dst[13] = ClampByte(y3 - (((C0344 * cb3) + (C0714 * cr3) - HalfPlusGOffset) >> 16));
                dst[12] = ClampByte(y3 + (((C1772 * cb3) + HalfMinusBOffset) >> 16));
                dst[15] = 255;

                // Пиксель 4
                int y4 = src[12], cb4 = src[13], cr4 = src[14];
                dst[18] = ClampByte(y4 + (((C1402 * cr4) + HalfMinusROffset) >> 16));
                dst[17] = ClampByte(y4 - (((C0344 * cb4) + (C0714 * cr4) - HalfPlusGOffset) >> 16));
                dst[16] = ClampByte(y4 + (((C1772 * cb4) + HalfMinusBOffset) >> 16));
                dst[19] = 255;

                // Пиксель 5
                int y5 = src[15], cb5 = src[16], cr5 = src[17];
                dst[22] = ClampByte(y5 + (((C1402 * cr5) + HalfMinusROffset) >> 16));
                dst[21] = ClampByte(y5 - (((C0344 * cb5) + (C0714 * cr5) - HalfPlusGOffset) >> 16));
                dst[20] = ClampByte(y5 + (((C1772 * cb5) + HalfMinusBOffset) >> 16));
                dst[23] = 255;

                // Пиксель 6
                int y6 = src[18], cb6 = src[19], cr6 = src[20];
                dst[26] = ClampByte(y6 + (((C1402 * cr6) + HalfMinusROffset) >> 16));
                dst[25] = ClampByte(y6 - (((C0344 * cb6) + (C0714 * cr6) - HalfPlusGOffset) >> 16));
                dst[24] = ClampByte(y6 + (((C1772 * cb6) + HalfMinusBOffset) >> 16));
                dst[27] = 255;

                // Пиксель 7
                int y7 = src[21], cb7 = src[22], cr7 = src[23];
                dst[30] = ClampByte(y7 + (((C1402 * cr7) + HalfMinusROffset) >> 16));
                dst[29] = ClampByte(y7 - (((C0344 * cb7) + (C0714 * cr7) - HalfPlusGOffset) >> 16));
                dst[28] = ClampByte(y7 + (((C1772 * cb7) + HalfMinusBOffset) >> 16));
                dst[31] = 255;

                src += 24; // 8 пикселей × 3 байта = 24 байта
                dst += 32; // 8 пикселей × 4 байта = 32 байта
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                int y = src[0], cb = src[1], cr = src[2];
                dst[2] = ClampByte(y + (((C1402 * cr) + HalfMinusROffset) >> 16));  // R
                dst[1] = ClampByte(y - (((C0344 * cb) + (C0714 * cr) - HalfPlusGOffset) >> 16));  // G
                dst[0] = ClampByte(y + (((C1772 * cb) + HalfMinusBOffset) >> 16));  // B
                dst[3] = 255;  // A
                src += 3;
                dst += 4;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrSse41(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы из Vectors.cs
            var cYR = Bgra32Sse41Vectors.CYR;
            var cYG = Bgra32Sse41Vectors.CYG;
            var cYB = Bgra32Sse41Vectors.CYB;
            var cCbR = Bgra32Sse41Vectors.CCbR;
            var cCbG = Bgra32Sse41Vectors.CCbG;
            var cCbB = Bgra32Sse41Vectors.CCbB;
            var cCrR = Bgra32Sse41Vectors.CCrR;
            var cCrG = Bgra32Sse41Vectors.CCrG;
            var cCrB = Bgra32Sse41Vectors.CCrB;
            var c128 = Bgra32Sse41Vectors.Offset128;
            var half = Bgra32Sse41Vectors.HalfQ16;

            // Кешированные маски извлечения компонентов
            var bMask = Bgra32Sse41Vectors.ExtractBMask;
            var gMask = Bgra32Sse41Vectors.ExtractGMask;
            var rMask = Bgra32Sse41Vectors.ExtractRMask;

            // 8 пикселей за итерацию (32 байт BGRA → 24 байт YCbCr)
            while (count >= 8)
            {
                // Загрузка 8 пикселей BGRA (32 байт = 2 × Vector128)
                var bgra0 = Sse2.LoadVector128(srcByte);
                var bgra1 = Sse2.LoadVector128(srcByte + 16);

                // Извлекаем B, G, R компоненты
                var b0 = Ssse3.Shuffle(bgra0, bMask);
                var g0 = Ssse3.Shuffle(bgra0, gMask);
                var r0 = Ssse3.Shuffle(bgra0, rMask);

                var b1 = Ssse3.Shuffle(bgra1, bMask);
                var g1 = Ssse3.Shuffle(bgra1, gMask);
                var r1 = Ssse3.Shuffle(bgra1, rMask);

                // Расширение до int32 для Q16 математики
                var rLo = Sse41.ConvertToVector128Int32(r0);
                var gLo = Sse41.ConvertToVector128Int32(g0);
                var bLo = Sse41.ConvertToVector128Int32(b0);

                var rHi = Sse41.ConvertToVector128Int32(r1);
                var gHi = Sse41.ConvertToVector128Int32(g1);
                var bHi = Sse41.ConvertToVector128Int32(b1);

                // Y = (CYR*R + CYG*G + CYB*B + Half) >> 16
                var yLo = Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cYR, rLo),
                        Sse41.MultiplyLow(cYG, gLo)),
                        Sse41.MultiplyLow(cYB, bLo)),
                        half), 16);

                var yHi = Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cYR, rHi),
                        Sse41.MultiplyLow(cYG, gHi)),
                        Sse41.MultiplyLow(cYB, bHi)),
                        half), 16);

                // Cb = ((CCbR*R + CCbG*G + CCbB*B + Half) >> 16) + 128
                var cbLo = Sse2.Add(Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cCbR, rLo),
                        Sse41.MultiplyLow(cCbG, gLo)),
                        Sse41.MultiplyLow(cCbB, bLo)),
                        half), 16), c128);

                var cbHi = Sse2.Add(Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cCbR, rHi),
                        Sse41.MultiplyLow(cCbG, gHi)),
                        Sse41.MultiplyLow(cCbB, bHi)),
                        half), 16), c128);

                // Cr = ((CCrR*R + CCrG*G + CCrB*B + Half) >> 16) + 128
                var crLo = Sse2.Add(Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cCrR, rLo),
                        Sse41.MultiplyLow(cCrG, gLo)),
                        Sse41.MultiplyLow(cCrB, bLo)),
                        half), 16), c128);

                var crHi = Sse2.Add(Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.Add(
                        Sse41.MultiplyLow(cCrR, rHi),
                        Sse41.MultiplyLow(cCrG, gHi)),
                        Sse41.MultiplyLow(cCrB, bHi)),
                        half), 16), c128);

                // Упаковка int32 → int16 → uint8
                var yPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(yLo, yHi),
                    Sse2.PackSignedSaturate(yLo, yHi));
                var cbPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(cbLo, cbHi),
                    Sse2.PackSignedSaturate(cbLo, cbHi));
                var crPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(crLo, crHi),
                    Sse2.PackSignedSaturate(crLo, crHi));

                // SIMD interleave: Y0Cb0Cr0 Y1Cb1Cr1 ... (24 байта)
                InterleaveYCbCr8(dstByte, yPacked, cbPacked, crPacked);

                srcByte += 32;
                dstByte += 24;
                count -= 8;
            }

            // Scalar остаток
            if (count > 0)
                ToYCbCrScalar(new ReadOnlySpan<Bgra32>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    /// <summary>SIMD interleave Y, Cb, Cr → YCbCr (8 пикселей, 24 байта).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr8(byte* dst, Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr)
    {
        // Кешированные маски
        var ycbMask0 = Bgra32Sse41Vectors.YCbToYCbCrShuffleMask0;
        var crMask0 = Bgra32Sse41Vectors.CrToYCbCrShuffleMask0;
        var ycbMask1 = Bgra32Sse41Vectors.YCbToYCbCrShuffleMask1;
        var crMask1 = Bgra32Sse41Vectors.CrToYCbCrShuffleMask1;

        // UnpackLow: Y0 Cb0 Y1 Cb1 Y2 Cb2 Y3 Cb3 Y4 Cb4 Y5 Cb5 Y6 Cb6 Y7 Cb7
        var ycb = Sse2.UnpackLow(y, cb);

        // Первые 16 байт (пиксели 0-4 + частично 5)
        var out0 = Sse2.Or(Ssse3.Shuffle(ycb, ycbMask0), Ssse3.Shuffle(cr, crMask0));
        out0.Store(dst);

        // Последние 8 байт (пиксели 5-7)
        var out1 = Sse2.Or(Ssse3.Shuffle(ycb, ycbMask1), Ssse3.Shuffle(cr, crMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы из Vectors.cs
            var c1402 = Bgra32Sse41Vectors.C1402;
            var c0344 = Bgra32Sse41Vectors.C0344;
            var c0714 = Bgra32Sse41Vectors.C0714;
            var c1772 = Bgra32Sse41Vectors.C1772;
            var c128 = Bgra32Sse41Vectors.Offset128;
            var half = Bgra32Sse41Vectors.HalfQ16;
            var alpha = Bgra32Sse41Vectors.Alpha255;

            // Кешированные маски деинтерливинга
            var deintY0 = Bgra32Sse41Vectors.YCbCrDeinterleaveY0;
            var deintY1 = Bgra32Sse41Vectors.YCbCrDeinterleaveY1;
            var deintCb0 = Bgra32Sse41Vectors.YCbCrDeinterleaveCb0;
            var deintCb1 = Bgra32Sse41Vectors.YCbCrDeinterleaveCb1;
            var deintCr0 = Bgra32Sse41Vectors.YCbCrDeinterleaveCr0;
            var deintCr1 = Bgra32Sse41Vectors.YCbCrDeinterleaveCr1;

            // 8 пикселей за итерацию (24 байт YCbCr → 32 байт BGRA)
            while (count >= 8)
            {
                // SIMD загрузка и деинтерливинг 24 байт YCbCr → Y, Cb, Cr
                var ycbcr0 = Sse2.LoadVector128(srcByte);        // первые 16 байт
                var ycbcr1 = Sse2.LoadScalarVector128((ulong*)(srcByte + 16)).AsByte(); // последние 8 байт

                var yVec = Sse2.Or(Ssse3.Shuffle(ycbcr0, deintY0), Ssse3.Shuffle(ycbcr1, deintY1));
                var cbVec = Sse2.Or(Ssse3.Shuffle(ycbcr0, deintCb0), Ssse3.Shuffle(ycbcr1, deintCb1));
                var crVec = Sse2.Or(Ssse3.Shuffle(ycbcr0, deintCr0), Ssse3.Shuffle(ycbcr1, deintCr1));

                // Первые 4 пикселя
                var yLo = Sse41.ConvertToVector128Int32(yVec);
                var cbLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec), c128);
                var crLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec), c128);

                // R = Y + ((C1402 * Cr + Half) >> 16)
                var rLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse41.MultiplyLow(c1402, crLo), half), 16));
                // G = Y - ((C0344 * Cb + C0714 * Cr + Half) >> 16)
                var gLo = Sse2.Subtract(yLo, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo), Sse41.MultiplyLow(c0714, crLo)), half), 16));
                // B = Y + ((C1772 * Cb + Half) >> 16)
                var bLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse41.MultiplyLow(c1772, cbLo), half), 16));

                // Следующие 4 пикселя
                var yHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec, 4));
                var cbHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec, 4)), c128);
                var crHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec, 4)), c128);

                var rHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse41.MultiplyLow(c1402, crHi), half), 16));
                var gHi = Sse2.Subtract(yHi, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi), Sse41.MultiplyLow(c0714, crHi)), half), 16));
                var bHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse41.MultiplyLow(c1772, cbHi), half), 16));

                // Упаковка в байты с насыщением
                var rPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(rLo, rHi), Sse2.PackSignedSaturate(rLo, rHi));
                var gPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(gLo, gHi), Sse2.PackSignedSaturate(gLo, gHi));
                var bPacked = Sse2.PackUnsignedSaturate(
                    Sse2.PackSignedSaturate(bLo, bHi), Sse2.PackSignedSaturate(bLo, bHi));

                // Интерливинг в BGRA порядок
                var bg = Sse2.UnpackLow(bPacked, gPacked);
                var ra = Sse2.UnpackLow(rPacked, alpha);
                var bgra0 = Sse2.UnpackLow(bg.AsInt16(), ra.AsInt16()).AsByte();
                var bgra1 = Sse2.UnpackHigh(bg.AsInt16(), ra.AsInt16()).AsByte();

                bgra0.Store(dstByte);
                bgra1.Store(dstByte + 16);

                srcByte += 24;
                dstByte += 32;
                count -= 8;
            }

            // Scalar остаток
            if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Bgra32>(dstByte, count));
        }
    }

    #endregion

    #region AVX2 Implementation (делегирование к SSE4.1)

    // ПРИМЕЧАНИЕ: AVX2 с Q16 int32 арифметикой не даёт ускорения над SSE4.1.
    // Причины:
    // 1. 8 int32 в 256-bit регистре = 2× SSE4.1 итерации (та же пропускная способность)
    // 2. Сложный pack pipeline (PackSignedSaturate + Permute4x64 + PackUnsignedSaturate)
    // 3. SSE/AVX transition penalties при GetLower()/GetUpper()
    // 4. Дополнительные Permute4x64 для исправления порядка lanes
    //
    // Результаты тестирования показали SSE4.1 = 2.87x speedup, AVX2 = 2.46x.
    // Делегирование к SSE4.1 обеспечивает стабильное ускорение ~2.9x.

    /// <summary>
    /// AVX2 конвертация Bgra32 → YCbCr. Делегирует к SSE4.1 (более эффективно для Q16 int32).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrAvx2(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination)
        => ToYCbCrSse41(source, destination);

    /// <summary>
    /// AVX2 конвертация YCbCr → Bgra32. Делегирует к SSE4.1 (более эффективно для Q16 int32).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination)
        => FromYCbCrSse41(source, destination);

    #endregion

    #region Unused SIMD Helpers (16 pixels)

    // Эти хелперы больше не используются, но оставлены для возможной будущей оптимизации

    /// <summary>Извлекает B, G, R из 16 BGRA пикселей (64 байта). [UNUSED]</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveBgra16Unused(
        byte* src,
        Vector128<byte> bMask, Vector128<byte> gMask, Vector128<byte> rMask,
        out Vector128<byte> b, out Vector128<byte> g, out Vector128<byte> r)
    {
        // Загружаем 4 блока по 16 байт (по 4 пикселя каждый)
        var bgra0 = Sse2.LoadVector128(src);
        var bgra1 = Sse2.LoadVector128(src + 16);
        var bgra2 = Sse2.LoadVector128(src + 32);
        var bgra3 = Sse2.LoadVector128(src + 48);

        // Извлекаем B, G, R из каждого блока (4 байта на блок)
        var b0 = Ssse3.Shuffle(bgra0, bMask);
        var b1 = Ssse3.Shuffle(bgra1, bMask);
        var b2 = Ssse3.Shuffle(bgra2, bMask);
        var b3 = Ssse3.Shuffle(bgra3, bMask);

        var g0 = Ssse3.Shuffle(bgra0, gMask);
        var g1 = Ssse3.Shuffle(bgra1, gMask);
        var g2 = Ssse3.Shuffle(bgra2, gMask);
        var g3 = Ssse3.Shuffle(bgra3, gMask);

        var r0 = Ssse3.Shuffle(bgra0, rMask);
        var r1 = Ssse3.Shuffle(bgra1, rMask);
        var r2 = Ssse3.Shuffle(bgra2, rMask);
        var r3 = Ssse3.Shuffle(bgra3, rMask);

        // Объединяем: 4+4+4+4 = 16 байт
        b = Sse2.Or(Sse2.Or(b0, Sse2.ShiftLeftLogical128BitLane(b1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(b2, 8), Sse2.ShiftLeftLogical128BitLane(b3, 12)));
        g = Sse2.Or(Sse2.Or(g0, Sse2.ShiftLeftLogical128BitLane(g1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(g2, 8), Sse2.ShiftLeftLogical128BitLane(g3, 12)));
        r = Sse2.Or(Sse2.Or(r0, Sse2.ShiftLeftLogical128BitLane(r1, 4)),
                    Sse2.Or(Sse2.ShiftLeftLogical128BitLane(r2, 8), Sse2.ShiftLeftLogical128BitLane(r3, 12)));
    }

    /// <summary>Deinterleave YCbCr → Y, Cb, Cr (16 пикселей из 48 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr16(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        DeinterleaveYCbCr8(src, out var y0, out var cb0, out var cr0);
        DeinterleaveYCbCr8(src + 24, out var y1, out var cb1, out var cr1);

        y = Sse2.Or(y0, Sse2.ShiftLeftLogical128BitLane(y1, 8));
        cb = Sse2.Or(cb0, Sse2.ShiftLeftLogical128BitLane(cb1, 8));
        cr = Sse2.Or(cr0, Sse2.ShiftLeftLogical128BitLane(cr1, 8));
    }

    /// <summary>Deinterleave YCbCr → Y, Cb, Cr (8 пикселей из 24 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr8(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        var ycbcr0 = Sse2.LoadVector128(src);
        var ycbcr1 = Sse2.LoadScalarVector128((ulong*)(src + 16)).AsByte();

        y = Sse2.Or(
            Ssse3.Shuffle(ycbcr0, Bgra32Sse41Vectors.YCbCrDeinterleaveY0),
            Ssse3.Shuffle(ycbcr1, Bgra32Sse41Vectors.YCbCrDeinterleaveY1));
        cb = Sse2.Or(
            Ssse3.Shuffle(ycbcr0, Bgra32Sse41Vectors.YCbCrDeinterleaveCb0),
            Ssse3.Shuffle(ycbcr1, Bgra32Sse41Vectors.YCbCrDeinterleaveCb1));
        cr = Sse2.Or(
            Ssse3.Shuffle(ycbcr0, Bgra32Sse41Vectors.YCbCrDeinterleaveCr0),
            Ssse3.Shuffle(ycbcr1, Bgra32Sse41Vectors.YCbCrDeinterleaveCr1));
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

    /// <summary>Interleave B, G, R, A → BGRA (16 пикселей, 64 байта).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveBgra16(byte* dst, Vector128<byte> b, Vector128<byte> g, Vector128<byte> r, Vector128<byte> a)
    {
        // Первые 8 пикселей
        var bg0 = Sse2.UnpackLow(b, g);     // B0G0 B1G1 B2G2 B3G3 B4G4 B5G5 B6G6 B7G7
        var ra0 = Sse2.UnpackLow(r, a);     // R0A0 R1A1 R2A2 R3A3 R4A4 R5A5 R6A6 R7A7
        var bgra0 = Sse2.UnpackLow(bg0.AsInt16(), ra0.AsInt16()).AsByte();   // пиксели 0-3
        var bgra1 = Sse2.UnpackHigh(bg0.AsInt16(), ra0.AsInt16()).AsByte();  // пиксели 4-7

        bgra0.Store(dst);
        bgra1.Store(dst + 16);

        // Вторые 8 пикселей
        var bg1 = Sse2.UnpackHigh(b, g);
        var ra1 = Sse2.UnpackHigh(r, a);
        var bgra2 = Sse2.UnpackLow(bg1.AsInt16(), ra1.AsInt16()).AsByte();
        var bgra3 = Sse2.UnpackHigh(bg1.AsInt16(), ra1.AsInt16()).AsByte();

        bgra2.Store(dst + 32);
        bgra3.Store(dst + 48);
    }

    #endregion

    #region Conversion Operators (YCbCr)

    /// <summary>Явная конвертация из YCbCr в Bgra32.</summary>
    public static explicit operator Bgra32(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явная конвертация из Bgra32 в YCbCr.</summary>
    public static explicit operator YCbCr(Bgra32 bgra) => bgra.ToYCbCr();

    #endregion
}
