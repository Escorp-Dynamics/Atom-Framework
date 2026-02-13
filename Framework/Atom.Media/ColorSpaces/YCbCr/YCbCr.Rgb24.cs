#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация RGB24 → YCbCr.
/// </summary>
public readonly partial struct YCbCr
{
    #region ITU-R BT.601 Constants (RGB24 → YCbCr)

    private const int CYR = 19595;    // 0.299 * 65536
    private const int CYG = 38470;    // 0.587 * 65536
    private const int CYB = 7471;     // 0.114 * 65536
    private const int CCbR = -11056;  // -0.168736 * 65536
    private const int CCbG = -21712;  // -0.331264 * 65536
    private const int CCbB = 32768;   // 0.5 * 65536
    private const int CCrR = 32768;   // 0.5 * 65536
    private const int CCrG = -27440;  // -0.418688 * 65536
    private const int CCrB = -5328;   // -0.081312 * 65536
    private const int Half = 32768;   // 0.5 * 65536

    #endregion

    #region Single Pixel Conversion (RGB24 → YCbCr)

    /// <summary>Конвертирует RGB24 в YCbCr (single pixel).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromRgb24(Rgb24 rgb)
    {
        var r = (int)rgb.R;
        var g = (int)rgb.G;
        var b = (int)rgb.B;

        var yCalc = ((CYR * r) + (CYG * g) + (CYB * b) + Half) >> 16;
        var cbCalc = (((CCbR * r) + (CCbG * g) + (CCbB * b) + Half) >> 16) + 128;
        var crCalc = (((CCrR * r) + (CCrG * g) + (CCrB * b) + Half) >> 16) + 128;

        return new((byte)Clamp(yCalc), (byte)Clamp(cbCalc), (byte)Clamp(crCalc));
    }

    #endregion

    #region Batch Conversion (RGB24 → YCbCr)

    /// <summary>
    /// Реализованные ускорители для конвертации RGB24 → YCbCr.
    /// </summary>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    /// <summary>
    /// Пакетная конвертация RGB24 → YCbCr с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация RGB24 → YCbCr с явным указанием ускорителя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FromRgb24(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgb24* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
            {
                FromRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    /// <summary>Однопоточная SIMD конвертация RGB24 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 32:
                FromRgb24Avx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 64:
                FromRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromRgb24Sse41(source, destination);
                break;
            default:
                FromRgb24Scalar(source, destination);
                break;
        }
    }

    /// <summary>Параллельная конвертация через Parallel.For.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Parallel(Rgb24* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);

        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);

            FromRgb24Core(new ReadOnlySpan<Rgb24>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation (RGB24 → YCbCr)

    // Предвычисленные константы для RGB24 → YCbCr
    // Half128Q16Rgb = Half + (128 << 16) = 32768 + 8388608 = 8421376
    private const int Half128Q16Rgb = 8421376;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Scalar(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // 8-pixel unrolling
            while (i + 8 <= count)
            {
                var r0 = (int)src[0]; var g0 = (int)src[1]; var b0 = (int)src[2];
                var r1 = (int)src[3]; var g1 = (int)src[4]; var b1 = (int)src[5];
                var r2 = (int)src[6]; var g2 = (int)src[7]; var b2 = (int)src[8];
                var r3 = (int)src[9]; var g3 = (int)src[10]; var b3 = (int)src[11];
                var r4 = (int)src[12]; var g4 = (int)src[13]; var b4 = (int)src[14];
                var r5 = (int)src[15]; var g5 = (int)src[16]; var b5 = (int)src[17];
                var r6 = (int)src[18]; var g6 = (int)src[19]; var b6 = (int)src[20];
                var r7 = (int)src[21]; var g7 = (int)src[22]; var b7 = (int)src[23];

                dst[0] = (byte)(((CYR * r0) + (CYG * g0) + (CYB * b0) + Half) >> 16);
                dst[1] = (byte)Math.Clamp(((CCbR * r0) + (CCbG * g0) + (CCbB * b0) + Half128Q16Rgb) >> 16, 0, 255);
                dst[2] = (byte)Math.Clamp(((CCrR * r0) + (CCrG * g0) + (CCrB * b0) + Half128Q16Rgb) >> 16, 0, 255);

                dst[3] = (byte)(((CYR * r1) + (CYG * g1) + (CYB * b1) + Half) >> 16);
                dst[4] = (byte)Math.Clamp(((CCbR * r1) + (CCbG * g1) + (CCbB * b1) + Half128Q16Rgb) >> 16, 0, 255);
                dst[5] = (byte)Math.Clamp(((CCrR * r1) + (CCrG * g1) + (CCrB * b1) + Half128Q16Rgb) >> 16, 0, 255);

                dst[6] = (byte)(((CYR * r2) + (CYG * g2) + (CYB * b2) + Half) >> 16);
                dst[7] = (byte)Math.Clamp(((CCbR * r2) + (CCbG * g2) + (CCbB * b2) + Half128Q16Rgb) >> 16, 0, 255);
                dst[8] = (byte)Math.Clamp(((CCrR * r2) + (CCrG * g2) + (CCrB * b2) + Half128Q16Rgb) >> 16, 0, 255);

                dst[9] = (byte)(((CYR * r3) + (CYG * g3) + (CYB * b3) + Half) >> 16);
                dst[10] = (byte)Math.Clamp(((CCbR * r3) + (CCbG * g3) + (CCbB * b3) + Half128Q16Rgb) >> 16, 0, 255);
                dst[11] = (byte)Math.Clamp(((CCrR * r3) + (CCrG * g3) + (CCrB * b3) + Half128Q16Rgb) >> 16, 0, 255);

                dst[12] = (byte)(((CYR * r4) + (CYG * g4) + (CYB * b4) + Half) >> 16);
                dst[13] = (byte)Math.Clamp(((CCbR * r4) + (CCbG * g4) + (CCbB * b4) + Half128Q16Rgb) >> 16, 0, 255);
                dst[14] = (byte)Math.Clamp(((CCrR * r4) + (CCrG * g4) + (CCrB * b4) + Half128Q16Rgb) >> 16, 0, 255);

                dst[15] = (byte)(((CYR * r5) + (CYG * g5) + (CYB * b5) + Half) >> 16);
                dst[16] = (byte)Math.Clamp(((CCbR * r5) + (CCbG * g5) + (CCbB * b5) + Half128Q16Rgb) >> 16, 0, 255);
                dst[17] = (byte)Math.Clamp(((CCrR * r5) + (CCrG * g5) + (CCrB * b5) + Half128Q16Rgb) >> 16, 0, 255);

                dst[18] = (byte)(((CYR * r6) + (CYG * g6) + (CYB * b6) + Half) >> 16);
                dst[19] = (byte)Math.Clamp(((CCbR * r6) + (CCbG * g6) + (CCbB * b6) + Half128Q16Rgb) >> 16, 0, 255);
                dst[20] = (byte)Math.Clamp(((CCrR * r6) + (CCrG * g6) + (CCrB * b6) + Half128Q16Rgb) >> 16, 0, 255);

                dst[21] = (byte)(((CYR * r7) + (CYG * g7) + (CYB * b7) + Half) >> 16);
                dst[22] = (byte)Math.Clamp(((CCbR * r7) + (CCbG * g7) + (CCbB * b7) + Half128Q16Rgb) >> 16, 0, 255);
                dst[23] = (byte)Math.Clamp(((CCrR * r7) + (CCrG * g7) + (CCrB * b7) + Half128Q16Rgb) >> 16, 0, 255);

                src += 24;
                dst += 24;
                i += 8;
            }

            // Остаток
            while (i < count)
            {
                var r = (int)src[0]; var g = (int)src[1]; var b = (int)src[2];
                dst[0] = (byte)(((CYR * r) + (CYG * g) + (CYB * b) + Half) >> 16);
                dst[1] = (byte)Math.Clamp(((CCbR * r) + (CCbG * g) + (CCbB * b) + Half128Q16Rgb) >> 16, 0, 255);
                dst[2] = (byte)Math.Clamp(((CCrR * r) + (CCrG * g) + (CCrB * b) + Half128Q16Rgb) >> 16, 0, 255);
                src += 3;
                dst += 3;
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (RGB24 → YCbCr, Q16 int32 for precision)

    /// <summary>
    /// AVX2 int32 версия — 32 пикселя за итерацию.
    /// Использует Q16 fixed-point с единым округлением для точности как у scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx2(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Q16 константы (как scalar)
            var cYR = YCbCrAvx2Vectors.CYR;       // 0.299 × 65536 = 19595
            var cYG = YCbCrAvx2Vectors.CYG;       // 0.587 × 65536 = 38470
            var cYB = YCbCrAvx2Vectors.CYB;       // 0.114 × 65536 = 7471
            var cCbR = YCbCrAvx2Vectors.CCbR;     // -0.168736 × 65536 = -11056
            var cCbG = YCbCrAvx2Vectors.CCbG;     // -0.331264 × 65536 = -21712
            var cCbB = YCbCrAvx2Vectors.CCbB;     // 0.5 × 65536 = 32768
            var cCrR = YCbCrAvx2Vectors.CCrR;     // 0.5 × 65536 = 32768
            var cCrG = YCbCrAvx2Vectors.CCrG;     // -0.418688 × 65536 = -27440
            var cCrB = YCbCrAvx2Vectors.CCrB;     // -0.081312 × 65536 = -5328
            var c128 = YCbCrAvx2Vectors.C128;
            var half = YCbCrAvx2Vectors.Half;     // 32768 для округления

            // 32 пикселя за итерацию (4 блока по 8 пикселей)
            while (count >= 32)
            {
                Sse.Prefetch0(srcByte + 384);

                // Блок 0-1: первые 16 пикселей
                DeinterleaveRgb16(srcByte, out var rBytes01, out var gBytes01, out var bBytes01);

                // Пиксели 0-7 (первые 8 байт из rBytes01)
                var r0 = Avx2.ConvertToVector256Int32(rBytes01);
                var g0 = Avx2.ConvertToVector256Int32(gBytes01);
                var b0 = Avx2.ConvertToVector256Int32(bBytes01);

                // Y = (CYR*R + CYG*G + CYB*B + Half) >> 16
                var y0 = Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cYR, r0), Avx2.MultiplyLow(cYG, g0)), Avx2.MultiplyLow(cYB, b0)), half), 16);
                // Cb = ((CCbR*R + CCbG*G + CCbB*B + Half) >> 16) + 128
                var cb0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCbR, r0), Avx2.MultiplyLow(cCbG, g0)), Avx2.MultiplyLow(cCbB, b0)), half), 16), c128);
                // Cr = ((CCrR*R + CCrG*G + CCrB*B + Half) >> 16) + 128
                var cr0 = Avx2.Add(Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cCrR, r0), Avx2.MultiplyLow(cCrG, g0)), Avx2.MultiplyLow(cCrB, b0)), half), 16), c128);

                // Пиксели 8-15 (верхние 8 байт)
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
                DeinterleaveRgb16(srcByte + 48, out var rBytes23, out var gBytes23, out var bBytes23);

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

                // Interleave и запись (32 пикселя = 96 байт)
                InterleaveYCbCr16(dstByte, yBytes.GetLower(), cbBytes.GetLower(), crBytes.GetLower());
                InterleaveYCbCr16(dstByte + 48, yBytes.GetUpper(), cbBytes.GetUpper(), crBytes.GetUpper());

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток через scalar
            if (count > 0)
                FromRgb24Scalar(new ReadOnlySpan<Rgb24>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    #endregion

    #region AVX-512 Implementation (RGB24 → YCbCr, 64 pixels per iteration with 2x unroll, int16 Q15)

    /// <summary>
    /// AVX-512 версия — 64 пикселя за итерацию (2x unroll).
    /// Использует Q15 fixed-point с MultiplyHighRoundScale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx512(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы Q15 (коэффициенты × 32768)
            var cYR = YCbCrAvx512Vectors.CYR;
            var cYG = YCbCrAvx512Vectors.CYG;
            var cYB = YCbCrAvx512Vectors.CYB;
            var cCbR = YCbCrAvx512Vectors.CCbR;
            var cCbG = YCbCrAvx512Vectors.CCbG;
            var cCbB = YCbCrAvx512Vectors.CCbB;
            var cCrR = YCbCrAvx512Vectors.CCrR;
            var cCrG = YCbCrAvx512Vectors.CCrG;
            var cCrB = YCbCrAvx512Vectors.CCrB;
            var c128 = YCbCrAvx512Vectors.C128;

            // 2x unroll: 64 пикселя за итерацию для лучшего ILP
            while (count >= 64)
            {
                // Prefetch ~1KB вперёд
                Sse.Prefetch0(srcByte + 768);
                Sse.Prefetch0(srcByte + 832);
                Sse.Prefetch0(srcByte + 896);

                // === Блок 0: пиксели 0-31 ===
                DeinterleaveRgb32(srcByte, out var rBytes0, out var gBytes0, out var bBytes0);
                var r0 = Avx512BW.ConvertToVector512Int16(rBytes0);
                var g0 = Avx512BW.ConvertToVector512Int16(gBytes0);
                var b0 = Avx512BW.ConvertToVector512Int16(bBytes0);

                // === Блок 1: пиксели 32-63 ===
                DeinterleaveRgb32(srcByte + 96, out var rBytes1, out var gBytes1, out var bBytes1);
                var r1 = Avx512BW.ConvertToVector512Int16(rBytes1);
                var g1 = Avx512BW.ConvertToVector512Int16(gBytes1);
                var b1 = Avx512BW.ConvertToVector512Int16(bBytes1);

                // Вычисление Y, Cb, Cr для блока 0
                var y0 = Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cYR, r0), Avx512BW.MultiplyHighRoundScale(cYG, g0)), Avx512BW.MultiplyHighRoundScale(cYB, b0));
                var cb0 = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCbR, r0), Avx512BW.MultiplyHighRoundScale(cCbG, g0)), Avx512BW.MultiplyHighRoundScale(cCbB, b0)), c128);
                var cr0 = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCrR, r0), Avx512BW.MultiplyHighRoundScale(cCrG, g0)), Avx512BW.MultiplyHighRoundScale(cCrB, b0)), c128);

                // Вычисление Y, Cb, Cr для блока 1
                var y1 = Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cYR, r1), Avx512BW.MultiplyHighRoundScale(cYG, g1)), Avx512BW.MultiplyHighRoundScale(cYB, b1));
                var cb1 = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCbR, r1), Avx512BW.MultiplyHighRoundScale(cCbG, g1)), Avx512BW.MultiplyHighRoundScale(cCbB, b1)), c128);
                var cr1 = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCrR, r1), Avx512BW.MultiplyHighRoundScale(cCrG, g1)), Avx512BW.MultiplyHighRoundScale(cCrB, b1)), c128);

                // Pack и запись блока 0
                var yOut0 = PackToBytesAvx512(y0);
                var cbOut0 = PackToBytesAvx512(cb0);
                var crOut0 = PackToBytesAvx512(cr0);
                InterleaveYCbCr32(dstByte, yOut0, cbOut0, crOut0);

                // Pack и запись блока 1
                var yOut1 = PackToBytesAvx512(y1);
                var cbOut1 = PackToBytesAvx512(cb1);
                var crOut1 = PackToBytesAvx512(cr1);
                InterleaveYCbCr32(dstByte + 96, yOut1, cbOut1, crOut1);

                srcByte += 192;  // 64 × 3 байта
                dstByte += 192;
                count -= 64;
            }

            // 32 пикселя (без unroll)
            while (count >= 32)
            {
                DeinterleaveRgb32(srcByte, out var rBytes, out var gBytes, out var bBytes);
                var r = Avx512BW.ConvertToVector512Int16(rBytes);
                var g = Avx512BW.ConvertToVector512Int16(gBytes);
                var b = Avx512BW.ConvertToVector512Int16(bBytes);

                var y = Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cYR, r), Avx512BW.MultiplyHighRoundScale(cYG, g)), Avx512BW.MultiplyHighRoundScale(cYB, b));
                var cb = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCbR, r), Avx512BW.MultiplyHighRoundScale(cCbG, g)), Avx512BW.MultiplyHighRoundScale(cCbB, b)), c128);
                var cr = Avx512BW.Add(Avx512BW.Add(Avx512BW.Add(Avx512BW.MultiplyHighRoundScale(cCrR, r), Avx512BW.MultiplyHighRoundScale(cCrG, g)), Avx512BW.MultiplyHighRoundScale(cCrB, b)), c128);

                InterleaveYCbCr32(dstByte, PackToBytesAvx512(y), PackToBytesAvx512(cb), PackToBytesAvx512(cr));

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток обрабатываем AVX2
            if (count > 0)
                FromRgb24Avx2(new ReadOnlySpan<Rgb24>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    /// <summary>Упаковка Vector512&lt;short&gt; → Vector256&lt;byte&gt; с насыщением и перестановкой.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> PackToBytesAvx512(Vector512<short> values)
    {
        // PackUnsignedSaturate: [A0..A15 | A16..A31] → [A0..A15, A16..A31] (interleaved by lanes)
        var packed = Avx512BW.PackUnsignedSaturate(values, values);

        // Результат: [0-15 | 0-15 | 16-31 | 16-31] — нужно переставить в [0-15 | 16-31 | ...]
        // PermuteVar8x64: переставляем 64-битные блоки: 0,2,4,6,1,3,5,7
        var permuted = Avx512F.PermuteVar8x64(packed.AsInt64(), Vector512.Create(0L, 2L, 4L, 6L, 1L, 3L, 5L, 7L)).AsByte();

        return permuted.GetLower();
    }

    /// <summary>Деинтерливинг RGB24: 96 байт → 32 R, 32 G, 32 B.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb32(byte* src, out Vector256<byte> r, out Vector256<byte> g, out Vector256<byte> b)
    {
        // Используем AVX2 шаффл для деинтерливинга (2x по 48 байт = 16 пикселей)
        DeinterleaveRgb16(src, out var rVec0, out var gVec0, out var bVec0);
        DeinterleaveRgb16(src + 48, out var rVec1, out var gVec1, out var bVec1);

        // Объединяем два Vector128 в Vector256
        r = Vector256.Create(rVec0, rVec1);
        g = Vector256.Create(gVec0, gVec1);
        b = Vector256.Create(bVec0, bVec1);
    }

    /// <summary>Интерливинг YCbCr: 32 Y, 32 Cb, 32 Cr → 96 байт.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr32(byte* dst, Vector256<byte> y, Vector256<byte> cb, Vector256<byte> cr)
    {
        // Используем AVX2 для интерливинга (2x по 16 пикселей)
        InterleaveYCbCr16(dst, y.GetLower(), cb.GetLower(), cr.GetLower());
        InterleaveYCbCr16(dst + 48, y.GetUpper(), cb.GetUpper(), cr.GetUpper());
    }

    #endregion

    #region SSE4.1 Implementation (RGB24 → YCbCr, 8 pixels per iteration)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Sse41(ReadOnlySpan<Rgb24> source, Span<YCbCr> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

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

            while (count >= 8)
            {
                DeinterleaveRgb8(srcByte, out var rVec, out var gVec, out var bVec);

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
                FromRgb24Scalar(new ReadOnlySpan<Rgb24>(srcByte, count), new Span<YCbCr>(dstByte, count));
        }
    }

    #endregion

    #region Deinterleave/Interleave Helpers (RGB24)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb8(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        var bytes0 = Vector128.Load(src);
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();

        r = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleR0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleR1));
        g = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleG0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleG1));
        b = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleB0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleB1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb16(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        DeinterleaveRgb8(src, out var r0, out var g0, out var b0);
        DeinterleaveRgb8(src + 24, out var r1, out var g1, out var b1);

        // Используем UnpackLow вместо GetElement + Create для избежания переходов через GPR
        r = Sse2.UnpackLow(r0.AsUInt64(), r1.AsUInt64()).AsByte();
        g = Sse2.UnpackLow(g0.AsUInt64(), g1.AsUInt64()).AsByte();
        b = Sse2.UnpackLow(b0.AsUInt64(), b1.AsUInt64()).AsByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr8(byte* dst, Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr)
    {
        // Y0Cb0Y1Cb1Y2Cb2Y3Cb3Y4Cb4Y5Cb5Y6Cb6Y7Cb7
        var ycb = Sse2.UnpackLow(y, cb);

        // Первые 16 байт: Y0Cb0Cr0 Y1Cb1Cr1 Y2Cb2Cr2 Y3Cb3Cr3 Y4Cb4Cr4 Y5_
        var out0 = Sse2.Or(
            Ssse3.Shuffle(ycb, YCbCrSse41Vectors.YCbToYCbCrShuffleMask0),
            Ssse3.Shuffle(cr, YCbCrSse41Vectors.CrToYCbCrShuffleMask0));
        out0.Store(dst);

        // Оставшиеся 8 байт: _Cb5Cr5 Y6Cb6Cr6 Y7Cb7Cr7
        var out1 = Sse2.Or(
            Ssse3.Shuffle(ycb, YCbCrSse41Vectors.YCbToYCbCrShuffleMask1),
            Ssse3.Shuffle(cr, YCbCrSse41Vectors.CrToYCbCrShuffleMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveYCbCr16(byte* dst, Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr)
    {
        InterleaveYCbCr8(dst, y, cb, cr);
        InterleaveYCbCr8(dst + 24,
            Sse2.ShiftRightLogical128BitLane(y, 8),
            Sse2.ShiftRightLogical128BitLane(cb, 8),
            Sse2.ShiftRightLogical128BitLane(cr, 8));
    }

    #endregion

    #region Conversion Operator (RGB24 → YCbCr)

    /// <summary>Явная конвертация из RGB24 в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Rgb24 rgb) => FromRgb24(rgb);

    #endregion
}
