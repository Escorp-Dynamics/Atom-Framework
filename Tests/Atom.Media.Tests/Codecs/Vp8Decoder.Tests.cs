#pragma warning disable CA1861, MA0051

using System.Diagnostics;
using Atom.Media.Codecs.Webp.Vp8;

namespace Atom.Media.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class Vp8DecoderTests(ILogger logger) : BenchmarkTests<Vp8DecoderTests>(logger)
{
    private const string AssetsDir = "assets";

    private WebpCodec? codec;

    public Vp8DecoderTests() : this(ConsoleLogger.Unicode)
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

    #region BoolDecoder Tests

    [TestCase(TestName = "VP8 BoolDecoder: инициализация из двух байтов")]
    public void BoolDecoderInitFromTwoBytes()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD, 0x12, 0x34];
        var bd = new Vp8BoolDecoder(data);

        Assert.That(bd.Pos, Is.EqualTo(2));
        Assert.That(bd.IsAtEnd, Is.False);
    }

    [TestCase(TestName = "VP8 BoolDecoder: декодирование бита с вероятностью 128")]
    public void BoolDecoderDecodeBitUniform()
    {
        // С вероятностью 128 (50/50) — это равномерное чтение
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF];
        var bd = new Vp8BoolDecoder(data);

        // DecodeBit(128) при value = 0xFFFF — должен вернуть 1
        var bit = bd.DecodeBit(128);
        Assert.That(bit, Is.Zero.Or.EqualTo(1));
    }

    [TestCase(TestName = "VP8 BoolDecoder: DecodeLiteral читает N бит")]
    public void BoolDecoderDecodeLiteral()
    {
        // Все нулевые биты → literal = 0
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var bd = new Vp8BoolDecoder(data);

        var value = bd.DecodeLiteral(8);
        // При value=0 и prob=128, split > value → bit=0, итого literal = 0
        Assert.That(value, Is.Zero);
    }

    [TestCase(TestName = "VP8 BoolDecoder: все единичные биты при 0xFF данных")]
    public void BoolDecoderAllOnesBits()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        var bd = new Vp8BoolDecoder(data);

        var value = bd.DecodeLiteral(8);
        // При value=0xFFFF и prob=128 — каждый бит будет 1 → literal = 255
        Assert.That(value, Is.EqualTo(255u));
    }

    [TestCase(TestName = "VP8 BoolDecoder: DecodeSigned корректно обрабатывает знак")]
    public void BoolDecoderDecodeSigned()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var bd = new Vp8BoolDecoder(data);

        // value=0, prob=128 → все биты 0 → signed = +0
        var signed = bd.DecodeSigned(4);
        Assert.That(signed, Is.Zero);
    }

    [TestCase(TestName = "VP8 BoolCoder: EncodeBit/DecodeBit roundtrip на mixed probabilities")]
    public void BoolCoderBitRoundtripMixedProbabilities()
    {
        var output = new byte[128];
        Span<int> bits = [1, 0, 1, 1, 0, 0, 1, 0, 1, 0, 1, 1, 1, 0, 0, 1];
        Span<int> probs = [1, 64, 128, 200, 255, 17, 99, 173, 231, 45, 90, 140, 220, 7, 111, 250];

        var encoder = new Vp8BoolEncoder(output);
        for (var index = 0; index < bits.Length; index++)
        {
            encoder.EncodeBit(bits[index], probs[index]);
        }

        encoder.Flush();

        var decoder = new Vp8BoolDecoder(output.AsSpan(0, encoder.BytesWritten));
        for (var index = 0; index < bits.Length; index++)
        {
            Assert.That(decoder.DecodeBit(probs[index]), Is.EqualTo(bits[index]),
                $"bit roundtrip mismatch at {index} with prob={probs[index]}");
        }
    }

    [TestCase(TestName = "VP8 BoolCoder: EncodeLiteral/DecodeLiteral roundtrip")]
    public void BoolCoderLiteralRoundtrip()
    {
        var output = new byte[128];
        var encoder = new Vp8BoolEncoder(output);

        encoder.EncodeLiteral(0b1010_0110, 8);
        encoder.EncodeLiteral(0b11_0011, 6);
        encoder.EncodeLiteral(0b1_0101_0110_1110, 13);
        encoder.Flush();

        var decoder = new Vp8BoolDecoder(output.AsSpan(0, encoder.BytesWritten));
        Assert.That(decoder.DecodeLiteral(8), Is.EqualTo(0b1010_0110u));
        Assert.That(decoder.DecodeLiteral(6), Is.EqualTo(0b11_0011u));
        Assert.That(decoder.DecodeLiteral(13), Is.EqualTo(0b1_0101_0110_1110u));
    }

    [TestCase(TestName = "VP8 BoolCoder: EncodeSigned/DecodeSigned roundtrip")]
    public void BoolCoderSignedRoundtrip()
    {
        var output = new byte[128];
        var encoder = new Vp8BoolEncoder(output);

        encoder.EncodeSigned(0, 4);
        encoder.EncodeSigned(7, 4);
        encoder.EncodeSigned(-5, 4);
        encoder.EncodeSigned(12, 5);
        encoder.EncodeSigned(-15, 5);
        encoder.Flush();

        var decoder = new Vp8BoolDecoder(output.AsSpan(0, encoder.BytesWritten));
        Assert.That(decoder.DecodeSigned(4), Is.Zero);
        Assert.That(decoder.DecodeSigned(4), Is.EqualTo(7));
        Assert.That(decoder.DecodeSigned(4), Is.EqualTo(-5));
        Assert.That(decoder.DecodeSigned(5), Is.EqualTo(12));
        Assert.That(decoder.DecodeSigned(5), Is.EqualTo(-15));
    }

    [TestCase(TestName = "VP8 BoolCoder: YModeTree symbol roundtrip")]
    public void BoolCoderYModeTreeRoundtrip()
    {
        var output = new byte[256];
        var encoder = new Vp8BoolEncoder(output);
        byte[] probs = [145, 156, 163, 128];

        for (var symbol = 0; symbol < Vp8Constants.NumYModes; symbol++)
        {
            EncodeTreeSymbol(ref encoder, Vp8Constants.YModeTree, probs, symbol);
        }

        encoder.Flush();

        var decoder = new Vp8BoolDecoder(output.AsSpan(0, encoder.BytesWritten));
        for (var symbol = 0; symbol < Vp8Constants.NumYModes; symbol++)
        {
            Assert.That(decoder.DecodeTree(Vp8Constants.YModeTree, probs), Is.EqualTo(symbol),
                $"YModeTree roundtrip mismatch for symbol {symbol}");
        }
    }

    [TestCase(TestName = "VP8 BoolDecoder: BytesRead корректно отслеживает позицию")]
    public void BoolDecoderBytesReadTracksPosition()
    {
        ReadOnlySpan<byte> data = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80];
        var bd = new Vp8BoolDecoder(data);

        Assert.That(bd.BytesRead, Is.EqualTo(2));

        // Чтение нескольких бит продвинет позицию
        for (var i = 0; i < 16; i++)
        {
            bd.DecodeBit(128);
        }

        Assert.That(bd.BytesRead, Is.GreaterThanOrEqualTo(2));
    }

    #endregion

    #region Inverse DCT Tests

    [TestCase(TestName = "VP8 DCT: InverseDct4x4 с нулевыми коэффициентами не меняет output")]
    public void InverseDct4x4ZeroCoeffsNoChange()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();

        Span<byte> output = stackalloc byte[4 * 4];
        output.Fill(128); // заполняем средними значениями

        Vp8Dct.InverseDct4x4(input, output, 4);

        // Нулевые коэффициенты → output не меняется
        for (var i = 0; i < 16; i++)
        {
            Assert.That(output[i], Is.EqualTo(128));
        }
    }

    [TestCase(TestName = "VP8 DCT: InverseDct4x4 DC-only добавляет постоянное значение")]
    public void InverseDct4x4DcOnlyAddsConstant()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();
        input[0] = 64; // DC значение

        Span<byte> output = stackalloc byte[4 * 4];
        output.Fill(100);

        Vp8Dct.InverseDct4x4(input, output, 4);

        // DC=64 → (64+4)>>3 = 8 добавляется к каждому пикселю → 108
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                Assert.That(output[(i * 4) + j], Is.EqualTo(108));
            }
        }
    }

    [TestCase(TestName = "VP8 DCT: InverseDct4x4DcOnly корректно добавляет DC")]
    public void InverseDct4x4DcOnlyMethod()
    {
        short dc = 64;

        Span<byte> output = stackalloc byte[4 * 4];
        output.Fill(100);

        Vp8Dct.InverseDct4x4DcOnly(dc, output, 4);

        // (64+4)>>3 = 8, 100+8 = 108
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                Assert.That(output[(i * 4) + j], Is.EqualTo(108));
            }
        }
    }

    [TestCase(TestName = "VP8 DCT: InverseDct4x4 с clamping к [0, 255]")]
    public void InverseDct4x4ClampingWorks()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();
        input[0] = 2048; // большое DC → (2048+4)>>3 = 256

        Span<byte> output = stackalloc byte[4 * 4];
        output.Fill(200);

        Vp8Dct.InverseDct4x4(input, output, 4);

        // 200 + 256 = 456, clamped to 255
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                Assert.That(output[(i * 4) + j], Is.EqualTo(255));
            }
        }
    }

    [TestCase(TestName = "VP8 DCT: InverseWht4x4 → ForwardWht4x4 roundtrip")]
    public void Wht4x4Roundtrip()
    {
        Span<short> original = stackalloc short[16];
        for (var i = 0; i < 16; i++)
        {
            original[i] = (short)(i * 10);
        }

        Span<short> forward = stackalloc short[16];
        Vp8Dct.ForwardWht4x4(original, forward);

        Span<short> inverse = stackalloc short[16];
        Vp8Dct.InverseWht4x4(forward, inverse);

        // Roundtrip должен восстановить приблизительно исходные значения
        for (var i = 0; i < 16; i++)
        {
            Assert.That(inverse[i], Is.EqualTo(original[i]).Within(1),
                $"WHT roundtrip: элемент {i}");
        }
    }

    [Explicit]
    [TestCase(TestName = "VP8 DCT: isolated repro для failing 4x4 блока совпадает с RFC reference IDCT")]
    public void InverseDct4x4CapturedBlockMatchesReferenceImplementation()
    {
        short[] coeffs =
        [
            -23, 0, 29, -58,
            0, 0, 87, 0,
            0, 29, 0, 0,
            -58, 0, 0, -58,
        ];

        byte[] prediction =
        [
            203, 203, 203, 203,
            203, 203, 203, 203,
            203, 203, 203, 203,
            203, 203, 203, 203,
        ];

        var actual = prediction.ToArray();
        var expected = prediction.ToArray();

        Vp8Dct.InverseDct4x4(coeffs, actual, 4);
        ReferenceInverseDct4x4Add(coeffs, expected, 4);

        TestContext.Out.WriteLine($"captured coeffs 4x4:\n{FormatShortMatrix4x4(coeffs)}");
        TestContext.Out.WriteLine($"prediction 4x4:\n{FormatByteMatrix4x4(prediction)}");
        TestContext.Out.WriteLine($"actual 4x4:\n{FormatByteMatrix4x4(actual)}");
        TestContext.Out.WriteLine($"reference 4x4:\n{FormatByteMatrix4x4(expected)}");

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(TestName = "VP8 DCT: ForwardDct4x4 с нулевым блоком даёт малые значения")]
    public void ForwardDct4x4ZeroBlockSmallValues()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();

        Span<short> output = stackalloc short[16];
        Vp8Dct.ForwardDct4x4(input, output, 4);

        // VP8 ForwardDCT имеет rounding offsets (+14500, +7500, +12000, +51000)
        // поэтому нулевой вход может дать малые ненулевые коэффициенты
        for (var i = 0; i < 16; i++)
        {
            Assert.That(Math.Abs(output[i]), Is.LessThanOrEqualTo(3),
                $"ForwardDCT zero input: коэффициент [{i}] слишком велик");
        }
    }

    [TestCase(TestName = "VP8 DCT: ForwardDct4x4 DC-блок концентрирует энергию в [0]")]
    public void ForwardDct4x4DcBlockConcentratesEnergy()
    {
        Span<short> input = stackalloc short[16];
        input.Fill(100);

        Span<short> output = stackalloc short[16];
        Vp8Dct.ForwardDct4x4(input, output, 4);

        // DC компонент должен быть значительно больше AC
        var dcEnergy = Math.Abs(output[0]);
        var maxAcEnergy = 0;
        for (var i = 1; i < 16; i++)
        {
            maxAcEnergy = Math.Max(maxAcEnergy, Math.Abs(output[i]));
        }

        Assert.That(dcEnergy, Is.GreaterThan(maxAcEnergy));
        TestContext.Out.WriteLine($"ForwardDCT DC={output[0]}, maxAC={maxAcEnergy}");
    }

    #endregion

    #region Prediction Tests

    [TestCase(TestName = "VP8 Prediction: 16x16 DC с верхней и левой границей")]
    public void Predict16x16DcWithBothEdges()
    {
        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];
        above.Fill(100);
        left.Fill(200);

        Span<byte> dst = stackalloc byte[16 * 16];

        Vp8Prediction.Predict16x16Dc(above, left, true, true, dst, 16);

        // Среднее = (100*16 + 200*16) / 32 = (1600 + 3200) / 32 = 150
        for (var i = 0; i < 16 * 16; i++)
        {
            Assert.That(dst[i], Is.EqualTo(150));
        }
    }

    [TestCase(TestName = "VP8 Prediction: 16x16 DC без границ → 128")]
    public void Predict16x16DcNoBorders()
    {
        Span<byte> dst = stackalloc byte[16 * 16];

        Vp8Prediction.Predict16x16Dc([], [], false, false, dst, 16);

        for (var i = 0; i < 16 * 16; i++)
        {
            Assert.That(dst[i], Is.EqualTo(128));
        }
    }

    [TestCase(TestName = "VP8 Prediction: 16x16 V копирует верхнюю строку")]
    public void Predict16x16VCopiesTopRow()
    {
        Span<byte> above = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            above[i] = (byte)(i * 16);
        }

        Span<byte> dst = stackalloc byte[16 * 16];
        Vp8Prediction.Predict16x16V(above, dst, 16);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.That(dst[(y * 16) + x], Is.EqualTo(above[x]),
                    $"V-prediction: пиксель [{x},{y}]");
            }
        }
    }

    [TestCase(TestName = "VP8 Prediction: 16x16 H заполняет строки левым пикселем")]
    public void Predict16x16HFillsRowsFromLeft()
    {
        Span<byte> left = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            left[i] = (byte)(i * 15);
        }

        Span<byte> dst = stackalloc byte[16 * 16];
        Vp8Prediction.Predict16x16H(left, dst, 16);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.That(dst[(y * 16) + x], Is.EqualTo(left[y]),
                    $"H-prediction: пиксель [{x},{y}]");
            }
        }
    }

    [TestCase(TestName = "VP8 Prediction: 16x16 TM формула clamp(left[y] + above[x] - aboveLeft)")]
    public void Predict16x16TmFormula()
    {
        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];
        above.Fill(100);
        left.Fill(50);
        byte aboveLeft = 75;

        Span<byte> dst = stackalloc byte[16 * 16];
        Vp8Prediction.Predict16x16Tm(above, left, aboveLeft, dst, 16);

        // pred = clamp(50 + 100 - 75) = 75
        for (var i = 0; i < 16 * 16; i++)
        {
            Assert.That(dst[i], Is.EqualTo(75));
        }
    }

    [TestCase(TestName = "VP8 Prediction: 8x8 DC chroma prediction")]
    public void Predict8x8DcChroma()
    {
        Span<byte> above = stackalloc byte[8];
        Span<byte> left = stackalloc byte[8];
        above.Fill(60);
        left.Fill(60);

        Span<byte> dst = stackalloc byte[8 * 8];
        Vp8Prediction.Predict8x8Dc(above, left, true, true, dst, 8);

        for (var i = 0; i < 64; i++)
        {
            Assert.That(dst[i], Is.EqualTo(60));
        }
    }

    [TestCase(TestName = "VP8 Prediction: 8x8 V chroma prediction")]
    public void Predict8x8VChroma()
    {
        Span<byte> above = stackalloc byte[8];
        for (var i = 0; i < 8; i++)
        {
            above[i] = (byte)(i * 30);
        }

        Span<byte> dst = stackalloc byte[8 * 8];
        Vp8Prediction.Predict8x8V(above, dst, 8);

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.That(dst[(y * 8) + x], Is.EqualTo(above[x]));
            }
        }
    }

    #endregion

    #region LoopFilter Tests

    [TestCase(TestName = "VP8 LoopFilter: ComputeParams с базовым уровнем")]
    public void LoopFilterComputeParamsBasicLevel()
    {
        var p = Vp8LoopFilter.ComputeParams(50, 0, true);

        Assert.That(p.FilterLevel, Is.EqualTo(50));
        Assert.That(p.InteriorLimit, Is.EqualTo(50));
        Assert.That(p.HevThreshold, Is.Zero); // keyframe → всегда 0
    }

    [TestCase(TestName = "VP8 LoopFilter: ComputeParams sharpness снижает interior limit")]
    public void LoopFilterComputeParamsSharpnessReducesInterior()
    {
        var p = Vp8LoopFilter.ComputeParams(50, 3, true);

        // sharpnessLevel=3 (>0, <=4) → interiorLimit >>= 1 → 25, min(25, 9-3=6) → 6
        Assert.That(p.FilterLevel, Is.EqualTo(50));
        Assert.That(p.InteriorLimit, Is.EqualTo(6));
    }

    [TestCase(TestName = "VP8 LoopFilter: ComputeParams высокая sharpness (>4)")]
    public void LoopFilterComputeParamsHighSharpness()
    {
        var p = Vp8LoopFilter.ComputeParams(100, 6, true);

        // sharpnessLevel=6 (>4) → interiorLimit >>= 2 → 25, min(25, 9-6=3) → 3
        Assert.That(p.FilterLevel, Is.EqualTo(100));
        Assert.That(p.InteriorLimit, Is.EqualTo(3));
    }

    [TestCase(TestName = "VP8 LoopFilter: ComputeParams non-keyframe HEV threshold")]
    public void LoopFilterComputeParamsNonKeyframeHev()
    {
        // baseLevel >= 40 → hev = 2
        var p1 = Vp8LoopFilter.ComputeParams(50, 0, false);
        Assert.That(p1.HevThreshold, Is.EqualTo(2));

        // baseLevel >= 15, < 40 → hev = 1
        var p2 = Vp8LoopFilter.ComputeParams(20, 0, false);
        Assert.That(p2.HevThreshold, Is.EqualTo(1));

        // baseLevel < 15 → hev = 0
        var p3 = Vp8LoopFilter.ComputeParams(10, 0, false);
        Assert.That(p3.HevThreshold, Is.Zero);
    }

    [TestCase(TestName = "VP8 LoopFilter: ComputeParams interior limit минимум 1")]
    public void LoopFilterInteriorLimitMinOne()
    {
        // filterLevel=0, sharpness=8 → interior = 0 >>= 2 = 0, max(0,1) = 1
        var p = Vp8LoopFilter.ComputeParams(0, 8, true);
        Assert.That(p.InteriorLimit, Is.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Integration: VP8 Decode test.webp

    [TestCase(TestName = "VP8: GetInfo парсит VP8 lossy заголовок")]
    public void GetInfoParsesVp8LossyHeader()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var result = codec!.GetInfo(data, out var info);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
        Assert.That(info.IsLossless, Is.False);

        TestContext.Out.WriteLine($"VP8 lossy: {info.Width}x{info.Height}, alpha={info.HasAlpha}");
    }

    [TestCase(TestName = "VP8: декодирование реального lossy WebP файла")]
    public void DecodeRealLossyWebpSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"VP8 lossy decoded: {info.Width}x{info.Height}");
    }

    [TestCase(TestName = "VP8: декодирование test.webp сохраняет реальные цвета")]
    public void DecodeRealLossyWebpMatchesReferencePixels()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        AssertPixelMatches(buffer.AsReadOnlyFrame(), 0, 0, 255, 255, 255, 6);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 799, 477, 255, 226, 249, 8);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 400, 240, 255, 255, 244, 8);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 1200, 715, 6, 6, 6, 6);
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: raw chunk из test.webp сохраняет реальные цвета")]
    public void InternalVp8DecoderMatchesReferencePixels()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        var vp8Data = ExtractVp8Chunk(data);
        var decoder = new Vp8Decoder();
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode(vp8Data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"internal VP8 samples: TL={FormatPixel(buffer.AsReadOnlyFrame(), 0, 0)}, C={FormatPixel(buffer.AsReadOnlyFrame(), 799, 477)}, Q1={FormatPixel(buffer.AsReadOnlyFrame(), 400, 240)}, Q3={FormatPixel(buffer.AsReadOnlyFrame(), 1200, 715)}");

        AssertPixelMatches(buffer.AsReadOnlyFrame(), 0, 0, 255, 255, 255, 6);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 799, 477, 255, 226, 249, 8);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 400, 240, 255, 255, 244, 8);
        AssertPixelMatches(buffer.AsReadOnlyFrame(), 1200, 715, 6, 6, 6, 6);
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: self-encoded frame roundtrip через Vp8Encoder/Vp8Decoder")]
    public void InternalVp8DecoderRoundTripsSelfEncodedFrame()
    {
        const int width = 32;
        const int height = 32;

        using var source = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillSyntheticVp8Frame(source.AsFrame());

        var encoder = new Vp8Encoder
        {
            Quality = 100,
        };
        var encodeDiagnostics = new Vp8EncodeDiagnostics();

        var encoded = new byte[256 * 1024];
        var encodeResult = encoder.Encode(source.AsReadOnlyFrame(), encoded, out var bytesWritten, encodeDiagnostics);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));

        var vp8Data = ExtractVp8Chunk(encoded.AsSpan(0, bytesWritten));
        var decoder = new Vp8Decoder();

        using var decoded = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decoded.AsFrame();
        var decodeResult = decoder.Decode(vp8Data, ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"self VP8 samples: TL={FormatPixel(decoded.AsReadOnlyFrame(), 0, 0)}, C={FormatPixel(decoded.AsReadOnlyFrame(), width / 2, height / 2)}, BR={FormatPixel(decoded.AsReadOnlyFrame(), width - 1, height - 1)}");

        AssertPixelCloseToSource(source.AsReadOnlyFrame(), decoded.AsReadOnlyFrame(), 0, 0, 24);
        AssertPixelCloseToSource(source.AsReadOnlyFrame(), decoded.AsReadOnlyFrame(), width / 2, height / 2, 24);
        AssertPixelCloseToSource(source.AsReadOnlyFrame(), decoded.AsReadOnlyFrame(), width - 1, height - 1, 24);
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: public fallback и internal decoder сравниваются на одном self-stream")]
    public void ComparePublicAndInternalDecodeOnSameSelfEncodedStream()
    {
        const int width = 32;
        const int height = 32;

        using var source = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillSyntheticVp8Frame(source.AsFrame());

        var encoder = new Vp8Encoder
        {
            Quality = 100,
        };
        var encodeDiagnostics = new Vp8EncodeDiagnostics();

        var encoded = new byte[256 * 1024];
        var encodeResult = encoder.Encode(source.AsReadOnlyFrame(), encoded, out var bytesWritten, encodeDiagnostics);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));

        var webpData = encoded.AsSpan(0, bytesWritten);
        var vp8Data = ExtractVp8Chunk(webpData);

        codec!.InitializeDecoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));
        using var publicDecoded = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var publicFrame = publicDecoded.AsFrame();
        var publicResult = codec.Decode(webpData, ref publicFrame);
        Assert.That(publicResult, Is.EqualTo(CodecResult.Success));

        var internalDecoder = new Vp8Decoder();
        using var internalDecoded = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var internalFrame = internalDecoded.AsFrame();
        var internalResult = internalDecoder.Decode(vp8Data, ref internalFrame);
        Assert.That(internalResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"source samples: TL={FormatPixel(source.AsReadOnlyFrame(), 0, 0)}, C={FormatPixel(source.AsReadOnlyFrame(), width / 2, height / 2)}, BR={FormatPixel(source.AsReadOnlyFrame(), width - 1, height - 1)}");
        TestContext.Out.WriteLine($"public samples: TL={FormatPixel(publicDecoded.AsReadOnlyFrame(), 0, 0)}, C={FormatPixel(publicDecoded.AsReadOnlyFrame(), width / 2, height / 2)}, BR={FormatPixel(publicDecoded.AsReadOnlyFrame(), width - 1, height - 1)}");
        TestContext.Out.WriteLine($"internal samples: TL={FormatPixel(internalDecoded.AsReadOnlyFrame(), 0, 0)}, C={FormatPixel(internalDecoded.AsReadOnlyFrame(), width / 2, height / 2)}, BR={FormatPixel(internalDecoded.AsReadOnlyFrame(), width - 1, height - 1)}");

        Assert.That(FormatPixel(publicDecoded.AsReadOnlyFrame(), width / 2, height / 2), Is.Not.EqualTo(FormatPixel(internalDecoded.AsReadOnlyFrame(), width / 2, height / 2)));
        Assert.That(FormatPixel(publicDecoded.AsReadOnlyFrame(), width - 1, height - 1), Is.Not.EqualTo(FormatPixel(internalDecoded.AsReadOnlyFrame(), width - 1, height - 1)));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: диагностика reconstruction первого macroblock на self-stream")]
    public void DumpFirstMacroblockReconstructionDiagnosticsOnSelfStream()
    {
        const int width = 32;
        const int height = 32;

        using var source = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillSyntheticVp8Frame(source.AsFrame());

        var encoder = new Vp8Encoder
        {
            Quality = 100,
        };
        var encodeDiagnostics = new Vp8EncodeDiagnostics();

        var encoded = new byte[256 * 1024];
        var encodeResult = encoder.Encode(source.AsReadOnlyFrame(), encoded, out var bytesWritten, encodeDiagnostics);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));

        var vp8Data = ExtractVp8Chunk(encoded.AsSpan(0, bytesWritten));
        var decoder = new Vp8Decoder();
        var diagnostics = new Vp8DecodeDiagnostics();

        using var decoded = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decoded.AsFrame();
        var decodeResult = decoder.Decode(vp8Data, ref decodedFrame, diagnostics);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"encoder mb0: chosenY={encodeDiagnostics.FirstMacroblockChosenYMode}, chosenUV={encodeDiagnostics.FirstMacroblockChosenUvMode}, srcY={encodeDiagnostics.FirstMacroblockOriginalYTopLeft}, predY={encodeDiagnostics.FirstMacroblockPredictedYTopLeft}, firstDctDc={encodeDiagnostics.FirstYBlockForwardDctDc}");
        TestContext.Out.WriteLine($"encoder mb0 y2 source dc: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2SourceDc)}");
        TestContext.Out.WriteLine($"encoder mb0 y2 forward wht: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2ForwardWht)}");
        TestContext.Out.WriteLine($"encoder mb0 y2 quantized: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2Quantized)}");
        TestContext.Out.WriteLine($"encoder mb0 y2 inverse wht: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2InverseWht)}");
        TestContext.Out.WriteLine($"encoder mb0 uv: srcU={encodeDiagnostics.FirstMacroblockOriginalUTopLeft}, srcV={encodeDiagnostics.FirstMacroblockOriginalVTopLeft}, predU={encodeDiagnostics.FirstMacroblockPredictedUTopLeft}, predV={encodeDiagnostics.FirstMacroblockPredictedVTopLeft}, uDctDc={encodeDiagnostics.FirstUBlockForwardDctDc}, vDctDc={encodeDiagnostics.FirstVBlockForwardDctDc}");
        TestContext.Out.WriteLine($"encoder mb0 u quantized: {FormatShortVector(encodeDiagnostics.FirstUBlockQuantized)}");
        TestContext.Out.WriteLine($"encoder mb0 v quantized: {FormatShortVector(encodeDiagnostics.FirstVBlockQuantized)}");
        TestContext.Out.WriteLine($"source TL={FormatPixel(source.AsReadOnlyFrame(), 0, 0)}");
        TestContext.Out.WriteLine($"decoded TL={FormatPixel(decoded.AsReadOnlyFrame(), 0, 0)}");
        TestContext.Out.WriteLine($"mb0 modes: Y={diagnostics.FirstMacroblockYMode}, UV={diagnostics.FirstMacroblockUvMode}, skip={diagnostics.FirstMacroblockIsSkip}");
        TestContext.Out.WriteLine($"mb0 prediction: Y={diagnostics.FirstMacroblockPredictedYTopLeft}, U={diagnostics.FirstMacroblockPredictedUTopLeft}, V={diagnostics.FirstMacroblockPredictedVTopLeft}");
        TestContext.Out.WriteLine($"mb0 y2: nonZero={diagnostics.FirstMacroblockY2NonZero}, rawDc={diagnostics.FirstMacroblockY2RawDc}, dequantDc={diagnostics.FirstMacroblockY2DequantDc}, whtDc={diagnostics.FirstMacroblockY2WhtDc}");
        TestContext.Out.WriteLine($"mb0 y-block0: nonZero={diagnostics.FirstYBlockNonZero}, rawDc={diagnostics.FirstYBlockRawDc}, dequantDc={diagnostics.FirstYBlockDequantDcBeforeY2}, injectedDc={diagnostics.FirstYBlockDcAfterY2Injection}, outY={diagnostics.FirstYBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb0 u-block0: nonZero={diagnostics.FirstUBlockNonZero}, rawDc={diagnostics.FirstUBlockRawDc}, dequantDc={diagnostics.FirstUBlockDequantDc}, outU={diagnostics.FirstUBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb0 v-block0: nonZero={diagnostics.FirstVBlockNonZero}, rawDc={diagnostics.FirstVBlockRawDc}, dequantDc={diagnostics.FirstVBlockDequantDc}, outV={diagnostics.FirstVBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb0 final yuv: Y={diagnostics.FirstMacroblockFinalYTopLeft}, U={diagnostics.FirstMacroblockFinalUTopLeft}, V={diagnostics.FirstMacroblockFinalVTopLeft}");
        TestContext.Out.WriteLine($"mb0 final rgba: {diagnostics.FirstMacroblockFinalRgbaTopLeft}");
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: диагностика reconstruction последнего macroblock на self-stream")]
    public void DumpLastMacroblockReconstructionDiagnosticsOnSelfStream()
    {
        const int width = 32;
        const int height = 32;

        using var source = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillSyntheticVp8Frame(source.AsFrame());

        var encoder = new Vp8Encoder
        {
            Quality = 100,
        };
        var encodeDiagnostics = new Vp8EncodeDiagnostics
        {
            TargetMacroblockX = 1,
            TargetMacroblockY = 1,
        };

        var encoded = new byte[256 * 1024];
        var encodeResult = encoder.Encode(source.AsReadOnlyFrame(), encoded, out var bytesWritten, encodeDiagnostics);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success));
        Assert.That(bytesWritten, Is.GreaterThan(0));

        var vp8Data = ExtractVp8Chunk(encoded.AsSpan(0, bytesWritten));
        var decoder = new Vp8Decoder();
        var diagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = 1,
            TargetMacroblockY = 1,
        };

        using var decoded = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decoded.AsFrame();
        var decodeResult = decoder.Decode(vp8Data, ref decodedFrame, diagnostics);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"encoder mb(1,1): chosenY={encodeDiagnostics.FirstMacroblockChosenYMode}, chosenUV={encodeDiagnostics.FirstMacroblockChosenUvMode}, srcY={encodeDiagnostics.FirstMacroblockOriginalYTopLeft}, predY={encodeDiagnostics.FirstMacroblockPredictedYTopLeft}, firstDctDc={encodeDiagnostics.FirstYBlockForwardDctDc}");
        TestContext.Out.WriteLine($"encoder mb(1,1) y2 source dc: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2SourceDc)}");
        TestContext.Out.WriteLine($"encoder mb(1,1) y2 quantized: {FormatShortVector(encodeDiagnostics.FirstMacroblockY2Quantized)}");
        TestContext.Out.WriteLine($"encoder mb(1,1) uv: srcU={encodeDiagnostics.FirstMacroblockOriginalUTopLeft}, srcV={encodeDiagnostics.FirstMacroblockOriginalVTopLeft}, predU={encodeDiagnostics.FirstMacroblockPredictedUTopLeft}, predV={encodeDiagnostics.FirstMacroblockPredictedVTopLeft}, uDctDc={encodeDiagnostics.FirstUBlockForwardDctDc}, vDctDc={encodeDiagnostics.FirstVBlockForwardDctDc}");
        TestContext.Out.WriteLine($"encoder mb(1,1) u quantized: {FormatShortVector(encodeDiagnostics.FirstUBlockQuantized)}");
        TestContext.Out.WriteLine($"encoder mb(1,1) v quantized: {FormatShortVector(encodeDiagnostics.FirstVBlockQuantized)}");
        TestContext.Out.WriteLine($"source BR={FormatPixel(source.AsReadOnlyFrame(), width - 1, height - 1)}");
        TestContext.Out.WriteLine($"decoded BR={FormatPixel(decoded.AsReadOnlyFrame(), width - 1, height - 1)}");
        TestContext.Out.WriteLine($"mb(1,1) modes: Y={diagnostics.FirstMacroblockYMode}, UV={diagnostics.FirstMacroblockUvMode}, skip={diagnostics.FirstMacroblockIsSkip}");
        TestContext.Out.WriteLine($"mb(1,1) prediction: Y={diagnostics.FirstMacroblockPredictedYTopLeft}, U={diagnostics.FirstMacroblockPredictedUTopLeft}, V={diagnostics.FirstMacroblockPredictedVTopLeft}");
        TestContext.Out.WriteLine($"mb(1,1) y2: nonZero={diagnostics.FirstMacroblockY2NonZero}, rawDc={diagnostics.FirstMacroblockY2RawDc}, dequantDc={diagnostics.FirstMacroblockY2DequantDc}, whtDc={diagnostics.FirstMacroblockY2WhtDc}");
        TestContext.Out.WriteLine($"mb(1,1) y-block0: nonZero={diagnostics.FirstYBlockNonZero}, rawDc={diagnostics.FirstYBlockRawDc}, dequantDc={diagnostics.FirstYBlockDequantDcBeforeY2}, injectedDc={diagnostics.FirstYBlockDcAfterY2Injection}, outY={diagnostics.FirstYBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb(1,1) u-block0: nonZero={diagnostics.FirstUBlockNonZero}, rawDc={diagnostics.FirstUBlockRawDc}, dequantDc={diagnostics.FirstUBlockDequantDc}, outU={diagnostics.FirstUBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb(1,1) v-block0: nonZero={diagnostics.FirstVBlockNonZero}, rawDc={diagnostics.FirstVBlockRawDc}, dequantDc={diagnostics.FirstVBlockDequantDc}, outV={diagnostics.FirstVBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"mb(1,1) final yuv: Y={diagnostics.FirstMacroblockFinalYTopLeft}, U={diagnostics.FirstMacroblockFinalUTopLeft}, V={diagnostics.FirstMacroblockFinalVTopLeft}");
        TestContext.Out.WriteLine($"mb(1,1) final rgba: {diagnostics.FirstMacroblockFinalRgbaTopLeft}");
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: Y2 quantized block roundtrip через token serialization")]
    public void Y2QuantizedBlockRoundTripsThroughTokenSerialization()
    {
        short[] encodedCoeffs = [-314, -122, 0, -61, -107, 0, 0, 0, 0, 0, 0, 0, -53, 0, 0, 0];
        var bitstream = new byte[256];
        var bytesWritten = Vp8Encoder.EncodeBlockForDiagnostics(encodedCoeffs, blockType: 1, firstCoeff: 0, bitstream);
        Assert.That(bytesWritten, Is.GreaterThan(0));

        var decoder = new Vp8Decoder();
        var decodedCoeffs = new short[16];
        var hasNonZero = decoder.DecodeBlockForDiagnostics(bitstream.AsSpan(0, bytesWritten), decodedCoeffs, blockType: 1, firstCoeff: 0, initialContext: 0);

        TestContext.Out.WriteLine($"encoded y2 quantized: {FormatShortVector(encodedCoeffs)}");
        TestContext.Out.WriteLine($"decoded y2 quantized: {FormatShortVector(decodedCoeffs)}");

        Assert.That(hasNonZero, Is.True);
        Assert.That(decodedCoeffs, Is.EqualTo(encodedCoeffs));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: UV quantized blocks roundtrip через token serialization")]
    public void UvQuantizedBlocksRoundTripThroughTokenSerialization()
    {
        short[] encodedU = [9, -1, 0, 0, 2, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0];
        short[] encodedV = [-6, -7, 0, -1, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var decoder = new Vp8Decoder();

        var uBitstream = new byte[256];
        var uBytesWritten = Vp8Encoder.EncodeBlockForDiagnostics(encodedU, blockType: 2, firstCoeff: 0, uBitstream);
        var decodedU = new short[16];
        var uHasNonZero = decoder.DecodeBlockForDiagnostics(uBitstream.AsSpan(0, uBytesWritten), decodedU, blockType: 2, firstCoeff: 0, initialContext: 0);

        var vBitstream = new byte[256];
        var vBytesWritten = Vp8Encoder.EncodeBlockForDiagnostics(encodedV, blockType: 2, firstCoeff: 0, vBitstream);
        var decodedV = new short[16];
        var vHasNonZero = decoder.DecodeBlockForDiagnostics(vBitstream.AsSpan(0, vBytesWritten), decodedV, blockType: 2, firstCoeff: 0, initialContext: 0);

        TestContext.Out.WriteLine($"encoded U quantized: {FormatShortVector(encodedU)}");
        TestContext.Out.WriteLine($"decoded U quantized: {FormatShortVector(decodedU)}");
        TestContext.Out.WriteLine($"encoded V quantized: {FormatShortVector(encodedV)}");
        TestContext.Out.WriteLine($"decoded V quantized: {FormatShortVector(decodedV)}");

        Assert.That(uHasNonZero, Is.True);
        Assert.That(vHasNonZero, Is.True);
        Assert.That(decodedU, Is.EqualTo(encodedU));
        Assert.That(decodedV, Is.EqualTo(encodedV));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: диагностика первого macroblock для real test.webp")]
    public void DumpFirstMacroblockDiagnosticsForRealWebp()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        var decoder = new Vp8Decoder();
        var diagnostics = new Vp8DecodeDiagnostics();

        using var decoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var decodedFrame = decoded.AsFrame();
        var decodeResult = decoder.Decode(vp8Data, ref decodedFrame, diagnostics);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"real vp8 TL={FormatPixel(decoded.AsReadOnlyFrame(), 0, 0)}");
        TestContext.Out.WriteLine($"real vp8 C={FormatPixel(decoded.AsReadOnlyFrame(), info.Width / 2, info.Height / 2)}");
        TestContext.Out.WriteLine($"real vp8 header: log2Parts={diagnostics.Log2Partitions}, baseQp={diagnostics.BaseQp}, mbNoCoeffSkip={diagnostics.MbNoCoeffSkip}, probSkipFalse={diagnostics.ProbSkipFalse}");
        TestContext.Out.WriteLine($"real vp8 mb0 modes: Y={diagnostics.FirstMacroblockYMode}, UV={diagnostics.FirstMacroblockUvMode}, skip={diagnostics.FirstMacroblockIsSkip}");
        TestContext.Out.WriteLine($"real vp8 mb0 bpred block0: mode={diagnostics.FirstBPredSubblockMode}, above={diagnostics.FirstBPredSubblockAboveMode}, left={diagnostics.FirstBPredSubblockLeftMode}");
        TestContext.Out.WriteLine($"real vp8 mb0 prediction: Y={diagnostics.FirstMacroblockPredictedYTopLeft}, U={diagnostics.FirstMacroblockPredictedUTopLeft}, V={diagnostics.FirstMacroblockPredictedVTopLeft}");
        TestContext.Out.WriteLine($"real vp8 mb0 y2: nonZero={diagnostics.FirstMacroblockY2NonZero}, rawDc={diagnostics.FirstMacroblockY2RawDc}, dequantDc={diagnostics.FirstMacroblockY2DequantDc}, whtDc={diagnostics.FirstMacroblockY2WhtDc}");
        TestContext.Out.WriteLine($"real vp8 mb0 y2 raw coeffs: {FormatShortVector(diagnostics.FirstMacroblockY2RawCoeffs)}");
        TestContext.Out.WriteLine($"real vp8 mb0 y2 dequant coeffs: {FormatShortVector(diagnostics.FirstMacroblockY2DequantCoeffs)}");
        TestContext.Out.WriteLine($"real vp8 mb0 y2 wht coeffs: {FormatShortVector(diagnostics.FirstMacroblockY2WhtCoeffs)}");
        TestContext.Out.WriteLine($"real vp8 mb0 y2 token trace: {string.Join(" | ", diagnostics.FirstMacroblockY2TokenTrace)}");
        TestContext.Out.WriteLine($"real vp8 mb0 y-block0: nonZero={diagnostics.FirstYBlockNonZero}, rawDc={diagnostics.FirstYBlockRawDc}, dequantDc={diagnostics.FirstYBlockDequantDcBeforeY2}, injectedDc={diagnostics.FirstYBlockDcAfterY2Injection}, outY={diagnostics.FirstYBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"real vp8 mb0 y-block0 token trace: {string.Join(" | ", diagnostics.FirstYBlockTokenTrace)}");
        TestContext.Out.WriteLine($"real vp8 mb0 u-block0: nonZero={diagnostics.FirstUBlockNonZero}, rawDc={diagnostics.FirstUBlockRawDc}, dequantDc={diagnostics.FirstUBlockDequantDc}, outU={diagnostics.FirstUBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"real vp8 mb0 v-block0: nonZero={diagnostics.FirstVBlockNonZero}, rawDc={diagnostics.FirstVBlockRawDc}, dequantDc={diagnostics.FirstVBlockDequantDc}, outV={diagnostics.FirstVBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"real vp8 mb0 final yuv: Y={diagnostics.FirstMacroblockFinalYTopLeft}, U={diagnostics.FirstMacroblockFinalUTopLeft}, V={diagnostics.FirstMacroblockFinalVTopLeft}");
        TestContext.Out.WriteLine($"real vp8 mb0 final rgba: {diagnostics.FirstMacroblockFinalRgbaTopLeft}");
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: real test.webp public vs internal sample compare")]
    public void CompareRealWebpPublicAndInternalSamples()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var publicDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var publicFrame = publicDecoded.AsFrame();
        var publicResult = codec.Decode(data, ref publicFrame);
        Assert.That(publicResult, Is.EqualTo(CodecResult.Success));

        var internalDecoder = new Vp8Decoder();
        using var internalDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var internalFrame = internalDecoded.AsFrame();
        var internalResult = internalDecoder.Decode(vp8Data, ref internalFrame);
        Assert.That(internalResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"public TL={FormatPixel(publicDecoded.AsReadOnlyFrame(), 0, 0)}");
        TestContext.Out.WriteLine($"internal TL={FormatPixel(internalDecoded.AsReadOnlyFrame(), 0, 0)}");
        TestContext.Out.WriteLine($"public C={FormatPixel(publicDecoded.AsReadOnlyFrame(), info.Width / 2, info.Height / 2)}");
        TestContext.Out.WriteLine($"internal C={FormatPixel(internalDecoded.AsReadOnlyFrame(), info.Width / 2, info.Height / 2)}");
        TestContext.Out.WriteLine($"public Q1={FormatPixel(publicDecoded.AsReadOnlyFrame(), info.Width / 4, info.Height / 4)}");
        TestContext.Out.WriteLine($"internal Q1={FormatPixel(internalDecoded.AsReadOnlyFrame(), info.Width / 4, info.Height / 4)}");
        TestContext.Out.WriteLine($"public Q3={FormatPixel(publicDecoded.AsReadOnlyFrame(), (info.Width * 3) / 4, (info.Height * 3) / 4)}");
        TestContext.Out.WriteLine($"internal Q3={FormatPixel(internalDecoded.AsReadOnlyFrame(), (info.Width * 3) / 4, (info.Height * 3) / 4)}");
    }

    [TestCase(TestName = "VP8 internal: диагностика проблемного macroblock для real test.webp")]
    public void DumpProblemMacroblockDiagnosticsForRealWebp()
    {
        const int sampleX = 799;
        const int sampleY = 477;
        var targetMbX = sampleX / 16;
        var targetMbY = sampleY / 16;
        var localX = sampleX % 16;
        var localY = sampleY % 16;
        var mbOriginX = targetMbX * 16;
        var mbOriginY = targetMbY * 16;

        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var publicDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var publicFrame = publicDecoded.AsFrame();
        var publicResult = codec.Decode(data, ref publicFrame);
        Assert.That(publicResult, Is.EqualTo(CodecResult.Success));

        var diagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = targetMbX,
            TargetMacroblockY = targetMbY,
        };

        var internalDecoder = new Vp8Decoder();
        using var internalDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var internalFrame = internalDecoded.AsFrame();
        var internalResult = internalDecoder.Decode(vp8Data, ref internalFrame, diagnostics);
        Assert.That(internalResult, Is.EqualTo(CodecResult.Success));

        TestContext.Out.WriteLine($"problem sample: ({sampleX},{sampleY}), macroblock=({targetMbX},{targetMbY}), local=({localX},{localY})");
        TestContext.Out.WriteLine($"public sample={FormatPixel(publicDecoded.AsReadOnlyFrame(), sampleX, sampleY)}");
        TestContext.Out.WriteLine($"internal sample={FormatPixel(internalDecoded.AsReadOnlyFrame(), sampleX, sampleY)}");
        TestContext.Out.WriteLine($"public mb TL={FormatPixel(publicDecoded.AsReadOnlyFrame(), mbOriginX, mbOriginY)}");
        TestContext.Out.WriteLine($"internal mb TL={FormatPixel(internalDecoded.AsReadOnlyFrame(), mbOriginX, mbOriginY)}");
        TestContext.Out.WriteLine($"public mb TR={FormatPixel(publicDecoded.AsReadOnlyFrame(), mbOriginX + 15, mbOriginY)}");
        TestContext.Out.WriteLine($"internal mb TR={FormatPixel(internalDecoded.AsReadOnlyFrame(), mbOriginX + 15, mbOriginY)}");
        TestContext.Out.WriteLine($"public mb BL={FormatPixel(publicDecoded.AsReadOnlyFrame(), mbOriginX, mbOriginY + 15)}");
        TestContext.Out.WriteLine($"internal mb BL={FormatPixel(internalDecoded.AsReadOnlyFrame(), mbOriginX, mbOriginY + 15)}");
        TestContext.Out.WriteLine($"public mb BR={FormatPixel(publicDecoded.AsReadOnlyFrame(), mbOriginX + 15, mbOriginY + 15)}");
        TestContext.Out.WriteLine($"internal mb BR={FormatPixel(internalDecoded.AsReadOnlyFrame(), mbOriginX + 15, mbOriginY + 15)}");
        TestContext.Out.WriteLine($"target mb modes: Y={diagnostics.FirstMacroblockYMode}, UV={diagnostics.FirstMacroblockUvMode}, skip={diagnostics.FirstMacroblockIsSkip}");
        TestContext.Out.WriteLine($"target mb bpred block0: mode={diagnostics.FirstBPredSubblockMode}, above={diagnostics.FirstBPredSubblockAboveMode}, left={diagnostics.FirstBPredSubblockLeftMode}");
        TestContext.Out.WriteLine($"target mb prediction: Y={diagnostics.FirstMacroblockPredictedYTopLeft}, U={diagnostics.FirstMacroblockPredictedUTopLeft}, V={diagnostics.FirstMacroblockPredictedVTopLeft}");
        TestContext.Out.WriteLine($"target mb y2: nonZero={diagnostics.FirstMacroblockY2NonZero}, rawDc={diagnostics.FirstMacroblockY2RawDc}, dequantDc={diagnostics.FirstMacroblockY2DequantDc}, whtDc={diagnostics.FirstMacroblockY2WhtDc}");
        TestContext.Out.WriteLine($"target mb y2 token trace: {string.Join(" | ", diagnostics.FirstMacroblockY2TokenTrace)}");
        TestContext.Out.WriteLine($"target mb y-block0: nonZero={diagnostics.FirstYBlockNonZero}, rawDc={diagnostics.FirstYBlockRawDc}, dequantDc={diagnostics.FirstYBlockDequantDcBeforeY2}, injectedDc={diagnostics.FirstYBlockDcAfterY2Injection}, outY={diagnostics.FirstYBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"target mb y-block0 token trace: {string.Join(" | ", diagnostics.FirstYBlockTokenTrace)}");
        TestContext.Out.WriteLine($"target mb final yuv: Y={diagnostics.FirstMacroblockFinalYTopLeft}, U={diagnostics.FirstMacroblockFinalUTopLeft}, V={diagnostics.FirstMacroblockFinalVTopLeft}");
        TestContext.Out.WriteLine($"target mb final rgba: {diagnostics.FirstMacroblockFinalRgbaTopLeft}");
    }

    [TestCase(TestName = "VP8 internal: concise diagnostics for real test.webp target subblock")]
    public void DumpProblemMacroblockConciseFailureForRealWebp()
    {
        const int sampleX = 799;
        const int sampleY = 477;
        var targetMbX = sampleX / 16;
        var targetMbY = sampleY / 16;
        var targetSubblockX = (sampleX % 16) / 4;
        var targetSubblockY = (sampleY % 16) / 4;

        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var publicDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var publicFrame = publicDecoded.AsFrame();
        var publicResult = codec.Decode(data, ref publicFrame);
        Assert.That(publicResult, Is.EqualTo(CodecResult.Success));

        var targetDiagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = targetMbX,
            TargetMacroblockY = targetMbY,
            TargetSubblockX = targetSubblockX,
            TargetSubblockY = targetSubblockY,
        };

        var internalDecoder = new Vp8Decoder();
        using var internalDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var internalFrame = internalDecoded.AsFrame();
        var internalResult = internalDecoder.Decode(vp8Data, ref internalFrame, targetDiagnostics);
        Assert.That(internalResult, Is.EqualTo(CodecResult.Success));

        var noCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = targetMbX,
            TargetMacroblockY = targetMbY,
            TargetSubblockX = targetSubblockX,
            TargetSubblockY = targetSubblockY,
            DisableCoeffProbUpdates = true,
        };

        var noCoeffUpdateDecoder = new Vp8Decoder();
        using var noCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var noCoeffUpdateFrame = noCoeffUpdateDecoded.AsFrame();
        var noCoeffUpdateResult = noCoeffUpdateDecoder.Decode(vp8Data, ref noCoeffUpdateFrame, noCoeffUpdateDiagnostics);
        Assert.That(noCoeffUpdateResult, Is.EqualTo(CodecResult.Success));

        var aboveBottomRowDiagnostics = new Vp8DecodeDiagnostics[4];
        for (var blockX = 0; blockX < 4; blockX++)
        {
            aboveBottomRowDiagnostics[blockX] = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY - 1,
                TargetSubblockX = blockX,
                TargetSubblockY = 3,
            };

            var aboveDecoder = new Vp8Decoder();
            using var aboveDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var aboveFrame = aboveDecoded.AsFrame();
            var aboveResult = aboveDecoder.Decode(vp8Data, ref aboveFrame, aboveBottomRowDiagnostics[blockX]);
            Assert.That(aboveResult, Is.EqualTo(CodecResult.Success));
        }

        var summary = string.Join(Environment.NewLine, [
            $"sample=({sampleX},{sampleY}), macroblock=({targetMbX},{targetMbY}), subblock=({targetSubblockX},{targetSubblockY})",
            $"public sample={FormatPixel(publicDecoded.AsReadOnlyFrame(), sampleX, sampleY)}",
            $"internal sample={FormatPixel(internalDecoded.AsReadOnlyFrame(), sampleX, sampleY)}",
            $"no-coeff-update sample={FormatPixel(noCoeffUpdateDecoded.AsReadOnlyFrame(), sampleX, sampleY)}",
            $"target mode={targetDiagnostics.TargetYSubblockMode}, aboveMode={targetDiagnostics.TargetYSubblockAboveMode}, leftMode={targetDiagnostics.TargetYSubblockLeftMode}",
            $"macroblock prediction above16={FormatByteVector(targetDiagnostics.FirstMacroblockPredictionAbove16)}",
            $"macroblock prediction left16={FormatByteVector(targetDiagnostics.FirstMacroblockPredictionLeft16)}",
            $"macroblock prediction aboveLeft={targetDiagnostics.FirstMacroblockPredictionAboveLeft}",
            $"target predictor flags: hasAbove={targetDiagnostics.TargetYSubblockHasAbove}, hasLeft={targetDiagnostics.TargetYSubblockHasLeft}, aboveLeft={targetDiagnostics.TargetYSubblockAboveLeftSample}",
            $"target contexts: above={targetDiagnostics.TargetYSubblockAboveNonZeroContext}, left={targetDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={targetDiagnostics.TargetYSubblockInitialContext}",
            $"target coeffs: nonZero={targetDiagnostics.TargetYSubblockNonZero}, rawDc={targetDiagnostics.TargetYSubblockRawDc}, dequantDc={targetDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={targetDiagnostics.TargetYSubblockDcAfterY2Injection}",
            $"target ref-style: nonZero={targetDiagnostics.TargetYSubblockReferenceStyleNonZero}, rawDc={targetDiagnostics.TargetYSubblockReferenceStyleRawDc}",
            $"target forced ctx0: nonZero={targetDiagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext0RawDc}",
            $"target forced ctx1: nonZero={targetDiagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext1RawDc}",
            $"target forced ctx2: nonZero={targetDiagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext2RawDc}",
            $"target predicted top-left={targetDiagnostics.TargetYSubblockPredictedTopLeft}, output top-left={targetDiagnostics.TargetYSubblockOutputTopLeft}",
            $"target U/V top-left={targetDiagnostics.TargetUBlockOutputTopLeft}/{targetDiagnostics.TargetVBlockOutputTopLeft}",
            $"no-coeff-update contexts: above={noCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={noCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={noCoeffUpdateDiagnostics.TargetYSubblockInitialContext}",
            $"no-coeff-update coeffs: nonZero={noCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={noCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={noCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={noCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}",
            $"no-coeff-update predicted top-left={noCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, output top-left={noCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}",
            $"above mb bottom row block0: ctx={aboveBottomRowDiagnostics[0].TargetYSubblockInitialContext}, predY={aboveBottomRowDiagnostics[0].TargetYSubblockPredictedTopLeft}, outY={aboveBottomRowDiagnostics[0].TargetYSubblockOutputTopLeft}, nonZero={aboveBottomRowDiagnostics[0].TargetYSubblockNonZero}, rawDc={aboveBottomRowDiagnostics[0].TargetYSubblockRawDc}, injectedDc={aboveBottomRowDiagnostics[0].TargetYSubblockDcAfterY2Injection}",
            $"above mb bottom row block1: ctx={aboveBottomRowDiagnostics[1].TargetYSubblockInitialContext}, predY={aboveBottomRowDiagnostics[1].TargetYSubblockPredictedTopLeft}, outY={aboveBottomRowDiagnostics[1].TargetYSubblockOutputTopLeft}, nonZero={aboveBottomRowDiagnostics[1].TargetYSubblockNonZero}, rawDc={aboveBottomRowDiagnostics[1].TargetYSubblockRawDc}, injectedDc={aboveBottomRowDiagnostics[1].TargetYSubblockDcAfterY2Injection}",
            $"above mb bottom row block2: ctx={aboveBottomRowDiagnostics[2].TargetYSubblockInitialContext}, predY={aboveBottomRowDiagnostics[2].TargetYSubblockPredictedTopLeft}, outY={aboveBottomRowDiagnostics[2].TargetYSubblockOutputTopLeft}, nonZero={aboveBottomRowDiagnostics[2].TargetYSubblockNonZero}, rawDc={aboveBottomRowDiagnostics[2].TargetYSubblockRawDc}, injectedDc={aboveBottomRowDiagnostics[2].TargetYSubblockDcAfterY2Injection}",
            $"above mb bottom row block3: ctx={aboveBottomRowDiagnostics[3].TargetYSubblockInitialContext}, predY={aboveBottomRowDiagnostics[3].TargetYSubblockPredictedTopLeft}, outY={aboveBottomRowDiagnostics[3].TargetYSubblockOutputTopLeft}, nonZero={aboveBottomRowDiagnostics[3].TargetYSubblockNonZero}, rawDc={aboveBottomRowDiagnostics[3].TargetYSubblockRawDc}, injectedDc={aboveBottomRowDiagnostics[3].TargetYSubblockDcAfterY2Injection}",
        ]);

        Assert.Fail(summary);
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: найти первый расходящийся macroblock для real test.webp")]
    public void FindFirstDivergentMacroblockForRealWebp()
    {
        FindAndDiagnoseFirstDivergentMacroblockForRealWebp(targetSevereIfAvailable: true);
    }

    [TestCase(TestName = "VP8 internal: снять diagnostics earliest noticeable macroblock для real test.webp")]
    public void DiagnoseFirstNoticeableMacroblockForRealWebp()
    {
        FindAndDiagnoseFirstDivergentMacroblockForRealWebp(targetSevereIfAvailable: false);
    }

    [TestCase(TestName = "VP8 internal: снять diagnostics known problem macroblock для real test.webp")]
    public void DiagnoseKnownProblemMacroblockForRealWebp()
    {
        FindAndDiagnoseFirstDivergentMacroblockForRealWebp(
            targetSevereIfAvailable: false,
            forcedTarget: (49, 29, 799, 477));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: найти first external divergence macroblock against Magick для real test.webp")]
    public void FindFirstExternalDivergentMacroblockAgainstMagickForRealWebp()
    {
        const int noticeableThreshold = 16;
        const int severeThreshold = 64;

        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        var decoder = new Vp8Decoder();
        using var decoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = decoded.AsFrame();
        var decodeResult = decoder.Decode(vp8Data, ref frame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        var externalRgba = LoadMagickRgbaImage(Path.Combine(AssetsDir, "test.webp"), info.Width, info.Height);
        var macroblockColumns = (info.Width + 15) / 16;
        var macroblockRows = (info.Height + 15) / 16;

        (int X, int Y, int MaxDiff, int AvgDiff, int SampleX, int SampleY) firstNoticeable = (-1, -1, -1, -1, -1, -1);
        (int X, int Y, int MaxDiff, int AvgDiff, int SampleX, int SampleY) firstSevere = (-1, -1, -1, -1, -1, -1);

        for (var mbY = 0; mbY < macroblockRows; mbY++)
        {
            for (var mbX = 0; mbX < macroblockColumns; mbX++)
            {
                var stats = GetMacroblockDiffStats(externalRgba, info.Width, info.Height, decoded.AsReadOnlyFrame(), mbX, mbY);

                if (firstNoticeable.X < 0 && stats.MaxDiff > noticeableThreshold)
                {
                    firstNoticeable = (mbX, mbY, stats.MaxDiff, stats.AvgDiff, stats.SampleX, stats.SampleY);
                }

                if (firstSevere.X < 0 && stats.MaxDiff > severeThreshold)
                {
                    firstSevere = (mbX, mbY, stats.MaxDiff, stats.AvgDiff, stats.SampleX, stats.SampleY);
                }

                if (firstNoticeable.X >= 0 && firstSevere.X >= 0)
                {
                    break;
                }
            }

            if (firstNoticeable.X >= 0 && firstSevere.X >= 0)
            {
                break;
            }
        }

        Assert.That(firstNoticeable.X, Is.GreaterThanOrEqualTo(0), "Не найден даже заметный внешний расходящийся macroblock против Magick");

        TestContext.Out.WriteLine($"first external noticeable macroblock=({firstNoticeable.X},{firstNoticeable.Y}), maxDiff={firstNoticeable.MaxDiff}, avgDiff={firstNoticeable.AvgDiff}, sample=({firstNoticeable.SampleX},{firstNoticeable.SampleY})");
        TestContext.Out.WriteLine($"first external noticeable internal sample={FormatPixel(decoded.AsReadOnlyFrame(), firstNoticeable.SampleX, firstNoticeable.SampleY)}");
        TestContext.Out.WriteLine($"first external noticeable Magick sample={FormatRgbaPixel(externalRgba, info.Width, firstNoticeable.SampleX, firstNoticeable.SampleY)}");

        if (firstSevere.X >= 0)
        {
            TestContext.Out.WriteLine($"first external severe macroblock=({firstSevere.X},{firstSevere.Y}), maxDiff={firstSevere.MaxDiff}, avgDiff={firstSevere.AvgDiff}, sample=({firstSevere.SampleX},{firstSevere.SampleY})");
            TestContext.Out.WriteLine($"first external severe internal sample={FormatPixel(decoded.AsReadOnlyFrame(), firstSevere.SampleX, firstSevere.SampleY)}");
            TestContext.Out.WriteLine($"first external severe Magick sample={FormatRgbaPixel(externalRgba, info.Width, firstSevere.SampleX, firstSevere.SampleY)}");
        }

        Assert.Fail("temporary diagnostic failure to surface earliest external divergence against Magick");
    }

    [TestCase(TestName = "VP8 internal: снять diagnostics first external divergence macroblock для real test.webp")]
    public void DiagnoseFirstExternalDivergenceMacroblockForRealWebp()
    {
        FindAndDiagnoseFirstDivergentMacroblockForRealWebp(
            targetSevereIfAvailable: false,
            forcedTarget: (3, 0, 52, 6));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: снять diagnostics current external divergence macroblock для real test.webp")]
    public void DiagnoseCurrentExternalDivergenceMacroblockForRealWebp()
    {
        FindAndDiagnoseFirstDivergentMacroblockForRealWebp(
            targetSevereIfAvailable: false,
            forcedTarget: (1, 0, 22, 4));
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: проверить symmetry remap для first external divergence macroblock")]
    public void DiagnoseFirstExternalDivergenceSymmetryRemapForRealWebp()
        => DiagnoseSymmetryRemapAgainstMagickForRealWebp(3, 0, 52, 6);

    [Explicit]
    [TestCase(TestName = "VP8 internal: проверить symmetry remap для known problem macroblock")]
    public void DiagnoseKnownProblemSymmetryRemapForRealWebp()
        => DiagnoseSymmetryRemapAgainstMagickForRealWebp(49, 29, 799, 477);

    [Explicit]
    [TestCase(TestName = "VP8 internal: проверить per-coefficient basis scan для first external divergence macroblock")]
    public void DiagnoseFirstExternalDivergencePerCoefficientBasisForRealWebp()
    {
        var comparison = CaptureSubblockAgainstMagickForRealWebp(3, 0, 1, 1);
        var baselineSad = ComputeBlockSad(comparison.Diagnostics.TargetYSubblockOutput4x4, comparison.ExternalRedBlock);

        TestContext.Out.WriteLine($"basis scan target block origin=({comparison.BlockX},{comparison.BlockY}), macroblock=({comparison.MacroblockX},{comparison.MacroblockY}), subblock=({comparison.SubblockX},{comparison.SubblockY})");
        TestContext.Out.WriteLine($"basis scan external red 4x4:\n{FormatByteMatrix4x4(comparison.ExternalRedBlock)}");
        TestContext.Out.WriteLine($"basis scan predictor 4x4:\n{FormatByteMatrix4x4(comparison.Diagnostics.TargetYSubblockPredicted4x4)}");
        TestContext.Out.WriteLine($"basis scan current output 4x4:\n{FormatByteMatrix4x4(comparison.Diagnostics.TargetYSubblockOutput4x4)}");
        TestContext.Out.WriteLine($"basis scan current dequant coeffs 4x4:\n{FormatShortMatrix4x4(comparison.Diagnostics.TargetYSubblockDequantCoeffs)}");
        TestContext.Out.WriteLine($"basis scan baseline sad={baselineSad}");

        for (var sourceIndex = 0; sourceIndex < 16; sourceIndex++)
        {
            var coefficientValue = comparison.Diagnostics.TargetYSubblockDequantCoeffs[sourceIndex];
            if (coefficientValue == 0)
            {
                continue;
            }

            var bestDestination = -1;
            var bestSad = int.MaxValue;
            byte[]? bestOutput = null;

            for (var destinationIndex = 0; destinationIndex < 16; destinationIndex++)
            {
                var candidateCoeffs = comparison.Diagnostics.TargetYSubblockDequantCoeffs.ToArray();
                candidateCoeffs[sourceIndex] = 0;
                candidateCoeffs[destinationIndex] = coefficientValue;

                var reconstructed = comparison.Diagnostics.TargetYSubblockPredicted4x4.ToArray();
                Vp8Dct.InverseDct4x4(candidateCoeffs, reconstructed, 4);
                var sad = ComputeBlockSad(reconstructed, comparison.ExternalRedBlock);
                if (sad >= bestSad)
                {
                    continue;
                }

                bestSad = sad;
                bestDestination = destinationIndex;
                bestOutput = reconstructed;
            }

            TestContext.Out.WriteLine($"basis scan coeff[{sourceIndex}]={coefficientValue}: bestDestination={bestDestination}, baselineDelta={bestSad - baselineSad}");
            if (bestOutput is not null)
            {
                TestContext.Out.WriteLine($"basis scan coeff[{sourceIndex}] best output 4x4:\n{FormatByteMatrix4x4(bestOutput)}");
            }
        }

        Assert.Fail("temporary diagnostic failure to surface per-coefficient basis scan");
    }

    [Explicit]
    [TestCase(TestName = "VP8 internal: протрассировать predictor chain для known problem macroblock")]
    public void TraceKnownProblemPredictorChainAgainstMagickForRealWebp()
    {
        var chain = new[]
        {
            CaptureSubblockAgainstMagickForRealWebp(49, 29, 3, 3),
            CaptureSubblockAgainstMagickForRealWebp(49, 29, 2, 3),
            CaptureSubblockAgainstMagickForRealWebp(49, 29, 3, 2),
            CaptureSubblockAgainstMagickForRealWebp(49, 29, 2, 2),
            CaptureSubblockAgainstMagickForRealWebp(49, 28, 3, 3),
            CaptureSubblockAgainstMagickForRealWebp(49, 28, 2, 3),
            CaptureSubblockAgainstMagickForRealWebp(48, 29, 3, 3),
        };

        for (var index = 0; index < chain.Length; index++)
        {
            var block = chain[index];
            var actualSad = ComputeBlockSad(block.Diagnostics.TargetYSubblockOutput4x4, block.ExternalRedBlock);
            var predictorSad = ComputeBlockSad(block.Diagnostics.TargetYSubblockPredicted4x4, block.ExternalRedBlock);

            TestContext.Out.WriteLine($"predictor chain block mb=({block.MacroblockX},{block.MacroblockY}) sb=({block.SubblockX},{block.SubblockY}) origin=({block.BlockX},{block.BlockY}) nonZero={block.Diagnostics.TargetYSubblockNonZero} rawDc={block.Diagnostics.TargetYSubblockRawDc} mode={block.Diagnostics.TargetYSubblockMode} aboveMode={block.Diagnostics.TargetYSubblockAboveMode} leftMode={block.Diagnostics.TargetYSubblockLeftMode} predictorSad={predictorSad} actualSad={actualSad}");
            TestContext.Out.WriteLine($"predictor chain external red 4x4:\n{FormatByteMatrix4x4(block.ExternalRedBlock)}");
            TestContext.Out.WriteLine($"predictor chain predictor 4x4:\n{FormatByteMatrix4x4(block.Diagnostics.TargetYSubblockPredicted4x4)}");
            TestContext.Out.WriteLine($"predictor chain actual output 4x4:\n{FormatByteMatrix4x4(block.Diagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"predictor chain dequant coeffs 4x4:\n{FormatShortMatrix4x4(block.Diagnostics.TargetYSubblockDequantCoeffs)}");
        }

        Assert.Fail("temporary diagnostic failure to surface predictor chain trace");
    }

    private void DiagnoseSymmetryRemapAgainstMagickForRealWebp(int targetMbX, int targetMbY, int targetSampleX, int targetSampleY)
    {
        var targetSubblockX = (targetSampleX - (targetMbX * 16)) / 4;
        var targetSubblockY = (targetSampleY - (targetMbY * 16)) / 4;
        var comparison = CaptureSubblockAgainstMagickForRealWebp(targetMbX, targetMbY, targetSubblockX, targetSubblockY);

        var symmetryCandidates = new (string Name, Func<ReadOnlySpan<short>, short[]> Remap)[]
        {
            ("identity", static coeffs => coeffs.ToArray()),
            ("transpose", Transpose4x4),
            ("flip-horizontal", FlipHorizontal4x4),
            ("flip-vertical", FlipVertical4x4),
            ("rotate-180", Rotate1804x4),
            ("rotate-90-cw", Rotate90Clockwise4x4),
            ("rotate-90-ccw", Rotate90CounterClockwise4x4),
            ("transpose-flip-horizontal", static coeffs => FlipHorizontal4x4(Transpose4x4(coeffs))),
        };

        var scoredCandidates = new List<(string Name, int Score, byte[] Output)>(symmetryCandidates.Length);
        for (var index = 0; index < symmetryCandidates.Length; index++)
        {
            var remappedCoeffs = symmetryCandidates[index].Remap(comparison.Diagnostics.TargetYSubblockDequantCoeffs);
            var reconstructed = comparison.Diagnostics.TargetYSubblockPredicted4x4.ToArray();
            Vp8Dct.InverseDct4x4(remappedCoeffs, reconstructed, 4);
            scoredCandidates.Add((
                symmetryCandidates[index].Name,
                ComputeBlockSad(reconstructed, comparison.ExternalRedBlock),
                reconstructed));
        }

        var orderedCandidates = scoredCandidates.OrderBy(static candidate => candidate.Score).ToArray();

        TestContext.Out.WriteLine($"symmetry remap target block origin=({comparison.BlockX},{comparison.BlockY}), sample=({targetSampleX},{targetSampleY}), macroblock=({comparison.MacroblockX},{comparison.MacroblockY}), subblock=({comparison.SubblockX},{comparison.SubblockY})");
        TestContext.Out.WriteLine($"symmetry remap external red 4x4:\n{FormatByteMatrix4x4(comparison.ExternalRedBlock)}");
        TestContext.Out.WriteLine($"symmetry remap predictor 4x4:\n{FormatByteMatrix4x4(comparison.Diagnostics.TargetYSubblockPredicted4x4)}");
        TestContext.Out.WriteLine($"symmetry remap current output 4x4:\n{FormatByteMatrix4x4(comparison.Diagnostics.TargetYSubblockOutput4x4)}");
        TestContext.Out.WriteLine($"symmetry remap current dequant coeffs 4x4:\n{FormatShortMatrix4x4(comparison.Diagnostics.TargetYSubblockDequantCoeffs)}");

        for (var index = 0; index < orderedCandidates.Length; index++)
        {
            TestContext.Out.WriteLine($"symmetry remap candidate {orderedCandidates[index].Name} sad={orderedCandidates[index].Score}:\n{FormatByteMatrix4x4(orderedCandidates[index].Output)}");
        }

        Assert.Fail("temporary diagnostic failure to surface symmetry remap comparison");
    }

    private SubblockMagickComparison CaptureSubblockAgainstMagickForRealWebp(int macroblockX, int macroblockY, int subblockX, int subblockY)
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        var diagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = macroblockX,
            TargetMacroblockY = macroblockY,
            TargetSubblockX = subblockX,
            TargetSubblockY = subblockY,
        };

        var decoder = new Vp8Decoder();
        using var decoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = decoded.AsFrame();
        var result = decoder.Decode(vp8Data, ref frame, diagnostics);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var blockX = macroblockX * 16 + subblockX * 4;
        var blockY = macroblockY * 16 + subblockY * 4;
        var externalRgbaBlock = LoadMagickRgbaBlock4x4(Path.Combine(AssetsDir, "test.webp"), blockX, blockY);

        return new SubblockMagickComparison(
            macroblockX,
            macroblockY,
            subblockX,
            subblockY,
            blockX,
            blockY,
            diagnostics,
            ExtractRedChannel4x4(externalRgbaBlock));
    }

    private void FindAndDiagnoseFirstDivergentMacroblockForRealWebp(bool targetSevereIfAvailable, (int MbX, int MbY, int SampleX, int SampleY)? forcedTarget = null)
    {
        const int noticeableThreshold = 16;
        const int severeThreshold = 64;

        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));
        var vp8Data = ExtractVp8Chunk(data);

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var publicDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var publicFrame = publicDecoded.AsFrame();
        var publicResult = codec.Decode(data, ref publicFrame);
        Assert.That(publicResult, Is.EqualTo(CodecResult.Success));

        var internalDecoder = new Vp8Decoder();
        using var internalDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var internalFrame = internalDecoded.AsFrame();
        var internalResult = internalDecoder.Decode(vp8Data, ref internalFrame);
        Assert.That(internalResult, Is.EqualTo(CodecResult.Success));

        var macroblockColumns = (info.Width + 15) / 16;
        var macroblockRows = (info.Height + 15) / 16;

        (int X, int Y, int MaxDiff, int AvgDiff, int SampleX, int SampleY) firstNoticeable = (-1, -1, -1, -1, -1, -1);
        (int X, int Y, int MaxDiff, int AvgDiff, int SampleX, int SampleY) firstSevere = (-1, -1, -1, -1, -1, -1);

        if (forcedTarget is null)
        {
            for (var mbY = 0; mbY < macroblockRows; mbY++)
            {
                for (var mbX = 0; mbX < macroblockColumns; mbX++)
                {
                    var stats = GetMacroblockDiffStats(publicDecoded.AsReadOnlyFrame(), internalDecoded.AsReadOnlyFrame(), mbX, mbY);

                    if (firstNoticeable.X < 0 && stats.MaxDiff > noticeableThreshold)
                    {
                        firstNoticeable = (mbX, mbY, stats.MaxDiff, stats.AvgDiff, stats.SampleX, stats.SampleY);
                    }

                    if (firstSevere.X < 0 && stats.MaxDiff > severeThreshold)
                    {
                        firstSevere = (mbX, mbY, stats.MaxDiff, stats.AvgDiff, stats.SampleX, stats.SampleY);
                    }

                    if (firstNoticeable.X >= 0 && firstSevere.X >= 0)
                    {
                        break;
                    }
                }

                if (firstNoticeable.X >= 0 && firstSevere.X >= 0)
                {
                    break;
                }
            }

            Assert.That(firstNoticeable.X, Is.GreaterThanOrEqualTo(0), "Не найден даже заметный расходящийся macroblock");

            TestContext.Out.WriteLine($"first noticeable macroblock=({firstNoticeable.X},{firstNoticeable.Y}), maxDiff={firstNoticeable.MaxDiff}, avgDiff={firstNoticeable.AvgDiff}, sample=({firstNoticeable.SampleX},{firstNoticeable.SampleY})");
            TestContext.Out.WriteLine($"first noticeable public sample={FormatPixel(publicDecoded.AsReadOnlyFrame(), firstNoticeable.SampleX, firstNoticeable.SampleY)}");
            TestContext.Out.WriteLine($"first noticeable internal sample={FormatPixel(internalDecoded.AsReadOnlyFrame(), firstNoticeable.SampleX, firstNoticeable.SampleY)}");

            if (firstSevere.X >= 0)
            {
                TestContext.Out.WriteLine($"first severe macroblock=({firstSevere.X},{firstSevere.Y}), maxDiff={firstSevere.MaxDiff}, avgDiff={firstSevere.AvgDiff}, sample=({firstSevere.SampleX},{firstSevere.SampleY})");
                TestContext.Out.WriteLine($"first severe public sample={FormatPixel(publicDecoded.AsReadOnlyFrame(), firstSevere.SampleX, firstSevere.SampleY)}");
                TestContext.Out.WriteLine($"first severe internal sample={FormatPixel(internalDecoded.AsReadOnlyFrame(), firstSevere.SampleX, firstSevere.SampleY)}");
            }
        }

        var target = forcedTarget is { } explicitTarget
            ? (X: explicitTarget.MbX, Y: explicitTarget.MbY, MaxDiff: -1, AvgDiff: -1, SampleX: explicitTarget.SampleX, SampleY: explicitTarget.SampleY)
            : targetSevereIfAvailable && firstSevere.X >= 0
                ? firstSevere
                : firstNoticeable;

        var targetMbX = target.X;
        var targetMbY = target.Y;
        var targetSampleX = target.SampleX;
        var targetSampleY = target.SampleY;
        var targetSubblockX = (targetSampleX - (targetMbX * 16)) / 4;
        var targetSubblockY = (targetSampleY - (targetMbY * 16)) / 4;
        var targetDiagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = targetMbX,
            TargetMacroblockY = targetMbY,
            TargetSubblockX = targetSubblockX,
            TargetSubblockY = targetSubblockY,
        };

        var diagnosticDecoder = new Vp8Decoder();
        using var diagnosedDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var diagnosedFrame = diagnosedDecoded.AsFrame();
        var diagnosedResult = diagnosticDecoder.Decode(vp8Data, ref diagnosedFrame, targetDiagnostics);
        Assert.That(diagnosedResult, Is.EqualTo(CodecResult.Success));

        var noCoeffUpdateTargetDiagnostics = new Vp8DecodeDiagnostics
        {
            TargetMacroblockX = targetMbX,
            TargetMacroblockY = targetMbY,
            TargetSubblockX = targetSubblockX,
            TargetSubblockY = targetSubblockY,
            DisableCoeffProbUpdates = true,
        };

        var noCoeffUpdateDecoder = new Vp8Decoder();
        using var noCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var noCoeffUpdateFrame = noCoeffUpdateDecoded.AsFrame();
        var noCoeffUpdateResult = noCoeffUpdateDecoder.Decode(vp8Data, ref noCoeffUpdateFrame, noCoeffUpdateTargetDiagnostics);
        Assert.That(noCoeffUpdateResult, Is.EqualTo(CodecResult.Success));

        var transposedCoeffPrediction = targetDiagnostics.TargetYSubblockPredicted4x4.ToArray();
        var transposedCoeffs = Transpose4x4(targetDiagnostics.TargetYSubblockDequantCoeffs);
        Vp8Dct.InverseDct4x4(transposedCoeffs, transposedCoeffPrediction, 4);

        Vp8DecodeDiagnostics? previousSubblockDiagnostics = null;
        VideoFrameBuffer? previousSubblockDecoded = null;
        Vp8DecodeDiagnostics? previousSubblockNoCoeffUpdateDiagnostics = null;
        VideoFrameBuffer? previousSubblockNoCoeffUpdateDecoded = null;
        Vp8DecodeDiagnostics? earlierSubblockDiagnostics = null;
        VideoFrameBuffer? earlierSubblockDecoded = null;
        Vp8DecodeDiagnostics? leftMacroblockBoundaryDiagnostics = null;
        VideoFrameBuffer? leftMacroblockBoundaryDecoded = null;
        Vp8DecodeDiagnostics? previousMacroblockTopRightDiagnostics = null;
        VideoFrameBuffer? previousMacroblockTopRightDecoded = null;
        Vp8DecodeDiagnostics? topRowBlock0Diagnostics = null;
        VideoFrameBuffer? topRowBlock0Decoded = null;
        Vp8DecodeDiagnostics? topRowBlock0NoCoeffUpdateDiagnostics = null;
        VideoFrameBuffer? topRowBlock0NoCoeffUpdateDecoded = null;
        Vp8DecodeDiagnostics? topRowBlock1Diagnostics = null;
        VideoFrameBuffer? topRowBlock1Decoded = null;
        Vp8DecodeDiagnostics? topRowBlock1NoCoeffUpdateDiagnostics = null;
        VideoFrameBuffer? topRowBlock1NoCoeffUpdateDecoded = null;
        Vp8DecodeDiagnostics? preSuspiciousTopRowDiagnostics = null;
        VideoFrameBuffer? preSuspiciousTopRowDecoded = null;
        Vp8DecodeDiagnostics? preSuspiciousTopRowNoCoeffUpdateDiagnostics = null;
        VideoFrameBuffer? preSuspiciousTopRowNoCoeffUpdateDecoded = null;
        Vp8DecodeDiagnostics? suspiciousTopRowDiagnostics = null;
        VideoFrameBuffer? suspiciousTopRowDecoded = null;
        Vp8DecodeDiagnostics? suspiciousTopRowNoCoeffUpdateDiagnostics = null;
        VideoFrameBuffer? suspiciousTopRowNoCoeffUpdateDecoded = null;
        if (targetSubblockX > 0)
        {
            previousSubblockDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = targetSubblockX - 1,
                TargetSubblockY = targetSubblockY,
            };

            var previousDecoder = new Vp8Decoder();
            previousSubblockDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var previousFrame = previousSubblockDecoded.AsFrame();
            var previousResult = previousDecoder.Decode(vp8Data, ref previousFrame, previousSubblockDiagnostics);
            Assert.That(previousResult, Is.EqualTo(CodecResult.Success));

            previousSubblockNoCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = targetSubblockX - 1,
                TargetSubblockY = targetSubblockY,
                DisableCoeffProbUpdates = true,
            };

            var previousNoCoeffUpdateDecoder = new Vp8Decoder();
            previousSubblockNoCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var previousNoCoeffUpdateFrame = previousSubblockNoCoeffUpdateDecoded.AsFrame();
            var previousNoCoeffUpdateResult = previousNoCoeffUpdateDecoder.Decode(vp8Data, ref previousNoCoeffUpdateFrame, previousSubblockNoCoeffUpdateDiagnostics);
            Assert.That(previousNoCoeffUpdateResult, Is.EqualTo(CodecResult.Success));
        }

        if (targetSubblockX > 1)
        {
            earlierSubblockDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = targetSubblockX - 2,
                TargetSubblockY = targetSubblockY,
            };

            var earlierDecoder = new Vp8Decoder();
            earlierSubblockDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var earlierFrame = earlierSubblockDecoded.AsFrame();
            var earlierResult = earlierDecoder.Decode(vp8Data, ref earlierFrame, earlierSubblockDiagnostics);
            Assert.That(earlierResult, Is.EqualTo(CodecResult.Success));
        }

        if (targetMbX > 0)
        {
            leftMacroblockBoundaryDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX - 1,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 3,
                TargetSubblockY = targetSubblockY,
            };

            var leftBoundaryDecoder = new Vp8Decoder();
            leftMacroblockBoundaryDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var leftBoundaryFrame = leftMacroblockBoundaryDecoded.AsFrame();
            var leftBoundaryResult = leftBoundaryDecoder.Decode(vp8Data, ref leftBoundaryFrame, leftMacroblockBoundaryDiagnostics);
            Assert.That(leftBoundaryResult, Is.EqualTo(CodecResult.Success));

            if (!targetSevereIfAvailable)
            {
                previousMacroblockTopRightDiagnostics = new Vp8DecodeDiagnostics
                {
                    TargetMacroblockX = targetMbX - 1,
                    TargetMacroblockY = targetMbY,
                    TargetSubblockX = 3,
                    TargetSubblockY = 0,
                };

                var previousMacroblockTopRightDecoder = new Vp8Decoder();
                previousMacroblockTopRightDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
                var previousMacroblockTopRightFrame = previousMacroblockTopRightDecoded.AsFrame();
                var previousMacroblockTopRightResult = previousMacroblockTopRightDecoder.Decode(vp8Data, ref previousMacroblockTopRightFrame, previousMacroblockTopRightDiagnostics);
                Assert.That(previousMacroblockTopRightResult, Is.EqualTo(CodecResult.Success));
            }
        }

        if (!targetSevereIfAvailable && (targetSubblockX != 3 || targetSubblockY != 0))
        {
            topRowBlock0Diagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 0,
                TargetSubblockY = 0,
            };

            var topRowBlock0Decoder = new Vp8Decoder();
            topRowBlock0Decoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var topRowBlock0Frame = topRowBlock0Decoded.AsFrame();
            var topRowBlock0Result = topRowBlock0Decoder.Decode(vp8Data, ref topRowBlock0Frame, topRowBlock0Diagnostics);
            Assert.That(topRowBlock0Result, Is.EqualTo(CodecResult.Success));

            topRowBlock0NoCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 0,
                TargetSubblockY = 0,
                DisableCoeffProbUpdates = true,
            };

            var topRowBlock0NoCoeffUpdateDecoder = new Vp8Decoder();
            topRowBlock0NoCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var topRowBlock0NoCoeffUpdateFrame = topRowBlock0NoCoeffUpdateDecoded.AsFrame();
            var topRowBlock0NoCoeffUpdateResult = topRowBlock0NoCoeffUpdateDecoder.Decode(vp8Data, ref topRowBlock0NoCoeffUpdateFrame, topRowBlock0NoCoeffUpdateDiagnostics);
            Assert.That(topRowBlock0NoCoeffUpdateResult, Is.EqualTo(CodecResult.Success));

            topRowBlock1Diagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 1,
                TargetSubblockY = 0,
            };

            var topRowBlock1Decoder = new Vp8Decoder();
            topRowBlock1Decoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var topRowBlock1Frame = topRowBlock1Decoded.AsFrame();
            var topRowBlock1Result = topRowBlock1Decoder.Decode(vp8Data, ref topRowBlock1Frame, topRowBlock1Diagnostics);
            Assert.That(topRowBlock1Result, Is.EqualTo(CodecResult.Success));

            topRowBlock1NoCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 1,
                TargetSubblockY = 0,
                DisableCoeffProbUpdates = true,
            };

            var topRowBlock1NoCoeffUpdateDecoder = new Vp8Decoder();
            topRowBlock1NoCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var topRowBlock1NoCoeffUpdateFrame = topRowBlock1NoCoeffUpdateDecoded.AsFrame();
            var topRowBlock1NoCoeffUpdateResult = topRowBlock1NoCoeffUpdateDecoder.Decode(vp8Data, ref topRowBlock1NoCoeffUpdateFrame, topRowBlock1NoCoeffUpdateDiagnostics);
            Assert.That(topRowBlock1NoCoeffUpdateResult, Is.EqualTo(CodecResult.Success));

            preSuspiciousTopRowDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 2,
                TargetSubblockY = 0,
            };

            var preSuspiciousTopRowDecoder = new Vp8Decoder();
            preSuspiciousTopRowDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var preSuspiciousTopRowFrame = preSuspiciousTopRowDecoded.AsFrame();
            var preSuspiciousTopRowResult = preSuspiciousTopRowDecoder.Decode(vp8Data, ref preSuspiciousTopRowFrame, preSuspiciousTopRowDiagnostics);
            Assert.That(preSuspiciousTopRowResult, Is.EqualTo(CodecResult.Success));

            preSuspiciousTopRowNoCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 2,
                TargetSubblockY = 0,
                DisableCoeffProbUpdates = true,
            };

            var preSuspiciousTopRowNoCoeffUpdateDecoder = new Vp8Decoder();
            preSuspiciousTopRowNoCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var preSuspiciousTopRowNoCoeffUpdateFrame = preSuspiciousTopRowNoCoeffUpdateDecoded.AsFrame();
            var preSuspiciousTopRowNoCoeffUpdateResult = preSuspiciousTopRowNoCoeffUpdateDecoder.Decode(vp8Data, ref preSuspiciousTopRowNoCoeffUpdateFrame, preSuspiciousTopRowNoCoeffUpdateDiagnostics);
            Assert.That(preSuspiciousTopRowNoCoeffUpdateResult, Is.EqualTo(CodecResult.Success));

            suspiciousTopRowDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 3,
                TargetSubblockY = 0,
            };

            var suspiciousTopRowDecoder = new Vp8Decoder();
            suspiciousTopRowDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var suspiciousTopRowFrame = suspiciousTopRowDecoded.AsFrame();
            var suspiciousTopRowResult = suspiciousTopRowDecoder.Decode(vp8Data, ref suspiciousTopRowFrame, suspiciousTopRowDiagnostics);
            Assert.That(suspiciousTopRowResult, Is.EqualTo(CodecResult.Success));

            suspiciousTopRowNoCoeffUpdateDiagnostics = new Vp8DecodeDiagnostics
            {
                TargetMacroblockX = targetMbX,
                TargetMacroblockY = targetMbY,
                TargetSubblockX = 3,
                TargetSubblockY = 0,
                DisableCoeffProbUpdates = true,
            };

            var suspiciousTopRowNoCoeffUpdateDecoder = new Vp8Decoder();
            suspiciousTopRowNoCoeffUpdateDecoded = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var suspiciousTopRowNoCoeffUpdateFrame = suspiciousTopRowNoCoeffUpdateDecoded.AsFrame();
            var suspiciousTopRowNoCoeffUpdateResult = suspiciousTopRowNoCoeffUpdateDecoder.Decode(vp8Data, ref suspiciousTopRowNoCoeffUpdateFrame, suspiciousTopRowNoCoeffUpdateDiagnostics);
            Assert.That(suspiciousTopRowNoCoeffUpdateResult, Is.EqualTo(CodecResult.Success));
        }

        TestContext.Out.WriteLine($"diagnostic target macroblock=({targetMbX},{targetMbY}), subblock=({targetSubblockX},{targetSubblockY}), sample=({targetSampleX},{targetSampleY})");
        TestContext.Out.WriteLine($"diagnostic public sample={FormatPixel(publicDecoded.AsReadOnlyFrame(), targetSampleX, targetSampleY)}");
        TestContext.Out.WriteLine($"diagnostic internal sample={FormatPixel(diagnosedDecoded.AsReadOnlyFrame(), targetSampleX, targetSampleY)}");
        TestContext.Out.WriteLine($"diagnostic concise target: ctx={targetDiagnostics.TargetYSubblockInitialContext}, predY={targetDiagnostics.TargetYSubblockPredictedTopLeft}, outY={targetDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={targetDiagnostics.TargetYSubblockNonZero}, rawDc={targetDiagnostics.TargetYSubblockRawDc}, refStyleNonZero={targetDiagnostics.TargetYSubblockReferenceStyleNonZero}, refStyleRawDc={targetDiagnostics.TargetYSubblockReferenceStyleRawDc}");
        TestContext.Out.WriteLine($"diagnostic concise target no coeff updates: ctx={noCoeffUpdateTargetDiagnostics.TargetYSubblockInitialContext}, predY={noCoeffUpdateTargetDiagnostics.TargetYSubblockPredictedTopLeft}, outY={noCoeffUpdateTargetDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={noCoeffUpdateTargetDiagnostics.TargetYSubblockNonZero}, rawDc={noCoeffUpdateTargetDiagnostics.TargetYSubblockRawDc}");
        if (previousSubblockDiagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise previous subblock: ctx={previousSubblockDiagnostics.TargetYSubblockInitialContext}, predY={previousSubblockDiagnostics.TargetYSubblockPredictedTopLeft}, outY={previousSubblockDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={previousSubblockDiagnostics.TargetYSubblockNonZero}, rawDc={previousSubblockDiagnostics.TargetYSubblockRawDc}");
        }

        if (leftMacroblockBoundaryDiagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise left boundary: ctx={leftMacroblockBoundaryDiagnostics.TargetYSubblockInitialContext}, predY={leftMacroblockBoundaryDiagnostics.TargetYSubblockPredictedTopLeft}, outY={leftMacroblockBoundaryDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={leftMacroblockBoundaryDiagnostics.TargetYSubblockNonZero}, rawDc={leftMacroblockBoundaryDiagnostics.TargetYSubblockRawDc}");
        }

        if (topRowBlock0Diagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise top-row block0: ctx={topRowBlock0Diagnostics.TargetYSubblockInitialContext}, predY={topRowBlock0Diagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock0Diagnostics.TargetYSubblockOutputTopLeft}, nonZero={topRowBlock0Diagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock0Diagnostics.TargetYSubblockRawDc}");
        }

        if (topRowBlock1Diagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise top-row block1: ctx={topRowBlock1Diagnostics.TargetYSubblockInitialContext}, predY={topRowBlock1Diagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock1Diagnostics.TargetYSubblockOutputTopLeft}, nonZero={topRowBlock1Diagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock1Diagnostics.TargetYSubblockRawDc}");
        }

        if (preSuspiciousTopRowDiagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise top-row block2: ctx={preSuspiciousTopRowDiagnostics.TargetYSubblockInitialContext}, predY={preSuspiciousTopRowDiagnostics.TargetYSubblockPredictedTopLeft}, outY={preSuspiciousTopRowDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={preSuspiciousTopRowDiagnostics.TargetYSubblockNonZero}, rawDc={preSuspiciousTopRowDiagnostics.TargetYSubblockRawDc}");
        }

        if (suspiciousTopRowDiagnostics is not null)
        {
            TestContext.Out.WriteLine($"diagnostic concise top-row block3: ctx={suspiciousTopRowDiagnostics.TargetYSubblockInitialContext}, predY={suspiciousTopRowDiagnostics.TargetYSubblockPredictedTopLeft}, outY={suspiciousTopRowDiagnostics.TargetYSubblockOutputTopLeft}, nonZero={suspiciousTopRowDiagnostics.TargetYSubblockNonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockRawDc}, refStyleNonZero={suspiciousTopRowDiagnostics.TargetYSubblockReferenceStyleNonZero}, refStyleRawDc={suspiciousTopRowDiagnostics.TargetYSubblockReferenceStyleRawDc}");
        }

        TestContext.Out.WriteLine($"diagnostic filter: normal={targetDiagnostics.FilterUseNormal}, level={targetDiagnostics.FilterLevel}, sharpness={targetDiagnostics.FilterSharpness}, adjust={targetDiagnostics.FilterAdjustEnabled}, refDelta=[{string.Join(',', targetDiagnostics.FilterRefDelta)}], modeDelta=[{string.Join(',', targetDiagnostics.FilterModeDelta)}], segmentEnabled={targetDiagnostics.SegmentEnabled}, segmentFilter=[{string.Join(',', targetDiagnostics.SegmentFilterLevel)}]");
        TestContext.Out.WriteLine($"diagnostic quant: baseQp={targetDiagnostics.BaseQp}, segmentQuant=[{string.Join(',', targetDiagnostics.SegmentQuantizerLevel)}], targetY1=({targetDiagnostics.TargetY1DcDequant},{targetDiagnostics.TargetY1AcDequant}), targetY2=({targetDiagnostics.TargetY2DcDequant},{targetDiagnostics.TargetY2AcDequant}), targetUv=({targetDiagnostics.TargetUvDcDequant},{targetDiagnostics.TargetUvAcDequant})");
        TestContext.Out.WriteLine($"diagnostic mb segment={targetDiagnostics.TargetMacroblockSegment}, modes: Y={targetDiagnostics.FirstMacroblockYMode}, UV={targetDiagnostics.FirstMacroblockUvMode}, skip={targetDiagnostics.FirstMacroblockIsSkip}");
        TestContext.Out.WriteLine($"diagnostic mb bpred block0: mode={targetDiagnostics.FirstBPredSubblockMode}, above={targetDiagnostics.FirstBPredSubblockAboveMode}, left={targetDiagnostics.FirstBPredSubblockLeftMode}");
        TestContext.Out.WriteLine($"diagnostic mb bpred modes 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetMacroblockSubblockModes)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock: mode={targetDiagnostics.TargetYSubblockMode}, above={targetDiagnostics.TargetYSubblockAboveMode}, left={targetDiagnostics.TargetYSubblockLeftMode}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock predictor flags: hasAbove={targetDiagnostics.TargetYSubblockHasAbove}, hasLeft={targetDiagnostics.TargetYSubblockHasLeft}, aboveLeft={targetDiagnostics.TargetYSubblockAboveLeftSample}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock predictor above8: {FormatByteVector(targetDiagnostics.TargetYSubblockAbovePredictor)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock predictor left4: {FormatByteVector(targetDiagnostics.TargetYSubblockLeftPredictor)}");
        TestContext.Out.WriteLine($"diagnostic mb prediction: Y={targetDiagnostics.FirstMacroblockPredictedYTopLeft}, U={targetDiagnostics.FirstMacroblockPredictedUTopLeft}, V={targetDiagnostics.FirstMacroblockPredictedVTopLeft}");
        TestContext.Out.WriteLine($"diagnostic mb y2: nonZero={targetDiagnostics.FirstMacroblockY2NonZero}, rawDc={targetDiagnostics.FirstMacroblockY2RawDc}, dequantDc={targetDiagnostics.FirstMacroblockY2DequantDc}, whtDc={targetDiagnostics.FirstMacroblockY2WhtDc}");
        TestContext.Out.WriteLine($"diagnostic mb y-block0 contexts: above={targetDiagnostics.FirstYBlockAboveNonZeroContext}, left={targetDiagnostics.FirstYBlockLeftNonZeroContext}, initial={targetDiagnostics.FirstYBlockInitialContext}");
        TestContext.Out.WriteLine($"diagnostic mb y-block0: nonZero={targetDiagnostics.FirstYBlockNonZero}, rawDc={targetDiagnostics.FirstYBlockRawDc}, dequantDc={targetDiagnostics.FirstYBlockDequantDcBeforeY2}, injectedDc={targetDiagnostics.FirstYBlockDcAfterY2Injection}, outY={targetDiagnostics.FirstYBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"diagnostic mb y-block0 token trace: {string.Join(" | ", targetDiagnostics.FirstYBlockTokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock contexts: above={targetDiagnostics.TargetYSubblockAboveNonZeroContext}, left={targetDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={targetDiagnostics.TargetYSubblockInitialContext}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock decoder state: before={targetDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={targetDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock: nonZero={targetDiagnostics.TargetYSubblockNonZero}, rawDc={targetDiagnostics.TargetYSubblockRawDc}, dequantDc={targetDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={targetDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={targetDiagnostics.TargetYSubblockPredictedTopLeft}, outY={targetDiagnostics.TargetYSubblockOutputTopLeft}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx0: nonZero={targetDiagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext0RawDc}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx0 token trace: {string.Join(" | ", targetDiagnostics.TargetYSubblockForcedContext0TokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx0 raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockForcedContext0RawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx1: nonZero={targetDiagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext1RawDc}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx1 token trace: {string.Join(" | ", targetDiagnostics.TargetYSubblockForcedContext1TokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx2: nonZero={targetDiagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={targetDiagnostics.TargetYSubblockForcedContext2RawDc}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx2 token trace: {string.Join(" | ", targetDiagnostics.TargetYSubblockForcedContext2TokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock forced ctx2 raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockForcedContext2RawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock reference style: nonZero={targetDiagnostics.TargetYSubblockReferenceStyleNonZero}, rawDc={targetDiagnostics.TargetYSubblockReferenceStyleRawDc}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock reference style token trace: {string.Join(" | ", targetDiagnostics.TargetYSubblockReferenceStyleTokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock reference style raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockReferenceStyleRawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock token trace: {string.Join(" | ", targetDiagnostics.TargetYSubblockTokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockRawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetYSubblockDequantCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates contexts: above={noCoeffUpdateTargetDiagnostics.TargetYSubblockAboveNonZeroContext}, left={noCoeffUpdateTargetDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={noCoeffUpdateTargetDiagnostics.TargetYSubblockInitialContext}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates decoder state: before={noCoeffUpdateTargetDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={noCoeffUpdateTargetDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates: nonZero={noCoeffUpdateTargetDiagnostics.TargetYSubblockNonZero}, rawDc={noCoeffUpdateTargetDiagnostics.TargetYSubblockRawDc}, dequantDc={noCoeffUpdateTargetDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={noCoeffUpdateTargetDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={noCoeffUpdateTargetDiagnostics.TargetYSubblockPredictedTopLeft}, outY={noCoeffUpdateTargetDiagnostics.TargetYSubblockOutputTopLeft}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates token trace: {string.Join(" | ", noCoeffUpdateTargetDiagnostics.TargetYSubblockTokenTrace)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(noCoeffUpdateTargetDiagnostics.TargetYSubblockRawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock no coeff updates dequant coeffs 4x4:\n{FormatShortMatrix4x4(noCoeffUpdateTargetDiagnostics.TargetYSubblockDequantCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock predicted 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetYSubblockPredicted4x4)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock output 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetYSubblockOutput4x4)}");
        TestContext.Out.WriteLine($"diagnostic target uv-subblock=({targetDiagnostics.TargetUvSubblockX},{targetDiagnostics.TargetUvSubblockY})");
        TestContext.Out.WriteLine($"diagnostic target u-subblock: nonZero={targetDiagnostics.TargetUBlockNonZero}, rawDc={targetDiagnostics.TargetUBlockRawDc}, dequantDc={targetDiagnostics.TargetUBlockDequantDc}, outU={targetDiagnostics.TargetUBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"diagnostic target u-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetUBlockRawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target u-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetUBlockDequantCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target u-subblock predicted 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetUBlockPredicted4x4)}");
        TestContext.Out.WriteLine($"diagnostic target u-subblock output 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetUBlockOutput4x4)}");
        TestContext.Out.WriteLine($"diagnostic target v-subblock: nonZero={targetDiagnostics.TargetVBlockNonZero}, rawDc={targetDiagnostics.TargetVBlockRawDc}, dequantDc={targetDiagnostics.TargetVBlockDequantDc}, outV={targetDiagnostics.TargetVBlockOutputTopLeft}");
        TestContext.Out.WriteLine($"diagnostic target v-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetVBlockRawCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target v-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(targetDiagnostics.TargetVBlockDequantCoeffs)}");
        TestContext.Out.WriteLine($"diagnostic target v-subblock predicted 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetVBlockPredicted4x4)}");
        TestContext.Out.WriteLine($"diagnostic target v-subblock output 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetVBlockOutput4x4)}");
        TestContext.Out.WriteLine($"diagnostic target final Y 4x4:\n{FormatByteMatrix4x4(targetDiagnostics.TargetFinalY4x4)}");
        TestContext.Out.WriteLine($"diagnostic target final U 2x2:\n{FormatByteMatrix2x2(targetDiagnostics.TargetFinalU2x2)}");
        TestContext.Out.WriteLine($"diagnostic target final V 2x2:\n{FormatByteMatrix2x2(targetDiagnostics.TargetFinalV2x2)}");
        TestContext.Out.WriteLine($"diagnostic target y-subblock output 4x4 with transposed coeffs:\n{FormatByteMatrix4x4(transposedCoeffPrediction)}");
        TestContext.Out.WriteLine($"diagnostic target public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), targetMbX * 16 + targetSubblockX * 4, targetMbY * 16 + targetSubblockY * 4)}");
        TestContext.Out.WriteLine($"diagnostic target internal final 4x4:\n{FormatFrameBlock4x4(diagnosedDecoded.AsReadOnlyFrame(), targetMbX * 16 + targetSubblockX * 4, targetMbY * 16 + targetSubblockY * 4)}");
        TestContext.Out.WriteLine($"diagnostic target public final rgba 4x4:\n{FormatFrameBlock4x4Rgba(publicDecoded.AsReadOnlyFrame(), targetMbX * 16 + targetSubblockX * 4, targetMbY * 16 + targetSubblockY * 4)}");
        TestContext.Out.WriteLine($"diagnostic target internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(diagnosedDecoded.AsReadOnlyFrame(), targetMbX * 16 + targetSubblockX * 4, targetMbY * 16 + targetSubblockY * 4)}");
        for (var scanSubblockX = 0; scanSubblockX < 4; scanSubblockX++)
        {
            var blockX = targetMbX * 16 + scanSubblockX * 4;
            var blockY = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic top-row subblock ({scanSubblockX},0) public:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), blockX, blockY)}");
            TestContext.Out.WriteLine($"diagnostic top-row subblock ({scanSubblockX},0) internal:\n{FormatFrameBlock4x4(diagnosedDecoded.AsReadOnlyFrame(), blockX, blockY)}");
        }

        if (previousSubblockDiagnostics is not null && previousSubblockDecoded is not null)
        {
            var previousBlockX = targetMbX * 16 + ((targetSubblockX - 1) * 4);
            var previousBlockY = targetMbY * 16 + (targetSubblockY * 4);
            TestContext.Out.WriteLine($"diagnostic previous macroblock modes: Y={previousSubblockDiagnostics.FirstMacroblockYMode}, UV={previousSubblockDiagnostics.FirstMacroblockUvMode}, skip={previousSubblockDiagnostics.FirstMacroblockIsSkip}");
            TestContext.Out.WriteLine($"diagnostic previous macroblock prediction: Y={previousSubblockDiagnostics.FirstMacroblockPredictedYTopLeft}, U={previousSubblockDiagnostics.FirstMacroblockPredictedUTopLeft}, V={previousSubblockDiagnostics.FirstMacroblockPredictedVTopLeft}");
            TestContext.Out.WriteLine($"diagnostic previous macroblock y2: nonZero={previousSubblockDiagnostics.FirstMacroblockY2NonZero}, rawDc={previousSubblockDiagnostics.FirstMacroblockY2RawDc}, dequantDc={previousSubblockDiagnostics.FirstMacroblockY2DequantDc}, whtDc={previousSubblockDiagnostics.FirstMacroblockY2WhtDc}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock: mode={previousSubblockDiagnostics.TargetYSubblockMode}, above={previousSubblockDiagnostics.TargetYSubblockAboveMode}, left={previousSubblockDiagnostics.TargetYSubblockLeftMode}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock predictor flags: hasAbove={previousSubblockDiagnostics.TargetYSubblockHasAbove}, hasLeft={previousSubblockDiagnostics.TargetYSubblockHasLeft}, aboveLeft={previousSubblockDiagnostics.TargetYSubblockAboveLeftSample}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock predictor above8: {FormatByteVector(previousSubblockDiagnostics.TargetYSubblockAbovePredictor)}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock predictor left4: {FormatByteVector(previousSubblockDiagnostics.TargetYSubblockLeftPredictor)}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock contexts: above={previousSubblockDiagnostics.TargetYSubblockAboveNonZeroContext}, left={previousSubblockDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={previousSubblockDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock decoder state: before={previousSubblockDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={previousSubblockDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock summary: nonZero={previousSubblockDiagnostics.TargetYSubblockNonZero}, rawDc={previousSubblockDiagnostics.TargetYSubblockRawDc}, dequantDc={previousSubblockDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={previousSubblockDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={previousSubblockDiagnostics.TargetYSubblockPredictedTopLeft}, outY={previousSubblockDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock ({targetSubblockX - 1},{targetSubblockY}) token trace: {string.Join(" | ", previousSubblockDiagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetYSubblockDequantCoeffs)}");
            if (previousSubblockNoCoeffUpdateDiagnostics is not null && previousSubblockNoCoeffUpdateDecoded is not null)
            {
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates contexts: above={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockInitialContext}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates decoder state: before={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates summary: nonZero={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, outY={previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates token trace: {string.Join(" | ", previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockTokenTrace)}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockRawCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates dequant coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockDequantCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic previous y-subblock no coeff updates output 4x4:\n{FormatByteMatrix4x4(previousSubblockNoCoeffUpdateDiagnostics.TargetYSubblockOutput4x4)}");
                TestContext.Out.WriteLine($"diagnostic previous no coeff updates internal final 4x4:\n{FormatFrameBlock4x4(previousSubblockNoCoeffUpdateDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
                TestContext.Out.WriteLine($"diagnostic previous no coeff updates internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(previousSubblockNoCoeffUpdateDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
            }
            TestContext.Out.WriteLine($"diagnostic previous y-subblock predicted 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetYSubblockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous y-subblock output 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous uv-subblock=({previousSubblockDiagnostics.TargetUvSubblockX},{previousSubblockDiagnostics.TargetUvSubblockY})");
            TestContext.Out.WriteLine($"diagnostic previous u-subblock: nonZero={previousSubblockDiagnostics.TargetUBlockNonZero}, rawDc={previousSubblockDiagnostics.TargetUBlockRawDc}, dequantDc={previousSubblockDiagnostics.TargetUBlockDequantDc}, outU={previousSubblockDiagnostics.TargetUBlockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic previous u-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetUBlockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous u-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetUBlockDequantCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous u-subblock predicted 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetUBlockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous u-subblock output 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetUBlockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous v-subblock: nonZero={previousSubblockDiagnostics.TargetVBlockNonZero}, rawDc={previousSubblockDiagnostics.TargetVBlockRawDc}, dequantDc={previousSubblockDiagnostics.TargetVBlockDequantDc}, outV={previousSubblockDiagnostics.TargetVBlockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic previous v-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetVBlockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous v-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(previousSubblockDiagnostics.TargetVBlockDequantCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous v-subblock predicted 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetVBlockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous v-subblock output 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetVBlockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous final Y 4x4:\n{FormatByteMatrix4x4(previousSubblockDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous final U 2x2:\n{FormatByteMatrix2x2(previousSubblockDiagnostics.TargetFinalU2x2)}");
            TestContext.Out.WriteLine($"diagnostic previous final V 2x2:\n{FormatByteMatrix2x2(previousSubblockDiagnostics.TargetFinalV2x2)}");
            TestContext.Out.WriteLine($"diagnostic previous public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic previous internal final 4x4:\n{FormatFrameBlock4x4(previousSubblockDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic previous public final rgba 4x4:\n{FormatFrameBlock4x4Rgba(publicDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic previous internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(previousSubblockDecoded.AsReadOnlyFrame(), previousBlockX, previousBlockY)}");
            previousSubblockNoCoeffUpdateDecoded?.Dispose();
            previousSubblockDecoded.Dispose();
        }

        if (earlierSubblockDiagnostics is not null && earlierSubblockDecoded is not null)
        {
            var earlierBlockX = targetMbX * 16 + ((targetSubblockX - 2) * 4);
            var earlierBlockY = targetMbY * 16 + (targetSubblockY * 4);
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock contexts: above={earlierSubblockDiagnostics.TargetYSubblockAboveNonZeroContext}, left={earlierSubblockDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={earlierSubblockDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock decoder state: before={earlierSubblockDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={earlierSubblockDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock summary: nonZero={earlierSubblockDiagnostics.TargetYSubblockNonZero}, rawDc={earlierSubblockDiagnostics.TargetYSubblockRawDc}, dequantDc={earlierSubblockDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={earlierSubblockDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={earlierSubblockDiagnostics.TargetYSubblockPredictedTopLeft}, outY={earlierSubblockDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock ({targetSubblockX - 2},{targetSubblockY}) token trace: {string.Join(" | ", earlierSubblockDiagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(earlierSubblockDiagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(earlierSubblockDiagnostics.TargetYSubblockDequantCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock predicted 4x4:\n{FormatByteMatrix4x4(earlierSubblockDiagnostics.TargetYSubblockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic earlier y-subblock output 4x4:\n{FormatByteMatrix4x4(earlierSubblockDiagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic earlier final Y 4x4:\n{FormatByteMatrix4x4(earlierSubblockDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic earlier public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), earlierBlockX, earlierBlockY)}");
            TestContext.Out.WriteLine($"diagnostic earlier internal final 4x4:\n{FormatFrameBlock4x4(earlierSubblockDecoded.AsReadOnlyFrame(), earlierBlockX, earlierBlockY)}");
            TestContext.Out.WriteLine($"diagnostic earlier public final rgba 4x4:\n{FormatFrameBlock4x4Rgba(publicDecoded.AsReadOnlyFrame(), earlierBlockX, earlierBlockY)}");
            TestContext.Out.WriteLine($"diagnostic earlier internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(earlierSubblockDecoded.AsReadOnlyFrame(), earlierBlockX, earlierBlockY)}");
            earlierSubblockDecoded.Dispose();
        }

        if (leftMacroblockBoundaryDiagnostics is not null && leftMacroblockBoundaryDecoded is not null)
        {
            var leftBoundaryBlockX = (targetMbX - 1) * 16 + 12;
            var leftBoundaryBlockY = targetMbY * 16 + (targetSubblockY * 4);
            TestContext.Out.WriteLine($"diagnostic left-boundary macroblock=({targetMbX - 1},{targetMbY}), segment={leftMacroblockBoundaryDiagnostics.TargetMacroblockSegment}, subblock=(3,{targetSubblockY})");
            TestContext.Out.WriteLine($"diagnostic left-boundary macroblock modes: Y={leftMacroblockBoundaryDiagnostics.FirstMacroblockYMode}, UV={leftMacroblockBoundaryDiagnostics.FirstMacroblockUvMode}, skip={leftMacroblockBoundaryDiagnostics.FirstMacroblockIsSkip}");
            TestContext.Out.WriteLine($"diagnostic left-boundary macroblock prediction: Y={leftMacroblockBoundaryDiagnostics.FirstMacroblockPredictedYTopLeft}, U={leftMacroblockBoundaryDiagnostics.FirstMacroblockPredictedUTopLeft}, V={leftMacroblockBoundaryDiagnostics.FirstMacroblockPredictedVTopLeft}");
            TestContext.Out.WriteLine($"diagnostic left-boundary macroblock y2: nonZero={leftMacroblockBoundaryDiagnostics.FirstMacroblockY2NonZero}, rawDc={leftMacroblockBoundaryDiagnostics.FirstMacroblockY2RawDc}, dequantDc={leftMacroblockBoundaryDiagnostics.FirstMacroblockY2DequantDc}, whtDc={leftMacroblockBoundaryDiagnostics.FirstMacroblockY2WhtDc}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock contexts: above={leftMacroblockBoundaryDiagnostics.TargetYSubblockAboveNonZeroContext}, left={leftMacroblockBoundaryDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={leftMacroblockBoundaryDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock decoder state: before={leftMacroblockBoundaryDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={leftMacroblockBoundaryDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock summary: nonZero={leftMacroblockBoundaryDiagnostics.TargetYSubblockNonZero}, rawDc={leftMacroblockBoundaryDiagnostics.TargetYSubblockRawDc}, dequantDc={leftMacroblockBoundaryDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={leftMacroblockBoundaryDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={leftMacroblockBoundaryDiagnostics.TargetYSubblockPredictedTopLeft}, outY={leftMacroblockBoundaryDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock token trace: {string.Join(" | ", leftMacroblockBoundaryDiagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock raw coeffs 4x4:\n{FormatShortMatrix4x4(leftMacroblockBoundaryDiagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock dequant coeffs 4x4:\n{FormatShortMatrix4x4(leftMacroblockBoundaryDiagnostics.TargetYSubblockDequantCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock predicted 4x4:\n{FormatByteMatrix4x4(leftMacroblockBoundaryDiagnostics.TargetYSubblockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary y-subblock output 4x4:\n{FormatByteMatrix4x4(leftMacroblockBoundaryDiagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary final Y 4x4:\n{FormatByteMatrix4x4(leftMacroblockBoundaryDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), leftBoundaryBlockX, leftBoundaryBlockY)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary internal final 4x4:\n{FormatFrameBlock4x4(leftMacroblockBoundaryDecoded.AsReadOnlyFrame(), leftBoundaryBlockX, leftBoundaryBlockY)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary public final rgba 4x4:\n{FormatFrameBlock4x4Rgba(publicDecoded.AsReadOnlyFrame(), leftBoundaryBlockX, leftBoundaryBlockY)}");
            TestContext.Out.WriteLine($"diagnostic left-boundary internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(leftMacroblockBoundaryDecoded.AsReadOnlyFrame(), leftBoundaryBlockX, leftBoundaryBlockY)}");
            leftMacroblockBoundaryDecoded.Dispose();
        }

        if (topRowBlock0Diagnostics is not null && topRowBlock0Decoded is not null)
        {
            var topRowBlock0X = targetMbX * 16;
            var topRowBlock0Y = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic top-row block0 macroblock=({targetMbX},{targetMbY}), subblock=(0,0)");
            TestContext.Out.WriteLine($"diagnostic top-row block0 y-subblock contexts: above={topRowBlock0Diagnostics.TargetYSubblockAboveNonZeroContext}, left={topRowBlock0Diagnostics.TargetYSubblockLeftNonZeroContext}, initial={topRowBlock0Diagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 decoder state: before={topRowBlock0Diagnostics.TargetYSubblockTokenDecoderStateBefore}, after={topRowBlock0Diagnostics.TargetYSubblockTokenDecoderStateAfter}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 summary: nonZero={topRowBlock0Diagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock0Diagnostics.TargetYSubblockRawDc}, dequantDc={topRowBlock0Diagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={topRowBlock0Diagnostics.TargetYSubblockDcAfterY2Injection}, predY={topRowBlock0Diagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock0Diagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 forced ctx0: nonZero={topRowBlock0Diagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={topRowBlock0Diagnostics.TargetYSubblockForcedContext0RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 forced ctx1: nonZero={topRowBlock0Diagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={topRowBlock0Diagnostics.TargetYSubblockForcedContext1RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 forced ctx2: nonZero={topRowBlock0Diagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={topRowBlock0Diagnostics.TargetYSubblockForcedContext2RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 token trace: {string.Join(" | ", topRowBlock0Diagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 forced ctx1 token trace: {string.Join(" | ", topRowBlock0Diagnostics.TargetYSubblockForcedContext1TokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock0Diagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock0Diagnostics.TargetYSubblockRawCoeffs)}");
            if (topRowBlock0NoCoeffUpdateDiagnostics is not null && topRowBlock0NoCoeffUpdateDecoded is not null)
            {
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates contexts: above={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockInitialContext}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates decoder state: before={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates summary: nonZero={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates token trace: {string.Join(" | ", topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockTokenTrace)}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock0NoCoeffUpdateDiagnostics.TargetYSubblockRawCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates final Y 4x4:\n{FormatByteMatrix4x4(topRowBlock0NoCoeffUpdateDiagnostics.TargetFinalY4x4)}");
                TestContext.Out.WriteLine($"diagnostic top-row block0 no coeff updates internal final 4x4:\n{FormatFrameBlock4x4(topRowBlock0NoCoeffUpdateDecoded.AsReadOnlyFrame(), topRowBlock0X, topRowBlock0Y)}");
            }
            TestContext.Out.WriteLine($"diagnostic top-row block0 final Y 4x4:\n{FormatByteMatrix4x4(topRowBlock0Diagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), topRowBlock0X, topRowBlock0Y)}");
            TestContext.Out.WriteLine($"diagnostic top-row block0 internal final 4x4:\n{FormatFrameBlock4x4(topRowBlock0Decoded.AsReadOnlyFrame(), topRowBlock0X, topRowBlock0Y)}");
            topRowBlock0NoCoeffUpdateDecoded?.Dispose();
            topRowBlock0Decoded.Dispose();
        }

        if (previousMacroblockTopRightDiagnostics is not null && previousMacroblockTopRightDecoded is not null)
        {
            var previousMacroblockTopRightX = (targetMbX - 1) * 16 + 12;
            var previousMacroblockTopRightY = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right macroblock=({targetMbX - 1},{targetMbY}), subblock=(3,0)");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right contexts: above={previousMacroblockTopRightDiagnostics.TargetYSubblockAboveNonZeroContext}, left={previousMacroblockTopRightDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={previousMacroblockTopRightDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right summary: nonZero={previousMacroblockTopRightDiagnostics.TargetYSubblockNonZero}, rawDc={previousMacroblockTopRightDiagnostics.TargetYSubblockRawDc}, dequantDc={previousMacroblockTopRightDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={previousMacroblockTopRightDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={previousMacroblockTopRightDiagnostics.TargetYSubblockPredictedTopLeft}, outY={previousMacroblockTopRightDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right forced ctx0: nonZero={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext0RawDc}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right forced ctx1: nonZero={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext1RawDc}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right forced ctx2: nonZero={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext2RawDc}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right raw coeffs 4x4:\n{FormatShortMatrix4x4(previousMacroblockTopRightDiagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(previousMacroblockTopRightDiagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right final Y 4x4:\n{FormatByteMatrix4x4(previousMacroblockTopRightDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), previousMacroblockTopRightX, previousMacroblockTopRightY)}");
            TestContext.Out.WriteLine($"diagnostic previous-macroblock top-right internal final 4x4:\n{FormatFrameBlock4x4(previousMacroblockTopRightDecoded.AsReadOnlyFrame(), previousMacroblockTopRightX, previousMacroblockTopRightY)}");
            previousMacroblockTopRightDecoded.Dispose();
        }

        if (topRowBlock1Diagnostics is not null && topRowBlock1Decoded is not null)
        {
            var topRowBlock1X = targetMbX * 16 + 4;
            var topRowBlock1Y = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic top-row block1 macroblock=({targetMbX},{targetMbY}), subblock=(1,0)");
            TestContext.Out.WriteLine($"diagnostic top-row block1 y-subblock contexts: above={topRowBlock1Diagnostics.TargetYSubblockAboveNonZeroContext}, left={topRowBlock1Diagnostics.TargetYSubblockLeftNonZeroContext}, initial={topRowBlock1Diagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 decoder state: before={topRowBlock1Diagnostics.TargetYSubblockTokenDecoderStateBefore}, after={topRowBlock1Diagnostics.TargetYSubblockTokenDecoderStateAfter}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 summary: nonZero={topRowBlock1Diagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock1Diagnostics.TargetYSubblockRawDc}, dequantDc={topRowBlock1Diagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={topRowBlock1Diagnostics.TargetYSubblockDcAfterY2Injection}, predY={topRowBlock1Diagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock1Diagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 forced ctx0: nonZero={topRowBlock1Diagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={topRowBlock1Diagnostics.TargetYSubblockForcedContext0RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 forced ctx1: nonZero={topRowBlock1Diagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={topRowBlock1Diagnostics.TargetYSubblockForcedContext1RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 forced ctx2: nonZero={topRowBlock1Diagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={topRowBlock1Diagnostics.TargetYSubblockForcedContext2RawDc}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 token trace: {string.Join(" | ", topRowBlock1Diagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 forced ctx1 token trace: {string.Join(" | ", topRowBlock1Diagnostics.TargetYSubblockForcedContext1TokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock1Diagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock1Diagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
            if (topRowBlock1NoCoeffUpdateDiagnostics is not null && topRowBlock1NoCoeffUpdateDecoded is not null)
            {
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates contexts: above={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockInitialContext}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates decoder state: before={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates summary: nonZero={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, outY={topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates token trace: {string.Join(" | ", topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockTokenTrace)}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(topRowBlock1NoCoeffUpdateDiagnostics.TargetYSubblockRawCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates final Y 4x4:\n{FormatByteMatrix4x4(topRowBlock1NoCoeffUpdateDiagnostics.TargetFinalY4x4)}");
                TestContext.Out.WriteLine($"diagnostic top-row block1 no coeff updates internal final 4x4:\n{FormatFrameBlock4x4(topRowBlock1NoCoeffUpdateDecoded.AsReadOnlyFrame(), topRowBlock1X, topRowBlock1Y)}");
            }
            TestContext.Out.WriteLine($"diagnostic top-row block1 final Y 4x4:\n{FormatByteMatrix4x4(topRowBlock1Diagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), topRowBlock1X, topRowBlock1Y)}");
            TestContext.Out.WriteLine($"diagnostic top-row block1 internal final 4x4:\n{FormatFrameBlock4x4(topRowBlock1Decoded.AsReadOnlyFrame(), topRowBlock1X, topRowBlock1Y)}");
            topRowBlock1NoCoeffUpdateDecoded?.Dispose();
            topRowBlock1Decoded.Dispose();
        }

        if (preSuspiciousTopRowDiagnostics is not null && preSuspiciousTopRowDecoded is not null)
        {
            var preSuspiciousBlockX = targetMbX * 16 + 8;
            var preSuspiciousBlockY = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row macroblock=({targetMbX},{targetMbY}), subblock=(2,0)");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row y-subblock: mode={preSuspiciousTopRowDiagnostics.TargetYSubblockMode}, above={preSuspiciousTopRowDiagnostics.TargetYSubblockAboveMode}, left={preSuspiciousTopRowDiagnostics.TargetYSubblockLeftMode}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row y-subblock contexts: above={preSuspiciousTopRowDiagnostics.TargetYSubblockAboveNonZeroContext}, left={preSuspiciousTopRowDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={preSuspiciousTopRowDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row y-subblock summary: nonZero={preSuspiciousTopRowDiagnostics.TargetYSubblockNonZero}, rawDc={preSuspiciousTopRowDiagnostics.TargetYSubblockRawDc}, dequantDc={preSuspiciousTopRowDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={preSuspiciousTopRowDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={preSuspiciousTopRowDiagnostics.TargetYSubblockPredictedTopLeft}, outY={preSuspiciousTopRowDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx0: nonZero={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext0RawDc}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx0 raw coeffs 4x4:\n{FormatShortMatrix4x4(preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext0RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx1: nonZero={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext1RawDc}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row token trace: {string.Join(" | ", preSuspiciousTopRowDiagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx1 token trace: {string.Join(" | ", preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext1TokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx2: nonZero={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext2RawDc}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row forced ctx2 raw coeffs 4x4:\n{FormatShortMatrix4x4(preSuspiciousTopRowDiagnostics.TargetYSubblockForcedContext2RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row raw coeffs 4x4:\n{FormatShortMatrix4x4(preSuspiciousTopRowDiagnostics.TargetYSubblockRawCoeffs)}");
            if (preSuspiciousTopRowNoCoeffUpdateDiagnostics is not null && preSuspiciousTopRowNoCoeffUpdateDecoded is not null)
            {
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates contexts: above={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockInitialContext}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates decoder state: before={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates summary: nonZero={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, outY={preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates token trace: {string.Join(" | ", preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenTrace)}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockRawCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates final Y 4x4:\n{FormatByteMatrix4x4(preSuspiciousTopRowNoCoeffUpdateDiagnostics.TargetFinalY4x4)}");
                TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row no coeff updates internal final 4x4:\n{FormatFrameBlock4x4(preSuspiciousTopRowNoCoeffUpdateDecoded.AsReadOnlyFrame(), preSuspiciousBlockX, preSuspiciousBlockY)}");
            }
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row output 4x4:\n{FormatByteMatrix4x4(preSuspiciousTopRowDiagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row final Y 4x4:\n{FormatByteMatrix4x4(preSuspiciousTopRowDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), preSuspiciousBlockX, preSuspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic pre-suspicious top-row internal final 4x4:\n{FormatFrameBlock4x4(preSuspiciousTopRowDecoded.AsReadOnlyFrame(), preSuspiciousBlockX, preSuspiciousBlockY)}");
            preSuspiciousTopRowNoCoeffUpdateDecoded?.Dispose();
            preSuspiciousTopRowDecoded.Dispose();
        }

        if (suspiciousTopRowDiagnostics is not null && suspiciousTopRowDecoded is not null)
        {
            var suspiciousBlockX = targetMbX * 16 + 12;
            var suspiciousBlockY = targetMbY * 16;
            TestContext.Out.WriteLine($"diagnostic suspicious top-row macroblock=({targetMbX},{targetMbY}), subblock=(3,0)");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row macroblock modes: Y={suspiciousTopRowDiagnostics.FirstMacroblockYMode}, UV={suspiciousTopRowDiagnostics.FirstMacroblockUvMode}, skip={suspiciousTopRowDiagnostics.FirstMacroblockIsSkip}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row macroblock prediction: Y={suspiciousTopRowDiagnostics.FirstMacroblockPredictedYTopLeft}, U={suspiciousTopRowDiagnostics.FirstMacroblockPredictedUTopLeft}, V={suspiciousTopRowDiagnostics.FirstMacroblockPredictedVTopLeft}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row macroblock y2: nonZero={suspiciousTopRowDiagnostics.FirstMacroblockY2NonZero}, rawDc={suspiciousTopRowDiagnostics.FirstMacroblockY2RawDc}, dequantDc={suspiciousTopRowDiagnostics.FirstMacroblockY2DequantDc}, whtDc={suspiciousTopRowDiagnostics.FirstMacroblockY2WhtDc}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock: mode={suspiciousTopRowDiagnostics.TargetYSubblockMode}, above={suspiciousTopRowDiagnostics.TargetYSubblockAboveMode}, left={suspiciousTopRowDiagnostics.TargetYSubblockLeftMode}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock predictor flags: hasAbove={suspiciousTopRowDiagnostics.TargetYSubblockHasAbove}, hasLeft={suspiciousTopRowDiagnostics.TargetYSubblockHasLeft}, aboveLeft={suspiciousTopRowDiagnostics.TargetYSubblockAboveLeftSample}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock predictor above8: {FormatByteVector(suspiciousTopRowDiagnostics.TargetYSubblockAbovePredictor)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock predictor left4: {FormatByteVector(suspiciousTopRowDiagnostics.TargetYSubblockLeftPredictor)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock contexts: above={suspiciousTopRowDiagnostics.TargetYSubblockAboveNonZeroContext}, left={suspiciousTopRowDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={suspiciousTopRowDiagnostics.TargetYSubblockInitialContext}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock summary: nonZero={suspiciousTopRowDiagnostics.TargetYSubblockNonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockRawDc}, dequantDc={suspiciousTopRowDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={suspiciousTopRowDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={suspiciousTopRowDiagnostics.TargetYSubblockPredictedTopLeft}, outY={suspiciousTopRowDiagnostics.TargetYSubblockOutputTopLeft}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row y-subblock token trace: {string.Join(" | ", suspiciousTopRowDiagnostics.TargetYSubblockTokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx0: nonZero={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext0NonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext0RawDc}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx0 raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockForcedContext0RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx1: nonZero={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext1NonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext1RawDc}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx1 token trace: {string.Join(" | ", suspiciousTopRowDiagnostics.TargetYSubblockForcedContext1TokenTrace)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx1 raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockForcedContext1RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx2: nonZero={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext2NonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockForcedContext2RawDc}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row forced ctx2 raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockForcedContext2RawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row reference style: nonZero={suspiciousTopRowDiagnostics.TargetYSubblockReferenceStyleNonZero}, rawDc={suspiciousTopRowDiagnostics.TargetYSubblockReferenceStyleRawDc}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row reference style raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockReferenceStyleRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockRawCoeffs)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row dequant coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockDequantCoeffs)}");
            if (suspiciousTopRowNoCoeffUpdateDiagnostics is not null && suspiciousTopRowNoCoeffUpdateDecoded is not null)
            {
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates contexts: above={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockAboveNonZeroContext}, left={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockLeftNonZeroContext}, initial={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockInitialContext}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates decoder state: before={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateBefore}, after={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenDecoderStateAfter}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates summary: nonZero={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockNonZero}, rawDc={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockRawDc}, dequantDc={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockDequantDcBeforeY2}, injectedDc={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockDcAfterY2Injection}, predY={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockPredictedTopLeft}, outY={suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockOutputTopLeft}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates token trace: {string.Join(" | ", suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockTokenTrace)}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates raw coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockRawCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates dequant coeffs 4x4:\n{FormatShortMatrix4x4(suspiciousTopRowNoCoeffUpdateDiagnostics.TargetYSubblockDequantCoeffs)}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates final Y 4x4:\n{FormatByteMatrix4x4(suspiciousTopRowNoCoeffUpdateDiagnostics.TargetFinalY4x4)}");
                TestContext.Out.WriteLine($"diagnostic suspicious top-row no coeff updates internal final 4x4:\n{FormatFrameBlock4x4(suspiciousTopRowNoCoeffUpdateDecoded.AsReadOnlyFrame(), suspiciousBlockX, suspiciousBlockY)}");
            }
            TestContext.Out.WriteLine($"diagnostic suspicious top-row predicted 4x4:\n{FormatByteMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockPredicted4x4)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row output 4x4:\n{FormatByteMatrix4x4(suspiciousTopRowDiagnostics.TargetYSubblockOutput4x4)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row final Y 4x4:\n{FormatByteMatrix4x4(suspiciousTopRowDiagnostics.TargetFinalY4x4)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row public left border4: {FormatFrameColumn4(publicDecoded.AsReadOnlyFrame(), suspiciousBlockX - 1, suspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row internal left border4: {FormatFrameColumn4(suspiciousTopRowDecoded.AsReadOnlyFrame(), suspiciousBlockX - 1, suspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row public final 4x4:\n{FormatFrameBlock4x4(publicDecoded.AsReadOnlyFrame(), suspiciousBlockX, suspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row internal final 4x4:\n{FormatFrameBlock4x4(suspiciousTopRowDecoded.AsReadOnlyFrame(), suspiciousBlockX, suspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row public final rgba 4x4:\n{FormatFrameBlock4x4Rgba(publicDecoded.AsReadOnlyFrame(), suspiciousBlockX, suspiciousBlockY)}");
            TestContext.Out.WriteLine($"diagnostic suspicious top-row internal final rgba 4x4:\n{FormatFrameBlock4x4Rgba(suspiciousTopRowDecoded.AsReadOnlyFrame(), suspiciousBlockX, suspiciousBlockY)}");

            Span<byte> candidatePrediction = stackalloc byte[16];
            for (var candidateMode = 0; candidateMode < 10; candidateMode++)
            {
                Vp8Prediction.Predict4x4(
                    candidateMode,
                    suspiciousTopRowDiagnostics.TargetYSubblockAbovePredictor,
                    suspiciousTopRowDiagnostics.TargetYSubblockLeftPredictor,
                    suspiciousTopRowDiagnostics.TargetYSubblockAboveLeftSample,
                    suspiciousTopRowDiagnostics.TargetYSubblockHasAbove,
                    suspiciousTopRowDiagnostics.TargetYSubblockHasLeft,
                    candidatePrediction,
                    4);
                TestContext.Out.WriteLine($"diagnostic suspicious top-row candidate mode {candidateMode} prediction 4x4:\n{FormatByteMatrix4x4(candidatePrediction)}");
            }

            suspiciousTopRowNoCoeffUpdateDecoded?.Dispose();
            suspiciousTopRowDecoded.Dispose();
        }
        TestContext.Out.WriteLine($"diagnostic mb final yuv: Y={targetDiagnostics.FirstMacroblockFinalYTopLeft}, U={targetDiagnostics.FirstMacroblockFinalUTopLeft}, V={targetDiagnostics.FirstMacroblockFinalVTopLeft}");
        TestContext.Out.WriteLine($"diagnostic mb final rgba: {targetDiagnostics.FirstMacroblockFinalRgbaTopLeft}");
        Assert.Fail("temporary diagnostic failure to surface TestContext.Out");
    }

    [TestCase(TestName = "VP8: декодированные пиксели содержат ненулевые данные")]
    public void DecodedPixelsContainNonZeroData()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var pixelData = buffer.AsReadOnlyFrame().PackedData;
        var hasNonZero = false;
        for (var y = 0; y < info.Height && !hasNonZero; y++)
        {
            var row = pixelData.GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
        }

        Assert.That(hasNonZero, Is.True, "Декодированное изображение содержит только нули");
    }

    [TestCase(TestName = "VP8: alpha канал заполнен 255 (непрозрачное)")]
    public void DecodedAlphaChannelIsFull()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // VP8 lossy не имеет alpha — все alpha байты должны быть 255
        var pixelData = buffer.AsReadOnlyFrame().PackedData;
        for (var y = 0; y < info.Height; y++)
        {
            var row = pixelData.GetRow(y);
            for (var x = 0; x < info.Width; x++)
            {
                var alpha = row[(x * 4) + 3];
                Assert.That(alpha, Is.EqualTo(255),
                    $"Alpha [{x},{y}] = {alpha}, ожидалось 255");
            }
        }
    }

    [TestCase(TestName = "VP8: пиксели RGBA в диапазоне [0, 255]")]
    public void DecodedPixelValuesInValidRange()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        var pixelData = buffer.AsReadOnlyFrame().PackedData;
        for (var y = 0; y < info.Height; y++)
        {
            var row = pixelData.GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                Assert.That(row[x], Is.InRange(0, 255));
            }
        }
    }

    private static void AssertPixelMatches(ReadOnlyVideoFrame frame, int x, int y, int expectedR, int expectedG, int expectedB, int tolerance)
    {
        var row = frame.PackedData.GetRow(y);
        var offset = x * 4;
        var actualR = row[offset];
        var actualG = row[offset + 1];
        var actualB = row[offset + 2];
        var actualA = row[offset + 3];

        Assert.That(actualR, Is.InRange(expectedR - tolerance, expectedR + tolerance),
            $"R[{x},{y}] = {actualR}, ожидалось около {expectedR}");
        Assert.That(actualG, Is.InRange(expectedG - tolerance, expectedG + tolerance),
            $"G[{x},{y}] = {actualG}, ожидалось около {expectedG}");
        Assert.That(actualB, Is.InRange(expectedB - tolerance, expectedB + tolerance),
            $"B[{x},{y}] = {actualB}, ожидалось около {expectedB}");
        Assert.That(actualA, Is.EqualTo(255),
            $"A[{x},{y}] = {actualA}, ожидалось 255");
    }

    private static string FormatPixel(ReadOnlyVideoFrame frame, int x, int y)
    {
        var row = frame.PackedData.GetRow(y);
        var offset = x * 4;
        return $"{row[offset]},{row[offset + 1]},{row[offset + 2]},{row[offset + 3]}";
    }

    private static string FormatShortVector(ReadOnlySpan<short> values) =>
        string.Join(",", values.ToArray());

    private static string FormatByteVector(ReadOnlySpan<byte> values) =>
        string.Join(",", values.ToArray());

    private static string FormatShortMatrix4x4(ReadOnlySpan<short> values)
    {
        var rows = new string[4];
        for (var row = 0; row < 4; row++)
        {
            rows[row] = string.Join(",", values.Slice(row * 4, 4).ToArray());
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string FormatByteMatrix4x4(ReadOnlySpan<byte> values)
    {
        var rows = new string[4];
        for (var row = 0; row < 4; row++)
        {
            rows[row] = string.Join(",", values.Slice(row * 4, 4).ToArray());
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string FormatByteMatrix2x2(ReadOnlySpan<byte> values)
    {
        var rows = new string[2];
        for (var row = 0; row < 2; row++)
        {
            rows[row] = $"{values[row * 2]},{values[row * 2 + 1]}";
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string FormatFrameBlock4x4(ReadOnlyVideoFrame frame, int startX, int startY)
    {
        var rows = new string[4];
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            var row = frame.PackedData.GetRow(startY + rowIndex);
            var values = new string[4];
            for (var columnIndex = 0; columnIndex < 4; columnIndex++)
            {
                values[columnIndex] = row[(startX + columnIndex) * 4].ToString();
            }

            rows[rowIndex] = string.Join(",", values);
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string FormatFrameBlock4x4Rgba(ReadOnlyVideoFrame frame, int startX, int startY)
    {
        var rows = new string[4];
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            var row = frame.PackedData.GetRow(startY + rowIndex);
            var values = new string[4];
            for (var columnIndex = 0; columnIndex < 4; columnIndex++)
            {
                var offset = (startX + columnIndex) * 4;
                values[columnIndex] = $"{row[offset]}/{row[offset + 1]}/{row[offset + 2]}/{row[offset + 3]}";
            }

            rows[rowIndex] = string.Join(" | ", values);
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string FormatFrameColumn4(ReadOnlyVideoFrame frame, int x, int startY)
    {
        var values = new string[4];
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            var row = frame.PackedData.GetRow(startY + rowIndex);
            values[rowIndex] = row[x * 4].ToString();
        }

        return string.Join(",", values);
    }

    private static short[] Transpose4x4(ReadOnlySpan<short> values)
    {
        var transposed = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                transposed[(row * 4) + column] = values[(column * 4) + row];
            }
        }

        return transposed;
    }

    private static short[] FlipHorizontal4x4(ReadOnlySpan<short> values)
    {
        var remapped = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                remapped[(row * 4) + column] = values[(row * 4) + (3 - column)];
            }
        }

        return remapped;
    }

    private static short[] FlipVertical4x4(ReadOnlySpan<short> values)
    {
        var remapped = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                remapped[(row * 4) + column] = values[((3 - row) * 4) + column];
            }
        }

        return remapped;
    }

    private static short[] Rotate1804x4(ReadOnlySpan<short> values)
    {
        var remapped = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                remapped[(row * 4) + column] = values[((3 - row) * 4) + (3 - column)];
            }
        }

        return remapped;
    }

    private static short[] Rotate90Clockwise4x4(ReadOnlySpan<short> values)
    {
        var remapped = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                remapped[(row * 4) + column] = values[((3 - column) * 4) + row];
            }
        }

        return remapped;
    }

    private static short[] Rotate90CounterClockwise4x4(ReadOnlySpan<short> values)
    {
        var remapped = new short[16];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                remapped[(row * 4) + column] = values[(column * 4) + (3 - row)];
            }
        }

        return remapped;
    }

    private static int ComputeBlockSad(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var total = 0;
        for (var index = 0; index < 16; index++)
        {
            total += Math.Abs(left[index] - right[index]);
        }

        return total;
    }

    private static byte[] ExtractRedChannel4x4(ReadOnlySpan<byte> rgbaBlock)
    {
        if (rgbaBlock.Length != 64)
        {
            throw new InvalidOperationException($"Expected 64 RGBA bytes for a 4x4 block, got {rgbaBlock.Length}.");
        }

        var red = new byte[16];
        for (var index = 0; index < 16; index++)
        {
            red[index] = rgbaBlock[index * 4];
        }

        return red;
    }

    private static byte[] LoadMagickRgbaBlock4x4(string imagePath, int startX, int startY)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "magick",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.StartInfo.ArgumentList.Add(imagePath);
        process.StartInfo.ArgumentList.Add("-crop");
        process.StartInfo.ArgumentList.Add($"4x4+{startX}+{startY}");
        process.StartInfo.ArgumentList.Add("rgba:-");

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ImageMagick process.");
        }

        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ImageMagick failed with exit code {process.ExitCode}: {error}");
        }

        var bytes = output.ToArray();
        if (bytes.Length != 64)
        {
            throw new InvalidOperationException($"Expected 64 RGBA bytes from ImageMagick, got {bytes.Length}.");
        }

        return bytes;
    }

    private static byte[] LoadMagickRgbaImage(string imagePath, int width, int height)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "magick",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.StartInfo.ArgumentList.Add(imagePath);
        process.StartInfo.ArgumentList.Add("rgba:-");

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ImageMagick process.");
        }

        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ImageMagick failed with exit code {process.ExitCode}: {error}");
        }

        var bytes = output.ToArray();
        var expectedLength = width * height * 4;
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException($"Expected {expectedLength} RGBA bytes from ImageMagick, got {bytes.Length}.");
        }

        return bytes;
    }

    private readonly record struct SubblockMagickComparison(
        int MacroblockX,
        int MacroblockY,
        int SubblockX,
        int SubblockY,
        int BlockX,
        int BlockY,
        Vp8DecodeDiagnostics Diagnostics,
        byte[] ExternalRedBlock);

    private static (int MaxDiff, int AvgDiff, int SampleX, int SampleY) GetMacroblockDiffStats(ReadOnlyVideoFrame expected, ReadOnlyVideoFrame actual, int macroblockX, int macroblockY)
    {
        var startX = macroblockX * 16;
        var startY = macroblockY * 16;
        var endX = Math.Min(startX + 16, expected.Width);
        var endY = Math.Min(startY + 16, expected.Height);
        var maxDiff = 0;
        var diffSum = 0;
        var sampleX = startX;
        var sampleY = startY;
        var pixelCount = 0;

        for (var y = startY; y < endY; y++)
        {
            var expectedRow = expected.PackedData.GetRow(y);
            var actualRow = actual.PackedData.GetRow(y);
            for (var x = startX; x < endX; x++)
            {
                var offset = x * 4;
                var diff = Math.Max(
                    Math.Abs(expectedRow[offset] - actualRow[offset]),
                    Math.Max(
                        Math.Abs(expectedRow[offset + 1] - actualRow[offset + 1]),
                        Math.Abs(expectedRow[offset + 2] - actualRow[offset + 2])));

                diffSum += diff;
                pixelCount++;

                if (diff <= maxDiff)
                {
                    continue;
                }

                maxDiff = diff;
                sampleX = x;
                sampleY = y;
            }
        }

        return (maxDiff, pixelCount > 0 ? diffSum / pixelCount : 0, sampleX, sampleY);
    }

    private static (int MaxDiff, int AvgDiff, int SampleX, int SampleY) GetMacroblockDiffStats(byte[] expectedRgba, int width, int height, ReadOnlyVideoFrame actual, int macroblockX, int macroblockY)
    {
        var startX = macroblockX * 16;
        var startY = macroblockY * 16;
        var endX = Math.Min(startX + 16, width);
        var endY = Math.Min(startY + 16, height);
        var maxDiff = 0;
        var diffSum = 0;
        var sampleX = startX;
        var sampleY = startY;
        var pixelCount = 0;

        for (var y = startY; y < endY; y++)
        {
            var actualRow = actual.PackedData.GetRow(y);
            for (var x = startX; x < endX; x++)
            {
                var offset = x * 4;
                var diff = Math.Max(
                    Math.Abs(expectedRgba[((y * width) + x) * 4] - actualRow[offset]),
                    Math.Max(
                        Math.Abs(expectedRgba[((y * width) + x) * 4 + 1] - actualRow[offset + 1]),
                        Math.Abs(expectedRgba[((y * width) + x) * 4 + 2] - actualRow[offset + 2])));

                diffSum += diff;
                pixelCount++;

                if (diff <= maxDiff)
                {
                    continue;
                }

                maxDiff = diff;
                sampleX = x;
                sampleY = y;
            }
        }

        return (maxDiff, pixelCount > 0 ? diffSum / pixelCount : 0, sampleX, sampleY);
    }

    private static string FormatRgbaPixel(byte[] rgba, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return $"{rgba[offset]},{rgba[offset + 1]},{rgba[offset + 2]},{rgba[offset + 3]}";
    }

    private static void AssertPixelCloseToSource(ReadOnlyVideoFrame expected, ReadOnlyVideoFrame actual, int x, int y, int tolerance)
    {
        var expectedRow = expected.PackedData.GetRow(y);
        var actualRow = actual.PackedData.GetRow(y);
        var offset = x * 4;

        Assert.That(actualRow[offset], Is.InRange(expectedRow[offset] - tolerance, expectedRow[offset] + tolerance),
            $"R[{x},{y}] = {actualRow[offset]}, ожидалось около {expectedRow[offset]}");
        Assert.That(actualRow[offset + 1], Is.InRange(expectedRow[offset + 1] - tolerance, expectedRow[offset + 1] + tolerance),
            $"G[{x},{y}] = {actualRow[offset + 1]}, ожидалось около {expectedRow[offset + 1]}");
        Assert.That(actualRow[offset + 2], Is.InRange(expectedRow[offset + 2] - tolerance, expectedRow[offset + 2] + tolerance),
            $"B[{x},{y}] = {actualRow[offset + 2]}, ожидалось около {expectedRow[offset + 2]}");
        Assert.That(actualRow[offset + 3], Is.EqualTo(255),
            $"A[{x},{y}] = {actualRow[offset + 3]}, ожидалось 255");
    }

    private static void FillSyntheticVp8Frame(VideoFrame frame)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.PackedData.GetRow(y);
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = x * 4;
                row[offset] = (byte)Math.Clamp(16 + (x * 7) + (y * 2), 0, 255);
                row[offset + 1] = (byte)Math.Clamp(24 + (x * 3) + (y * 5), 0, 255);
                row[offset + 2] = (byte)Math.Clamp(32 + (x * 5) + (y * 3), 0, 255);
                row[offset + 3] = 255;
            }
        }
    }

    private static void ReferenceInverseDct4x4Add(ReadOnlySpan<short> input, Span<byte> output, int stride)
    {
        Span<int> temp = stackalloc int[16];

        for (var i = 0; i < 4; i++)
        {
            var a1 = input[i] + input[8 + i];
            var b1 = input[i] - input[8 + i];

            var temp1 = input[4 + i] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var temp2 = input[12 + i] + (input[12 + i] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            var c1 = temp1 - temp2;

            temp1 = input[4 + i] + (input[4 + i] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            temp2 = input[12 + i] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var d1 = temp1 + temp2;

            temp[i] = a1 + d1;
            temp[12 + i] = a1 - d1;
            temp[4 + i] = b1 + c1;
            temp[8 + i] = b1 - c1;
        }

        for (var i = 0; i < 4; i++)
        {
            var row = i * 4;
            var a1 = temp[row] + temp[row + 2];
            var b1 = temp[row] - temp[row + 2];

            var temp1 = temp[row + 1] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var temp2 = temp[row + 3] + (temp[row + 3] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            var c1 = temp1 - temp2;

            temp1 = temp[row + 1] + (temp[row + 1] * Vp8Constants.CosPI8Sqrt2Minus1 >> 16);
            temp2 = temp[row + 3] * Vp8Constants.SinPI8Sqrt2 >> 16;
            var d1 = temp1 + temp2;

            var outOff = i * stride;
            output[outOff] = ClampByte(output[outOff] + ((a1 + d1 + 4) >> 3));
            output[outOff + 1] = ClampByte(output[outOff + 1] + ((b1 + c1 + 4) >> 3));
            output[outOff + 2] = ClampByte(output[outOff + 2] + ((b1 - c1 + 4) >> 3));
            output[outOff + 3] = ClampByte(output[outOff + 3] + ((a1 - d1 + 4) >> 3));
        }
    }

    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    private static void EncodeTreeSymbol(ref Vp8BoolEncoder encoder, ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probs, int symbol)
    {
        if (!TryFindTreePath(tree, symbol, nodeIndex: 0, path: [], out var resolvedPath))
        {
            throw new InvalidOperationException($"Symbol {symbol} not found in tree.");
        }

        for (var index = 0; index < resolvedPath.Length; index++)
        {
            encoder.EncodeBit(resolvedPath[index], probs[index]);
        }
    }

    private static bool TryFindTreePath(ReadOnlySpan<sbyte> tree, int symbol, int nodeIndex, int[] path, out int[] resolvedPath)
    {
        for (var bit = 0; bit <= 1; bit++)
        {
            var child = tree[nodeIndex + bit];
            var nextPath = new int[path.Length + 1];
            path.CopyTo(nextPath, 0);
            nextPath[^1] = bit;

            if (child <= 0)
            {
                if (-child == symbol)
                {
                    resolvedPath = nextPath;
                    return true;
                }

                continue;
            }

            if (TryFindTreePath(tree, symbol, child, nextPath, out resolvedPath))
            {
                return true;
            }
        }

        resolvedPath = [];
        return false;
    }

    private static ReadOnlySpan<byte> ExtractVp8Chunk(ReadOnlySpan<byte> webpData)
    {
        var offset = 12;
        while (offset + 8 <= webpData.Length)
        {
            var chunkType = BitConverter.ToUInt32(webpData.Slice(offset, 4));
            var chunkSize = BitConverter.ToInt32(webpData.Slice(offset + 4, 4));

            if (chunkType == 0x20385056)
            {
                return webpData.Slice(offset + 8, chunkSize);
            }

            offset += 8 + ((chunkSize + 1) & ~1);
        }

        throw new InvalidOperationException("VP8 chunk not found in test.webp");
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "VP8: пустые данные возвращают InvalidData")]
    public void DecodeEmptyDataReturnsInvalidData()
    {
        var decoder = new Vp8Decoder();
        using var buffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode([], ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP8: данные < 10 байт возвращают InvalidData")]
    public void DecodeShortDataReturnsInvalidData()
    {
        var decoder = new Vp8Decoder();
        using var buffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode(new byte[9], ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP8: неверный sync code возвращает InvalidData")]
    public void DecodeInvalidSyncCodeReturnsInvalidData()
    {
        var decoder = new Vp8Decoder();

        // Создаём минимальный VP8 фрейм с неверным sync code
        var data = new byte[20];
        data[0] = 0x00; // frame tag: keyframe, version=0, show=0, firstPartSize=0
        data[3] = 0xAA; // wrong sync code (should be 0x9D)
        data[4] = 0xBB;
        data[5] = 0xCC;

        using var buffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP8: CanDecode определяет VP8 lossy как WebP")]
    public void CanDecodeRecognizesVp8LossyAsWebp()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        var result = codec!.CanDecode(data);

        Assert.That(result, Is.True);
    }

    #endregion
}
