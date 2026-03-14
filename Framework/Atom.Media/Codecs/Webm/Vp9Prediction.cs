#pragma warning disable IDE0007, IDE0011, IDE0045, IDE0048, IDE0055, S109, S3776, MA0051

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 intra prediction modes per VP9 specification §7.11.2.
/// 10 modes: DC, V, H, D45, D135, D117, D153, D207, D63, TM.
/// Supports block sizes from 4×4 to 64×64.
/// </summary>
internal static class Vp9Prediction
{
    #region Dispatch

    /// <summary>
    /// Dispatches intra prediction for a block of given size.
    /// </summary>
    /// <param name="mode">Prediction mode (0..9 = DC..TM, see <see cref="Vp9Constants"/>).</param>
    /// <param name="dst">Destination block (stride bytes per row).</param>
    /// <param name="stride">Destination stride.</param>
    /// <param name="above">Pixels above the block (at least <paramref name="size"/> + 1).</param>
    /// <param name="left">Pixels to the left (at least <paramref name="size"/>).</param>
    /// <param name="aboveLeft">Top-left corner pixel.</param>
    /// <param name="size">Block width/height (4, 8, 16, 32, or 64).</param>
    /// <param name="hasAbove">Whether above row is available.</param>
    /// <param name="hasLeft">Whether left column is available.</param>
    public static void Predict(int mode, Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft,
        int size, bool hasAbove, bool hasLeft)
    {
        switch (mode)
        {
            case Vp9Constants.DcPred:
                PredictDc(dst, stride, above, left, size, hasAbove, hasLeft);
                break;
            case Vp9Constants.VPred:
                PredictV(dst, stride, above, size);
                break;
            case Vp9Constants.HPred:
                PredictH(dst, stride, left, size);
                break;
            case Vp9Constants.D45Pred:
                PredictD45(dst, stride, above, size);
                break;
            case Vp9Constants.D135Pred:
                PredictD135(dst, stride, above, left, aboveLeft, size);
                break;
            case Vp9Constants.D117Pred:
                PredictD117(dst, stride, above, left, aboveLeft, size);
                break;
            case Vp9Constants.D153Pred:
                PredictD153(dst, stride, above, left, aboveLeft, size);
                break;
            case Vp9Constants.D207Pred:
                PredictD207(dst, stride, left, size);
                break;
            case Vp9Constants.D63Pred:
                PredictD63(dst, stride, above, size);
                break;
            case Vp9Constants.TmPred:
                PredictTm(dst, stride, above, left, aboveLeft, size);
                break;
        }
    }

    #endregion

    #region DC Prediction

    /// <summary>
    /// DC prediction: fills block with average of available reference pixels.
    /// </summary>
    private static void PredictDc(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, int size,
        bool hasAbove, bool hasLeft)
    {
        int sum = 0;
        int count = 0;

        if (hasAbove)
        {
            for (var i = 0; i < size; i++)
                sum += above[i];
            count += size;
        }

        if (hasLeft)
        {
            for (var i = 0; i < size; i++)
                sum += left[i];
            count += size;
        }

        byte dc;
        if (count > 0)
            dc = (byte)((sum + (count >> 1)) / count);
        else
            dc = 128;

        FillBlock(dst, stride, dc, size);
    }

    #endregion

    #region V Prediction (Vertical)

    /// <summary>
    /// Vertical prediction: copies above row to every row of the block.
    /// </summary>
    private static void PredictV(Span<byte> dst, int stride, ReadOnlySpan<byte> above, int size)
    {
        for (var y = 0; y < size; y++)
            above[..size].CopyTo(dst.Slice(y * stride, size));
    }

    #endregion

    #region H Prediction (Horizontal)

    /// <summary>
    /// Horizontal prediction: fills each row with the corresponding left pixel.
    /// </summary>
    private static void PredictH(Span<byte> dst, int stride, ReadOnlySpan<byte> left, int size)
    {
        for (var y = 0; y < size; y++)
            dst.Slice(y * stride, size).Fill(left[y]);
    }

    #endregion

    #region TM Prediction (TrueMotion)

