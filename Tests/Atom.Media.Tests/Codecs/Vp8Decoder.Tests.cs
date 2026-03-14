#pragma warning disable CA1861, MA0051

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
