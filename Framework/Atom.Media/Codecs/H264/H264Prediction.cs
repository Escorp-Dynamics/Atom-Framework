#pragma warning disable S109, S2325, S3776, CA1822, MA0051, IDE0045, IDE0047, IDE0048

using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// H.264 Intra Prediction (ITU-T H.264 Section 8.3.1–8.3.3).
/// </summary>
/// <remarks>
/// Intra 4×4: 9 режимов (Vertical, Horizontal, DC, DiagDownLeft, DiagDownRight,
///            VerticalRight, HorizontalDown, VerticalLeft, HorizontalUp).
/// Intra 16×16: 4 режима (Vertical, Horizontal, DC, Plane).
/// Intra Chroma 8×8: 4 режима (DC, Horizontal, Vertical, Plane).
/// </remarks>
internal static class H264Prediction
{
    #region Intra 4×4

    /// <summary>
    /// Intra 4×4 prediction.
    /// </summary>
    /// <param name="dst">4×4 destination block.</param>
    /// <param name="stride">Stride of destination buffer.</param>
    /// <param name="mode">Prediction mode (0–8).</param>
    /// <param name="above">4 pixels above (+ 4 above-right if available).</param>
    /// <param name="left">4 pixels to the left.</param>
    /// <param name="aboveLeft">Top-left corner pixel.</param>
    /// <param name="hasAbove">Whether above pixels are available.</param>
    /// <param name="hasLeft">Whether left pixels are available.</param>
    /// <param name="hasAboveRight">Whether above-right pixels are available.</param>
    public static void PredictIntra4x4(
        Span<byte> dst, int stride, int mode,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        byte aboveLeft, bool hasAbove, bool hasLeft, bool hasAboveRight)
    {
        switch (mode)
        {
            case 0: Intra4x4Vertical(dst, stride, above); break;
            case 1: Intra4x4Horizontal(dst, stride, left); break;
            case 2: Intra4x4Dc(dst, stride, above, left, hasAbove, hasLeft); break;
            case 3: Intra4x4DiagDownLeft(dst, stride, above, hasAboveRight); break;
            case 4: Intra4x4DiagDownRight(dst, stride, above, left, aboveLeft); break;
            case 5: Intra4x4VerticalRight(dst, stride, above, left, aboveLeft); break;
            case 6: Intra4x4HorizontalDown(dst, stride, above, left, aboveLeft); break;
            case 7: Intra4x4VerticalLeft(dst, stride, above, hasAboveRight); break;
            case 8: Intra4x4HorizontalUp(dst, stride, left); break;
        }
    }

    private static void Intra4x4Vertical(Span<byte> dst, int stride, ReadOnlySpan<byte> above)
    {
        for (var y = 0; y < 4; y++)
        {
            dst[(y * stride) + 0] = above[0];
            dst[(y * stride) + 1] = above[1];
            dst[(y * stride) + 2] = above[2];
            dst[(y * stride) + 3] = above[3];
        }
    }

    private static void Intra4x4Horizontal(Span<byte> dst, int stride, ReadOnlySpan<byte> left)
    {
        for (var y = 0; y < 4; y++)
        {
            dst[(y * stride) + 0] = left[y];
            dst[(y * stride) + 1] = left[y];
            dst[(y * stride) + 2] = left[y];
            dst[(y * stride) + 3] = left[y];
        }
    }

    private static void Intra4x4Dc(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        bool hasAbove, bool hasLeft)
    {
        var sum = 0;
        var count = 0;

        if (hasAbove)
        {
            sum += above[0] + above[1] + above[2] + above[3];
            count += 4;
        }

        if (hasLeft)
        {
            sum += left[0] + left[1] + left[2] + left[3];
            count += 4;
        }

        var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;

        for (var y = 0; y < 4; y++)
        {
            dst[(y * stride) + 0] = dc;
            dst[(y * stride) + 1] = dc;
            dst[(y * stride) + 2] = dc;
            dst[(y * stride) + 3] = dc;
        }
    }

