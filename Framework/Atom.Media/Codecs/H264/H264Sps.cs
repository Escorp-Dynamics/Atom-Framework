#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0017, IDE0045, IDE0047, IDE0048

using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 Sequence Parameter Set (ITU-T H.264 Section 7.3.2.1).
/// </summary>
internal sealed class H264Sps
{
    #region Fields

    public byte ProfileIdc;
    public bool ConstraintSet0Flag;
    public bool ConstraintSet1Flag;
    public bool ConstraintSet2Flag;
    public bool ConstraintSet3Flag;
    public byte LevelIdc;
    public uint SeqParameterSetId;
    public uint ChromaFormatIdc = 1;
    public bool SeparateColourPlaneFlag;
    public uint BitDepthLumaMinus8;
    public uint BitDepthChromaMinus8;
    public bool QpprimeYZeroTransformBypassFlag;
    public bool SeqScalingMatrixPresentFlag;
    public uint Log2MaxFrameNumMinus4;
    public uint PicOrderCntType;
    public uint Log2MaxPicOrderCntLsbMinus4;
    public bool DeltaPicOrderAlwaysZeroFlag;
    public int OffsetForNonRefPic;
    public int OffsetForTopToBottomField;
    public uint NumRefFramesInPicOrderCntCycle;
    public int[] OffsetForRefFrame = [];
    public uint MaxNumRefFrames;
    public bool GapsInFrameNumValueAllowedFlag;
    public uint PicWidthInMbsMinus1;
    public uint PicHeightInMapUnitsMinus1;
    public bool FrameMbsOnlyFlag = true;
    public bool MbAdaptiveFrameFieldFlag;
    public bool Direct8x8InferenceFlag;
    public bool FrameCroppingFlag;
    public uint FrameCropLeftOffset;
    public uint FrameCropRightOffset;
    public uint FrameCropTopOffset;
    public uint FrameCropBottomOffset;
    public bool VuiParametersPresentFlag;

    // VUI
    public bool AspectRatioInfoPresentFlag;
    public byte AspectRatioIdc;
    public ushort SarWidth;
    public ushort SarHeight;

    #endregion

    #region Computed Properties

    public int Width => (int)((PicWidthInMbsMinus1 + 1) * 16 - FrameCropLeftOffset * 2 - FrameCropRightOffset * 2);
    public int Height => (int)((2 - (FrameMbsOnlyFlag ? 1u : 0u)) * (PicHeightInMapUnitsMinus1 + 1) * 16
        - FrameCropTopOffset * 2 - FrameCropBottomOffset * 2);
    public int MbWidth => (int)(PicWidthInMbsMinus1 + 1);
    public int MbHeight => (int)(PicHeightInMapUnitsMinus1 + 1);
    public uint MaxFrameNum => 1u << (int)(Log2MaxFrameNumMinus4 + 4);
    public uint MaxPicOrderCntLsb => 1u << (int)(Log2MaxPicOrderCntLsbMinus4 + 4);
    public int BitDepthLuma => (int)(8 + BitDepthLumaMinus8);
    public int BitDepthChroma => (int)(8 + BitDepthChromaMinus8);

    #endregion

    #region Parse

