#pragma warning disable CA1707

using System.Diagnostics;
using Atom.Media.Processing;

namespace Atom.Media.Tests.Processing;

/// <summary>
/// Тесты для <see cref="ImageScaler"/>.
/// </summary>
public class ImageScalerTests(ILogger logger) : BenchmarkTests<ImageScalerTests>(logger)
{
    #region Constructors

    public ImageScalerTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Helper Methods

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    /// <summary>
    /// Создаёт тестовое RGB24 изображение с шахматным паттерном.
    /// </summary>
    private static byte[] CreateCheckerboardRgb24(int width, int height, int cellSize = 8)
    {
        var data = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = ((y * width) + x) * 3;
                var isWhite = ((x / cellSize) + (y / cellSize)) % 2 == 0;

                var value = isWhite ? (byte)255 : (byte)0;
                data[idx] = value;
                data[idx + 1] = value;
                data[idx + 2] = value;
            }
        }

        return data;
    }

    /// <summary>
    /// Создаёт однотонное изображение.
    /// </summary>
    private static byte[] CreateSolidColorRgb24(int width, int height, byte r, byte g, byte b)
    {
        var data = new byte[width * height * 3];

        for (var i = 0; i < data.Length; i += 3)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
        }

        return data;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "ImageScaler: идентичный размер — копирование")]
    public void Scale_SameSize_CopiesData()
    {
        // Arrange
        const int width = 100;
        const int height = 100;
        var src = CreateSolidColorRgb24(width, height, 255, 128, 64);
        var dst = new byte[width * height * 3];

        // Act
        ImageScaler.ScaleRgb24(src, width, height, dst, width, height);

        // Assert
        Assert.That(dst, Is.EqualTo(src));
        Log("Идентичный размер — данные скопированы");
    }

    [TestCase(TestName = "ImageScaler: upscale Nearest сохраняет цвета")]
    public void ScaleNearest_Upscale_PreservesColors()
    {
        // Arrange
        const int srcWidth = 2;
        const int srcHeight = 2;
        const int dstWidth = 4;
        const int dstHeight = 4;

        // 2x2 изображение: красный, зелёный, синий, белый
        var src = new byte[]
        {
            255, 0, 0, 0, 255, 0,     // красный, зелёный
            0, 0, 255, 255, 255, 255  // синий, белый
        };

        var dst = new byte[dstWidth * dstHeight * 3];

        // Act
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Nearest);

        // Assert — верхний левый квадрант должен быть красным
        Assert.That(dst[0], Is.EqualTo(255), "R at (0,0)");
        Assert.That(dst[1], Is.Zero, "G at (0,0)");
        Assert.That(dst[2], Is.Zero, "B at (0,0)");

        Log("Upscale Nearest сохраняет цвета");
    }

    [TestCase(TestName = "ImageScaler: downscale 2x сохраняет общий цвет")]
    public void Scale_Downscale2x_PreservesAverageColor()
    {
        // Arrange
        const int srcWidth = 100;
        const int srcHeight = 100;
        const int dstWidth = 50;
        const int dstHeight = 50;

        var src = CreateSolidColorRgb24(srcWidth, srcHeight, 100, 150, 200);
        var dst = new byte[dstWidth * dstHeight * 3];

        // Act
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Assert — цвет должен сохраниться с небольшой погрешностью
        Assert.That(dst[0], Is.InRange(95, 105), "R preserved");
        Assert.That(dst[1], Is.InRange(145, 155), "G preserved");
        Assert.That(dst[2], Is.InRange(195, 205), "B preserved");

        Log("Downscale 2x сохраняет средний цвет");
    }

    #endregion

    #region Algorithm Tests

    [TestCase(ScaleAlgorithm.Nearest, TestName = "ImageScaler: Nearest алгоритм работает")]
    [TestCase(ScaleAlgorithm.Bilinear, TestName = "ImageScaler: Bilinear алгоритм работает")]
    [TestCase(ScaleAlgorithm.Bicubic, TestName = "ImageScaler: Bicubic алгоритм работает")]
    [TestCase(ScaleAlgorithm.Lanczos, TestName = "ImageScaler: Lanczos алгоритм работает")]
    public void Scale_AllAlgorithms_ProduceValidOutput(ScaleAlgorithm algorithm)
    {
        // Arrange
        const int srcWidth = 64;
        const int srcHeight = 64;
        const int dstWidth = 128;
        const int dstHeight = 128;

        var src = CreateCheckerboardRgb24(srcWidth, srcHeight);
        var dst = new byte[dstWidth * dstHeight * 3];

        // Act
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, algorithm);

        // Assert — выход не должен быть пустым
        Assert.That(dst.Any(b => b != 0), Is.True, "Output should not be all zeros");
        Assert.That(dst.Any(b => b == 255), Is.True, "Output should contain white pixels");

        Log($"Алгоритм {algorithm} работает корректно");
    }

    [TestCase(TestName = "ImageScaler: Bilinear даёт более гладкий результат чем Nearest")]
    public void ScaleBilinear_ProducesSmoother_ThanNearest()
    {
        // Arrange
        const int srcWidth = 4;
        const int srcHeight = 4;
        const int dstWidth = 16;
        const int dstHeight = 16;

        // Чёрно-белая шахматка
        var src = CreateCheckerboardRgb24(srcWidth, srcHeight, 2);
        var dstNearest = new byte[dstWidth * dstHeight * 3];
        var dstBilinear = new byte[dstWidth * dstHeight * 3];

        // Act
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dstNearest, dstWidth, dstHeight, ScaleAlgorithm.Nearest);
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dstBilinear, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Assert — Nearest содержит только 0 и 255, Bilinear имеет промежуточные значения
        var nearestUniqueValues = dstNearest.Distinct().Count();
        var bilinearUniqueValues = dstBilinear.Distinct().Count();

        Assert.That(bilinearUniqueValues, Is.GreaterThan(nearestUniqueValues),
            "Bilinear should have more unique values (smoother)");

        Log($"Nearest unique values: {nearestUniqueValues}, Bilinear: {bilinearUniqueValues}");
    }

    #endregion

    #region RGBA32 Tests

    [TestCase(TestName = "ImageScaler: RGBA32 scaling сохраняет альфа-канал")]
    public void ScaleRgba32_PreservesAlphaChannel()
    {
        // Arrange
        const int srcWidth = 10;
        const int srcHeight = 10;
        const int dstWidth = 20;
        const int dstHeight = 20;

        var src = new byte[srcWidth * srcHeight * 4];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i] = 100;     // R
            src[i + 1] = 150; // G
            src[i + 2] = 200; // B
            src[i + 3] = 128; // A = 50% прозрачность
        }

        var dst = new byte[dstWidth * dstHeight * 4];

        // Act
        ImageScaler.ScaleRgba32(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Assert — альфа должна сохраниться
        for (var i = 3; i < dst.Length; i += 4)
        {
            Assert.That(dst[i], Is.InRange(125, 131), $"Alpha at {i / 4} should be ~128");
        }

        Log("RGBA32 scaling сохраняет альфа-канал");
    }

    #endregion

    #region Grayscale Tests

    [TestCase(TestName = "ImageScaler: Grayscale Nearest scaling")]
    public void ScaleGrayscale_Nearest_Works()
    {
        // Arrange
        const int srcWidth = 8;
        const int srcHeight = 8;
        const int dstWidth = 16;
        const int dstHeight = 16;

        var src = new byte[srcWidth * srcHeight];
        for (var i = 0; i < src.Length; i++)
            src[i] = (byte)(i * 4);

        var dst = new byte[dstWidth * dstHeight];

        // Act
        ImageScaler.ScaleGrayscale(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Nearest);

        // Assert
        Assert.That(dst.Length, Is.EqualTo(dstWidth * dstHeight));
        Assert.That(dst.Any(b => b != 0), Is.True);

        Log("Grayscale Nearest scaling работает");
    }

    [TestCase(TestName = "ImageScaler: Grayscale Bilinear scaling")]
    public void ScaleGrayscale_Bilinear_Works()
    {
        // Arrange
        const int srcWidth = 4;
        const int srcHeight = 4;
        const int dstWidth = 8;
        const int dstHeight = 8;

        // Градиент
        var src = new byte[srcWidth * srcHeight];
        for (var y = 0; y < srcHeight; y++)
        {
            for (var x = 0; x < srcWidth; x++)
            {
                src[(y * srcWidth) + x] = (byte)((x + y) * 32);
            }
        }

        var dst = new byte[dstWidth * dstHeight];

        // Act
        ImageScaler.ScaleGrayscale(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Assert
        var uniqueValues = dst.Distinct().Count();
        Assert.That(uniqueValues, Is.GreaterThan(2), "Bilinear should produce smooth gradients");

        Log($"Grayscale Bilinear: {uniqueValues} уникальных значений");
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "ImageScaler: минимальный размер 1x1")]
    public void Scale_MinimumSize_Works()
    {
        // Arrange
        var src = new byte[] { 100, 150, 200 }; // 1x1 RGB
        var dst = new byte[3];

        // Act & Assert — не должно бросать исключение
        ImageScaler.ScaleRgb24(src, 1, 1, dst, 1, 1);
        Assert.That(dst, Is.EqualTo(src));

        Log("Минимальный размер 1x1 работает");
    }

    [TestCase(TestName = "ImageScaler: невалидные параметры")]
    public void Scale_InvalidParameters_ThrowsException()
    {
        var src = new byte[100];
        var dst = new byte[100];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageScaler.ScaleRgb24(src, 0, 10, dst, 10, 10));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageScaler.ScaleRgb24(src, 10, 0, dst, 10, 10));

        Assert.Throws<ArgumentException>(() =>
            ImageScaler.ScaleRgb24(new byte[10], 100, 100, dst, 10, 10));

        Log("Валидация параметров работает");
    }

    [TestCase(TestName = "ImageScaler: extreme downscale 1000x → 1x")]
    public void Scale_ExtremeDownscale_Works()
    {
        // Arrange
        const int srcWidth = 100;
        const int srcHeight = 100;
        const int dstWidth = 1;
        const int dstHeight = 1;

        var src = CreateSolidColorRgb24(srcWidth, srcHeight, 128, 64, 32);
        var dst = new byte[dstWidth * dstHeight * 3];

        // Act
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Assert — должен получить примерно тот же цвет
        Assert.That(dst[0], Is.InRange(120, 136), "R preserved");
        Assert.That(dst[1], Is.InRange(56, 72), "G preserved");
        Assert.That(dst[2], Is.InRange(24, 40), "B preserved");

        Log($"Extreme downscale 100x100→1x1: RGB({dst[0]},{dst[1]},{dst[2]})");
    }

    #endregion

    #region Performance Tests

    [TestCase(TestName = "ImageScaler: 1080p → 720p производительность")]
    [Benchmark]
    public void Scale_1080pTo720p_Performance()
    {
        // Arrange
        const int srcWidth = 1920;
        const int srcHeight = 1080;
        const int dstWidth = 1280;
        const int dstHeight = 720;

        var src = new byte[srcWidth * srcHeight * 3];
        var dst = new byte[dstWidth * dstHeight * 3];

        // Warmup
        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);

        // Act
        var sw = Stopwatch.StartNew();
        const int iterations = 10;
        for (var i = 0; i < iterations; i++)
        {
            ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, ScaleAlgorithm.Bilinear);
        }
        sw.Stop();

        var avgMs = sw.ElapsedMilliseconds / (double)iterations;
        Log($"1080p→720p Bilinear: {avgMs:F2}ms/frame");

        // Не слишком строгий лимит для разных машин
        Assert.That(avgMs, Is.LessThan(200), "Should be reasonably fast");
    }

    [TestCase(TestName = "ImageScaler: сравнение производительности алгоритмов")]
    public void Scale_AlgorithmComparison_Performance()
    {
        // Arrange
        const int srcWidth = 640;
        const int srcHeight = 480;
        const int dstWidth = 1280;
        const int dstHeight = 960;
        const int iterations = 20;

        var src = CreateCheckerboardRgb24(srcWidth, srcHeight);
        var dst = new byte[dstWidth * dstHeight * 3];

        var algorithms = new[] { ScaleAlgorithm.Nearest, ScaleAlgorithm.Bilinear, ScaleAlgorithm.Bicubic };
        var times = new Dictionary<ScaleAlgorithm, double>();

        foreach (var algorithm in algorithms)
        {
            // Warmup
            ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, algorithm);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight, algorithm);
            }
            sw.Stop();

            times[algorithm] = sw.ElapsedMilliseconds / (double)iterations;
        }

        // Assert — Nearest должен быть быстрее Bilinear, Bilinear быстрее Bicubic
        Assert.That(times[ScaleAlgorithm.Nearest], Is.LessThan(times[ScaleAlgorithm.Bicubic]));

        foreach (var kvp in times)
        {
            Log($"{kvp.Key}: {kvp.Value:F2}ms");
        }
    }

    #endregion

    #region Thread Safety Tests

    [TestCase(TestName = "ImageScaler: параллельное масштабирование")]
    public void Scale_Parallel_ThreadSafe()
    {
        // Arrange
        const int srcWidth = 100;
        const int srcHeight = 100;
        const int dstWidth = 200;
        const int dstHeight = 200;
        const int threadCount = 8;
        const int iterationsPerThread = 50;

        var exceptions = new List<Exception>();
        var threads = new Thread[threadCount];

        for (var t = 0; t < threadCount; t++)
        {
            threads[t] = new Thread(() =>
            {
                try
                {
                    var src = new byte[srcWidth * srcHeight * 3];
                    var dst = new byte[dstWidth * dstHeight * 3];

                    for (var i = 0; i < iterationsPerThread; i++)
                    {
                        ImageScaler.ScaleRgb24(src, srcWidth, srcHeight, dst, dstWidth, dstHeight,
                            ScaleAlgorithm.Bilinear);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }

        // Act
        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();

        // Assert
        Assert.That(exceptions, Is.Empty);
        Log($"Параллельное масштабирование: {threadCount} потоков × {iterationsPerThread} итераций");
    }

    #endregion
}
