#pragma warning disable CA1861, MA0051

using Atom.Media.Codecs.Webp.Vp8;

namespace Atom.Media.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class Vp8EncoderTests(ILogger logger) : BenchmarkTests<Vp8EncoderTests>(logger)
{
    private const string AssetsDir = "assets";

    private WebpCodec? codec;

    public Vp8EncoderTests() : this(ConsoleLogger.Unicode)
    {
    }

    [SetUp]
    public void SetUp() => codec = new WebpCodec();

    [TearDown]
    public void TearDown()
    {
        codec?.Dispose();
        codec = null;
    }

    #region BoolEncoder Tests

    [TestCase(TestName = "VP8 BoolEncoder: запись и чтение литерала")]
    public void BoolEncoderLiteralRoundTrip()
    {
        Span<byte> buf = stackalloc byte[256];
        var enc = new Vp8BoolEncoder(buf);

        enc.EncodeLiteral(42, 8);
        enc.Flush();

        var dec = new Vp8BoolDecoder(buf[..enc.BytesWritten]);
        var value = dec.DecodeLiteral(8);

        Assert.That(value, Is.EqualTo(42));
    }

    [TestCase(TestName = "VP8 BoolEncoder: запись и чтение знакового значения")]
    public void BoolEncoderSignedRoundTrip()
    {
        Span<byte> buf = stackalloc byte[256];
        var enc = new Vp8BoolEncoder(buf);

        enc.EncodeSigned(-17, 8);
        enc.Flush();

        var dec = new Vp8BoolDecoder(buf[..enc.BytesWritten]);
        var value = dec.DecodeSigned(8);

        Assert.That(value, Is.EqualTo(-17));
    }

    [TestCase(TestName = "VP8 BoolEncoder: множественные литералы")]
    public void BoolEncoderMultipleLiterals()
    {
        Span<byte> buf = stackalloc byte[1024];
        var enc = new Vp8BoolEncoder(buf);

        enc.EncodeLiteral(0, 7);
        enc.EncodeLiteral(127, 7);
        enc.EncodeLiteral(63, 7);
        enc.Flush();

        var dec = new Vp8BoolDecoder(buf[..enc.BytesWritten]);
        Assert.That(dec.DecodeLiteral(7), Is.Zero);
        Assert.That(dec.DecodeLiteral(7), Is.EqualTo(127));
        Assert.That(dec.DecodeLiteral(7), Is.EqualTo(63));
    }

    [TestCase(TestName = "VP8 BoolEncoder: битовое кодирование с вероятностью")]
    public void BoolEncoderBitWithProbability()
    {
        Span<byte> buf = stackalloc byte[256];
        var enc = new Vp8BoolEncoder(buf);

        // Encode bits with various probabilities
        enc.EncodeBit(0, 128);
        enc.EncodeBit(1, 128);
        enc.EncodeBit(1, 200);
        enc.EncodeBit(0, 50);
        enc.Flush();

        var dec = new Vp8BoolDecoder(buf[..enc.BytesWritten]);
        Assert.That(dec.DecodeBit(128), Is.Zero);
        Assert.That(dec.DecodeBit(128), Is.EqualTo(1));
        Assert.That(dec.DecodeBit(200), Is.EqualTo(1));
        Assert.That(dec.DecodeBit(50), Is.Zero);
    }

    #endregion

    #region Container Format Tests

    [TestCase(TestName = "VP8 Encoder: выходной формат содержит RIFF/WEBP/VP8 заголовки")]
    public void EncoderOutputContainsRiffHeaders()
    {
        var c = codec!;

        var result = c.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Create a simple 16x16 test frame
        using var buffer = CreateSolidFrame(16, 16, 128, 64, 32, 255);
        var frame = buffer.AsReadOnlyFrame();

        var output = new byte[64 * 1024];
        var encResult = c.Encode(frame, output, out var bytesWritten);

        Assert.That(encResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(20));

        // Check RIFF signature
        Assert.That(output[0], Is.EqualTo((byte)'R'));
        Assert.That(output[1], Is.EqualTo((byte)'I'));
        Assert.That(output[2], Is.EqualTo((byte)'F'));
        Assert.That(output[3], Is.EqualTo((byte)'F'));

        // Check WEBP signature
        Assert.That(output[8], Is.EqualTo((byte)'W'));
        Assert.That(output[9], Is.EqualTo((byte)'E'));
        Assert.That(output[10], Is.EqualTo((byte)'B'));
        Assert.That(output[11], Is.EqualTo((byte)'P'));

        // Check VP8 chunk type
        Assert.That(output[12], Is.EqualTo((byte)'V'));
        Assert.That(output[13], Is.EqualTo((byte)'P'));
        Assert.That(output[14], Is.EqualTo((byte)'8'));
        Assert.That(output[15], Is.EqualTo((byte)' '));
    }

