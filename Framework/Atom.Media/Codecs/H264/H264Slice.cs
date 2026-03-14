#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0017, IDE0045, IDE0047, IDE0048, S1871, IDE0078

using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 Slice Header parsing (ITU-T H.264 Section 7.3.3).
/// </summary>
internal sealed class H264SliceHeader
{
    #region Fields

    public uint FirstMbInSlice;
    public H264Macroblock.SliceType SliceType;
    public uint PicParameterSetId;
    public uint FrameNum;
    public bool FieldPicFlag;
    public bool BottomFieldFlag;
    public uint IdrPicId;
    public uint PicOrderCntLsb;
    public int DeltaPicOrderCntBottom;
    public int DeltaPicOrderCnt0;
    public int DeltaPicOrderCnt1;
    public uint RedundantPicCnt;
    public bool DirectSpatialMvPredFlag;
    public uint NumRefIdxL0ActiveMinus1;
    public uint NumRefIdxL1ActiveMinus1;
    public int SliceQpDelta;
    public bool SpForSwitchFlag;
    public int SliceQsDelta;
    public uint DisableDeblockingFilterIdc;
    public int SliceAlphaC0OffsetDiv2;
    public int SliceBetaOffsetDiv2;
    public uint SliceGroupChangeCycle;

    #endregion

    #region Computed

    public int Qp(int picInitQp) => picInitQp + 26 + SliceQpDelta;

    #endregion

    #region Parse

    /// <summary>
    /// Парсит slice header из RBSP данных (после NAL byte).
    /// </summary>
    public static H264SliceHeader Parse(
        ReadOnlySpan<byte> rbsp,
        H264Sps sps,
        H264Pps pps,
        byte nalUnitType)
    {
        var reader = new BitReader(rbsp);
        var hdr = new H264SliceHeader();

        hdr.FirstMbInSlice = H264ExpGolomb.ReadUe(ref reader);

        var rawSliceType = H264ExpGolomb.ReadUe(ref reader);
        hdr.SliceType = (H264Macroblock.SliceType)(rawSliceType > 4 ? rawSliceType - 5 : rawSliceType);

        hdr.PicParameterSetId = H264ExpGolomb.ReadUe(ref reader);

        if (sps.SeparateColourPlaneFlag)
        {
            reader.ReadBits(2); // colour_plane_id
        }

        hdr.FrameNum = reader.ReadBits((int)(sps.Log2MaxFrameNumMinus4 + 4));

        if (!sps.FrameMbsOnlyFlag)
        {
            hdr.FieldPicFlag = reader.ReadBits(1) != 0;

            if (hdr.FieldPicFlag)
            {
                hdr.BottomFieldFlag = reader.ReadBits(1) != 0;
            }
        }

        if (nalUnitType == H264Constants.NalSliceIdr)
        {
            hdr.IdrPicId = H264ExpGolomb.ReadUe(ref reader);
        }

        if (sps.PicOrderCntType == 0)
        {
            hdr.PicOrderCntLsb = reader.ReadBits((int)(sps.Log2MaxPicOrderCntLsbMinus4 + 4));

            if (pps.BottomFieldPicOrderInFramePresentFlag && !hdr.FieldPicFlag)
            {
                hdr.DeltaPicOrderCntBottom = H264ExpGolomb.ReadSe(ref reader);
            }
        }

        if (sps.PicOrderCntType == 1 && !sps.DeltaPicOrderAlwaysZeroFlag)
        {
            hdr.DeltaPicOrderCnt0 = H264ExpGolomb.ReadSe(ref reader);

            if (pps.BottomFieldPicOrderInFramePresentFlag && !hdr.FieldPicFlag)
            {
                hdr.DeltaPicOrderCnt1 = H264ExpGolomb.ReadSe(ref reader);
            }
        }

        if (pps.RedundantPicCntPresentFlag)
        {
            hdr.RedundantPicCnt = H264ExpGolomb.ReadUe(ref reader);
        }

        var isB = hdr.SliceType == H264Macroblock.SliceType.B;
        var isP = hdr.SliceType == H264Macroblock.SliceType.P;

        if (isB)
        {
            hdr.DirectSpatialMvPredFlag = reader.ReadBits(1) != 0;
        }

        if (isP || isB)
        {
            var numRefIdxActiveOverrideFlag = reader.ReadBits(1) != 0;

            if (numRefIdxActiveOverrideFlag)
            {
                hdr.NumRefIdxL0ActiveMinus1 = H264ExpGolomb.ReadUe(ref reader);

                if (isB)
                {
                    hdr.NumRefIdxL1ActiveMinus1 = H264ExpGolomb.ReadUe(ref reader);
                }
            }
            else
            {
                hdr.NumRefIdxL0ActiveMinus1 = pps.NumRefIdxL0DefaultActiveMinus1;
                hdr.NumRefIdxL1ActiveMinus1 = pps.NumRefIdxL1DefaultActiveMinus1;
            }

            // ref_pic_list_modification (skip for now)
            SkipRefPicListModification(ref reader, hdr.SliceType);
        }

        // pred_weight_table — skip for Baseline
        // dec_ref_pic_marking
        if (nalUnitType == H264Constants.NalSliceIdr)
        {
            SkipDecRefPicMarking(ref reader, isIdr: true);
        }
        else if (reader.RemainingBits > 0)
        {
            // For non-IDR slices with nal_ref_idc > 0 — we skip since most basic streams don't need this
        }

        hdr.SliceQpDelta = H264ExpGolomb.ReadSe(ref reader);

        if (hdr.SliceType is H264Macroblock.SliceType.Sp or H264Macroblock.SliceType.Si)
        {
            if (hdr.SliceType is H264Macroblock.SliceType.Sp)
            {
                hdr.SpForSwitchFlag = reader.ReadBits(1) != 0;
            }

            hdr.SliceQsDelta = H264ExpGolomb.ReadSe(ref reader);
        }

        if (pps.DeblockingFilterControlPresentFlag)
        {
            hdr.DisableDeblockingFilterIdc = H264ExpGolomb.ReadUe(ref reader);

            if (hdr.DisableDeblockingFilterIdc != 1)
            {
                hdr.SliceAlphaC0OffsetDiv2 = H264ExpGolomb.ReadSe(ref reader);
                hdr.SliceBetaOffsetDiv2 = H264ExpGolomb.ReadSe(ref reader);
            }
        }

        return hdr;
    }

