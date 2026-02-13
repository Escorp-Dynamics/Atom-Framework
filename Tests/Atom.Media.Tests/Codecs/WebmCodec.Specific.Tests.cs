#pragma warning disable CA1861, S125

using System.Diagnostics;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты WebM видеокодека.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class WebmCodecTests(ILogger logger) : BenchmarkTests<WebmCodecTests>(logger)
{
    #region Constants

    private const int TestWidth = 64;
    private const int TestHeight = 64;

    #endregion

    #region Setup

    private WebmCodec? codec;

    public WebmCodecTests() : this(ConsoleLogger.Unicode) { }

    [SetUp]
    public void SetUp() => codec = new WebmCodec();

    [TearDown]
    public void TearDown()
    {
        codec?.Dispose();
        codec = null;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "WebmCodec: создание экземпляра")]
    public void CanCreateInstance()
    {
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec!.CodecId, Is.EqualTo(MediaCodecId.Vp9));
        Assert.That(codec.Name, Does.Contain("WebM"));
        Assert.That(codec.MimeType, Is.EqualTo("video/webm"));
    }

    [TestCase(TestName = "WebmCodec: инициализация энкодера RGBA32")]
    public void InitializeEncoderRgba32Success()
    {
        var parameters = new VideoCodecParameters
        {
            Width = TestWidth,
            Height = TestHeight,
            PixelFormat = VideoPixelFormat.Rgba32
        };

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "WebmCodec: инициализация энкодера YUV420P")]
    public void InitializeEncoderYuv420PSuccess()
    {
        var parameters = new VideoCodecParameters
        {
            Width = TestWidth,
            Height = TestHeight,
            PixelFormat = VideoPixelFormat.Yuv420P
        };

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "WebmCodec: инициализация декодера")]
    public void InitializeDecoderSuccess()
    {
        var parameters = new VideoCodecParameters
        {
            Width = TestWidth,
            Height = TestHeight,
            PixelFormat = VideoPixelFormat.Rgba32
        };

        var result = codec!.InitializeDecoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "WebmCodec: инициализация с нулевыми размерами")]
    public void InitializeEncoderZeroDimensionsReturnsInvalidData()
    {
        var parameters = new VideoCodecParameters
        {
            Width = 0,
            Height = 0,
            PixelFormat = VideoPixelFormat.Rgba32
        };

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebmCodec: инициализация с превышением лимита 16384")]
    public void InitializeEncoderExceedsSizeLimitReturnsInvalidData()
    {
        var parameters = new VideoCodecParameters
        {
            Width = 16385,
            Height = 16385,
            PixelFormat = VideoPixelFormat.Rgba32
        };

        var result = codec!.InitializeEncoder(parameters);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    #endregion

    #region Encode Tests

    [TestCase(TestName = "WebmCodec: кодирование RGBA32 кадра")]
    public void EncodeRgba32FrameSuccess()
    {
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано RGBA32: {bytesWritten} байт");
    }

    [TestCase(TestName = "WebmCodec: кодирование YUV420P кадра")]
    public void EncodeYuv420PFrameSuccess()
    {
        const int width = 64;
        const int height = 64;

        codec!.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        FillTestPatternYuv420P(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано YUV420P: {bytesWritten} байт");
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "WebmCodec: round-trip RGBA32")]
    public void RoundTripRgba32DataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer);

        // Encode
        var encoder = new WebmCodec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new WebmCodec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        // Compare
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var decodedData = decodedBuffer.AsReadOnlyFrame().PackedData.Data;

        Assert.That(decodedData.SequenceEqual(originalData), Is.True,
            "Декодированные данные должны совпадать с оригинальными");

        encoder.Dispose();
        decoder.Dispose();
    }

    [TestCase(TestName = "WebmCodec: round-trip YUV420P")]
    public void RoundTripYuv420PDataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        FillTestPatternYuv420P(originalBuffer);

        // Encode
        var encoder = new WebmCodec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new WebmCodec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        // Compare all planes by row (accounting for potential stride differences)
        var originalYPlane = originalBuffer.AsReadOnlyFrame().GetPlaneY();
        var decodedYPlane = decodedBuffer.AsReadOnlyFrame().GetPlaneY();
        for (var y = 0; y < originalYPlane.Height; y++)
        {
            var originalRow = originalYPlane.GetRow(y);
            var decodedRow = decodedYPlane.GetRow(y);
            Assert.That(decodedRow.SequenceEqual(originalRow), Is.True, $"Y plane row {y} should match");
        }

        var originalUPlane = originalBuffer.AsReadOnlyFrame().GetPlaneU();
        var decodedUPlane = decodedBuffer.AsReadOnlyFrame().GetPlaneU();
        for (var y = 0; y < originalUPlane.Height; y++)
        {
            var originalRow = originalUPlane.GetRow(y);
            var decodedRow = decodedUPlane.GetRow(y);
            Assert.That(decodedRow.SequenceEqual(originalRow), Is.True, $"U plane row {y} should match");
        }

        var originalVPlane = originalBuffer.AsReadOnlyFrame().GetPlaneV();
        var decodedVPlane = decodedBuffer.AsReadOnlyFrame().GetPlaneV();
        for (var y = 0; y < originalVPlane.Height; y++)
        {
            var originalRow = originalVPlane.GetRow(y);
            var decodedRow = decodedVPlane.GetRow(y);
            Assert.That(decodedRow.SequenceEqual(originalRow), Is.True, $"V plane row {y} should match");
        }

        encoder.Dispose();
        decoder.Dispose();
    }

    #endregion

    #region Performance Tests

    [TestCase(TestName = "WebmCodec: real-time round-trip 480p >= 240 FPS")]
    public void RealTimeRoundTrip480p()
    {
        const int width = 640;
        const int height = 480;
        const int requiredFps = 240;
        const int iterations = 50;

        using var encodeCodec = new WebmCodec();
        encodeCodec.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodeCodec = new WebmCodec();

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        // Warmup
        encodeCodec.Encode(roFrame, encoded, out var bytesWritten);
        decodeCodec.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });
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

        TestContext.Out.WriteLine($"WebM 480p round-trip: {avgMs:F3} мс/кадр, {fps:F1} FPS");
        TestContext.Out.WriteLine($"  Размер: {bytesWritten} байт");

        Assert.That(fps, Is.GreaterThanOrEqualTo(requiredFps),
            $"FPS ({fps:F1}) должен быть >= {requiredFps}");
    }

    [TestCase(TestName = "WebmCodec: FPS по разрешениям от 480p до 8K")]
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
            var rawBytes = pixels * 4; // RGBA32
            const int iterations = 20;

            using var encodeCodec = new WebmCodec();
            encodeCodec.InitializeEncoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Rgba32
            });

            using var decodeCodec = new WebmCodec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            FillTestPatternRgba32(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[outputSize];

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            // Warmup
            encodeCodec.Encode(roFrame, encoded, out var bytesWritten);
            decodeCodec.InitializeDecoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Rgba32
            });
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
        TestContext.Out.WriteLine("Примечание: Store mode (AFRM chunk), SIMD копирование");
    }

    [TestCase(TestName = "WebmCodec: YUV420P multi-resolution benchmark")]
    public void Yuv420PMultiResolutionBenchmark()
    {
        var resolutions = new (int Width, int Height, string Name)[]
        {
            (640, 480, "480p"),
            (1280, 720, "720p"),
            (1920, 1080, "1080p"),
            (3840, 2160, "4K"),
        };

        TestContext.Out.WriteLine("YUV420P Performance:");
        TestContext.Out.WriteLine("┌───────────┬────────────────┬────────────────┬───────────────┬────────────────┐");
        TestContext.Out.WriteLine("│ Разреш.   │   Encode (мс)  │  Decode (мс)   │ Round-trip FPS│   Пропускная   │");
        TestContext.Out.WriteLine("├───────────┼────────────────┼────────────────┼───────────────┼────────────────┤");

        foreach (var (width, height, name) in resolutions)
        {
            var ySize = width * height;
            var uvSize = (width / 2) * (height / 2) * 2;
            var rawBytes = ySize + uvSize; // YUV420P
            const int iterations = 20;

            using var encodeCodec = new WebmCodec();
            encodeCodec.InitializeEncoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Yuv420P
            });

            using var decodeCodec = new WebmCodec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
            FillTestPatternYuv420P(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
            var encoded = new byte[outputSize];

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
            var decodedFrame = decodedBuffer.AsFrame();

            // Warmup
            encodeCodec.Encode(roFrame, encoded, out var bytesWritten);
            decodeCodec.InitializeDecoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Yuv420P
            });
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
                $"│ {name,-9} │ {encodeMs,14:F2} │ {decodeMs,14:F2} │ {fps,13:F1} │ {throughputMBps,12:F1} MB/s│");
        }

        TestContext.Out.WriteLine("└───────────┴────────────────┴────────────────┴───────────────┴────────────────┘");
    }

    #endregion

    #region Multi-Frame Tests

    [TestCase(TestName = "WebmCodec: последовательность из 100 кадров")]
    public void EncodeDecodeSequence100Frames()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 100;

        using var encoder = new WebmCodec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decoder = new WebmCodec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);

        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        var sw = Stopwatch.StartNew();

        for (var frameIdx = 0; frameIdx < frameCount; frameIdx++)
        {
            // Заполняем уникальным паттерном для каждого кадра
            FillTestPatternRgba32WithSeed(frameBuffer, frameIdx);

            var roFrame = frameBuffer.AsReadOnlyFrame();
            var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
            Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

            var decodedFrame = decodedBuffer.AsFrame();
            var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
            Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

            // Проверяем данные
            var original = frameBuffer.AsReadOnlyFrame().PackedData.Data;
            var decoded = decodedBuffer.AsReadOnlyFrame().PackedData.Data;
            Assert.That(decoded.SequenceEqual(original), Is.True,
                $"Frame {frameIdx} data mismatch");
        }

        sw.Stop();
        var fps = frameCount * 1000.0 / sw.Elapsed.TotalMilliseconds;

        TestContext.Out.WriteLine($"100 кадров 320x240: {sw.Elapsed.TotalMilliseconds:F1} мс, {fps:F1} FPS");
    }

    #endregion

    #region WebM ↔ Raw Conversion Tests

    [TestCase(TestName = "WebmCodec: WebM → WebM реконвертация")]
    public void WebmToWebmReconversion()
    {
        const int width = 128;
        const int height = 128;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer);

        // First encode
        using var encoder1 = new WebmCodec();
        encoder1.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var output1 = new byte[WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        encoder1.Encode(originalBuffer.AsReadOnlyFrame(), output1, out var size1);

        // Decode
        using var decoder1 = new WebmCodec();
        decoder1.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodedBuffer1 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame1 = decodedBuffer1.AsFrame();
        decoder1.Decode(output1.AsSpan(0, size1), ref frame1);

        // Second encode
        using var encoder2 = new WebmCodec();
        encoder2.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var output2 = new byte[WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        encoder2.Encode(decodedBuffer1.AsReadOnlyFrame(), output2, out var size2);

        // Decode again
        using var decoder2 = new WebmCodec();
        decoder2.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodedBuffer2 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame2 = decodedBuffer2.AsFrame();
        decoder2.Decode(output2.AsSpan(0, size2), ref frame2);

        // Compare
        var original = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var final = decodedBuffer2.AsReadOnlyFrame().PackedData.Data;

        Assert.That(final.SequenceEqual(original), Is.True);
        Assert.That(size2, Is.EqualTo(size1));

        TestContext.Out.WriteLine($"WebM → WebM: {size1} байт → {size2} байт (идентично)");
    }

    #endregion

    #region Helper Methods

    private static void FillTestPatternRgba32(VideoFrameBuffer buffer)
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

    private static void FillTestPatternRgba32WithSeed(VideoFrameBuffer buffer, int seed)
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
                row[offset] = (byte)((x + seed) % 256);
                row[offset + 1] = (byte)((y + seed) % 256);
                row[offset + 2] = (byte)((x + y + seed) % 256);
                row[offset + 3] = 255;
            }
        }
    }

    private static void FillTestPatternYuv420P(VideoFrameBuffer buffer)
    {
        var frame = buffer.AsFrame();
        var width = frame.Width;
        var height = frame.Height;

        // Y plane
        var yPlane = frame.GetPlaneY();
        for (var y = 0; y < height; y++)
        {
            var row = yPlane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                row[x] = (byte)((x + y) * 255 / (width + height));
            }
        }

        // U plane (Cb)
        var uPlane = frame.GetPlaneU();
        var uvWidth = width / 2;
        var uvHeight = height / 2;
        for (var y = 0; y < uvHeight; y++)
        {
            var row = uPlane.GetRow(y);
            for (var x = 0; x < uvWidth; x++)
            {
                row[x] = (byte)(128 + (x - uvWidth / 2) * 50 / uvWidth);
            }
        }

        // V plane (Cr)
        var vPlane = frame.GetPlaneV();
        for (var y = 0; y < uvHeight; y++)
        {
            var row = vPlane.GetRow(y);
            for (var x = 0; x < uvWidth; x++)
            {
                row[x] = (byte)(128 + (y - uvHeight / 2) * 50 / uvHeight);
            }
        }
    }

    #endregion
}
