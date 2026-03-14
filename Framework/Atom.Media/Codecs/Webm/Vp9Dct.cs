#pragma warning disable CA2014, IDE0004, IDE0048, IDE0055, S109, S1144, S1905, MA0051

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 inverse transforms: IDCT and IADST for 4×4, 8×8, 16×16, 32×32.
/// Based on VP9 specification §12.5 (Inverse DCT) and libvpx reference implementation.
/// </summary>
/// <remarks>
/// VP9 uses fixed-point cosine constants with 14 bits of precision:
/// cos(k * π / N) * 16384, rounded to nearest integer.
/// Two transform types: DCT (Type II) and ADST (Asymmetric Discrete Sine Transform).
/// </remarks>
internal static class Vp9Dct
{
    #region Constants

    // Fixed-point trigonometric constants (14-bit precision: round(cos(k*pi/N) * 16384))
    private const int CosPI_1_64 = 16364;   // cos(1π/64)
    private const int CosPI_2_64 = 16305;   // cos(2π/64)
    private const int CosPI_3_64 = 16207;   // cos(3π/64)
    private const int CosPI_4_64 = 16069;   // cos(4π/64)
    private const int CosPI_5_64 = 15893;   // cos(5π/64)
    private const int CosPI_6_64 = 15679;   // cos(6π/64)
    private const int CosPI_7_64 = 15426;   // cos(7π/64)
    private const int CosPI_8_64 = 15137;   // cos(8π/64) = cos(π/8)
    private const int CosPI_9_64 = 14811;   // cos(9π/64)
    private const int CosPI_10_64 = 14449;  // cos(10π/64)
    private const int CosPI_11_64 = 14053;  // cos(11π/64)
    private const int CosPI_12_64 = 13623;  // cos(12π/64) = cos(3π/16)
    private const int CosPI_13_64 = 13160;  // cos(13π/64)
    private const int CosPI_14_64 = 12665;  // cos(14π/64)
    private const int CosPI_15_64 = 12140;  // cos(15π/64)
    private const int CosPI_16_64 = 11585;  // cos(16π/64) = cos(π/4) = √2/2 * 16384
    private const int CosPI_17_64 = 11003;
    private const int CosPI_18_64 = 10394;
    private const int CosPI_19_64 = 9760;
    private const int CosPI_20_64 = 9102;   // cos(20π/64) = cos(5π/16)
    private const int CosPI_21_64 = 8423;
    private const int CosPI_22_64 = 7723;
    private const int CosPI_23_64 = 7005;
    private const int CosPI_24_64 = 6270;   // cos(24π/64) = cos(3π/8)
    private const int CosPI_25_64 = 5520;
    private const int CosPI_26_64 = 4756;
    private const int CosPI_27_64 = 3981;
    private const int CosPI_28_64 = 3196;   // cos(28π/64) = cos(7π/16)
    private const int CosPI_29_64 = 2404;
    private const int CosPI_30_64 = 1606;
    private const int CosPI_31_64 = 804;

    // For ADST
    private const int SinPI_1_9 = 5283;     // sin(1π/9) * 16384
    private const int SinPI_2_9 = 9929;     // sin(2π/9) * 16384
    private const int SinPI_3_9 = 13377;    // sin(3π/9) * 16384
    private const int SinPI_4_9 = 15212;    // sin(4π/9) * 16384

    private const int RoundingShift = 14;

    #endregion

    #region Butterfly Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long RoundShift(long value, int bits) =>
        (value + (1L << (bits - 1))) >> bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundShift(int value, int bits) =>
        (value + (1 << (bits - 1))) >> bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    /// <summary>Butterfly rotation: out1 = c * a - s * b, out2 = s * a + c * b (14-bit rounding).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ButterflyRotation(int a, int b, int cos, int sin, out int out1, out int out2)
    {
        out1 = (int)RoundShift((long)a * cos - (long)b * sin, RoundingShift);
        out2 = (int)RoundShift((long)a * sin + (long)b * cos, RoundingShift);
    }

