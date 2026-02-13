using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Atom.Media.Tests;

/// <summary>
/// Специфичные тесты PNG кодека (заголовки, инициализация, сигнатуры).
/// Тесты round-trip и производительности вынесены в PngPngCodecTests.
/// </summary>
[TestFixture]
public sealed class PngCodecSpecificTests(ILogger logger) : BenchmarkTests<PngCodecSpecificTests>(logger)
{
    #region Constants

    private const string TestAssetPath = "assets/test.png";
    private const int TestWidth = 256;
    private const int TestHeight = 256;

    #endregion

    #region Fields

    private byte[]? testPngData;
    private PngCodec? codec;

    #endregion

    #region Constructors

    public PngCodecSpecificTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Setup/Teardown

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        // Загружаем тестовый PNG файл
        var assetPath = Path.Combine(TestContext.CurrentContext.TestDirectory, TestAssetPath);
        if (File.Exists(assetPath))
        {
            testPngData = File.ReadAllBytes(assetPath);
            TestContext.Out.WriteLine($"Загружен тестовый файл: {assetPath}, размер: {testPngData.Length} байт");
        }
        else
        {
            TestContext.Out.WriteLine($"Тестовый файл не найден: {assetPath}");
        }
    }

    [SetUp]
    public void SetUp() => codec = new PngCodec();

    [TearDown]
    public override void GlobalTearDown()
    {
        codec?.Dispose();
        base.GlobalTearDown();
    }

    #endregion

    #region Initialization Tests

    [TestCase(TestName = "PngCodec: инициализация декодера")]
    public void InitializeDecoderSuccess()
    {
        // Arrange
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Rgba32);

        // Act
        var result = codec!.InitializeDecoder(parameters);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(codec.Parameters.Width, Is.EqualTo(TestWidth));
        Assert.That(codec.Parameters.Height, Is.EqualTo(TestHeight));
    }

    [TestCase(TestName = "PngCodec: инициализация энкодера")]
    public void InitializeEncoderSuccess()
    {
        // Arrange
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Rgba32);

        // Act
        var result = codec!.InitializeEncoder(parameters);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "PngCodec: инициализация энкодера с некорректными размерами")]
    public void InitializeEncoderInvalidDimensionsReturnsInvalidData()
    {
        // Arrange
        var parameters = new ImageCodecParameters(0, 0, VideoPixelFormat.Rgba32);

        // Act
        var result = codec!.InitializeEncoder(parameters);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "PngCodec: инициализация энкодера с неподдерживаемым форматом")]
    public void InitializeEncoderUnsupportedFormatReturnsUnsupportedFormat()
    {
        // Arrange
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Yuv420P);

        // Act
        var result = codec!.InitializeEncoder(parameters);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
    }

    #endregion

    #region Header Detection Tests

    [TestCase(TestName = "PngCodec: определение PNG по сигнатуре")]
    public void CanDecodeValidPngSignatureReturnsTrue()
    {
        // Arrange
        ReadOnlySpan<byte> validHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        // Act
        var result = codec!.CanDecode(validHeader);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "PngCodec: отклонение невалидной сигнатуры")]
    public void CanDecodeInvalidSignatureReturnsFalse()
    {
        // Arrange
        ReadOnlySpan<byte> invalidHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]; // JPEG

        // Act
        var result = codec!.CanDecode(invalidHeader);

        // Assert
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "PngCodec: отклонение слишком короткого заголовка")]
    public void CanDecodeTooShortHeaderReturnsFalse()
    {
        // Arrange
        ReadOnlySpan<byte> shortHeader = [0x89, 0x50, 0x4E];

        // Act
        var result = codec!.CanDecode(shortHeader);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetInfo Tests

    [TestCase(TestName = "PngCodec: получение информации о PNG файле")]
    public void GetInfoValidPngReturnsCorrectInfo()
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        // Act
        var result = codec!.GetInfo(testPngData, out var info);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
        Assert.That(info.BitDepth, Is.EqualTo(8));
        TestContext.Out.WriteLine($"PNG Info: {info.Width}x{info.Height}, {info.BitDepth} bit, colorType={info.ColorType}");
    }

    #endregion

    #region Decode Tests

    [TestCase(TestName = "PngCodec: декодирование реального PNG файла")]
    public void DecodeRealPngFileSuccess()
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        codec!.GetInfo(testPngData, out var info);
        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        var frame = frameBuffer.AsFrame();

        // Act
        var result = codec.Decode(testPngData, ref frame);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"Декодировано: {info.Width}x{info.Height}");
    }

    [TestCase(TestName = "PngCodec: декодирование невалидных данных")]
    public void DecodeInvalidDataReturnsInvalidData()
    {
        // Arrange
        codec!.InitializeDecoder(new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Rgba32));
        using var frameBuffer = new VideoFrameBuffer(TestWidth, TestHeight, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var result = codec.Decode(invalidData, ref frame);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    #endregion

    #region Encode Tests

    [TestCase(TestName = "PngCodec: кодирование RGBA32 изображения")]
    public void EncodeRgba32ImageSuccess()
    {
        // Arrange
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var output = new byte[outputSize];

        // Act
        var result = codec.Encode(roFrame, output, out var bytesWritten);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        Assert.That(output[..8], Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        TestContext.Out.WriteLine($"Закодировано: {bytesWritten} байт");
    }

    [TestCase(TestName = "PngCodec: кодирование RGB24 изображения")]
    public void EncodeRgb24ImageSuccess()
    {
        // Arrange
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgb24));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgb24);
        FillTestPatternRgb24(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgb24);
        var output = new byte[outputSize];

        // Act
        var result = codec.Encode(roFrame, output, out var bytesWritten);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано RGB24: {bytesWritten} байт");
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "PngCodec: round-trip кодирование/декодирование")]
    public void RoundTripEncodeAndDecodeDataMatches()
    {
        // Arrange
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);

        // Encode
        var encoder = new PngCodec();
        encoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        var outputSize = encoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new PngCodec();
        decoder.GetInfo(encoded.AsSpan(0, bytesWritten), out var info);
        decoder.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        // Compare
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var decodedData = decodedBuffer.AsReadOnlyFrame().PackedData.Data;

        var matches = originalData.SequenceEqual(decodedData);
        Assert.That(matches, Is.True, "Декодированные данные должны совпадать с оригинальными");

        encoder.Dispose();
        decoder.Dispose();
    }

    #endregion

    #region Hardware Acceleration Tests

    [TestCase(HardwareAcceleration.None, TestName = "PngCodec: декодирование без ускорения (Scalar)")]
    [TestCase(HardwareAcceleration.Sse2, TestName = "PngCodec: декодирование с SSE2")]
    [TestCase(HardwareAcceleration.Avx2, TestName = "PngCodec: декодирование с AVX2")]
    public void DecodeWithSpecificAccelerationSuccess(HardwareAcceleration acceleration)
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        using var acceleratedCodec = new PngCodec { Acceleration = acceleration };
        acceleratedCodec.GetInfo(testPngData, out var info);
        acceleratedCodec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        var frame = frameBuffer.AsFrame();

        // Act
        var result = acceleratedCodec.Decode(testPngData, ref frame);

        // Assert
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"Декодировано с {acceleration}: {info.Width}x{info.Height}");
    }

    [TestCase(TestName = "PngCodec: результаты декодирования идентичны для всех ускорителей")]
    public void DecodeResultsAreIdenticalAcrossAccelerators()
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        // Вычисляем хеш данных для проверки целостности
        var dataHash = testPngData!.Aggregate(0, (h, b) => (h * 31) + b);
        TestContext.Out.WriteLine($"testPngData hash: {dataHash}, length: {testPngData!.Length}");

        using var scalarCodec = new PngCodec { Acceleration = HardwareAcceleration.None };
        using var sse2Codec = new PngCodec { Acceleration = HardwareAcceleration.Sse2 };
        using var avx2Codec = new PngCodec { Acceleration = HardwareAcceleration.Avx2 };

        scalarCodec.GetInfo(testPngData, out var info);

        TestContext.Out.WriteLine($"Info: {info.Width}x{info.Height}, PixelFormat={info.PixelFormat}");

        var parameters = new ImageCodecParameters(info.Width, info.Height, info.PixelFormat);
        scalarCodec.InitializeDecoder(parameters);
        sse2Codec.InitializeDecoder(parameters);
        avx2Codec.InitializeDecoder(parameters);

        using var scalarBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        using var sse2Buffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        using var avx2Buffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);

        // Очищаем буферы, чтобы исключить влияние данных из ArrayPool
        scalarBuffer.Clear();
        sse2Buffer.Clear();
        avx2Buffer.Clear();

        var scalarFrame = scalarBuffer.AsFrame();
        var sse2Frame = sse2Buffer.AsFrame();
        var avx2Frame = avx2Buffer.AsFrame();

        // Act
        var scalarResult = scalarCodec.Decode(testPngData, ref scalarFrame);
        var sse2Result = sse2Codec.Decode(testPngData, ref sse2Frame);
        var avx2Result = avx2Codec.Decode(testPngData, ref avx2Frame);

        // Assert
        Assert.That(scalarResult, Is.EqualTo(CodecResult.Success));
        Assert.That(sse2Result, Is.EqualTo(CodecResult.Success));
        Assert.That(avx2Result, Is.EqualTo(CodecResult.Success));

        var scalarData = scalarBuffer.AsReadOnlyFrame().PackedData.Data;
        var sse2Data = sse2Buffer.AsReadOnlyFrame().PackedData.Data;
        var avx2Data = avx2Buffer.AsReadOnlyFrame().PackedData.Data;

        TestContext.Out.WriteLine($"Buffer sizes: Scalar={scalarData.Length}, SSE2={sse2Data.Length}, AVX2={avx2Data.Length}");
        TestContext.Out.WriteLine($"RowBytes: {info.Width * 4}, Height: {info.Height}");

        // Диагностика различий
        if (!sse2Data.SequenceEqual(scalarData))
        {
            var diffIdx = FindFirstDifference(scalarData, sse2Data);
            var row = diffIdx / (info.Width * 4);
            var col = diffIdx % (info.Width * 4) / 4;
            var channel = diffIdx % 4;
            TestContext.Out.WriteLine($"SSE2 различие на позиции {diffIdx} (строка {row}, пиксель {col}, канал {channel}): Scalar={scalarData[diffIdx]}, SSE2={sse2Data[diffIdx]}");
        }

        if (!avx2Data.SequenceEqual(scalarData))
        {
            var diffIdx = FindFirstDifference(scalarData, avx2Data);
            var row = diffIdx / (info.Width * 4);
            var col = diffIdx % (info.Width * 4) / 4;
            var channel = diffIdx % 4;
            TestContext.Out.WriteLine($"AVX2 различие на позиции {diffIdx} (строка {row}, пиксель {col}, канал {channel}): Scalar={scalarData[diffIdx]}, AVX2={avx2Data[diffIdx]}");
        }

        Assert.That(sse2Data.SequenceEqual(scalarData), Is.True, "SSE2 результат должен совпадать со Scalar");
        Assert.That(avx2Data.SequenceEqual(scalarData), Is.True, "AVX2 результат должен совпадать со Scalar");

        TestContext.Out.WriteLine($"Все ускорители дают идентичный результат для {info.Width}x{info.Height}");
    }

    private static int FindFirstDifference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return len;
    }

    [TestCase(HardwareAcceleration.None, TestName = "PngCodec: производительность Scalar")]
    [TestCase(HardwareAcceleration.Sse2, TestName = "PngCodec: производительность SSE2")]
    [TestCase(HardwareAcceleration.Avx2, TestName = "PngCodec: производительность AVX2")]
    public void MeasureDecodePerformanceByAccelerator(HardwareAcceleration acceleration)
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        using var acceleratedCodec = new PngCodec { Acceleration = acceleration };
        acceleratedCodec.GetInfo(testPngData, out var info);
        acceleratedCodec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        var frame = frameBuffer.AsFrame();

        const int warmupIterations = 3;
        const int iterations = 50;

        // Warmup
        for (var i = 0; i < warmupIterations; i++)
        {
            acceleratedCodec.Decode(testPngData, ref frame);
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            acceleratedCodec.Decode(testPngData, ref frame);
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        var fps = 1000.0 / avgMs;
        var mpixPerSec = info.Width * info.Height * fps / 1_000_000.0;

        TestContext.Out.WriteLine($"{acceleration}: {avgMs:F3} мс/кадр, {fps:F1} FPS, {mpixPerSec:F1} Mpix/s");
    }

    [TestCase(TestName = "PngCodec: сравнительный бенчмарк ускорителей")]
    public void CompareAcceleratorPerformance()
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        var accelerations = new[]
        {
            HardwareAcceleration.None,
            HardwareAcceleration.Sse2,
            HardwareAcceleration.Avx2,
        };

        using var referenceCodec = new PngCodec();
        referenceCodec.GetInfo(testPngData, out var info);

        const int warmupIterations = 3;
        const int iterations = 30;

        var results = new Dictionary<HardwareAcceleration, double>();

        foreach (var acceleration in accelerations)
        {
            using var acceleratedCodec = new PngCodec { Acceleration = acceleration };
            acceleratedCodec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

            using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
            var frame = frameBuffer.AsFrame();

            // Warmup
            for (var i = 0; i < warmupIterations; i++)
            {
                acceleratedCodec.Decode(testPngData, ref frame);
            }

            // Measure
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                acceleratedCodec.Decode(testPngData, ref frame);
            }
            sw.Stop();

            results[acceleration] = sw.Elapsed.TotalMilliseconds / iterations;
        }

        // Output results
        TestContext.Out.WriteLine($"Сравнение ускорителей для {info.Width}x{info.Height}:");
        var baselineMs = results[HardwareAcceleration.None];
        foreach (var (accel, ms) in results)
        {
            var speedup = baselineMs / ms;
            TestContext.Out.WriteLine($"  {accel,-10}: {ms:F3} мс ({speedup:F2}x от Scalar)");
        }

        // Assert SSE2 and AVX2 are not slower than Scalar (with 10% margin for small images)
        Assert.That(results[HardwareAcceleration.Sse2], Is.LessThanOrEqualTo(baselineMs * 1.1),
            "SSE2 не должен быть значительно медленнее Scalar");
        Assert.That(results[HardwareAcceleration.Avx2], Is.LessThanOrEqualTo(baselineMs * 1.1),
            "AVX2 не должен быть значительно медленнее Scalar");
    }

    #endregion

    #region Performance Tests

    [TestCase(TestName = "PngCodec: производительность декодирования")]
    public void DecodePerformanceMeasureTime()
    {
        // Arrange
        Skip.If(testPngData is null, "Тестовый файл не загружен");

        codec!.GetInfo(testPngData, out var info);
        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        var frame = frameBuffer.AsFrame();

        const int warmupIterations = 5;
        const int iterations = 50;

        // Warmup
        for (var i = 0; i < warmupIterations; i++)
        {
            codec.Decode(testPngData, ref frame);
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            codec.Decode(testPngData, ref frame);
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        var fps = 1000.0 / avgMs;
        var mpixPerSec = info.Width * info.Height * fps / 1_000_000.0;

        TestContext.Out.WriteLine($"PNG Decode: {avgMs:F3} мс/кадр, {fps:F1} FPS, {mpixPerSec:F1} Mpix/s");
        TestContext.Out.WriteLine($"  Размер: {info.Width}x{info.Height}, входные данные: {testPngData!.Length} байт");
    }

    [TestCase(TestName = "PngCodec: производительность кодирования")]
    public void EncodePerformanceMeasureTime()
    {
        // Arrange
        const int width = 512;
        const int height = 512;

        codec!.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var output = new byte[outputSize];

        const int warmupIterations = 3;
        const int iterations = 20;

        // Warmup
        for (var i = 0; i < warmupIterations; i++)
        {
            codec.Encode(roFrame, output, out _);
        }

        // Measure
        var sw = Stopwatch.StartNew();
        var totalBytes = 0;
        for (var i = 0; i < iterations; i++)
        {
            codec.Encode(roFrame, output, out var written);
            totalBytes += written;
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        var fps = 1000.0 / avgMs;
        var mpixPerSec = width * height * fps / 1_000_000.0;
        var avgBytes = totalBytes / iterations;
        var compressionRatio = (double)(width * height * 4) / avgBytes;

        TestContext.Out.WriteLine($"PNG Encode: {avgMs:F3} мс/кадр, {fps:F1} FPS, {mpixPerSec:F1} Mpix/s");
        TestContext.Out.WriteLine($"  Размер: {width}x{height}, выход: {avgBytes} байт, сжатие: {compressionRatio:F2}x");
    }

    [TestCase(TestName = "PngCodec: сравнение уровней сжатия")]
    public void EncodeCompressionLevelComparison()
    {
        // Arrange
        const int width = 640;
        const int height = 480;
        const int iterations = 10;

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = (width * height * 4) + 1024;
        var output = new byte[outputSize];

        TestContext.Out.WriteLine($"PNG Encode 640x480 - сравнение уровней сжатия:");
        TestContext.Out.WriteLine($"{"Уровень",-10} {"мс/кадр",-12} {"FPS",-10} {"Размер",-12} {"Сжатие",-10}");
        TestContext.Out.WriteLine(new string('-', 54));

        int[] levels = [1, 4, 6, 9];

        foreach (var level in levels)
        {
            using var testCodec = new PngCodec();
            testCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
            {
                CompressionLevel = level,
                FastFiltering = level <= 3
            });

            // Warmup
            for (var i = 0; i < 2; i++)
                testCodec.Encode(roFrame, output, out _);

            // Measure
            var sw = Stopwatch.StartNew();
            var totalBytes = 0;
            for (var i = 0; i < iterations; i++)
            {
                testCodec.Encode(roFrame, output, out var written);
                totalBytes += written;
            }
            sw.Stop();

            var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            var fps = 1000.0 / avgMs;
            var avgBytes = totalBytes / iterations;
            var ratio = (double)(width * height * 4) / avgBytes;

            TestContext.Out.WriteLine($"{level,-10} {avgMs,-12:F3} {fps,-10:F1} {avgBytes,-12} {ratio,-10:F2}x");
        }
    }

    [TestCase(TestName = "PngCodec: real-time round-trip 480p >= 240 FPS")]
    public void RealTimeRoundTrip480p()
    {
        // Arrange: 640x480 (480p), real-time настройки
        const int width = 640;
        const int height = 480;
        const int requiredFps = 240; // Целевое требование для PNG Store mode
        const int iterations = 50;

        using var encodeCodec = new PngCodec();
        encodeCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0,      // Store mode — максимальная скорость
            FastFiltering = true       // SIMD Sub-filter only
        });

        using var decodeCodec = new PngCodec();

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        // Store mode требует больший буфер: данные + блочные заголовки (5 байт на строку)
        var outputSize = encodeCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        // Warmup
        for (var i = 0; i < 5; i++)
        {
            var result = encodeCodec.Encode(roFrame, encoded, out var written);
            Assert.That(result, Is.EqualTo(CodecResult.Success), $"Encode failed: {result}");
            Assert.That(written, Is.GreaterThan(0), "Encoded size must be > 0");
            decodeCodec.GetInfo(encoded.AsSpan(0, written), out var info);
            decodeCodec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));
            decodeCodec.Decode(encoded.AsSpan(0, written), ref decodedFrame);
        }

        // Measure round-trip (encode + decode)
        var swEncode = Stopwatch.StartNew();
        var totalBytes = 0;
        var lastWritten = 0;
        for (var i = 0; i < iterations; i++)
        {
            encodeCodec.Encode(roFrame, encoded, out lastWritten);
            totalBytes += lastWritten;
        }
        swEncode.Stop();

        var swDecode = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            decodeCodec.Decode(encoded.AsSpan(0, lastWritten), ref decodedFrame);
        }
        swDecode.Stop();

        var encodeMs = swEncode.Elapsed.TotalMilliseconds / iterations;
        var decodeMs = swDecode.Elapsed.TotalMilliseconds / iterations;
        var avgMs = encodeMs + decodeMs;
        var fps = 1000.0 / avgMs;
        var avgBytes = totalBytes / iterations;
        var ratio = avgBytes > 0 ? (double)(width * height * 4) / avgBytes : 0;

        TestContext.Out.WriteLine($"PNG Round-Trip 480p (real-time mode):");
        TestContext.Out.WriteLine($"  Encode: {encodeMs:F3} мс, Decode: {decodeMs:F3} мс");
        TestContext.Out.WriteLine($"  {avgMs:F3} мс/кадр, {fps:F1} FPS, сжатие: {ratio:F2}x");
        TestContext.Out.WriteLine($"  Encode size: {avgBytes} байт");

        // Assert: должен быть >= 240 FPS
        Assert.That(fps, Is.GreaterThanOrEqualTo(requiredFps),
            $"PNG round-trip должен быть >= {requiredFps} FPS для real-time. Текущий: {fps:F1} FPS. Требуется оптимизация!");
    }

    #endregion

    #region Benchmark Methods

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void BenchmarkDecode()
    {
        if (testPngData is null || codec is null) return;

        codec.GetInfo(testPngData, out var info);
        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));

        using var frameBuffer = new VideoFrameBuffer(info.Width, info.Height, info.PixelFormat);
        var frame = frameBuffer.AsFrame();
        codec.Decode(testPngData, ref frame);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void BenchmarkEncode()
    {
        if (codec is null) return;

        const int width = 256;
        const int height = 256;

        codec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var output = new byte[codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        codec.Encode(roFrame, output, out _);
    }

    [TestCase(TestName = "PngCodec: FPS по разрешениям от 480p до 8K")]
    public void MultiResolutionFpsBenchmark()
    {
        // Разрешения: 480p → 720p → 1080p → 1440p → 4K → 8K
        var resolutions = new (int Width, int Height, string Name)[]
        {
            (640, 480, "480p"),
            (1280, 720, "720p"),
            (1920, 1080, "1080p"),
            (2560, 1440, "1440p"),
            (3840, 2160, "4K"),
            (7680, 4320, "8K"),
        };

        TestContext.Out.WriteLine("┌───────────┬────────────────┬────────────────┬───────────────┬────────────────┬────────────────┐");
        TestContext.Out.WriteLine("│ Разреш.   │     Пиксели    │   Encode (мс)  │  Decode (мс)  │ Round-trip FPS │   Пропускная   │");
        TestContext.Out.WriteLine("├───────────┼────────────────┼────────────────┼───────────────┼────────────────┼────────────────┤");

        foreach (var (width, height, name) in resolutions)
        {
            var pixels = width * height;
            var rawBytes = pixels * 4;
            const int iterations = 20;

            // Создаём кодеки
            using var encodeCodec = new PngCodec();
            encodeCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
            {
                CompressionLevel = 0,   // Store mode
                FastFiltering = true
            });

            using var decodeCodec = new PngCodec();

            // Буферы
            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            FillTestPattern(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = encodeCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[outputSize];

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            // Encode warmup
            encodeCodec.Encode(roFrame, encoded, out var bytesWritten);

            // Decode warmup
            decodeCodec.GetInfo(encoded.AsSpan(0, bytesWritten), out var info);
            decodeCodec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, info.PixelFormat));
            decodeCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

            // Measure Encode
            var swEncode = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                encodeCodec.Encode(roFrame, encoded, out _);
            }
            swEncode.Stop();
            var encodeMs = swEncode.Elapsed.TotalMilliseconds / iterations;

            // Measure Decode
            var swDecode = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                decodeCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
            }
            swDecode.Stop();
            var decodeMs = swDecode.Elapsed.TotalMilliseconds / iterations;

            // Рассчитываем FPS и пропускную способность
            var totalMs = encodeMs + decodeMs;
            var fps = 1000.0 / totalMs;
            var throughputMBps = rawBytes * fps / (1024.0 * 1024.0);

            TestContext.Out.WriteLine(
                $"│ {name,-9} │ {pixels,14:N0} │ {encodeMs,14:F2} │ {decodeMs,13:F2} │ {fps,14:F1} │ {throughputMBps,12:F1} MB/s│");
        }

        TestContext.Out.WriteLine("└───────────┴────────────────┴────────────────┴───────────────┴────────────────┴────────────────┘");
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("Примечание: Store mode (Level=0), SIMD ADLER32, sliced-by-8 CRC32");
    }

    #endregion

    #region Helper Methods

    private static void FillTestPattern(VideoFrameBuffer buffer)
    {
        var frame = buffer.AsFrame();
        var plane = frame.PackedData;
        var width = frame.Width;
        var height = frame.Height;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var offset = x * 4;
                row[offset] = (byte)(x * 255 / width);     // R
                row[offset + 1] = (byte)(y * 255 / height); // G
                row[offset + 2] = (byte)((x + y) * 127 / (width + height)); // B
                row[offset + 3] = 255; // A
            }
        }
    }

    private static void FillTestPatternRgb24(VideoFrameBuffer buffer)
    {
        var frame = buffer.AsFrame();
        var plane = frame.PackedData;
        var width = frame.Width;
        var height = frame.Height;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var offset = x * 3;
                row[offset] = (byte)(x * 255 / width);     // R
                row[offset + 1] = (byte)(y * 255 / height); // G
                row[offset + 2] = (byte)((x + y) * 127 / (width + height)); // B
            }
        }
    }

    #endregion
}

/// <summary>
/// Хелпер для пропуска тестов.
/// </summary>
internal static class Skip
{
    public static void If(bool condition, string message)
    {
        if (condition)
        {
            Assert.Ignore(message);
        }
    }
}
