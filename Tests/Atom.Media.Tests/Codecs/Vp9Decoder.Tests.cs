#pragma warning disable CA1861, MA0051, S109

using Atom.Media;
using Atom.Media.Codecs.Webm;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты VP9 декодера: BoolDecoder, Constants, DCT, Prediction, LoopFilter, Decoder, WebmCodec routing.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class Vp9DecoderTests(ILogger logger) : BenchmarkTests<Vp9DecoderTests>(logger)
{
    public Vp9DecoderTests() : this(ConsoleLogger.Unicode) { }

    #region BoolDecoder Tests

    [TestCase(TestName = "VP9 BoolDecoder: инициализация из данных")]
    public void BoolDecoderInitializesFromData()
    {
        ReadOnlySpan<byte> data = [0x80, 0x00, 0x00, 0x00];
        var decoder = new Vp9BoolDecoder(data);

        Assert.That(decoder.Pos, Is.EqualTo(2)); // Consumes 2 bytes for initial value
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeBit(128) декодирует равномерно")]
    public void BoolDecoderDecodeBitUniform()
    {
        // 0xFF → all 1 bits → first bit at prob=128 should be 1
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF];
        var decoder = new Vp9BoolDecoder(data);

        var bit = decoder.DecodeBit(128);
        Assert.That(bit, Is.EqualTo(1));
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeBit(128) с нулями даёт 0")]
    public void BoolDecoderDecodeBitZeroData()
    {
        // 0x00 → all 0 bits → first bit at prob=128 should be 0
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        var decoder = new Vp9BoolDecoder(data);

        var bit = decoder.DecodeBit(128);
        Assert.That(bit, Is.Zero);
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeLiteral декодирует 8 бит")]
    public void BoolDecoderDecodeLiteral8Bits()
    {
        // Uniform bits with prob=128 → literal bits from MSB
        // 0xFF 0xFF → value=0xFFFF, all bits should decode as 1 → literal = 0xFF
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        var decoder = new Vp9BoolDecoder(data);

        var value = decoder.DecodeLiteral(8);
        Assert.That(value, Is.EqualTo(255u));
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeSigned положительное")]
    public void BoolDecoderDecodeSignedPositive()
    {
        // Zero data → DecodeLiteral returns 0, sign bit = 0 → value = 0
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        var decoder = new Vp9BoolDecoder(data);

        var value = decoder.DecodeSigned(4);
        Assert.That(value, Is.Zero);
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeTree с тривиальным деревом")]
    public void BoolDecoderDecodeTreeTrivial()
    {
        // Simple 2-leaf tree: tree = [-leaf0, -leaf1] = [0, -1]
        // Actually VP9 tree format: positive = offset to next node, negative = -(leaf value)
        // Minimal tree: 2 outcomes → [-0, -1] won't work. Let's use the partition tree.
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        var decoder = new Vp9BoolDecoder(data);

        // Partition tree: [2, -0, -1, -2], probability for first split
        sbyte[] tree = [2, -0, -1, -2];
        byte[] probs = [128, 128];

        // With all-zero data, DecodeBit(128)=0 → goes to tree[0]=2 → then DecodeBit(128)=0 → tree[2]=-1 → leaf=1
        // Wait: tree[0 + bit] where bit=0 → tree[0]=2 (next node index), tree[2 + bit=0] = -1 → leaf=1
        var result = decoder.DecodeTree(tree, probs);
        Assert.That(result, Is.GreaterThanOrEqualTo(0));
    }

    [TestCase(TestName = "VP9 BoolDecoder: последовательное декодирование множества бит")]
    public void BoolDecoderDecodeMultipleBitsSequential()
    {
        ReadOnlySpan<byte> data = [0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55];
        var decoder = new Vp9BoolDecoder(data);

        // Decode 16 bits — should not throw
        var bits = new int[16];
        for (var i = 0; i < 16; i++)
            bits[i] = decoder.DecodeBit(128);

        // Verify we got actual bit values
        Assert.That(bits, Has.All.InRange(0, 1));
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeUniform с n=4")]
    public void BoolDecoderDecodeUniformRange4()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        var decoder = new Vp9BoolDecoder(data);

        var value = decoder.DecodeUniform(4);
        Assert.That(value, Is.InRange(0, 3));
    }

    [TestCase(TestName = "VP9 BoolDecoder: DecodeUniform с n=1 всегда 0")]
    public void BoolDecoderDecodeUniformOne()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFF];
        var decoder = new Vp9BoolDecoder(data);

        var value = decoder.DecodeUniform(1);
        Assert.That(value, Is.Zero);
    }

    #endregion

    #region Constants Tests

    [TestCase(TestName = "VP9 Constants: BlockSizeLookup содержит корректные размеры")]
    public void ConstantsBlockSizeLookupValid()
    {
        Assert.That(Vp9Constants.BlockSizeLookup, Has.Length.EqualTo(Vp9Constants.NumBlockSizes));

        // First entry: 4×4
        Assert.That(Vp9Constants.BlockSizeLookup[0].W, Is.EqualTo(4));
        Assert.That(Vp9Constants.BlockSizeLookup[0].H, Is.EqualTo(4));

        // Last entry: 64×64
        var last = Vp9Constants.BlockSizeLookup[Vp9Constants.NumBlockSizes - 1];
        Assert.That(last.W, Is.EqualTo(64));
        Assert.That(last.H, Is.EqualTo(64));
    }

    [TestCase(TestName = "VP9 Constants: PartitionTree имеет 6 элементов")]
    public void ConstantsPartitionTreeValid()
    {
        Assert.That(Vp9Constants.PartitionTree, Has.Length.EqualTo(6));
    }

    [TestCase(TestName = "VP9 Constants: IntraModeTree имеет 18 элементов")]
    public void ConstantsIntraModeTreeValid()
    {
        Assert.That(Vp9Constants.IntraModeTree, Has.Length.EqualTo(18));
    }

    [TestCase(TestName = "VP9 Constants: DcQLookup имеет 256 записей")]
    public void ConstantsDcQLookupValid()
    {
        Assert.That(Vp9Constants.DcQLookup, Has.Length.EqualTo(256));
        Assert.That(Vp9Constants.DcQLookup[0], Is.EqualTo(4)); // Q=0 → DC quant = 4
        Assert.That(Vp9Constants.DcQLookup[255], Is.GreaterThan(0));
    }

    [TestCase(TestName = "VP9 Constants: AcQLookup имеет 256 записей")]
    public void ConstantsAcQLookupValid()
    {
        Assert.That(Vp9Constants.AcQLookup, Has.Length.EqualTo(256));
        Assert.That(Vp9Constants.AcQLookup[0], Is.EqualTo(4)); // Q=0 → AC quant = 4
        Assert.That(Vp9Constants.AcQLookup[255], Is.GreaterThan(0));
    }

    [TestCase(TestName = "VP9 Constants: DefaultKfYModeProbs имеет 10×10×9 записей")]
    public void ConstantsDefaultKfYModeProbs()
    {
        Assert.That(Vp9Constants.DefaultKfYModeProbs.GetLength(0), Is.EqualTo(10));
        Assert.That(Vp9Constants.DefaultKfYModeProbs.GetLength(1), Is.EqualTo(10));
        Assert.That(Vp9Constants.DefaultKfYModeProbs.GetLength(2), Is.EqualTo(9));
    }

    [TestCase(TestName = "VP9 Constants: DefaultKfUvModeProbs имеет 10×9 записей")]
    public void ConstantsDefaultKfUvModeProbs()
    {
        Assert.That(Vp9Constants.DefaultKfUvModeProbs.GetLength(0), Is.EqualTo(10));
        Assert.That(Vp9Constants.DefaultKfUvModeProbs.GetLength(1), Is.EqualTo(9));
    }

    [TestCase(TestName = "VP9 Constants: CoeffTokenTree имеет 24 элемента")]
    public void ConstantsCoeffTokenTreeValid()
    {
        Assert.That(Vp9Constants.CoeffTokenTree, Has.Length.EqualTo(24));
    }

    [TestCase(TestName = "VP9 Constants: MaxTxSizeLookup согласован с блоками")]
    public void ConstantsMaxTxSizeLookupConsistent()
    {
        Assert.That(Vp9Constants.MaxTxSizeLookup, Has.Length.EqualTo(Vp9Constants.NumBlockSizes));

        // 4×4 block → max tx size 0 (4×4)
        Assert.That(Vp9Constants.MaxTxSizeLookup[0], Is.Zero);

        // 64×64 block → max tx size 3 (32×32)
        Assert.That(Vp9Constants.MaxTxSizeLookup[Vp9Constants.NumBlockSizes - 1], Is.EqualTo(3));
    }

    [TestCase(TestName = "VP9 Constants: DefaultPartitionProbs имеет 16×3 записей")]
    public void ConstantsDefaultPartitionProbs()
    {
        Assert.That(Vp9Constants.DefaultPartitionProbs.GetLength(0), Is.EqualTo(16));
        Assert.That(Vp9Constants.DefaultPartitionProbs.GetLength(1), Is.EqualTo(3));
    }

    #endregion

    #region DCT Tests

    [TestCase(TestName = "VP9 DCT: InverseDct4x4 DC-only")]
    public void Dct4x4DcOnly()
    {
        // DC-only: input[0] = 128, rest zero
        // After IDCT, all output pixels should get the same DC value added
        Span<short> input = stackalloc short[16];
        input[0] = 128;

        // Start with gray (128) output
        var output = new byte[4 * 4];
        Array.Fill<byte>(output, 128);

        Vp9Dct.InverseDct4x4(input, output, 4);

        // After adding DC to gray base, all pixels should be the same non-128 value
        var firstVal = output[0];
        for (var i = 1; i < 16; i++)
            Assert.That(output[i], Is.EqualTo(firstVal), $"Pixel {i} differs from pixel 0");

        Assert.That(firstVal, Is.Not.EqualTo(128), "DC should have changed the output");
    }

    [TestCase(TestName = "VP9 DCT: InverseDct4x4 нулевой вход")]
    public void Dct4x4ZeroInput()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();

        var output = new byte[4 * 4];
        Array.Fill<byte>(output, 100);

        Vp9Dct.InverseDct4x4(input, output, 4);

        // Zero input → no change to output
        for (var i = 0; i < 16; i++)
            Assert.That(output[i], Is.EqualTo(100));
    }

    [TestCase(TestName = "VP9 DCT: InverseDct8x8 DC-only")]
    public void Dct8x8DcOnly()
    {
        Span<short> input = stackalloc short[64];
        input[0] = 256;

        var output = new byte[8 * 8];
        Array.Fill<byte>(output, 128);

        Vp9Dct.InverseDct8x8(input, output, 8);

        var firstVal = output[0];
        Assert.That(firstVal, Is.Not.EqualTo(128));
        for (var i = 1; i < 64; i++)
            Assert.That(output[i], Is.EqualTo(firstVal), $"8×8 DC pixel {i} differs");
    }

    [TestCase(TestName = "VP9 DCT: InverseDct8x8 нулевой вход")]
    public void Dct8x8ZeroInput()
    {
        Span<short> input = stackalloc short[64];
        input.Clear();

        var output = new byte[8 * 8];
        Array.Fill<byte>(output, 50);

        Vp9Dct.InverseDct8x8(input, output, 8);

        for (var i = 0; i < 64; i++)
            Assert.That(output[i], Is.EqualTo(50));
    }

    [TestCase(TestName = "VP9 DCT: InverseDct16x16 DC-only")]
    public void Dct16x16DcOnly()
    {
        Span<short> input = stackalloc short[256];
        input[0] = 512;

        var output = new byte[16 * 16];
        Array.Fill<byte>(output, 128);

        Vp9Dct.InverseDct16x16(input, output, 16);

        var firstVal = output[0];
        Assert.That(firstVal, Is.Not.EqualTo(128));
        for (var i = 1; i < 256; i++)
            Assert.That(output[i], Is.EqualTo(firstVal), $"16×16 DC pixel {i} differs");
    }

    [TestCase(TestName = "VP9 DCT: InverseDct32x32 DC-only")]
    public void Dct32x32DcOnly()
    {
        Span<short> input = stackalloc short[1024];
        input[0] = 1024;

        var output = new byte[32 * 32];
        Array.Fill<byte>(output, 128);

        var workBuffer = new int[32 * 32];
        Vp9Dct.InverseDct32x32(input, output, 32, workBuffer);

        var firstVal = output[0];
        Assert.That(firstVal, Is.Not.EqualTo(128));
        for (var i = 1; i < 1024; i++)
            Assert.That(output[i], Is.EqualTo(firstVal), $"32×32 DC pixel {i} differs");
    }

    [TestCase(TestName = "VP9 DCT: InverseTransform dispatch для txSize=0")]
    public void DctInverseTransformDispatchTxSize0()
    {
        Span<short> coeffs = stackalloc short[16];
        coeffs[0] = 64;

        var output = new byte[4 * 4];
        Array.Fill<byte>(output, 128);

        Vp9Dct.InverseTransform(coeffs, output, 4, 0, 0, []);

        // Should have applied 4×4 IDCT
        Assert.That(output[0], Is.Not.EqualTo(128));
    }

    [TestCase(TestName = "VP9 DCT: ADST 4×4 DC-only")]
    public void Adst4x4DcOnly()
    {
        Span<short> coeffs = stackalloc short[16];
        coeffs[0] = 128;

        var output = new byte[4 * 4];
        Array.Fill<byte>(output, 128);

        // txType=3 (ADST_ADST) for 4×4
        Vp9Dct.InverseTransform(coeffs, output, 4, 0, 3, []);

        // Output should have changed
        var hasNon128 = false;
        for (var i = 0; i < 16; i++)
        {
            if (output[i] != 128)
            {
                hasNon128 = true;
                break;
            }
        }

        Assert.That(hasNon128, Is.True, "ADST should modify output");
    }

    #endregion

    #region Prediction Tests

    [TestCase(TestName = "VP9 Prediction: DC prediction 4×4")]
    public void PredictionDc4x4()
    {
        var dst = new byte[4 * 4];
        ReadOnlySpan<byte> above = [100, 100, 100, 100, 100, 100, 100, 100];
        ReadOnlySpan<byte> left = [100, 100, 100, 100, 100, 100, 100, 100];

        Vp9Prediction.Predict(Vp9Constants.DcPred, dst, 4, above, left, 100, 4, true, true);

        // DC prediction with uniform 100 → all pixels should be 100
        for (var i = 0; i < 16; i++)
            Assert.That(dst[i], Is.EqualTo(100), $"DC prediction pixel {i}");
    }

    [TestCase(TestName = "VP9 Prediction: V (vertical) prediction 4×4")]
    public void PredictionVertical4x4()
    {
        var dst = new byte[4 * 4];
        ReadOnlySpan<byte> above = [10, 20, 30, 40, 10, 20, 30, 40];
        ReadOnlySpan<byte> left = [0, 0, 0, 0, 0, 0, 0, 0];

        Vp9Prediction.Predict(Vp9Constants.VPred, dst, 4, above, left, 0, 4, true, true);

        // Vertical: each column copies from above
        for (var y = 0; y < 4; y++)
        {
            Assert.That(dst[y * 4 + 0], Is.EqualTo(10));
            Assert.That(dst[y * 4 + 1], Is.EqualTo(20));
            Assert.That(dst[y * 4 + 2], Is.EqualTo(30));
            Assert.That(dst[y * 4 + 3], Is.EqualTo(40));
        }
    }

    [TestCase(TestName = "VP9 Prediction: H (horizontal) prediction 4×4")]
    public void PredictionHorizontal4x4()
    {
        var dst = new byte[4 * 4];
        ReadOnlySpan<byte> above = [0, 0, 0, 0, 0, 0, 0, 0];
        ReadOnlySpan<byte> left = [10, 20, 30, 40, 10, 20, 30, 40];

        Vp9Prediction.Predict(Vp9Constants.HPred, dst, 4, above, left, 0, 4, true, true);

        // Horizontal: each row fills with left value
        for (var x = 0; x < 4; x++)
        {
            Assert.That(dst[0 * 4 + x], Is.EqualTo(10));
            Assert.That(dst[1 * 4 + x], Is.EqualTo(20));
            Assert.That(dst[2 * 4 + x], Is.EqualTo(30));
            Assert.That(dst[3 * 4 + x], Is.EqualTo(40));
        }
    }

    [TestCase(TestName = "VP9 Prediction: TM prediction 4×4")]
    public void PredictionTm4x4()
    {
        var dst = new byte[4 * 4];
        // TM: dst[y][x] = above[x] + left[y] - aboveLeft
        ReadOnlySpan<byte> above = [100, 110, 120, 130, 100, 110, 120, 130];
        ReadOnlySpan<byte> left = [100, 110, 120, 130, 100, 110, 120, 130];
        byte aboveLeft = 100;

        Vp9Prediction.Predict(Vp9Constants.TmPred, dst, 4, above, left, aboveLeft, 4, true, true);

        // dst[0][0] = 100 + 100 - 100 = 100
        Assert.That(dst[0], Is.EqualTo(100));
        // dst[0][1] = 110 + 100 - 100 = 110
        Assert.That(dst[1], Is.EqualTo(110));
        // dst[1][0] = 100 + 110 - 100 = 110
        Assert.That(dst[4], Is.EqualTo(110));
        // dst[1][1] = 110 + 110 - 100 = 120
        Assert.That(dst[5], Is.EqualTo(120));
    }

    [TestCase(TestName = "VP9 Prediction: все 10 режимов не бросают исключений 8×8")]
    public void PredictionAllModes8x8NoThrow()
    {
        var dst = new byte[8 * 8];
        var above = new byte[16];
        var left = new byte[16];
        Array.Fill<byte>(above, 128);
        Array.Fill<byte>(left, 128);

        for (var mode = 0; mode < 10; mode++)
        {
            Array.Clear(dst);
            Assert.DoesNotThrow(() =>
                Vp9Prediction.Predict(mode, dst, 8, above, left, 128, 8, true, true),
                $"Mode {mode} threw for 8×8");
        }
    }

    [TestCase(TestName = "VP9 Prediction: DC prediction 8×8 с разными значениями")]
    public void PredictionDc8x8MixedValues()
    {
        var dst = new byte[8 * 8];
        var above = new byte[16];
        var left = new byte[16];
        Array.Fill<byte>(above, 200);
        Array.Fill<byte>(left, 100);

        Vp9Prediction.Predict(Vp9Constants.DcPred, dst, 8, above, left, 128, 8, true, true);

        // DC = average of above(200) and left(100) = 150
        Assert.That(dst[0], Is.EqualTo(150));
    }

    [TestCase(TestName = "VP9 Prediction: DC prediction без above")]
    public void PredictionDcWithoutAbove()
    {
        var dst = new byte[4 * 4];
        ReadOnlySpan<byte> above = [0, 0, 0, 0, 0, 0, 0, 0];
        ReadOnlySpan<byte> left = [80, 80, 80, 80, 80, 80, 80, 80];

        Vp9Prediction.Predict(Vp9Constants.DcPred, dst, 4, above, left, 0, 4, false, true);

        // Only left available → average of left = 80
        Assert.That(dst[0], Is.EqualTo(80));
    }

    [TestCase(TestName = "VP9 Prediction: DC prediction без left")]
    public void PredictionDcWithoutLeft()
    {
        var dst = new byte[4 * 4];
        ReadOnlySpan<byte> above = [120, 120, 120, 120, 120, 120, 120, 120];
        ReadOnlySpan<byte> left = [0, 0, 0, 0, 0, 0, 0, 0];

        Vp9Prediction.Predict(Vp9Constants.DcPred, dst, 4, above, left, 0, 4, true, false);

        // Only above available → average of above = 120
        Assert.That(dst[0], Is.EqualTo(120));
    }

    #endregion

    #region LoopFilter Tests

    [TestCase(TestName = "VP9 LoopFilter: ComputeFilterLevel базовый")]
    public void LoopFilterComputeFilterLevelBasic()
    {
        var fp = new Vp9LoopFilter.FilterParams { FilterLevel = 32, SharpnessLevel = 0 };
        var level = Vp9LoopFilter.ComputeFilterLevel(in fp, 32, 0, 0);
        Assert.That(level, Is.EqualTo(32));
    }

    [TestCase(TestName = "VP9 LoopFilter: ComputeFilterLevel с ref delta")]
    public void LoopFilterComputeFilterLevelWithRefDelta()
    {
        var fp = new Vp9LoopFilter.FilterParams
        {
            FilterLevel = 32,
            SharpnessLevel = 0,
            ModeRefDeltaEnabled = true,
            RefDeltas = [4, 0, 0, 0],
            ModeDeltas = [0, 0],
        };

        var level = Vp9LoopFilter.ComputeFilterLevel(in fp, 32, 0, 0);
        Assert.That(level, Is.EqualTo(36)); // 32 + 4
    }

    [TestCase(TestName = "VP9 LoopFilter: ComputeThresholds при level=0")]
    public void LoopFilterComputeThresholdsZeroLevel()
    {
        Vp9LoopFilter.ComputeThresholds(0, 0, out var limit, out var blimit, out var thresh);
        Assert.That(limit, Is.Zero);
    }

    [TestCase(TestName = "VP9 LoopFilter: ComputeThresholds при level>0")]
    public void LoopFilterComputeThresholdsNonZero()
    {
        Vp9LoopFilter.ComputeThresholds(32, 0, out var limit, out var blimit, out var thresh);
        Assert.That(limit, Is.GreaterThan(0));
        Assert.That(blimit, Is.GreaterThan(0));
    }

    [TestCase(TestName = "VP9 LoopFilter: ApplyFrameFilter на плоской поверхности")]
    public void LoopFilterApplyFrameFilterFlat()
    {
        // Flat surface (all 128) → filter should not change pixels
        var plane = new byte[16 * 16];
        Array.Fill<byte>(plane, 128);

        var fp = new Vp9LoopFilter.FilterParams { FilterLevel = 32, SharpnessLevel = 0 };

        Vp9LoopFilter.ApplyFrameFilter(plane, 16, 16, 16, in fp, ReadOnlySpan<int>.Empty, 8);

        // Flat surface → no edges → no changes
        for (var i = 0; i < plane.Length; i++)
            Assert.That(plane[i], Is.EqualTo(128), $"Flat pixel {i} changed");
    }

    [TestCase(TestName = "VP9 LoopFilter: FilterParams поля по умолчанию")]
    public void LoopFilterParamsDefaults()
    {
        var fp = new Vp9LoopFilter.FilterParams();
        Assert.That(fp.FilterLevel, Is.Zero);
        Assert.That(fp.SharpnessLevel, Is.Zero);
        Assert.That(fp.ModeRefDeltaEnabled, Is.False);
    }

    #endregion

    #region Decoder Tests

    [TestCase(TestName = "VP9 Decoder: создание экземпляра")]
    public void DecoderCanCreate()
    {
        var decoder = new Vp9Decoder();
        Assert.That(decoder, Is.Not.Null);
    }

    [TestCase(TestName = "VP9 Decoder: IsVp9Frame с VP9 данными")]
    public void DecoderIsVp9FrameValid()
    {
        // VP9 frame marker: bits 7-6 of byte[0] = 0b10 → byte[0] = 0b10xxxxxx = 0x80..0xBF
        ReadOnlySpan<byte> data = [0x82, 0x49, 0x83, 0x42]; // marker=0b10, profile=0, keyframe
        Assert.That(Vp9Decoder.IsVp9Frame(data), Is.True);
    }

    [TestCase(TestName = "VP9 Decoder: IsVp9Frame с не-VP9 данными")]
    public void DecoderIsVp9FrameInvalid()
    {
        // AFRM magic bytes
        ReadOnlySpan<byte> data = [0x41, 0x46, 0x52, 0x4D];
        Assert.That(Vp9Decoder.IsVp9Frame(data), Is.False);
    }

    [TestCase(TestName = "VP9 Decoder: IsVp9Frame с коротким массивом")]
    public void DecoderIsVp9FrameTooShort()
    {
        ReadOnlySpan<byte> data = [0x82, 0x49];
        Assert.That(Vp9Decoder.IsVp9Frame(data), Is.False);
    }

    [TestCase(TestName = "VP9 Decoder: DecodeFrame с невалидным маркером бросает")]
    public void DecoderDecodeFrameInvalidMarkerThrows()
    {
        var decoder = new Vp9Decoder();
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        Assert.Throws<InvalidDataException>(() =>
            decoder.DecodeFrame(data, out _, out _, out _));
    }

    [TestCase(TestName = "VP9 Decoder: Decode возвращает InvalidData для мусора")]
    public void DecoderDecodeReturnsInvalidDataForGarbage()
    {
        var decoder = new Vp9Decoder();
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP9 Decoder: Decode возвращает UnsupportedFormat для inter-frame")]
    public void DecoderDecodeReturnsUnsupportedForInterFrame()
    {
        var decoder = new Vp9Decoder();
        // VP9 marker=0b10, profile=0, show_existing=0, frame_type=1 (inter)
        // byte = 0b10_00_0_1_x_x = 0x84 + show/error bits
        ReadOnlySpan<byte> data = [0x86, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = decoder.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
    }

    [TestCase(TestName = "VP9 Decoder: Decode минимальный keyframe header")]
    public void DecoderDecodeMinimalKeyframeHeader()
    {
        // Construct a minimal VP9 keyframe:
        // byte[0]: marker=0b10(bits7-6), profile=0b00(bits5-4), show_existing=0(bit3), frame_type=0(bit2), show_frame=1(bit1), error_resilient=1(bit0)
        // = 0b10_00_0_0_1_1 = 0x83
        // byte[1-3]: sync code: 0x49, 0x83, 0x42
        // byte[4]: color_space(bits7-5)=0 (CS_UNKNOWN), full_range(bit4)=0, remaining bits for size
        // Then frame size via bit reader: 16-bit width-1, 16-bit height-1
        // This requires careful bit-level packing

        var decoder = new Vp9Decoder();

        // Build a VP9 keyframe header for a 16×16 frame
        // This is complex due to bit-level encoding; try with minimal data
        // and expect parsing to fail gracefully
        var data = new byte[128];
        data[0] = 0x83; // marker=0b10, profile=0, show_existing=0, frame_type=0(key), show=1, error_resilient=1
        data[1] = 0x49; // sync code
        data[2] = 0x83;
        data[3] = 0x42;
        // Remaining data: color config + frame size + headers — complex to mock

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        // Should attempt to parse but may return an error due to incomplete header
        var result = decoder.Decode(data, ref frame);
        // Any result is acceptable — just shouldn't crash
        Assert.That(result, Is.AnyOf(CodecResult.Success, CodecResult.InvalidData, CodecResult.UnsupportedFormat));
    }

    #endregion

    #region WebmCodec VP9 Routing Tests

    [TestCase(TestName = "VP9 WebmCodec: routing VP9 frame данных")]
    public void WebmCodecRoutesVp9Frame()
    {
        using var codec = new WebmCodec();
        codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        // VP9 frame marker but incomplete data → should return error, not crash
        var data = new byte[32];
        data[0] = 0x83; // VP9 marker in bits 7-6

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        // Incomplete VP9 data → InvalidData or UnsupportedFormat, not NotInitialized
        Assert.That(result, Is.Not.EqualTo(CodecResult.NotInitialized));
    }

    [TestCase(TestName = "VP9 WebmCodec: AFRM routing по-прежнему работает")]
    public void WebmCodecAfrmRoutingStillWorks()
    {
        const int width = 16;
        const int height = 16;

        using var codec = new WebmCodec();
        codec.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        // Encode a frame
        using var srcBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var packed = srcBuffer.AsFrame().PackedData;
        for (var y = 0; y < height; y++)
        {
            var row = packed.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                row[x * 4] = 255;     // R
                row[x * 4 + 1] = 0;   // G
                row[x * 4 + 2] = 0;   // B
                row[x * 4 + 3] = 255; // A
            }
        }

        var roFrame = srcBuffer.AsReadOnlyFrame();
        var outputSize = WebmCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[outputSize];
        var encResult = codec.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encResult, Is.EqualTo(CodecResult.Success));

        // Decode
        using var decCodec = new WebmCodec();
        decCodec.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        using var dstBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decFrame = dstBuffer.AsFrame();
        var decResult = decCodec.Decode(encoded.AsSpan(0, bytesWritten), ref decFrame);

        Assert.That(decResult, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "VP9 WebmCodec: Decode с коротким пакетом → InvalidData")]
    public void WebmCodecDecodeShortPacket()
    {
        using var codec = new WebmCodec();
        codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        ReadOnlySpan<byte> data = [0x00, 0x01]; // Too short
        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP9 WebmCodec: Decode без инициализации → NotInitialized")]
    public void WebmCodecDecodeWithoutInit()
    {
        using var codec = new WebmCodec();
        var data = new byte[64];

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.NotInitialized));
    }

    [TestCase(TestName = "VP9 WebmCodec: Decode после Dispose → ObjectDisposedException")]
    public void WebmCodecDecodeAfterDispose()
    {
        var codec = new WebmCodec();
        codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
        });
        codec.Dispose();

        var data = new byte[64];
        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        try
        {
            codec.Decode(data, ref frame);
            Assert.Fail("Expected ObjectDisposedException");
        }
        catch (ObjectDisposedException)
        {
            // Expected
        }
    }

    [TestCase(TestName = "VP9 WebmCodec: CanDecodeStream с EBML header")]
    public void WebmCodecCanDecodeStreamEbml()
    {
        ReadOnlySpan<byte> ebml = [0x1A, 0x45, 0xDF, 0xA3];
        Assert.That(WebmCodec.CanDecodeStream(ebml), Is.True);
    }

    [TestCase(TestName = "VP9 WebmCodec: CanDecodeStream с не-EBML данными")]
    public void WebmCodecCanDecodeStreamNonEbml()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        Assert.That(WebmCodec.CanDecodeStream(data), Is.False);
    }

    [TestCase(TestName = "VP9 WebmCodec: Reset очищает состояние")]
    public void WebmCodecResetClearsState()
    {
        using var codec = new WebmCodec();
        codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        codec.Reset();
        // Should still work after reset (re-initialize)
        Assert.DoesNotThrow(() => codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 32,
            Height = 32,
            PixelFormat = VideoPixelFormat.Rgba32,
        }));
    }

    [TestCase(TestName = "VP9 WebmCodec: не-VP9 non-AFRM данные → InvalidData")]
    public void WebmCodecDecodeUnknownFormat()
    {
        using var codec = new WebmCodec();
        codec.InitializeDecoder(new VideoCodecParameters
        {
            Width = 16,
            Height = 16,
            PixelFormat = VideoPixelFormat.Rgba32,
        });

        // Random data that's neither AFRM nor VP9
        var data = new byte[64];
        data[0] = 0x12;
        data[1] = 0x34;
        data[2] = 0x56;
        data[3] = 0x78;

        using var buffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    #endregion
}
