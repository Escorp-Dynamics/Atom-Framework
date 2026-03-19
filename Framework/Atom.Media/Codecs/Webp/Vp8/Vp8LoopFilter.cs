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
        if (baseLevel >= 40)
        {
            hevThreshold = isKeyFrame ? 2 : 3;
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
    public static void FilterMbEdgeH(Span<byte> pixels, int offset, int stride, int size, in FilterParams p)
    {
        for (var i = 0; i < 8 * size; i++)
        {
            FilterMbEdge(pixels, offset + i, stride, p.FilterLevel + 2, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a vertical edge (filters horizontally adjacent pixels).
    /// Used for macroblock edges.
    /// </summary>
    public static void FilterMbEdgeV(Span<byte> pixels, int offset, int stride, int size, in FilterParams p)
    {
        for (var i = 0; i < 8 * size; i++)
        {
            FilterMbEdge(pixels, offset + (i * stride), 1, p.FilterLevel + 2, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a horizontal subblock edge (3 pixels each side).
    /// Used for inner subblock edges within a macroblock.
    /// </summary>
    public static void FilterSubEdgeH(Span<byte> pixels, int offset, int stride, int size, in FilterParams p)
    {
        for (var i = 0; i < 8 * size; i++)
        {
            FilterSubEdge(pixels, offset + i, stride, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Applies the normal loop filter to a vertical subblock edge.
    /// </summary>
    public static void FilterSubEdgeV(Span<byte> pixels, int offset, int stride, int size, in FilterParams p)
    {
        for (var i = 0; i < 8 * size; i++)
        {
            FilterSubEdge(pixels, offset + (i * stride), 1, p.FilterLevel, p.InteriorLimit, p.HevThreshold);
        }
    }

    /// <summary>
    /// Simple loop filter for horizontal macroblock edge.
    /// Only modifies the 2 pixels adjacent to the edge.
    /// </summary>
    public static void SimpleFilterEdgeH(Span<byte> pixels, int offset, int stride, int filterLimit)
    {
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

            if (!SimpleThreshold(p1, p0, q0, q1, filterLimit))
            {
                continue;
            }

            CommonAdjust(pixels, p1Idx, p0Idx, q0Idx, q1Idx, useOuterTaps: true);
        }
    }

    /// <summary>
    /// Simple loop filter for vertical macroblock edge.
    /// </summary>
    public static void SimpleFilterEdgeV(Span<byte> pixels, int offset, int stride, int filterLimit)
    {
        for (var i = 0; i < 16; i++)
        {
            var row = offset + (i * stride);
            var p1 = (int)pixels[row - 2];
            var p0 = (int)pixels[row - 1];
            var q0 = (int)pixels[row];
            var q1 = (int)pixels[row + 1];

            if (!SimpleThreshold(p1, p0, q0, q1, filterLimit))
            {
                continue;
            }

            CommonAdjust(pixels, row - 2, row - 1, row, row + 1, useOuterTaps: true);
        }
    }

    private static void FilterMbEdge(Span<byte> pixels, int idx, int step, int edgeLimit, int interiorLimit, int hevThreshold)
    {
        var p3 = (int)pixels[idx - (4 * step)];
        var p2 = (int)pixels[idx - (3 * step)];
        var p1 = (int)pixels[idx - (2 * step)];
        var p0 = (int)pixels[idx - step];
        var q0 = (int)pixels[idx];
        var q1 = (int)pixels[idx + step];
        var q2 = (int)pixels[idx + (2 * step)];
        var q3 = (int)pixels[idx + (3 * step)];

        if (!NormalThreshold(p3, p2, p1, p0, q0, q1, q2, q3, edgeLimit, interiorLimit))
        {
            return;
        }

        var hev = HighEdgeVariance(p1, p0, q0, q1, hevThreshold);

        if (hev)
        {
            CommonAdjust(pixels, idx - (2 * step), idx - step, idx, idx + step, useOuterTaps: true);
        }
        else
        {
            var w = Clamp128(Clamp128(p1 - q1) + 3 * (q0 - p0));
            var a = (27 * w + 63) >> 7;
            pixels[idx - step] = ClampByte(p0 + a);
            pixels[idx] = ClampByte(q0 - a);

            a = (18 * w + 63) >> 7;
            pixels[idx - (2 * step)] = ClampByte(p1 + a);
            pixels[idx + step] = ClampByte(q1 - a);

            a = (9 * w + 63) >> 7;
            pixels[idx - (3 * step)] = ClampByte(p2 + a);
            pixels[idx + (2 * step)] = ClampByte(q2 - a);
        }
    }

    private static void FilterSubEdge(Span<byte> pixels, int idx, int step, int edgeLimit, int interiorLimit, int hevThreshold)
    {
        var p3 = (int)pixels[idx - (4 * step)];
        var p2 = (int)pixels[idx - (3 * step)];
        var p1 = (int)pixels[idx - (2 * step)];
        var p0 = (int)pixels[idx - step];
        var q0 = (int)pixels[idx];
        var q1 = (int)pixels[idx + step];
        var q2 = (int)pixels[idx + (2 * step)];
        var q3 = (int)pixels[idx + (3 * step)];

        if (!NormalThreshold(p3, p2, p1, p0, q0, q1, q2, q3, edgeLimit, interiorLimit))
        {
            return;
        }

        var hev = HighEdgeVariance(p1, p0, q0, q1, hevThreshold);

        var a = CommonAdjust(pixels, idx - (2 * step), idx - step, idx, idx + step, useOuterTaps: hev);
        if (!hev)
        {
            a = (a + 1) >> 1;
            pixels[idx - (2 * step)] = ClampByte(p1 + a);
            pixels[idx + step] = ClampByte(q1 - a);
        }
    }

    // ── Helpers ──

    /// <summary>Edge detection: is filtering needed? RFC 6386 §15.2.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleThreshold(int p1, int p0, int q0, int q1, int filterLimit) =>
        ((2 * Abs(p0 - q0)) + (Abs(p1 - q1) >> 1)) <= filterLimit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NormalThreshold(int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3, int edgeLimit, int interiorLimit) =>
        SimpleThreshold(p1, p0, q0, q1, 2 * edgeLimit + interiorLimit)
        && Abs(p3 - p2) <= interiorLimit
        && Abs(p2 - p1) <= interiorLimit
        && Abs(p1 - p0) <= interiorLimit
        && Abs(q3 - q2) <= interiorLimit
        && Abs(q2 - q1) <= interiorLimit
        && Abs(q1 - q0) <= interiorLimit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HighEdgeVariance(int p1, int p0, int q0, int q1, int hevThreshold) =>
        Abs(p1 - p0) > hevThreshold || Abs(q1 - q0) > hevThreshold;

    private static int CommonAdjust(Span<byte> pixels, int p1Idx, int p0Idx, int q0Idx, int q1Idx, bool useOuterTaps)
    {
        var p1 = (int)pixels[p1Idx];
        var p0 = (int)pixels[p0Idx];
        var q0 = (int)pixels[q0Idx];
        var q1 = (int)pixels[q1Idx];

        var a = Clamp128((useOuterTaps ? Clamp128(p1 - q1) : 0) + 3 * (q0 - p0));
        var b = Clamp128(a + 3) >> 3;
        a = Clamp128(a + 4) >> 3;

        pixels[q0Idx] = ClampByte(q0 - a);
        pixels[p0Idx] = ClampByte(p0 + b);
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Abs(int v) => v < 0 ? -v : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp128(int v) => Math.Clamp(v, -128, 127);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int v) =>
        (byte)Math.Clamp(v, 0, 255);
}
