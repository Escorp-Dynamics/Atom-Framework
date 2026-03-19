using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 quantization and dequantization per RFC 6386 §9, §20.3.
/// Manages per-segment dequantization factors for all block types.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Vp8QuantMatrix
{
    /// <summary>Y DC dequant factor.</summary>
    public short Y1DcDequant;

    /// <summary>Y AC dequant factor.</summary>
    public short Y1AcDequant;

    /// <summary>Y2 DC dequant factor.</summary>
    public short Y2DcDequant;

    /// <summary>Y2 AC dequant factor.</summary>
    public short Y2AcDequant;

    /// <summary>UV DC dequant factor.</summary>
    public short UvDcDequant;

    /// <summary>UV AC dequant factor.</summary>
    public short UvAcDequant;
}

/// <summary>
/// VP8 quantization helpers.
/// </summary>
internal static class Vp8Quantization
{
    /// <summary>
    /// Clamps QP to valid range [0, 127].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampQp(int qp) => Math.Clamp(qp, 0, 127);

    /// <summary>
    /// Builds dequantization matrix for a given base QP plus per-type deltas.
    /// Per RFC 6386 §9.6: each type has its own delta (y1_dc, y1_ac, y2_dc, y2_ac, uv_dc, uv_ac).
    /// </summary>
    public static Vp8QuantMatrix BuildDequantMatrix(int baseQp, int y1DcDelta, int y2DcDelta,
        int y2AcDelta, int uvDcDelta, int uvAcDelta)
    {
        Vp8QuantMatrix m;
        m.Y1DcDequant = Vp8Constants.DcQLookup[ClampQp(baseQp + y1DcDelta)];
        m.Y1AcDequant = Vp8Constants.AcQLookup[ClampQp(baseQp)];
        m.Y2DcDequant = (short)(Vp8Constants.DcQLookup[ClampQp(baseQp + y2DcDelta)] * 2);
        m.Y2AcDequant = (short)Math.Max(Vp8Constants.AcQLookup[ClampQp(baseQp + y2AcDelta)] * 155 / 100, 8);
        m.UvDcDequant = (short)Math.Min((int)Vp8Constants.DcQLookup[ClampQp(baseQp + uvDcDelta)], 132);
        m.UvAcDequant = Vp8Constants.AcQLookup[ClampQp(baseQp + uvAcDelta)];
        return m;
    }

    /// <summary>
    /// Dequantizes a 4×4 block of coefficients in-place.
    /// dc_factor applies to coefficient[0], ac_factor to coefficients[1..15].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Dequantize(Span<short> coeffs, short dcFactor, short acFactor)
    {
        coeffs[0] = (short)(coeffs[0] * dcFactor);
        for (var i = 1; i < 16; i++)
        {
            coeffs[i] = (short)(coeffs[i] * acFactor);
        }
    }

    /// <summary>
    /// Quantizes a 4×4 block of coefficients in-place (encoder path).
    /// Returns the number of non-zero coefficients after quantization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Quantize(Span<short> coeffs, short dcFactor, short acFactor)
    {
        var nonZero = 0;

        coeffs[0] = Divide(coeffs[0], dcFactor);
        if (coeffs[0] != 0)
        {
            nonZero++;
        }

        for (var i = 1; i < 16; i++)
        {
            coeffs[i] = Divide(coeffs[i], acFactor);
            if (coeffs[i] != 0)
            {
                nonZero++;
            }
        }

        return nonZero;
    }

    /// <summary>Signed division with rounding toward zero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short Divide(short value, short divisor)
    {
        if (value == 0)
        {
            return 0;
        }

        var sign = value < 0 ? -1 : 1;
        var abs = Math.Abs(value);
        return (short)(sign * ((abs + (divisor >> 1)) / divisor));
    }
}