    /// <summary>
    /// Парсит SPS из RBSP данных (после NAL header byte).
    /// </summary>
    public static H264Sps Parse(ReadOnlySpan<byte> rbsp)
    {
        var reader = new BitReader(rbsp);
        var sps = new H264Sps();

        sps.ProfileIdc = (byte)reader.ReadBits(8);
        sps.ConstraintSet0Flag = reader.ReadBits(1) != 0;
        sps.ConstraintSet1Flag = reader.ReadBits(1) != 0;
        sps.ConstraintSet2Flag = reader.ReadBits(1) != 0;
        sps.ConstraintSet3Flag = reader.ReadBits(1) != 0;
        reader.SkipBits(4); // reserved_zero_4bits

        sps.LevelIdc = (byte)reader.ReadBits(8);
        sps.SeqParameterSetId = H264ExpGolomb.ReadUe(ref reader);

        if (IsHighProfile(sps.ProfileIdc))
        {
            sps.ChromaFormatIdc = H264ExpGolomb.ReadUe(ref reader);

            if (sps.ChromaFormatIdc == 3)
                sps.SeparateColourPlaneFlag = reader.ReadBits(1) != 0;

            sps.BitDepthLumaMinus8 = H264ExpGolomb.ReadUe(ref reader);
            sps.BitDepthChromaMinus8 = H264ExpGolomb.ReadUe(ref reader);
            sps.QpprimeYZeroTransformBypassFlag = reader.ReadBits(1) != 0;
            sps.SeqScalingMatrixPresentFlag = reader.ReadBits(1) != 0;

            if (sps.SeqScalingMatrixPresentFlag)
            {
                var count = sps.ChromaFormatIdc != 3 ? 8 : 12;
                for (var i = 0; i < count; i++)
                {
                    var listPresent = reader.ReadBits(1) != 0;
                    if (listPresent)
                        SkipScalingList(ref reader, i < 6 ? 16 : 64);
                }
            }
        }

        sps.Log2MaxFrameNumMinus4 = H264ExpGolomb.ReadUe(ref reader);
        sps.PicOrderCntType = H264ExpGolomb.ReadUe(ref reader);

        if (sps.PicOrderCntType == 0)
        {
            sps.Log2MaxPicOrderCntLsbMinus4 = H264ExpGolomb.ReadUe(ref reader);
        }
        else if (sps.PicOrderCntType == 1)
        {
            sps.DeltaPicOrderAlwaysZeroFlag = reader.ReadBits(1) != 0;
            sps.OffsetForNonRefPic = H264ExpGolomb.ReadSe(ref reader);
            sps.OffsetForTopToBottomField = H264ExpGolomb.ReadSe(ref reader);
            sps.NumRefFramesInPicOrderCntCycle = H264ExpGolomb.ReadUe(ref reader);

            sps.OffsetForRefFrame = new int[sps.NumRefFramesInPicOrderCntCycle];
            for (var i = 0; i < (int)sps.NumRefFramesInPicOrderCntCycle; i++)
                sps.OffsetForRefFrame[i] = H264ExpGolomb.ReadSe(ref reader);
        }

        sps.MaxNumRefFrames = H264ExpGolomb.ReadUe(ref reader);
        sps.GapsInFrameNumValueAllowedFlag = reader.ReadBits(1) != 0;
        sps.PicWidthInMbsMinus1 = H264ExpGolomb.ReadUe(ref reader);
        sps.PicHeightInMapUnitsMinus1 = H264ExpGolomb.ReadUe(ref reader);
        sps.FrameMbsOnlyFlag = reader.ReadBits(1) != 0;

        if (!sps.FrameMbsOnlyFlag)
            sps.MbAdaptiveFrameFieldFlag = reader.ReadBits(1) != 0;

        sps.Direct8x8InferenceFlag = reader.ReadBits(1) != 0;
        sps.FrameCroppingFlag = reader.ReadBits(1) != 0;

        if (sps.FrameCroppingFlag)
        {
            sps.FrameCropLeftOffset = H264ExpGolomb.ReadUe(ref reader);
            sps.FrameCropRightOffset = H264ExpGolomb.ReadUe(ref reader);
            sps.FrameCropTopOffset = H264ExpGolomb.ReadUe(ref reader);
            sps.FrameCropBottomOffset = H264ExpGolomb.ReadUe(ref reader);
        }

        sps.VuiParametersPresentFlag = reader.ReadBits(1) != 0;

        if (sps.VuiParametersPresentFlag)
            ParseVui(ref reader, sps);

        return sps;
    }

    #endregion

    #region Private

    private static bool IsHighProfile(byte profileIdc) =>
        profileIdc is H264Constants.ProfileHigh or
        H264Constants.ProfileHigh10 or
        H264Constants.ProfileHigh422 or
        H264Constants.ProfileHigh444Predictive;

    private static void SkipScalingList(ref BitReader reader, int size)
    {
        var lastScale = 8;
        var nextScale = 8;

        for (var i = 0; i < size; i++)
        {
            if (nextScale != 0)
            {
                var deltaScale = H264ExpGolomb.ReadSe(ref reader);
                nextScale = (lastScale + deltaScale + 256) % 256;
            }

            lastScale = nextScale == 0 ? lastScale : nextScale;
        }
    }

    private static void ParseVui(ref BitReader reader, H264Sps sps)
    {
        sps.AspectRatioInfoPresentFlag = reader.ReadBits(1) != 0;

        if (sps.AspectRatioInfoPresentFlag)
        {
            sps.AspectRatioIdc = (byte)reader.ReadBits(8);

            if (sps.AspectRatioIdc == 255) // Extended_SAR
            {
                sps.SarWidth = (ushort)reader.ReadBits(16);
                sps.SarHeight = (ushort)reader.ReadBits(16);
            }
        }

        // overscan_info_present_flag
        if (reader.ReadBits(1) != 0)
            reader.SkipBits(1); // overscan_appropriate_flag

        // video_signal_type_present_flag
        if (reader.ReadBits(1) != 0)
        {
            reader.SkipBits(3); // video_format
            reader.SkipBits(1); // video_full_range_flag

            // colour_description_present_flag
            if (reader.ReadBits(1) != 0)
            {
                reader.SkipBits(8);  // colour_primaries
                reader.SkipBits(8);  // transfer_characteristics
                reader.SkipBits(8);  // matrix_coefficients
            }
        }

        // chroma_loc_info_present_flag
        if (reader.ReadBits(1) != 0)
        {
            H264ExpGolomb.ReadUe(ref reader); // chroma_sample_loc_type_top_field
            H264ExpGolomb.ReadUe(ref reader); // chroma_sample_loc_type_bottom_field
        }

        // Remaining VUI fields skipped (timing_info, nal_hrd, etc.)
    }

    #endregion
}
