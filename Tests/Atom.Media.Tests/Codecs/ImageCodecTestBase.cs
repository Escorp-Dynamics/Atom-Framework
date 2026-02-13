#pragma warning disable CA1000, CA1707, CA1819, MA0048, NUnit2007, NUnit2045, S2368, S4144

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Базовый абстрактный класс для тестов кодеков изображений.
/// Поддерживает тестирование пар кодеков (PNG ↔ PNG, PNG ↔ WebP и т.д.)
/// с различными разрешениями и аппаратными ускорителями.
/// </summary>
/// <typeparam name="TEncoder">Тип кодека-энкодера.</typeparam>
/// <typeparam name="TDecoder">Тип кодека-декодера.</typeparam>
[TestFixture]
public abstract class ImageCodecTestBase<TEncoder, TDecoder>
    where TEncoder : class, IImageCodec, new()
    where TDecoder : class, IImageCodec, new()
{
    #region Constants

    /// <summary>Количество прогревов перед замером производительности.</summary>
    protected const int WarmupIterations = 5;

    /// <summary>Количество итераций для замера производительности.</summary>
    protected const int BenchmarkIterations = 20;

    /// <summary>Количество первых замеров, исключаемых из расчёта среднего.</summary>
    protected const int WarmupMeasurements = 3;

    /// <summary>Папка для сохранения результатов конвертации.</summary>
    protected const string OutputFolder = "output";

    #endregion

    #region Abstract Properties

    /// <summary>Имя пары конвертации (например, "PNG ↔ PNG").</summary>
    protected abstract string PairName { get; }

    /// <summary>Расширение файла для энкодера (например, ".png").</summary>
    protected abstract string EncoderExtension { get; }

    /// <summary>Расширение файла для декодера (например, ".png").</summary>
    protected abstract string DecoderExtension { get; }

    /// <summary>Формат пикселей для тестов.</summary>
    protected virtual VideoPixelFormat TestPixelFormat => VideoPixelFormat.Rgba32;

    /// <summary>Ожидаемая точность round-trip (0 = lossless).</summary>
    protected virtual int RoundTripTolerance => 0;

    /// <summary>Реализованные ускорители для этой пары кодеков.</summary>
    protected virtual HardwareAcceleration ImplementedAccelerations { get; } =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse2 |
        HardwareAcceleration.Avx2;

    /// <summary>Путь к тестовому файлу (опционально).</summary>
    protected virtual string? TestAssetPath => null;

    /// <summary>Сохранять результаты конвертации для визуального анализа.</summary>
    protected virtual bool SaveOutputForVisualInspection => true;

    #endregion

    #region Resolution Presets

    /// <summary>Стандартные разрешения для тестов производительности.</summary>
    protected static readonly (string Name, int Width, int Height)[] Resolutions =
    [
        ("480p", 640, 480),
        ("720p", 1280, 720),
        ("1080p", 1920, 1080),
        ("2K", 2560, 1440),
        ("4K", 3840, 2160),
        ("8K", 7680, 4320),
    ];

    /// <summary>Разрешения для базовых тестов (ускоренные).</summary>
    protected static readonly (string Name, int Width, int Height)[] BasicResolutions =
    [
        ("480p", 640, 480),
        ("1080p", 1920, 1080),
        ("4K", 3840, 2160),
    ];

    #endregion

    #region Fields

    private byte[]? testAssetData;
    private string outputDirectory = null!;

    #endregion

    #region Setup/Teardown

    [OneTimeSetUp]
    public virtual void OneTimeSetUp()
    {
        // Загружаем тестовый файл если указан
        if (TestAssetPath is not null)
        {
            var assetPath = Path.Combine(TestContext.CurrentContext.TestDirectory, TestAssetPath);
            if (File.Exists(assetPath))
            {
                testAssetData = File.ReadAllBytes(assetPath);
                TestContext.Out.WriteLine($"Загружен тестовый файл: {assetPath}, размер: {testAssetData.Length} байт");
            }
            else
            {
                TestContext.Out.WriteLine($"Тестовый файл не найден: {assetPath}");
            }
        }

        // Создаём директорию для выходных файлов
        // PairName используется напрямую как имя папки
        outputDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, OutputFolder, PairName);
        if (SaveOutputForVisualInspection)
        {
            Directory.CreateDirectory(outputDirectory);
            TestContext.Out.WriteLine($"Выходная директория: {outputDirectory}");
        }
    }

    #endregion

    #region Abstract Methods

    /// <summary>Создаёт энкодер с указанным ускорителем.</summary>
    protected virtual TEncoder CreateEncoder(HardwareAcceleration acceleration = HardwareAcceleration.Auto)
        => new() { Acceleration = acceleration };

    /// <summary>Создаёт декодер с указанным ускорителем.</summary>
    protected virtual TDecoder CreateDecoder(HardwareAcceleration acceleration = HardwareAcceleration.Auto)
        => new() { Acceleration = acceleration };

    #endregion

    #region Test: Round-Trip Correctness

    /// <summary>
    /// Тест: Round-trip кодирование/декодирование для разных разрешений.
    /// </summary>
    [Test, Order(1)]
    [TestCase("480p", 640, 480)]
    [TestCase("720p", 1280, 720)]
    [TestCase("1080p", 1920, 1080)]
    [TestCase("2K", 2560, 1440)]
    [TestCase("4K", 3840, 2160)]
    public void RoundTripCorrectness(string resName, int width, int height)
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Round-trip {resName} ({width}x{height}) ═══");

        using var encoder = CreateEncoder();
        using var decoder = CreateDecoder();

        // Инициализация
        var encParams = new ImageCodecParameters(width, height, TestPixelFormat);
        var encResult = encoder.InitializeEncoder(encParams);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success), "Ошибка инициализации энкодера");

        // Создаём тестовое изображение
        using var originalBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        FillTestPattern(originalBuffer, width, height);

        // Кодируем
        var estimatedSize = encoder.EstimateEncodedSize(width, height, TestPixelFormat);
        var encodedData = new byte[estimatedSize];
        var roFrame = originalBuffer.AsReadOnlyFrame();

        var encodeResult = encoder.Encode(roFrame, encodedData, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success), "Ошибка кодирования");
        Assert.That(bytesWritten, Is.GreaterThan(0), "Данные не записаны");

        TestContext.Out.WriteLine($"  Закодировано: {bytesWritten:N0} байт ({(double)bytesWritten / (width * height * GetBytesPerPixel(TestPixelFormat)) * 100:F1}%)");

        // Декодируем
        var decParams = new ImageCodecParameters(width, height, TestPixelFormat);
        var decResult = decoder.InitializeDecoder(decParams);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success), "Ошибка инициализации декодера");

        using var decodedBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        var decodedFrame = decodedBuffer.AsFrame();

        var decodeResult = decoder.Decode(encodedData.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success), "Ошибка декодирования");

        // Сравниваем построчно (учитываем stride != width из-за выравнивания)
        var originalPlane = originalBuffer.AsReadOnlyFrame().PackedData;
        var decodedPlane = decodedBuffer.AsReadOnlyFrame().PackedData;

        var (errors, maxError) = ComparePlanes(originalPlane, decodedPlane, RoundTripTolerance);

        if (errors == 0)
        {
            TestContext.Out.WriteLine($"  ✓ Round-trip успешен (tolerance={RoundTripTolerance})");
        }
        else
        {
            TestContext.Out.WriteLine($"  ✗ Ошибки: {errors:N0}, максимальная: {maxError}");
        }

        Assert.That(errors, Is.Zero, $"Round-trip ошибки: {errors}, max error: {maxError}");

        // Сохраняем результат для визуального анализа
        if (SaveOutputForVisualInspection)
        {
            SaveEncodedOutput(encodedData.AsSpan(0, bytesWritten), $"roundtrip_{resName}");
        }
    }

    #endregion

    #region Test: Accelerator Correctness

    /// <summary>
    /// Тест: Проверка корректности всех ускорителей.
    /// </summary>
    [Test, Order(2)]
    public void AcceleratorCorrectness()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Проверка корректности ускорителей ═══");

        const int width = 1280;
        const int height = 720;

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE2", Accel: HardwareAcceleration.Sse2),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
        };

        // Получаем эталонный результат от Scalar
        using var originalBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        FillTestPattern(originalBuffer, width, height);

        using var scalarEncoder = CreateEncoder(HardwareAcceleration.None);
        scalarEncoder.InitializeEncoder(new ImageCodecParameters(width, height, TestPixelFormat));

        var estimatedSize = scalarEncoder.EstimateEncodedSize(width, height, TestPixelFormat);
        var scalarEncoded = new byte[estimatedSize];
        var roFrame = originalBuffer.AsReadOnlyFrame();
        scalarEncoder.Encode(roFrame, scalarEncoded, out var scalarBytes);

        using var scalarDecoder = CreateDecoder(HardwareAcceleration.None);
        scalarDecoder.InitializeDecoder(new ImageCodecParameters(width, height, TestPixelFormat));

        using var scalarDecodedBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        var scalarDecodedFrame = scalarDecodedBuffer.AsFrame();
        scalarDecoder.Decode(scalarEncoded.AsSpan(0, scalarBytes), ref scalarDecodedFrame);

        var referencePlane = scalarDecodedBuffer.AsReadOnlyFrame().PackedData;

        foreach (var (name, accel) in accelerators)
        {
            if (!HardwareAccelerationInfo.IsSupported(accel))
            {
                TestContext.Out.WriteLine($"  ⊘ {name}: не поддерживается на этом CPU");
                continue;
            }

            if ((ImplementedAccelerations & accel) == 0 && accel != HardwareAcceleration.None)
            {
                TestContext.Out.WriteLine($"  ⊘ {name}: не реализован");
                continue;
            }

            // Encode с этим ускорителем
            using var encoder = CreateEncoder(accel);
            encoder.InitializeEncoder(new ImageCodecParameters(width, height, TestPixelFormat));

            var encoded = new byte[estimatedSize];
            encoder.Encode(roFrame, encoded, out var bytesWritten);

            // Decode с этим ускорителем
            using var decoder = CreateDecoder(accel);
            decoder.InitializeDecoder(new ImageCodecParameters(width, height, TestPixelFormat));

            using var decodedBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
            var decodedFrame = decodedBuffer.AsFrame();
            decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

            var decodedPlane = decodedBuffer.AsReadOnlyFrame().PackedData;
            var (errors, maxError) = ComparePlanes(referencePlane, decodedPlane, 0);

            if (errors == 0)
            {
                TestContext.Out.WriteLine($"  ✓ {name}: все пиксели корректны");
            }
            else
            {
                TestContext.Out.WriteLine($"  ✗ {name}: {errors:N0} ошибок, max error={maxError}");
                Assert.Fail($"{name} имеет {errors} ошибок конвертации");
            }
        }
    }

    #endregion

    #region Test: Real File Conversion

    /// <summary>
    /// Тест: Конвертация реального файла.
    /// </summary>
    [Test, Order(3)]
    public void RealFileConversion()
    {
        if (testAssetData is null)
        {
            Assert.Ignore("Тестовый файл не загружен");
            return;
        }

        TestContext.Out.WriteLine($"═══ {PairName}: Конвертация реального файла ═══");

        using var decoder = CreateDecoder();
        using var encoder = CreateEncoder();

        // Получаем информацию о файле (используем метод кодека если доступен)
        if (decoder is PngCodec pngDecoder)
        {
            pngDecoder.GetInfo(testAssetData, out var info);
            decoder.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

            using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
            var frame = frameBuffer.AsFrame();

            var decodeResult = decoder.Decode(testAssetData, ref frame);
            Assert.That(decodeResult, Is.EqualTo(CodecResult.Success), "Ошибка декодирования");

            TestContext.Out.WriteLine($"  Декодировано: {info.Width}x{info.Height}, {info.PixelFormat}");

            // Перекодируем
            encoder.InitializeEncoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

            var estimatedSize = encoder.EstimateEncodedSize(info.Width, info.Height, info.PixelFormat);
            var encodedData = new byte[estimatedSize];
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var encodeResult = encoder.Encode(roFrame, encodedData, out var bytesWritten);
            Assert.That(encodeResult, Is.EqualTo(CodecResult.Success), "Ошибка кодирования");

            TestContext.Out.WriteLine($"  Закодировано: {bytesWritten:N0} байт");

            // Сохраняем для визуального анализа
            if (SaveOutputForVisualInspection)
            {
                SaveEncodedOutput(encodedData.AsSpan(0, bytesWritten), "real_file");
                TestContext.Out.WriteLine($"  Сохранено: {Path.Combine(outputDirectory, $"real_file{EncoderExtension}")}");
            }
        }
        else
        {
            Assert.Ignore("Метод GetInfo недоступен для этого декодера");
        }
    }

    #endregion

    #region Test: Performance Matrix

    /// <summary>
    /// Тест: Матрица производительности по разрешениям.
    /// </summary>
    [Test, Order(10)]
    public void PerformanceMatrix()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Матрица производительности ═══");

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE2", Accel: HardwareAcceleration.Sse2),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
        };

        // Фильтруем доступные ускорители
        var availableAccels = accelerators
            .Where(a => HardwareAccelerationInfo.IsSupported(a.Accel))
            .Where(a => a.Accel == HardwareAcceleration.None || (ImplementedAccelerations & a.Accel) != 0)
            .ToArray();

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"{"Разрешение",-12} {"Пикселей",12} │ {string.Join(" │ ", availableAccels.Select(a => $"{a.Name,10}"))} │");
        TestContext.Out.WriteLine(new string('─', 30 + (availableAccels.Length * 14)));

        foreach (var (resName, width, height) in BasicResolutions)
        {
            var pixelCount = width * height;
            var times = new List<string>();

            foreach (var (_, accel) in availableAccels)
            {
                var avgMs = MeasureRoundTripPerformance(width, height, accel);
                var fps = 1000.0 / avgMs;
                times.Add($"{avgMs,6:F1}ms/{fps,4:F0}fps");
            }

            TestContext.Out.WriteLine($"{resName,-12} {pixelCount,12:N0} │ {string.Join(" │ ", times)} │");
        }

        TestContext.Out.WriteLine();
    }

    /// <summary>
    /// Тест: Сравнение производительности ускорителей (1080p).
    /// </summary>
    [Test, Order(11)]
    public void AcceleratorPerformanceComparison()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Производительность ускорителей (1080p) ═══");

        const int width = 1920;
        const int height = 1080;

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE2", Accel: HardwareAcceleration.Sse2),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
        };

        var results = new List<(string Name, double EncodeMs, double DecodeMs, double TotalMs)>();

        foreach (var (name, accel) in accelerators)
        {
            if (!HardwareAccelerationInfo.IsSupported(accel))
                continue;

            if ((ImplementedAccelerations & accel) == 0 && accel != HardwareAcceleration.None)
                continue;

            var (encodeMs, decodeMs) = MeasureEncodeDecode(width, height, accel);
            results.Add((name, encodeMs, decodeMs, encodeMs + decodeMs));
        }

        // Вывод таблицы
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"{"Ускоритель",-12} {"Encode",12} {"Decode",12} {"Total",12} {"Speedup",10}");
        TestContext.Out.WriteLine(new string('─', 60));

        var scalarTotal = results.FirstOrDefault(r => r.Name == "Scalar").TotalMs;

        foreach (var (name, encode, decode, total) in results)
        {
            var speedup = scalarTotal > 0 ? scalarTotal / total : 1.0;
            TestContext.Out.WriteLine($"{name,-12} {encode,9:F2} ms {decode,9:F2} ms {total,9:F2} ms {speedup,9:F2}x");
        }

        TestContext.Out.WriteLine();

        // Проверки
        var sseResult = results.FirstOrDefault(r => r.Name == "SSE2");
        var avx2Result = results.FirstOrDefault(r => r.Name == "AVX2");

        if (sseResult != default && scalarTotal > 0)
        {
            var sseSpeedup = scalarTotal / sseResult.TotalMs;
            Assert.That(sseSpeedup, Is.GreaterThanOrEqualTo(0.9), $"SSE2 слишком медленный: {sseSpeedup:F2}x");
            TestContext.Out.WriteLine($"  ✓ SSE2 vs Scalar: {sseSpeedup:F2}x");
        }

        if (avx2Result != default && sseResult != default)
        {
            var avx2Speedup = sseResult.TotalMs / avx2Result.TotalMs;
            Assert.That(avx2Speedup, Is.GreaterThanOrEqualTo(0.9), $"AVX2 слишком медленный vs SSE2: {avx2Speedup:F2}x");
            TestContext.Out.WriteLine($"  ✓ AVX2 vs SSE2: {avx2Speedup:F2}x");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>Заполняет буфер тестовым паттерном.</summary>
    protected virtual void FillTestPattern(VideoFrameBuffer buffer, int width, int height)
    {
        var frame = buffer.AsFrame();
        var plane = frame.PackedData;
        var bpp = GetBytesPerPixel(TestPixelFormat);

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var offset = x * bpp;

                if (bpp >= 3)
                {
                    row[offset] = (byte)(x * 255 / width);     // R
                    row[offset + 1] = (byte)(y * 255 / height); // G
                    row[offset + 2] = (byte)((x + y) * 127 / (width + height)); // B
                }

                if (bpp == 4)
                {
                    row[offset + 3] = 255; // A
                }
            }
        }
    }

    /// <summary>Сравнивает два массива пикселей.</summary>
    protected static (int Errors, int MaxError) ComparePixels(ReadOnlySpan<byte> original, ReadOnlySpan<byte> decoded, int tolerance)
    {
        var errors = 0;
        var maxError = 0;

        var len = Math.Min(original.Length, decoded.Length);
        for (var i = 0; i < len; i++)
        {
            var diff = Math.Abs(original[i] - decoded[i]);
            if (diff > tolerance)
            {
                errors++;
                maxError = Math.Max(maxError, diff);
            }
        }

        return (errors, maxError);
    }

    /// <summary>Сравнивает две плоскости построчно, учитывая различие stride и width.</summary>
    protected static (int Errors, int MaxError) ComparePlanes(ReadOnlyPlane<byte> original, ReadOnlyPlane<byte> decoded, int tolerance)
    {
        var errors = 0;
        var maxError = 0;

        var height = Math.Min(original.Height, decoded.Height);
        var width = Math.Min(original.Width, decoded.Width);

        for (var y = 0; y < height; y++)
        {
            var origRow = original.GetRow(y)[..width];
            var decRow = decoded.GetRow(y)[..width];

            for (var x = 0; x < width; x++)
            {
                var diff = Math.Abs(origRow[x] - decRow[x]);
                if (diff > tolerance)
                {
                    errors++;
                    maxError = Math.Max(maxError, diff);
                }
            }
        }

        return (errors, maxError);
    }

    /// <summary>Сохраняет закодированные данные в файл.</summary>
    protected void SaveEncodedOutput(ReadOnlySpan<byte> data, string baseName)
    {
        if (!SaveOutputForVisualInspection) return;

        var fileName = $"{baseName}{EncoderExtension}";
        var filePath = Path.Combine(outputDirectory, fileName);
        File.WriteAllBytes(filePath, data.ToArray());
    }

    /// <summary>Измеряет производительность round-trip.</summary>
    private double MeasureRoundTripPerformance(int width, int height, HardwareAcceleration accel)
    {
        using var encoder = CreateEncoder(accel);
        using var decoder = CreateDecoder(accel);

        encoder.InitializeEncoder(new ImageCodecParameters(width, height, TestPixelFormat));
        decoder.InitializeDecoder(new ImageCodecParameters(width, height, TestPixelFormat));

        using var originalBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        FillTestPattern(originalBuffer, width, height);

        var estimatedSize = encoder.EstimateEncodedSize(width, height, TestPixelFormat);
        var encoded = new byte[estimatedSize];
        var roFrame = originalBuffer.AsReadOnlyFrame();

        using var decodedBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);

        // Прогрев
        for (var w = 0; w < WarmupIterations; w++)
        {
            encoder.Encode(roFrame, encoded, out var written);
            var frame = decodedBuffer.AsFrame();
            decoder.Decode(encoded.AsSpan(0, written), ref frame);
        }

        // Замер
        var times = new double[BenchmarkIterations];
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            encoder.Encode(roFrame, encoded, out var written);
            var frame = decodedBuffer.AsFrame();
            decoder.Decode(encoded.AsSpan(0, written), ref frame);
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }

        return times.Skip(WarmupMeasurements).Average();
    }

    /// <summary>Измеряет производительность encode и decode отдельно.</summary>
    private (double EncodeMs, double DecodeMs) MeasureEncodeDecode(int width, int height, HardwareAcceleration accel)
    {
        using var encoder = CreateEncoder(accel);
        using var decoder = CreateDecoder(accel);

        encoder.InitializeEncoder(new ImageCodecParameters(width, height, TestPixelFormat));
        decoder.InitializeDecoder(new ImageCodecParameters(width, height, TestPixelFormat));

        using var originalBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);
        FillTestPattern(originalBuffer, width, height);

        var estimatedSize = encoder.EstimateEncodedSize(width, height, TestPixelFormat);
        var encoded = new byte[estimatedSize];
        var roFrame = originalBuffer.AsReadOnlyFrame();

        using var decodedBuffer = new VideoFrameBuffer(width, height, TestPixelFormat);

        // Прогрев
        encoder.Encode(roFrame, encoded, out var bytesWritten);
        for (var w = 0; w < WarmupIterations; w++)
        {
            encoder.Encode(roFrame, encoded, out _);
            var frame = decodedBuffer.AsFrame();
            decoder.Decode(encoded.AsSpan(0, bytesWritten), ref frame);
        }

        // Замер Encode
        var encodeTimes = new double[BenchmarkIterations];
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            encoder.Encode(roFrame, encoded, out _);
            sw.Stop();
            encodeTimes[i] = sw.Elapsed.TotalMilliseconds;
        }

        // Замер Decode
        var decodeTimes = new double[BenchmarkIterations];
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var frame = decodedBuffer.AsFrame();
            decoder.Decode(encoded.AsSpan(0, bytesWritten), ref frame);
            sw.Stop();
            decodeTimes[i] = sw.Elapsed.TotalMilliseconds;
        }

        return (
            encodeTimes.Skip(WarmupMeasurements).Average(),
            decodeTimes.Skip(WarmupMeasurements).Average()
        );
    }

    /// <summary>Возвращает количество байт на пиксель.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBytesPerPixel(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Gray8 => 1,
        VideoPixelFormat.Gray16Le => 2,
        VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => 3,
        VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32 => 4,
        _ => 4,
    };

    #endregion
}