    private static void Intra4x4DiagDownLeft(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, bool hasAboveRight)
    {
        // Extend above-right if not available
        Span<byte> a = stackalloc byte[8];
        above[..4].CopyTo(a);

        if (hasAboveRight && above.Length >= 8)
        {
            above[4..8].CopyTo(a[4..]);
        }
        else
        {
            a[4] = a[5] = a[6] = a[7] = above[3];
        }

        dst[0] = Avg3(a[0], a[1], a[2]);
        dst[1] = dst[stride] = Avg3(a[1], a[2], a[3]);
        dst[2] = dst[stride + 1] = dst[stride * 2] = Avg3(a[2], a[3], a[4]);
        dst[3] = dst[stride + 2] = dst[(stride * 2) + 1] = dst[stride * 3] = Avg3(a[3], a[4], a[5]);
        dst[stride + 3] = dst[(stride * 2) + 2] = dst[(stride * 3) + 1] = Avg3(a[4], a[5], a[6]);
        dst[(stride * 2) + 3] = dst[(stride * 3) + 2] = Avg3(a[5], a[6], a[7]);
        dst[(stride * 3) + 3] = Avg3(a[6], a[7], a[7]);
    }

    private static void Intra4x4DiagDownRight(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft)
    {
        dst[(stride * 3) + 0] = Avg3(left[3], left[2], left[1]);
        dst[(stride * 3) + 1] = dst[(stride * 2) + 0] = Avg3(left[2], left[1], left[0]);
        dst[(stride * 3) + 2] = dst[(stride * 2) + 1] = dst[stride] = Avg3(left[1], left[0], aboveLeft);
        dst[(stride * 3) + 3] = dst[(stride * 2) + 2] = dst[stride + 1] = dst[0] = Avg3(left[0], aboveLeft, above[0]);
        dst[(stride * 2) + 3] = dst[stride + 2] = dst[1] = Avg3(aboveLeft, above[0], above[1]);
        dst[stride + 3] = dst[2] = Avg3(above[0], above[1], above[2]);
        dst[3] = Avg3(above[1], above[2], above[3]);
    }

    private static void Intra4x4VerticalRight(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft)
    {
        dst[0] = dst[(stride * 2) + 1] = Avg2(aboveLeft, above[0]);
        dst[1] = dst[(stride * 2) + 2] = Avg2(above[0], above[1]);
        dst[2] = dst[(stride * 2) + 3] = Avg2(above[1], above[2]);
        dst[3] = Avg2(above[2], above[3]);

        dst[stride] = dst[(stride * 3) + 1] = Avg3(left[0], aboveLeft, above[0]);
        dst[stride + 1] = dst[(stride * 3) + 2] = Avg3(aboveLeft, above[0], above[1]);
        dst[stride + 2] = dst[(stride * 3) + 3] = Avg3(above[0], above[1], above[2]);
        dst[stride + 3] = Avg3(above[1], above[2], above[3]);

        dst[(stride * 2) + 0] = Avg3(aboveLeft, left[0], left[1]);
        dst[(stride * 3) + 0] = Avg3(left[0], left[1], left[2]);
    }

    private static void Intra4x4HorizontalDown(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft)
    {
        dst[0] = dst[stride + 2] = Avg2(aboveLeft, left[0]);
        dst[1] = dst[stride + 3] = Avg3(above[0], aboveLeft, left[0]);
        dst[2] = Avg3(aboveLeft, above[0], above[1]);
        dst[3] = Avg3(above[0], above[1], above[2]);

        dst[stride] = dst[(stride * 2) + 2] = Avg2(left[0], left[1]);
        dst[stride + 1] = dst[(stride * 2) + 3] = Avg3(aboveLeft, left[0], left[1]);

        dst[stride * 2] = dst[(stride * 3) + 2] = Avg2(left[1], left[2]);
        dst[(stride * 2) + 1] = dst[(stride * 3) + 3] = Avg3(left[0], left[1], left[2]);

        dst[stride * 3] = Avg2(left[2], left[3]);
        dst[(stride * 3) + 1] = Avg3(left[1], left[2], left[3]);
    }

