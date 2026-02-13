#pragma warning disable CA1000, CA2208, MA0051, S1172, IDE0060

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Nv12 ↔ Rgb24.
/// </summary>
public readonly partial struct Nv12
{
    #region Rgb24 → Nv12

    /// <summary>
    /// Конвертирует Rgb24 кадр в Nv12 planes.
    /// </summary>
    /// <param name="rgb">Исходный RGB24 буфер (width × height × 3 байт).</param>
    /// <param name="yPlane">Целевой Y plane (width × height байт).</param>
    /// <param name="uvPlane">Целевой UV plane (width × height/2 байт, interleaved).</param>
    /// <param name="width">Ширина кадра (должна быть чётной).</param>
    /// <param name="height">Высота кадра (должна быть чётной).</param>
    public static void FromRgb24(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uvPlane,
        int width,
        int height) =>
        FromRgb24(rgb, yPlane, uvPlane, width, height, HardwareAcceleration.Auto);

    /// <summary>
    /// Конвертирует Rgb24 кадр в Nv12 planes с указанием ускорителя.
    /// </summary>
    public static unsafe void FromRgb24(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uvPlane,
        int width,
        int height,
        HardwareAcceleration acceleration)
    {
        ValidateDimensions(width, height);
        ValidateBuffers(rgb.Length, yPlane.Length, uvPlane.Length, width, height);

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, SupportedAccelerations, width * height);
        var pixelCount = width * height;

        if (IPlanarColorSpace<Nv12>.ShouldParallelize(pixelCount))
        {
            fixed (byte* pRgb = rgb)
            fixed (byte* pY = yPlane)
            fixed (byte* pUv = uvPlane)
            {
                FromRgb24Parallel(pRgb, pY, pUv, width, height, selected);
            }
            return;
        }

        FromRgb24Scalar(rgb, yPlane, uvPlane, width, height);
    }

    private static unsafe void FromRgb24Parallel(
        byte* rgb, byte* yPlane, byte* uvPlane,
        int width, int height, HardwareAcceleration selected)
    {
        var halfHeight = height / 2;
        var threadCount = IPlanarColorSpace<Nv12>.GetOptimalThreadCount(width * height);
        var rowPairsPerThread = halfHeight / threadCount;
        var remainder = halfHeight % threadCount;

        Parallel.For(0, threadCount, t =>
        {
            var startRowPair = (t * rowPairsPerThread) + Math.Min(t, remainder);
            var rowPairCount = rowPairsPerThread + (t < remainder ? 1 : 0);

            ProcessRgbToNv12RowPairs(rgb, yPlane, uvPlane, width, startRowPair, rowPairCount);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ProcessRgbToNv12RowPairs(
        byte* rgb, byte* yPlane, byte* uvPlane,
        int width, int startRowPair, int rowPairCount)
    {
        var rgbStride = width * 3;

        for (var rp = 0; rp < rowPairCount; rp++)
        {
            var y = (startRowPair + rp) * 2;
            var rgbRow0 = rgb + (y * rgbStride);
            var rgbRow1 = rgb + ((y + 1) * rgbStride);
            var yRow0 = yPlane + (y * width);
            var yRow1 = yPlane + ((y + 1) * width);
            var uvRow = uvPlane + ((startRowPair + rp) * width); // width, не width/2, т.к. UV interleaved

            for (var x = 0; x < width; x += 2)
            {
                var rgbIdx0 = x * 3;
                var rgbIdx1 = rgbIdx0 + 3;

                int r00 = rgbRow0[rgbIdx0];
                int g00 = rgbRow0[rgbIdx0 + 1];
                int b00 = rgbRow0[rgbIdx0 + 2];
                int r01 = rgbRow0[rgbIdx1];
                int g01 = rgbRow0[rgbIdx1 + 1];
                int b01 = rgbRow0[rgbIdx1 + 2];
                int r10 = rgbRow1[rgbIdx0];
                int g10 = rgbRow1[rgbIdx0 + 1];
                int b10 = rgbRow1[rgbIdx0 + 2];
                int r11 = rgbRow1[rgbIdx1];
                int g11 = rgbRow1[rgbIdx1 + 1];
                int b11 = rgbRow1[rgbIdx1 + 2];

                // Y для каждого пикселя
                yRow0[x] = (byte)(((77 * r00) + (150 * g00) + (29 * b00) + 128) >> 8);
                yRow0[x + 1] = (byte)(((77 * r01) + (150 * g01) + (29 * b01) + 128) >> 8);
                yRow1[x] = (byte)(((77 * r10) + (150 * g10) + (29 * b10) + 128) >> 8);
                yRow1[x + 1] = (byte)(((77 * r11) + (150 * g11) + (29 * b11) + 128) >> 8);

                // U и V — среднее по 4 пикселям, записываем interleaved
                var rAvg = (r00 + r01 + r10 + r11 + 2) >> 2;
                var gAvg = (g00 + g01 + g10 + g11 + 2) >> 2;
                var bAvg = (b00 + b01 + b10 + b11 + 2) >> 2;

                var uRaw = ((-43 * rAvg) - (85 * gAvg) + (128 * bAvg) + 32768) >> 8;
                var vRaw = ((128 * rAvg) - (107 * gAvg) - (21 * bAvg) + 32768) >> 8;

                uvRow[x] = ClampByte(uRaw);      // U
                uvRow[x + 1] = ClampByte(vRaw); // V
            }
        }
    }

    private static void FromRgb24Scalar(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uvPlane,
        int width,
        int height)
    {
        var rgbStride = width * 3;

        for (var y = 0; y < height; y += 2)
        {
            var rgbRow0 = rgb.Slice(y * rgbStride, rgbStride);
            var rgbRow1 = rgb.Slice((y + 1) * rgbStride, rgbStride);
            var yRow0 = yPlane.Slice(y * width, width);
            var yRow1 = yPlane.Slice((y + 1) * width, width);
            var uvRow = uvPlane.Slice(y / 2 * width, width);

            for (var x = 0; x < width; x += 2)
            {
                var rgbIdx0 = x * 3;
                var rgbIdx1 = rgbIdx0 + 3;

                int r00 = rgbRow0[rgbIdx0];
                int g00 = rgbRow0[rgbIdx0 + 1];
                int b00 = rgbRow0[rgbIdx0 + 2];
                int r01 = rgbRow0[rgbIdx1];
                int g01 = rgbRow0[rgbIdx1 + 1];
                int b01 = rgbRow0[rgbIdx1 + 2];
                int r10 = rgbRow1[rgbIdx0];
                int g10 = rgbRow1[rgbIdx0 + 1];
                int b10 = rgbRow1[rgbIdx0 + 2];
                int r11 = rgbRow1[rgbIdx1];
                int g11 = rgbRow1[rgbIdx1 + 1];
                int b11 = rgbRow1[rgbIdx1 + 2];

                yRow0[x] = (byte)(((77 * r00) + (150 * g00) + (29 * b00) + 128) >> 8);
                yRow0[x + 1] = (byte)(((77 * r01) + (150 * g01) + (29 * b01) + 128) >> 8);
                yRow1[x] = (byte)(((77 * r10) + (150 * g10) + (29 * b10) + 128) >> 8);
                yRow1[x + 1] = (byte)(((77 * r11) + (150 * g11) + (29 * b11) + 128) >> 8);

                var rAvg = (r00 + r01 + r10 + r11 + 2) >> 2;
                var gAvg = (g00 + g01 + g10 + g11 + 2) >> 2;
                var bAvg = (b00 + b01 + b10 + b11 + 2) >> 2;

                var uRaw = ((-43 * rAvg) - (85 * gAvg) + (128 * bAvg) + 32768) >> 8;
                var vRaw = ((128 * rAvg) - (107 * gAvg) - (21 * bAvg) + 32768) >> 8;

                uvRow[x] = ClampByte(uRaw);
                uvRow[x + 1] = ClampByte(vRaw);
            }
        }
    }

    #endregion

    #region Nv12 → Rgb24

    /// <summary>
    /// Конвертирует Nv12 planes в Rgb24 кадр.
    /// </summary>
    public static void ToRgb24(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uvPlane,
        Span<byte> rgb,
        int width,
        int height) =>
        ToRgb24(yPlane, uvPlane, rgb, width, height, HardwareAcceleration.Auto);

    /// <summary>
    /// Конвертирует Nv12 planes в Rgb24 кадр с указанием ускорителя.
    /// </summary>
    public static unsafe void ToRgb24(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uvPlane,
        Span<byte> rgb,
        int width,
        int height,
        HardwareAcceleration acceleration)
    {
        ValidateDimensions(width, height);
        ValidateBuffers(rgb.Length, yPlane.Length, uvPlane.Length, width, height);

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, SupportedAccelerations, width * height);
        var pixelCount = width * height;

        if (IPlanarColorSpace<Nv12>.ShouldParallelize(pixelCount))
        {
            fixed (byte* pY = yPlane)
            fixed (byte* pUv = uvPlane)
            fixed (byte* pRgb = rgb)
            {
                ToRgb24Parallel(pY, pUv, pRgb, width, height, selected);
            }
            return;
        }

        ToRgb24Scalar(yPlane, uvPlane, rgb, width, height);
    }

    private static unsafe void ToRgb24Parallel(
        byte* yPlane, byte* uvPlane, byte* rgb,
        int width, int height, HardwareAcceleration selected)
    {
        var threadCount = IPlanarColorSpace<Nv12>.GetOptimalThreadCount(width * height);
        var rowsPerThread = height / threadCount;
        var remainder = height % threadCount;

        Parallel.For(0, threadCount, t =>
        {
            var startRow = (t * rowsPerThread) + Math.Min(t, remainder);
            var rowCount = rowsPerThread + (t < remainder ? 1 : 0);

            ProcessNv12ToRgbRows(yPlane, uvPlane, rgb, width, startRow, rowCount);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ProcessNv12ToRgbRows(
        byte* yPlane, byte* uvPlane, byte* rgb,
        int width, int startRow, int rowCount)
    {
        var rgbStride = width * 3;

        for (var rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            var y = startRow + rowOffset;
            var yRow = yPlane + (y * width);
            var uvRowIdx = y / 2;
            var uvRow = uvPlane + (uvRowIdx * width);
            var rgbRow = rgb + (y * rgbStride);

            for (var x = 0; x < width; x++)
            {
                var yVal = yRow[x];
                var uvIdx = x / 2 * 2; // Индекс UV пары
                var uVal = uvRow[uvIdx];
                var vVal = uvRow[uvIdx + 1];

                var d = uVal - 128;
                var e = vVal - 128;

                var rVal = yVal + (((359 * e) + 128) >> 8);
                var gVal = yVal - (((88 * d) + (183 * e) + 128) >> 8);
                var bVal = yVal + (((454 * d) + 128) >> 8);

                var rgbIdx = x * 3;
                rgbRow[rgbIdx] = ClampByte(rVal);
                rgbRow[rgbIdx + 1] = ClampByte(gVal);
                rgbRow[rgbIdx + 2] = ClampByte(bVal);
            }
        }
    }

    private static void ToRgb24Scalar(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uvPlane,
        Span<byte> rgb,
        int width,
        int height)
    {
        var rgbStride = width * 3;

        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Slice(y * width, width);
            var uvRowIdx = y / 2;
            var uvRow = uvPlane.Slice(uvRowIdx * width, width);
            var rgbRow = rgb.Slice(y * rgbStride, rgbStride);

            for (var x = 0; x < width; x++)
            {
                var yVal = yRow[x];
                var uvIdx = x / 2 * 2;
                var uVal = uvRow[uvIdx];
                var vVal = uvRow[uvIdx + 1];

                var d = uVal - 128;
                var e = vVal - 128;

                var rVal = yVal + (((359 * e) + 128) >> 8);
                var gVal = yVal - (((88 * d) + (183 * e) + 128) >> 8);
                var bVal = yVal + (((454 * d) + 128) >> 8);

                var rgbIdx = x * 3;
                rgbRow[rgbIdx] = ClampByte(rVal);
                rgbRow[rgbIdx + 1] = ClampByte(gVal);
                rgbRow[rgbIdx + 2] = ClampByte(bVal);
            }
        }
    }

    #endregion

    #region Validation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);

        if ((width & 1) != 0)
            throw new ArgumentException("Width must be even for NV12", nameof(width));

        if ((height & 1) != 0)
            throw new ArgumentException("Height must be even for NV12", nameof(height));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateBuffers(int rgbLength, int yLength, int uvLength, int width, int height)
    {
        var expectedRgb = width * height * 3;
        var expectedY = width * height;
        var expectedUv = width * (height / 2);

        if (rgbLength < expectedRgb)
        {
            throw new ArgumentException(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"RGB buffer too small: {rgbLength} < {expectedRgb}"),
                nameof(rgbLength));
        }

        if (yLength < expectedY)
        {
            throw new ArgumentException(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Y plane too small: {yLength} < {expectedY}"),
                nameof(yLength));
        }

        if (uvLength < expectedUv)
        {
            throw new ArgumentException(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"UV plane too small: {uvLength} < {expectedUv}"),
                nameof(uvLength));
        }
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    #endregion
}
