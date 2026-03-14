#pragma warning disable S109, S2325, S3776, CA1822, MA0051, IDE0045, IDE0047, IDE0048

using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// H.264 Deblocking Filter (ITU-T H.264 Section 8.7).
/// </summary>
/// <remarks>
/// Примеряется к каждому ребру макроблока для устранения блочных артефактов.
/// Boundary Strength (bS) определяет интенсивность фильтрации: bS=0..4.
/// Два типа фильтра: Normal (для bS 1-3) и Strong (для bS=4).
/// </remarks>
internal static class H264Deblock
{
    /// <summary>
    /// Применяет деблокинг-фильтр к одному ребру макроблока.
    /// </summary>
    /// <param name="pixels">Буфер пикселей.</param>
    /// <param name="offset">Offset до точки фильтрации (p0).</param>
    /// <param name="step">Шаг до следующего пикселя вдоль ребра (+1 для вертикального, +stride для горизонтального).</param>
    /// <param name="stride">Шаг между строками (для горизонтальных рёбер) или 1 (для вертикальных).</param>
    /// <param name="bS">Boundary strength: 0=skip, 1-3=normal, 4=strong.</param>
    /// <param name="qp">Quantization parameter для определения thresholds.</param>
    /// <param name="edgeLength">Длина ребра в пикселях (4 для 4x4 sub-block edges).</param>
    public static void FilterEdge(
        Span<byte> pixels, int offset, int step, int stride,
        int bS, int qp, int edgeLength = 4)
    {
        if (bS == 0)
        {
            return;
        }

        var indexA = Math.Clamp(qp + 0, 0, 51); // alpha offset = 0 (default)
        var indexB = Math.Clamp(qp + 0, 0, 51); // beta offset = 0 (default)

        var alpha = AlphaTable[indexA];
        var beta = BetaTable[indexB];

        for (var i = 0; i < edgeLength; i++)
        {
            var idx = offset + (i * step);
            FilterSample(pixels, idx, stride, bS, alpha, beta);
        }
    }

    /// <summary>
    /// Деблокинг вертикальных рёбер макроблока (левые границы 4x4 sub-blocks).
    /// </summary>
    public static void FilterMbVertical(
        Span<byte> pixels, int mbX, int mbY, int stride,
        ReadOnlySpan<int> bsValues, int qp)
    {
        var mbOffset = (mbY * 16 * stride) + (mbX * 16);

        for (var edge = 0; edge < 4; edge++)
        {
            var edgeOffset = mbOffset + (edge * 4);
            var bs = bsValues[edge];

            if (bs <= 0)
            {
                continue;
            }

            for (var y = 0; y < 16; y++)
            {
                var idx = edgeOffset + (y * stride);
                var indexA = Math.Clamp(qp, 0, 51);
                var indexB = Math.Clamp(qp, 0, 51);
                FilterSample(pixels, idx, 1, bs, AlphaTable[indexA], BetaTable[indexB]);
            }
        }
    }

    /// <summary>
    /// Деблокинг горизонтальных рёбер макроблока (верхние границы 4x4 sub-blocks).
    /// </summary>
    public static void FilterMbHorizontal(
        Span<byte> pixels, int mbX, int mbY, int stride,
        ReadOnlySpan<int> bsValues, int qp)
    {
        var mbOffset = (mbY * 16 * stride) + (mbX * 16);

        for (var edge = 0; edge < 4; edge++)
        {
            var edgeOffset = mbOffset + (edge * 4 * stride);
            var bs = bsValues[edge];

            if (bs <= 0)
            {
                continue;
            }

            for (var x = 0; x < 16; x++)
            {
                var idx = edgeOffset + x;
                var indexA = Math.Clamp(qp, 0, 51);
                var indexB = Math.Clamp(qp, 0, 51);
                FilterSample(pixels, idx, stride, bs, AlphaTable[indexA], BetaTable[indexB]);
            }
        }
    }

    /// <summary>
    /// Вычисляет Boundary Strength для ребра между двумя блоками.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ComputeBoundaryStrength(bool isIntra, bool isEdgeMbBoundary, bool hasNonZeroCoeff)
    {
        if (isIntra && isEdgeMbBoundary)
        {
            return 4;
        }

        if (isIntra)
        {
            return 3;
        }

        if (hasNonZeroCoeff)
        {
            return 2;
        }

        return 0;
    }

    #region Private