    private static void Intra4x4VerticalLeft(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, bool hasAboveRight)
    {
        Span<byte> a = stackalloc byte[8];
        above[..4].CopyTo(a);

        if (hasAboveRight && above.Length >= 8)
        {
            above[4..8].CopyTo(a[4..]);
        }
        else
        {
            a[4] = a[5] = a[6] = a[7] = above[3];
        }

        dst[0] = Avg2(a[0], a[1]);
        dst[1] = dst[(stride * 2) + 0] = Avg2(a[1], a[2]);
        dst[2] = dst[(stride * 2) + 1] = Avg2(a[2], a[3]);
        dst[3] = dst[(stride * 2) + 2] = Avg2(a[3], a[4]);
        dst[(stride * 2) + 3] = Avg2(a[4], a[5]);

        dst[stride] = Avg3(a[0], a[1], a[2]);
        dst[stride + 1] = dst[(stride * 3) + 0] = Avg3(a[1], a[2], a[3]);
        dst[stride + 2] = dst[(stride * 3) + 1] = Avg3(a[2], a[3], a[4]);
        dst[stride + 3] = dst[(stride * 3) + 2] = Avg3(a[3], a[4], a[5]);
        dst[(stride * 3) + 3] = Avg3(a[4], a[5], a[6]);
    }

    private static void Intra4x4HorizontalUp(Span<byte> dst, int stride, ReadOnlySpan<byte> left)
    {
        dst[0] = Avg2(left[0], left[1]);
        dst[1] = Avg3(left[0], left[1], left[2]);
        dst[2] = dst[stride] = Avg2(left[1], left[2]);
        dst[3] = dst[stride + 1] = Avg3(left[1], left[2], left[3]);

        dst[stride + 2] = Avg2(left[2], left[3]);
        dst[stride + 3] = Avg3(left[2], left[3], left[3]);

        dst[(stride * 2) + 0] = dst[(stride * 2) + 1] = dst[(stride * 2) + 2] = dst[(stride * 2) + 3] = left[3];
        dst[(stride * 3) + 0] = dst[(stride * 3) + 1] = dst[(stride * 3) + 2] = dst[(stride * 3) + 3] = left[3];
    }

    #endregion

    #region Intra 16×16

    /// <summary>
    /// Intra 16×16 prediction.
    /// </summary>
    /// <param name="dst">16×16 destination block.</param>
    /// <param name="stride">Stride of destination buffer.</param>
    /// <param name="mode">Prediction mode: 0=Vert, 1=Horiz, 2=DC, 3=Plane.</param>
    /// <param name="above">16 pixels above.</param>
    /// <param name="left">16 pixels to the left.</param>
    /// <param name="aboveLeft">Top-left corner pixel.</param>
    /// <param name="hasAbove">Whether above pixels are available.</param>
    /// <param name="hasLeft">Whether left pixels are available.</param>
    public static void PredictIntra16x16(
        Span<byte> dst, int stride, int mode,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        byte aboveLeft, bool hasAbove, bool hasLeft)
    {
        switch (mode)
        {
            case 0: Intra16x16Vertical(dst, stride, above); break;
            case 1: Intra16x16Horizontal(dst, stride, left); break;
            case 2: Intra16x16Dc(dst, stride, above, left, hasAbove, hasLeft); break;
            case 3: Intra16x16Plane(dst, stride, above, left, aboveLeft); break;
        }
    }

    private static void Intra16x16Vertical(Span<byte> dst, int stride, ReadOnlySpan<byte> above)
    {
        for (var y = 0; y < 16; y++)
        {
            above[..16].CopyTo(dst[(y * stride)..]);
        }
    }

    private static void Intra16x16Horizontal(Span<byte> dst, int stride, ReadOnlySpan<byte> left)
    {
        for (var y = 0; y < 16; y++)
        {
            dst.Slice(y * stride, 16).Fill(left[y]);
        }
    }

    private static void Intra16x16Dc(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        bool hasAbove, bool hasLeft)
    {
        var sum = 0;
        var count = 0;

        if (hasAbove)
        {
            for (var x = 0; x < 16; x++)
            {
                sum += above[x];
            }

            count += 16;
        }

        if (hasLeft)
        {
            for (var y = 0; y < 16; y++)
            {
                sum += left[y];
            }

            count += 16;
        }

        var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;

        for (var y = 0; y < 16; y++)
        {
            dst.Slice(y * stride, 16).Fill(dc);
        }
    }

