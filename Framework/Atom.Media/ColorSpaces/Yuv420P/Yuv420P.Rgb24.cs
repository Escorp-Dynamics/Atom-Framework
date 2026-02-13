#pragma warning disable CA1000, CA2208, MA0051, S1172, IDE0060

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Yuv420P ↔ Rgb24.
/// </summary>
public readonly partial struct Yuv420P
{
    #region Rgb24 → Yuv420P

    /// <summary>
    /// Конвертирует Rgb24 кадр в Yuv420P planes.
    /// </summary>
    /// <param name="rgb">Исходный RGB24 буфер (width × height × 3 байт).</param>
    /// <param name="yPlane">Целевой Y plane (width × height байт).</param>
    /// <param name="uPlane">Целевой U plane (width/2 × height/2 байт).</param>
    /// <param name="vPlane">Целевой V plane (width/2 × height/2 байт).</param>
    /// <param name="width">Ширина кадра (должна быть чётной).</param>
    /// <param name="height">Высота кадра (должна быть чётной).</param>
    public static void FromRgb24(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uPlane,
        Span<byte> vPlane,
        int width,
        int height) =>
        FromRgb24(rgb, yPlane, uPlane, vPlane, width, height, HardwareAcceleration.Auto);

    /// <summary>
    /// Конвертирует Rgb24 кадр в Yuv420P planes с указанием ускорителя.
    /// </summary>
    public static unsafe void FromRgb24(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uPlane,
        Span<byte> vPlane,
        int width,
        int height,
        HardwareAcceleration acceleration)
    {
        ValidateDimensions(width, height);
        ValidateBuffers(rgb.Length, yPlane.Length, uPlane.Length, vPlane.Length, width, height);

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, SupportedAccelerations, width * height);
        var pixelCount = width * height;

        if (IPlanarColorSpace<Yuv420P>.ShouldParallelize(pixelCount))
        {
            fixed (byte* pRgb = rgb)
            fixed (byte* pY = yPlane)
            fixed (byte* pU = uPlane)
            fixed (byte* pV = vPlane)
            {
                FromRgb24Parallel(pRgb, pY, pU, pV, width, height, selected);
            }
            return;
        }

        FromRgb24Core(rgb, yPlane, uPlane, vPlane, width, height, selected);
    }

    private static void FromRgb24Core(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uPlane,
        Span<byte> vPlane,
        int width,
        int height,
        HardwareAcceleration selected) =>
        FromRgb24Scalar(rgb, yPlane, uPlane, vPlane, width, height);

    private static unsafe void FromRgb24Parallel(
        byte* rgb, byte* yPlane, byte* uPlane, byte* vPlane,
        int width, int height, HardwareAcceleration selected)
    {
        var halfHeight = height / 2;
        var threadCount = IPlanarColorSpace<Yuv420P>.GetOptimalThreadCount(width * height);
        var rowPairsPerThread = halfHeight / threadCount;
        var remainder = halfHeight % threadCount;

        Parallel.For(0, threadCount, t =>
        {
            var startRowPair = (t * rowPairsPerThread) + Math.Min(t, remainder);
            var rowPairCount = rowPairsPerThread + (t < remainder ? 1 : 0);

            ProcessRowPairs(rgb, yPlane, uPlane, vPlane, width, startRowPair, rowPairCount);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ProcessRowPairs(
        byte* rgb, byte* yPlane, byte* uPlane, byte* vPlane,
        int width, int startRowPair, int rowPairCount)
    {
        var rgbStride = width * 3;
        var chromaWidth = width / 2;

        for (var rp = 0; rp < rowPairCount; rp++)
        {
            var y = (startRowPair + rp) * 2;
            var rgbRow0 = rgb + (y * rgbStride);
            var rgbRow1 = rgb + ((y + 1) * rgbStride);
            var yRow0 = yPlane + (y * width);
            var yRow1 = yPlane + ((y + 1) * width);
            var uRow = uPlane + ((startRowPair + rp) * chromaWidth);
            var vRow = vPlane + ((startRowPair + rp) * chromaWidth);

            for (var x = 0; x < width; x += 2)
            {
                // Загружаем 4 пикселя (2×2 блок)
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

                // U и V — среднее по 4 пикселям
                var rAvg = (r00 + r01 + r10 + r11 + 2) >> 2;
                var gAvg = (g00 + g01 + g10 + g11 + 2) >> 2;
                var bAvg = (b00 + b01 + b10 + b11 + 2) >> 2;

                var uRaw = ((-43 * rAvg) - (85 * gAvg) + (128 * bAvg) + 32768) >> 8;
                var vRaw = ((128 * rAvg) - (107 * gAvg) - (21 * bAvg) + 32768) >> 8;

                uRow[x / 2] = ClampByte(uRaw);
                vRow[x / 2] = ClampByte(vRaw);
            }
        }
    }

    private static void FromRgb24Scalar(
        ReadOnlySpan<byte> rgb,
        Span<byte> yPlane,
        Span<byte> uPlane,
        Span<byte> vPlane,
        int width,
        int height)
    {
        var rgbStride = width * 3;
        var chromaWidth = width / 2;

        for (var y = 0; y < height; y += 2)
        {
            var rgbRow0 = rgb.Slice(y * rgbStride, rgbStride);
            var rgbRow1 = rgb.Slice((y + 1) * rgbStride, rgbStride);
            var yRow0 = yPlane.Slice(y * width, width);
            var yRow1 = yPlane.Slice((y + 1) * width, width);
            var uRow = uPlane.Slice(y / 2 * chromaWidth, chromaWidth);
            var vRow = vPlane.Slice(y / 2 * chromaWidth, chromaWidth);

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

                uRow[x / 2] = ClampByte(uRaw);
                vRow[x / 2] = ClampByte(vRaw);
            }
        }
    }

    #endregion

    #region Yuv420P → Rgb24

    /// <summary>
    /// Конвертирует Yuv420P planes в Rgb24 кадр.
    /// </summary>
    public static void ToRgb24(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uPlane,
        ReadOnlySpan<byte> vPlane,
        Span<byte> rgb,
        int width,
        int height) =>
        ToRgb24(yPlane, uPlane, vPlane, rgb, width, height, HardwareAcceleration.Auto);

    /// <summary>
    /// Конвертирует Yuv420P planes в Rgb24 кадр с указанием ускорителя.
    /// </summary>
    public static unsafe void ToRgb24(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uPlane,
        ReadOnlySpan<byte> vPlane,
        Span<byte> rgb,
        int width,
        int height,
        HardwareAcceleration acceleration)
    {
        ValidateDimensions(width, height);
        ValidateBuffers(rgb.Length, yPlane.Length, uPlane.Length, vPlane.Length, width, height);

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, SupportedAccelerations, width * height);
        var pixelCount = width * height;

        if (IPlanarColorSpace<Yuv420P>.ShouldParallelize(pixelCount))
        {
            fixed (byte* pY = yPlane)
            fixed (byte* pU = uPlane)
            fixed (byte* pV = vPlane)
            fixed (byte* pRgb = rgb)
            {
                ToRgb24Parallel(pY, pU, pV, pRgb, width, height, selected);
            }
            return;
        }

        ToRgb24Core(yPlane, uPlane, vPlane, rgb, width, height, selected);
    }

    private static unsafe void ToRgb24Core(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> uPlane,
        ReadOnlySpan<byte> vPlane,
        Span<byte> rgb,
        int width,
        int height,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when width >= 16:
                fixed (byte* pY = yPlane)
                fixed (byte* pU = uPlane)
                fixed (byte* pV = vPlane)
                fixed (byte* pRgb = rgb)
                    ToRgb24Avx2(pY, pU, pV, pRgb, width, height);
                break;
            case HardwareAcceleration.Sse41 when width >= 8:
                fixed (byte* pY = yPlane)
                fixed (byte* pU = uPlane)
                fixed (byte* pV = vPlane)
                fixed (byte* pRgb = rgb)
                    ToRgb24Sse41(pY, pU, pV, pRgb, width, height);
                break;
            default:
                ToRgb24Scalar(yPlane, uPlane, vPlane, rgb, width, height);
                break;
        }
    }

    private static unsafe void ToRgb24Parallel(
        byte* yPlane, byte* uPlane, byte* vPlane, byte* rgb,
        int width, int height, HardwareAcceleration selected)
    {
        var threadCount = IPlanarColorSpace<Yuv420P>.GetOptimalThreadCount(width * height);
        var rowsPerThread = height / threadCount;
        var remainder = height % threadCount;

        Parallel.For(0, threadCount, t =>
        {
            var startRow = (t * rowsPerThread) + Math.Min(t, remainder);
            var rowCount = rowsPerThread + (t < remainder ? 1 : 0);

            ProcessYuvToRgbRows(yPlane, uPlane, vPlane, rgb, width, startRow, rowCount);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ProcessYuvToRgbRows(
        byte* yPlane, byte* uPlane, byte* vPlane, byte* rgb,
        int width, int startRow, int rowCount)
    {
        var rgbStride = width * 3;
        var chromaWidth = width / 2;

        for (var rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            var y = startRow + rowOffset;
            var yRow = yPlane + (y * width);
            var uvRow = y / 2;
            var uRow = uPlane + (uvRow * chromaWidth);
            var vRow = vPlane + (uvRow * chromaWidth);
            var rgbRow = rgb + (y * rgbStride);

            for (var x = 0; x < width; x++)
            {
                var yVal = yRow[x];
                var chromaX = x / 2;
                var uVal = uRow[chromaX];
                var vVal = vRow[chromaX];

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
        ReadOnlySpan<byte> uPlane,
        ReadOnlySpan<byte> vPlane,
        Span<byte> rgb,
        int width,
        int height)
    {
        var rgbStride = width * 3;
        var chromaWidth = width / 2;

        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Slice(y * width, width);
            var uvRowIdx = y / 2;
            var uRow = uPlane.Slice(uvRowIdx * chromaWidth, chromaWidth);
            var vRow = vPlane.Slice(uvRowIdx * chromaWidth, chromaWidth);
            var rgbRow = rgb.Slice(y * rgbStride, rgbStride);

            for (var x = 0; x < width; x++)
            {
                var yVal = yRow[x];
                var chromaX = x / 2;
                var uVal = uRow[chromaX];
                var vVal = vRow[chromaX];

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

    #region SSE41 Implementation (YUV → RGB)

    /// <summary>
    /// SSE41: YUV420P → RGB24 с int16 точной математикой.
    /// Обрабатывает 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Sse41(
        byte* yPlane, byte* uPlane, byte* vPlane, byte* rgb,
        int width, int height)
    {
        var rgbStride = width * 3;
        var chromaWidth = width / 2;

        // Кешируем Q8 коэффициенты
        var cVtoR = Yuv420PSse41Vectors.Q8_VtoR;
        var cUtoG = Yuv420PSse41Vectors.Q8_UtoG;
        var cVtoG = Yuv420PSse41Vectors.Q8_VtoG;
        var cUtoB = Yuv420PSse41Vectors.Q8_UtoB;
        var c128 = Yuv420PSse41Vectors.Offset128;
        var round = Yuv420PSse41Vectors.RoundHalf;

        for (var row = 0; row < height; row++)
        {
            var yRow = yPlane + (row * width);
            var uvRowIdx = row / 2;
            var uRow = uPlane + (uvRowIdx * chromaWidth);
            var vRow = vPlane + (uvRowIdx * chromaWidth);
            var rgbRow = rgb + (row * rgbStride);

            var x = 0;

            // 8 пикселей за итерацию
            while (x + 8 <= width)
            {
                // Загружаем 8 Y значений
                var yVec = Sse2.LoadScalarVector128((ulong*)(yRow + x)).AsByte();

                // Загружаем 4 U и 4 V (каждое значение используется для 2 пикселей)
                var u4 = Sse2.LoadScalarVector128((uint*)(uRow + (x / 2))).AsByte();
                var v4 = Sse2.LoadScalarVector128((uint*)(vRow + (x / 2))).AsByte();

                // Дублируем U и V: [u0,u1,u2,u3] → [u0,u0,u1,u1,u2,u2,u3,u3]
                var uDup = Sse2.UnpackLow(u4, u4);
                var vDup = Sse2.UnpackLow(v4, v4);

                // Расширяем до int16
                var yShort = Sse41.ConvertToVector128Int16(yVec);
                var uShort = Sse41.ConvertToVector128Int16(uDup);
                var vShort = Sse41.ConvertToVector128Int16(vDup);

                // d = U - 128, e = V - 128
                var d = Sse2.Subtract(uShort, c128);
                var e = Sse2.Subtract(vShort, c128);

                // R = Y + ((359 * e + 128) >> 8)
                var rDelta = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.MultiplyLow(cVtoR, e), round), 8);
                var rShort = Sse2.Add(yShort, rDelta);

                // G = Y - ((88 * d + 183 * e + 128) >> 8)
                var gDelta = Sse2.ShiftRightArithmetic(
                    Sse2.Add(Sse2.Add(Sse2.MultiplyLow(cUtoG, d), Sse2.MultiplyLow(cVtoG, e)), round), 8);
                var gShort = Sse2.Subtract(yShort, gDelta);

                // B = Y + ((454 * d + 128) >> 8)
                var bDelta = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.MultiplyLow(cUtoB, d), round), 8);
                var bShort = Sse2.Add(yShort, bDelta);

                // PackUnsignedSaturate: int16 → uint8 с насыщением
                var rByte = Sse2.PackUnsignedSaturate(rShort, rShort);
                var gByte = Sse2.PackUnsignedSaturate(gShort, gShort);
                var bByte = Sse2.PackUnsignedSaturate(bShort, bShort);

                // Записываем результат (8 пикселей = 24 байта RGB)
                var dst = rgbRow + (x * 3);
                dst[0] = rByte.GetElement(0); dst[1] = gByte.GetElement(0); dst[2] = bByte.GetElement(0);
                dst[3] = rByte.GetElement(1); dst[4] = gByte.GetElement(1); dst[5] = bByte.GetElement(1);
                dst[6] = rByte.GetElement(2); dst[7] = gByte.GetElement(2); dst[8] = bByte.GetElement(2);
                dst[9] = rByte.GetElement(3); dst[10] = gByte.GetElement(3); dst[11] = bByte.GetElement(3);
                dst[12] = rByte.GetElement(4); dst[13] = gByte.GetElement(4); dst[14] = bByte.GetElement(4);
                dst[15] = rByte.GetElement(5); dst[16] = gByte.GetElement(5); dst[17] = bByte.GetElement(5);
                dst[18] = rByte.GetElement(6); dst[19] = gByte.GetElement(6); dst[20] = bByte.GetElement(6);
                dst[21] = rByte.GetElement(7); dst[22] = gByte.GetElement(7); dst[23] = bByte.GetElement(7);

                x += 8;
            }

            // Остаток скалярно
            while (x < width)
            {
                var yVal = yRow[x];
                var chromaX = x / 2;
                var uVal = uRow[chromaX];
                var vVal = vRow[chromaX];

                var dVal = uVal - 128;
                var eVal = vVal - 128;

                var rVal = yVal + (((359 * eVal) + 128) >> 8);
                var gVal = yVal - (((88 * dVal) + (183 * eVal) + 128) >> 8);
                var bVal = yVal + (((454 * dVal) + 128) >> 8);

                var rgbIdx = x * 3;
                rgbRow[rgbIdx] = ClampByte(rVal);
                rgbRow[rgbIdx + 1] = ClampByte(gVal);
                rgbRow[rgbIdx + 2] = ClampByte(bVal);
                x++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YUV → RGB)

    /// <summary>
    /// AVX2: YUV420P → RGB24 с int16 точной математикой.
    /// Использует SSE41 fallback (AVX2 не даёт преимущества для planar).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx2(
        byte* yPlane, byte* uPlane, byte* vPlane, byte* rgb,
        int width, int height) =>
        // AVX2 не даёт значительного преимущества для planar форматов
        // из-за сложности работы с chroma subsampling через lanes
        ToRgb24Sse41(yPlane, uPlane, vPlane, rgb, width, height);

    #endregion

    #region Validation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);

        if ((width & 1) != 0)
            throw new ArgumentException("Width must be even for YUV420P", nameof(width));

        if ((height & 1) != 0)
            throw new ArgumentException("Height must be even for YUV420P", nameof(height));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateBuffers(int rgbLength, int yLength, int uLength, int vLength, int width, int height)
    {
        var expectedRgb = width * height * 3;
        var expectedY = width * height;
        var expectedChroma = width / 2 * (height / 2);

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

        if (uLength < expectedChroma)
        {
            throw new ArgumentException(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"U plane too small: {uLength} < {expectedChroma}"),
                nameof(uLength));
        }

        if (vLength < expectedChroma)
        {
            throw new ArgumentException(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"V plane too small: {vLength} < {expectedChroma}"),
                nameof(vLength));
        }
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    #endregion
}
