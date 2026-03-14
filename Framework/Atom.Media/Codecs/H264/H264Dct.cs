#pragma warning disable S109, MA0051

using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// H.264 4x4 Integer DCT/IDCT (ITU-T H.264 Section 8.5.12).
/// </summary>
/// <remarks>
/// H.264 использует целочисленную аппроксимацию DCT:
/// - Без ошибок округления
/// - Идентичные результаты на всех платформах
/// - Квантование интегрировано в трансформ
/// </remarks>
internal static class H264Dct
{
    /// <summary>
    /// Inverse 4x4 integer DCT (добавляет к предсказанному блоку).
    /// </summary>
    /// <param name="coeffs">4x4 DCT коэффициенты (16 элементов, zigzag unscan).</param>
    /// <param name="dst">Destination блок (шаг stride байт).</param>
    /// <param name="stride">Stride destination буфера.</param>
    public static void InverseDct4x4Add(ReadOnlySpan<short> coeffs, Span<byte> dst, int stride)
    {
        Span<int> tmp = stackalloc int[16];

        // Horizontal pass (rows)
        for (var i = 0; i < 4; i++)
        {
            var s0 = coeffs[i * 4];
            var s1 = coeffs[(i * 4) + 1];
            var s2 = coeffs[(i * 4) + 2];
            var s3 = coeffs[(i * 4) + 3];

            var e0 = s0 + s2;
            var e1 = s0 - s2;
            var e2 = (s1 >> 1) - s3;
            var e3 = s1 + (s3 >> 1);

            tmp[i * 4] = e0 + e3;
            tmp[(i * 4) + 1] = e1 + e2;
            tmp[(i * 4) + 2] = e1 - e2;
            tmp[(i * 4) + 3] = e0 - e3;
        }

        // Vertical pass (columns) + add to prediction + clip
        for (var j = 0; j < 4; j++)
        {
            var f0 = tmp[j];
            var f1 = tmp[4 + j];
            var f2 = tmp[8 + j];
            var f3 = tmp[12 + j];

            var g0 = f0 + f2;
            var g1 = f0 - f2;
            var g2 = (f1 >> 1) - f3;
            var g3 = f1 + (f3 >> 1);

            dst[j] = Clip((g0 + g3 + 32) >> 6, dst[j]);
            dst[stride + j] = Clip((g1 + g2 + 32) >> 6, dst[stride + j]);
            dst[(stride * 2) + j] = Clip((g1 - g2 + 32) >> 6, dst[(stride * 2) + j]);
            dst[(stride * 3) + j] = Clip((g0 - g3 + 32) >> 6, dst[(stride * 3) + j]);
        }
    }

    /// <summary>
    /// Inverse 4x4 DC-only (все коэффициенты кроме DC = 0).
    /// </summary>
    public static void InverseDct4x4DcAdd(short dcCoeff, Span<byte> dst, int stride)
    {
        var dc = (dcCoeff + 32) >> 6;

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
                dst[(y * stride) + x] = ClipByte(dst[(y * stride) + x] + dc);
        }
    }

    /// <summary>
    /// Inverse Hadamard 4x4 для luma DC (16x16 intra mode).
    /// </summary>
    public static void InverseHadamard4x4(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<int> tmp = stackalloc int[16];

        // Horizontal
        for (var i = 0; i < 4; i++)
        {
            var a = input[i * 4];
            var b = input[(i * 4) + 1];
            var c = input[(i * 4) + 2];
            var d = input[(i * 4) + 3];

            tmp[i * 4] = a + b + c + d;
            tmp[(i * 4) + 1] = a + b - c - d;
            tmp[(i * 4) + 2] = a - b - c + d;
            tmp[(i * 4) + 3] = a - b + c - d;
        }

        // Vertical
        for (var j = 0; j < 4; j++)
        {
            var a = tmp[j];
            var b = tmp[4 + j];
            var c = tmp[8 + j];
            var d = tmp[12 + j];

            output[j] = (short)((a + b + c + d + 2) >> 2);
            output[4 + j] = (short)((a + b - c - d + 2) >> 2);
            output[8 + j] = (short)((a - b - c + d + 2) >> 2);
            output[12 + j] = (short)((a - b + c - d + 2) >> 2);
        }
    }

    /// <summary>
    /// Inverse Hadamard 2x2 для chroma DC.
    /// </summary>
    public static void InverseHadamard2x2(ReadOnlySpan<short> input, Span<short> output)
    {
        var a = input[0] + input[1] + input[2] + input[3];
        var b = input[0] - input[1] + input[2] - input[3];
        var c = input[0] + input[1] - input[2] - input[3];
        var d = input[0] - input[1] - input[2] + input[3];

        output[0] = (short)a;
        output[1] = (short)b;
        output[2] = (short)c;
        output[3] = (short)d;
    }

    /// <summary>
    /// Forward 4x4 integer DCT.
    /// </summary>
    public static void ForwardDct4x4(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<int> tmp = stackalloc int[16];

        // Horizontal pass
        for (var i = 0; i < 4; i++)
        {
            var s0 = input[i * 4];
            var s1 = input[(i * 4) + 1];
            var s2 = input[(i * 4) + 2];
            var s3 = input[(i * 4) + 3];

            var d0 = s0 + s3;
            var d1 = s1 + s2;
            var d2 = s1 - s2;
            var d3 = s0 - s3;

            tmp[i * 4] = d0 + d1;
            tmp[(i * 4) + 1] = (d3 << 1) + d2;
            tmp[(i * 4) + 2] = d0 - d1;
            tmp[(i * 4) + 3] = d3 - (d2 << 1);
        }

        // Vertical pass
        for (var j = 0; j < 4; j++)
        {
            var d0 = tmp[j] + tmp[12 + j];
            var d1 = tmp[4 + j] + tmp[8 + j];
            var d2 = tmp[4 + j] - tmp[8 + j];
            var d3 = tmp[j] - tmp[12 + j];

            output[j] = (short)(d0 + d1);
            output[4 + j] = (short)((d3 << 1) + d2);
            output[8 + j] = (short)(d0 - d1);
            output[12 + j] = (short)(d3 - (d2 << 1));
        }
    }

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clip(int residual, byte prediction)
    {
        var val = prediction + residual;
        return (byte)Math.Clamp(val, 0, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipByte(int val) => (byte)Math.Clamp(val, 0, 255);

    #endregion
}
