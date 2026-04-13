#pragma warning disable IDE0045, IDE0048, S109, S2234

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 intra prediction modes per RFC 6386 §12, §20.14.
/// 16×16 luma, 8×8 chroma, and 4×4 subblock prediction.
/// </summary>
internal static class Vp8Prediction
{
    // ── 16×16 Intra prediction (DC_PRED, V_PRED, H_PRED, TM_PRED) ──

    /// <summary>16×16 DC prediction: fill with average of top + left (or 128 if unavailable).</summary>
    public static void Predict16x16Dc(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, bool hasAbove, bool hasLeft, Span<byte> dst, int stride)
    {
        var sum = 0;
        var count = 0;

        if (hasAbove)
        {
            for (var i = 0; i < 16; i++)
            {
                sum += above[i];
            }

            count += 16;
        }

        if (hasLeft)
        {
            for (var i = 0; i < 16; i++)
            {
                sum += left[i];
            }

            count += 16;
        }

        var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;
        Fill16x16(dst, stride, dc);
    }

    /// <summary>16×16 V (vertical) prediction: copy top row to all rows.</summary>
    public static void Predict16x16V(ReadOnlySpan<byte> above, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 16; y++)
        {
            above[..16].CopyTo(dst.Slice(y * stride, 16));
        }
    }

    /// <summary>16×16 H (horizontal) prediction: fill each row with left pixel.</summary>
    public static void Predict16x16H(ReadOnlySpan<byte> left, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 16; y++)
        {
            dst.Slice(y * stride, 16).Fill(left[y]);
        }
    }

    /// <summary>16×16 TM (TrueMotion) prediction: pred[y][x] = clamp(left[y] + above[x] - above_left).</summary>
    public static void Predict16x16Tm(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 16; y++)
        {
            var l = (int)left[y];
            var off = y * stride;
            for (var x = 0; x < 16; x++)
            {
                dst[off + x] = ClampByte(l + above[x] - aboveLeft);
            }
        }
    }

    // ── 8×8 Chroma intra prediction (DC_PRED, V_PRED, H_PRED, TM_PRED) ──

    /// <summary>8×8 DC prediction for chroma.</summary>
    public static void Predict8x8Dc(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, bool hasAbove, bool hasLeft, Span<byte> dst, int stride)
    {
        var sum = 0;
        var count = 0;

        if (hasAbove)
        {
            for (var i = 0; i < 8; i++)
            {
                sum += above[i];
            }

            count += 8;
        }

        if (hasLeft)
        {
            for (var i = 0; i < 8; i++)
            {
                sum += left[i];
            }

            count += 8;
        }

        var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;
        Fill8x8(dst, stride, dc);
    }

    /// <summary>8×8 V (vertical) prediction for chroma.</summary>
    public static void Predict8x8V(ReadOnlySpan<byte> above, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 8; y++)
        {
            above[..8].CopyTo(dst.Slice(y * stride, 8));
        }
    }

    /// <summary>8×8 H (horizontal) prediction for chroma.</summary>
    public static void Predict8x8H(ReadOnlySpan<byte> left, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 8; y++)
        {
            dst.Slice(y * stride, 8).Fill(left[y]);
        }
    }

    /// <summary>8×8 TM prediction for chroma.</summary>
    public static void Predict8x8Tm(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 8; y++)
        {
            var l = (int)left[y];
            var off = y * stride;
            for (var x = 0; x < 8; x++)
            {
                dst[off + x] = ClampByte(l + above[x] - aboveLeft);
            }
        }
    }

    // ── 4×4 Subblock intra prediction (B_DC_PRED through B_HU_PRED) ──

    /// <summary>
    /// Dispatches to the appropriate 4×4 subblock prediction mode.
    /// <paramref name="above"/> = 8 pixels (4 above + 4 above-right).
    /// <paramref name="left"/> = 4 pixels (left column).
    /// <paramref name="aboveLeft"/> = the top-left corner pixel.
    /// </summary>
    public static void Predict4x4(int mode, ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        byte aboveLeft, bool hasAbove, bool hasLeft, Span<byte> dst, int stride)
    {
        _ = hasAbove;
        _ = hasLeft;

        switch (mode)
        {
            case Vp8Constants.BDcPred:
                Predict4x4Dc(above, left, dst, stride);
                break;
            case Vp8Constants.BTmPred:
                Predict4x4Tm(above, left, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BVePred:
                Predict4x4Ve(above, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BHePred:
                Predict4x4He(left, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BLdPred:
                Predict4x4Ld(above, dst, stride);
                break;
            case Vp8Constants.BRdPred:
                Predict4x4Rd(above, left, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BVrPred:
                Predict4x4Vr(above, left, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BVlPred:
                Predict4x4Vl(above, dst, stride);
                break;
            case Vp8Constants.BHdPred:
                Predict4x4Hd(above, left, aboveLeft, dst, stride);
                break;
            case Vp8Constants.BHuPred:
                Predict4x4Hu(left, dst, stride);
                break;
        }
    }

    /// <summary>4×4 DC prediction.</summary>
    private static void Predict4x4Dc(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, Span<byte> dst, int stride)
    {
        var sum = 0;
        for (var i = 0; i < 4; i++)
        {
            sum += above[i];
        }

        for (var i = 0; i < 4; i++)
        {
            sum += left[i];
        }

        var dc = (byte)((sum + 4) >> 3);
        Fill4x4(dst, stride, dc);
    }

    /// <summary>4×4 TM prediction: pred[y][x] = clamp(left[y] + above[x] - above_left).</summary>
    private static void Predict4x4Tm(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        for (var y = 0; y < 4; y++)
        {
            var l = (int)left[y];
            var off = y * stride;
            for (var x = 0; x < 4; x++)
            {
                dst[off + x] = ClampByte(l + above[x] - aboveLeft);
            }
        }
    }

    /// <summary>4×4 VE (vertical-ish) prediction with smoothing. RFC 6386 §12.3.</summary>
    private static void Predict4x4Ve(ReadOnlySpan<byte> above, byte aboveLeft, Span<byte> dst, int stride)
    {
        var p0 = Avg3(aboveLeft, above[0], above[1]);
        var p1 = Avg3(above[0], above[1], above[2]);
        var p2 = Avg3(above[1], above[2], above[3]);
        var p3 = Avg3(above[2], above[3], above[4]);

        for (var y = 0; y < 4; y++)
        {
            var off = y * stride;
            dst[off] = p0;
            dst[off + 1] = p1;
            dst[off + 2] = p2;
            dst[off + 3] = p3;
        }
    }

    /// <summary>4×4 HE (horizontal-ish) prediction with smoothing.</summary>
    private static void Predict4x4He(ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        var p0 = Avg3(aboveLeft, left[0], left[1]);
        var p1 = Avg3(left[0], left[1], left[2]);
        var p2 = Avg3(left[1], left[2], left[3]);
        var p3 = Avg3(left[2], left[3], left[3]);

        dst.Slice(0 * stride, 4).Fill(p0);
        dst.Slice(1 * stride, 4).Fill(p1);
        dst.Slice(2 * stride, 4).Fill(p2);
        dst.Slice(3 * stride, 4).Fill(p3);
    }

    /// <summary>4×4 LD (left-down diagonal) prediction.</summary>
    private static void Predict4x4Ld(ReadOnlySpan<byte> above, Span<byte> dst, int stride)
    {
        var a = above[0];
        var b = above[1];
        var c = above[2];
        var d = above[3];
        var e = above[4];
        var f = above[5];
        var g = above[6];
        var h = above[7];

        dst[0 * stride] = Avg3(a, b, c);
        dst[(0 * stride) + 1] = dst[1 * stride] = Avg3(b, c, d);
        dst[(0 * stride) + 2] = dst[(1 * stride) + 1] = dst[2 * stride] = Avg3(c, d, e);
        dst[(0 * stride) + 3] = dst[(1 * stride) + 2] = dst[(2 * stride) + 1] = dst[3 * stride] = Avg3(d, e, f);
        dst[(1 * stride) + 3] = dst[(2 * stride) + 2] = dst[(3 * stride) + 1] = Avg3(e, f, g);
        dst[(2 * stride) + 3] = dst[(3 * stride) + 2] = Avg3(f, g, h);
        dst[(3 * stride) + 3] = Avg3(g, h, h);
    }

    /// <summary>4×4 RD (right-down diagonal) prediction.</summary>
    private static void Predict4x4Rd(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        var i = left[0];
        var j = left[1];
        var k = left[2];
        var l = left[3];
        var x = aboveLeft;
        var a = above[0];
        var b = above[1];
        var c = above[2];
        var d = above[3];

        dst[3 * stride] = Avg3(j, k, l);
        dst[2 * stride] = dst[(3 * stride) + 1] = Avg3(i, j, k);
        dst[1 * stride] = dst[(2 * stride) + 1] = dst[(3 * stride) + 2] = Avg3(x, i, j);
        dst[0 * stride] = dst[(1 * stride) + 1] = dst[(2 * stride) + 2] = dst[(3 * stride) + 3] = Avg3(a, x, i);
        dst[(0 * stride) + 1] = dst[(1 * stride) + 2] = dst[(2 * stride) + 3] = Avg3(b, a, x);
        dst[(0 * stride) + 2] = dst[(1 * stride) + 3] = Avg3(c, b, a);
        dst[(0 * stride) + 3] = Avg3(d, c, b);
    }

    /// <summary>4×4 VR (vertical-right) prediction.</summary>
    private static void Predict4x4Vr(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        var i = left[0];
        var j = left[1];
        var x = aboveLeft;
        var a = above[0];
        var b = above[1];
        var c = above[2];
        var d = above[3];

        dst[3 * stride] = Avg3(j, i, x);
        dst[2 * stride] = Avg3(i, x, a);
        dst[1 * stride] = dst[(3 * stride) + 1] = Avg3(x, a, b);
        dst[0 * stride] = dst[(2 * stride) + 1] = Avg2(x, a);
        dst[(1 * stride) + 1] = dst[(3 * stride) + 2] = Avg3(a, b, c);
        dst[(0 * stride) + 1] = dst[(2 * stride) + 2] = Avg2(a, b);
        dst[(1 * stride) + 2] = dst[(3 * stride) + 3] = Avg3(b, c, d);
        dst[(0 * stride) + 2] = dst[(2 * stride) + 3] = Avg2(b, c);
        dst[(1 * stride) + 3] = Avg3(c, d, d);
        dst[(0 * stride) + 3] = Avg2(c, d);
    }

    /// <summary>4×4 VL (vertical-left) prediction.</summary>
    private static void Predict4x4Vl(ReadOnlySpan<byte> above, Span<byte> dst, int stride)
    {
        var a = above[0];
        var b = above[1];
        var c = above[2];
        var d = above[3];
        var e = above[4];
        var f = above[5];
        var g = above[6];

        dst[0 * stride] = Avg2(a, b);
        dst[(0 * stride) + 1] = dst[2 * stride] = Avg2(b, c);
        dst[(0 * stride) + 2] = dst[(2 * stride) + 1] = Avg2(c, d);
        dst[(0 * stride) + 3] = dst[(2 * stride) + 2] = Avg2(d, e);
        dst[(2 * stride) + 3] = Avg2(e, f);

        dst[1 * stride] = Avg3(a, b, c);
        dst[(1 * stride) + 1] = dst[3 * stride] = Avg3(b, c, d);
        dst[(1 * stride) + 2] = dst[(3 * stride) + 1] = Avg3(c, d, e);
        dst[(1 * stride) + 3] = dst[(3 * stride) + 2] = Avg3(d, e, f);
        dst[(3 * stride) + 3] = Avg3(e, f, g);
    }

    /// <summary>4×4 HD (horizontal-down) prediction.</summary>
    private static void Predict4x4Hd(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, Span<byte> dst, int stride)
    {
        var i = left[0];
        var j = left[1];
        var k = left[2];
        var l = left[3];
        var x = aboveLeft;
        var a = above[0];
        var b = above[1];
        var c = above[2];

        dst[3 * stride] = Avg2(k, l);
        dst[(3 * stride) + 1] = Avg3(j, k, l);
        dst[2 * stride] = dst[(3 * stride) + 2] = Avg2(j, k);
        dst[(2 * stride) + 1] = dst[(3 * stride) + 3] = Avg3(i, j, k);
        dst[1 * stride] = dst[(2 * stride) + 2] = Avg2(i, j);
        dst[(1 * stride) + 1] = dst[(2 * stride) + 3] = Avg3(x, i, j);
        dst[0 * stride] = dst[(1 * stride) + 2] = Avg2(x, i);
        dst[(0 * stride) + 1] = dst[(1 * stride) + 3] = Avg3(a, x, i);
        dst[(0 * stride) + 2] = Avg3(b, a, x);
        dst[(0 * stride) + 3] = Avg3(c, b, a);
    }

    /// <summary>4×4 HU (horizontal-up) prediction.</summary>
    private static void Predict4x4Hu(ReadOnlySpan<byte> left, Span<byte> dst, int stride)
    {
        var i = left[0];
        var j = left[1];
        var k = left[2];
        var l = left[3];

        dst[0 * stride] = Avg2(i, j);
        dst[(0 * stride) + 1] = Avg3(i, j, k);
        dst[(0 * stride) + 2] = dst[1 * stride] = Avg2(j, k);
        dst[(0 * stride) + 3] = dst[(1 * stride) + 1] = Avg3(j, k, l);
        dst[(1 * stride) + 2] = dst[2 * stride] = Avg2(k, l);
        dst[(1 * stride) + 3] = dst[(2 * stride) + 1] = Avg3(k, l, l);
        dst[(2 * stride) + 2] = dst[(2 * stride) + 3] = dst[3 * stride] = dst[(3 * stride) + 1] =
            dst[(3 * stride) + 2] = dst[(3 * stride) + 3] = l;
    }

    // ── Helpers ───────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg2(byte a, byte b) => (byte)((a + b + 1) >> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg3(byte a, byte b, byte c) => (byte)((a + b + b + c + 2) >> 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Fill16x16(Span<byte> dst, int stride, byte value)
    {
        for (var y = 0; y < 16; y++)
        {
            dst.Slice(y * stride, 16).Fill(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Fill8x8(Span<byte> dst, int stride, byte value)
    {
        for (var y = 0; y < 8; y++)
        {
            dst.Slice(y * stride, 8).Fill(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Fill4x4(Span<byte> dst, int stride, byte value)
    {
        for (var y = 0; y < 4; y++)
        {
            dst.Slice(y * stride, 4).Fill(value);
        }
    }
}
