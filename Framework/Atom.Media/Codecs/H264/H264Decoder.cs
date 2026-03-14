#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0017, IDE0045, IDE0047, IDE0048, IDE0010, MA0182, CA2014
#pragma warning disable S1172, IDE0060, IDE0022, MA0038

using System.Runtime.CompilerServices;
using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 Baseline Profile decoder (ITU-T H.264 — I-frame only).
/// </summary>
/// <remarks>
/// Декодирует I-slices из Annex B или AVCC bitstream в RGBA32 VideoFrame.
/// Поддерживает: CAVLC, 4×4/16×16 intra prediction, deblocking filter, 4:2:0 chroma.
/// </remarks>
internal sealed class H264Decoder
{
    #region State

    private readonly Dictionary<uint, H264Sps> spsMap = [];
    private readonly Dictionary<uint, H264Pps> ppsMap = [];

    // Frame planes (YUV 4:2:0)
    private byte[] lumaPlane = [];
    private byte[] cbPlane = [];
    private byte[] crPlane = [];

    // Per-macroblock NNZ for CAVLC context
    private H264Macroblock.MbInfo[] mbInfos = [];

    // Frame dimensions
    private int mbWidth;
    private int mbHeight;
    private int lumaStride;
    private int chromaStride;

    #endregion

    /// <summary>
    /// Decodes an H.264 Annex B bitstream (one or more NAL units) into RGBA32 pixels.
    /// </summary>
    public unsafe CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        if (data.Length < 4)
        {
            return CodecResult.InvalidData;
        }

        // Parse NAL units
        Span<NalUnit> nalUnits = stackalloc NalUnit[32];
        var nalCount = H264Nal.ParseAnnexB(data, nalUnits);

        if (nalCount == 0)
        {
            return CodecResult.InvalidData;
        }

        // Temporary RBSP buffer for emulation prevention removal
        var rbspBuffer = new byte[data.Length];

        for (var i = 0; i < nalCount; i++)
        {
            var nal = nalUnits[i];
            var nalType = nal.Header.UnitType;
            var nalData = data.Slice(nal.Offset, nal.Length);

            // Remove emulation prevention bytes
            var rbsp = rbspBuffer.AsSpan();
            var rbspLen = H264Nal.RemoveEmulationPrevention(nalData, rbsp);
            rbsp = rbsp[..rbspLen];

            switch (nalType)
            {
                case H264Constants.NalSps:
                    var sps = H264Sps.Parse(rbsp);
                    spsMap[sps.SeqParameterSetId] = sps;
                    break;

                case H264Constants.NalPps:
                    var pps = H264Pps.Parse(rbsp);
                    ppsMap[pps.PicParameterSetId] = pps;
                    break;

                case H264Constants.NalSliceIdr:
                case H264Constants.NalSliceNonIdr:
                    var result = DecodeSlice(rbsp, nalType, ref frame);

                    if (result != CodecResult.Success)
                    {
                        return result;
                    }

                    break;
            }
        }

