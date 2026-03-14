#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0017, IDE0045

using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 Picture Parameter Set (ITU-T H.264 Section 7.3.2.2).
/// </summary>
internal sealed class H264Pps
{
    #region Fields

    public uint PicParameterSetId;
    public uint SeqParameterSetId;
    public bool EntropyCodingModeFlag; // false=CAVLC, true=CABAC
    public bool BottomFieldPicOrderInFramePresentFlag;
    public uint NumSliceGroupsMinus1;
    public uint NumRefIdxL0DefaultActiveMinus1;
    public uint NumRefIdxL1DefaultActiveMinus1;
    public bool WeightedPredFlag;
    public byte WeightedBipredIdc;
    public int PicInitQpMinus26;
    public int PicInitQsMinus26;
    public int ChromaQpIndexOffset;
    public bool DeblockingFilterControlPresentFlag;
    public bool ConstrainedIntraPredFlag;
    public bool RedundantPicCntPresentFlag;
    public bool Transform8x8ModeFlag;
    public bool PicScalingMatrixPresentFlag;
    public int SecondChromaQpIndexOffset;

    #endregion

    #region Parse

    /// <summary>
    /// Парсит PPS из RBSP данных.
    /// </summary>
    public static H264Pps Parse(ReadOnlySpan<byte> rbsp)
    {
        var reader = new BitReader(rbsp);
        var pps = new H264Pps();

        pps.PicParameterSetId = H264ExpGolomb.ReadUe(ref reader);
        pps.SeqParameterSetId = H264ExpGolomb.ReadUe(ref reader);
        pps.EntropyCodingModeFlag = reader.ReadBits(1) != 0;
        pps.BottomFieldPicOrderInFramePresentFlag = reader.ReadBits(1) != 0;
        pps.NumSliceGroupsMinus1 = H264ExpGolomb.ReadUe(ref reader);

        if (pps.NumSliceGroupsMinus1 > 0)
        {
            var sliceGroupMapType = H264ExpGolomb.ReadUe(ref reader);

            if (sliceGroupMapType == 0)
            {
                for (var i = 0; i <= (int)pps.NumSliceGroupsMinus1; i++)
                    H264ExpGolomb.ReadUe(ref reader); // run_length_minus1
            }
            else if (sliceGroupMapType == 2)
            {
                for (var i = 0; i < (int)pps.NumSliceGroupsMinus1; i++)
                {
                    H264ExpGolomb.ReadUe(ref reader); // top_left
                    H264ExpGolomb.ReadUe(ref reader); // bottom_right
                }
            }
            else if (sliceGroupMapType is 3 or 4 or 5)
            {
                reader.ReadBits(1); // slice_group_change_direction_flag
                H264ExpGolomb.ReadUe(ref reader); // slice_group_change_rate_minus1
            }
            else if (sliceGroupMapType == 6)
            {
                var picSizeInMapUnits = H264ExpGolomb.ReadUe(ref reader) + 1;
                var bits = CeilLog2(pps.NumSliceGroupsMinus1 + 1);

                for (var i = 0; i < (int)picSizeInMapUnits; i++)
                    reader.ReadBits(bits);
            }
        }

        pps.NumRefIdxL0DefaultActiveMinus1 = H264ExpGolomb.ReadUe(ref reader);
        pps.NumRefIdxL1DefaultActiveMinus1 = H264ExpGolomb.ReadUe(ref reader);
        pps.WeightedPredFlag = reader.ReadBits(1) != 0;
        pps.WeightedBipredIdc = (byte)reader.ReadBits(2);
        pps.PicInitQpMinus26 = H264ExpGolomb.ReadSe(ref reader);
        pps.PicInitQsMinus26 = H264ExpGolomb.ReadSe(ref reader);
        pps.ChromaQpIndexOffset = H264ExpGolomb.ReadSe(ref reader);
        pps.DeblockingFilterControlPresentFlag = reader.ReadBits(1) != 0;
        pps.ConstrainedIntraPredFlag = reader.ReadBits(1) != 0;
        pps.RedundantPicCntPresentFlag = reader.ReadBits(1) != 0;

        // Check if more RBSP data
        if (reader.RemainingBits > 8)
        {
            pps.Transform8x8ModeFlag = reader.ReadBits(1) != 0;
            pps.PicScalingMatrixPresentFlag = reader.ReadBits(1) != 0;

            if (pps.PicScalingMatrixPresentFlag)
            {
                var count = 6 + (pps.Transform8x8ModeFlag ? 6 : 0);
                for (var i = 0; i < count; i++)
                {
                    if (reader.ReadBits(1) != 0) // scaling list present
                        SkipScalingList(ref reader, i < 6 ? 16 : 64);
                }
            }

            pps.SecondChromaQpIndexOffset = H264ExpGolomb.ReadSe(ref reader);
        }
        else
        {
            pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
        }

        return pps;
    }

    #endregion

    #region Private

    private static int CeilLog2(uint value)
    {
        var bits = 0;
        var v = value - 1;

        while (v > 0)
        {
            v >>= 1;
            bits++;
        }

        return bits;
    }

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

    #endregion
}
