#pragma warning disable IDE0045, IDE0048, S109

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 Discrete Cosine Transform operations per RFC 6386 §14.3 / §20.8.
/// 4×4 IDCT (short_idct4x4llm_c) and inverse Walsh-Hadamard (vp8_short_inv_walsh4x4_c).
/// </summary>
internal static class Vp8Dct
{
    /// <summary>
    /// Performs 4×4 inverse DCT (RFC 6386 §14.3 short_idct4x4llm_c).
    /// Coefficients in <paramref name="input"/> (16 shorts, zigzag-reordered before call).
    /// Result is ADDED to <paramref name="output"/> (stride = <paramref name="stride"/>).
    /// </summary>
    /// <remarks>
    /// RFC 6386 §20.8 short_idct4x4llm_c reference:
    /// <code>
    /// temp1 = (ip[n] * sinpi8sqrt2) >> 16;  if (ip[n] &lt; 0) temp1--;
    /// temp2 = ip[n] + ((ip[n] * cospi8sqrt2minus1) >> 16);
    /// </code>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InverseDct4x4(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[16];

        // Columns first
        for (var i = 0; i < 4; i++)
        {
            var a = input[i] + input[8 + i];
            var b = input[i] - input[8 + i];

            var t1 = input[4 + i] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var t2 = input[12 + i] + (input[12 + i] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            var c = t1 - t2;

            t1 = input[4 + i] + (input[4 + i] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            t2 = input[12 + i] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var d = t1 + t2;

            temp[i] = a + d;
            temp[12 + i] = a - d;
            temp[4 + i] = b + c;
            temp[8 + i] = b - c;
        }

        // Rows
        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;
            var a = temp[row] + temp[row + 2];
            var b = temp[row] - temp[row + 2];

            var t1 = temp[row + 1] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var t2 = temp[row + 3] + (temp[row + 3] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            var c = t1 - t2;

            t1 = temp[row + 1] + (temp[row + 1] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            t2 = temp[row + 3] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var d = t1 + t2;

            var outOff = i * stride;
            output[outOff] = ClampByte(output[outOff] + ((a + d + 4) >> 3));
            output[outOff + 1] = ClampByte(output[outOff + 1] + ((b + c + 4) >> 3));
            output[outOff + 2] = ClampByte(output[outOff + 2] + ((b - c + 4) >> 3));
            output[outOff + 3] = ClampByte(output[outOff + 3] + ((a - d + 4) >> 3));
        }
    }

    /// <summary>
    /// Performs 4×4 inverse DCT adding only the DC component (when all AC = 0).
    /// This is a fast path: only input[0] matters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InverseDct4x4DcOnly(short dc, Span<byte> output, int stride)
    {
        var value = (dc + 4) >> 3;

        for (var i = 0; i < 4; i++)
        {
            var off = i * stride;
            output[off] = ClampByte(output[off] + value);
            output[off + 1] = ClampByte(output[off + 1] + value);
            output[off + 2] = ClampByte(output[off + 2] + value);
            output[off + 3] = ClampByte(output[off + 3] + value);
        }
    }

    /// <summary>
    /// 4×4 inverse Walsh-Hadamard Transform (RFC 6386 §14.4 / vp8_short_inv_walsh4x4_c).
    /// Takes 16 WHT-coded DC values and produces 16 dequantized DC values for the Y2 block.
    /// </summary>
    public static void InverseWht4x4(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<int> temp = stackalloc int[16];

        // Columns
        for (var i = 0; i < 4; i++)
        {
            var a1 = input[i] + input[12 + i];
            var b1 = input[4 + i] + input[8 + i];
            var c1 = input[4 + i] - input[8 + i];
            var d1 = input[i] - input[12 + i];

            temp[i] = a1 + b1;
            temp[4 + i] = c1 + d1;
            temp[8 + i] = a1 - b1;
            temp[12 + i] = d1 - c1;
        }

        // Rows
        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;
            var a1 = temp[row] + temp[row + 3];
            var b1 = temp[row + 1] + temp[row + 2];
            var c1 = temp[row + 1] - temp[row + 2];
            var d1 = temp[row] - temp[row + 3];

            var a2 = a1 + b1;
            var b2 = c1 + d1;
            var c2 = a1 - b1;
            var d2 = d1 - c1;

            output[row] = (short)((a2 + 3) >> 3);
            output[row + 1] = (short)((b2 + 3) >> 3);
            output[row + 2] = (short)((c2 + 3) >> 3);
            output[row + 3] = (short)((d2 + 3) >> 3);
        }
    }

    /// <summary>
    /// Forward 4×4 DCT (encoder path). RFC 6386 does not specify forward DCT explicitly,
    /// but it is the transpose of the inverse scaled appropriately.
    /// Uses the standard VP8 forward transform from libvpx vp8_short_fdct4x4_c.
    /// </summary>
    public static void ForwardDct4x4(ReadOnlySpan<short> input, Span<short> output, int inputStride)
    {
        Span<int> temp = stackalloc int[16];

        // Rows first
        for (var i = 0; i < 4; i++)
        {
            var off = i * inputStride;
            var i0 = (int)input[off];
            var i1 = (int)input[off + 1];
            var i2 = (int)input[off + 2];
            var i3 = (int)input[off + 3];

            var a1 = (i0 + i3) * 8;
            var b1 = (i1 + i2) * 8;
            var c1 = (i1 - i2) * 8;
            var d1 = (i0 - i3) * 8;

            temp[i * 4] = a1 + b1;
            temp[(i * 4) + 1] = ((c1 * 2217) + (d1 * 5352) + 14500) >> 12;
            temp[(i * 4) + 2] = a1 - b1;
            temp[(i * 4) + 3] = ((d1 * 2217) - (c1 * 5352) + 7500) >> 12;
        }

        // Columns
        for (var i = 0; i < 4; i++)
        {
            var a1 = temp[i] + temp[12 + i];
            var b1 = temp[4 + i] + temp[8 + i];
            var c1 = temp[4 + i] - temp[8 + i];
            var d1 = temp[i] - temp[12 + i];

            output[i] = (short)((a1 + b1 + 7) >> 4);
            output[4 + i] = (short)((((c1 * 2217) + (d1 * 5352) + 12000) >> 16) + (d1 != 0 ? 1 : 0));
            output[8 + i] = (short)((a1 - b1 + 7) >> 4);
            output[12 + i] = (short)(((d1 * 2217) - (c1 * 5352) + 51000) >> 16);
        }
    }

    /// <summary>
    /// Forward 4×4 Walsh-Hadamard Transform for Y2 DC coefficients (encoder path).
    /// </summary>
    public static void ForwardWht4x4(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<int> temp = stackalloc int[16];

        // Rows
        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;
            var a1 = input[row] + input[row + 3];
            var b1 = input[row + 1] + input[row + 2];
            var c1 = input[row + 1] - input[row + 2];
            var d1 = input[row] - input[row + 3];

            temp[row] = a1 + b1;
            temp[row + 1] = c1 + d1;
            temp[row + 2] = a1 - b1;
            temp[row + 3] = d1 - c1;
        }

        // Columns
        for (var i = 0; i < 4; i++)
        {
            var a1 = temp[i] + temp[12 + i];
            var b1 = temp[4 + i] + temp[8 + i];
            var c1 = temp[4 + i] - temp[8 + i];
            var d1 = temp[i] - temp[12 + i];

            output[i] = (short)((a1 + b1 + 1) >> 1);
            output[4 + i] = (short)((c1 + d1 + 1) >> 1);
            output[8 + i] = (short)((a1 - b1 + 1) >> 1);
            output[12 + i] = (short)((d1 - c1 + 1) >> 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);
}
