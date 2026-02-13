#pragma warning disable S109, S3776, CA1028

using System.Runtime.CompilerServices;

namespace Atom.Media.Processing;

/// <summary>
/// Алгоритмы масштабирования изображений.
/// </summary>
public enum ScaleAlgorithm : byte
{
    /// <summary>
    /// Ближайший сосед (Nearest Neighbor) — быстрый, но пикселизованный.
    /// </summary>
    Nearest = 0,

    /// <summary>
    /// Билинейная интерполяция — баланс качества и скорости.
    /// </summary>
    Bilinear = 1,

    /// <summary>
    /// Бикубическая интерполяция — высокое качество.
    /// </summary>
    Bicubic = 2,

    /// <summary>
    /// Lanczos — наивысшее качество для даунскейлинга.
    /// </summary>
    Lanczos = 3,
}

/// <summary>
/// Высокопроизводительный масштабировщик изображений.
/// </summary>
public static class ImageScaler
{
    #region Public API

    /// <summary>
    /// Масштабирует RGB24 изображение.
    /// </summary>
    /// <param name="src">Исходное изображение.</param>
    /// <param name="srcWidth">Ширина исходного изображения.</param>
    /// <param name="srcHeight">Высота исходного изображения.</param>
    /// <param name="dst">Буфер для результата.</param>
    /// <param name="dstWidth">Целевая ширина.</param>
    /// <param name="dstHeight">Целевая высота.</param>
    /// <param name="algorithm">Алгоритм масштабирования.</param>
    public static void ScaleRgb24(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight,
        ScaleAlgorithm algorithm = ScaleAlgorithm.Bilinear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(srcWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(srcHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dstWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dstHeight, 1);

        if (src.Length < srcWidth * srcHeight * 3)
            throw new ArgumentException("Source buffer too small", nameof(src));
        if (dst.Length < dstWidth * dstHeight * 3)
            throw new ArgumentException("Destination buffer too small", nameof(dst));

        // Если размеры совпадают — просто копируем
        if (srcWidth == dstWidth && srcHeight == dstHeight)
        {
            src[..(srcWidth * srcHeight * 3)].CopyTo(dst);
            return;
        }

        switch (algorithm)
        {
            case ScaleAlgorithm.Nearest:
                ScaleRgb24Nearest(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Bilinear:
                ScaleRgb24Bilinear(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Bicubic:
                ScaleRgb24Bicubic(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Lanczos:
                ScaleRgb24Lanczos(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(algorithm));
        }
    }

    /// <summary>
    /// Масштабирует RGBA32 изображение.
    /// </summary>
    public static void ScaleRgba32(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight,
        ScaleAlgorithm algorithm = ScaleAlgorithm.Bilinear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(srcWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(srcHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dstWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dstHeight, 1);

        if (src.Length < srcWidth * srcHeight * 4)
            throw new ArgumentException("Source buffer too small", nameof(src));
        if (dst.Length < dstWidth * dstHeight * 4)
            throw new ArgumentException("Destination buffer too small", nameof(dst));

        if (srcWidth == dstWidth && srcHeight == dstHeight)
        {
            src[..(srcWidth * srcHeight * 4)].CopyTo(dst);
            return;
        }

        switch (algorithm)
        {
            case ScaleAlgorithm.Nearest:
                ScaleRgba32Nearest(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Bilinear:
                ScaleRgba32Bilinear(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Bicubic:
            case ScaleAlgorithm.Lanczos:
                // Пока используем биллинейный как fallback
                ScaleRgba32Bilinear(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(algorithm));
        }
    }

    /// <summary>
    /// Масштабирует Y плоскость (grayscale).
    /// </summary>
    public static void ScaleGrayscale(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight,
        ScaleAlgorithm algorithm = ScaleAlgorithm.Bilinear)
    {
        if (srcWidth == dstWidth && srcHeight == dstHeight)
        {
            src[..(srcWidth * srcHeight)].CopyTo(dst);
            return;
        }

        switch (algorithm)
        {
            case ScaleAlgorithm.Nearest:
                ScaleGrayscaleNearest(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            case ScaleAlgorithm.Bilinear:
                ScaleGrayscaleBilinear(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;

            default:
                ScaleGrayscaleBilinear(src, srcWidth, srcHeight, dst, dstWidth, dstHeight);
                break;
        }
    }

    #endregion

    #region Nearest Neighbor

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgb24Nearest(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)srcWidth / dstWidth;
        var yRatio = (float)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var srcY = (int)(y * yRatio);
            srcY = Math.Min(srcY, srcHeight - 1);

            for (var x = 0; x < dstWidth; x++)
            {
                var srcX = (int)(x * xRatio);
                srcX = Math.Min(srcX, srcWidth - 1);

                var srcIdx = ((srcY * srcWidth) + srcX) * 3;
                var dstIdx = ((y * dstWidth) + x) * 3;

                dst[dstIdx] = src[srcIdx];
                dst[dstIdx + 1] = src[srcIdx + 1];
                dst[dstIdx + 2] = src[srcIdx + 2];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgba32Nearest(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)srcWidth / dstWidth;
        var yRatio = (float)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var srcY = Math.Min((int)(y * yRatio), srcHeight - 1);

            for (var x = 0; x < dstWidth; x++)
            {
                var srcX = Math.Min((int)(x * xRatio), srcWidth - 1);

                var srcIdx = ((srcY * srcWidth) + srcX) * 4;
                var dstIdx = ((y * dstWidth) + x) * 4;

                dst[dstIdx] = src[srcIdx];
                dst[dstIdx + 1] = src[srcIdx + 1];
                dst[dstIdx + 2] = src[srcIdx + 2];
                dst[dstIdx + 3] = src[srcIdx + 3];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleGrayscaleNearest(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)srcWidth / dstWidth;
        var yRatio = (float)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var srcY = Math.Min((int)(y * yRatio), srcHeight - 1);

            for (var x = 0; x < dstWidth; x++)
            {
                var srcX = Math.Min((int)(x * xRatio), srcWidth - 1);
                dst[(y * dstWidth) + x] = src[(srcY * srcWidth) + srcX];
            }
        }
    }

    #endregion

    #region Bilinear

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgb24Bilinear(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)(srcWidth - 1) / dstWidth;
        var yRatio = (float)(srcHeight - 1) / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var yFloat = y * yRatio;
            var yFloor = (int)yFloat;
            var yCeil = Math.Min(yFloor + 1, srcHeight - 1);
            var yLerp = yFloat - yFloor;

            for (var x = 0; x < dstWidth; x++)
            {
                var xFloat = x * xRatio;
                var xFloor = (int)xFloat;
                var xCeil = Math.Min(xFloor + 1, srcWidth - 1);
                var xLerp = xFloat - xFloor;

                // 4 соседних пикселя
                var idx00 = ((yFloor * srcWidth) + xFloor) * 3;
                var idx01 = ((yFloor * srcWidth) + xCeil) * 3;
                var idx10 = ((yCeil * srcWidth) + xFloor) * 3;
                var idx11 = ((yCeil * srcWidth) + xCeil) * 3;

                var dstIdx = ((y * dstWidth) + x) * 3;

                for (var c = 0; c < 3; c++)
                {
                    var v00 = src[idx00 + c];
                    var v01 = src[idx01 + c];
                    var v10 = src[idx10 + c];
                    var v11 = src[idx11 + c];

                    // Билинейная интерполяция
                    var top = v00 + ((v01 - v00) * xLerp);
                    var bottom = v10 + ((v11 - v10) * xLerp);
                    var value = top + ((bottom - top) * yLerp);

                    dst[dstIdx + c] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgba32Bilinear(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)(srcWidth - 1) / dstWidth;
        var yRatio = (float)(srcHeight - 1) / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var yFloat = y * yRatio;
            var yFloor = (int)yFloat;
            var yCeil = Math.Min(yFloor + 1, srcHeight - 1);
            var yLerp = yFloat - yFloor;

            for (var x = 0; x < dstWidth; x++)
            {
                var xFloat = x * xRatio;
                var xFloor = (int)xFloat;
                var xCeil = Math.Min(xFloor + 1, srcWidth - 1);
                var xLerp = xFloat - xFloor;

                var idx00 = ((yFloor * srcWidth) + xFloor) * 4;
                var idx01 = ((yFloor * srcWidth) + xCeil) * 4;
                var idx10 = ((yCeil * srcWidth) + xFloor) * 4;
                var idx11 = ((yCeil * srcWidth) + xCeil) * 4;

                var dstIdx = ((y * dstWidth) + x) * 4;

                for (var c = 0; c < 4; c++)
                {
                    var v00 = src[idx00 + c];
                    var v01 = src[idx01 + c];
                    var v10 = src[idx10 + c];
                    var v11 = src[idx11 + c];

                    var top = v00 + ((v01 - v00) * xLerp);
                    var bottom = v10 + ((v11 - v10) * xLerp);
                    var value = top + ((bottom - top) * yLerp);

                    dst[dstIdx + c] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleGrayscaleBilinear(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)(srcWidth - 1) / dstWidth;
        var yRatio = (float)(srcHeight - 1) / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var yFloat = y * yRatio;
            var yFloor = (int)yFloat;
            var yCeil = Math.Min(yFloor + 1, srcHeight - 1);
            var yLerp = yFloat - yFloor;

            for (var x = 0; x < dstWidth; x++)
            {
                var xFloat = x * xRatio;
                var xFloor = (int)xFloat;
                var xCeil = Math.Min(xFloor + 1, srcWidth - 1);
                var xLerp = xFloat - xFloor;

                var v00 = src[(yFloor * srcWidth) + xFloor];
                var v01 = src[(yFloor * srcWidth) + xCeil];
                var v10 = src[(yCeil * srcWidth) + xFloor];
                var v11 = src[(yCeil * srcWidth) + xCeil];

                var top = v00 + ((v01 - v00) * xLerp);
                var bottom = v10 + ((v11 - v10) * xLerp);
                var value = top + ((bottom - top) * yLerp);

                dst[(y * dstWidth) + x] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
            }
        }
    }

    #endregion

    #region Bicubic

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgb24Bicubic(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        var xRatio = (float)srcWidth / dstWidth;
        var yRatio = (float)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var yFloat = ((y + 0.5f) * yRatio) - 0.5f;
            var yInt = (int)MathF.Floor(yFloat);
            var yFrac = yFloat - yInt;

            for (var x = 0; x < dstWidth; x++)
            {
                var xFloat = ((x + 0.5f) * xRatio) - 0.5f;
                var xInt = (int)MathF.Floor(xFloat);
                var xFrac = xFloat - xInt;

                var dstIdx = ((y * dstWidth) + x) * 3;

                for (var c = 0; c < 3; c++)
                {
                    var value = 0f;

                    for (var j = -1; j <= 2; j++)
                    {
                        var srcY = Math.Clamp(yInt + j, 0, srcHeight - 1);
                        var wy = CubicWeight(j - yFrac);

                        for (var i = -1; i <= 2; i++)
                        {
                            var srcX = Math.Clamp(xInt + i, 0, srcWidth - 1);
                            var wx = CubicWeight(i - xFrac);

                            var srcIdx = (((srcY * srcWidth) + srcX) * 3) + c;
                            value += src[srcIdx] * wx * wy;
                        }
                    }

                    dst[dstIdx + c] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
                }
            }
        }
    }

    /// <summary>
    /// Кубический вес для интерполяции (Catmull-Rom).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CubicWeight(float x)
    {
        x = MathF.Abs(x);

        if (x < 1f)
            return (((1.5f * x) - 2.5f) * x * x) + 1f;

        if (x < 2f)
            return (((((-0.5f * x) + 2.5f) * x) - 4f) * x) + 2f;

        return 0f;
    }

    #endregion

    #region Lanczos

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleRgb24Lanczos(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        Span<byte> dst,
        int dstWidth,
        int dstHeight)
    {
        const int a = 3; // Lanczos-3
        var xRatio = (float)srcWidth / dstWidth;
        var yRatio = (float)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var yFloat = ((y + 0.5f) * yRatio) - 0.5f;
            var yInt = (int)MathF.Floor(yFloat);
            var yFrac = yFloat - yInt;

            for (var x = 0; x < dstWidth; x++)
            {
                var xFloat = ((x + 0.5f) * xRatio) - 0.5f;
                var xInt = (int)MathF.Floor(xFloat);
                var xFrac = xFloat - xInt;

                var dstIdx = ((y * dstWidth) + x) * 3;

                for (var c = 0; c < 3; c++)
                {
                    var value = 0f;
                    var weightSum = 0f;

                    for (var j = -a + 1; j <= a; j++)
                    {
                        var srcY = Math.Clamp(yInt + j, 0, srcHeight - 1);
                        var wy = LanczosWeight(j - yFrac, a);

                        for (var i = -a + 1; i <= a; i++)
                        {
                            var srcX = Math.Clamp(xInt + i, 0, srcWidth - 1);
                            var wx = LanczosWeight(i - xFrac, a);
                            var w = wx * wy;

                            var srcIdx = (((srcY * srcWidth) + srcX) * 3) + c;
                            value += src[srcIdx] * w;
                            weightSum += w;
                        }
                    }

                    if (weightSum > 0)
                        value /= weightSum;

                    dst[dstIdx + c] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
                }
            }
        }
    }

    /// <summary>
    /// Вес Lanczos.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LanczosWeight(float x, int a)
    {
        if (MathF.Abs(x) < 1e-6f)
            return 1f;

        if (MathF.Abs(x) >= a)
            return 0f;

        var piX = MathF.PI * x;
        return a * MathF.Sin(piX) * MathF.Sin(piX / a) / (piX * piX);
    }

    #endregion
}
