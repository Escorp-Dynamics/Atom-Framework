#pragma warning disable CA1861, S125

using System.Buffers.Binary;
using System.Diagnostics;
using Atom.Media.ColorSpaces;

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

    [TestCase(TestName = "WebpCodec: GetInfo сохраняет alpha-флаг у extended VP8X+ALPH+VP8")]
    public void GetInfoExtendedLossyAlphaPreservesAlphaFlag()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));
        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var extendedWebp = BuildExtendedStillWebp(vp8Payload, baseInfo.Width, baseInfo.Height, includeAlphaChunk: true, alphaFlag: true);

        var result = codec.GetInfo(extendedWebp, out var info);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(baseInfo.Width));
        Assert.That(info.Height, Is.EqualTo(baseInfo.Height));
        Assert.That(info.HasAlpha, Is.True);
        Assert.That(info.IsLossless, Is.False);
    }

    [TestCase(TestName = "WebpCodec: строгий парсинг отклоняет reconstruction chunk вне RFC порядка")]
    public void GetInfoOutOfOrderReconstructionChunkReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));
        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var outOfOrderWebp = BuildOutOfOrderVp8ThenVp8XWebp(vp8Payload, baseInfo.Width, baseInfo.Height);

        var result = codec.GetInfo(outOfOrderWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: анимированный WebP помечается как неподдерживаемый")]
    public void DecodeAnimatedWebpReturnsUnsupportedFormat()
    {
        var baseWebp = CreateLosslessFrameWebp(4, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var payload = ExtractChunkPayload(baseWebp, "VP8L");
        var animatedWebp = BuildAnimatedWebp(4, 1, 0x00000000, new AnimatedFrameSpec(0, 0, 4, 1, payload, IsLossless: true));

        codec!.InitializeDecoder(new ImageCodecParameters(4, 1, VideoPixelFormat.Rgb24));
        using var buffer = new VideoFrameBuffer(4, 1, VideoPixelFormat.Rgb24);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
    }

    [TestCase(TestName = "WebpCodec: single-frame animated WebP декодирует первый кадр")]
    public void DecodeSingleFrameAnimatedWebpSucceeds()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var animatedWebp = BuildSingleFrameAnimatedWebp(vp8Payload, baseInfo.Width, baseInfo.Height);

        codec.InitializeDecoder(new ImageCodecParameters(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var packed = buffer.AsReadOnlyFrame().PackedData;
        var sample = packed.GetRow(baseInfo.Height / 2).Slice((baseInfo.Width / 2) * 4, 4);
        Assert.That(sample[3], Is.EqualTo(255));
        Assert.That(sample[0] | sample[1] | sample[2], Is.GreaterThan((byte)0));
    }

    [TestCase(TestName = "WebpCodec: GetInfo возвращает canvas info для animated WebP")]
    public void GetInfoAnimatedWebpReturnsCanvasInfo()
    {
        var framePayload = CreateLosslessFramePayload(4, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var animatedWebp = BuildAnimatedWebp(4, 1, 0x00000000, new AnimatedFrameSpec(0, 0, 4, 1, framePayload, IsLossless: true));

        var result = codec!.GetInfo(animatedWebp, out var info);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(4));
        Assert.That(info.Height, Is.EqualTo(1));
    }

    [TestCase(TestName = "WebpCodec: animated WebP собирает multi-frame canvas с overwrite")]
    public void DecodeAnimatedWebpComposesMultipleFrames()
    {
        var firstFrameWebp = CreateLosslessFrameWebp(4, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var firstFrame = ExtractChunkPayload(firstFrameWebp, "VP8L");
        var firstRow = DecodeWebpRow(firstFrameWebp, 4, 1, VideoPixelFormat.Rgba32);
        var secondFrameWebp = CreateLosslessFrameWebp(2, 1, (_, _) => new Rgba32(0, 0, 255, 255));
        var secondFrame = ExtractChunkPayload(secondFrameWebp, "VP8L");
        var secondRow = DecodeWebpRow(secondFrameWebp, 2, 1, VideoPixelFormat.Rgba32);
        var animatedWebp = BuildAnimatedWebp(4, 1, 0x00000000,
            new AnimatedFrameSpec(0, 0, 4, 1, firstFrame, IsLossless: true, DoNotBlend: true),
            new AnimatedFrameSpec(2, 0, 2, 1, secondFrame, IsLossless: true, DoNotBlend: true));

        codec!.InitializeDecoder(new ImageCodecParameters(4, 1, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(4, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var row = buffer.AsReadOnlyFrame().PackedData.GetRow(0);
        AssertPixelMatches(row, 0, firstRow, 0);
        AssertPixelMatches(row, 1, firstRow, 1);
        AssertPixelMatches(row, 2, secondRow, 0);
        AssertPixelMatches(row, 3, secondRow, 1);
    }

    [TestCase(TestName = "WebpCodec: animated WebP применяет dispose-to-background перед следующим кадром")]
    public void DecodeAnimatedWebpDisposeToBackgroundClearsPreviousFrame()
    {
        var firstFrame = CreateLosslessFramePayload(2, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var secondFrameWebp = CreateLosslessFrameWebp(2, 1, (_, _) => new Rgba32(0, 0, 255, 255));
        var secondFrame = ExtractChunkPayload(secondFrameWebp, "VP8L");
        var secondRow = DecodeWebpRow(secondFrameWebp, 2, 1, VideoPixelFormat.Rgba32);
        var animatedWebp = BuildAnimatedWebp(4, 1, 0x00000000,
            new AnimatedFrameSpec(0, 0, 2, 1, firstFrame, IsLossless: true, DoNotBlend: true, DisposeToBackground: true),
            new AnimatedFrameSpec(2, 0, 2, 1, secondFrame, IsLossless: true, DoNotBlend: true));

        codec!.InitializeDecoder(new ImageCodecParameters(4, 1, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(4, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var row = buffer.AsReadOnlyFrame().PackedData.GetRow(0);
        AssertPixel(row, 0, 0, 0, 0, 0);
        AssertPixel(row, 1, 0, 0, 0, 0);
        AssertPixelMatches(row, 2, secondRow, 0);
        AssertPixelMatches(row, 3, secondRow, 1);
    }

    [TestCase(TestName = "WebpCodec: animated WebP alpha-blend смешивает кадры на canvas")]
    public void DecodeAnimatedWebpBlendsFrames()
    {
        var firstFrameWebp = CreateLosslessFrameWebp(4, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var firstFrame = ExtractChunkPayload(firstFrameWebp, "VP8L");
        var firstRow = DecodeWebpRow(firstFrameWebp, 4, 1, VideoPixelFormat.Rgba32);
        var secondFrameWebp = CreateLosslessFrameWebp(2, 1, (_, _) => new Rgba32(0, 0, 255, 128));
        var secondFrame = ExtractChunkPayload(secondFrameWebp, "VP8L");
        var secondRow = DecodeWebpRow(secondFrameWebp, 2, 1, VideoPixelFormat.Rgba32);
        var animatedWebp = BuildAnimatedWebp(4, 1, 0x00000000,
            new AnimatedFrameSpec(0, 0, 4, 1, firstFrame, IsLossless: true, DoNotBlend: true),
            new AnimatedFrameSpec(2, 0, 2, 1, secondFrame, IsLossless: true, DoNotBlend: false));

        codec!.InitializeDecoder(new ImageCodecParameters(4, 1, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(4, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var row = buffer.AsReadOnlyFrame().PackedData.GetRow(0);
        AssertPixelMatches(row, 0, firstRow, 0);
        AssertPixelMatches(row, 1, firstRow, 1);
        AssertPixel(row, 2, BlendChannel(firstRow[(2 * 4)], firstRow[(2 * 4) + 3], secondRow[0], secondRow[3]),
            BlendChannel(firstRow[(2 * 4) + 1], firstRow[(2 * 4) + 3], secondRow[1], secondRow[3]),
            BlendChannel(firstRow[(2 * 4) + 2], firstRow[(2 * 4) + 3], secondRow[2], secondRow[3]),
            BlendAlpha(firstRow[(2 * 4) + 3], secondRow[3]));
        AssertPixel(row, 3, BlendChannel(firstRow[(3 * 4)], firstRow[(3 * 4) + 3], secondRow[4], secondRow[7]),
            BlendChannel(firstRow[(3 * 4) + 1], firstRow[(3 * 4) + 3], secondRow[5], secondRow[7]),
            BlendChannel(firstRow[(3 * 4) + 2], firstRow[(3 * 4) + 3], secondRow[6], secondRow[7]),
            BlendAlpha(firstRow[(3 * 4) + 3], secondRow[7]));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF за пределами canvas отклоняется строгим парсером")]
    public void GetInfoAnimatedFrameOutsideCanvasReturnsInvalidData()
    {
        var framePayload = CreateLosslessFramePayload(2, 1, (_, _) => new Rgba32(255, 0, 0, 255));
        var animatedWebp = BuildAnimatedWebp(2, 1, 0x00000000, new AnimatedFrameSpec(2, 0, 2, 1, framePayload, IsLossless: true));

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated lossy ANMF+ALPH применяет alpha к canvas")]
    public void DecodeAnimatedLossyFrameWithAlphaChunkAppliesAlpha()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateRawAlphaPayload(baseInfo.Width, baseInfo.Height, (x, _) => x < baseInfo.Width / 2 ? (byte)40 : (byte)200);
        var animatedWebp = BuildAnimatedWebp(baseInfo.Width, baseInfo.Height, 0x00000000,
            includeAlphaFlag: true,
            new AnimatedFrameSpec(0, 0, baseInfo.Width, baseInfo.Height, vp8Payload, IsLossless: false, AlphaPayload: alphaPayload));

        codec.InitializeDecoder(new ImageCodecParameters(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(animatedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var packed = buffer.AsReadOnlyFrame().PackedData;
        var leftAlpha = packed.GetRow(baseInfo.Height / 2)[((baseInfo.Width / 4) * 4) + 3];
        var rightAlpha = packed.GetRow(baseInfo.Height / 2)[(((baseInfo.Width * 3) / 4) * 4) + 3];
        Assert.That(leftAlpha, Is.EqualTo(40));
        Assert.That(rightAlpha, Is.EqualTo(200));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF с alpha требует VP8X alpha flag")]
    public void GetInfoAnimatedAlphaWithoutVp8xAlphaFlagReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateRawAlphaPayload(baseInfo.Width, baseInfo.Height, (_, _) => (byte)128);
        var animatedWebp = BuildAnimatedWebp(baseInfo.Width, baseInfo.Height, 0x00000000,
            includeAlphaFlag: false,
            new AnimatedFrameSpec(0, 0, baseInfo.Width, baseInfo.Height, vp8Payload, IsLossless: false, AlphaPayload: alphaPayload));

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF отклоняет unknown chunk до bitstream")]
    public void GetInfoAnimatedUnknownChunkBeforeBitstreamReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var framePayload = ExtractChunkPayload(baseWebp, "VP8 ");
        var animatedWebp = BuildAnimatedWebp(baseInfo.Width, baseInfo.Height, 0x00000000,
            includeAlphaFlag: false,
            new AnimatedFrameSpec(0, 0, baseInfo.Width, baseInfo.Height, framePayload, IsLossless: false,
                UnknownChunksBeforeImage: [new WebpChunkSpec("ZZZZ", [0x2A])]));

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF допускает trailing unknown chunk после bitstream")]
    public void GetInfoAnimatedUnknownChunkAfterBitstreamSucceeds()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var framePayload = ExtractChunkPayload(baseWebp, "VP8 ");
        var animatedWebp = BuildAnimatedWebp(baseInfo.Width, baseInfo.Height, 0x00000000,
            includeAlphaFlag: false,
            new AnimatedFrameSpec(0, 0, baseInfo.Width, baseInfo.Height, framePayload, IsLossless: false,
                UnknownChunksAfterImage: [new WebpChunkSpec("ZZZZ", [0x2A])]));

        var result = codec!.GetInfo(animatedWebp, out var info);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(baseInfo.Width));
        Assert.That(info.Height, Is.EqualTo(baseInfo.Height));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF отклоняет duplicate ALPH subchunks")]
    public void GetInfoAnimatedDuplicateAlphaChunkReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateRawAlphaPayload(baseInfo.Width, baseInfo.Height, (_, _) => (byte)128);
        var framePayload = BuildAnimatedFramePayloadCustom(baseInfo.Width, baseInfo.Height,
            new WebpChunkSpec("ALPH", alphaPayload),
            new WebpChunkSpec("ALPH", alphaPayload),
            new WebpChunkSpec("VP8 ", vp8Payload));
        var animatedWebp = BuildAnimatedWebpFromFramePayloads(baseInfo.Width, baseInfo.Height, 0x00000000, includeAlphaFlag: true, framePayload);

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF отклоняет ALPH после bitstream")]
    public void GetInfoAnimatedAlphaAfterBitstreamReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateRawAlphaPayload(baseInfo.Width, baseInfo.Height, (_, _) => (byte)128);
        var framePayload = BuildAnimatedFramePayloadCustom(baseInfo.Width, baseInfo.Height,
            new WebpChunkSpec("VP8 ", vp8Payload),
            new WebpChunkSpec("ALPH", alphaPayload));
        var animatedWebp = BuildAnimatedWebpFromFramePayloads(baseInfo.Width, baseInfo.Height, 0x00000000, includeAlphaFlag: true, framePayload);

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF отклоняет duplicate bitstream subchunks")]
    public void GetInfoAnimatedDuplicateBitstreamReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var framePayload = BuildAnimatedFramePayloadCustom(baseInfo.Width, baseInfo.Height,
            new WebpChunkSpec("VP8 ", vp8Payload),
            new WebpChunkSpec("VP8 ", vp8Payload));
        var animatedWebp = BuildAnimatedWebpFromFramePayloads(baseInfo.Width, baseInfo.Height, 0x00000000, includeAlphaFlag: false, framePayload);

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: animated ANMF отклоняет смешанные VP8 и VP8L в одном frame")]
    public void GetInfoAnimatedMixedVp8AndVp8LReturnsInvalidData()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var vp8lPayload = CreateLosslessFramePayload(baseInfo.Width, baseInfo.Height, (_, _) => new Rgba32(255, 0, 0, 255));
        var framePayload = BuildAnimatedFramePayloadCustom(baseInfo.Width, baseInfo.Height,
            new WebpChunkSpec("VP8 ", vp8Payload),
            new WebpChunkSpec("VP8L", vp8lPayload));
        var animatedWebp = BuildAnimatedWebpFromFramePayloads(baseInfo.Width, baseInfo.Height, 0x00000000, includeAlphaFlag: false, framePayload);

        var result = codec!.GetInfo(animatedWebp, out _);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "WebpCodec: extended VP8X+ALPH raw применяет alpha к lossy кадру")]
    public void DecodeExtendedLossyRawAlphaAppliesAlpha()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateRawAlphaPayload(baseInfo.Width, baseInfo.Height, (x, y) => x < baseInfo.Width / 2 ? (byte)32 : (byte)224);
        var extendedWebp = BuildExtendedStillWebp(vp8Payload, baseInfo.Width, baseInfo.Height, alphaPayload, alphaFlag: true);

        codec.InitializeDecoder(new ImageCodecParameters(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(extendedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var packed = buffer.AsReadOnlyFrame().PackedData;
        var leftAlpha = packed.GetRow(baseInfo.Height / 2)[((baseInfo.Width / 4) * 4) + 3];
        var rightAlpha = packed.GetRow(baseInfo.Height / 2)[(((baseInfo.Width * 3) / 4) * 4) + 3];

        Assert.That(leftAlpha, Is.EqualTo(32));
        Assert.That(rightAlpha, Is.EqualTo(224));
    }

    [TestCase(TestName = "WebpCodec: extended VP8X+ALPH VP8L image-stream применяет alpha к lossy кадру")]
    public void DecodeExtendedLossyCompressedAlphaAppliesAlpha()
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateCompressedAlphaPayload(baseInfo.Width, baseInfo.Height, (x, y) => y < baseInfo.Height / 2 ? (byte)48 : (byte)208);
        var extendedWebp = BuildExtendedStillWebp(vp8Payload, baseInfo.Width, baseInfo.Height, alphaPayload, alphaFlag: true);

        codec.InitializeDecoder(new ImageCodecParameters(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(extendedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var packed = buffer.AsReadOnlyFrame().PackedData;
        var topAlpha = packed.GetRow(baseInfo.Height / 4)[((baseInfo.Width / 2) * 4) + 3];
        var bottomAlpha = packed.GetRow((baseInfo.Height * 3) / 4)[((baseInfo.Width / 2) * 4) + 3];

        Assert.That(topAlpha, Is.EqualTo(48));
        Assert.That(bottomAlpha, Is.EqualTo(208));
    }

    [TestCase(1, TestName = "WebpCodec: ALPH horizontal filter восстанавливает alpha корректно")]
    [TestCase(2, TestName = "WebpCodec: ALPH vertical filter восстанавливает alpha корректно")]
    [TestCase(3, TestName = "WebpCodec: ALPH gradient filter восстанавливает alpha корректно")]
    public void DecodeExtendedLossyFilteredAlphaAppliesExpectedValues(int filter)
    {
        var baseWebp = File.ReadAllBytes(Path.Combine("assets", "test.webp"));
        var baseInfoResult = codec!.GetInfo(baseWebp, out var baseInfo);
        Assert.That(baseInfoResult, Is.EqualTo(CodecResult.Success));

        var vp8Payload = ExtractChunkPayload(baseWebp, "VP8 ");
        var alphaPayload = CreateFilteredRawAlphaPayload(baseInfo.Width, baseInfo.Height, filter, GetFilterTestAlpha);
        var extendedWebp = BuildExtendedStillWebp(vp8Payload, baseInfo.Width, baseInfo.Height, alphaPayload, alphaFlag: true);

        codec.InitializeDecoder(new ImageCodecParameters(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(baseInfo.Width, baseInfo.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(extendedWebp, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var decodedFrame = buffer.AsReadOnlyFrame();
        AssertDecodedAlpha(decodedFrame, 0, 0, baseInfo.Width, GetFilterTestAlpha(0, 0));
        AssertDecodedAlpha(decodedFrame, 1, 0, baseInfo.Width, GetFilterTestAlpha(1, 0));
        AssertDecodedAlpha(decodedFrame, 0, 1, baseInfo.Width, GetFilterTestAlpha(0, 1));
        AssertDecodedAlpha(decodedFrame, 1, 1, baseInfo.Width, GetFilterTestAlpha(1, 1));
        AssertDecodedAlpha(decodedFrame, baseInfo.Width / 2, baseInfo.Height / 2, baseInfo.Width, GetFilterTestAlpha(baseInfo.Width / 2, baseInfo.Height / 2));
        AssertDecodedAlpha(decodedFrame, baseInfo.Width - 1, baseInfo.Height - 1, baseInfo.Width, GetFilterTestAlpha(baseInfo.Width - 1, baseInfo.Height - 1));
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

    private static byte[] ExtractChunkPayload(ReadOnlySpan<byte> webpData, string fourCc)
    {
        var fourCcBytes = System.Text.Encoding.ASCII.GetBytes(fourCc);
        for (var offset = 12; offset + 8 <= webpData.Length;)
        {
            var chunkType = webpData.Slice(offset, 4);
            var chunkSize = BitConverter.ToInt32(webpData.Slice(offset + 4, 4));
            if (chunkType.SequenceEqual(fourCcBytes))
            {
                return webpData.Slice(offset + 8, chunkSize).ToArray();
            }

            offset += 8 + ((chunkSize + 1) & ~1);
        }

        Assert.Fail($"Chunk '{fourCc}' не найден в тестовом WebP");
        return [];
    }

    private static byte[] BuildExtendedStillWebp(byte[] vp8Payload, int width, int height, bool includeAlphaChunk, bool alphaFlag)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = alphaFlag ? (byte)0x10 : (byte)0x00;
        WriteUint24(vp8x[4..7], width - 1);
        WriteUint24(vp8x[7..10], height - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());

        if (includeAlphaChunk)
        {
            WriteChunk(writer, "ALPH", [0x00]);
        }

        WriteChunk(writer, "VP8 ", vp8Payload);
        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildExtendedStillWebp(byte[] vp8Payload, int width, int height, byte[] alphaPayload, bool alphaFlag)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = alphaFlag ? (byte)0x10 : (byte)0x00;
        WriteUint24(vp8x[4..7], width - 1);
        WriteUint24(vp8x[7..10], height - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());
        WriteChunk(writer, "ALPH", alphaPayload);
        WriteChunk(writer, "VP8 ", vp8Payload);

        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildOutOfOrderVp8ThenVp8XWebp(byte[] vp8Payload, int width, int height)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());
        WriteChunk(writer, "VP8 ", vp8Payload);

        Span<byte> vp8x = stackalloc byte[10];
        WriteUint24(vp8x[4..7], width - 1);
        WriteUint24(vp8x[7..10], height - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());

        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildAnimatedWebp(byte[] vp8Payload, int width, int height, int frameCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = 0x02;
        WriteUint24(vp8x[4..7], width - 1);
        WriteUint24(vp8x[7..10], height - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());
        WriteChunk(writer, "ANIM", [0x00, 0x00, 0x00, 0x00, 0x01, 0x00]);

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            WriteChunk(writer, "ANMF", BuildAnimatedFramePayload(vp8Payload, width, height));
        }

        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildAnimatedWebp(int canvasWidth, int canvasHeight, uint backgroundColor, params AnimatedFrameSpec[] frames)
        => BuildAnimatedWebp(canvasWidth, canvasHeight, backgroundColor, includeAlphaFlag: true, frames);

    private static byte[] BuildAnimatedWebp(int canvasWidth, int canvasHeight, uint backgroundColor, bool includeAlphaFlag, params AnimatedFrameSpec[] frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = (byte)(0x02 | (includeAlphaFlag ? 0x10 : 0x00));
        WriteUint24(vp8x[4..7], canvasWidth - 1);
        WriteUint24(vp8x[7..10], canvasHeight - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());

        Span<byte> anim = stackalloc byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(anim[..4], backgroundColor);
        anim[4] = 1;
        anim[5] = 0;
        WriteChunk(writer, "ANIM", anim.ToArray());

        foreach (var frame in frames)
        {
            WriteChunk(writer, "ANMF", BuildAnimatedFramePayload(frame));
        }

        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildSingleFrameAnimatedWebp(byte[] vp8Payload, int width, int height)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = 0x02;
        WriteUint24(vp8x[4..7], width - 1);
        WriteUint24(vp8x[7..10], height - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());
        WriteChunk(writer, "ANIM", [0x00, 0x00, 0x00, 0x00, 0x01, 0x00]);

        WriteChunk(writer, "ANMF", BuildAnimatedFramePayload(vp8Payload, width, height));
        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildAnimatedWebpFromFramePayloads(int canvasWidth, int canvasHeight, uint backgroundColor, bool includeAlphaFlag, params byte[][] framePayloads)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WEBP"u8.ToArray());

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = (byte)(0x02 | (includeAlphaFlag ? 0x10 : 0x00));
        WriteUint24(vp8x[4..7], canvasWidth - 1);
        WriteUint24(vp8x[7..10], canvasHeight - 1);
        WriteChunk(writer, "VP8X", vp8x.ToArray());
        WriteChunk(writer, "ANIM", [0x00, 0x00, 0x00, 0x00, 0x01, 0x00]);

        foreach (var framePayload in framePayloads)
        {
            WriteChunk(writer, "ANMF", framePayload);
        }

        FinalizeRiffSize(stream);
        return stream.ToArray();
    }

    private static byte[] BuildAnimatedFramePayload(byte[] vp8Payload, int width, int height)
    {
        using var frameStream = new MemoryStream();
        using var frameWriter = new BinaryWriter(frameStream, System.Text.Encoding.ASCII, leaveOpen: true);
        frameWriter.Write(new byte[16]);
        frameStream.Position = 0;
        WriteUint24(frameStream.GetBuffer().AsSpan(6, 3), width - 1);
        WriteUint24(frameStream.GetBuffer().AsSpan(9, 3), height - 1);
        frameStream.Position = frameStream.Length;
        WriteChunk(frameWriter, "VP8 ", vp8Payload);
        return frameStream.ToArray();
    }

    private static byte[] BuildAnimatedFramePayloadCustom(int width, int height, params WebpChunkSpec[] chunks)
    {
        using var frameStream = new MemoryStream();
        using var frameWriter = new BinaryWriter(frameStream, System.Text.Encoding.ASCII, leaveOpen: true);
        frameWriter.Write(new byte[16]);
        frameStream.Position = 0;
        WriteUint24(frameStream.GetBuffer().AsSpan(6, 3), width - 1);
        WriteUint24(frameStream.GetBuffer().AsSpan(9, 3), height - 1);
        frameStream.Position = 16;

        foreach (var chunk in chunks)
        {
            WriteChunk(frameWriter, chunk.FourCc, chunk.Payload);
        }

        return frameStream.ToArray();
    }

    private static byte[] BuildAnimatedFramePayload(AnimatedFrameSpec frame)
    {
        using var frameStream = new MemoryStream();
        using var frameWriter = new BinaryWriter(frameStream, System.Text.Encoding.ASCII, leaveOpen: true);
        frameWriter.Write(new byte[16]);
        var buffer = frameStream.GetBuffer();
        WriteUint24(buffer.AsSpan(0, 3), frame.X / 2);
        WriteUint24(buffer.AsSpan(3, 3), frame.Y / 2);
        WriteUint24(buffer.AsSpan(6, 3), frame.Width - 1);
        WriteUint24(buffer.AsSpan(9, 3), frame.Height - 1);
        WriteUint24(buffer.AsSpan(12, 3), frame.Duration);
        buffer[15] = (byte)((frame.DoNotBlend ? 0x02 : 0x00) | (frame.DisposeToBackground ? 0x01 : 0x00));
        frameStream.Position = 16;
        if (frame.AlphaPayload is not null)
        {
            WriteChunk(frameWriter, "ALPH", frame.AlphaPayload);
        }

        foreach (var unknownChunk in frame.UnknownChunksBeforeImage ?? [])
        {
            WriteChunk(frameWriter, unknownChunk.FourCc, unknownChunk.Payload);
        }

        WriteChunk(frameWriter, frame.IsLossless ? "VP8L" : "VP8 ", frame.Payload);

        foreach (var unknownChunk in frame.UnknownChunksAfterImage ?? [])
        {
            WriteChunk(frameWriter, unknownChunk.FourCc, unknownChunk.Payload);
        }

        return frameStream.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string fourCc, byte[] payload)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        writer.Write(payload.Length);
        writer.Write(payload);

        if ((payload.Length & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void FinalizeRiffSize(MemoryStream stream)
    {
        var size = checked((int)stream.Length - 8);
        var originalPosition = stream.Position;
        stream.Position = 4;
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(size);
        stream.Position = originalPosition;
    }

    private static void WriteUint24(Span<byte> destination, int value)
    {
        destination[0] = (byte)(value & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)((value >> 16) & 0xFF);
    }

    private static byte[] CreateRawAlphaPayload(int width, int height, Func<int, int, byte> getAlpha)
    {
        var payload = new byte[(width * height) + 1];
        payload[0] = 0x00;

        var index = 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                payload[index++] = getAlpha(x, y);
            }
        }

        return payload;
    }

    private static byte[] CreateFilteredRawAlphaPayload(int width, int height, int filter, Func<int, int, byte> getAlpha)
    {
        var payload = new byte[(width * height) + 1];
        payload[0] = (byte)(filter << 2);
        var decodedAlpha = new byte[width * height];

        var index = 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var actualAlpha = getAlpha(x, y);
                var predictor = GetAlphaPredictor(decodedAlpha, width, x, y, filter);
                payload[index++] = unchecked((byte)(actualAlpha - predictor));
                decodedAlpha[(y * width) + x] = actualAlpha;
            }
        }

        return payload;
    }

    private static byte GetFilterTestAlpha(int x, int y) => (byte)((17 + (x * 13) + (y * 29)) & 0xFF);

    private static void AssertDecodedAlpha(in ReadOnlyVideoFrame frame, int x, int y, int width, byte expectedAlpha)
    {
        var actualAlpha = frame.PackedData.GetRow(y)[(x * 4) + 3];
        Assert.That(actualAlpha, Is.EqualTo(expectedAlpha), $"Alpha mismatch at ({x}, {y}) for width {width}");
    }

    private static byte GetAlphaPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y, int filter)
    {
        return filter switch
        {
            1 => GetHorizontalPredictor(alphaValues, width, x, y),
            2 => GetVerticalPredictor(alphaValues, width, x, y),
            3 => GetGradientPredictor(alphaValues, width, x, y),
            _ => 0
        };
    }

    private static byte GetHorizontalPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (x == 0)
        {
            return y == 0 ? (byte)0 : alphaValues[(y - 1) * width];
        }

        return alphaValues[(y * width) + x - 1];
    }

    private static byte GetVerticalPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (y == 0)
        {
            return x == 0 ? (byte)0 : alphaValues[x - 1];
        }

        return alphaValues[((y - 1) * width) + x];
    }

    private static byte GetGradientPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (x == 0 && y == 0)
        {
            return 0;
        }

        if (x == 0)
        {
            return alphaValues[((y - 1) * width) + x];
        }

        if (y == 0)
        {
            return alphaValues[(y * width) + x - 1];
        }

        var left = alphaValues[(y * width) + x - 1];
        var top = alphaValues[((y - 1) * width) + x];
        var topLeft = alphaValues[((y - 1) * width) + x - 1];
        return ClampToByte(left + top - topLeft);
    }

    private static byte ClampToByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 255)
        {
            return 255;
        }

        return (byte)value;
    }

    private static byte[] CreateCompressedAlphaPayload(int width, int height, Func<int, int, byte> getAlpha)
    {
        using var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        var packed = frame.PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = packed.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var offset = x * 4;
                var alpha = getAlpha(x, y);
                row[offset] = 0;
                row[offset + 1] = alpha;
                row[offset + 2] = 0;
                row[offset + 3] = 255;
            }
        }

        using var webpCodec = new WebpCodec();
        webpCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
        var encoded = new byte[webpCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var encodeResult = webpCodec.Encode(buffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));

        var vp8lPayload = ExtractChunkPayload(encoded.AsSpan(0, bytesWritten), "VP8L");
        var alphaPayload = new byte[1 + vp8lPayload.Length - 5];
        alphaPayload[0] = 0x01;
        vp8lPayload.AsSpan(5).CopyTo(alphaPayload.AsSpan(1));
        return alphaPayload;
    }

    private static byte[] CreateLosslessFramePayload(int width, int height, Func<int, int, Rgba32> getPixel)
    {
        var webp = CreateLosslessFrameWebp(width, height, getPixel);
        return ExtractChunkPayload(webp, "VP8L");
    }

    private static byte[] CreateLosslessFrameWebp(int width, int height, Func<int, int, Rgba32> getPixel)
    {
        using var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();
        var packed = frame.PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = packed.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var pixel = getPixel(x, y);
                var offset = x * 4;
                row[offset] = pixel.R;
                row[offset + 1] = pixel.G;
                row[offset + 2] = pixel.B;
                row[offset + 3] = pixel.A;
            }
        }

        using var webpCodec = new WebpCodec();
        webpCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
        var encoded = new byte[webpCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32)];
        var encodeResult = webpCodec.Encode(buffer.AsReadOnlyFrame(), encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));
        return encoded.AsSpan(0, bytesWritten).ToArray();
    }

    private static byte[] DecodeWebpRow(byte[] webp, int width, int height, VideoPixelFormat pixelFormat)
    {
        using var decodeCodec = new WebpCodec();
        decodeCodec.InitializeDecoder(new ImageCodecParameters(width, height, pixelFormat));
        using var buffer = new VideoFrameBuffer(width, height, pixelFormat);
        var frame = buffer.AsFrame();
        var result = decodeCodec.Decode(webp, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        return buffer.AsReadOnlyFrame().PackedData.GetRow(0).ToArray();
    }

    private static void AssertPixel(ReadOnlySpan<byte> row, int x, byte red, byte green, byte blue, byte alpha)
    {
        var offset = x * 4;
        Assert.That(row[offset], Is.EqualTo(red));
        Assert.That(row[offset + 1], Is.EqualTo(green));
        Assert.That(row[offset + 2], Is.EqualTo(blue));
        Assert.That(row[offset + 3], Is.EqualTo(alpha));
    }

    private static void AssertPixelMatches(ReadOnlySpan<byte> actualRow, int actualX, ReadOnlySpan<byte> expectedRow, int expectedX)
    {
        var actualOffset = actualX * 4;
        var expectedOffset = expectedX * 4;
        Assert.That(actualRow[actualOffset], Is.EqualTo(expectedRow[expectedOffset]));
        Assert.That(actualRow[actualOffset + 1], Is.EqualTo(expectedRow[expectedOffset + 1]));
        Assert.That(actualRow[actualOffset + 2], Is.EqualTo(expectedRow[expectedOffset + 2]));
        Assert.That(actualRow[actualOffset + 3], Is.EqualTo(expectedRow[expectedOffset + 3]));
    }

    private static byte BlendAlpha(byte destinationAlpha, byte sourceAlpha) => (byte)(sourceAlpha + ((destinationAlpha * (255 - sourceAlpha)) / 255));

    private static byte BlendChannel(byte destination, byte destinationAlpha, byte source, byte sourceAlpha)
    {
        var outAlpha = BlendAlpha(destinationAlpha, sourceAlpha);
        if (outAlpha == 0)
        {
            return 0;
        }

        return (byte)(((source * sourceAlpha) + (destination * destinationAlpha * (255 - sourceAlpha) / 255)) / outAlpha);
    }

    private readonly record struct AnimatedFrameSpec(
        int X,
        int Y,
        int Width,
        int Height,
        byte[] Payload,
        bool IsLossless,
        int Duration = 0,
        bool DoNotBlend = true,
        bool DisposeToBackground = false,
        byte[]? AlphaPayload = null,
        WebpChunkSpec[]? UnknownChunksBeforeImage = null,
        WebpChunkSpec[]? UnknownChunksAfterImage = null);

    private readonly record struct WebpChunkSpec(string FourCc, byte[] Payload);

    #endregion
}