    private static void Intra16x16Plane(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft)
    {
        var h = 0;
        var v = 0;

        for (var i = 0; i < 7; i++)
        {
            h += (i + 1) * (above[8 + i] - above[6 - i]);
            v += (i + 1) * (left[8 + i] - left[6 - i]);
        }

        h += 8 * (above[15] - aboveLeft);
        v += 8 * (left[15] - aboveLeft);

        var a = 16 * (above[15] + left[15]);
        var b = (5 * h + 32) >> 6;
        var c = (5 * v + 32) >> 6;

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var val = (a + b * (x - 7) + c * (y - 7) + 16) >> 5;
                dst[(y * stride) + x] = ClipByte(val);
            }
        }
    }

    #endregion

    #region Intra Chroma 8×8

    /// <summary>
    /// Intra chroma 8×8 prediction.
    /// </summary>
    /// <param name="dst">8×8 destination block.</param>
    /// <param name="stride">Stride of destination buffer.</param>
    /// <param name="mode">Mode: 0=DC, 1=Horizontal, 2=Vertical, 3=Plane.</param>
    /// <param name="above">8 pixels above.</param>
    /// <param name="left">8 pixels to the left.</param>
    /// <param name="aboveLeft">Top-left corner pixel.</param>
    /// <param name="hasAbove">Whether above pixels are available.</param>
    /// <param name="hasLeft">Whether left pixels are available.</param>
    public static void PredictChroma8x8(
        Span<byte> dst, int stride, int mode,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        byte aboveLeft, bool hasAbove, bool hasLeft)
    {
        switch (mode)
        {
            case 0: Chroma8x8Dc(dst, stride, above, left, hasAbove, hasLeft); break;
            case 1: Chroma8x8Horizontal(dst, stride, left); break;
            case 2: Chroma8x8Vertical(dst, stride, above); break;
            case 3: Chroma8x8Plane(dst, stride, above, left, aboveLeft); break;
        }
    }

    private static void Chroma8x8Dc(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left,
        bool hasAbove, bool hasLeft)
    {
        // Each 4×4 sub-block has its own DC
        for (var by = 0; by < 2; by++)
        {
            for (var bx = 0; bx < 2; bx++)
            {
                var sum = 0;
                var count = 0;

                if (hasAbove)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        sum += above[(bx * 4) + x];
                    }

                    count += 4;
                }

                if (hasLeft)
                {
                    for (var y = 0; y < 4; y++)
                    {
                        sum += left[(by * 4) + y];
                    }

                    count += 4;
                }

                var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;

                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        dst[((by * 4 + y) * stride) + (bx * 4) + x] = dc;
                    }
                }
            }
        }
    }

    private static void Chroma8x8Horizontal(Span<byte> dst, int stride, ReadOnlySpan<byte> left)
    {
        for (var y = 0; y < 8; y++)
        {
            dst.Slice(y * stride, 8).Fill(left[y]);
        }
    }

    private static void Chroma8x8Vertical(Span<byte> dst, int stride, ReadOnlySpan<byte> above)
    {
        for (var y = 0; y < 8; y++)
        {
            above[..8].CopyTo(dst[(y * stride)..]);
        }
    }

    private static void Chroma8x8Plane(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft)
    {
        var h = 0;
        var v = 0;

        for (var i = 0; i < 3; i++)
        {
            h += (i + 1) * (above[4 + i] - above[2 - i]);
            v += (i + 1) * (left[4 + i] - left[2 - i]);
        }

        h += 4 * (above[7] - aboveLeft);
        v += 4 * (left[7] - aboveLeft);

        var a = 16 * (above[7] + left[7]);
        var b = (17 * h + 16) >> 5;
        var c = (17 * v + 16) >> 5;

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var val = (a + b * (x - 3) + c * (y - 3) + 16) >> 5;
                dst[(y * stride) + x] = ClipByte(val);
            }
        }
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg2(byte a, byte b) => (byte)((a + b + 1) >> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg3(byte a, byte b, byte c) => (byte)((a + (b << 1) + c + 2) >> 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipByte(int val) => (byte)Math.Clamp(val, 0, 255);

    #endregion
}