    #endregion

    #region 4×4 Inverse DCT

    /// <summary>
    /// 4×4 inverse DCT. Result is ADDED to <paramref name="output"/>.
    /// </summary>
    public static void InverseDct4x4(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[16];

        // Column pass
        for (var i = 0; i < 4; i++)
        {
            var s0 = input[i];
            var s1 = input[4 + i];
            var s2 = input[8 + i];
            var s3 = input[12 + i];

            // Stage 1
            ButterflyRotation(s0, s2, CosPI_16_64, CosPI_16_64, out var t0, out var t1);
            ButterflyRotation(s1, s3, CosPI_24_64, CosPI_8_64, out var t2, out var t3);

            // Stage 2
            temp[i] = t0 + t3;
            temp[4 + i] = t1 + t2;
            temp[8 + i] = t1 - t2;
            temp[12 + i] = t0 - t3;
        }

        // Row pass + add to output
        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;

            ButterflyRotation(temp[row], temp[row + 2], CosPI_16_64, CosPI_16_64, out var t0, out var t1);
            ButterflyRotation(temp[row + 1], temp[row + 3], CosPI_24_64, CosPI_8_64, out var t2, out var t3);

            var off = i * stride;
            output[off] = ClampByte(output[off] + RoundShift(t0 + t3, 4));
            output[off + 1] = ClampByte(output[off + 1] + RoundShift(t1 + t2, 4));
            output[off + 2] = ClampByte(output[off + 2] + RoundShift(t1 - t2, 4));
            output[off + 3] = ClampByte(output[off + 3] + RoundShift(t0 - t3, 4));
        }
    }

    /// <summary>4×4 DC-only fast path. Only input[0] is used.</summary>
    public static void InverseDct4x4DcOnly(short dc, Span<byte> output, int stride)
    {
        var a = (int)RoundShift((long)dc * CosPI_16_64, RoundingShift);
        a = (int)RoundShift((long)a * CosPI_16_64, RoundingShift);
        a = RoundShift(a, 4);

        for (var i = 0; i < 4; i++)
        {
            var off = i * stride;
            output[off] = ClampByte(output[off] + a);
            output[off + 1] = ClampByte(output[off + 1] + a);
            output[off + 2] = ClampByte(output[off + 2] + a);
            output[off + 3] = ClampByte(output[off + 3] + a);
        }
    }

    #endregion

    #region 4×4 Inverse ADST

    /// <summary>
    /// 4×4 inverse ADST (Asymmetric Discrete Sine Transform).
    /// VP9 uses ADST for blocks at prediction boundaries.
    /// Result is ADDED to <paramref name="output"/>.
    /// </summary>
    public static void InverseAdst4x4(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[16];

        // Column pass (ADST)
        for (var i = 0; i < 4; i++)
        {
            var s0 = input[i];
            var s1 = input[4 + i];
            var s2 = input[8 + i];
            var s3 = input[12 + i];

            Adst4Kernel(s0, s1, s2, s3, out temp[i], out temp[4 + i], out temp[8 + i], out temp[12 + i]);
        }

        // Row pass (ADST) + add to output
        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;
            Adst4Kernel(temp[row], temp[row + 1], temp[row + 2], temp[row + 3],
                out var o0, out var o1, out var o2, out var o3);

            var off = i * stride;
            output[off] = ClampByte(output[off] + RoundShift(o0, 4));
            output[off + 1] = ClampByte(output[off + 1] + RoundShift(o1, 4));
            output[off + 2] = ClampByte(output[off + 2] + RoundShift(o2, 4));
            output[off + 3] = ClampByte(output[off + 3] + RoundShift(o3, 4));
        }
    }

    /// <summary>
    /// 4-point ADST butterfly kernel (VP9 spec / libvpx iadst4_c).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Adst4Kernel(int s0, int s1, int s2, int s3,
        out int o0, out int o1, out int o2, out int o3)
    {
        var x0 = SinPI_1_9 * s0;
        var x1 = SinPI_2_9 * s0;
        var x2 = SinPI_3_9 * s1;
        var x3 = SinPI_4_9 * s2;
        var x4 = SinPI_1_9 * s2;
        var x5 = SinPI_2_9 * s3;
        var x6 = SinPI_4_9 * s3;

        var a = x0 + x3 + x5;
        var b = x1 - x4 - x6;
        var c = SinPI_3_9 * (s0 - s2 + s3);

        // s1 is treated as DC (added to all rotated outputs)
        o0 = (int)RoundShift(a + x2, RoundingShift);
        o1 = (int)RoundShift(b + x2, RoundingShift);
        o2 = (int)RoundShift(c, RoundingShift);
        o3 = (int)RoundShift(a + b - x2, RoundingShift);
    }

    #endregion

    #region 8×8 Inverse DCT

    /// <summary>
    /// 8×8 inverse DCT. Result is ADDED to <paramref name="output"/>.
    /// </summary>
    public static void InverseDct8x8(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[64];

        // Column pass
        for (var i = 0; i < 8; i++)
        {
            Idct8Column(
                input[i], input[8 + i], input[16 + i], input[24 + i],
                input[32 + i], input[40 + i], input[48 + i], input[56 + i],
                out temp[i], out temp[8 + i], out temp[16 + i], out temp[24 + i],
                out temp[32 + i], out temp[40 + i], out temp[48 + i], out temp[56 + i]);
        }

        // Row pass + add to output
        for (var i = 0; i < 8; i++)
        {
            var row = i * 8;
            Idct8Column(
                temp[row], temp[row + 1], temp[row + 2], temp[row + 3],
                temp[row + 4], temp[row + 5], temp[row + 6], temp[row + 7],
                out var o0, out var o1, out var o2, out var o3,
                out var o4, out var o5, out var o6, out var o7);

            var off = i * stride;
            output[off] = ClampByte(output[off] + RoundShift(o0, 5));
            output[off + 1] = ClampByte(output[off + 1] + RoundShift(o1, 5));
            output[off + 2] = ClampByte(output[off + 2] + RoundShift(o2, 5));
            output[off + 3] = ClampByte(output[off + 3] + RoundShift(o3, 5));
            output[off + 4] = ClampByte(output[off + 4] + RoundShift(o4, 5));
            output[off + 5] = ClampByte(output[off + 5] + RoundShift(o5, 5));
            output[off + 6] = ClampByte(output[off + 6] + RoundShift(o6, 5));
            output[off + 7] = ClampByte(output[off + 7] + RoundShift(o7, 5));
        }
    }

    /// <summary>Single column/row 8-point IDCT butterfly.</summary>
    private static void Idct8Column(
        int s0, int s1, int s2, int s3, int s4, int s5, int s6, int s7,
        out int o0, out int o1, out int o2, out int o3,
        out int o4, out int o5, out int o6, out int o7)
    {
        // Stage 1: even-odd decomposition
        ButterflyRotation(s0, s4, CosPI_16_64, CosPI_16_64, out var e0, out var e1);
        ButterflyRotation(s2, s6, CosPI_24_64, CosPI_8_64, out var e2, out var e3);

        var a0 = e0 + e3;
        var a1 = e1 + e2;
        var a2 = e1 - e2;
        var a3 = e0 - e3;

        // Stage 2: odd part
        ButterflyRotation(s1, s7, CosPI_28_64, CosPI_4_64, out var b0, out var b3);
        ButterflyRotation(s5, s3, CosPI_12_64, CosPI_20_64, out var b1, out var b2);

        var c0 = b0 + b1;
        var c1 = b0 - b1;
        var c2 = b3 - b2;
        var c3 = b3 + b2;

        ButterflyRotation(c1, c2, CosPI_16_64, CosPI_16_64, out c1, out c2);

        // Stage 3: combine
        o0 = a0 + c3;
        o1 = a1 + c2;
        o2 = a2 + c1;
        o3 = a3 + c0;
        o4 = a3 - c0;
        o5 = a2 - c1;
        o6 = a1 - c2;
        o7 = a0 - c3;
    }

    #endregion

    #region 16×16 Inverse DCT

    /// <summary>
    /// 16×16 inverse DCT. Result is ADDED to <paramref name="output"/>.
    /// </summary>
    public static void InverseDct16x16(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[256];

        // Column pass
        for (var i = 0; i < 16; i++)
        {
            Span<int> col = stackalloc int[16];
            for (var j = 0; j < 16; j++)
                col[j] = input[j * 16 + i];

            Idct16(col, out var r);

            for (var j = 0; j < 16; j++)
                temp[j * 16 + i] = r[j];
        }

        // Row pass + add to output
        for (var i = 0; i < 16; i++)
        {
            Span<int> row = stackalloc int[16];
            for (var j = 0; j < 16; j++)
                row[j] = temp[i * 16 + j];

            Idct16(row, out var r);

            var off = i * stride;
            for (var j = 0; j < 16; j++)
                output[off + j] = ClampByte(output[off + j] + RoundShift(r[j], 6));
        }
    }

    /// <summary>16-point IDCT butterfly.</summary>
    private static void Idct16(ReadOnlySpan<int> input, out Idct16Result result)
    {
        result = default;

        // Stage 1: 8-point even part (reuse Idct8Column logic)
        ButterflyRotation(input[0], input[8], CosPI_16_64, CosPI_16_64, out var e0, out var e1);
        ButterflyRotation(input[4], input[12], CosPI_24_64, CosPI_8_64, out var e2, out var e3);

        var a0 = e0 + e3;
        var a1 = e1 + e2;
        var a2 = e1 - e2;
        var a3 = e0 - e3;

        ButterflyRotation(input[2], input[14], CosPI_28_64, CosPI_4_64, out var b0, out var b3);
        ButterflyRotation(input[10], input[6], CosPI_12_64, CosPI_20_64, out var b1, out var b2);

        var c0 = b0 + b1;
        var c1 = b0 - b1;
        var c2 = b3 - b2;
        var c3 = b3 + b2;

        ButterflyRotation(c1, c2, CosPI_16_64, CosPI_16_64, out c1, out c2);

        var d0 = a0 + c3;
        var d1 = a1 + c2;
        var d2 = a2 + c1;
        var d3 = a3 + c0;
        var d4 = a3 - c0;
        var d5 = a2 - c1;
        var d6 = a1 - c2;
        var d7 = a0 - c3;

        // Stage 2: 8-point odd part
        ButterflyRotation(input[1], input[15], CosPI_30_64, CosPI_2_64, out var h0, out var h7);
        ButterflyRotation(input[9], input[7], CosPI_14_64, CosPI_18_64, out var h1, out var h6);
        ButterflyRotation(input[5], input[11], CosPI_22_64, CosPI_10_64, out var h2, out var h5);
        ButterflyRotation(input[13], input[3], CosPI_6_64, CosPI_26_64, out var h3, out var h4);

        var g0 = h0 + h1;
        var g1 = h0 - h1;
        var g2 = h3 - h2;
        var g3 = h3 + h2;
        var g4 = h4 + h5;
        var g5 = h4 - h5;
        var g6 = h7 - h6;
        var g7 = h7 + h6;

        ButterflyRotation(g1, g6, CosPI_24_64, CosPI_8_64, out g1, out g6);
        ButterflyRotation(g2, g5, -CosPI_24_64, CosPI_8_64, out g2, out g5);

        var f0 = g0 + g3;
        var f1 = g1 + g2;
        var f2 = g1 - g2;
        var f3 = g0 - g3;
        var f4 = g4 - g7;  // Note: reverse order for odd part bottom
        var f5 = g5 - g6;  // These get negated in the final butterfly
        var f6 = g5 + g6;
        var f7 = g4 + g7;

        ButterflyRotation(f2, f5, CosPI_16_64, CosPI_16_64, out f2, out f5);
        ButterflyRotation(f3, f4, CosPI_16_64, CosPI_16_64, out f3, out f4);

        // Stage 3: combine even + odd
        result[0] = d0 + f7;
        result[1] = d1 + f6;
        result[2] = d2 + f5;
        result[3] = d3 + f4;
        result[4] = d4 + f3;
        result[5] = d5 + f2;
        result[6] = d6 + f1;
        result[7] = d7 + f0;
        result[8] = d7 - f0;
        result[9] = d6 - f1;
        result[10] = d5 - f2;
        result[11] = d4 - f3;
        result[12] = d3 - f4;
        result[13] = d2 - f5;
        result[14] = d1 - f6;
        result[15] = d0 - f7;
    }

    [InlineArray(16)]
    private struct Idct16Result
    {
        private int _element;
    }

    #endregion

    #region 32×32 Inverse DCT

    /// <summary>
    /// 32×32 inverse DCT. Uses a working buffer to avoid huge stackalloc.
    /// Result is ADDED to <paramref name="output"/>.
    /// </summary>
    public static void InverseDct32x32(ReadOnlySpan<short> input, Span<byte> output, int stride, int[] workBuffer)
    {
        // Column pass → workBuffer
        Span<int> col = stackalloc int[32];
        Span<int> colOut = stackalloc int[32];

        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
                col[j] = input[j * 32 + i];

            Idct32(col, colOut);

            for (var j = 0; j < 32; j++)
                workBuffer[j * 32 + i] = colOut[j];
        }

        // Row pass + add to output
        Span<int> row = stackalloc int[32];
        Span<int> rowOut = stackalloc int[32];

        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
                row[j] = workBuffer[i * 32 + j];

            Idct32(row, rowOut);

            var off = i * stride;
            for (var j = 0; j < 32; j++)
                output[off + j] = ClampByte(output[off + j] + RoundShift(rowOut[j], 6));
        }
    }

    /// <summary>32-point IDCT butterfly (VP9 spec §12.5.3 / libvpx idct32_c).</summary>
    private static void Idct32(ReadOnlySpan<int> input, Span<int> output)
    {
        // Stage 1: 16-point even part via recursive splitting to 8+8
        // Even indices feed into 16-pt IDCT, odd indices feed into 16-pt rotation stage

        // 16-pt even part: use input[0,2,4,...,30]
        Span<int> even = stackalloc int[16];
        for (var i = 0; i < 16; i++)
            even[i] = input[i * 2];

        Idct16(even, out var evenResult);

        // 16-pt odd part: input[1,3,5,...,31]
        // Stage 1 rotations
        ButterflyRotation(input[1], input[31], CosPI_31_64, CosPI_1_64, out var s0, out var s15);
        ButterflyRotation(input[17], input[15], CosPI_15_64, CosPI_17_64, out var s1, out var s14);
        ButterflyRotation(input[9], input[23], CosPI_23_64, CosPI_9_64, out var s2, out var s13);
        ButterflyRotation(input[25], input[7], CosPI_7_64, CosPI_25_64, out var s3, out var s12);
        ButterflyRotation(input[5], input[27], CosPI_27_64, CosPI_5_64, out var s4, out var s11);
        ButterflyRotation(input[21], input[11], CosPI_11_64, CosPI_21_64, out var s5, out var s10);
        ButterflyRotation(input[13], input[19], CosPI_19_64, CosPI_13_64, out var s6, out var s9);
        ButterflyRotation(input[29], input[3], CosPI_3_64, CosPI_29_64, out var s7, out var s8);

        // Stage 2 additions
        var t0 = s0 + s1;
        var t1 = s0 - s1;
        var t2 = s3 - s2;
        var t3 = s3 + s2;
        var t4 = s4 + s5;
        var t5 = s4 - s5;
        var t6 = s7 - s6;
        var t7 = s7 + s6;
        var t8 = s8 + s9;
        var t9 = s8 - s9;
        var t10 = s11 - s10;
        var t11 = s11 + s10;
        var t12 = s12 + s13;
        var t13 = s12 - s13;
        var t14 = s15 - s14;
        var t15 = s15 + s14;

        // Stage 3 rotations
        ButterflyRotation(t1, t14, CosPI_28_64, CosPI_4_64, out t1, out t14);
        ButterflyRotation(t2, t13, -CosPI_4_64, CosPI_28_64, out t2, out t13);
        ButterflyRotation(t5, t10, CosPI_12_64, CosPI_20_64, out t5, out t10);
        ButterflyRotation(t6, t9, -CosPI_20_64, CosPI_12_64, out t6, out t9);

        // Stage 4 additions
        var u0 = t0 + t3;
        var u1 = t1 + t2;
        var u2 = t1 - t2;
        var u3 = t0 - t3;
        var u4 = t4 - t7;
        var u5 = t5 - t6;
        var u6 = t5 + t6;
        var u7 = t4 + t7;
        var u8 = t8 + t11;
        var u9 = t9 + t10;
        var u10 = t9 - t10;
        var u11 = t8 - t11;
        var u12 = t15 - t12;
        var u13 = t14 - t13;
        var u14 = t14 + t13;
        var u15 = t15 + t12;

        // Stage 5 rotations
        ButterflyRotation(u2, u13, CosPI_16_64, CosPI_16_64, out u2, out u13);
        ButterflyRotation(u3, u12, CosPI_16_64, CosPI_16_64, out u3, out u12);
        ButterflyRotation(u5, u10, CosPI_16_64, CosPI_16_64, out u5, out u10);
        ButterflyRotation(u4, u11, CosPI_16_64, CosPI_16_64, out u4, out u11);

        // Stage 6: combine odd part pairs
        var v0 = u0 + u7;
        var v1 = u1 + u6;
        var v2 = u2 + u5;
        var v3 = u3 + u4;
        var v4 = u3 - u4;
        var v5 = u2 - u5;
        var v6 = u1 - u6;
        var v7 = u0 - u7;
        var v8 = u8 - u15;  // Note: these cross-pairs
        var v9 = u9 - u14;
        var v10 = u10 - u13;
        var v11 = u11 - u12;
        var v12 = u11 + u12;
        var v13 = u10 + u13;
        var v14 = u9 + u14;
        var v15 = u8 + u15;

        ButterflyRotation(v4, v11, CosPI_16_64, CosPI_16_64, out v4, out v11);
        ButterflyRotation(v5, v10, CosPI_16_64, CosPI_16_64, out v5, out v10);
        ButterflyRotation(v6, v9, CosPI_16_64, CosPI_16_64, out v6, out v9);
        ButterflyRotation(v7, v8, CosPI_16_64, CosPI_16_64, out v7, out v8);

        // Final combine: even + odd
        output[0] = evenResult[0] + v15;
        output[1] = evenResult[1] + v14;
        output[2] = evenResult[2] + v13;
        output[3] = evenResult[3] + v12;
        output[4] = evenResult[4] + v11;
        output[5] = evenResult[5] + v10;
        output[6] = evenResult[6] + v9;
        output[7] = evenResult[7] + v8;
        output[8] = evenResult[8] + v7;
        output[9] = evenResult[9] + v6;
        output[10] = evenResult[10] + v5;
        output[11] = evenResult[11] + v4;
        output[12] = evenResult[12] + v3;
        output[13] = evenResult[13] + v2;
        output[14] = evenResult[14] + v1;
        output[15] = evenResult[15] + v0;
        output[16] = evenResult[15] - v0;
        output[17] = evenResult[14] - v1;
        output[18] = evenResult[13] - v2;
        output[19] = evenResult[12] - v3;
        output[20] = evenResult[11] - v4;
        output[21] = evenResult[10] - v5;
        output[22] = evenResult[9] - v6;
        output[23] = evenResult[8] - v7;
        output[24] = evenResult[7] - v8;
        output[25] = evenResult[6] - v9;
        output[26] = evenResult[5] - v10;
        output[27] = evenResult[4] - v11;
        output[28] = evenResult[3] - v12;
        output[29] = evenResult[2] - v13;
        output[30] = evenResult[1] - v14;
        output[31] = evenResult[0] - v15;
    }

    #endregion

    #region Transform Dispatch

    /// <summary>
    /// Dispatches inverse transform based on size and type (DCT vs ADST).
    /// <paramref name="txType"/>: 0=DCT_DCT, 1=ADST_DCT, 2=DCT_ADST, 3=ADST_ADST.
    /// </summary>
    public static void InverseTransform(ReadOnlySpan<short> coeffs, Span<byte> output, int stride,
        int txSize, int txType, int[] workBuffer)
    {
        switch (txSize)
        {
            case Vp9Constants.Tx4x4:
                InverseTransform4x4(coeffs, output, stride, txType);
                break;
            case Vp9Constants.Tx8x8:
                // VP9 spec: 8×8 only uses DCT_DCT
                InverseDct8x8(coeffs, output, stride);
                break;
            case Vp9Constants.Tx16x16:
                InverseDct16x16(coeffs, output, stride);
                break;
            case Vp9Constants.Tx32x32:
                InverseDct32x32(coeffs, output, stride, workBuffer);
                break;
        }
    }

    /// <summary>4×4 transform dispatch by type (DCT/ADST combinations).</summary>
    private static void InverseTransform4x4(ReadOnlySpan<short> coeffs, Span<byte> output, int stride, int txType)
    {
        // txType: 0=DCT_DCT, 1=ADST_DCT, 2=DCT_ADST, 3=ADST_ADST
        // For 4×4, we support mixed DCT/ADST via row/column separate transforms
        if (txType == 0)
        {
            InverseDct4x4(coeffs, output, stride);
        }
        else
        {
            // Generic path: separate column + row transforms with intermediate buffer
            Span<int> temp = stackalloc int[16];

            // Column pass
            for (var i = 0; i < 4; i++)
            {
                var s0 = (int)coeffs[i];
                var s1 = (int)coeffs[4 + i];
                var s2 = (int)coeffs[8 + i];
                var s3 = (int)coeffs[12 + i];

                if ((txType & 1) != 0) // ADST in columns (txType 1 or 3)
                {
                    Adst4Kernel(s0, s1, s2, s3, out temp[i], out temp[4 + i], out temp[8 + i], out temp[12 + i]);
                }
                else // DCT in columns
                {
                    ButterflyRotation(s0, s2, CosPI_16_64, CosPI_16_64, out var t0, out var t1);
                    ButterflyRotation(s1, s3, CosPI_24_64, CosPI_8_64, out var t2, out var t3);
                    temp[i] = t0 + t3;
                    temp[4 + i] = t1 + t2;
                    temp[8 + i] = t1 - t2;
                    temp[12 + i] = t0 - t3;
                }
            }

            // Row pass + add to output
            for (var i = 0; i < 4; i++)
            {
                var row = i * 4;
                int o0, o1, o2, o3;

                if ((txType & 2) != 0) // ADST in rows (txType 2 or 3)
                {
                    Adst4Kernel(temp[row], temp[row + 1], temp[row + 2], temp[row + 3],
                        out o0, out o1, out o2, out o3);
                }
                else // DCT in rows
                {
                    ButterflyRotation(temp[row], temp[row + 2], CosPI_16_64, CosPI_16_64, out var t0, out var t1);
                    ButterflyRotation(temp[row + 1], temp[row + 3], CosPI_24_64, CosPI_8_64, out var t2, out var t3);
                    o0 = t0 + t3;
                    o1 = t1 + t2;
                    o2 = t1 - t2;
                    o3 = t0 - t3;
                }

                var off = i * stride;
                output[off] = ClampByte(output[off] + RoundShift(o0, 4));
                output[off + 1] = ClampByte(output[off + 1] + RoundShift(o1, 4));
                output[off + 2] = ClampByte(output[off + 2] + RoundShift(o2, 4));
                output[off + 3] = ClampByte(output[off + 3] + RoundShift(o3, 4));
            }
        }
    }

    #endregion
}