    private static void FilterSample(
        Span<byte> pixels, int idx, int pixelStep,
        int bS, int alpha, int beta)
    {
        // p2, p1, p0 | q0, q1, q2
        var p0Idx = idx - pixelStep;
        var p1Idx = idx - (pixelStep * 2);
        var q0Idx = idx;
        var q1Idx = idx + pixelStep;

        if (p0Idx < 0 || q1Idx >= pixels.Length)
        {
            return;
        }

        var p0 = pixels[p0Idx];
        var p1 = pixels[p1Idx];
        var q0 = pixels[q0Idx];
        var q1 = pixels[q1Idx];

        // Check filter conditions
        if (Math.Abs(p0 - q0) >= alpha ||
            Math.Abs(p1 - p0) >= beta ||
            Math.Abs(q1 - q0) >= beta)
        {
            return;
        }

        if (bS == 4)
        {
            FilterStrong(pixels, idx, pixelStep, p0, p1, q0, q1, alpha, beta);
        }
        else
        {
            FilterNormal(pixels, idx, pixelStep, p0, p1, q0, q1, bS);
        }
    }

    private static void FilterStrong(
        Span<byte> pixels, int idx, int pixelStep,
        int p0, int p1, int q0, int q1, int alpha, int beta)
    {
        var p2Idx = idx - (pixelStep * 3);
        var q2Idx = idx + (pixelStep * 2);

        var p2 = p2Idx >= 0 ? pixels[p2Idx] : p1;
        var q2 = q2Idx < pixels.Length ? pixels[q2Idx] : q1;

        if (Math.Abs(p0 - q0) < ((alpha >> 2) + 2) && Math.Abs(p2 - p0) < beta)
        {
            // Strong filtering for p side
            pixels[idx - pixelStep] = ClipByte((p2 + (2 * p1) + (2 * p0) + (2 * q0) + q1 + 4) >> 3);
            pixels[idx - (pixelStep * 2)] = ClipByte((p2 + p1 + p0 + q0 + 2) >> 2);

            if (p2Idx >= 0)
            {
                pixels[p2Idx] = ClipByte(((2 * pixels[idx - (pixelStep * 4)]) + (3 * p2) + p1 + p0 + q0 + 4) >> 3);
            }
        }
        else
        {
            pixels[idx - pixelStep] = ClipByte(((2 * p1) + p0 + q1 + 2) >> 2);
        }

        if (Math.Abs(p0 - q0) < ((alpha >> 2) + 2) && Math.Abs(q2 - q0) < beta)
        {
            pixels[idx] = ClipByte((p1 + (2 * p0) + (2 * q0) + (2 * q1) + q2 + 4) >> 3);
            pixels[idx + pixelStep] = ClipByte((p0 + q0 + q1 + q2 + 2) >> 2);

            if (q2Idx < pixels.Length)
            {
                pixels[q2Idx] = ClipByte((p0 + q0 + q1 + (3 * q2) + (2 * pixels[idx + (pixelStep * 3)]) + 4) >> 3);
            }
        }
        else
        {
            pixels[idx] = ClipByte(((2 * q1) + q0 + p1 + 2) >> 2);
        }
    }

    private static void FilterNormal(
        Span<byte> pixels, int idx, int pixelStep,
        int p0, int p1, int q0, int q1, int bS)
    {
        // tc0 table (indexed by bS-1 and QP/6, simplified)
        var tc0 = bS - 1; // Simplified, actual uses TC0 table

        var delta = Math.Clamp(((((q0 - p0) << 2) + (p1 - q1) + 4) >> 3), -tc0, tc0);

        pixels[idx - pixelStep] = ClipByte(p0 + delta);
        pixels[idx] = ClipByte(q0 - delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipByte(int val) => (byte)Math.Clamp(val, 0, 255);

    #endregion

    #region Filter Threshold Tables (ITU-T H.264 Table 8-16, 8-17)

    private static ReadOnlySpan<int> AlphaTable =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        4, 4, 5, 6, 7, 8, 9, 10, 12, 13, 15, 17, 20, 22, 25, 28,
        32, 36, 40, 45, 50, 56, 63, 71, 80, 90, 101, 113, 127, 144,
        162, 182, 203, 226, 255, 255,
    ];

    private static ReadOnlySpan<int> BetaTable =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 6, 6, 7, 7, 8, 8,
        9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15,
        16, 16, 17, 17, 18, 18,
    ];

    #endregion
}
