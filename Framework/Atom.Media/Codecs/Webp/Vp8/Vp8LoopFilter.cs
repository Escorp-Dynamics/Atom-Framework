#pragma warning disable IDE0045, IDE0048, S109

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 loop filter per RFC 6386 §15 / §20.6.
/// Normal and simple loop filters with 4-tap and 2-tap variants.
/// </summary>
internal static class Vp8LoopFilter
{
    /// <summary>
    /// Filter parameters computed per macroblock.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct FilterParams
    {
        public int FilterLevel { get; init; }
        public int InteriorLimit { get; init; }
        public int HevThreshold { get; init; }
    }

    /// <summary>
    /// Computes filter parameters for a given macroblock.
    /// </summary>
    public static FilterParams ComputeParams(int baseLevel, int sharpnessLevel, bool isKeyFrame)
    {
        // Interior limit
        var interiorLimit = baseLevel;
        if (sharpnessLevel > 0)
        {
            interiorLimit >>= sharpnessLevel > 4 ? 2 : 1;
            if (interiorLimit > 9 - sharpnessLevel)
            {
                interiorLimit = 9 - sharpnessLevel;
            }
        }

        // HEV threshold
        int hevThreshold;
        if (isKeyFrame)
        {
            hevThreshold = 0;
        }
        else if (baseLevel >= 40)
        {
            hevThreshold = 2;
        }
        else if (baseLevel >= 15)
        {
            hevThreshold = 1;
        }
        else
        {
            hevThreshold = 0;
        }

        return new FilterParams
        {
            FilterLevel = baseLevel,
            InteriorLimit = Math.Max(interiorLimit, 1),
            HevThreshold = hevThreshold,
        };
    }

