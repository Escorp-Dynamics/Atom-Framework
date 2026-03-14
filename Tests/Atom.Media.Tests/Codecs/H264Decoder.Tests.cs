#pragma warning disable CA1861, MA0051

using Atom.IO;
using Atom.Media;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты H.264 Baseline Profile декодера.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class H264DecoderTests(ILogger logger) : BenchmarkTests<H264DecoderTests>(logger)
{
    public H264DecoderTests() : this(ConsoleLogger.Unicode) { }

    #region ExpGolomb Tests

    [TestCase(TestName = "H264 ExpGolomb: ReadUe значение 0")]
    public void ExpGolombReadUeZero()
    {
        // ue(0) = 1 (single bit "1")
        ReadOnlySpan<byte> data = [0x80]; // 1000 0000
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadUe(ref reader);

        Assert.That(value, Is.Zero);
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadUe значение 1")]
    public void ExpGolombReadUeOne()
    {
        // ue(1) = 010 → codeNum = 2^1 - 1 + 0 = 1
        ReadOnlySpan<byte> data = [0x40]; // 0100 0000
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadUe(ref reader);

        Assert.That(value, Is.EqualTo(1u));
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadUe значение 2")]
    public void ExpGolombReadUeTwo()
    {
        // ue(2) = 011 → codeNum = 2^1 - 1 + 1 = 2
        ReadOnlySpan<byte> data = [0x60]; // 0110 0000
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadUe(ref reader);

        Assert.That(value, Is.EqualTo(2u));
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadUe значение 6")]
    public void ExpGolombReadUeSix()
    {
        // ue(6): codeNum+1=7=0b111, M=2 → bits = 00 111 → 00111 000 = 0x38
        ReadOnlySpan<byte> data = [0x38]; // 0011 1000
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadUe(ref reader);

        Assert.That(value, Is.EqualTo(6u));
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadSe положительные и отрицательные")]
    public void ExpGolombReadSe()
    {
        // se: codeNum 0→0, 1→+1, 2→-1, 3→+2, 4→-2
        // codeNum=1 → se=+1: bits = 010
        // codeNum=2 → se=-1: bits = 011
        ReadOnlySpan<byte> data = [0x58]; // 0101 1000 → 010 | 11 ...
        var reader = new BitReader(data);

        var val1 = H264ExpGolomb.ReadSe(ref reader); // 010 → codeNum=1 → +1
        Assert.That(val1, Is.EqualTo(1));

        var val2 = H264ExpGolomb.ReadSe(ref reader); // 1 → codeNum=0 → 0
        Assert.That(val2, Is.Zero);
    }

    [TestCase(TestName = "H264 ExpGolomb: WriteUe/ReadUe roundtrip")]
    public void ExpGolombWriteUeReadUeRoundTrip()
    {
        uint[] values = [0, 1, 2, 3, 7, 15, 31, 100, 255, 1000];
        Span<byte> buffer = stackalloc byte[256];

        foreach (var expected in values)
        {
            buffer.Clear();
            var writer = new BitWriter(buffer);
            H264ExpGolomb.WriteUe(ref writer, expected);
            writer.Flush();

            var reader = new BitReader(buffer);
            var actual = H264ExpGolomb.ReadUe(ref reader);

            Assert.That(actual, Is.EqualTo(expected), $"Roundtrip failed for ue({expected})");
        }
    }

    [TestCase(TestName = "H264 ExpGolomb: WriteSe/ReadSe roundtrip")]
    public void ExpGolombWriteSeReadSeRoundTrip()
    {
        int[] values = [0, 1, -1, 2, -2, 10, -10, 127, -128];
        Span<byte> buffer = stackalloc byte[256];

        foreach (var expected in values)
        {
            buffer.Clear();
            var writer = new BitWriter(buffer);
            H264ExpGolomb.WriteSe(ref writer, expected);
            writer.Flush();

            var reader = new BitReader(buffer);
            var actual = H264ExpGolomb.ReadSe(ref reader);

            Assert.That(actual, Is.EqualTo(expected), $"Roundtrip failed for se({expected})");
        }
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadTe с range=1")]
    public void ExpGolombReadTeRange1()
    {
        // te(v) с range=1: читает 1 бит и инвертирует
        ReadOnlySpan<byte> data = [0x00]; // бит = 0 → value = 1
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadTe(ref reader, 1);

        Assert.That(value, Is.EqualTo(1u));
    }

    [TestCase(TestName = "H264 ExpGolomb: ReadTe с range>1 = ReadUe")]
    public void ExpGolombReadTeRangeGreaterThan1()
    {
        // te(v) с range>1: эквивалентно ue(v)
        ReadOnlySpan<byte> data = [0x40]; // 0100 0000 → ue = 1
        var reader = new BitReader(data);

        var value = H264ExpGolomb.ReadTe(ref reader, 5);

        Assert.That(value, Is.EqualTo(1u));
    }

    [TestCase(TestName = "H264 ExpGolomb: последовательное чтение нескольких значений")]
    public void ExpGolombSequentialRead()
    {
        // Записываем несколько значений подряд
        Span<byte> buffer = stackalloc byte[64];
        var writer = new BitWriter(buffer);

        H264ExpGolomb.WriteUe(ref writer, 0u);   // 1
        H264ExpGolomb.WriteUe(ref writer, 5u);   // 00110
        H264ExpGolomb.WriteUe(ref writer, 3u);   // 00100
        writer.Flush();

        var reader = new BitReader(buffer);
        Assert.That(H264ExpGolomb.ReadUe(ref reader), Is.Zero);
        Assert.That(H264ExpGolomb.ReadUe(ref reader), Is.EqualTo(5u));
        Assert.That(H264ExpGolomb.ReadUe(ref reader), Is.EqualTo(3u));
    }

    #endregion

    #region NAL Parser Tests

    [TestCase(TestName = "H264 NAL: ParseHeader корректно извлекает поля")]
    public void NalParseHeaderCorrect()
    {
        // byte = 0_01_00101 → forbidden=0, ref_idc=1, unit_type=5 (IDR)
        var header = H264Nal.ParseHeader(0x25);

        Assert.That(header.ForbiddenBit, Is.Zero);
        Assert.That(header.RefIdc, Is.EqualTo(1));
        Assert.That(header.UnitType, Is.EqualTo(5));
    }

    [TestCase(TestName = "H264 NAL: ParseHeader SPS (type=7)")]
    public void NalParseHeaderSps()
    {
        // ref_idc=3, unit_type=7 → 0_11_00111 = 0x67
        var header = H264Nal.ParseHeader(0x67);

        Assert.That(header.ForbiddenBit, Is.Zero);
        Assert.That(header.RefIdc, Is.EqualTo(3));
        Assert.That(header.UnitType, Is.EqualTo(H264Constants.NalSps));
    }

    [TestCase(TestName = "H264 NAL: ParseHeader PPS (type=8)")]
    public void NalParseHeaderPps()
    {
        // ref_idc=3, unit_type=8 → 0_11_01000 = 0x68
        var header = H264Nal.ParseHeader(0x68);

        Assert.That(header.ForbiddenBit, Is.Zero);
        Assert.That(header.RefIdc, Is.EqualTo(3));
        Assert.That(header.UnitType, Is.EqualTo(H264Constants.NalPps));
    }

    [TestCase(TestName = "H264 NAL: FindStartCode 3-byte")]
    public void NalFindStartCode3Byte()
    {
        ReadOnlySpan<byte> data = [0xFF, 0x00, 0x00, 0x01, 0x67];

        var pos = H264Nal.FindStartCode(data);

        Assert.That(pos, Is.EqualTo(1));
    }

    [TestCase(TestName = "H264 NAL: FindStartCode 4-byte")]
    public void NalFindStartCode4Byte()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x01, 0x67];

        var pos = H264Nal.FindStartCode(data);

        Assert.That(pos, Is.Zero);
    }

    [TestCase(TestName = "H264 NAL: FindStartCode не найден")]
    public void NalFindStartCodeNotFound()
    {
        ReadOnlySpan<byte> data = [0x01, 0x02, 0x03, 0x04];

        var pos = H264Nal.FindStartCode(data);

        Assert.That(pos, Is.EqualTo(-1));
    }

    [TestCase(TestName = "H264 NAL: FindStartCode слишком короткие данные")]
    public void NalFindStartCodeTooShort()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00];

        var pos = H264Nal.FindStartCode(data);

        Assert.That(pos, Is.EqualTo(-1));
    }

    [TestCase(TestName = "H264 NAL: StartCodeLength 3 vs 4 байта")]
    public void NalStartCodeLength()
    {
        ReadOnlySpan<byte> data3 = [0x00, 0x00, 0x01, 0x67];
        ReadOnlySpan<byte> data4 = [0x00, 0x00, 0x00, 0x01, 0x67];

        Assert.That(H264Nal.StartCodeLength(data3, 0), Is.EqualTo(3));
        Assert.That(H264Nal.StartCodeLength(data4, 0), Is.EqualTo(4));
    }

    [TestCase(TestName = "H264 NAL: RemoveEmulationPrevention удаляет 0x03")]
    public void NalRemoveEmulationPrevention()
    {
        // 0x00 0x00 0x03 → 0x00 0x00
        ReadOnlySpan<byte> input = [0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x02];
        Span<byte> output = stackalloc byte[input.Length];

        var written = H264Nal.RemoveEmulationPrevention(input, output);

        Assert.That(written, Is.EqualTo(6));
        ReadOnlySpan<byte> expected = [0x00, 0x00, 0x01, 0x00, 0x00, 0x02];
        Assert.That(output[..written].SequenceEqual(expected), Is.True);
    }

    [TestCase(TestName = "H264 NAL: RemoveEmulationPrevention без паттернов")]
    public void NalRemoveEmulationPreventionNone()
    {
        ReadOnlySpan<byte> input = [0x01, 0x02, 0x03, 0x04, 0x05];
        Span<byte> output = stackalloc byte[input.Length];

        var written = H264Nal.RemoveEmulationPrevention(input, output);

        Assert.That(written, Is.EqualTo(input.Length));
        Assert.That(output[..written].SequenceEqual(input), Is.True);
    }

    [TestCase(TestName = "H264 NAL: ParseAnnexB один NAL unit")]
    public void NalParseAnnexBSingleNal()
    {
        // 4-byte start code + SPS NAL header (0x67) + 2 bytes data
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x01, 0x67, 0xAB, 0xCD];
        Span<NalUnit> units = stackalloc NalUnit[4];

        var count = H264Nal.ParseAnnexB(data, units);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(units[0].Header.UnitType, Is.EqualTo(H264Constants.NalSps));
        Assert.That(units[0].Length, Is.EqualTo(2)); // 2 bytes after NAL header
    }

    [TestCase(TestName = "H264 NAL: ParseAnnexB несколько NAL units")]
    public void NalParseAnnexBMultipleNals()
    {
        // SPS + PPS + IDR slice
        ReadOnlySpan<byte> data = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E, // SPS
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80, // PPS
            0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x40, // IDR
        ];
        Span<NalUnit> units = stackalloc NalUnit[8];

        var count = H264Nal.ParseAnnexB(data, units);

        Assert.That(count, Is.EqualTo(3));
        Assert.That(units[0].Header.UnitType, Is.EqualTo(H264Constants.NalSps));
        Assert.That(units[1].Header.UnitType, Is.EqualTo(H264Constants.NalPps));
        Assert.That(units[2].Header.UnitType, Is.EqualTo(H264Constants.NalSliceIdr));
    }

    [TestCase(TestName = "H264 NAL: ParseAvcc парсит length-prefixed NAL")]
    public void NalParseAvccSingleNal()
    {
        // 4-byte length prefix (3) + NAL: type=5 (IDR) + 2 bytes
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x03, 0x65, 0x88, 0x80];
        Span<NalUnit> units = stackalloc NalUnit[4];

        var count = H264Nal.ParseAvcc(data, 4, units);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(units[0].Header.UnitType, Is.EqualTo(H264Constants.NalSliceIdr));
    }

    #endregion

    #region SPS Parser Tests

    [TestCase(TestName = "H264 SPS: парсинг Baseline Profile")]
    public void SpsParseBaseline()
    {
        // Hand-crafted minimal SPS for Baseline profile, 16×16 (1 MB)
        // profile_idc=66 (Baseline), constraint_set0=1, level_idc=30
        // seq_parameter_set_id=0, log2_max_frame_num_minus4=0
        // pic_order_cnt_type=0, log2_max_pic_order_cnt_lsb_minus4=0
        // max_num_ref_frames=0, gaps_in_frame_num=0
        // pic_width_in_mbs_minus1=0, pic_height_in_map_units_minus1=0
        // frame_mbs_only=1, direct_8x8=0, cropping=0, vui=0
        Span<byte> rbsp = stackalloc byte[64];
        var w = new BitWriter(rbsp);

        w.WriteBits(66, 8);          // profile_idc = Baseline
        w.WriteBits(1, 1);           // constraint_set0_flag
        w.WriteBits(0, 1);           // constraint_set1_flag
        w.WriteBits(0, 1);           // constraint_set2_flag
        w.WriteBits(0, 1);           // constraint_set3_flag
        w.WriteBits(0, 4);           // reserved_zero_4bits
        w.WriteBits(30, 8);          // level_idc = 3.0
        H264ExpGolomb.WriteUe(ref w, 0);  // seq_parameter_set_id
        H264ExpGolomb.WriteUe(ref w, 0);  // log2_max_frame_num_minus4
        H264ExpGolomb.WriteUe(ref w, 0);  // pic_order_cnt_type
        H264ExpGolomb.WriteUe(ref w, 0);  // log2_max_pic_order_cnt_lsb_minus4
        H264ExpGolomb.WriteUe(ref w, 0);  // max_num_ref_frames
        w.WriteBits(0, 1);           // gaps_in_frame_num
        H264ExpGolomb.WriteUe(ref w, 0);  // pic_width_in_mbs_minus1 = 0 → 1 MB → 16px
        H264ExpGolomb.WriteUe(ref w, 0);  // pic_height_in_map_units_minus1 = 0 → 1 MB → 16px
        w.WriteBits(1, 1);           // frame_mbs_only_flag
        w.WriteBits(0, 1);           // direct_8x8_inference_flag
        w.WriteBits(0, 1);           // frame_cropping_flag
        w.WriteBits(0, 1);           // vui_parameters_present_flag
        w.Flush();

        var sps = H264Sps.Parse(rbsp);

        Assert.That(sps.ProfileIdc, Is.EqualTo(66));
        Assert.That(sps.LevelIdc, Is.EqualTo(30));
        Assert.That(sps.Width, Is.EqualTo(16));
        Assert.That(sps.Height, Is.EqualTo(16));
        Assert.That(sps.MbWidth, Is.EqualTo(1));
        Assert.That(sps.MbHeight, Is.EqualTo(1));
        Assert.That(sps.FrameMbsOnlyFlag, Is.True);
        Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1u)); // 4:2:0 default
    }

    [TestCase(TestName = "H264 SPS: парсинг 320×240 (20×15 MB)")]
    public void SpsParse320x240()
    {
        Span<byte> rbsp = stackalloc byte[64];
        var w = new BitWriter(rbsp);

        w.WriteBits(66, 8);          // profile_idc
        w.WriteBits(1, 1);           // constraint_set0
        w.WriteBits(0, 3);           // constraint_set1-3
        w.WriteBits(0, 4);           // reserved
        w.WriteBits(30, 8);          // level_idc
        H264ExpGolomb.WriteUe(ref w, 0);  // sps_id
        H264ExpGolomb.WriteUe(ref w, 0);  // log2_max_frame_num_minus4
        H264ExpGolomb.WriteUe(ref w, 0);  // pic_order_cnt_type
        H264ExpGolomb.WriteUe(ref w, 0);  // log2_max_pic_order_cnt_lsb_minus4
        H264ExpGolomb.WriteUe(ref w, 0);  // max_num_ref_frames
        w.WriteBits(0, 1);           // gaps_in_frame_num
        H264ExpGolomb.WriteUe(ref w, 19); // pic_width_in_mbs_minus1 = 19 → 20 MBs → 320px
        H264ExpGolomb.WriteUe(ref w, 14); // pic_height_in_map_units_minus1 = 14 → 15 MBs → 240px
        w.WriteBits(1, 1);           // frame_mbs_only_flag
        w.WriteBits(0, 1);           // direct_8x8_inference
        w.WriteBits(0, 1);           // cropping
        w.WriteBits(0, 1);           // vui
        w.Flush();

        var sps = H264Sps.Parse(rbsp);

        Assert.That(sps.Width, Is.EqualTo(320));
        Assert.That(sps.Height, Is.EqualTo(240));
        Assert.That(sps.MbWidth, Is.EqualTo(20));
        Assert.That(sps.MbHeight, Is.EqualTo(15));
    }

    [TestCase(TestName = "H264 SPS: MaxFrameNum зависит от log2_max_frame_num")]
    public void SpsMaxFrameNum()
    {
        Span<byte> rbsp = stackalloc byte[64];
        var w = new BitWriter(rbsp);

        w.WriteBits(66, 8);
        w.WriteBits(0, 8); // constraints + reserved combined
        w.WriteBits(30, 8);
        H264ExpGolomb.WriteUe(ref w, 0);  // sps_id
        H264ExpGolomb.WriteUe(ref w, 4);  // log2_max_frame_num_minus4 = 4 → log2=8 → MaxFrameNum=256
        H264ExpGolomb.WriteUe(ref w, 0);  // poc type
        H264ExpGolomb.WriteUe(ref w, 0);  // log2_max_poc_lsb_minus4
        H264ExpGolomb.WriteUe(ref w, 0);  // max_ref_frames
        w.WriteBits(0, 1);
        H264ExpGolomb.WriteUe(ref w, 0);
        H264ExpGolomb.WriteUe(ref w, 0);
        w.WriteBits(1, 1); // frame_mbs_only
        w.WriteBits(0, 1); // direct_8x8
        w.WriteBits(0, 1); // cropping
        w.WriteBits(0, 1); // vui
        w.Flush();

        var sps = H264Sps.Parse(rbsp);

        Assert.That(sps.MaxFrameNum, Is.EqualTo(256u));
    }

    #endregion

    #region PPS Parser Tests

    [TestCase(TestName = "H264 PPS: парсинг минимальной PPS")]
    public void PpsParseMinimal()
    {
        Span<byte> rbsp = stackalloc byte[64];
        var w = new BitWriter(rbsp);

        H264ExpGolomb.WriteUe(ref w, 0);  // pps_id
        H264ExpGolomb.WriteUe(ref w, 0);  // sps_id
        w.WriteBits(0, 1);                // entropy_coding_mode = CAVLC
        w.WriteBits(0, 1);                // bottom_field_pic_order_in_frame_present
        H264ExpGolomb.WriteUe(ref w, 0);  // num_slice_groups_minus1
        H264ExpGolomb.WriteUe(ref w, 0);  // num_ref_idx_l0_default_active_minus1
        H264ExpGolomb.WriteUe(ref w, 0);  // num_ref_idx_l1_default_active_minus1
        w.WriteBits(0, 1);                // weighted_pred_flag
        w.WriteBits(0, 2);                // weighted_bipred_idc
        H264ExpGolomb.WriteSe(ref w, 0);  // pic_init_qp_minus26
        H264ExpGolomb.WriteSe(ref w, 0);  // pic_init_qs_minus26
        H264ExpGolomb.WriteSe(ref w, 0);  // chroma_qp_index_offset
        w.WriteBits(0, 1);                // deblocking_filter_control_present
        w.WriteBits(0, 1);                // constrained_intra_pred
        w.WriteBits(0, 1);                // redundant_pic_cnt_present
        w.Flush();

        var pps = H264Pps.Parse(rbsp);

        Assert.That(pps.PicParameterSetId, Is.Zero);
        Assert.That(pps.SeqParameterSetId, Is.Zero);
        Assert.That(pps.EntropyCodingModeFlag, Is.False); // CAVLC
        Assert.That(pps.PicInitQpMinus26, Is.Zero);
        Assert.That(pps.ChromaQpIndexOffset, Is.Zero);
        Assert.That(pps.DeblockingFilterControlPresentFlag, Is.False);
    }

    [TestCase(TestName = "H264 PPS: entropy_coding_mode = CABAC")]
    public void PpsParseCabac()
    {
        Span<byte> rbsp = stackalloc byte[64];
        var w = new BitWriter(rbsp);

        H264ExpGolomb.WriteUe(ref w, 1);  // pps_id = 1
        H264ExpGolomb.WriteUe(ref w, 0);  // sps_id
        w.WriteBits(1, 1);                // entropy_coding_mode = CABAC
        w.WriteBits(0, 1);
        H264ExpGolomb.WriteUe(ref w, 0);
        H264ExpGolomb.WriteUe(ref w, 0);
        H264ExpGolomb.WriteUe(ref w, 0);
        w.WriteBits(0, 1);
        w.WriteBits(0, 2);
        H264ExpGolomb.WriteSe(ref w, -2); // pic_init_qp_minus26 = -2
        H264ExpGolomb.WriteSe(ref w, 0);
        H264ExpGolomb.WriteSe(ref w, 1);  // chroma_qp_index_offset = 1
        w.WriteBits(1, 1);                // deblocking_filter_control_present = true
        w.WriteBits(0, 1);
        w.WriteBits(0, 1);
        w.Flush();

        var pps = H264Pps.Parse(rbsp);

        Assert.That(pps.PicParameterSetId, Is.EqualTo(1u));
        Assert.That(pps.EntropyCodingModeFlag, Is.True); // CABAC
        Assert.That(pps.PicInitQpMinus26, Is.EqualTo(-2));
        Assert.That(pps.ChromaQpIndexOffset, Is.EqualTo(1));
        Assert.That(pps.DeblockingFilterControlPresentFlag, Is.True);
    }

    #endregion

    #region DCT Tests

    [TestCase(TestName = "H264 DCT: ForwardDct4x4 + InverseDct4x4Add roundtrip")]
    public void DctForwardInverseRoundTrip()
    {
        // Для flat-блока (все пиксели одинаковы): forward DCT даёт только DC коэффициент (AC = 0)
        // input = all 8 → forward DC = 16*8 = 128 (матрица Cf нормирует в 16×)
        Span<short> input = stackalloc short[16];
        input.Fill(8);

        Span<short> dctCoeffs = stackalloc short[16];
        H264Dct.ForwardDct4x4(input, dctCoeffs);

        // DC = 16 * value, AC = 0 для flat-блока
        Assert.That(dctCoeffs[0], Is.EqualTo(128), "DC = 16 * 8");
        for (var i = 1; i < 16; i++)
            Assert.That(dctCoeffs[i], Is.Zero, $"AC[{i}] должен быть 0 для flat-блока");

        // Inverse: масштабирует (coeff + 32) >> 6, добавляя к prediction
        // Для DC=128: (128+32)>>6 = 2 для каждого пикселя
        // С prediction=6 → результат = 6+2 = 8 = исходное значение
        Span<byte> prediction = stackalloc byte[16];
        prediction.Fill(6);
        H264Dct.InverseDct4x4Add(dctCoeffs, prediction, 4);

        for (var i = 0; i < 16; i++)
            Assert.That(prediction[i], Is.EqualTo(8), $"Pixel {i}: prediction+residual = 6+2 = 8");
    }

    [TestCase(TestName = "H264 DCT: InverseDct4x4DcAdd добавляет DC ко всем пикселям")]
    public void DctInverseDcOnlyAdd()
    {
        // Prediction = все 100
        Span<byte> dst = stackalloc byte[16];
        dst.Fill(100);

        // DC coefficient = 64 → (64 + 32) >> 6 = 1
        H264Dct.InverseDct4x4DcAdd(64, dst, 4);

        for (var i = 0; i < 16; i++)
            Assert.That(dst[i], Is.EqualTo(101));
    }

    [TestCase(TestName = "H264 DCT: InverseDct4x4DcAdd с клиппингом")]
    public void DctInverseDcOnlyClipping()
    {
        Span<byte> dst = stackalloc byte[16];
        dst.Fill(250);

        // DC coefficient = 640 → (640 + 32) >> 6 = 10 → 250 + 10 = 260 → clipped to 255
        H264Dct.InverseDct4x4DcAdd(640, dst, 4);

        for (var i = 0; i < 16; i++)
            Assert.That(dst[i], Is.EqualTo(255));
    }

    [TestCase(TestName = "H264 DCT: InverseHadamard2x2 chroma DC")]
    public void DctInverseHadamard2x2()
    {
        Span<short> input = [10, 20, 30, 40];
        Span<short> output = stackalloc short[4];

        H264Dct.InverseHadamard2x2(input, output);

        // H2x2: [a+b+c+d, a-b+c-d, a+b-c-d, a-b-c+d]
        // = [100, -20, -40, 0]
        Assert.That(output[0], Is.EqualTo(100));
        Assert.That(output[1], Is.EqualTo(-20));
        Assert.That(output[2], Is.EqualTo(-40));
        Assert.That(output[3], Is.Zero);
    }

    [TestCase(TestName = "H264 DCT: InverseHadamard4x4 luma DC")]
    public void DctInverseHadamard4x4()
    {
        // Input: DC коэффициенты от 16 блоков 4×4
        Span<short> input = stackalloc short[16];
        input[0] = 100; // только DC
        // остальные = 0

        Span<short> output = stackalloc short[16];
        H264Dct.InverseHadamard4x4(input, output);

        // С единственным DC=100: все выходные = (100 + 2) >> 2 = 25
        for (var i = 0; i < 16; i++)
            Assert.That(output[i], Is.EqualTo(25), $"output[{i}]");
    }

    [TestCase(TestName = "H264 DCT: ForwardDct4x4 нулевой вход")]
    public void DctForwardZeroInput()
    {
        Span<short> input = stackalloc short[16];
        input.Clear();

        Span<short> output = stackalloc short[16];
        H264Dct.ForwardDct4x4(input, output);

        for (var i = 0; i < 16; i++)
            Assert.That(output[i], Is.Zero);
    }

    [TestCase(TestName = "H264 DCT: ForwardDct4x4 постоянный блок")]
    public void DctForwardConstantBlock()
    {
        // Постоянный блок → только DC коэффициент
        Span<short> input = stackalloc short[16];
        input.Fill(50);

        Span<short> output = stackalloc short[16];
        H264Dct.ForwardDct4x4(input, output);

        Assert.That(output[0], Is.Not.Zero, "DC должен быть ненулевым");

        // Все AC коэффициенты должны быть 0 для постоянного блока
        for (var i = 1; i < 16; i++)
            Assert.That(output[i], Is.Zero, $"AC[{i}] должен быть 0 для постоянного блока");
    }

    #endregion

    #region Macroblock Type Tests

    [TestCase(TestName = "H264 Macroblock: GetIntra16x16PredMode для mb_type 1-24")]
    public void MbGetIntra16x16PredMode()
    {
        // mb_type 1-4 → pred mode 0-3
        Assert.That(H264Macroblock.GetIntra16x16PredMode(1), Is.Zero);
        Assert.That(H264Macroblock.GetIntra16x16PredMode(2), Is.EqualTo(1));
        Assert.That(H264Macroblock.GetIntra16x16PredMode(3), Is.EqualTo(2));
        Assert.That(H264Macroblock.GetIntra16x16PredMode(4), Is.EqualTo(3));

        // mb_type 5-8 → pred mode 0-3 (pattern repeats)
        Assert.That(H264Macroblock.GetIntra16x16PredMode(5), Is.Zero);
        Assert.That(H264Macroblock.GetIntra16x16PredMode(8), Is.EqualTo(3));

        // mb_type 0 (INxN) → 0
        Assert.That(H264Macroblock.GetIntra16x16PredMode(0), Is.Zero);

        // mb_type 25 (PCM) → 0
        Assert.That(H264Macroblock.GetIntra16x16PredMode(25), Is.Zero);
    }

    [TestCase(TestName = "H264 Macroblock: Block4x4 координаты в raster scan порядке")]
    public void MbBlock4x4Coordinates()
    {
        // Проверяем первые 4 позиции (верхний-левый квадрант)
        Assert.That(H264Macroblock.Block4x4X[0], Is.Zero);
        Assert.That(H264Macroblock.Block4x4Y[0], Is.Zero);

        Assert.That(H264Macroblock.Block4x4X[1], Is.EqualTo(4));
        Assert.That(H264Macroblock.Block4x4Y[1], Is.Zero);

        Assert.That(H264Macroblock.Block4x4X[2], Is.Zero);
        Assert.That(H264Macroblock.Block4x4Y[2], Is.EqualTo(4));

        Assert.That(H264Macroblock.Block4x4X[3], Is.EqualTo(4));
        Assert.That(H264Macroblock.Block4x4Y[3], Is.EqualTo(4));
    }

    [TestCase(TestName = "H264 Macroblock: DecodeCbpIntra для codeNum значений")]
    public void MbDecodeCbpIntra()
    {
        // codeNum=3 → luma=0, chroma=0 (Table 9-4)
        var (luma3, chroma3) = H264Macroblock.DecodeCbpIntra(3);
        Assert.That(luma3, Is.Zero);
        Assert.That(chroma3, Is.Zero);

        // codeNum=0 → luma=47, chroma=0
        var (luma0, chroma0) = H264Macroblock.DecodeCbpIntra(0);
        Assert.That(luma0, Is.EqualTo(47));
        Assert.That(chroma0, Is.Zero);

        // codeNum=29 → luma=1, chroma=2
        var (luma29, chroma29) = H264Macroblock.DecodeCbpIntra(29);
        Assert.That(luma29, Is.EqualTo(1));
        Assert.That(chroma29, Is.EqualTo(2));
    }

    #endregion

    #region Constants Tests

    [TestCase(TestName = "H264 Constants: NAL типы")]
    public void ConstantsNalTypes()
    {
        Assert.That(H264Constants.NalSliceNonIdr, Is.EqualTo(1));
        Assert.That(H264Constants.NalSliceIdr, Is.EqualTo(5));
        Assert.That(H264Constants.NalSps, Is.EqualTo(7));
        Assert.That(H264Constants.NalPps, Is.EqualTo(8));
    }

    [TestCase(TestName = "H264 Constants: профили")]
    public void ConstantsProfiles()
    {
        Assert.That(H264Constants.ProfileBaseline, Is.EqualTo(66));
    }

    [TestCase(TestName = "H264 Constants: MbSize")]
    public void ConstantsMbSize()
    {
        Assert.That(H264Constants.MbSize, Is.EqualTo(16));
    }

    [TestCase(TestName = "H264 Constants: ChromaQp таблица длина 52")]
    public void ConstantsChromaQpTable()
    {
        var table = H264Constants.ChromaQp;
        Assert.That(table.Length, Is.EqualTo(52));
        Assert.That(table[0], Is.Zero);
        Assert.That(table[51], Is.EqualTo(39));
    }

    [TestCase(TestName = "H264 Constants: ZigzagScan 16 элементов")]
    public void ConstantsZigzagScan()
    {
        var scan = H264Constants.ZigzagScan4x4;
        Assert.That(scan.Length, Is.EqualTo(16));
        // Первый элемент — DC (0,0)
        Assert.That(scan[0], Is.Zero);
    }

    #endregion

    #region Decoder Integration Tests

    [TestCase(TestName = "H264 Decoder: пустые данные возвращают InvalidData")]
    public void DecoderEmptyDataReturnsInvalidData()
    {
        var decoder = new H264Decoder();
        ReadOnlySpan<byte> data = [0x00, 0x01];

        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();
        var result = decoder.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "H264 Decoder: нет start codes возвращает InvalidData")]
    public void DecoderNoStartCodesReturnsInvalidData()
    {
        var decoder = new H264Decoder();
        ReadOnlySpan<byte> data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();
        var result = decoder.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "H264 Decoder: SPS/PPS парсинг из Annex B")]
    public void DecoderParsesSpsPps()
    {
        // Создаём минимальное SPS + PPS в Annex B формате
        var spsRbsp = BuildMinimalSpsRbsp(16, 16);
        var ppsRbsp = BuildMinimalPpsRbsp();

        // Annex B: start code + NAL header + RBSP
        var data = new List<byte>();

        // SPS NAL
        data.AddRange([0x00, 0x00, 0x00, 0x01, 0x67]); // start code + SPS header
        data.AddRange(spsRbsp);

        // PPS NAL
        data.AddRange([0x00, 0x00, 0x00, 0x01, 0x68]); // start code + PPS header
        data.AddRange(ppsRbsp);

        var decoder = new H264Decoder();
        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        // Только SPS+PPS без slice → ожидаем Success (нет slice = нет ошибки)
        var result = decoder.Decode(data.ToArray(), ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "H264 Decoder: полный I-frame 16×16 (1 MB)")]
    public void DecoderDecodesSingleMbIFrame()
    {
        // Собираем полный Annex B bitstream: SPS + PPS + IDR slice
        var bitstream = BuildMinimalIFrameBitstream(16, 16);

        var decoder = new H264Decoder();
        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        // Декодер должен успешно обработать битстрим
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Проверяем что пиксели записаны (не все нули)
        var pixelData = frame.PackedData.Data;
        var hasNonZero = false;

        for (var i = 0; i < pixelData.Length; i++)
        {
            if (pixelData[i] != 0)
            {
                hasNonZero = true;
                break;
            }
        }

        Assert.That(hasNonZero, Is.True, "Кадр не должен быть полностью чёрным после декодирования");
    }

    [TestCase(TestName = "H264 Decoder: полный I-frame 32×32 (4 MB)")]
    public void DecoderDecodes4MbIFrame()
    {
        var bitstream = BuildMinimalIFrameBitstream(32, 32);

        var decoder = new H264Decoder();
        using var frameBuffer = new VideoFrameBuffer(32, 32, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Проверяем что альфа-канал корректен (должен быть 255)
        var data = frame.PackedData;
        var alphaOk = true;

        for (var y = 0; y < 32; y++)
        {
            var row = data.GetRow(y);

            for (var x = 0; x < 32; x++)
            {
                if (row[(x * 4) + 3] != 255)
                {
                    alphaOk = false;
                    break;
                }
            }

            if (!alphaOk)
                break;
        }

        Assert.That(alphaOk, Is.True, "Альфа-канал должен быть 255");
    }

    [TestCase(TestName = "H264 Decoder: I_PCM macroblock 16×16")]
    public void DecoderDecodesIpcmMacroblock()
    {
        // Создаём битстрим с I_PCM макроблоком (mb_type=25)
        var bitstream = BuildIpcmIFrameBitstream(16, 16);

        var decoder = new H264Decoder();
        using var frameBuffer = new VideoFrameBuffer(16, 16, VideoPixelFormat.Rgba32);
        var frame = frameBuffer.AsFrame();

        var result = decoder.Decode(bitstream, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Helpers

    /// <summary>Создаёт минимальный SPS RBSP (без NAL header).</summary>
    private static byte[] BuildMinimalSpsRbsp(int width, int height)
    {
        var mbW = width / 16;
        var mbH = height / 16;

        Span<byte> buf = stackalloc byte[128];
        var w = new BitWriter(buf);

        w.WriteBits(66, 8);           // profile_idc = Baseline
        w.WriteBits(1, 1);            // constraint_set0
        w.WriteBits(0, 3);            // constraint_set1-3
        w.WriteBits(0, 4);            // reserved
        w.WriteBits(30, 8);           // level_idc
        H264ExpGolomb.WriteUe(ref w, 0);   // sps_id
        H264ExpGolomb.WriteUe(ref w, 0);   // log2_max_frame_num_minus4
        H264ExpGolomb.WriteUe(ref w, 0);   // poc_type
        H264ExpGolomb.WriteUe(ref w, 0);   // log2_max_poc_lsb_minus4
        H264ExpGolomb.WriteUe(ref w, 0);   // max_ref_frames
        w.WriteBits(0, 1);            // gaps_in_frame_num
        H264ExpGolomb.WriteUe(ref w, (uint)(mbW - 1));  // pic_width_in_mbs_minus1
        H264ExpGolomb.WriteUe(ref w, (uint)(mbH - 1));  // pic_height_in_map_units_minus1
        w.WriteBits(1, 1);            // frame_mbs_only
        w.WriteBits(0, 1);            // direct_8x8
        w.WriteBits(0, 1);            // cropping
        w.WriteBits(0, 1);            // vui
        w.Flush();

        var byteCount = (w.BitPosition + 7) / 8;
        return buf[..byteCount].ToArray();
    }

    /// <summary>Создаёт минимальный PPS RBSP (без NAL header).</summary>
    private static byte[] BuildMinimalPpsRbsp()
    {
        Span<byte> buf = stackalloc byte[64];
        var w = new BitWriter(buf);

        H264ExpGolomb.WriteUe(ref w, 0);   // pps_id
        H264ExpGolomb.WriteUe(ref w, 0);   // sps_id
        w.WriteBits(0, 1);            // entropy_coding_mode = CAVLC
        w.WriteBits(0, 1);            // bottom_field_pic_order
        H264ExpGolomb.WriteUe(ref w, 0);   // num_slice_groups_minus1
        H264ExpGolomb.WriteUe(ref w, 0);   // num_ref_idx_l0
        H264ExpGolomb.WriteUe(ref w, 0);   // num_ref_idx_l1
        w.WriteBits(0, 1);            // weighted_pred
        w.WriteBits(0, 2);            // weighted_bipred_idc
        H264ExpGolomb.WriteSe(ref w, 0);   // pic_init_qp_minus26
        H264ExpGolomb.WriteSe(ref w, 0);   // pic_init_qs_minus26
        H264ExpGolomb.WriteSe(ref w, 0);   // chroma_qp_index_offset
        w.WriteBits(1, 1);            // deblocking_filter_control_present
        w.WriteBits(0, 1);            // constrained_intra_pred
        w.WriteBits(0, 1);            // redundant_pic_cnt_present
        w.Flush();

        var byteCount = (w.BitPosition + 7) / 8;
        return buf[..byteCount].ToArray();
    }

    /// <summary>Создаёт полный Annex B I-frame битстрим с I16×16 prediction mode DC.</summary>
    private static byte[] BuildMinimalIFrameBitstream(int width, int height)
    {
        var mbW = width / 16;
        var mbH = height / 16;
        var totalMbs = mbW * mbH;

        var spsRbsp = BuildMinimalSpsRbsp(width, height);
        var ppsRbsp = BuildMinimalPpsRbsp();

        // Строим slice RBSP
        Span<byte> sliceBuf = stackalloc byte[4096];
        var sw = new BitWriter(sliceBuf);

        // Slice header (IDR I-slice)
        H264ExpGolomb.WriteUe(ref sw, 0);   // first_mb_in_slice
        H264ExpGolomb.WriteUe(ref sw, 2);   // slice_type = I (2)
        H264ExpGolomb.WriteUe(ref sw, 0);   // pic_parameter_set_id
        sw.WriteBits(0, 4);            // frame_num (log2_max_frame_num_minus4+4 = 4 bits)

        // IDR: idr_pic_id
        H264ExpGolomb.WriteUe(ref sw, 0);   // idr_pic_id

        // pic_order_cnt_type == 0: pic_order_cnt_lsb
        sw.WriteBits(0, 4);            // pic_order_cnt_lsb (4 bits)

        // dec_ref_pic_marking for IDR
        sw.WriteBits(0, 1);            // no_output_of_prior_pics
        sw.WriteBits(0, 1);            // long_term_reference

        // slice_qp_delta
        H264ExpGolomb.WriteSe(ref sw, 0);   // slice_qp_delta → qp = 26

        // deblocking_filter_control_present = true, so:
        H264ExpGolomb.WriteUe(ref sw, 1);   // disable_deblocking_filter_idc = 1 (disabled)

        // Macroblock data: I16×16 mode DC (mb_type=1), no residual
        for (var mb = 0; mb < totalMbs; mb++)
        {
            // mb_type = 1 → I_16x16_0_0_0 (pred_mode=0=DC, cbpLuma=0, cbpChroma=0)
            H264ExpGolomb.WriteUe(ref sw, 1);

            // intra_chroma_pred_mode
            H264ExpGolomb.WriteUe(ref sw, 0);   // DC chroma

            // mb_qp_delta (since cbp=0, this is NOT coded for I16x16 with cbpLuma=0 and cbpChroma=0)
            // Actually for I16x16: mb_qp_delta is always present
            H264ExpGolomb.WriteSe(ref sw, 0);

            // With cbpLuma=0 and cbpChroma=0: only Intra16x16DCLevel is coded
            // Intra16x16DCLevel: CAVLC residual block with nC context
            // For simplest case: all zeros → coeff_token = (0,0) = VLC 1 (single bit "1")
            sw.WriteBits(1, 1); // coeff_token for nC=0: TotalCoeff=0, TrailingOnes=0 → code = "1"
        }

        // RBSP trailing bits
        sw.WriteBits(1, 1); // rbsp_stop_one_bit
        while (sw.BitPosition % 8 != 0)
            sw.WriteBits(0, 1); // alignment

        sw.Flush();

        var sliceByteCount = (sw.BitPosition + 7) / 8;
        var sliceRbsp = sliceBuf[..sliceByteCount].ToArray();

        // Собираем Annex B bitstream
        var result = new List<byte>();

        // SPS
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x67]);
        result.AddRange(spsRbsp);

        // PPS
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x68]);
        result.AddRange(ppsRbsp);

        // IDR slice
        result.AddRange([0x00, 0x00, 0x00, 0x01, 0x65]);
        result.AddRange(sliceRbsp);

        return [.. result];
    }

    /// <summary>Создаёт I-frame с I_PCM макроблоком.</summary>
    private static byte[] BuildIpcmIFrameBitstream(int width, int height)
    {
        var mbW = width / 16;
        var mbH = height / 16;
        var totalMbs = mbW * mbH;

        var spsRbsp = BuildMinimalSpsRbsp(width, height);
        var ppsRbsp = BuildMinimalPpsRbsp();

        // Slice RBSP с I_PCM
        Span<byte> sliceBuf = stackalloc byte[8192];
        var sw = new BitWriter(sliceBuf);

        // Slice header
        H264ExpGolomb.WriteUe(ref sw, 0);   // first_mb_in_slice
        H264ExpGolomb.WriteUe(ref sw, 2);   // slice_type = I
        H264ExpGolomb.WriteUe(ref sw, 0);   // pps_id
        sw.WriteBits(0, 4);            // frame_num
        H264ExpGolomb.WriteUe(ref sw, 0);   // idr_pic_id
        sw.WriteBits(0, 4);            // pic_order_cnt_lsb
        sw.WriteBits(0, 1);            // no_output_of_prior_pics
        sw.WriteBits(0, 1);            // long_term_reference
        H264ExpGolomb.WriteSe(ref sw, 0);   // slice_qp_delta
        H264ExpGolomb.WriteUe(ref sw, 1);   // disable_deblocking = 1

        for (var mb = 0; mb < totalMbs; mb++)
        {
            // mb_type = 25 → I_PCM
            H264ExpGolomb.WriteUe(ref sw, 25);

            // Align to byte boundary
            while (sw.BitPosition % 8 != 0)
                sw.WriteBits(0, 1);

            // PCM data: 256 luma + 64 Cb + 64 Cr = 384 bytes
            // Fill with gray (Y=128, Cb=128, Cr=128)
            for (var p = 0; p < 256; p++)
                sw.WriteBits(128, 8); // luma

            for (var p = 0; p < 64; p++)
                sw.WriteBits(128, 8); // Cb

            for (var p = 0; p < 64; p++)
                sw.WriteBits(128, 8); // Cr
        }

        // RBSP trailing
        sw.WriteBits(1, 1);
        while (sw.BitPosition % 8 != 0)
            sw.WriteBits(0, 1);

        sw.Flush();

        var sliceByteCount = (sw.BitPosition + 7) / 8;
        var sliceRbsp = sliceBuf[..sliceByteCount].ToArray();

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
}
