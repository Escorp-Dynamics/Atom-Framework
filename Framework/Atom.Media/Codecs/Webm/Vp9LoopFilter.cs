#pragma warning disable IDE0045, IDE0048, IDE0055, IDE0059, RCS1242, S109, S1481, S3776, MA0051

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 loop filter (deblocking) per VP9 specification §10.3.
/// Applies horizontal and vertical edge filtering to remove blocking artifacts.
/// </summary>
/// <remarks>
/// VP9 loop filter operates on 8-pixel edges with variable filter strength.
/// Filter levels can vary per-segment and per-reference frame.
/// Three filter widths: 2-tap, 4-tap (normal), and 8-tap (wide) selectable per edge.
/// </remarks>
internal static class Vp9LoopFilter
{
    #region Filter Parameters

    /// <summary>
    /// Loop filter parameters for a frame.
    /// </summary>
    internal struct FilterParams
    {
        public int FilterLevel;
        public int SharpnessLevel;
        public bool ModeRefDeltaEnabled;
        public int[] RefDeltas;     // [4] indexed by reference frame type
        public int[] ModeDeltas;    // [2] indexed by inter-mode category

        public static FilterParams Create()
        {
            return new FilterParams
            {
                RefDeltas = new int[Vp9Constants.NumRefFrames],
                ModeDeltas = new int[2],
            };
        }
    }

    /// <summary>
    /// Computes effective filter level for a block given segment/ref/mode.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ComputeFilterLevel(in FilterParams p, int segmentLevel, int refFrame, int modeCategory)
    {
        var level = segmentLevel;

        if (p.ModeRefDeltaEnabled)
        {
            level += p.RefDeltas[refFrame];
            level += p.ModeDeltas[modeCategory];
        }

        return Math.Clamp(level, 0, Vp9Constants.MaxLoopFilterLevel);
    }

    /// <summary>
    /// Computes filter limit and blimit from level and sharpness.
    /// VP9 spec §10.3.2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ComputeThresholds(int level, int sharpness, out int limit, out int blimit, out int thresh)
    {
        // limit depends on sharpness
        if (sharpness == 0)
        {
            limit = level;
        }
        else if (sharpness <= 4)
        {
            limit = Math.Min(level >> (sharpness + 1), 9 - sharpness);
            limit = Math.Max(limit, 1);
        }
        else
        {
            limit = Math.Min(level >> 2, 9 - sharpness);
            limit = Math.Max(limit, 1);
        }

        blimit = 2 * (level + 2) + limit;
        thresh = level >> 4;
    }

    #endregion

    #region Simple Filter (2-tap)

    /// <summary>
    /// Simple 2-tap loop filter for a single edge (4 or 8 pixel span).
    /// Modifies pixels p0,p1,q0,q1 (the two pixels on each side of the edge).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SimpleFilter2(Span<byte> buf, int offset, int step, int blimit)
    {
        var p1 = buf[offset - 2 * step];
        var p0 = buf[offset - step];
        var q0 = buf[offset];
        var q1 = buf[offset + step];

        // Check mask: |p0-q0|*2 + |p1-q1|/2 <= blimit
        var mask = (Math.Abs(p0 - q0) * 2 + (Math.Abs(p1 - q1) >> 1)) <= blimit;
        if (!mask) return;

        // Filter
        var f = Clamp3(p0 - q0 + Clamp3(3 * (q0 - p0)));
        var f1 = Math.Min(f + 4, 127) >> 3;
        var f2 = Math.Min(f + 3, 127) >> 3;

        buf[offset - step] = ClampByte(p0 + f2);
        buf[offset] = ClampByte(q0 - f1);
    }

    #endregion

    #region Normal Filter (4-tap)

    /// <summary>
    /// Normal (4-tap) loop filter for a single edge.
    /// Modifies p1,p0,q0,q1.
    /// </summary>
    internal static void NormalFilter4(Span<byte> buf, int offset, int step, int limit, int blimit, int thresh)
    {
        var p3 = buf[offset - 4 * step];
        var p2 = buf[offset - 3 * step];
        var p1 = buf[offset - 2 * step];
        var p0 = buf[offset - step];
        var q0 = buf[offset];
        var q1 = buf[offset + step];
        var q2 = buf[offset + 2 * step];
        var q3 = buf[offset + 3 * step];

        // Edge mask
        if (!FilterMask(p3, p2, p1, p0, q0, q1, q2, q3, limit, blimit))
            return;

        // Flat check: all |p_i - p0| <= 1 and |q_i - q0| <= 1 for i in {1,2,3}
        var flat = IsFlat(p3, p2, p1, p0, q0, q1, q2, q3, thresh);

        if (flat)
        {
            // Wide filter: 7-tap for p2..q2
            buf[offset - 3 * step] = ClampByte((p3 + p3 + p3 + 2 * p2 + p1 + p0 + q0 + 4) >> 3);
            buf[offset - 2 * step] = ClampByte((p3 + p3 + p2 + 2 * p1 + p0 + q0 + q1 + 4) >> 3);
            buf[offset - step] = ClampByte((p3 + p2 + p1 + 2 * p0 + q0 + q1 + q2 + 4) >> 3);
            buf[offset] = ClampByte((p2 + p1 + p0 + 2 * q0 + q1 + q2 + q3 + 4) >> 3);
            buf[offset + step] = ClampByte((p1 + p0 + q0 + 2 * q1 + q2 + q3 + q3 + 4) >> 3);
            buf[offset + 2 * step] = ClampByte((p0 + q0 + q1 + 2 * q2 + q3 + q3 + q3 + 4) >> 3);
        }
        else
        {
            // Narrow filter
            var hev = Math.Abs(p1 - p0) > thresh || Math.Abs(q1 - q0) > thresh;
            NarrowFilter(buf, offset, step, hev);
        }
    }

