#pragma warning disable CA1861, S125

using System.Diagnostics;
using Atom.IO;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты MP4 видеокодека.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class Mp4CodecTests(ILogger logger) : BenchmarkTests<Mp4CodecTests>(logger)
{
    #region Constants

    private const int TestWidth = 64;
    private const int TestHeight = 64;

    #endregion

    #region Setup

    private Mp4Codec? codec;

    public Mp4CodecTests() : this(ConsoleLogger.Unicode) { }

    [SetUp]
    public void SetUp() => codec = new Mp4Codec();

    [TearDown]
    public void TearDown()
    {
        codec?.Dispose();
        codec = null;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "Mp4Codec: создание экземпляра")]
    public void CanCreateInstance()
    {
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec!.CodecId, Is.EqualTo(MediaCodecId.H264));
        Assert.That(codec.Name, Does.Contain("MP4"));
        Assert.That(codec.MimeType, Is.EqualTo("video/mp4"));
    }

    [TestCase(TestName = "Mp4Codec: инициализация энкодера RGBA32")]
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

    [TestCase(TestName = "Mp4Codec: инициализация энкодера YUV420P")]
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

    [TestCase(TestName = "Mp4Codec: инициализация декодера")]
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

    [TestCase(TestName = "Mp4Codec: инициализация с нулевыми размерами")]
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

    [TestCase(TestName = "Mp4Codec: инициализация с превышением лимита 16384")]
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

    [TestCase(TestName = "Mp4Codec: кодирование RGBA32 кадра")]
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

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано RGBA32: {bytesWritten} байт");
    }

    [TestCase(TestName = "Mp4Codec: кодирование YUV420P кадра")]
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

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
        var output = new byte[outputSize];

        var result = codec.Encode(roFrame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Закодировано YUV420P: {bytesWritten} байт");
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "Mp4Codec: round-trip RGBA32")]
    public void RoundTripRgba32DataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer);

        // Encode
        var encoder = new Mp4Codec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new Mp4Codec();
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

    [TestCase(TestName = "Mp4Codec: round-trip YUV420P")]
    public void RoundTripYuv420PDataMatches()
    {
        const int width = 32;
        const int height = 32;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        FillTestPatternYuv420P(originalBuffer);

        // Encode
        var encoder = new Mp4Codec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
        var encoded = new byte[outputSize];

        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        // Decode
        var decoder = new Mp4Codec();
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

    [TestCase(TestName = "Mp4Codec: real-time round-trip 480p >= 240 FPS")]
    public void RealTimeRoundTrip480p()
    {
        const int width = 640;
        const int height = 480;
        const int requiredFps = 240;
        const int iterations = 50;

        using var encodeCodec = new Mp4Codec();
        encodeCodec.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodeCodec = new Mp4Codec();

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(frameBuffer);
        var roFrame = frameBuffer.AsReadOnlyFrame();

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
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

        TestContext.Out.WriteLine($"MP4 480p round-trip: {avgMs:F3} мс/кадр, {fps:F1} FPS");
        TestContext.Out.WriteLine($"  Размер: {bytesWritten} байт");

        Assert.That(fps, Is.GreaterThanOrEqualTo(requiredFps),
            $"FPS ({fps:F1}) должен быть >= {requiredFps}");
    }

    [TestCase(TestName = "Mp4Codec: FPS по разрешениям от 480p до 8K")]
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

            using var encodeCodec = new Mp4Codec();
            encodeCodec.InitializeEncoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Rgba32
            });

            using var decodeCodec = new Mp4Codec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            FillTestPatternRgba32(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
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

    [TestCase(TestName = "Mp4Codec: YUV420P multi-resolution benchmark")]
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
            var uvSize = width / 2 * (height / 2) * 2;
            var rawBytes = ySize + uvSize; // YUV420P
            const int iterations = 20;

            using var encodeCodec = new Mp4Codec();
            encodeCodec.InitializeEncoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Yuv420P
            });

            using var decodeCodec = new Mp4Codec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
            FillTestPatternYuv420P(frameBuffer);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
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

    [TestCase(TestName = "Mp4Codec: последовательность из 100 кадров")]
    public void EncodeDecodeSequence100Frames()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 100;

        using var encoder = new Mp4Codec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
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

    #region MP4 ↔ MP4 Conversion Tests

    [TestCase(TestName = "Mp4Codec: MP4 → MP4 реконвертация")]
    public void Mp4ToMp4Reconversion()
    {
        const int width = 128;
        const int height = 128;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer);

        // First encode
        using var encoder1 = new Mp4Codec();
        encoder1.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var output1 = new byte[Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        encoder1.Encode(originalBuffer.AsReadOnlyFrame(), output1, out var size1);

        // Decode
        using var decoder1 = new Mp4Codec();
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
        using var encoder2 = new Mp4Codec();
        encoder2.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var output2 = new byte[Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        encoder2.Encode(decodedBuffer1.AsReadOnlyFrame(), output2, out var size2);

        // Decode again
        using var decoder2 = new Mp4Codec();
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

        TestContext.Out.WriteLine($"MP4 → MP4: {size1} байт → {size2} байт (идентично)");
    }

    #endregion

    #region H.264 Integration Tests

    [TestCase(TestName = "Mp4Codec: H.264 Annex B I-frame → RGBA32 decode")]
    public void DecodeH264AnnexBIFrame()
    {
        const int width = 16;
        const int height = 16;

        var bitstream = BuildH264IFrameBitstream(width, height);

        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Verify output has valid RGBA pixel data (alpha = 255)
        var row = frameBuffer.AsReadOnlyFrame().PackedData.GetRow(0);
        Assert.That(row[3], Is.EqualTo(255), "Alpha channel should be 255");

        decoder.Dispose();
        TestContext.Out.WriteLine($"H.264 Annex B decode: {bitstream.Length} bytes → {width}×{height} RGBA32");
    }

    [TestCase(TestName = "Mp4Codec: H.264 Annex B multi-MB I-frame decode")]
    public void DecodeH264AnnexBMultiMbIFrame()
    {
        const int width = 32;
        const int height = 32;

        var bitstream = BuildH264IFrameBitstream(width, height);

        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"H.264 Annex B multi-MB: {bitstream.Length} bytes → {width}×{height} RGBA32");
    }

    [TestCase(TestName = "Mp4Codec: H.264 AVCC decode с ExtraData (SPS/PPS)")]
    public void DecodeH264AvccWithExtraData()
    {
        const int width = 16;
        const int height = 16;

        // Build SPS and PPS RBSP
        var spsRbsp = BuildSpsRbsp(width, height);
        var ppsRbsp = BuildPpsRbsp();

        // Build AVCDecoderConfigurationRecord (ExtraData)
        var extraData = BuildAvccExtraData(spsRbsp, ppsRbsp);

        // Build AVCC-formatted slice (length-prefixed NAL)
        var sliceNal = BuildH264SliceNal(width, height);
        var avccPacket = BuildAvccPacket(sliceNal, nalLengthSize: 4);

        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
            ExtraData = extraData
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(avccPacket, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Verify alpha channel
        var row = frameBuffer.AsReadOnlyFrame().PackedData.GetRow(0);
        Assert.That(row[3], Is.EqualTo(255), "Alpha channel should be 255");

        decoder.Dispose();
        TestContext.Out.WriteLine($"H.264 AVCC decode: ExtraData={extraData.Length}B, packet={avccPacket.Length}B → {width}×{height}");
    }

    [TestCase(TestName = "Mp4Codec: H.264 I_PCM Annex B decode")]
    public void DecodeH264IpcmAnnexB()
    {
        const int width = 16;
        const int height = 16;

        var bitstream = BuildH264IpcmBitstream(width, height);

        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // I_PCM with Y=128, Cb=128, Cr=128 → neutral gray
        var row = frameBuffer.AsReadOnlyFrame().PackedData.GetRow(8);
        Assert.That(row[3], Is.EqualTo(255));

        decoder.Dispose();
        TestContext.Out.WriteLine($"H.264 I_PCM: {bitstream.Length} bytes → {width}×{height} RGBA32");
    }

    [TestCase(TestName = "Mp4Codec: AFRM по-прежнему работает после интеграции H.264")]
    public void AfrmStillWorksAfterH264Integration()
    {
        const int width = 32;
        const int height = 32;

        // Encode with AFRM
        using var encoder = new Mp4Codec();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var srcBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(srcBuffer);
        var roFrame = srcBuffer.AsReadOnlyFrame();

        var outputSize = Mp4Codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];
        var encResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        // Decode with same Mp4Codec
        using var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var dstBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Exact match (Store mode is lossless)
        var original = srcBuffer.AsReadOnlyFrame().PackedData.Data;
        var decoded = dstBuffer.AsReadOnlyFrame().PackedData.Data;
        Assert.That(decoded.SequenceEqual(original), Is.True);
    }

    [TestCase(TestName = "Mp4Codec: codec routing — AFRM vs Annex B vs invalid")]
    public void CodecRouting()
    {
        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        // Random data → InvalidData
        var garbage = new byte[100];
        Array.Fill(garbage, (byte)0xFF);
        Assert.That(decoder.Decode(garbage, ref frame), Is.EqualTo(CodecResult.InvalidData));

        // Too short → InvalidData
        Assert.That(decoder.Decode(new byte[2], ref frame), Is.EqualTo(CodecResult.InvalidData));

        // Annex B start code → routes to H264Decoder
        var annexB = BuildH264IFrameBitstream(16, 16);
        Assert.That(decoder.Decode(annexB, ref frame), Is.EqualTo(CodecResult.Success));

        decoder.Dispose();
    }

    [TestCase(TestName = "Mp4Codec: Reset очищает H.264 состояние")]
    public void ResetClearsH264State()
    {
        var decoder = new Mp4Codec();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        // Decode H.264 to initialize internal decoder
        var bitstream = BuildH264IFrameBitstream(16, 16);
        var result = decoder.Decode(bitstream, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Reset
        decoder.Reset();

        // Should still work after reset (new decoder created on demand)
        frame = frameBuffer.AsFrame();
        result = decoder.Decode(bitstream, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        decoder.Dispose();
    }

    #endregion

    #region H.264 Bitstream Helpers

    private static byte[] BuildSpsRbsp(int width, int height)
    {
        var mbW = width / 16;
        var mbH = height / 16;

        Span<byte> buf = stackalloc byte[64];
        var w = new BitWriter(buf);

        w.WriteBits(66, 8);  // profile_idc = Baseline (66)
        w.WriteBits(0b1100_0000, 8); // constraints
        w.WriteBits(30, 8);  // level_idc = 3.0

        H264ExpGolomb.WriteUe(ref w, 0); // seq_parameter_set_id
        H264ExpGolomb.WriteUe(ref w, 0); // log2_max_frame_num_minus4
        H264ExpGolomb.WriteUe(ref w, 0); // pic_order_cnt_type
        H264ExpGolomb.WriteUe(ref w, 0); // log2_max_pic_order_cnt_lsb_minus4
        H264ExpGolomb.WriteUe(ref w, 0); // max_num_ref_frames
        w.WriteBits(0, 1);              // gaps_in_frame_num_value_allowed_flag
        H264ExpGolomb.WriteUe(ref w, (uint)(mbW - 1)); // pic_width_in_mbs_minus1
        H264ExpGolomb.WriteUe(ref w, (uint)(mbH - 1)); // pic_height_in_map_units_minus1
        w.WriteBits(1, 1);              // frame_mbs_only_flag
        w.WriteBits(0, 1);              // direct_8x8_inference_flag
        w.WriteBits(0, 1);              // frame_cropping_flag
        w.WriteBits(0, 1);              // vui_parameters_present_flag

        // RBSP trailing
        w.WriteBits(1, 1);
        while (w.BitPosition % 8 != 0)
            w.WriteBits(0, 1);
        w.Flush();

        return buf[..((w.BitPosition + 7) / 8)].ToArray();
    }

    private static byte[] BuildPpsRbsp()
    {
        Span<byte> buf = stackalloc byte[16];
        var w = new BitWriter(buf);

        H264ExpGolomb.WriteUe(ref w, 0);  // pic_parameter_set_id
        H264ExpGolomb.WriteUe(ref w, 0);  // seq_parameter_set_id
        w.WriteBits(0, 1);               // entropy_coding_mode_flag = CAVLC
        w.WriteBits(0, 1);               // bottom_field_pic_order_in_frame_present_flag
        H264ExpGolomb.WriteUe(ref w, 0);  // num_slice_groups_minus1
        H264ExpGolomb.WriteUe(ref w, 0);  // num_ref_idx_l0_default_active_minus1
        H264ExpGolomb.WriteUe(ref w, 0);  // num_ref_idx_l1_default_active_minus1
        w.WriteBits(0, 1);               // weighted_pred_flag
        w.WriteBits(0, 2);               // weighted_bipred_idc
        H264ExpGolomb.WriteSe(ref w, 0);  // pic_init_qp_minus26
        H264ExpGolomb.WriteSe(ref w, 0);  // pic_init_qs_minus26
        H264ExpGolomb.WriteSe(ref w, 0);  // chroma_qp_index_offset
        w.WriteBits(1, 1);               // deblocking_filter_control_present_flag
        w.WriteBits(0, 1);               // constrained_intra_pred_flag
        w.WriteBits(0, 1);               // redundant_pic_cnt_present_flag

        w.WriteBits(1, 1);
        while (w.BitPosition % 8 != 0)
            w.WriteBits(0, 1);
        w.Flush();

        return buf[..((w.BitPosition + 7) / 8)].ToArray();
    }

    private static byte[] BuildH264SliceRbsp(int width, int height)
    {
        var totalMbs = (width / 16) * (height / 16);

        Span<byte> buf = stackalloc byte[4096];
        var w = new BitWriter(buf);

        // Slice header (IDR I-slice)
        H264ExpGolomb.WriteUe(ref w, 0);   // first_mb_in_slice
        H264ExpGolomb.WriteUe(ref w, 2);   // slice_type = I
        H264ExpGolomb.WriteUe(ref w, 0);   // pps_id
        w.WriteBits(0, 4);                 // frame_num
        H264ExpGolomb.WriteUe(ref w, 0);   // idr_pic_id
        w.WriteBits(0, 4);                 // pic_order_cnt_lsb
        w.WriteBits(0, 1);                 // no_output_of_prior_pics
        w.WriteBits(0, 1);                 // long_term_reference
        H264ExpGolomb.WriteSe(ref w, 0);   // slice_qp_delta
        H264ExpGolomb.WriteUe(ref w, 1);   // disable_deblocking = 1

        for (var mb = 0; mb < totalMbs; mb++)
        {
            H264ExpGolomb.WriteUe(ref w, 1); // I_16x16_0_0_0
            H264ExpGolomb.WriteUe(ref w, 0); // intra_chroma_pred_mode DC
            H264ExpGolomb.WriteSe(ref w, 0); // mb_qp_delta
            w.WriteBits(1, 1);               // coeff_token: TotalCoeff=0
        }

        // RBSP trailing
        w.WriteBits(1, 1);
        while (w.BitPosition % 8 != 0)
            w.WriteBits(0, 1);
        w.Flush();

        return buf[..((w.BitPosition + 7) / 8)].ToArray();
    }

    private static byte[] BuildH264IFrameBitstream(int width, int height)
    {
        var spsRbsp = BuildSpsRbsp(width, height);
        var ppsRbsp = BuildPpsRbsp();
        var sliceRbsp = BuildH264SliceRbsp(width, height);

        var result = new List<byte>();
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x67]); // SPS NAL
        result.AddRange(spsRbsp);
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x68]); // PPS NAL
        result.AddRange(ppsRbsp);
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x65]); // IDR slice NAL
        result.AddRange(sliceRbsp);

        return [.. result];
    }

    private static byte[] BuildH264SliceNal(int width, int height)
    {
        // NAL header byte (0x65 = IDR slice) + slice RBSP
        var sliceRbsp = BuildH264SliceRbsp(width, height);
        var nal = new byte[1 + sliceRbsp.Length];
        nal[0] = 0x65; // forbidden_zero_bit=0, nal_ref_idc=3, nal_unit_type=5 (IDR)
        sliceRbsp.CopyTo(nal, 1);
        return nal;
    }

    private static byte[] BuildAvccExtraData(byte[] spsRbsp, byte[] ppsRbsp)
    {
        // SPS NAL = 0x67 + spsRbsp
        var spsNal = new byte[1 + spsRbsp.Length];
        spsNal[0] = 0x67;
        spsRbsp.CopyTo(spsNal, 1);

        // PPS NAL = 0x68 + ppsRbsp
        var ppsNal = new byte[1 + ppsRbsp.Length];
        ppsNal[0] = 0x68;
        ppsRbsp.CopyTo(ppsNal, 1);

        // AVCDecoderConfigurationRecord
        var result = new List<byte>
        {
            1,         // configurationVersion
            spsRbsp[0], // AVCProfileIndication (profile_idc)
            spsRbsp[1], // profile_compatibility
            spsRbsp[2], // AVCLevelIndication (level_idc)
            0xFF,      // lengthSizeMinusOne = 3 → nalLengthSize = 4 (0b11111111)
            (byte)(0xE0 | 1), // numOfSPS = 1 (0b111 00001)
        };

        // SPS length (big-endian)
        result.Add((byte)(spsNal.Length >> 8));
        result.Add((byte)(spsNal.Length & 0xFF));
        result.AddRange(spsNal);

        // numOfPPS = 1
        result.Add(1);
        result.Add((byte)(ppsNal.Length >> 8));
        result.Add((byte)(ppsNal.Length & 0xFF));
        result.AddRange(ppsNal);

        return [.. result];
    }

    private static byte[] BuildAvccPacket(byte[] nalData, int nalLengthSize)
    {
        var packet = new byte[nalLengthSize + nalData.Length];

        // Write NAL length in big-endian
        for (var i = 0; i < nalLengthSize; i++)
        {
            packet[i] = (byte)(nalData.Length >> (8 * (nalLengthSize - 1 - i)));
        }

        nalData.CopyTo(packet, nalLengthSize);
        return packet;
    }

    private static byte[] BuildH264IpcmBitstream(int width, int height)
    {
        var totalMbs = (width / 16) * (height / 16);
        var spsRbsp = BuildSpsRbsp(width, height);
        var ppsRbsp = BuildPpsRbsp();

        Span<byte> buf = stackalloc byte[8192];
        var w = new BitWriter(buf);

        // Slice header
        H264ExpGolomb.WriteUe(ref w, 0);   // first_mb_in_slice
        H264ExpGolomb.WriteUe(ref w, 2);   // slice_type = I
        H264ExpGolomb.WriteUe(ref w, 0);   // pps_id
        w.WriteBits(0, 4);                 // frame_num
        H264ExpGolomb.WriteUe(ref w, 0);   // idr_pic_id
        w.WriteBits(0, 4);                 // pic_order_cnt_lsb
        w.WriteBits(0, 1);                 // no_output_of_prior_pics
        w.WriteBits(0, 1);                 // long_term_reference
        H264ExpGolomb.WriteSe(ref w, 0);   // slice_qp_delta
        H264ExpGolomb.WriteUe(ref w, 1);   // disable_deblocking = 1

        for (var mb = 0; mb < totalMbs; mb++)
        {
            H264ExpGolomb.WriteUe(ref w, 25); // I_PCM

            while (w.BitPosition % 8 != 0)
                w.WriteBits(0, 1);

            for (var p = 0; p < 256; p++) w.WriteBits(128, 8); // Y
            for (var p = 0; p < 64; p++) w.WriteBits(128, 8);  // Cb
            for (var p = 0; p < 64; p++) w.WriteBits(128, 8);  // Cr
        }

        w.WriteBits(1, 1);
        while (w.BitPosition % 8 != 0)
            w.WriteBits(0, 1);
        w.Flush();

        var sliceRbsp = buf[..((w.BitPosition + 7) / 8)].ToArray();

        var result = new List<byte>();
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x67]);
        result.AddRange(spsRbsp);
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x68]);
        result.AddRange(ppsRbsp);
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x65]);
        result.AddRange(sliceRbsp);

        return [.. result];
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
                row[x] = (byte)(128 + ((x - (uvWidth / 2)) * 50 / uvWidth));
            }
        }

        // V plane (Cr)
        var vPlane = frame.GetPlaneV();
        for (var y = 0; y < uvHeight; y++)
        {
            var row = vPlane.GetRow(y);
            for (var x = 0; x < uvWidth; x++)
            {
                row[x] = (byte)(128 + ((y - (uvHeight / 2)) * 50 / uvHeight));
            }
        }
    }

    #endregion
}