    /// <summary>
    /// Applies the normal loop filter to a horizontal edge (filters vertically adjacent pixels).
    /// Processes 4 pixels on each side of the edge. Used for macroblock edges.
    /// </summary>
    public static void FilterMbEdgeH(Span<byte> pixels, int offset, int stride, in FilterParams p)
    {
        for (var i = 0; i < 16; i++)
        {
            FilterNormal4(pixels, offset + i, stride, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a vertical edge (filters horizontally adjacent pixels).
    /// Used for macroblock edges.
    /// </summary>
    public static void FilterMbEdgeV(Span<byte> pixels, int offset, int stride, in FilterParams p)
    {
        for (var i = 0; i < 16; i++)
        {
            FilterNormal4(pixels, offset + (i * stride), 1, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a horizontal subblock edge (3 pixels each side).
    /// Used for inner subblock edges within a macroblock.
    /// </summary>
    public static void FilterSubEdgeH(Span<byte> pixels, int offset, int stride, in FilterParams p)
    {
        for (var i = 0; i < 16; i++)
        {
            FilterNormal3(pixels, offset + i, stride, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a vertical subblock edge.
    /// </summary>
    public static void FilterSubEdgeV(Span<byte> pixels, int offset, int stride, in FilterParams p)
    {
        for (var i = 0; i < 16; i++)
        {
            FilterNormal3(pixels, offset + (i * stride), 1, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Simple loop filter for horizontal macroblock edge.
    /// Only modifies the 2 pixels adjacent to the edge.
    /// </summary>
    public static void SimpleFilterMbEdgeH(Span<byte> pixels, int offset, int stride, int filterLevel)
    {
        var limit = (2 * filterLevel) + (filterLevel >> 2);

        for (var i = 0; i < 16; i++)
        {
            var p1Idx = offset - (2 * stride) + i;
            var p0Idx = offset - stride + i;
            var q0Idx = offset + i;
            var q1Idx = offset + stride + i;

            var p1 = (int)pixels[p1Idx];
            var p0 = (int)pixels[p0Idx];
            var q0 = (int)pixels[q0Idx];
            var q1 = (int)pixels[q1Idx];

            if (!FilterYes(p1, p0, q0, q1, limit))
            {
                continue;
            }

            var a = Clamp128(3 * (q0 - p0));

            var a1 = (a + 4) >> 3;
            var a2 = (a + 3) >> 3;

            pixels[q0Idx] = ClampByte(q0 - a1);
            pixels[p0Idx] = ClampByte(p0 + a2);
        }
    }

    /// <summary>
    /// Simple loop filter for vertical macroblock edge.
    /// </summary>
    public static void SimpleFilterMbEdgeV(Span<byte> pixels, int offset, int stride, int filterLevel)
    {
        var limit = (2 * filterLevel) + (filterLevel >> 2);

        for (var i = 0; i < 16; i++)
        {
            var row = offset + (i * stride);
            var p1 = (int)pixels[row - 2];
            var p0 = (int)pixels[row - 1];
            var q0 = (int)pixels[row];
            var q1 = (int)pixels[row + 1];

            if (!FilterYes(p1, p0, q0, q1, limit))
            {
                continue;
            }

            var a = Clamp128(3 * (q0 - p0));

            var a1 = (a + 4) >> 3;
            var a2 = (a + 3) >> 3;

            pixels[row] = ClampByte(q0 - a1);
            pixels[row - 1] = ClampByte(p0 + a2);
        }
    }

    // ── Normal filter (4-tap) for macroblock edges ──

    private static void FilterNormal4(Span<byte> pixels, int idx, int step, int filterLevel, int interiorLimit, int hevThreshold)
    {
        var limit = (2 * filterLevel) + interiorLimit;

        var p3 = (int)pixels[idx - (4 * step)];
        var p2 = (int)pixels[idx - (3 * step)];
        var p1 = (int)pixels[idx - (2 * step)];
        var p0 = (int)pixels[idx - step];
        var q0 = (int)pixels[idx];
        var q1 = (int)pixels[idx + step];
        var q2 = (int)pixels[idx + (2 * step)];
        var q3 = (int)pixels[idx + (3 * step)];

        if (!FilterYes(p1, p0, q0, q1, limit))
        {
            return;
        }

        if (Abs(p3 - p2) > interiorLimit || Abs(p2 - p1) > interiorLimit ||
            Abs(q2 - q1) > interiorLimit || Abs(q3 - q2) > interiorLimit)
        {
            return;
        }

        var hev = Abs(p1 - p0) > hevThreshold || Abs(q1 - q0) > hevThreshold;

        if (hev)
        {
            // Same as simple filter
            var a = Clamp128(3 * (q0 - p0));
            var a1 = (a + 4) >> 3;
            var a2 = (a + 3) >> 3;
            pixels[idx] = ClampByte(q0 - a1);
            pixels[idx - step] = ClampByte(p0 + a2);
        }
        else
        {
            // Wider filter (4 pixels)
            var a = Clamp128(3 * (q0 - p0));

            var a1 = (a + 4) >> 3;
            var a2 = (a + 3) >> 3;
            var a3 = (a1 + 1) >> 1;

            pixels[idx] = ClampByte(q0 - a1);
            pixels[idx - step] = ClampByte(p0 + a2);
            pixels[idx + step] = ClampByte(q1 - a3);
            pixels[idx - (2 * step)] = ClampByte(p1 + a3);
        }
    }

    // ── Normal filter (3-tap) for subblock edges ──

    private static void FilterNormal3(Span<byte> pixels, int idx, int step, int filterLevel, int interiorLimit, int hevThreshold)
    {
        var limit = (2 * filterLevel) + interiorLimit;

        var p2 = (int)pixels[idx - (3 * step)];
        var p1 = (int)pixels[idx - (2 * step)];
        var p0 = (int)pixels[idx - step];
        var q0 = (int)pixels[idx];
        var q1 = (int)pixels[idx + step];
        var q2 = (int)pixels[idx + (2 * step)];

        if (!FilterYes(p1, p0, q0, q1, limit))
        {
            return;
        }

        if (Abs(p2 - p1) > interiorLimit || Abs(q2 - q1) > interiorLimit)
        {
            return;
        }

        var hev = Abs(p1 - p0) > hevThreshold || Abs(q1 - q0) > hevThreshold;

        if (hev)
        {
            var a = Clamp128(3 * (q0 - p0));
            var a1 = (a + 4) >> 3;
            var a2 = (a + 3) >> 3;
            pixels[idx] = ClampByte(q0 - a1);
            pixels[idx - step] = ClampByte(p0 + a2);
        }
        else
        {
            // 3-tap subblock filter: adjust p0, q0 with weaker correction + p1, q1
            var a = Clamp128(3 * (q0 - p0));
            var a1 = ((27 * a) + 63) >> 7;
            var a2 = ((18 * a) + 63) >> 7;
            var a3 = ((9 * a) + 63) >> 7;

            pixels[idx] = ClampByte(q0 - a1);
            pixels[idx - step] = ClampByte(p0 + a1);
            pixels[idx + step] = ClampByte(q1 - a2);
            pixels[idx - (2 * step)] = ClampByte(p1 + a2);
            pixels[idx + (2 * step)] = ClampByte(q2 - a3);
            pixels[idx - (3 * step)] = ClampByte(p2 + a3);
        }
    }

    // ── Helpers ──

    /// <summary>Edge detection: is filtering needed? RFC 6386 §15.2.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FilterYes(int p1, int p0, int q0, int q1, int limit) =>
        ((2 * Abs(p0 - q0)) + (Abs(p1 - q1) >> 1)) <= limit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Abs(int v) => v < 0 ? -v : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp128(int v) => Math.Clamp(v, -128, 127);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int v) =>
        (byte)Math.Clamp(v, 0, 255);
}
