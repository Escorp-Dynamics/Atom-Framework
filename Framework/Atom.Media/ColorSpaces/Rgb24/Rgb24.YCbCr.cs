#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr → RGB24.
/// </summary>
public readonly partial struct Rgb24
{
    #region ITU-R BT.601 Constants (YCbCr → RGB24)

    private const int C1402 = 91881;   // 1.402 * 65536
    private const int C0344 = 22554;   // 0.344136 * 65536
    private const int C0714 = 46802;   // 0.714136 * 65536
    private const int C1772 = 116130;  // 1.772 * 65536
    private const int Half = 32768;    // 0.5 * 65536

    #endregion



    #region Single Pixel Conversion (YCbCr → RGB24)

    /// <summary>Конвертирует YCbCr в RGB24 (single pixel).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromYCbCr(YCbCr ycbcr)
    {
        var y = (int)ycbcr.Y;
        var cb = ycbcr.Cb - 128;
        var cr = ycbcr.Cr - 128;

        var rCalc = y + (((C1402 * cr) + Half) >> 16);
        var gCalc = y - (((C0344 * cb) + (C0714 * cr) + Half) >> 16);
        var bCalc = y + (((C1772 * cb) + Half) >> 16);

        return new((byte)Clamp(rCalc), (byte)Clamp(gCalc), (byte)Clamp(bCalc));
    }

    #endregion

    #region Batch Conversion (YCbCr → RGB24)

    /// <summary>
    /// Реализованные ускорители для конвертации YCbCr ↔ RGB24.
    /// </summary>
    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    /// <summary>
    /// Пакетная конвертация YCbCr → RGB24 с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация YCbCr → RGB24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер YCbCr.</param>
    /// <param name="destination">Целевой буфер RGB24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        // Параллельная обработка для буферов >= 1024 пикселей
        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Rgb24* dstPtr = destination)
            {
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        // Однопоточная SIMD обработка
        FromYCbCrCore(source, destination, selected);
    }

    /// <summary>Однопоточная SIMD конвертация YCbCr → RGB24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 32:
                FromYCbCrAvx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 64:
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

    /// <summary>Параллельная конвертация через Parallel.For с выбранным ускорителем.</summary>
    private static unsafe void FromYCbCrParallel(YCbCr* source, Rgb24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);

        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);

            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Rgb24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation (YCbCr → RGB24)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // 8-pixel unrolling
            while (i + 8 <= count)
            {
                var y0 = (int)src[0]; var cb0 = (int)src[1] - 128; var cr0 = (int)src[2] - 128;
                var y1 = (int)src[3]; var cb1 = (int)src[4] - 128; var cr1 = (int)src[5] - 128;
                var y2 = (int)src[6]; var cb2 = (int)src[7] - 128; var cr2 = (int)src[8] - 128;
                var y3 = (int)src[9]; var cb3 = (int)src[10] - 128; var cr3 = (int)src[11] - 128;
                var y4 = (int)src[12]; var cb4 = (int)src[13] - 128; var cr4 = (int)src[14] - 128;
                var y5 = (int)src[15]; var cb5 = (int)src[16] - 128; var cr5 = (int)src[17] - 128;
                var y6 = (int)src[18]; var cb6 = (int)src[19] - 128; var cr6 = (int)src[20] - 128;
                var y7 = (int)src[21]; var cb7 = (int)src[22] - 128; var cr7 = (int)src[23] - 128;

                dst[0] = (byte)Math.Clamp(y0 + (((C1402 * cr0) + Half) >> 16), 0, 255);
                dst[1] = (byte)Math.Clamp(y0 - (((C0344 * cb0) + (C0714 * cr0) + Half) >> 16), 0, 255);
                dst[2] = (byte)Math.Clamp(y0 + (((C1772 * cb0) + Half) >> 16), 0, 255);

                dst[3] = (byte)Math.Clamp(y1 + (((C1402 * cr1) + Half) >> 16), 0, 255);
                dst[4] = (byte)Math.Clamp(y1 - (((C0344 * cb1) + (C0714 * cr1) + Half) >> 16), 0, 255);
                dst[5] = (byte)Math.Clamp(y1 + (((C1772 * cb1) + Half) >> 16), 0, 255);

                dst[6] = (byte)Math.Clamp(y2 + (((C1402 * cr2) + Half) >> 16), 0, 255);
                dst[7] = (byte)Math.Clamp(y2 - (((C0344 * cb2) + (C0714 * cr2) + Half) >> 16), 0, 255);
                dst[8] = (byte)Math.Clamp(y2 + (((C1772 * cb2) + Half) >> 16), 0, 255);

                dst[9] = (byte)Math.Clamp(y3 + (((C1402 * cr3) + Half) >> 16), 0, 255);
                dst[10] = (byte)Math.Clamp(y3 - (((C0344 * cb3) + (C0714 * cr3) + Half) >> 16), 0, 255);
                dst[11] = (byte)Math.Clamp(y3 + (((C1772 * cb3) + Half) >> 16), 0, 255);

                dst[12] = (byte)Math.Clamp(y4 + (((C1402 * cr4) + Half) >> 16), 0, 255);
                dst[13] = (byte)Math.Clamp(y4 - (((C0344 * cb4) + (C0714 * cr4) + Half) >> 16), 0, 255);
                dst[14] = (byte)Math.Clamp(y4 + (((C1772 * cb4) + Half) >> 16), 0, 255);

                dst[15] = (byte)Math.Clamp(y5 + (((C1402 * cr5) + Half) >> 16), 0, 255);
                dst[16] = (byte)Math.Clamp(y5 - (((C0344 * cb5) + (C0714 * cr5) + Half) >> 16), 0, 255);
                dst[17] = (byte)Math.Clamp(y5 + (((C1772 * cb5) + Half) >> 16), 0, 255);

                dst[18] = (byte)Math.Clamp(y6 + (((C1402 * cr6) + Half) >> 16), 0, 255);
                dst[19] = (byte)Math.Clamp(y6 - (((C0344 * cb6) + (C0714 * cr6) + Half) >> 16), 0, 255);
                dst[20] = (byte)Math.Clamp(y6 + (((C1772 * cb6) + Half) >> 16), 0, 255);

                dst[21] = (byte)Math.Clamp(y7 + (((C1402 * cr7) + Half) >> 16), 0, 255);
                dst[22] = (byte)Math.Clamp(y7 - (((C0344 * cb7) + (C0714 * cr7) + Half) >> 16), 0, 255);
                dst[23] = (byte)Math.Clamp(y7 + (((C1772 * cb7) + Half) >> 16), 0, 255);

                src += 24;
                dst += 24;
                i += 8;
            }

            // Остаток
            while (i < count)
            {
                var y = (int)src[0]; var cb = (int)src[1] - 128; var cr = (int)src[2] - 128;
                dst[0] = (byte)Math.Clamp(y + (((C1402 * cr) + Half) >> 16), 0, 255);
                dst[1] = (byte)Math.Clamp(y - (((C0344 * cb) + (C0714 * cr) + Half) >> 16), 0, 255);
                dst[2] = (byte)Math.Clamp(y + (((C1772 * cb) + Half) >> 16), 0, 255);
                src += 3;
                dst += 3;
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr → RGB24, Q16 int32 for precision)

    /// <summary>
    /// AVX2 int32 версия — 32 пикселя за итерацию.
    /// Использует Q16 fixed-point с единым округлением для точности как у scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
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

                // R = Y + ((C1402 * Cr + Half) >> 16)
                var r0 = Avx2.Add(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.MultiplyLow(c1402, cr0), half), 16));
                // G = Y - ((C0344 * Cb + C0714 * Cr + Half) >> 16)
                var g0 = Avx2.Subtract(y0, Avx2.ShiftRightArithmetic(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(c0344, cb0), Avx2.MultiplyLow(c0714, cr0)), half), 16));
                // B = Y + ((C1772 * Cb + Half) >> 16)
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

                // Interleave и запись (32 пикселя = 96 байт)
                InterleaveRgb16(dstByte, rBytes.GetLower(), gBytes.GetLower(), bBytes.GetLower());
                InterleaveRgb16(dstByte + 48, rBytes.GetUpper(), gBytes.GetUpper(), bBytes.GetUpper());

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток через scalar
            if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgb24>(dstByte, count));
        }
    }

    #endregion

    #region AVX-512 Implementation (YCbCr → RGB24, 32 pixels per iteration, int16 Q15)

    /// <summary>
    /// AVX-512 int16 версия — 32 пикселя за итерацию.
    /// Использует Q15 fixed-point с разделением коэффициентов > 1.0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx512(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var srcByte = (byte*)srcPtr;
            var dstByte = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы Q15
            var c0402 = YCbCrAvx512Vectors.C0402;
            var c0344 = YCbCrAvx512Vectors.C0344;
            var c0714 = YCbCrAvx512Vectors.C0714;
            var c0772 = YCbCrAvx512Vectors.C0772;
            var c128 = YCbCrAvx512Vectors.C128;

            // 32 пикселя за итерацию
            while (count >= 32)
            {
                // Загрузка 96 байт (32 пикселя × 3)
                DeinterleaveYCbCr32(srcByte, out var yBytes, out var cbBytes, out var crBytes);

                // Zero-extend byte→short
                var y = Avx512BW.ConvertToVector512Int16(yBytes);
                var cb = Avx512BW.Subtract(Avx512BW.ConvertToVector512Int16(cbBytes), c128);
                var cr = Avx512BW.Subtract(Avx512BW.ConvertToVector512Int16(crBytes), c128);

                // R = Y + Cr + round(0.402 × Cr)
                var r = Avx512BW.Add(Avx512BW.Add(y, cr), Avx512BW.MultiplyHighRoundScale(c0402, cr));

                // G = Y - round(0.344 × Cb) - round(0.714 × Cr)
                var g = Avx512BW.Subtract(Avx512BW.Subtract(y, Avx512BW.MultiplyHighRoundScale(c0344, cb)), Avx512BW.MultiplyHighRoundScale(c0714, cr));

                // B = Y + Cb + round(0.772 × Cb)
                var b = Avx512BW.Add(Avx512BW.Add(y, cb), Avx512BW.MultiplyHighRoundScale(c0772, cb));

                // Pack short→byte с saturation
                var rBytesOut = Avx512BW.PackUnsignedSaturate(r, r);
                var gBytesOut = Avx512BW.PackUnsignedSaturate(g, g);
                var bBytesOut = Avx512BW.PackUnsignedSaturate(b, b);

                // Permute для правильного порядка после pack
                rBytesOut = Avx512F.PermuteVar8x64(rBytesOut.AsInt64(), Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte();
                gBytesOut = Avx512F.PermuteVar8x64(gBytesOut.AsInt64(), Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte();
                bBytesOut = Avx512F.PermuteVar8x64(bBytesOut.AsInt64(), Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte();

                // Interleave и запись
                InterleaveRgb32(dstByte, rBytesOut.GetLower(), gBytesOut.GetLower(), bBytesOut.GetLower());

                srcByte += 96;
                dstByte += 96;
                count -= 32;
            }

            // Остаток обрабатываем AVX2
            if (count > 0)
                FromYCbCrAvx2(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgb24>(dstByte, count));
        }
    }

    /// <summary>Deinterleave 32 YCbCr пикселей (96 байт) в отдельные Y, Cb, Cr компоненты.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr32(byte* src, out Vector256<byte> y, out Vector256<byte> cb, out Vector256<byte> cr)
    {
        // Загружаем 2 блока по 16 пикселей и объединяем
        DeinterleaveYCbCr16(src, out var y0, out var cb0, out var cr0);
        DeinterleaveYCbCr16(src + 48, out var y1, out var cb1, out var cr1);

        y = Vector256.Create(y0, y1);
        cb = Vector256.Create(cb0, cb1);
        cr = Vector256.Create(cr0, cr1);
    }

    /// <summary>Interleave R, G, B компоненты в 32 RGB24 пикселя (96 байт).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgb32(byte* dst, Vector256<byte> r, Vector256<byte> g, Vector256<byte> b)
    {
        InterleaveRgb16(dst, r.GetLower(), g.GetLower(), b.GetLower());
        InterleaveRgb16(dst + 48, r.GetUpper(), g.GetUpper(), b.GetUpper());
    }

    #endregion

    #region SSE4.1 Implementation (YCbCr → RGB24, 8 pixels per iteration)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Rgb24> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
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

            while (count >= 8)
            {
                DeinterleaveYCbCr8(srcByte, out var yVec, out var cbVec, out var crVec);

                var yLo = Sse41.ConvertToVector128Int32(yVec);
                var cbLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(cbVec), c128);
                var crLo = Sse2.Subtract(Sse41.ConvertToVector128Int32(crVec), c128);

                var rLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crLo), half), 16));
                var gLo = Sse2.Subtract(yLo, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbLo), Sse41.MultiplyLow(c0714, crLo)), half), 16));
                var bLo = Sse2.Add(yLo, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbLo), half), 16));

                var yHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(yVec, 4));
                var cbHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(cbVec, 4)), c128);
                var crHi = Sse2.Subtract(Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(crVec, 4)), c128);

                var rHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1402, crHi), half), 16));
                var gHi = Sse2.Subtract(yHi, Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse41.MultiplyLow(c0344, cbHi), Sse41.MultiplyLow(c0714, crHi)), half), 16));
                var bHi = Sse2.Add(yHi, Sse2.ShiftRightArithmetic(Sse2.Add(Sse41.MultiplyLow(c1772, cbHi), half), 16));

                var rBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(rLo, rHi), Sse2.PackSignedSaturate(rLo, rHi));
                var gBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(gLo, gHi), Sse2.PackSignedSaturate(gLo, gHi));
                var bBytes = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(bLo, bHi), Sse2.PackSignedSaturate(bLo, bHi));

                InterleaveRgb8(dstByte, rBytes, gBytes, bBytes);

                srcByte += 24;
                dstByte += 24;
                count -= 8;
            }

            if (count > 0)
                FromYCbCrScalar(new ReadOnlySpan<YCbCr>(srcByte, count), new Span<Rgb24>(dstByte, count));
        }
    }

    #endregion

    #region Deinterleave/Interleave Helpers (YCbCr)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr8(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        var bytes0 = Vector128.Load(src);
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();

        y = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleY0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleY1));
        cb = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleCb0),
                     Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleCb1));
        cr = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleCr0),
                     Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleCr1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveYCbCr16(byte* src, out Vector128<byte> y, out Vector128<byte> cb, out Vector128<byte> cr)
    {
        DeinterleaveYCbCr8(src, out var y0, out var cb0, out var cr0);
        DeinterleaveYCbCr8(src + 24, out var y1, out var cb1, out var cr1);

        // Используем UnpackLow вместо GetElement + Create для избежания переходов через GPR
        y = Sse2.UnpackLow(y0.AsUInt64(), y1.AsUInt64()).AsByte();
        cb = Sse2.UnpackLow(cb0.AsUInt64(), cb1.AsUInt64()).AsByte();
        cr = Sse2.UnpackLow(cr0.AsUInt64(), cr1.AsUInt64()).AsByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgb8(byte* dst, Vector128<byte> r, Vector128<byte> g, Vector128<byte> b)
    {
        // R0G0R1G1R2G2R3G3R4G4R5G5R6G6R7G7
        var rg = Sse2.UnpackLow(r, g);

        // Первые 16 байт: R0G0B0 R1G1B1 R2G2B2 R3G3B3 R4G4B4 R5_
        var out0 = Sse2.Or(
            Ssse3.Shuffle(rg, Rgb24Sse41Vectors.RgToRgb24ShuffleMask0),
            Ssse3.Shuffle(b, Rgb24Sse41Vectors.BToRgb24ShuffleMask0));
        out0.Store(dst);

        // Оставшиеся 8 байт: _G5B5 R6G6B6 R7G7B7
        var out1 = Sse2.Or(
            Ssse3.Shuffle(rg, Rgb24Sse41Vectors.RgToRgb24ShuffleMask1),
            Ssse3.Shuffle(b, Rgb24Sse41Vectors.BToRgb24ShuffleMask1));
        Sse2.StoreLow((double*)(dst + 16), out1.AsDouble());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InterleaveRgb16(byte* dst, Vector128<byte> r, Vector128<byte> g, Vector128<byte> b)
    {
        InterleaveRgb8(dst, r, g, b);
        InterleaveRgb8(dst + 24,
            Sse2.ShiftRightLogical128BitLane(r, 8),
            Sse2.ShiftRightLogical128BitLane(g, 8),
            Sse2.ShiftRightLogical128BitLane(b, 8));
    }

    #endregion

    #region Conversion Operator (YCbCr → RGB24)

    /// <summary>Явная конвертация из YCbCr в RGB24.</summary>
    public static explicit operator Rgb24(YCbCr ycbcr) => FromYCbCr(ycbcr);

    #endregion
}