        return CodecResult.Success;
    }

    /// <summary>
    /// Decodes AVCC-formatted NAL units (length-prefixed, used inside MP4/MKV containers).
    /// </summary>
    public unsafe CodecResult DecodeAvcc(
        ReadOnlySpan<byte> data,
        int nalLengthSize,
        H264Sps sps,
        H264Pps pps,
        ref VideoFrame frame)
    {
        if (data.Length < nalLengthSize)
        {
            return CodecResult.InvalidData;
        }

        // Store pre-parsed SPS/PPS
        spsMap[sps.SeqParameterSetId] = sps;
        ppsMap[pps.PicParameterSetId] = pps;

        Span<NalUnit> nalUnits = stackalloc NalUnit[16];
        var nalCount = H264Nal.ParseAvcc(data, nalLengthSize, nalUnits);
        var rbspBuffer = new byte[data.Length];

        for (var i = 0; i < nalCount; i++)
        {
            var nal = nalUnits[i];
            var nalType = nal.Header.UnitType;
            var nalData = data.Slice(nal.Offset, nal.Length);

            var rbsp = rbspBuffer.AsSpan();
            var rbspLen = H264Nal.RemoveEmulationPrevention(nalData, rbsp);
            rbsp = rbsp[..rbspLen];

            if (nalType is H264Constants.NalSliceIdr or H264Constants.NalSliceNonIdr)
            {
                var result = DecodeSlice(rbsp, nalType, ref frame);

                if (result != CodecResult.Success)
                {
                    return result;
                }
            }
        }

        return CodecResult.Success;
    }

    #region Slice Decoding

    private CodecResult DecodeSlice(ReadOnlySpan<byte> rbsp, byte nalType, ref VideoFrame frame)
    {
        // Parse slice header (first few bytes)
        // We need to peek at PPS ID to establish active SPS/PPS
        var tempReader = new BitReader(rbsp);
        H264ExpGolomb.ReadUe(ref tempReader); // first_mb_in_slice
        H264ExpGolomb.ReadUe(ref tempReader); // slice_type
        var ppsId = H264ExpGolomb.ReadUe(ref tempReader);

        if (!ppsMap.TryGetValue(ppsId, out var pps))
        {
            return CodecResult.InvalidData;
        }

        if (!spsMap.TryGetValue(pps.SeqParameterSetId, out var sps))
        {
            return CodecResult.InvalidData;
        }

        // Allocate/resize frame planes
        AllocateFrameBuffers(sps);

        // Parse full slice header
        var sliceHeader = H264SliceHeader.Parse(rbsp, sps, pps, nalType);

        if (sliceHeader.SliceType != H264Macroblock.SliceType.I)
        {
            return CodecResult.UnsupportedFormat; // Only I-slices for now
        }

        // Compute slice QP
        var sliceQp = pps.PicInitQpMinus26 + 26 + sliceHeader.SliceQpDelta;

        // Skip to macroblock data (re-parse from after slice header)
        var reader = new BitReader(rbsp);
        SkipSliceHeader(ref reader, sps, pps, nalType, sliceHeader);

        // Decode macroblocks
        var mbIdx = (int)sliceHeader.FirstMbInSlice;
        var currentQp = sliceQp;

        while (mbIdx < mbWidth * mbHeight && reader.RemainingBits > 0)
        {
            var mbX = mbIdx % mbWidth;
            var mbY = mbIdx / mbWidth;

            var result = DecodeMacroblock(ref reader, mbX, mbY, ref currentQp, sps, pps);

            if (result != CodecResult.Success)
            {
                break; // Tolerate errors at end of slice
            }

            mbIdx++;
        }

        // Apply deblocking filter
        if (sliceHeader.DisableDeblockingFilterIdc != 1)
        {
            ApplyDeblocking(sliceQp);
        }

        // Convert YUV → RGBA and write to frame
        ConvertYuvToRgba(ref frame);

        return CodecResult.Success;
    }

    private void AllocateFrameBuffers(H264Sps sps)
    {
        mbWidth = sps.MbWidth;
        mbHeight = sps.MbHeight;
        lumaStride = mbWidth * 16;
        chromaStride = mbWidth * 8;

        var lumaSize = lumaStride * mbHeight * 16;
        var chromaSize = chromaStride * mbHeight * 8;

        if (lumaPlane.Length != lumaSize)
        {
            lumaPlane = new byte[lumaSize];
            cbPlane = new byte[chromaSize];
            crPlane = new byte[chromaSize];
            mbInfos = new H264Macroblock.MbInfo[mbWidth * mbHeight];
        }
        else
        {
            Array.Clear(lumaPlane);
            Array.Clear(cbPlane);
            Array.Clear(crPlane);
            Array.Clear(mbInfos);
        }
    }

    #endregion

    #region Macroblock Decoding

    private unsafe CodecResult DecodeMacroblock(
        ref BitReader reader, int mbX, int mbY,
        ref int currentQp, H264Sps sps, H264Pps pps)
    {
        var mbIdx = (mbY * mbWidth) + mbX;
        ref var mb = ref mbInfos[mbIdx];

        // mb_type
        mb.MbType = (int)H264ExpGolomb.ReadUe(ref reader);

        if (H264Macroblock.IsPcm(mb.MbType))
        {
            return DecodePcmMb(ref reader, mbX, mbY, ref mb);
        }

        if (H264Macroblock.IsIntra16x16(mb.MbType))
        {
            mb.Intra16x16PredMode = H264Macroblock.GetIntra16x16PredMode(mb.MbType);
            mb.CbpLuma = H264Macroblock.GetI16x16CbpLuma(mb.MbType);
            mb.CbpChroma = H264Macroblock.GetI16x16CbpChroma(mb.MbType);
        }
        else
        {
            // I_NxN: decode intra 4×4 prediction modes
            DecodeIntra4x4Modes(ref reader, mbX, mbY, ref mb);
        }

        // Chroma prediction mode
        mb.IntraChromaPredMode = (int)H264ExpGolomb.ReadUe(ref reader);

        // CBP (if not I16×16)
        if (!H264Macroblock.IsIntra16x16(mb.MbType))
        {
            var cbpCode = H264ExpGolomb.ReadUe(ref reader);
            var (luma, chroma) = H264Macroblock.DecodeCbpIntra(cbpCode);
            mb.CbpLuma = luma;
            mb.CbpChroma = chroma;
        }

        // QP delta
        if (mb.CbpLuma != 0 || mb.CbpChroma != 0 || H264Macroblock.IsIntra16x16(mb.MbType))
        {
            mb.QpDelta = H264ExpGolomb.ReadSe(ref reader);
            currentQp = Math.Clamp(currentQp + mb.QpDelta, 0, 51);
        }

        // Decode residual
        DecodeResidual(ref reader, mbX, mbY, ref mb, currentQp);

        // Apply prediction
        ApplyPrediction(mbX, mbY, ref mb);

        // Add residual to prediction
        AddResidualToFrame(mbX, mbY, ref mb, currentQp);

        return CodecResult.Success;
    }

    private unsafe void DecodeIntra4x4Modes(
        ref BitReader reader, int mbX, int mbY, ref H264Macroblock.MbInfo mb)
    {
        for (var blkIdx = 0; blkIdx < 16; blkIdx++)
        {
            // Get predicted mode from neighbors
            var modeA = GetNeighborIntra4x4Mode(mbX, mbY, blkIdx, isLeft: true);
            var modeB = GetNeighborIntra4x4Mode(mbX, mbY, blkIdx, isLeft: false);
            var predicted = H264Macroblock.PredictIntra4x4Mode(modeA, modeB);

            mb.Intra4x4PredMode[blkIdx] = H264Macroblock.DecodeIntra4x4PredMode(ref reader, predicted);
        }
    }

    private unsafe int GetNeighborIntra4x4Mode(int mbX, int mbY, int blkIdx, bool isLeft)
    {
        // 4×4 block position within MB
        var bx = blkIdx % 4;
        var by = blkIdx / 4;

        int neighborMbIdx;
        int neighborBlkIdx;

        if (isLeft)
        {
            if (bx > 0)
            {
                neighborMbIdx = (mbY * mbWidth) + mbX;
                neighborBlkIdx = (by * 4) + (bx - 1);
            }
            else if (mbX > 0)
            {
                neighborMbIdx = (mbY * mbWidth) + (mbX - 1);
                neighborBlkIdx = (by * 4) + 3;
            }
            else
            {
                return -1;
            }
        }
        else
        {
            if (by > 0)
            {
                neighborMbIdx = (mbY * mbWidth) + mbX;
                neighborBlkIdx = ((by - 1) * 4) + bx;
            }
            else if (mbY > 0)
            {
                neighborMbIdx = ((mbY - 1) * mbWidth) + mbX;
                neighborBlkIdx = (3 * 4) + bx;
            }
            else
            {
                return -1;
            }
        }

        ref var neighborMb = ref mbInfos[neighborMbIdx];

        if (H264Macroblock.IsIntra16x16(neighborMb.MbType))
        {
            return 2; // DC mode
        }

        return neighborMb.Intra4x4PredMode[neighborBlkIdx];
    }

    private unsafe CodecResult DecodePcmMb(
        ref BitReader reader, int mbX, int mbY, ref H264Macroblock.MbInfo mb)
    {
        mb.IsPcm = true;

        // Align to byte boundary
        var bitsToSkip = reader.RemainingBits % 8;
        if (bitsToSkip > 0)
        {
            reader.SkipBits(8 - bitsToSkip);
        }

        // Read 256 luma samples
        var lumaOffset = (mbY * 16 * lumaStride) + (mbX * 16);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                lumaPlane[lumaOffset + (y * lumaStride) + x] = (byte)reader.ReadBits(8);
            }
        }

        // Read 64 Cb + 64 Cr samples
        var chromaOffset = (mbY * 8 * chromaStride) + (mbX * 8);

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                cbPlane[chromaOffset + (y * chromaStride) + x] = (byte)reader.ReadBits(8);
            }
        }

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                crPlane[chromaOffset + (y * chromaStride) + x] = (byte)reader.ReadBits(8);
            }
        }

        // Set all NNZ to 16 for I_PCM (forces max bS for deblocking)
        for (var i = 0; i < 24; i++)
        {
            mb.Nnz[i] = 16;
        }

        return CodecResult.Success;
    }

    #endregion

    #region Residual Decoding

    // Per-MB residual storage
    private short[]? _residualBuffer;

    private unsafe void DecodeResidual(
        ref BitReader reader, int mbX, int mbY,
        ref H264Macroblock.MbInfo mb, int qp)
    {
        _residualBuffer ??= new short[24 * 16]; // 24 blocks × 16 coefficients
        var residual = _residualBuffer.AsSpan();
        residual.Clear();

        if (H264Macroblock.IsIntra16x16(mb.MbType))
        {
            // Luma DC (Intra 16×16)
            var nC = GetLumaNc(mbX, mbY, 0);
            mb.Nnz[0] = H264Cavlc.DecodeResidualBlock(ref reader, residual[..16], nC, 16);

            // Dequant + Hadamard for DC
            Span<short> dcCoeffs = stackalloc short[16];

            for (var i = 0; i < 16; i++)
            {
                dcCoeffs[i] = residual[i];
            }

            H264Dct.InverseHadamard4x4(dcCoeffs, dcCoeffs);

            // Luma AC (each 4×4 block, 15 AC coefficients)
            if (mb.CbpLuma != 0)
            {
                for (var blk = 0; blk < 16; blk++)
                {
                    var blockOffset = blk * 16;
                    residual[blockOffset] = dcCoeffs[blk]; // Set DC from Hadamard

                    if ((mb.CbpLuma & (1 << (blk / 4))) != 0)
                    {
                        nC = GetLumaNc(mbX, mbY, blk);
                        mb.Nnz[blk] = H264Cavlc.DecodeResidualBlock(
                            ref reader, residual.Slice(blockOffset + 1, 15), nC, 15);
                    }
                }
            }
            else
            {
                // Only DC, set DC coefficients
                for (var blk = 0; blk < 16; blk++)
                {
                    residual[blk * 16] = dcCoeffs[blk];
                }
            }
        }
        else
        {
            // I_NxN: each 4×4 block independently
            for (var blk = 0; blk < 16; blk++)
            {
                if ((mb.CbpLuma & (1 << (blk / 4))) != 0)
                {
                    var blockOffset = blk * 16;
                    var nC = GetLumaNc(mbX, mbY, blk);
                    mb.Nnz[blk] = H264Cavlc.DecodeResidualBlock(
                        ref reader, residual.Slice(blockOffset, 16), nC, 16);
                }
            }
        }

        // Chroma DC + AC
        Span<short> cbDc = stackalloc short[4];
        Span<short> crDc = stackalloc short[4];

        if (mb.CbpChroma != 0)
        {
            // Cb DC (2×2 Hadamard)
            var chromaNc = GetChromaNc(mbX, mbY, 0, isCb: true);
            H264Cavlc.DecodeResidualBlock(ref reader, cbDc, chromaNc, 4);
            H264Dct.InverseHadamard2x2(cbDc, cbDc);

            // Cr DC (2×2 Hadamard)
            chromaNc = GetChromaNc(mbX, mbY, 0, isCb: false);
            H264Cavlc.DecodeResidualBlock(ref reader, crDc, chromaNc, 4);
            H264Dct.InverseHadamard2x2(crDc, crDc);

            // Chroma AC
            if (mb.CbpChroma >= 2)
            {
                // Cb AC
                for (var blk = 0; blk < 4; blk++)
                {
                    var blockOffset = (16 + blk) * 16;
                    residual[blockOffset] = cbDc[blk];
                    chromaNc = GetChromaNc(mbX, mbY, blk, isCb: true);
                    mb.Nnz[16 + blk] = H264Cavlc.DecodeResidualBlock(
                        ref reader, residual.Slice(blockOffset + 1, 15), chromaNc, 15);
                }

                // Cr AC
                for (var blk = 0; blk < 4; blk++)
                {
                    var blockOffset = (20 + blk) * 16;
                    residual[blockOffset] = crDc[blk];
                    chromaNc = GetChromaNc(mbX, mbY, blk, isCb: false);
                    mb.Nnz[20 + blk] = H264Cavlc.DecodeResidualBlock(
                        ref reader, residual.Slice(blockOffset + 1, 15), chromaNc, 15);
                }
            }
            else
            {
                // DC only for chroma
                for (var blk = 0; blk < 4; blk++)
                {
                    residual[(16 + blk) * 16] = cbDc[blk];
                    residual[(20 + blk) * 16] = crDc[blk];
                }
            }
        }
    }

    private unsafe int GetLumaNc(int mbX, int mbY, int blkIdx)
    {
        var bx = blkIdx % 4;
        var by = blkIdx / 4;
        var nA = -1;
        var nB = -1;

        // Left neighbor
        if (bx > 0)
        {
            nA = mbInfos[(mbY * mbWidth) + mbX].Nnz[(by * 4) + bx - 1];
        }
        else if (mbX > 0)
        {
            nA = mbInfos[(mbY * mbWidth) + mbX - 1].Nnz[(by * 4) + 3];
        }

        // Top neighbor
        if (by > 0)
        {
            nB = mbInfos[(mbY * mbWidth) + mbX].Nnz[((by - 1) * 4) + bx];
        }
        else if (mbY > 0)
        {
            nB = mbInfos[((mbY - 1) * mbWidth) + mbX].Nnz[(3 * 4) + bx];
        }

        if (nA >= 0 && nB >= 0)
        {
            return (nA + nB + 1) / 2;
        }

        if (nA >= 0)
        {
            return nA;
        }

        if (nB >= 0)
        {
            return nB;
        }

        return 0;
    }

    private static int GetChromaNc(int mbX, int mbY, int blkIdx, bool isCb) =>
        // Simplified: chroma nC uses -1 for chroma DC table selection
        -1;

    #endregion

    #region Prediction

    private unsafe void ApplyPrediction(int mbX, int mbY, ref H264Macroblock.MbInfo mb)
    {
        if (mb.IsPcm)
        {
            return;
        }

        var lumaOffset = (mbY * 16 * lumaStride) + (mbX * 16);

        // Gather neighbor pixels for luma prediction
        Span<byte> above = stackalloc byte[20]; // 16 above + 4 above-right (padded)
        Span<byte> left = stackalloc byte[16];
        byte aboveLeft = 128;
        var hasAbove = mbY > 0;
        var hasLeft = mbX > 0;

        if (hasAbove)
        {
            var prevRow = lumaOffset - lumaStride;

            for (var x = 0; x < 16; x++)
            {
                above[x] = lumaPlane[prevRow + x];
            }

            // Above-right (next MB or padding)
            if (mbX + 1 < mbWidth)
            {
                for (var x = 0; x < 4; x++)
                {
                    above[16 + x] = lumaPlane[prevRow + 16 + x];
                }
            }
            else
            {
                above[16] = above[17] = above[18] = above[19] = above[15];
            }
        }
        else
        {
            above.Fill(128);
        }

        if (hasLeft)
        {
            for (var y = 0; y < 16; y++)
            {
                left[y] = lumaPlane[lumaOffset + (y * lumaStride) - 1];
            }
        }
        else
        {
            left.Fill(128);
        }

        if (hasAbove && hasLeft)
        {
            aboveLeft = lumaPlane[lumaOffset - lumaStride - 1];
        }

        // Luma prediction
        if (H264Macroblock.IsIntra16x16(mb.MbType))
        {
            H264Prediction.PredictIntra16x16(
                lumaPlane.AsSpan(lumaOffset), lumaStride,
                mb.Intra16x16PredMode, above, left, aboveLeft, hasAbove, hasLeft);
        }
        else
        {
            // 4×4 prediction for each sub-block
            Span<byte> blkAbove = stackalloc byte[8];
            Span<byte> blkLeft = stackalloc byte[4];

            for (var blk = 0; blk < 16; blk++)
            {
                var bx = H264Macroblock.Block4x4X[blk];
                var by = H264Macroblock.Block4x4Y[blk];
                var blockOffset = lumaOffset + (by * lumaStride) + bx;

                // Gather 4×4 neighbors
                byte blkAboveLeft = 128;
                var blkHasAbove = (mbY > 0) || (by > 0);
                var blkHasLeft = (mbX > 0) || (bx > 0);

                if (blkHasAbove)
                {
                    var prevRow = blockOffset - lumaStride;

                    for (var x = 0; x < 8 && prevRow + x < lumaPlane.Length; x++)
                    {
                        blkAbove[x] = lumaPlane[prevRow + x];
                    }
                }
                else
                {
                    blkAbove.Fill(128);
                }

                if (blkHasLeft)
                {
                    for (var y = 0; y < 4; y++)
                    {
                        blkLeft[y] = lumaPlane[blockOffset + (y * lumaStride) - 1];
                    }
                }
                else
                {
                    blkLeft.Fill(128);
                }

                if (blkHasAbove && blkHasLeft)
                {
                    blkAboveLeft = lumaPlane[blockOffset - lumaStride - 1];
                }

                var hasAboveRight = bx + 4 < 16 || by == 0;

                H264Prediction.PredictIntra4x4(
                    lumaPlane.AsSpan(blockOffset), lumaStride,
                    mb.Intra4x4PredMode[blk],
                    blkAbove, blkLeft, blkAboveLeft,
                    blkHasAbove, blkHasLeft, hasAboveRight);
            }
        }

        // Chroma prediction
        var cbOffset = (mbY * 8 * chromaStride) + (mbX * 8);
        var crOffset = cbOffset;

        Span<byte> chromaAbove = stackalloc byte[8];
        Span<byte> chromaLeft = stackalloc byte[8];
        byte chromaAboveLeft = 128;
        var chromaHasAbove = mbY > 0;
        var chromaHasLeft = mbX > 0;

        // Cb prediction
        if (chromaHasAbove)
        {
            for (var x = 0; x < 8; x++)
            {
                chromaAbove[x] = cbPlane[cbOffset - chromaStride + x];
            }
        }
        else
        {
            chromaAbove.Fill(128);
        }

        if (chromaHasLeft)
        {
            for (var y = 0; y < 8; y++)
            {
                chromaLeft[y] = cbPlane[cbOffset + (y * chromaStride) - 1];
            }
        }
        else
        {
            chromaLeft.Fill(128);
        }

        if (chromaHasAbove && chromaHasLeft)
        {
            chromaAboveLeft = cbPlane[cbOffset - chromaStride - 1];
        }

        H264Prediction.PredictChroma8x8(
            cbPlane.AsSpan(cbOffset), chromaStride,
            mb.IntraChromaPredMode, chromaAbove, chromaLeft,
            chromaAboveLeft, chromaHasAbove, chromaHasLeft);

        // Cr prediction
        if (chromaHasAbove)
        {
            for (var x = 0; x < 8; x++)
            {
                chromaAbove[x] = crPlane[crOffset - chromaStride + x];
            }
        }
        else
        {
            chromaAbove.Fill(128);
        }

        if (chromaHasLeft)
        {
            for (var y = 0; y < 8; y++)
            {
                chromaLeft[y] = crPlane[crOffset + (y * chromaStride) - 1];
            }
        }
        else
        {
            chromaLeft.Fill(128);
        }

        if (chromaHasAbove && chromaHasLeft)
        {
            chromaAboveLeft = crPlane[crOffset - chromaStride - 1];
        }

        H264Prediction.PredictChroma8x8(
            crPlane.AsSpan(crOffset), chromaStride,
            mb.IntraChromaPredMode, chromaAbove, chromaLeft,
            chromaAboveLeft, chromaHasAbove, chromaHasLeft);
    }

    #endregion

    #region Residual Addition

    private unsafe void AddResidualToFrame(
        int mbX, int mbY, ref H264Macroblock.MbInfo mb, int qp)
    {
        if (mb.IsPcm)
        {
            return;
        }

        var residual = (_residualBuffer ?? []).AsSpan();

        if (residual.Length < 24 * 16)
        {
            return;
        }

        var lumaOffset = (mbY * 16 * lumaStride) + (mbX * 16);

        // Add luma residual (IDCT for each 4×4 block)
        for (var blk = 0; blk < 16; blk++)
        {
            var bx = H264Macroblock.Block4x4X[blk];
            var by = H264Macroblock.Block4x4Y[blk];
            var blockOffset = lumaOffset + (by * lumaStride) + bx;
            var coeffs = residual.Slice(blk * 16, 16);

            // Check if block has non-zero coefficients
            var hasCoeffs = false;

            for (var i = 0; i < 16; i++)
            {
                if (coeffs[i] != 0)
                {
                    hasCoeffs = true;
                    break;
                }
            }

            if (hasCoeffs)
            {
                // Dequantize + IDCT + add to prediction
                DequantBlock(coeffs, qp, isLuma: true);
                H264Dct.InverseDct4x4Add(coeffs, lumaPlane.AsSpan(blockOffset), lumaStride);
            }
        }

        // Add chroma residual
        var cbOffset = (mbY * 8 * chromaStride) + (mbX * 8);
        var crOffset = cbOffset;
        var chromaQp = H264Constants.ChromaQp[Math.Clamp(qp, 0, 51)];

        // Cb blocks
        for (var blk = 0; blk < 4; blk++)
        {
            var bx = (blk % 2) * 4;
            var by = (blk / 2) * 4;
            var blockOff = cbOffset + (by * chromaStride) + bx;
            var coeffs = residual.Slice((16 + blk) * 16, 16);

            var hasCoeffs = false;

            for (var i = 0; i < 16; i++)
            {
                if (coeffs[i] != 0)
                {
                    hasCoeffs = true;
                    break;
                }
            }

            if (hasCoeffs)
            {
                DequantBlock(coeffs, chromaQp, isLuma: false);
                H264Dct.InverseDct4x4Add(coeffs, cbPlane.AsSpan(blockOff), chromaStride);
            }
        }

        // Cr blocks
        for (var blk = 0; blk < 4; blk++)
        {
            var bx = (blk % 2) * 4;
            var by = (blk / 2) * 4;
            var blockOff = crOffset + (by * chromaStride) + bx;
            var coeffs = residual.Slice((20 + blk) * 16, 16);

            var hasCoeffs = false;

            for (var i = 0; i < 16; i++)
            {
                if (coeffs[i] != 0)
                {
                    hasCoeffs = true;
                    break;
                }
            }

            if (hasCoeffs)
            {
                DequantBlock(coeffs, chromaQp, isLuma: false);
                H264Dct.InverseDct4x4Add(coeffs, crPlane.AsSpan(blockOff), chromaStride);
            }
        }
    }

    private static void DequantBlock(Span<short> coeffs, int qp, bool isLuma)
    {
        var qpDiv6 = qp / 6;
        var qpMod6 = qp % 6;

        // Simplified dequantization (H.264 Section 8.5.12.1)
        ReadOnlySpan<int> scaleFactors =
        [
            10, 16, 13,
            11, 18, 14,
            13, 20, 16,
            14, 23, 18,
            16, 25, 20,
            18, 29, 23,
        ];

        var scale = scaleFactors[qpMod6 * 3]; // Simplified: use DC scale for all
        var shift = qpDiv6;

        for (var i = 0; i < coeffs.Length; i++)
        {
            coeffs[i] = (short)((coeffs[i] * scale) << shift);
        }
    }

    #endregion

    #region Deblocking

    private unsafe void ApplyDeblocking(int qp)
    {
        Span<int> bsVert = stackalloc int[4];
        Span<int> bsHoriz = stackalloc int[4];

        for (var mbY = 0; mbY < mbHeight; mbY++)
        {
            for (var mbX = 0; mbX < mbWidth; mbX++)
            {
                // Vertical edges
                for (var edge = 0; edge < 4; edge++)
                {
                    var isLeftEdge = edge == 0;
                    bsVert[edge] = H264Deblock.ComputeBoundaryStrength(
                        isIntra: true,
                        isEdgeMbBoundary: isLeftEdge && mbX > 0,
                        hasNonZeroCoeff: true);
                }

                if (mbX > 0 || bsVert[0] > 0)
                {
                    H264Deblock.FilterMbVertical(
                        lumaPlane, mbX, mbY, lumaStride, bsVert, qp);
                }

                // Horizontal edges
                for (var edge = 0; edge < 4; edge++)
                {
                    var isTopEdge = edge == 0;
                    bsHoriz[edge] = H264Deblock.ComputeBoundaryStrength(
                        isIntra: true,
                        isEdgeMbBoundary: isTopEdge && mbY > 0,
                        hasNonZeroCoeff: true);
                }

                if (mbY > 0 || bsHoriz[0] > 0)
                {
                    H264Deblock.FilterMbHorizontal(
                        lumaPlane, mbX, mbY, lumaStride, bsHoriz, qp);
                }
            }
        }
    }

    #endregion

    #region YUV → RGBA Conversion

    private void ConvertYuvToRgba(ref VideoFrame frame)
    {
        var width = mbWidth * 16;
        var height = mbHeight * 16;
        var rgba = frame.PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = rgba.GetRow(y);

            for (var x = 0; x < width; x++)
            {
                var yVal = lumaPlane[(y * lumaStride) + x];
                var cb = cbPlane[((y / 2) * chromaStride) + (x / 2)];
                var cr = crPlane[((y / 2) * chromaStride) + (x / 2)];

                // BT.601 YCbCr → RGB
                var c = yVal - 16;
                var d = cb - 128;
                var e = cr - 128;

                var r = ClipByte((298 * c + 409 * e + 128) >> 8);
                var g = ClipByte((298 * c - 100 * d - 208 * e + 128) >> 8);
                var b = ClipByte((298 * c + 516 * d + 128) >> 8);

                var pixelOffset = x * 4;
                row[pixelOffset] = r;
                row[pixelOffset + 1] = g;
                row[pixelOffset + 2] = b;
                row[pixelOffset + 3] = 255;
            }
        }
    }

    #endregion

    #region Helpers

    private static void SkipSliceHeader(
        ref BitReader reader, H264Sps sps, H264Pps pps,
        byte nalType, H264SliceHeader hdr)
    {
        // Re-parse slice header to advance reader past it
        // This is duplicated work but avoids tracking bit position
        H264ExpGolomb.ReadUe(ref reader); // first_mb_in_slice
        H264ExpGolomb.ReadUe(ref reader); // slice_type
        H264ExpGolomb.ReadUe(ref reader); // pps_id

        if (sps.SeparateColourPlaneFlag)
        {
            reader.ReadBits(2);
        }

        reader.ReadBits((int)(sps.Log2MaxFrameNumMinus4 + 4)); // frame_num

        if (!sps.FrameMbsOnlyFlag)
        {
            var fieldPicFlag = reader.ReadBits(1) != 0;

            if (fieldPicFlag)
            {
                reader.ReadBits(1); // bottom_field_flag
            }
        }

        if (nalType == H264Constants.NalSliceIdr)
        {
            H264ExpGolomb.ReadUe(ref reader); // idr_pic_id
        }

        if (sps.PicOrderCntType == 0)
        {
            reader.ReadBits((int)(sps.Log2MaxPicOrderCntLsbMinus4 + 4));

            if (pps.BottomFieldPicOrderInFramePresentFlag)
            {
                H264ExpGolomb.ReadSe(ref reader);
            }
        }

        if (sps.PicOrderCntType == 1 && !sps.DeltaPicOrderAlwaysZeroFlag)
        {
            H264ExpGolomb.ReadSe(ref reader);

            if (pps.BottomFieldPicOrderInFramePresentFlag)
            {
                H264ExpGolomb.ReadSe(ref reader);
            }
        }

        if (pps.RedundantPicCntPresentFlag)
        {
            H264ExpGolomb.ReadUe(ref reader);
        }

        // dec_ref_pic_marking
        if (nalType == H264Constants.NalSliceIdr)
        {
            reader.ReadBits(1); // no_output_of_prior_pics_flag
            reader.ReadBits(1); // long_term_reference_flag
        }

        // slice_qp_delta
        H264ExpGolomb.ReadSe(ref reader);

        if (pps.DeblockingFilterControlPresentFlag)
        {
            var deblockIdc = H264ExpGolomb.ReadUe(ref reader);

            if (deblockIdc != 1)
            {
                H264ExpGolomb.ReadSe(ref reader); // alpha
                H264ExpGolomb.ReadSe(ref reader); // beta
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipByte(int val) => (byte)Math.Clamp(val, 0, 255);

    #endregion
}