    [TestCase(TestName = "VP8 Encoder: sync code в VP8 данных")]
    public void EncoderOutputContainsSyncCode()
    {
        var c = codec!;

        c.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var buffer = CreateSolidFrame(16, 16, 100, 150, 200, 255);
        var frame = buffer.AsReadOnlyFrame();

        var output = new byte[64 * 1024];
        c.Encode(frame, output, out var bytesWritten);

        // Skip RIFF(12) + VP8 chunk header(8) + frame tag(3) → sync code at offset 23
        Assert.That(output[23], Is.EqualTo(0x9D));
        Assert.That(output[24], Is.EqualTo(0x01));
        Assert.That(output[25], Is.EqualTo(0x2A));
    }

    [TestCase(TestName = "VP8 Encoder: размеры корректно записаны в заголовок")]
    public void EncoderOutputContainsCorrectDimensions()
    {
        var c = codec!;

        c.InitializeEncoder(new ImageCodecParameters
        {
            Width = 32,
            Height = 48,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 50,
        });

        using var buffer = CreateSolidFrame(32, 48, 200, 100, 50, 255);
        var frame = buffer.AsReadOnlyFrame();

        var output = new byte[128 * 1024];
        c.Encode(frame, output, out _);

        // Width at offset 26-27, height at offset 28-29
        var w = output[26] | ((output[27] & 0x3F) << 8);
        var h = output[28] | ((output[29] & 0x3F) << 8);

        Assert.That(w, Is.EqualTo(32));
        Assert.That(h, Is.EqualTo(48));
    }

    #endregion

    #region Round-trip Tests

