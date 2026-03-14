#pragma warning disable CA1861, S125

using System.Diagnostics;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты WebP кодека.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class WebpCodecTests(ILogger logger) : BenchmarkTests<WebpCodecTests>(logger)
{
    #region Constants

    private const int TestWidth = 64;
    private const int TestHeight = 64;

    #endregion

    #region Setup

    private WebpCodec? codec;

    public WebpCodecTests() : this(ConsoleLogger.Unicode) { }

    [SetUp]
    public void SetUp() => codec = new WebpCodec();

    [TearDown]
    public void TearDown()
    {
        codec?.Dispose();
        codec = null;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "WebpCodec: создание экземпляра")]
    public void CanCreateInstance()
    {
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec!.CodecId, Is.EqualTo(MediaCodecId.WebP));
        Assert.That(codec.Name, Does.Contain("WebP"));
        Assert.That(codec.MimeType, Is.EqualTo("image/webp"));
    }

    [TestCase(TestName = "WebpCodec: инициализация энкодера")]
    public void InitializeEncoderSuccess()
    {
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Rgba32);

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "WebpCodec: инициализация декодера")]
    public void InitializeDecoderSuccess()
    {
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Rgba32);

        var result = codec!.InitializeDecoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "WebpCodec: инициализация энкодера с нулевыми размерами")]
    public void InitializeEncoderZeroDimensionsReturnsInvalidData()
    {
        var parameters = new ImageCodecParameters(0, 0, VideoPixelFormat.Rgba32);

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: инициализация энкодера с неподдерживаемым форматом")]
    public void InitializeEncoderUnsupportedFormatReturnsUnsupportedFormat()
    {
        var parameters = new ImageCodecParameters(TestWidth, TestHeight, VideoPixelFormat.Yuv420P);

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
    }

    [TestCase(TestName = "WebpCodec: инициализация энкодера с превышением лимита 16383")]
    public void InitializeEncoderExceedsSizeLimitReturnsInvalidData()
    {
        var parameters = new ImageCodecParameters(16384, 16384, VideoPixelFormat.Rgba32);

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    #endregion

    #region Header Detection Tests

    [TestCase(TestName = "WebpCodec: определение WebP по сигнатуре RIFF/WEBP")]
    public void CanDecodeValidWebpSignatureReturnsTrue()
    {
        // RIFF + size + WEBP
        ReadOnlySpan<byte> validHeader = [
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x00, 0x00, 0x00, 0x00, // size (ignored)
            0x57, 0x45, 0x42, 0x50  // WEBP
        ];

        var result = codec!.CanDecode(validHeader);

        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "WebpCodec: отклонение невалидной сигнатуры")]
    public void CanDecodeInvalidSignatureReturnsFalse()
    {
        ReadOnlySpan<byte> invalidHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]; // PNG

        var result = codec!.CanDecode(invalidHeader);

        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "WebpCodec: отклонение слишком короткого заголовка")]
    public void CanDecodeTooShortHeaderReturnsFalse()
    {
        ReadOnlySpan<byte> shortHeader = [0x52, 0x49, 0x46, 0x46];

        var result = codec!.CanDecode(shortHeader);

        Assert.That(result, Is.False);
    }

    #endregion

    #region Encode Tests

    [TestCase(TestName = "WebpCodec: кодирование RGBA32 изображения")]
    public void EncodeRgba32ImageSuccess()
    {
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        Assert.That(output[..4], Is.EqualTo("RIFF"u8.ToArray())); // RIFF
        Assert.That(output[8..12], Is.EqualTo("WEBP"u8.ToArray())); // WEBP
        TestContext.Out.WriteLine($"Закодировано: {bytesWritten} байт");
    }

    [TestCase(TestName = "WebpCodec: кодирование RGB24 изображения")]
    public void EncodeRgb24ImageSuccess()
    {
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgb24));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgb24);
        FillTestPatternRgb24(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgb24);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано RGB24: {bytesWritten} байт");
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "WebpCodec: round-trip кодирование/декодирование")]
    public void RoundTripEncodeAndDecodeDataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);

        // Encode
        var encoder = new WebpCodec();
        encoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        var outputSize = encoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new WebpCodec();
        decoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

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

    [TestCase(TestName = "WebpCodec: round-trip RGB24")]
    public void RoundTripRgb24DataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgb24);
        FillTestPatternRgb24(originalBuffer);

        // Encode
        var encoder = new WebpCodec();
        encoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgb24));

        var outputSize = encoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgb24);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new WebpCodec();
        decoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgb24));

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgb24);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        // Compare
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var decodedData = decodedBuffer.AsReadOnlyFrame().PackedData.Data;

        Assert.That(decodedData.SequenceEqual(originalData), Is.True);

        encoder.Dispose();
        decoder.Dispose();
    }

    #endregion

    #region Performance Tests

    [TestCase(TestName = "WebpCodec: real-time round-trip 480p >= 240 FPS")]
    public void RealTimeRoundTrip480p()
    {
        const int width = 640;
        const int height = 480;
        const int requiredFps = 240;
        const int iterations = 50;

        using var encodeCodec = new WebpCodec();
        encodeCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0,
            FastFiltering = true
        });

        using var decodeCodec = new WebpCodec();

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = encodeCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        // Warmup
        encodeCodec.Encode(roFrame, encoded, out var bytesWritten);
        decodeCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
        decodeCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

        // Measure
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            encodeCodec.Encode(roFrame, encoded, out bytesWritten);
            decodeCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        }
        sw.Stop();

        var totalMs = sw.Elapsed.TotalMilliseconds;
        var avgMs = totalMs / iterations;
        var fps = 1000.0 / avgMs;

        TestContext.Out.WriteLine($"WebP 480p round-trip: {avgMs:F3} мс/кадр, {fps:F1} FPS");
        TestContext.Out.WriteLine($"  Размер: {bytesWritten} байт");

        Assert.That(fps, Is.GreaterThanOrEqualTo(requiredFps),
            $"FPS ({fps:F1}) должен быть >= {requiredFps}");
    }

    [TestCase(TestName = "WebpCodec: FPS по разрешениям от 480p до 8K")]
    public void MultiResolutionFpsBenchmark()
    {
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

            using var encodeCodec = new WebpCodec();
            encodeCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
            {
                CompressionLevel = 0,
                FastFiltering = true
            });

            using var decodeCodec = new WebpCodec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            FillTestPattern(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = encodeCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[outputSize];

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            // Warmup
            encodeCodec.Encode(roFrame, encoded, out var bytesWritten);
            decodeCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
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

            var totalMs = encodeMs + decodeMs;
            var fps = 1000.0 / totalMs;
            var throughputMBps = rawBytes * fps / (1024.0 * 1024.0);

            TestContext.Out.WriteLine(
                $"│ {name,-9} │ {pixels,14:N0} │ {encodeMs,14:F2} │ {decodeMs,13:F2} │ {fps,14:F1} │ {throughputMBps,12:F1} MB/s│");
        }

        TestContext.Out.WriteLine("└───────────┴────────────────┴────────────────┴───────────────┴────────────────┴────────────────┘");
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("Примечание: Store mode (ARAW chunk), SIMD копирование");
    }

    [TestCase(TestName = "WebpCodec: сравнение с PNG")]
    public void CompareWithPng()
    {
        const int width = 640;
        const int height = 480;
        const int iterations = 30;

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        // WebP
        using var webpCodec = new WebpCodec();
        webpCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0,
            FastFiltering = true
        });

        var webpOutput = new byte[webpCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];

        // Warmup
        webpCodec.Encode(roFrame, webpOutput, out var webpSize);

        var swWebp = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            webpCodec.Encode(roFrame, webpOutput, out _);
        }
        swWebp.Stop();
        var webpEncodeMs = swWebp.Elapsed.TotalMilliseconds / iterations;

        // PNG
        using var pngCodec = new PngCodec();
        pngCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0,
            FastFiltering = true
        });

        var pngOutput = new byte[pngCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];

        // Warmup
        pngCodec.Encode(roFrame, pngOutput, out var pngSize);

        var swPng = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            pngCodec.Encode(roFrame, pngOutput, out _);
        }
        swPng.Stop();
        var pngEncodeMs = swPng.Elapsed.TotalMilliseconds / iterations;

        TestContext.Out.WriteLine($"Сравнение WebP vs PNG на 480p:");
        TestContext.Out.WriteLine($"  WebP: {webpEncodeMs:F3} мс, {webpSize} байт");
        TestContext.Out.WriteLine($"  PNG:  {pngEncodeMs:F3} мс, {pngSize} байт");
        TestContext.Out.WriteLine($"  WebP/PNG ratio: {webpEncodeMs / pngEncodeMs:F2}x");

        Assert.That(webpEncodeMs, Is.LessThanOrEqualTo(pngEncodeMs * 1.1),
            "WebP не должен быть значительно медленнее PNG");
    }

    #endregion

    #region Cross-Codec Conversion Tests

    [TestCase(TestName = "WebpCodec: PNG → WebP конвертация")]
    public void PngToWebpConversion()
    {
        const int width = 256;
        const int height = 256;

        // Создаём исходное изображение
        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);
        var roFrame = originalBuffer.AsReadOnlyFrame();

        // Кодируем в PNG
        using var pngCodec = new PngCodec();
        pngCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var pngOutput = new byte[pngCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var pngResult = pngCodec.Encode(roFrame, pngOutput, out var pngSize);
        Assert.That(pngResult, Is.EqualTo(CodecResult.Success));

        // Декодируем PNG
        using var pngDecoder = new PngCodec();
        pngDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var pngDecodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var pngDecodedFrame = pngDecodedBuffer.AsFrame();
        var pngDecodeResult = pngDecoder.Decode(pngOutput.AsSpan(0, pngSize), ref pngDecodedFrame);
        Assert.That(pngDecodeResult, Is.EqualTo(CodecResult.Success));

        // Кодируем в WebP
        using var webpCodec = new WebpCodec();
        webpCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var webpOutput = new byte[webpCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var webpResult = webpCodec.Encode(pngDecodedBuffer.AsReadOnlyFrame(), webpOutput, out var webpSize);
        Assert.That(webpResult, Is.EqualTo(CodecResult.Success));

        // Декодируем WebP
        using var webpDecoder = new WebpCodec();
        webpDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var webpDecodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var webpDecodedFrame = webpDecodedBuffer.AsFrame();
        var webpDecodeResult = webpDecoder.Decode(webpOutput.AsSpan(0, webpSize), ref webpDecodedFrame);
        Assert.That(webpDecodeResult, Is.EqualTo(CodecResult.Success));

        // Сравниваем данные
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var finalData = webpDecodedBuffer.AsReadOnlyFrame().PackedData.Data;

        Assert.That(finalData.SequenceEqual(originalData), Is.True,
            "Данные после PNG → WebP конвертации должны совпадать с оригиналом");

        TestContext.Out.WriteLine($"PNG → WebP: {pngSize} байт → {webpSize} байт");
    }

    [TestCase(TestName = "WebpCodec: WebP → PNG конвертация")]
    public void WebpToPngConversion()
    {
        const int width = 256;
        const int height = 256;

        // Создаём исходное изображение
        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);
        var roFrame = originalBuffer.AsReadOnlyFrame();

        // Кодируем в WebP
        using var webpCodec = new WebpCodec();
        webpCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var webpOutput = new byte[webpCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var webpResult = webpCodec.Encode(roFrame, webpOutput, out var webpSize);
        Assert.That(webpResult, Is.EqualTo(CodecResult.Success));

        // Декодируем WebP
        using var webpDecoder = new WebpCodec();
        webpDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var webpDecodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var webpDecodedFrame = webpDecodedBuffer.AsFrame();
        var webpDecodeResult = webpDecoder.Decode(webpOutput.AsSpan(0, webpSize), ref webpDecodedFrame);
        Assert.That(webpDecodeResult, Is.EqualTo(CodecResult.Success));

        // Кодируем в PNG
        using var pngCodec = new PngCodec();
        pngCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var pngOutput = new byte[pngCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var pngResult = pngCodec.Encode(webpDecodedBuffer.AsReadOnlyFrame(), pngOutput, out var pngSize);
        Assert.That(pngResult, Is.EqualTo(CodecResult.Success));

        // Декодируем PNG
        using var pngDecoder = new PngCodec();
        pngDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var pngDecodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var pngDecodedFrame = pngDecodedBuffer.AsFrame();
        var pngDecodeResult = pngDecoder.Decode(pngOutput.AsSpan(0, pngSize), ref pngDecodedFrame);
        Assert.That(pngDecodeResult, Is.EqualTo(CodecResult.Success));

        // Сравниваем данные
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var finalData = pngDecodedBuffer.AsReadOnlyFrame().PackedData.Data;

        Assert.That(finalData.SequenceEqual(originalData), Is.True,
            "Данные после WebP → PNG конвертации должны совпадать с оригиналом");

        TestContext.Out.WriteLine($"WebP → PNG: {webpSize} байт → {pngSize} байт");
    }

    [TestCase(TestName = "WebpCodec: WebP → WebP реконвертация")]
    public void WebpToWebpReconversion()
    {
        const int width = 256;
        const int height = 256;

        // Создаём исходное изображение
        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);
        var roFrame = originalBuffer.AsReadOnlyFrame();

        // Кодируем в WebP (первый раз)
        using var webpEncoder1 = new WebpCodec();
        webpEncoder1.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var webpOutput1 = new byte[webpEncoder1.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var encodeResult1 = webpEncoder1.Encode(roFrame, webpOutput1, out var webpSize1);
        Assert.That(encodeResult1, Is.EqualTo(CodecResult.Success));

        // Декодируем WebP (первый раз)
        using var webpDecoder1 = new WebpCodec();
        webpDecoder1.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var decodedBuffer1 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame1 = decodedBuffer1.AsFrame();
        var decodeResult1 = webpDecoder1.Decode(webpOutput1.AsSpan(0, webpSize1), ref decodedFrame1);
        Assert.That(decodeResult1, Is.EqualTo(CodecResult.Success));

        // Кодируем в WebP (второй раз)
        using var webpEncoder2 = new WebpCodec();
        webpEncoder2.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
        {
            CompressionLevel = 0
        });

        var webpOutput2 = new byte[webpEncoder2.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var encodeResult2 = webpEncoder2.Encode(decodedBuffer1.AsReadOnlyFrame(), webpOutput2, out var webpSize2);
        Assert.That(encodeResult2, Is.EqualTo(CodecResult.Success));

        // Декодируем WebP (второй раз)
        using var webpDecoder2 = new WebpCodec();
        webpDecoder2.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var decodedBuffer2 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame2 = decodedBuffer2.AsFrame();
        var decodeResult2 = webpDecoder2.Decode(webpOutput2.AsSpan(0, webpSize2), ref decodedFrame2);
        Assert.That(decodeResult2, Is.EqualTo(CodecResult.Success));

        // Сравниваем данные
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var finalData = decodedBuffer2.AsReadOnlyFrame().PackedData.Data;

        Assert.That(finalData.SequenceEqual(originalData), Is.True,
            "Данные после WebP → WebP реконвертации должны совпадать с оригиналом");

        Assert.That(webpSize2, Is.EqualTo(webpSize1),
            "Размер при реконвертации должен быть идентичен");

        TestContext.Out.WriteLine($"WebP → WebP: {webpSize1} байт → {webpSize2} байт (идентично)");
    }

    [TestCase(TestName = "WebpCodec: PNG ↔ WebP бенчмарк конвертации")]
    public void PngWebpConversionBenchmark()
    {
        const int width = 640;
        const int height = 480;
        const int iterations = 30;

        // Создаём изображение
        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        // Подготовка кодеков
        using var pngEncoder = new PngCodec();
        pngEncoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32) { CompressionLevel = 0 });

        using var pngDecoder = new PngCodec();
        pngDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var webpEncoder = new WebpCodec();
        webpEncoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32) { CompressionLevel = 0 });

        using var webpDecoder = new WebpCodec();

        var pngOutput = new byte[pngEncoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var webpOutput = new byte[webpEncoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];

        using var intermediateBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var intermediateFrame = intermediateBuffer.AsFrame();

        // Warmup
        pngEncoder.Encode(roFrame, pngOutput, out var pngSize);
        pngDecoder.Decode(pngOutput.AsSpan(0, pngSize), ref intermediateFrame);
        webpEncoder.Encode(intermediateBuffer.AsReadOnlyFrame(), webpOutput, out var webpSize);
        webpDecoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
        webpDecoder.Decode(webpOutput.AsSpan(0, webpSize), ref intermediateFrame);

        // PNG → WebP бенчмарк
        var swPngToWebp = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            // Decode PNG → Encode WebP
            pngDecoder.Decode(pngOutput.AsSpan(0, pngSize), ref intermediateFrame);
            webpEncoder.Encode(intermediateBuffer.AsReadOnlyFrame(), webpOutput, out _);
        }
        swPngToWebp.Stop();
        var pngToWebpMs = swPngToWebp.Elapsed.TotalMilliseconds / iterations;

        // WebP → PNG бенчмарк
        var swWebpToPng = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            // Decode WebP → Encode PNG
            webpDecoder.Decode(webpOutput.AsSpan(0, webpSize), ref intermediateFrame);
            pngEncoder.Encode(intermediateBuffer.AsReadOnlyFrame(), pngOutput, out _);
        }
        swWebpToPng.Stop();
        var webpToPngMs = swWebpToPng.Elapsed.TotalMilliseconds / iterations;

        var pngToWebpFps = 1000.0 / pngToWebpMs;
        var webpToPngFps = 1000.0 / webpToPngMs;

        TestContext.Out.WriteLine("┌──────────────────┬──────────────┬──────────────┐");
        TestContext.Out.WriteLine("│ Конвертация      │   Время (мс) │          FPS │");
        TestContext.Out.WriteLine("├──────────────────┼──────────────┼──────────────┤");
        TestContext.Out.WriteLine($"│ PNG → WebP       │ {pngToWebpMs,12:F2} │ {pngToWebpFps,12:F1} │");
        TestContext.Out.WriteLine($"│ WebP → PNG       │ {webpToPngMs,12:F2} │ {webpToPngFps,12:F1} │");
        TestContext.Out.WriteLine("└──────────────────┴──────────────┴──────────────┘");
    }

    [TestCase(TestName = "WebpCodec: множественная WebP → WebP конвертация (10 итераций)")]
    public void MultipleWebpReconversions()
    {
        const int width = 128;
        const int height = 128;
        const int reconversions = 10;

        // Создаём исходное изображение
        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPattern(originalBuffer);

        var currentData = originalBuffer.AsReadOnlyFrame().PackedData.Data.ToArray();

        for (var iteration = 0; iteration < reconversions; iteration++)
        {
            // Создаём буфер с текущими данными
            using var inputBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            currentData.CopyTo(inputBuffer.AsFrame().PackedData.Data);

            // Кодируем
            using var encoder = new WebpCodec();
            encoder.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32)
            {
                CompressionLevel = 0
            });

            var output = new byte[encoder.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
            var encodeResult = encoder.Encode(inputBuffer.AsReadOnlyFrame(), output, out var size);
            Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

            // Декодируем
            using var decoder = new WebpCodec();
            decoder.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

            using var outputBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var outputFrame = outputBuffer.AsFrame();
            var decodeResult = decoder.Decode(output.AsSpan(0, size), ref outputFrame);
            Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

            // Обновляем текущие данные для следующей итерации
            currentData = outputBuffer.AsReadOnlyFrame().PackedData.Data.ToArray();
        }

        // Сравниваем финальные данные с оригиналом
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;

        Assert.That(currentData.AsSpan().SequenceEqual(originalData), Is.True,
            $"Данные после {reconversions} реконвертаций WebP → WebP должны совпадать с оригиналом");

        TestContext.Out.WriteLine($"WebP → WebP x{reconversions}: данные идентичны оригиналу ✓");
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