    /// <summary>
    /// Narrow 4-tap filter kernel (modifies p1,p0,q0,q1).
    /// </summary>
    private static void NarrowFilter(Span<byte> buf, int offset, int step, bool hev)
    {
        var p1 = (int)buf[offset - 2 * step];
        var p0 = (int)buf[offset - step];
        var q0 = (int)buf[offset];
        var q1 = (int)buf[offset + step];

        var ps0 = (sbyte)(p0 - 128);
        var ps1 = (sbyte)(p1 - 128);
        var qs0 = (sbyte)(q0 - 128);
        var qs1 = (sbyte)(q1 - 128);

        int filter;
        if (hev)
        {
            filter = Clamp3(ps1 - qs1 + 3 * (qs0 - ps0));
        }
        else
        {
            filter = Clamp3(3 * (qs0 - ps0));
        }

        var f1 = Math.Min(filter + 4, 127) >> 3;
        var f2 = Math.Min(filter + 3, 127) >> 3;

        buf[offset - step] = ClampByte(p0 + f2);
        buf[offset] = ClampByte(q0 - f1);

        if (!hev)
        {
            var f3 = (f1 + 1) >> 1;
            buf[offset - 2 * step] = ClampByte(p1 + f3);
            buf[offset + step] = ClampByte(q1 - f3);
        }
    }

    #endregion

    #region Frame-Level Loop Filter

    /// <summary>
    /// Applies loop filter to entire frame (horizontal and vertical edges).
    /// Processes superblock edges (8×8 grid minimum).
    /// </summary>
    /// <param name="plane">Pixel plane data.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="stride">Row stride.</param>
    /// <param name="filterParams">Frame filter parameters.</param>
    /// <param name="segmentLevels">Per-block filter levels (indexed by block position).</param>
    /// <param name="blockSize">Filter grid block size (typically 8).</param>
    internal static void ApplyFrameFilter(Span<byte> plane, int width, int height, int stride,
        in FilterParams filterParams, ReadOnlySpan<int> segmentLevels, int blockSize)
    {
        if (filterParams.FilterLevel == 0)
            return;

        ComputeThresholds(filterParams.FilterLevel, filterParams.SharpnessLevel,
            out var limit, out var blimit, out var thresh);

        var blocksX = (width + blockSize - 1) / blockSize;
        var blocksY = (height + blockSize - 1) / blockSize;

        // Vertical edges (between horizontally adjacent blocks)
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 1; bx < blocksX; bx++)
            {
                var level = segmentLevels.Length > 0
                    ? segmentLevels[by * blocksX + bx]
                    : filterParams.FilterLevel;

                if (level == 0) continue;

                ComputeThresholds(level, filterParams.SharpnessLevel,
                    out var lim, out var blim, out var thr);

                var edgeX = bx * blockSize;
                for (var dy = 0; dy < blockSize && by * blockSize + dy < height; dy++)
                {
                    var y = by * blockSize + dy;
                    if (edgeX >= 4 && edgeX < width)
                        NormalFilter4(plane, y * stride + edgeX, 1, lim, blim, thr);
                }
            }
        }

        // Horizontal edges (between vertically adjacent blocks)
        for (var by = 1; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var level = segmentLevels.Length > 0
                    ? segmentLevels[by * blocksX + bx]
                    : filterParams.FilterLevel;

                if (level == 0) continue;

                ComputeThresholds(level, filterParams.SharpnessLevel,
                    out var lim, out var blim, out var thr);

                var edgeY = by * blockSize;
                for (var dx = 0; dx < blockSize && bx * blockSize + dx < width; dx++)
                {
                    var x = bx * blockSize + dx;
                    if (edgeY >= 4 && edgeY < height)
                        NormalFilter4(plane, edgeY * stride + x, stride, lim, blim, thr);
                }
            }
        }
    }

    #endregion

    #region Filter Masks

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FilterMask(int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3,
        int limit, int blimit)
    {
        return Math.Abs(p3 - p2) <= limit && Math.Abs(p2 - p1) <= limit &&
               Math.Abs(p1 - p0) <= limit && Math.Abs(q1 - q0) <= limit &&
               Math.Abs(q2 - q1) <= limit && Math.Abs(q3 - q2) <= limit &&
               (Math.Abs(p0 - q0) * 2 + (Math.Abs(p1 - q1) >> 1)) <= blimit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFlat(int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3, int thresh)
    {
        return Math.Abs(p1 - p0) <= thresh && Math.Abs(q1 - q0) <= thresh &&
               Math.Abs(p2 - p0) <= thresh && Math.Abs(q2 - q0) <= thresh &&
               Math.Abs(p3 - p0) <= thresh && Math.Abs(q3 - q0) <= thresh;
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp3(int value) =>
        Math.Clamp(value, -128, 127);

    #endregion
}