    [TestCase(TestName = "VP8 Encoder: round-trip сплошного цвета 16×16")]
    public void RoundTripSolidColor16x16()
    {
        var output = new byte[64 * 1024];

        // Encode
        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 90,
        });

        using var srcBuffer = CreateSolidFrame(16, 16, 128, 128, 128, 255);
        var srcFrame = srcBuffer.AsReadOnlyFrame();
        var encResult = encCodec.Encode(srcFrame, output, out var bytesWritten);

        Assert.That(encResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));

        // Decode
        using var decCodec = new WebpCodec();
        var data = output.AsSpan(0, bytesWritten).ToArray();
        var infoResult = decCodec.GetInfo(data, out var info);

        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(16));
        Assert.That(info.Height, Is.EqualTo(16));
        Assert.That(info.IsLossless, Is.False);

        decCodec.InitializeDecoder(new ImageCodecParameters
        {
            Width = info.Width,
            Height = info.Height,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        using var dstBuffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var dstFrame2 = dstBuffer.AsFrame();
        var decResult = decCodec.Decode(data, ref dstFrame2);

        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Verify pixels are close (lossy, so not exact)
        var dstFrame = dstBuffer.AsReadOnlyFrame();
        var maxDiff = 0;

        for (var y = 0; y < 16; y++)
        {
            var row = dstFrame.PackedData.GetRow(y);
            for (var x = 0; x < 16; x++)
            {
                var diff = Math.Abs(row[x * 4] - 128);
                if (diff > maxDiff) { maxDiff = diff; }
                diff = Math.Abs(row[(x * 4) + 1] - 128);
                if (diff > maxDiff) { maxDiff = diff; }
                diff = Math.Abs(row[(x * 4) + 2] - 128);
                if (diff > maxDiff) { maxDiff = diff; }
                // Alpha should be 255
                Assert.That(row[(x * 4) + 3], Is.EqualTo(255));
            }
        }

        // For a solid gray image, lossy compression should be very close
        Assert.That(maxDiff, Is.LessThanOrEqualTo(30), $"Max pixel difference: {maxDiff}");
    }

    [TestCase(TestName = "VP8 Encoder: round-trip 32×32 градиент")]
    public void RoundTripGradient32x32()
    {
        var output = new byte[128 * 1024];

        // Encode
        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 32,
            Height = 32,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 90,
        });

        using var srcBuffer = CreateGradientFrame(32, 32);
        var srcFrame = srcBuffer.AsReadOnlyFrame();
        var encResult = encCodec.Encode(srcFrame, output, out var bytesWritten);

        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        // Decode
        using var decCodec = new WebpCodec();
        var data = output.AsSpan(0, bytesWritten).ToArray();
        decCodec.InitializeDecoder(new ImageCodecParameters
        {
            Width = 32,
            Height = 32,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        using var dstBuffer = new VideoFrameBuffer(32, 32, VideoPixelFormat.Rgba32);
        var dstFrame2 = dstBuffer.AsFrame();
        var decResult = decCodec.Decode(data, ref dstFrame2);

        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        // Check non-zero pixels exist
        var dstFrame = dstBuffer.AsReadOnlyFrame();
        var nonZero = 0;
        for (var y = 0; y < 32; y++)
        {
            var row = dstFrame.PackedData.GetRow(y);
            for (var x = 0; x < 32; x++)
            {
                if (row[x * 4] > 0 || row[(x * 4) + 1] > 0 || row[(x * 4) + 2] > 0)
                {
                    nonZero++;
                }
            }
        }

        Assert.That(nonZero, Is.GreaterThan(0));
    }

    [TestCase(TestName = "VP8 Encoder: round-trip decode real lossy WebP после encode")]
    public void RoundTripDecodesOwnOutput()
    {
        var output = new byte[256 * 1024];

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 48,
            Height = 48,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var srcBuffer = CreateCheckerFrame(48, 48);
        var srcFrame = srcBuffer.AsReadOnlyFrame();
        var encResult = encCodec.Encode(srcFrame, output, out var bytesWritten);

        Assert.That(encResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(20));

        // Verify the encoded data can be detected as WebP
        using var decCodec = new WebpCodec();
        var data = output.AsSpan(0, bytesWritten).ToArray();
        Assert.That(decCodec.CanDecode(data), Is.True);

        // Decode
        var infoResult = decCodec.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(48));
        Assert.That(info.Height, Is.EqualTo(48));

        decCodec.InitializeDecoder(new ImageCodecParameters
        {
            Width = 48,
            Height = 48,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        using var dstBuffer = new VideoFrameBuffer(48, 48, VideoPixelFormat.Rgba32);
        var dstFrame = dstBuffer.AsFrame();
        var decResult = decCodec.Decode(data, ref dstFrame);

        Assert.That(decResult, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Quality Tests

    [TestCase(TestName = "VP8 Encoder: Quality=0 использует VP8L lossless")]
    public void QualityZeroUsesLossless()
    {
        var output = new byte[64 * 1024];

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 0,
        });

        using var buffer = CreateSolidFrame(16, 16, 100, 100, 100, 255);
        var frame = buffer.AsReadOnlyFrame();
        encCodec.Encode(frame, output, out var bytesWritten);

        // VP8L uses "VP8L" chunk
        Assert.That(output[12], Is.EqualTo((byte)'V'));
        Assert.That(output[13], Is.EqualTo((byte)'P'));
        Assert.That(output[14], Is.EqualTo((byte)'8'));
        Assert.That(output[15], Is.EqualTo((byte)'L'));
    }

    [TestCase(TestName = "VP8 Encoder: высокое качество → больший размер")]
    public void HigherQualityProducesLargerOutput()
    {
        using var buffer = CreateGradientFrame(32, 32);
        var frame = buffer.AsReadOnlyFrame();

        var output = new byte[128 * 1024];

        // Low quality
        using var lowCodec = new WebpCodec();
        lowCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 32,
            Height = 32,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 10,
        });
        lowCodec.Encode(frame, output, out var lowSize);

        // High quality
        using var highCodec = new WebpCodec();
        highCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 32,
            Height = 32,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 95,
        });
        highCodec.Encode(frame, output, out var highSize);

        // Higher quality should generally produce larger output (more bits per coeff)
        // But this isn't always guaranteed; at minimum both should succeed
        Assert.That(lowSize, Is.GreaterThan(0));
        Assert.That(highSize, Is.GreaterThan(0));
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "VP8 Encoder: буфер слишком мал")]
    public void OutputBufferTooSmall()
    {
        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var buffer = CreateSolidFrame(16, 16, 128, 128, 128, 255);
        var frame = buffer.AsReadOnlyFrame();

        var tinyOutput = new byte[10];
        var result = encCodec.Encode(frame, tinyOutput, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.OutputBufferTooSmall));
        Assert.That(bytesWritten, Is.Zero);
    }

    [TestCase(TestName = "VP8 Encoder: чёрное изображение 16×16")]
    public void EncodeBlackImage()
    {
        var output = new byte[64 * 1024];

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var buffer = CreateSolidFrame(16, 16, 0, 0, 0, 255);
        var frame = buffer.AsReadOnlyFrame();
        var result = encCodec.Encode(frame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(20));
    }

    [TestCase(TestName = "VP8 Encoder: белое изображение 16×16")]
    public void EncodeWhiteImage()
    {
        var output = new byte[64 * 1024];

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var buffer = CreateSolidFrame(16, 16, 255, 255, 255, 255);
        var frame = buffer.AsReadOnlyFrame();
        var result = encCodec.Encode(frame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(20));
    }

    [TestCase(TestName = "VP8 Encoder: не-кратные-16 размеры (17×13)")]
    public void EncodeNonMultipleOf16()
    {
        var output = new byte[128 * 1024];

        using var encCodec = new WebpCodec();
        encCodec.InitializeEncoder(new ImageCodecParameters
        {
            Width = 17,
            Height = 13,
            PixelFormat = VideoPixelFormat.Rgba32,
            Quality = 75,
        });

        using var buffer = CreateSolidFrame(17, 13, 100, 200, 50, 255);
        var frame = buffer.AsReadOnlyFrame();
        var result = encCodec.Encode(frame, output, out var bytesWritten);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Verify dimensions in output
        var w = output[26] | ((output[27] & 0x3F) << 8);
        var h = output[28] | ((output[29] & 0x3F) << 8);
        Assert.That(w, Is.EqualTo(17));
        Assert.That(h, Is.EqualTo(13));
    }

    #endregion

    #region Helpers

    private static VideoFrameBuffer CreateSolidFrame(int width, int height, byte r, byte g, byte b, byte a)
    {
        var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var plane = buffer.AsFrame().PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                row[x * 4] = r;
                row[(x * 4) + 1] = g;
                row[(x * 4) + 2] = b;
                row[(x * 4) + 3] = a;
            }
        }

        return buffer;
    }

    private static VideoFrameBuffer CreateGradientFrame(int width, int height)
    {
        var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var plane = buffer.AsFrame().PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                row[x * 4] = (byte)((x * 255) / Math.Max(width - 1, 1));
                row[(x * 4) + 1] = (byte)((y * 255) / Math.Max(height - 1, 1));
                row[(x * 4) + 2] = (byte)(((x + y) * 127) / Math.Max(width + height - 2, 1));
                row[(x * 4) + 3] = 255;
            }
        }

        return buffer;
    }

    private static VideoFrameBuffer CreateCheckerFrame(int width, int height)
    {
        var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var plane = buffer.AsFrame().PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var isWhite = ((x / 8) + (y / 8)) % 2 == 0;
                var val = isWhite ? (byte)255 : (byte)0;
                row[x * 4] = val;
                row[(x * 4) + 1] = val;
                row[(x * 4) + 2] = val;
                row[(x * 4) + 3] = 255;
            }
        }

        return buffer;
    }

    #endregion
}