    /// <summary>
    /// TrueMotion prediction: dst[y][x] = clamp(above[x] + left[y] - aboveLeft).
    /// </summary>
    private static void PredictTm(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, int size)
    {
        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            var leftVal = (int)left[y];

            for (var x = 0; x < size; x++)
                dst[off + x] = ClampByte(leftVal + above[x] - aboveLeft);
        }
    }

    #endregion

    #region D45 Prediction (Diagonal 45°)

    /// <summary>
    /// D45 prediction: diagonal from top-left to bottom-right at 45°.
    /// dst[y][x] = avg3(above[x+y], above[x+y+1], above[x+y+2]).
    /// </summary>
    private static void PredictD45(Span<byte> dst, int stride, ReadOnlySpan<byte> above, int size)
    {
        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = x + y;
                if (idx + 2 < 2 * size)
                    dst[off + x] = Avg3(above[idx], above[idx + 1], above[idx + 2]);
                else
                    dst[off + x] = above[2 * size - 1]; // Replicate last available pixel
            }
        }
    }

    #endregion

    #region D135 Prediction (Diagonal 135°)

    /// <summary>
    /// D135 prediction: diagonal from top-right to bottom-left at 135°.
    /// </summary>
    private static void PredictD135(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, int size)
    {
        // Build border array: left[size-1..0] + aboveLeft + above[0..size-1]
        var borderLen = 2 * size + 1;
        Span<byte> border = stackalloc byte[borderLen];

        for (var i = 0; i < size; i++)
            border[i] = left[size - 1 - i];
        border[size] = aboveLeft;
        for (var i = 0; i < size; i++)
            border[size + 1 + i] = above[i];

        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = size - 1 - y + x;
                dst[off + x] = Avg3(border[idx], border[idx + 1], border[idx + 2]);
            }
        }
    }

    #endregion

    #region D117 Prediction (Diagonal 117°)

    /// <summary>
    /// D117 prediction: ~117° angle (closer to vertical).
    /// </summary>
    private static void PredictD117(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, int size)
    {
        // Row 0: avg2(aboveLeft, above[0]), avg2(above[0], above[1]), ...
        // Row 1: avg3(left[0], aboveLeft, above[0]), avg3(aboveLeft, above[0], above[1]), ...
        // Row 2r: left-shifted version of row r-2 with left pixel extension

        // Build border: left + aboveLeft + above
        var totalLen = 2 * size + 1;
        Span<byte> border = stackalloc byte[totalLen];

        for (var i = 0; i < size; i++)
            border[i] = left[size - 1 - i];
        border[size] = aboveLeft;
        for (var i = 0; i < size; i++)
            border[size + 1 + i] = above[i];

        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = size - (y >> 1) + x;
                if ((y & 1) == 0)
                    dst[off + x] = Avg2(border[idx], border[idx + 1]);
                else
                    dst[off + x] = Avg3(border[idx - 1], border[idx], border[idx + 1]);
            }
        }
    }

    #endregion

    #region D153 Prediction (Diagonal 153°)

    /// <summary>
    /// D153 prediction: ~153° angle (closer to horizontal).
    /// </summary>
    private static void PredictD153(Span<byte> dst, int stride,
        ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte aboveLeft, int size)
    {
        // Build border: above[size-1..0] + aboveLeft + left[0..size-1]
        var totalLen = 2 * size + 1;
        Span<byte> border = stackalloc byte[totalLen];

        for (var i = 0; i < size; i++)
            border[i] = above[size - 1 - i];
        border[size] = aboveLeft;
        for (var i = 0; i < size; i++)
            border[size + 1 + i] = left[i];

        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = size - (x >> 1) + y;
                if ((x & 1) == 0)
                    dst[off + x] = Avg2(border[idx], border[idx + 1]);
                else
                    dst[off + x] = Avg3(border[idx - 1], border[idx], border[idx + 1]);
            }
        }
    }

    #endregion

    #region D207 Prediction (Diagonal 207°)

    /// <summary>
    /// D207 prediction: ~207° angle (steep left diagonal).
    /// dst[y][x] based on left pixels shifted diagonally.
    /// </summary>
    private static void PredictD207(Span<byte> dst, int stride, ReadOnlySpan<byte> left, int size)
    {
        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = y + (x >> 1);
                if (idx + 1 < size)
                {
                    if ((x & 1) == 0)
                        dst[off + x] = Avg2(left[idx], left[idx + 1]);
                    else
                        dst[off + x] = Avg3(left[idx], left[idx + 1],
                            idx + 2 < size ? left[idx + 2] : left[size - 1]);
                }
                else
                {
                    dst[off + x] = left[size - 1]; // Replicate last left pixel
                }
            }
        }
    }

    #endregion

    #region D63 Prediction (Diagonal 63°)

    /// <summary>
    /// D63 prediction: ~63° angle (steep above diagonal).
    /// dst[y][x] based on above pixels shifted diagonally.
    /// </summary>
    private static void PredictD63(Span<byte> dst, int stride, ReadOnlySpan<byte> above, int size)
    {
        for (var y = 0; y < size; y++)
        {
            var off = y * stride;
            for (var x = 0; x < size; x++)
            {
                var idx = x + (y >> 1);
                if (idx + 1 < 2 * size)
                {
                    if ((y & 1) == 0)
                        dst[off + x] = Avg2(above[idx], above[idx + 1]);
                    else
                        dst[off + x] = Avg3(above[idx], above[idx + 1],
                            idx + 2 < 2 * size ? above[idx + 2] : above[2 * size - 1]);
                }
                else
                {
                    dst[off + x] = above[2 * size - 1];
                }
            }
        }
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg2(byte a, byte b) =>
        (byte)((a + b + 1) >> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg3(byte a, byte b, byte c) =>
        (byte)((a + b + b + c + 2) >> 2);

    private static void FillBlock(Span<byte> dst, int stride, byte value, int size)
    {
        for (var y = 0; y < size; y++)
            dst.Slice(y * stride, size).Fill(value);
    }

    #endregion
}
