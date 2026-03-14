#pragma warning disable CA1861, MA0051, IDE0047, IDE0048

using System.Diagnostics;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты VP8L (WebP Lossless) кодировщика.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class Vp8LEncoderTests(ILogger logger) : BenchmarkTests<Vp8LEncoderTests>(logger)
{
    #region Setup

    private WebpCodec? encoder;
    private WebpCodec? decoder;

    public Vp8LEncoderTests() : this(ConsoleLogger.Unicode) { }

    [SetUp]
    public void SetUp()
    {
        encoder = new WebpCodec();
        decoder = new WebpCodec();
    }

    [TearDown]
    public void TearDown()
    {
        encoder?.Dispose();
        encoder = null;
        decoder?.Dispose();
        decoder = null;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "VP8L Encode: 1x1 красный пиксель")]
    public void Encode1x1RedPixelSuccess()
    {
        using var buffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        var row = frame.PackedData.GetRow(0);
        row[0] = 255; // R
        row[1] = 0;   // G
        row[2] = 0;   // B
        row[3] = 255; // A

        var output = new byte[1024];
        encoder!.InitializeEncoder(new ImageCodecParameters(1, 1, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"VP8L encode 1x1: {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Encode: 4x4 белое изображение")]
    public void Encode4x4WhiteImageSuccess()
    {
        using var buffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        // Заполняем белым цветом
        for (var y = 0; y < 4; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                row[x * 4] = 255;     // R
                row[x * 4 + 1] = 255; // G
                row[x * 4 + 2] = 255; // B
                row[x * 4 + 3] = 255; // A
            }
        }

        var output = new byte[4096];
        encoder!.InitializeEncoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"VP8L encode 4x4 white: {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Encode: 16x16 сплошной цвет")]
    public void Encode16x16SolidColorSuccess()
    {
        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        // Заполняем синим цветом
        for (var y = 0; y < 16; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 16; x++)
            {
                row[x * 4] = 0;       // R
                row[x * 4 + 1] = 0;   // G
                row[x * 4 + 2] = 255; // B
                row[x * 4 + 3] = 255; // A
            }
        }

        var output = new byte[8192];
        encoder!.InitializeEncoder(new ImageCodecParameters(16, 16, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"VP8L encode 16x16 solid: {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Encode: 8x8 градиент")]
    public void Encode8x8GradientSuccess()
    {
        using var buffer = new VideoFrameBuffer(8, 8, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        for (var y = 0; y < 8; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 8; x++)
            {
                var val = (byte)(x * 32 + y * 32);
                row[x * 4] = val;     // R
                row[x * 4 + 1] = val; // G
                row[x * 4 + 2] = val; // B
                row[x * 4 + 3] = 255; // A
            }
        }

        var output = new byte[8192];
        encoder!.InitializeEncoder(new ImageCodecParameters(8, 8, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"VP8L encode 8x8 gradient: {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Encode: RGB24 формат")]
    public void EncodeRgb24FormatSuccess()
    {
        using var buffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgb24);
        var frame = buffer.AsFrame();

        for (var y = 0; y < 4; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                row[x * 3] = 128;     // R
                row[x * 3 + 1] = 64;  // G
                row[x * 3 + 2] = 32;  // B
            }
        }

        var output = new byte[4096];
        encoder!.InitializeEncoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgb24));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"VP8L encode 4x4 RGB24: {bytesWritten} байт");
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "VP8L Round-trip: 1x1 красный пиксель")]
    public void RoundTrip1x1RedPixelExact()
    {
        // Кодируем
        using var srcBuffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var srcFrame = srcBuffer.AsFrame();
        var srcRow = srcFrame.PackedData.GetRow(0);
        srcRow[0] = 255; // R
        srcRow[1] = 0;   // G
        srcRow[2] = 0;   // B
        srcRow[3] = 255; // A

        var encoded = new byte[1024];
        encoder!.InitializeEncoder(new ImageCodecParameters(1, 1, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        // Декодируем
        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        var infoResult = decoder!.GetInfo(webpData, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(1));
        Assert.That(info.Height, Is.EqualTo(1));
        Assert.That(info.IsLossless, Is.True);

        decoder.InitializeDecoder(new ImageCodecParameters(1, 1, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Сравниваем пиксели
        var dstRow = dstBuffer.AsReadOnlyFrame().PackedData.GetRow(0);
        Assert.That(dstRow[0], Is.EqualTo(255), "R");
        Assert.That(dstRow[1], Is.Zero, "G");
        Assert.That(dstRow[2], Is.Zero, "B");
        Assert.That(dstRow[3], Is.EqualTo(255), "A");

        TestContext.Out.WriteLine($"VP8L round-trip 1x1: OK, {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Round-trip: 4x4 белое изображение")]
    public void RoundTrip4x4WhiteImageExact()
    {
        using var srcBuffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgba32);
        var srcFrame = srcBuffer.AsFrame();

        for (var y = 0; y < 4; y++)
        {
            var row = srcFrame.PackedData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                row[x * 4] = 255;
                row[x * 4 + 1] = 255;
                row[x * 4 + 2] = 255;
                row[x * 4 + 3] = 255;
            }
        }

        var encoded = new byte[4096];
        encoder!.InitializeEncoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        decoder!.InitializeDecoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Все пиксели должны быть (255, 255, 255, 255)
        var dstData = dstBuffer.AsReadOnlyFrame().PackedData;
        for (var y = 0; y < 4; y++)
        {
            var row = dstData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                Assert.That(row[x * 4], Is.EqualTo(255), $"R at [{x},{y}]");
                Assert.That(row[x * 4 + 1], Is.EqualTo(255), $"G at [{x},{y}]");
                Assert.That(row[x * 4 + 2], Is.EqualTo(255), $"B at [{x},{y}]");
                Assert.That(row[x * 4 + 3], Is.EqualTo(255), $"A at [{x},{y}]");
            }
        }

        TestContext.Out.WriteLine($"VP8L round-trip 4x4 white: OK, {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Round-trip: 16x16 сплошной цвет")]
    public void RoundTrip16x16SolidColorExact()
    {
        const byte srcR = 100, srcG = 150, srcB = 200, srcA = 255;

        using var srcBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var srcFrame = srcBuffer.AsFrame();

        for (var y = 0; y < 16; y++)
        {
            var row = srcFrame.PackedData.GetRow(y);
            for (var x = 0; x < 16; x++)
            {
                row[x * 4] = srcR;
                row[x * 4 + 1] = srcG;
                row[x * 4 + 2] = srcB;
                row[x * 4 + 3] = srcA;
            }
        }

        var encoded = new byte[8192];
        encoder!.InitializeEncoder(new ImageCodecParameters(16, 16, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        decoder!.InitializeDecoder(new ImageCodecParameters(16, 16, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        var dstData = dstBuffer.AsReadOnlyFrame().PackedData;
        for (var y = 0; y < 16; y++)
        {
            var row = dstData.GetRow(y);
            for (var x = 0; x < 16; x++)
            {
                Assert.That(row[x * 4], Is.EqualTo(srcR), $"R at [{x},{y}]");
                Assert.That(row[x * 4 + 1], Is.EqualTo(srcG), $"G at [{x},{y}]");
                Assert.That(row[x * 4 + 2], Is.EqualTo(srcB), $"B at [{x},{y}]");
                Assert.That(row[x * 4 + 3], Is.EqualTo(srcA), $"A at [{x},{y}]");
            }
        }

        TestContext.Out.WriteLine($"VP8L round-trip 16x16 solid: OK, {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Round-trip: 8x8 градиент")]
    public void RoundTrip8x8GradientExact()
    {
        using var srcBuffer = new VideoFrameBuffer(8, 8, VideoPixelFormat.Rgba32);
        var srcFrame = srcBuffer.AsFrame();

        // Создаём градиент
        for (var y = 0; y < 8; y++)
        {
            var row = srcFrame.PackedData.GetRow(y);
            for (var x = 0; x < 8; x++)
            {
                row[x * 4] = (byte)(x * 32);       // R
                row[x * 4 + 1] = (byte)(y * 32);   // G
                row[x * 4 + 2] = (byte)(x + y * 8); // B
                row[x * 4 + 3] = 255;               // A
            }
        }

        var encoded = new byte[8192];
        encoder!.InitializeEncoder(new ImageCodecParameters(8, 8, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        decoder!.InitializeDecoder(new ImageCodecParameters(8, 8, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(8, 8, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Lossless — пиксели должны совпадать точно
        var srcData = srcBuffer.AsReadOnlyFrame().PackedData;
        var dstData = dstBuffer.AsReadOnlyFrame().PackedData;

        for (var y = 0; y < 8; y++)
        {
            var srcRow = srcData.GetRow(y);
            var dstRow = dstData.GetRow(y);
            for (var x = 0; x < 8; x++)
            {
                Assert.That(dstRow[x * 4], Is.EqualTo(srcRow[x * 4]), $"R at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 1], Is.EqualTo(srcRow[x * 4 + 1]), $"G at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 2], Is.EqualTo(srcRow[x * 4 + 2]), $"B at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 3], Is.EqualTo(srcRow[x * 4 + 3]), $"A at [{x},{y}]");
            }
        }

        TestContext.Out.WriteLine($"VP8L round-trip 8x8 gradient: OK, {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Round-trip: 32x32 случайные данные")]
    public void RoundTrip32x32RandomDataExact()
    {
        var rng = new Random(42); // детерминированный seed
        using var srcBuffer = new VideoFrameBuffer(32, 32, VideoPixelFormat.Rgba32);
        var srcFrame = srcBuffer.AsFrame();

        for (var y = 0; y < 32; y++)
        {
            var row = srcFrame.PackedData.GetRow(y);
            for (var x = 0; x < 32; x++)
            {
                row[x * 4] = (byte)rng.Next(256);
                row[x * 4 + 1] = (byte)rng.Next(256);
                row[x * 4 + 2] = (byte)rng.Next(256);
                row[x * 4 + 3] = 255;
            }
        }

        var encoded = new byte[32768];
        encoder!.InitializeEncoder(new ImageCodecParameters(32, 32, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        decoder!.InitializeDecoder(new ImageCodecParameters(32, 32, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(32, 32, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        var srcData = srcBuffer.AsReadOnlyFrame().PackedData;
        var dstData = dstBuffer.AsReadOnlyFrame().PackedData;

        for (var y = 0; y < 32; y++)
        {
            var srcRow = srcData.GetRow(y);
            var dstRow = dstData.GetRow(y);
            for (var x = 0; x < 32; x++)
            {
                Assert.That(dstRow[x * 4], Is.EqualTo(srcRow[x * 4]), $"R at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 1], Is.EqualTo(srcRow[x * 4 + 1]), $"G at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 2], Is.EqualTo(srcRow[x * 4 + 2]), $"B at [{x},{y}]");
                Assert.That(dstRow[x * 4 + 3], Is.EqualTo(srcRow[x * 4 + 3]), $"A at [{x},{y}]");
            }
        }

        TestContext.Out.WriteLine($"VP8L round-trip 32x32 random: OK, {bytesWritten} байт");
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "VP8L Encode: буфер слишком мал")]
    public void EncodeOutputBufferTooSmallReturnsError()
    {
        using var buffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        for (var y = 0; y < 4; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                row[x * 4] = 128;
                row[x * 4 + 1] = 128;
                row[x * 4 + 2] = 128;
                row[x * 4 + 3] = 255;
            }
        }

        var output = new byte[10]; // слишком маленький буфер
        encoder!.InitializeEncoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.OutputBufferTooSmall));
        Assert.That(bytesWritten, Is.Zero);
    }

    [TestCase(TestName = "VP8L Encode: контейнер RIFF/WEBP корректен")]
    public void EncodeProducesValidRiffContainer()
    {
        using var buffer = new VideoFrameBuffer(2, 2, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        for (var y = 0; y < 2; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 2; x++)
            {
                row[x * 4] = 64;
                row[x * 4 + 1] = 128;
                row[x * 4 + 2] = 192;
                row[x * 4 + 3] = 255;
            }
        }

        var output = new byte[4096];
        encoder!.InitializeEncoder(new ImageCodecParameters(2, 2, VideoPixelFormat.Rgba32));
        var result = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var data = output.AsSpan(0, bytesWritten);

        // RIFF header
        Assert.That(data[0], Is.EqualTo((byte)'R'));
        Assert.That(data[1], Is.EqualTo((byte)'I'));
        Assert.That(data[2], Is.EqualTo((byte)'F'));
        Assert.That(data[3], Is.EqualTo((byte)'F'));

        // WEBP magic
        Assert.That(data[8], Is.EqualTo((byte)'W'));
        Assert.That(data[9], Is.EqualTo((byte)'E'));
        Assert.That(data[10], Is.EqualTo((byte)'B'));
        Assert.That(data[11], Is.EqualTo((byte)'P'));

        // VP8L chunk
        Assert.That(data[12], Is.EqualTo((byte)'V'));
        Assert.That(data[13], Is.EqualTo((byte)'P'));
        Assert.That(data[14], Is.EqualTo((byte)'8'));
        Assert.That(data[15], Is.EqualTo((byte)'L'));

        // VP8L signature
        Assert.That(data[20], Is.EqualTo((byte)0x2F));

        TestContext.Out.WriteLine($"VP8L RIFF container: OK, {bytesWritten} байт");
    }

    [TestCase(TestName = "VP8L Encode: GetInfo читает закодированное изображение")]
    public void EncodeGetInfoReturnsCorrectDimensions()
    {
        using var buffer = new VideoFrameBuffer(8, 4, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        for (var y = 0; y < 4; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < 8; x++)
            {
                row[x * 4] = (byte)(x * 32);
                row[x * 4 + 1] = (byte)(y * 64);
                row[x * 4 + 2] = 128;
                row[x * 4 + 3] = 255;
            }
        }

        var output = new byte[8192];
        encoder!.InitializeEncoder(new ImageCodecParameters(8, 4, VideoPixelFormat.Rgba32));
        var encResult = encoder.Encode(buffer.AsReadOnlyFrame(), output, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = output.AsSpan(0, bytesWritten).ToArray();
        var infoResult = decoder!.GetInfo(webpData, out var info);

        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(8));
        Assert.That(info.Height, Is.EqualTo(4));
        Assert.That(info.IsLossless, Is.True);
        Assert.That(info.HasAlpha, Is.True);

        TestContext.Out.WriteLine($"VP8L GetInfo: {info.Width}x{info.Height}, lossless={info.IsLossless}, alpha={info.HasAlpha}");
    }

    [TestCase(TestName = "VP8L Round-trip: RGB24 → декодирование в RGBA32")]
    public void RoundTripRgb24ToRgba32()
    {
        const byte srcR = 200, srcG = 100, srcB = 50;

        using var srcBuffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgb24);
        var srcFrame = srcBuffer.AsFrame();

        for (var y = 0; y < 4; y++)
        {
            var row = srcFrame.PackedData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                row[x * 3] = srcR;
                row[x * 3 + 1] = srcG;
                row[x * 3 + 2] = srcB;
            }
        }

        var encoded = new byte[4096];
        encoder!.InitializeEncoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgb24));
        var encResult = encoder.Encode(srcBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        var webpData = encoded.AsSpan(0, bytesWritten).ToArray();
        decoder!.InitializeDecoder(new ImageCodecParameters(4, 4, VideoPixelFormat.Rgba32));
        using var dstBuffer = new VideoFrameBuffer(4, 4, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decoder.Decode(webpData, ref dstFrame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        var dstData = dstBuffer.AsReadOnlyFrame().PackedData;
        for (var y = 0; y < 4; y++)
        {
            var row = dstData.GetRow(y);
            for (var x = 0; x < 4; x++)
            {
                Assert.That(row[x * 4], Is.EqualTo(srcR), $"R at [{x},{y}]");
                Assert.That(row[x * 4 + 1], Is.EqualTo(srcG), $"G at [{x},{y}]");
                Assert.That(row[x * 4 + 2], Is.EqualTo(srcB), $"B at [{x},{y}]");
                Assert.That(row[x * 4 + 3], Is.EqualTo(255), $"A at [{x},{y}]");
            }
        }

        TestContext.Out.WriteLine($"VP8L round-trip RGB24→RGBA32: OK, {bytesWritten} байт");
    }

    #endregion

    #region Profiling

    [TestCase(TestName = "VP8L Profile: encode/decode FPS по разрешениям")]
    public void ProfileEncodeDecodeFps()
    {
        var resolutions = new (int Width, int Height, string Name, int Iters)[]
        {
            (640, 480, "480p", 20),
            (1280, 720, "720p", 10),
            (1920, 1080, "1080p", 5),
        };

        TestContext.Out.WriteLine("┌───────────┬────────────────┬────────────────┬───────────────┬────────────────┬────────────────┐");
        TestContext.Out.WriteLine("│ Разреш.   │     Пиксели    │   Encode (мс)  │  Decode (мс)  │ Round-trip FPS │   Пропускная   │");
        TestContext.Out.WriteLine("├───────────┼────────────────┼────────────────┼───────────────┼────────────────┼────────────────┤");

        foreach (var (width, height, name, iterations) in resolutions)
        {
            var pixels = width * height;
            var rawBytes = pixels * 4;
            const int warmup = 2;

            using var encCodec = new WebpCodec();
            encCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

            using var decCodec = new WebpCodec();

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var frame = frameBuffer.AsFrame();

            // Фотоподобные данные: градиенты + небольшой шум (хорошо сжимаются VP8L, реалистичный сценарий)
            var rng = new Random(42);
            for (var y = 0; y < height; y++)
            {
                var row = frame.PackedData.GetRow(y);
                for (var x = 0; x < width; x++)
                {
                    var idx = x * 4;
                    row[idx] = (byte)(((x * 255 / width) + (y * 50 / height) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 1] = (byte)(((y * 255 / height) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 2] = (byte)((((x + y) * 128 / (width + height)) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 3] = 255;
                }
            }

            var roFrame = frameBuffer.AsReadOnlyFrame();
            var outputSize = encCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[outputSize];

            // Warmup
            for (var i = 0; i < warmup; i++)
                encCodec.Encode(roFrame, encoded, out _);

            encCodec.Encode(roFrame, encoded, out var bytesWritten);
            decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            for (var i = 0; i < warmup; i++)
                decCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

            // Encode
            var swEnc = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                encCodec.Encode(roFrame, encoded, out _);
            swEnc.Stop();
            var encodeMs = swEnc.Elapsed.TotalMilliseconds / iterations;

            // Decode
            var swDec = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                decCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
            swDec.Stop();
            var decodeMs = swDec.Elapsed.TotalMilliseconds / iterations;

            var totalMs = encodeMs + decodeMs;
            var fps = 1000.0 / totalMs;
            var throughputMBps = rawBytes * fps / (1024.0 * 1024.0);

            TestContext.Out.WriteLine(
                $"│ {name,-9} │ {pixels,14:N0} │ {encodeMs,14:F2} │ {decodeMs,13:F2} │ {fps,14:F1} │ {throughputMBps,12:F1} MB/s│");
        }

        TestContext.Out.WriteLine("└───────────┴────────────────┴────────────────┴───────────────┴────────────────┴────────────────┘");
    }

#if DEBUG
    [TestCase(TestName = "VP8L Profile: decode breakdown по этапам")]
    public void ProfileDecodeBreakdown()
    {
        const int width = 1920;
        const int height = 1080;
        const int iterations = 5;
        const int warmup = 2;

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        // Фотоподобные данные: градиенты + плоские блоки + небольшой шум (хорошо сжимаются VP8L)
        var rng = new Random(42);
        for (var y = 0; y < height; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var idx = x * 4;
                var baseR = (byte)((x * 255 / width + y * 50 / height) & 0xFF);
                var baseG = (byte)((y * 255 / height) & 0xFF);
                var baseB = (byte)(((x + y) * 128 / (width + height)) & 0xFF);
                row[idx] = (byte)(baseR + rng.Next(-3, 4));
                row[idx + 1] = (byte)(baseG + rng.Next(-3, 4));
                row[idx + 2] = (byte)(baseB + rng.Next(-3, 4));
                row[idx + 3] = 255;
            }
        }

        var roFrame = frameBuffer.AsReadOnlyFrame();
        var outputSize = encCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];
        encCodec.Encode(roFrame, encoded, out var bytesWritten);

        using var decCodec = new WebpCodec();
        decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        // Warmup
        for (var i = 0; i < warmup; i++)
            decCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

        // Measure
        for (var i = 0; i < iterations; i++)
            decCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);

        var freq = (double)Stopwatch.Frequency;
        var toUs = 1_000_000.0 / freq;
        var total = Vp8LDecoder.DecTotalTicks;
        var totalUs = total * toUs;

        static double Pct(long part, long tot) => tot > 0 ? 100.0 * part / tot : 0;

        TestContext.Out.WriteLine($"=== VP8L Decoder Breakdown (1080p, last iteration) ===");
        TestContext.Out.WriteLine($"Encoded size: {bytesWritten:N0} bytes (ratio: {100.0 * bytesWritten / (width * height * 4):F1}%)");
        TestContext.Out.WriteLine($"ReadTransforms:     {Vp8LDecoder.DecReadTransformsTicks * toUs,10:F1}us ({Pct(Vp8LDecoder.DecReadTransformsTicks, total):F1}%)");
        TestContext.Out.WriteLine($"ReadMetaPrefix:     {Vp8LDecoder.DecReadMetaPrefixTicks * toUs,10:F1}us ({Pct(Vp8LDecoder.DecReadMetaPrefixTicks, total):F1}%)");
        TestContext.Out.WriteLine($"DecodePixels:       {Vp8LDecoder.DecDecodePixelsTicks * toUs,10:F1}us ({Pct(Vp8LDecoder.DecDecodePixelsTicks, total):F1}%)");
        TestContext.Out.WriteLine($"InverseTransforms:  {Vp8LDecoder.DecInverseTransformsTicks * toUs,10:F1}us ({Pct(Vp8LDecoder.DecInverseTransformsTicks, total):F1}%)");
        TestContext.Out.WriteLine($"WriteToFrame:       {Vp8LDecoder.DecWriteToFrameTicks * toUs,10:F1}us ({Pct(Vp8LDecoder.DecWriteToFrameTicks, total):F1}%)");
        TestContext.Out.WriteLine($"TOTAL:              {totalUs,10:F1}us ({totalUs / 1000.0:F2}ms)");
    }

    [TestCase(TestName = "VP8L Profile: encode breakdown по этапам")]
    public void ProfileEncodeBreakdown()
    {
        const int width = 1920;
        const int height = 1080;
        const int iterations = 5;
        const int warmup = 2;

        using var codec = new WebpCodec();
        codec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        // Фотоподобные данные: градиенты + плоские блоки + небольшой шум
        var rng = new Random(42);
        for (var y = 0; y < height; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var idx = x * 4;
                var baseR = (byte)((x * 255 / width + y * 50 / height) & 0xFF);
                var baseG = (byte)((y * 255 / height) & 0xFF);
                var baseB = (byte)(((x + y) * 128 / (width + height)) & 0xFF);
                row[idx] = (byte)(baseR + rng.Next(-3, 4));
                row[idx + 1] = (byte)(baseG + rng.Next(-3, 4));
                row[idx + 2] = (byte)(baseB + rng.Next(-3, 4));
                row[idx + 3] = 255;
            }
        }

        var roFrame = frameBuffer.AsReadOnlyFrame();
        var outputSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        // Warmup
        for (var i = 0; i < warmup; i++)
            codec.Encode(roFrame, encoded, out _);

        // Measure
        for (var i = 0; i < iterations; i++)
            codec.Encode(roFrame, encoded, out _);

        var freq = (double)Stopwatch.Frequency;
        var toUs = 1_000_000.0 / freq;
        var total = Vp8LEncoder.EncTotalTicks;
        var totalUs = total * toUs;

        static double Pct(long part, long tot) => tot > 0 ? 100.0 * part / tot : 0;

        TestContext.Out.WriteLine($"=== VP8L Encoder Breakdown (1080p, last iteration) ===");
        TestContext.Out.WriteLine($"ReadPixels:         {Vp8LEncoder.EncReadPixelsTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncReadPixelsTicks, total):F1}%)");
        TestContext.Out.WriteLine($"Predictor:          {Vp8LEncoder.EncPredictorTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncPredictorTicks, total):F1}%)");
        TestContext.Out.WriteLine($"  Selection:        {Vp8LEncoder.EncPredictorSelectionTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncPredictorSelectionTicks, total):F1}%)");
        TestContext.Out.WriteLine($"  Apply:            {Vp8LEncoder.EncPredictorApplyTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncPredictorApplyTicks, total):F1}%)");
        TestContext.Out.WriteLine($"CrossColor:         {Vp8LEncoder.EncCrossColorTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncCrossColorTicks, total):F1}%)");
        TestContext.Out.WriteLine($"SubtractGreen:      {Vp8LEncoder.EncSubtractGreenTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncSubtractGreenTicks, total):F1}%)");
        TestContext.Out.WriteLine($"LZ77:               {Vp8LEncoder.EncLz77Ticks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncLz77Ticks, total):F1}%)");
        TestContext.Out.WriteLine($"Frequency:          {Vp8LEncoder.EncFrequencyTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncFrequencyTicks, total):F1}%)");
        TestContext.Out.WriteLine($"HuffmanBuild:       {Vp8LEncoder.EncHuffmanBuildTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncHuffmanBuildTicks, total):F1}%)");
        TestContext.Out.WriteLine($"BitstreamWrite:     {Vp8LEncoder.EncBitstreamWriteTicks * toUs,10:F1}us ({Pct(Vp8LEncoder.EncBitstreamWriteTicks, total):F1}%)");
        TestContext.Out.WriteLine($"TOTAL:              {totalUs,10:F1}us ({totalUs / 1000.0:F2}ms)");
    }
#endif

    [TestCase(TestName = "VP8L Profile: pipeline throughput vs sequential")]
    public void ProfilePipelineThroughput()
    {
        const int width = 1920;
        const int height = 1080;
        const int frames = 10;
        const int warmup = 2;

        // Создаём фотоподобные данные
        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();
        var rng = new Random(42);
        for (var y = 0; y < height; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var idx = x * 4;
                row[idx] = (byte)(((x * 255 / width) + (y * 50 / height) + rng.Next(-3, 4)) & 0xFF);
                row[idx + 1] = (byte)(((y * 255 / height) + rng.Next(-3, 4)) & 0xFF);
                row[idx + 2] = (byte)((((x + y) * 128 / (width + height)) + rng.Next(-3, 4)) & 0xFF);
                row[idx + 3] = 255;
            }
        }

        var roFrame = frameBuffer.AsReadOnlyFrame();
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        // === Baseline: Sequential encode + decode ===
        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        using var decCodec = new WebpCodec();

        var outputSize = encCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];

        // Warmup sequential
        for (var i = 0; i < warmup; i++)
        {
            encCodec.Encode(roFrame, encoded, out var bw);
            decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
            decCodec.Decode(encoded.AsSpan(0, bw), ref decodedFrame);
        }

        // Measure sequential
        var swSeq = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            encCodec.Encode(roFrame, encoded, out var bw);
            decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
            decCodec.Decode(encoded.AsSpan(0, bw), ref decodedFrame);
        }
        swSeq.Stop();
        var seqMs = swSeq.Elapsed.TotalMilliseconds;
        var seqPerFrame = seqMs / frames;
        var seqFps = 1000.0 * frames / seqMs;

        // === Pipeline: overlapped encode + decode ===
        using var pipeline = new Vp8LStreamPipeline();
        pipeline.Warmup(roFrame, ref decodedFrame);

        // Measure pipeline (frames + 1 calls: frames ProcessFrame + 1 Flush)
        var swPipe = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
            pipeline.ProcessFrame(roFrame, ref decodedFrame, out _);
        pipeline.Flush(ref decodedFrame);
        swPipe.Stop();
        var pipeMs = swPipe.Elapsed.TotalMilliseconds;
        var pipePerFrame = pipeMs / frames;
        var pipeFps = 1000.0 * frames / pipeMs;

        var speedup = seqPerFrame / pipePerFrame;

        TestContext.Out.WriteLine($"=== VP8L Pipeline Throughput (1080p, {frames} frames) ===");
        TestContext.Out.WriteLine($"Sequential:  {seqMs,8:F1}ms total, {seqPerFrame,7:F1}ms/frame, {seqFps,5:F1} FPS");
        TestContext.Out.WriteLine($"Pipeline:    {pipeMs,8:F1}ms total, {pipePerFrame,7:F1}ms/frame, {pipeFps,5:F1} FPS");
        TestContext.Out.WriteLine($"Speedup:     {speedup:F2}x");
    }

    [TestCase(TestName = "VP8L Profile: pipeline throughput по разрешениям")]
    public void ProfilePipelineResolutions()
    {
        var resolutions = new (int Width, int Height, string Name, int Frames)[]
        {
            (640, 480, "480p", 20),
            (1280, 720, "720p", 10),
            (1920, 1080, "1080p", 5),
        };

        TestContext.Out.WriteLine("┌───────────┬────────────┬────────────┬────────────┬────────────┬─────────┐");
        TestContext.Out.WriteLine("│ Разреш.   │  Seq мс/фр │ Pipe мс/фр │   Seq FPS  │  Pipe FPS  │ Speedup │");
        TestContext.Out.WriteLine("├───────────┼────────────┼────────────┼────────────┼────────────┼─────────┤");

        foreach (var (width, height, name, frames) in resolutions)
        {
            const int warmup = 2;

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var frame = frameBuffer.AsFrame();
            var rng = new Random(42);
            for (var y = 0; y < height; y++)
            {
                var row = frame.PackedData.GetRow(y);
                for (var x = 0; x < width; x++)
                {
                    var idx = x * 4;
                    row[idx] = (byte)(((x * 255 / width) + (y * 50 / height) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 1] = (byte)(((y * 255 / height) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 2] = (byte)((((x + y) * 128 / (width + height)) + rng.Next(-3, 4)) & 0xFF);
                    row[idx + 3] = 255;
                }
            }

            var roFrame = frameBuffer.AsReadOnlyFrame();
            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            // Sequential
            using var encCodec = new WebpCodec();
            encCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
            using var decCodec = new WebpCodec();

            var outputSize = encCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[outputSize];

            for (var i = 0; i < warmup; i++)
            {
                encCodec.Encode(roFrame, encoded, out var bw);
                decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
                decCodec.Decode(encoded.AsSpan(0, bw), ref decodedFrame);
            }

            var swSeq = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
            {
                encCodec.Encode(roFrame, encoded, out var bw);
                decCodec.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
                decCodec.Decode(encoded.AsSpan(0, bw), ref decodedFrame);
            }
            swSeq.Stop();
            var seqPerFrame = swSeq.Elapsed.TotalMilliseconds / frames;
            var seqFps = 1000.0 * frames / swSeq.Elapsed.TotalMilliseconds;

            // Pipeline
            using var pipeline = new Vp8LStreamPipeline();
            pipeline.Warmup(roFrame, ref decodedFrame);

            var swPipe = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
                pipeline.ProcessFrame(roFrame, ref decodedFrame, out _);
            pipeline.Flush(ref decodedFrame);
            swPipe.Stop();
            var pipePerFrame = swPipe.Elapsed.TotalMilliseconds / frames;
            var pipeFps = 1000.0 * frames / swPipe.Elapsed.TotalMilliseconds;

            var speedup = seqPerFrame / pipePerFrame;

            TestContext.Out.WriteLine(
                $"│ {name,-9} │ {seqPerFrame,10:F1} │ {pipePerFrame,10:F1} │ {seqFps,10:F1} │ {pipeFps,10:F1} │ {speedup,7:F2}x│");
        }

        TestContext.Out.WriteLine("└───────────┴────────────┴────────────┴────────────┴────────────┴─────────┘");
    }

    #endregion
}