    #endregion

    #region Skip Helpers

    private static void SkipRefPicListModification(ref BitReader reader, H264Macroblock.SliceType sliceType)
    {
        if (sliceType is H264Macroblock.SliceType.P or H264Macroblock.SliceType.B
            or H264Macroblock.SliceType.Sp)
        {
            var refPicListModificationFlagL0 = reader.ReadBits(1) != 0;

            if (refPicListModificationFlagL0)
            {
                uint modOp;

                do
                {
                    modOp = H264ExpGolomb.ReadUe(ref reader);

                    if (modOp is 0 or 1)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // abs_diff_pic_num_minus1
                    }
                    else if (modOp == 2)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // long_term_pic_num
                    }
                } while (modOp != 3 && reader.RemainingBits > 0);
            }
        }

        if (sliceType == H264Macroblock.SliceType.B)
        {
            var refPicListModificationFlagL1 = reader.ReadBits(1) != 0;

            if (refPicListModificationFlagL1)
            {
                uint modOp;

                do
                {
                    modOp = H264ExpGolomb.ReadUe(ref reader);

                    if (modOp is 0 or 1)
                    {
                        H264ExpGolomb.ReadUe(ref reader);
                    }
                    else if (modOp == 2)
                    {
                        H264ExpGolomb.ReadUe(ref reader);
                    }
                } while (modOp != 3 && reader.RemainingBits > 0);
            }
        }
    }

    private static void SkipDecRefPicMarking(ref BitReader reader, bool isIdr)
    {
        if (isIdr)
        {
            reader.ReadBits(1); // no_output_of_prior_pics_flag
            reader.ReadBits(1); // long_term_reference_flag
        }
        else
        {
            var adaptiveRefPicMarkingModeFlag = reader.ReadBits(1) != 0;

            if (adaptiveRefPicMarkingModeFlag)
            {
                uint mmco;

                do
                {
                    mmco = H264ExpGolomb.ReadUe(ref reader);

                    if (mmco is 1 or 3)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // difference_of_pic_nums_minus1
                    }

                    if (mmco == 2)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // long_term_pic_num
                    }

                    if (mmco is 3 or 6)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // long_term_frame_idx
                    }

                    if (mmco == 4)
                    {
                        H264ExpGolomb.ReadUe(ref reader); // max_long_term_frame_idx_plus1
                    }
                } while (mmco != 0 && reader.RemainingBits > 0);
            }
        }
    }

    #endregion
}
