#pragma warning disable S109, S3776, MA0051, MA0182, IDE0010, IDE0045, IDE0047, IDE0048, CA1822, S1172, IDE0060

using System.Globalization;
using System.Runtime.CompilerServices;
using Atom.Media.Codecs.Webp.Vp8;

namespace Atom.Media;

/// <summary>
/// VP8 lossy keyframe decoder per RFC 6386.
/// Decodes VP8 bitstream chunk data into a VideoFrame (RGBA32).
/// </summary>
internal sealed class Vp8Decoder
{
    private readonly Vp8FrameContext ctx = new();

    /// <summary>
    /// Decodes a VP8 bitstream (the data after the "VP8 " chunk header).
    /// </summary>
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame) =>
        Decode(data, ref frame, diagnostics: null);

    /// <summary>
    /// Decodes a VP8 bitstream and optionally captures first-macroblock reconstruction diagnostics.
    /// </summary>
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame, Vp8DecodeDiagnostics? diagnostics)
    {
        diagnostics?.Reset();

        if (data.Length < 10)
        {
            return CodecResult.InvalidData;
        }

        // ── Frame tag (§9.1) ──
        var frameTag = data[0] | (data[1] << 8) | (data[2] << 16);
        ctx.Header.IsKeyFrame = (frameTag & 1) == 0;
        ctx.Header.Version = (frameTag >> 1) & 7;
        ctx.Header.ShowFrame = ((frameTag >> 4) & 1) != 0;
        ctx.Header.FirstPartSize = frameTag >> 5;
        ctx.IsKeyFrame = ctx.Header.IsKeyFrame;

        if (!ctx.Header.IsKeyFrame)
        {
            return CodecResult.UnsupportedFormat; // Only keyframes supported
        }

        // ── Keyframe header (§9.1) ──
        if (data[3] != Vp8Constants.SyncCode0 || data[4] != Vp8Constants.SyncCode1 || data[5] != Vp8Constants.SyncCode2)
        {
            return CodecResult.InvalidData;
        }

        var sizeInfo1 = data[6] | (data[7] << 8);
        ctx.Header.Width = sizeInfo1 & 0x3FFF;
        ctx.Header.HorizontalScale = sizeInfo1 >> 14;

        var sizeInfo2 = data[8] | (data[9] << 8);
        ctx.Header.Height = sizeInfo2 & 0x3FFF;
        ctx.Header.VerticalScale = sizeInfo2 >> 14;

        if (ctx.Header.Width == 0 || ctx.Header.Height == 0)
        {
            return CodecResult.InvalidData;
        }

        ctx.MbWidth = (ctx.Header.Width + 15) >> 4;
        ctx.MbHeight = (ctx.Header.Height + 15) >> 4;

        // Validate frame dimensions match
        if (frame.Width != ctx.Header.Width || frame.Height != ctx.Header.Height)
        {
            return CodecResult.InvalidData;
        }

        // ── First data partition (§9.2 onward) ──
        var firstPartData = data.Slice(10, ctx.Header.FirstPartSize);
        var tokenData = data[(10 + ctx.Header.FirstPartSize)..];

        var bd = new Vp8BoolDecoder(firstPartData);

        // ── Keyframe-specific header (§9.2) ──
        ctx.Header.ColorSpace = (int)bd.DecodeLiteral(1);
        ctx.Header.ClampingType = (int)bd.DecodeLiteral(1);

        // ── Segments (§9.3) ──
        ParseSegmentHeader(ref bd);

        // ── Filter header (§9.4) ──
        ParseFilterHeader(ref bd);

        // ── Partitions (§9.5) ──
        var log2Partitions = (int)bd.DecodeLiteral(2);
        ctx.Partitions.Count = 1 << log2Partitions;

        diagnostics?.SetPartitionInfo(log2Partitions);

        // Parse partition sizes for multi-partition (RFC §9.5)
        var partitionSizes = new int[ctx.Partitions.Count];
        var tokenOffset = 0;
        for (var i = 0; i < ctx.Partitions.Count - 1; i++)
        {
            if (tokenOffset + 3 > tokenData.Length)
            {
                return CodecResult.InvalidData;
            }

            partitionSizes[i] = tokenData[tokenOffset] | (tokenData[tokenOffset + 1] << 8) | (tokenData[tokenOffset + 2] << 16);
            tokenOffset += 3;
        }

        // Last partition takes remaining data
        var remainingForLastPart = tokenData.Length - tokenOffset;
        for (var i = 0; i < ctx.Partitions.Count - 1; i++)
        {
            remainingForLastPart -= partitionSizes[i];
        }

        partitionSizes[^1] = remainingForLastPart;
        ctx.Partitions.Sizes = partitionSizes;

        // ── Quantization (§9.6) ──
        ctx.BaseQp = (int)bd.DecodeLiteral(7);
        ctx.Y1DcDelta = ReadDeltaQ(ref bd);
        ctx.Y2DcDelta = ReadDeltaQ(ref bd);
        ctx.Y2AcDelta = ReadDeltaQ(ref bd);
        ctx.UvDcDelta = ReadDeltaQ(ref bd);
        ctx.UvAcDelta = ReadDeltaQ(ref bd);

        // ── Reference header (§9.7, keyframe) ──
        // Keyframes still carry refresh_entropy in the reference header.
        _ = bd.DecodeBit(128);

        diagnostics?.SetQuantizerInfo(ctx.BaseQp, ctx.Segment.Enabled ? ctx.Segment.QuantizerLevel : null);
        diagnostics?.SetFilterInfo(
            ctx.Filter.UseNormalFilter,
            ctx.Filter.Level,
            ctx.Filter.Sharpness,
            ctx.Filter.AdjustEnabled,
            ctx.Filter.RefDelta,
            ctx.Filter.ModeDelta,
            ctx.Segment.Enabled,
            ctx.Segment.FilterLevel);

        // Build dequant matrices per segment
        for (var s = 0; s < Vp8Constants.MaxMbSegments; s++)
        {
            var qp = ctx.BaseQp;
            if (ctx.Segment.Enabled)
            {
                qp = ctx.Segment.AbsoluteDelta
                    ? ctx.Segment.QuantizerLevel[s]
                    : qp + ctx.Segment.QuantizerLevel[s];
            }

            ctx.DequantMatrices[s] = Vp8Quantization.BuildDequantMatrix(
                qp, ctx.Y1DcDelta, ctx.Y2DcDelta, ctx.Y2AcDelta, ctx.UvDcDelta, ctx.UvAcDelta);
        }

        // ── Coefficient probabilities (§13.4) ──
        ctx.InitDefaultProbs();
        if (!(diagnostics?.DisableCoeffProbUpdates ?? false))
        {
            UpdateCoeffProbs(ref bd);
        }

        // ── Skip coeff (§9.11) ──
        var mbNoCoeffSkip = bd.DecodeBit(128) != 0;
        var probSkipFalse = mbNoCoeffSkip ? (int)bd.DecodeLiteral(8) : 0;

        diagnostics?.SetSkipInfo(mbNoCoeffSkip, probSkipFalse);

        // ── Decode macroblocks ──
        var mbW = ctx.MbWidth;
        var mbH = ctx.MbHeight;

        // Allocate YUV buffers (16-pixel-aligned rows)
        var yStride = mbW * 16;
        var uvStride = mbW * 8;
        var yPlane = new byte[yStride * mbH * 16];
        var uPlane = new byte[uvStride * mbH * 8];
        var vPlane = new byte[uvStride * mbH * 8];

        // Context rows for above
        var aboveBModes = new byte[mbW * 4]; // bottom row of 4x4 modes
        var aboveYNonZero = new byte[mbW * 4];
        var aboveY2NonZero = new byte[mbW];
        var aboveUNonZero = new byte[mbW * 2];
        var aboveVNonZero = new byte[mbW * 2];

        // Create token partition decoders
        var tokenDecoders = new Vp8BoolDecoder[ctx.Partitions.Count];
        var partitionStart = tokenOffset;
        for (var i = 0; i < ctx.Partitions.Count; i++)
        {
            var partSize = Math.Min(partitionSizes[i], tokenData.Length - partitionStart);
            tokenDecoders[i] = new Vp8BoolDecoder(tokenData.Slice(partitionStart, partSize));
            partitionStart += partitionSizes[i];
        }

        var coeffs = new short[16];
        var y2Coeffs = new short[16];
        var mbInfo = new Vp8Macroblock[mbW]; // above macroblock row info
        var allMacroblocks = new Vp8Macroblock[mbW * mbH];

        var tokenPartitionIndex = 0;
        for (var mbY = 0; mbY < mbH; mbY++)
        {
            ref var tokenBd = ref tokenDecoders[tokenPartitionIndex];
            var leftBModes = new byte[4]; // right column of 4x4 modes
            var leftYNonZero = new byte[4];
            byte leftY2NonZero = 0;
            var leftUNonZero = new byte[2];
            var leftVNonZero = new byte[2];

            for (var mbX = 0; mbX < mbW; mbX++)
            {
                var mbNumber = (mbY * mbW) + mbX;
                ref var mb = ref mbInfo[mbX];
                var currentMbYNonZero = new byte[16];
                var currentMbUNonZero = new byte[4];
                var currentMbVNonZero = new byte[4];
                var currentMbY2NonZero = (byte)0;
                var captureCurrentMacroblock = diagnostics is not null && mbX == diagnostics.TargetMacroblockX && mbY == diagnostics.TargetMacroblockY;

                // ── Segment (§10) ──
                if (ctx.Segment.Enabled && ctx.Segment.UpdateMap)
                {
                    mb.Segment = (byte)bd.DecodeTree(
                        [2, 4, 0, -1, -2, -3],
                        ctx.Segment.TreeProbs);
                }

                // ── Skip flag (§11.1) ──
                mb.IsSkip = mbNoCoeffSkip && bd.DecodeBit(probSkipFalse) != 0;

                // ── Y mode (§11.2 keyframe) ──
                mb.YMode = (byte)DecodeKfYMode(ref bd);
                mb.SubblockModes ??= new byte[16];

                if (mb.YMode == Vp8Constants.BPred)
                {
                    // Decode 4x4 subblock modes with context from above + left
                    for (var by = 0; by < 4; by++)
                    {
                        for (var bx = 0; bx < 4; bx++)
                        {
                            int above;
                            if (by > 0)
                            {
                                above = mb.SubblockModes[((by - 1) * 4) + bx];
                            }
                            else if (mbY == 0)
                            {
                                above = Vp8Constants.BDcPred;
                            }
                            else
                            {
                                above = aboveBModes[(mbX * 4) + bx];
                            }

                            int left;
                            if (bx > 0)
                            {
                                left = mb.SubblockModes[(by * 4) + bx - 1];
                            }
                            else if (mbX == 0)
                            {
                                left = Vp8Constants.BDcPred;
                            }
                            else
                            {
                                left = leftBModes[by];
                            }

                            mb.SubblockModes[(by * 4) + bx] = (byte)bd.DecodeTree(
                                Vp8Constants.BModeTree,
                                Vp8Constants.KfBModeProbs.AsSpan3D(above, left));

                            var captureCurrentSubblock = captureCurrentMacroblock
                                && by == diagnostics!.TargetSubblockY
                                && bx == diagnostics.TargetSubblockX;

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstBPredSubblockMode = mb.SubblockModes[0];
                                diagnostics.FirstBPredSubblockAboveMode = (byte)above;
                                diagnostics.FirstBPredSubblockLeftMode = (byte)left;
                            }

                            if (captureCurrentSubblock)
                            {
                                diagnostics!.TargetYSubblockMode = mb.SubblockModes[(by * 4) + bx];
                                diagnostics.TargetYSubblockAboveMode = (byte)above;
                                diagnostics.TargetYSubblockLeftMode = (byte)left;
                            }
                        }
                    }

                    if (captureCurrentMacroblock)
                    {
                        Array.Copy(mb.SubblockModes, diagnostics!.TargetMacroblockSubblockModes, 16);
                    }

                }

                mb.UvMode = (byte)DecodeKfUvMode(ref bd);

                if (captureCurrentMacroblock)
                {
                    diagnostics!.TargetMacroblockSegment = mb.Segment;
                    diagnostics!.FirstMacroblockYMode = mb.YMode;
                    diagnostics.FirstMacroblockUvMode = mb.UvMode;
                    diagnostics.FirstMacroblockIsSkip = mb.IsSkip;
                }

                // ── Reconstruct: Prediction -> Coefficients -> IDCT (adds residuals to prediction) ──
                var yOffset = (mbY * 16 * yStride) + (mbX * 16);
                var uvOffset = (mbY * 8 * uvStride) + (mbX * 8);
                ref var dqm = ref ctx.DequantMatrices[mb.Segment];
                var hasAbove = mbY > 0;
                var hasLeft = mbX > 0;

                if (captureCurrentMacroblock)
                {
                    diagnostics!.TargetY1DcDequant = dqm.Y1DcDequant;
                    diagnostics.TargetY1AcDequant = dqm.Y1AcDequant;
                    diagnostics.TargetY2DcDequant = dqm.Y2DcDequant;
                    diagnostics.TargetY2AcDequant = dqm.Y2AcDequant;
                    diagnostics.TargetUvDcDequant = dqm.UvDcDequant;
                    diagnostics.TargetUvAcDequant = dqm.UvAcDequant;
                }

                if (mb.YMode == Vp8Constants.BPred)
                {
                    // B_PRED: per-4x4 interleaved predict->decode->IDCT
                    DecodeBPredBlocks(ref tokenBd, yPlane, yStride, yOffset, hasAbove, hasLeft, ref mb, ref dqm,
                        coeffs, aboveYNonZero, leftYNonZero, currentMbYNonZero, mbX, diagnostics, captureCurrentMacroblock);
                }
                else
                {
                    if (captureCurrentMacroblock)
                    {
                        for (var i = 0; i < 16; i++)
                        {
                            diagnostics!.FirstMacroblockPredictionAbove16[i] = hasAbove
                                ? yPlane[yOffset - yStride + i]
                                : (byte)127;
                            diagnostics.FirstMacroblockPredictionLeft16[i] = hasLeft
                                ? yPlane[yOffset + (i * yStride) - 1]
                                : (byte)129;
                        }

                        diagnostics!.FirstMacroblockPredictionAboveLeft = (hasAbove && hasLeft)
                            ? yPlane[yOffset - yStride - 1]
                            : GetOutOfFrameAboveLeft(hasAbove, hasLeft);
                    }

                    // 16x16 prediction first
                    Apply16x16Prediction(yPlane, yStride, yOffset, hasAbove, hasLeft, mb.YMode);

                    if (captureCurrentMacroblock)
                    {
                        diagnostics!.FirstMacroblockPredictedYTopLeft = yPlane[yOffset];
                    }

                    if (!mb.IsSkip)
                    {
                        // Decode Y2 (DC of 16 Y subblocks)
                        Array.Clear(y2Coeffs);
                        Array.Clear(coeffs);
                        var y2InitialContext = aboveY2NonZero[mbX] + leftY2NonZero;
                        System.Collections.Generic.List<string>? y2TokenTrace = null;
                        if (captureCurrentMacroblock)
                        {
                            y2TokenTrace = diagnostics!.FirstMacroblockY2TokenTrace;
                        }

                        currentMbY2NonZero = DecodeBlock(ref tokenBd, coeffs, 1, 0, 0, y2InitialContext, y2TokenTrace) ? (byte)1 : (byte)0;

                        if (captureCurrentMacroblock)
                        {
                            diagnostics!.FirstMacroblockY2NonZero = currentMbY2NonZero != 0;
                            diagnostics.FirstMacroblockY2RawDc = coeffs[0];
                            Array.Copy(coeffs, diagnostics.FirstMacroblockY2RawCoeffs, 16);
                        }

                        Vp8Quantization.Dequantize(coeffs, dqm.Y2DcDequant, dqm.Y2AcDequant);

                        if (captureCurrentMacroblock)
                        {
                            diagnostics!.FirstMacroblockY2DequantDc = coeffs[0];
                            Array.Copy(coeffs, diagnostics.FirstMacroblockY2DequantCoeffs, 16);
                        }

                        Vp8Dct.InverseWht4x4(coeffs, y2Coeffs);

                        if (captureCurrentMacroblock)
                        {
                            diagnostics!.FirstMacroblockY2WhtDc = y2Coeffs[0];
                            Array.Copy(y2Coeffs, diagnostics.FirstMacroblockY2WhtCoeffs, 16);
                        }

                        // 16 Y subblocks: decode, dequant, inject Y2 DC, IDCT adds to prediction
                        for (var by = 0; by < 4; by++)
                        {
                            for (var bx = 0; bx < 4; bx++)
                            {
                                Array.Clear(coeffs);
                                var aboveContext = by > 0
                                    ? currentMbYNonZero[((by - 1) * 4) + bx]
                                    : aboveYNonZero[(mbX * 4) + bx];
                                var leftContext = bx > 0
                                    ? currentMbYNonZero[(by * 4) + bx - 1]
                                    : leftYNonZero[by];
                                System.Collections.Generic.List<string>? yBlockTokenTrace = null;
                                var captureCurrentSubblock = captureCurrentMacroblock
                                    && by == diagnostics!.TargetSubblockY
                                    && bx == diagnostics.TargetSubblockX;

                                if (captureCurrentMacroblock && by == 0 && bx == 0)
                                {
                                    yBlockTokenTrace = diagnostics!.FirstYBlockTokenTrace;
                                    diagnostics.FirstYBlockAboveNonZeroContext = aboveContext;
                                    diagnostics.FirstYBlockLeftNonZeroContext = leftContext;
                                    diagnostics.FirstYBlockInitialContext = aboveContext + leftContext;
                                }

                                if (captureCurrentSubblock && !(by == 0 && bx == 0))
                                {
                                    yBlockTokenTrace = diagnostics!.TargetYSubblockTokenTrace;
                                    diagnostics.TargetYSubblockAboveNonZeroContext = aboveContext;
                                    diagnostics.TargetYSubblockLeftNonZeroContext = leftContext;
                                    diagnostics.TargetYSubblockInitialContext = aboveContext + leftContext;
                                    diagnostics.TargetYSubblockTokenDecoderStateBefore = tokenBd.DebugState;
                                }

                                currentMbYNonZero[(by * 4) + bx] = DecodeBlock(ref tokenBd, coeffs, 0, (by * 4) + bx, 1, aboveContext + leftContext, yBlockTokenTrace)
                                    ? (byte)1
                                    : (byte)0;

                                if (captureCurrentSubblock && !(by == 0 && bx == 0))
                                {
                                    diagnostics!.TargetYSubblockTokenDecoderStateAfter = tokenBd.DebugState;
                                }

                                if (captureCurrentMacroblock && by == 0 && bx == 0)
                                {
                                    diagnostics!.FirstYBlockNonZero = currentMbYNonZero[0] != 0;
                                    diagnostics.FirstYBlockRawDc = coeffs[0];
                                }

                                if (captureCurrentSubblock)
                                {
                                    diagnostics!.TargetYSubblockNonZero = currentMbYNonZero[(by * 4) + bx] != 0;
                                    diagnostics.TargetYSubblockRawDc = coeffs[0];
                                    Array.Copy(coeffs, diagnostics.TargetYSubblockRawCoeffs, 16);

                                    if (by == 0 && bx == 0)
                                    {
                                        diagnostics.TargetYSubblockTokenTrace.Clear();
                                        diagnostics.TargetYSubblockTokenTrace.AddRange(diagnostics.FirstYBlockTokenTrace);
                                        diagnostics.TargetYSubblockAboveNonZeroContext = diagnostics.FirstYBlockAboveNonZeroContext;
                                        diagnostics.TargetYSubblockLeftNonZeroContext = diagnostics.FirstYBlockLeftNonZeroContext;
                                        diagnostics.TargetYSubblockInitialContext = diagnostics.FirstYBlockInitialContext;
                                        diagnostics.TargetYSubblockTokenDecoderStateBefore = string.Empty;
                                        diagnostics.TargetYSubblockTokenDecoderStateAfter = string.Empty;
                                    }
                                }

                                Vp8Quantization.Dequantize(coeffs, dqm.Y1DcDequant, dqm.Y1AcDequant);

                                if (captureCurrentMacroblock && by == 0 && bx == 0)
                                {
                                    diagnostics!.FirstYBlockDequantDcBeforeY2 = coeffs[0];
                                }

                                if (captureCurrentSubblock)
                                {
                                    diagnostics!.TargetYSubblockDequantDcBeforeY2 = coeffs[0];
                                    Array.Copy(coeffs, diagnostics.TargetYSubblockDequantCoeffs, 16);
                                }

                                var subOff = yOffset + (by * 4 * yStride) + (bx * 4);

                                if (captureCurrentSubblock)
                                {
                                    diagnostics!.TargetYSubblockPredictedTopLeft = yPlane[subOff];
                                    Copy4x4Block(yPlane, yStride, subOff, diagnostics.TargetYSubblockPredicted4x4);
                                }

                                coeffs[0] = y2Coeffs[(by * 4) + bx];

                                if (captureCurrentMacroblock && by == 0 && bx == 0)
                                {
                                    diagnostics!.FirstYBlockDcAfterY2Injection = coeffs[0];
                                }

                                if (captureCurrentSubblock)
                                {
                                    diagnostics!.TargetYSubblockDcAfterY2Injection = coeffs[0];
                                }

                                Vp8Dct.InverseDct4x4(coeffs, yPlane.AsSpan(subOff), yStride);

                                if (captureCurrentMacroblock && by == 0 && bx == 0)
                                {
                                    diagnostics!.FirstYBlockOutputTopLeft = yPlane[subOff];
                                }

                                if (captureCurrentSubblock)
                                {
                                    diagnostics!.TargetYSubblockPredictedTopLeft = diagnostics.TargetYSubblockPredictedTopLeft == 0
                                        ? yPlane[subOff]
                                        : diagnostics.TargetYSubblockPredictedTopLeft;
                                    diagnostics.TargetYSubblockOutputTopLeft = yPlane[subOff];
                                    Copy4x4Block(yPlane, yStride, subOff, diagnostics.TargetYSubblockOutput4x4);
                                }
                            }
                        }
                    }
                }

                // UV prediction first, then decode + IDCT adds residuals
                ApplyUvPrediction(uPlane, vPlane, uvStride, uvOffset, hasAbove, hasLeft, mb.UvMode);

                if (captureCurrentMacroblock)
                {
                    diagnostics!.FirstMacroblockPredictedUTopLeft = uPlane[uvOffset];
                    diagnostics.FirstMacroblockPredictedVTopLeft = vPlane[uvOffset];
                }

                if (!mb.IsSkip)
                {
                    // 4 U subblocks
                    for (var by = 0; by < 2; by++)
                    {
                        for (var bx = 0; bx < 2; bx++)
                        {
                            Array.Clear(coeffs);
                            var captureTargetUvSubblock = captureCurrentMacroblock
                                && by == diagnostics!.TargetSubblockY / 2
                                && bx == diagnostics.TargetSubblockX / 2;
                            var aboveContext = by > 0
                                ? currentMbUNonZero[((by - 1) * 2) + bx]
                                : aboveUNonZero[(mbX * 2) + bx];
                            var leftContext = bx > 0
                                ? currentMbUNonZero[(by * 2) + bx - 1]
                                : leftUNonZero[by];
                            currentMbUNonZero[(by * 2) + bx] = DecodeBlock(ref tokenBd, coeffs, 2, (by * 2) + bx, 0, aboveContext + leftContext)
                                ? (byte)1
                                : (byte)0;

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstUBlockNonZero = currentMbUNonZero[0] != 0;
                                diagnostics.FirstUBlockRawDc = coeffs[0];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetUvSubblockX = bx;
                                diagnostics.TargetUvSubblockY = by;
                                diagnostics.TargetUBlockNonZero = currentMbUNonZero[(by * 2) + bx] != 0;
                                diagnostics.TargetUBlockRawDc = coeffs[0];
                                Array.Copy(coeffs, diagnostics.TargetUBlockRawCoeffs, 16);
                            }

                            Vp8Quantization.Dequantize(coeffs, dqm.UvDcDequant, dqm.UvAcDequant);

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstUBlockDequantDc = coeffs[0];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetUBlockDequantDc = coeffs[0];
                                Array.Copy(coeffs, diagnostics.TargetUBlockDequantCoeffs, 16);
                            }

                            var subOff = uvOffset + (by * 4 * uvStride) + (bx * 4);

                            if (captureTargetUvSubblock)
                            {
                                CopyBlock4x4(uPlane, uvStride, subOff, diagnostics!.TargetUBlockPredicted4x4);
                            }

                            Vp8Dct.InverseDct4x4(coeffs, uPlane.AsSpan(subOff), uvStride);

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstUBlockOutputTopLeft = uPlane[subOff];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetUBlockOutputTopLeft = uPlane[subOff];
                                CopyBlock4x4(uPlane, uvStride, subOff, diagnostics.TargetUBlockOutput4x4);
                            }
                        }
                    }

                    // 4 V subblocks
                    for (var by = 0; by < 2; by++)
                    {
                        for (var bx = 0; bx < 2; bx++)
                        {
                            Array.Clear(coeffs);
                            var captureTargetUvSubblock = captureCurrentMacroblock
                                && by == diagnostics!.TargetSubblockY / 2
                                && bx == diagnostics.TargetSubblockX / 2;
                            var aboveContext = by > 0
                                ? currentMbVNonZero[((by - 1) * 2) + bx]
                                : aboveVNonZero[(mbX * 2) + bx];
                            var leftContext = bx > 0
                                ? currentMbVNonZero[(by * 2) + bx - 1]
                                : leftVNonZero[by];
                            currentMbVNonZero[(by * 2) + bx] = DecodeBlock(ref tokenBd, coeffs, 2, (by * 2) + bx, 0, aboveContext + leftContext)
                                ? (byte)1
                                : (byte)0;

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstVBlockNonZero = currentMbVNonZero[0] != 0;
                                diagnostics.FirstVBlockRawDc = coeffs[0];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetVBlockNonZero = currentMbVNonZero[(by * 2) + bx] != 0;
                                diagnostics.TargetVBlockRawDc = coeffs[0];
                                Array.Copy(coeffs, diagnostics.TargetVBlockRawCoeffs, 16);
                            }

                            Vp8Quantization.Dequantize(coeffs, dqm.UvDcDequant, dqm.UvAcDequant);

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstVBlockDequantDc = coeffs[0];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetVBlockDequantDc = coeffs[0];
                                Array.Copy(coeffs, diagnostics.TargetVBlockDequantCoeffs, 16);
                            }

                            var subOff = uvOffset + (by * 4 * uvStride) + (bx * 4);

                            if (captureTargetUvSubblock)
                            {
                                CopyBlock4x4(vPlane, uvStride, subOff, diagnostics!.TargetVBlockPredicted4x4);
                            }

                            Vp8Dct.InverseDct4x4(coeffs, vPlane.AsSpan(subOff), uvStride);

                            if (captureCurrentMacroblock && by == 0 && bx == 0)
                            {
                                diagnostics!.FirstVBlockOutputTopLeft = vPlane[subOff];
                            }

                            if (captureTargetUvSubblock)
                            {
                                diagnostics!.TargetVBlockOutputTopLeft = vPlane[subOff];
                                CopyBlock4x4(vPlane, uvStride, subOff, diagnostics.TargetVBlockOutput4x4);
                            }
                        }
                    }
                }

                for (var i = 0; i < 4; i++)
                {
                    aboveYNonZero[(mbX * 4) + i] = currentMbYNonZero[12 + i];
                    leftYNonZero[i] = currentMbYNonZero[(i * 4) + 3];
                }

                if (mb.YMode != Vp8Constants.BPred)
                {
                    aboveY2NonZero[mbX] = currentMbY2NonZero;
                    leftY2NonZero = currentMbY2NonZero;
                }

                for (var i = 0; i < 2; i++)
                {
                    aboveUNonZero[(mbX * 2) + i] = currentMbUNonZero[2 + i];
                    leftUNonZero[i] = currentMbUNonZero[(i * 2) + 1];
                    aboveVNonZero[(mbX * 2) + i] = currentMbVNonZero[2 + i];
                    leftVNonZero[i] = currentMbVNonZero[(i * 2) + 1];
                }

                for (var i = 0; i < 4; i++)
                {
                    aboveBModes[(mbX * 4) + i] = mb.YMode == Vp8Constants.BPred
                        ? mb.SubblockModes[12 + i]
                        : (byte)MapMacroblockModeToSubblockContext(mb.YMode);
                    leftBModes[i] = mb.YMode == Vp8Constants.BPred
                        ? mb.SubblockModes[(i * 4) + 3]
                        : (byte)MapMacroblockModeToSubblockContext(mb.YMode);
                }

                mb.HasFilterSubblocks = currentMbY2NonZero != 0
                    || Array.Exists(currentMbYNonZero, value => value != 0)
                    || Array.Exists(currentMbUNonZero, value => value != 0)
                    || Array.Exists(currentMbVNonZero, value => value != 0)
                    || mb.YMode == Vp8Constants.BPred;
                mb.FilterLevel = GetMacroblockFilterLevel(mb);
                allMacroblocks[mbNumber] = mb;
            }

            tokenPartitionIndex++;
            if (tokenPartitionIndex == ctx.Partitions.Count)
            {
                tokenPartitionIndex = 0;
            }
        }

        // ── Loop filter ──
        if (ctx.Filter.Level > 0)
        {
            ApplyLoopFilter(yPlane, uPlane, vPlane, yStride, uvStride, allMacroblocks);
        }

        if (diagnostics is not null)
        {
            var targetYOffset = (diagnostics.TargetMacroblockY * 16 * yStride) + (diagnostics.TargetMacroblockX * 16);
            var targetUvOffset = (diagnostics.TargetMacroblockY * 8 * uvStride) + (diagnostics.TargetMacroblockX * 8);
            diagnostics.FirstMacroblockFinalYTopLeft = yPlane[targetYOffset];
            diagnostics.FirstMacroblockFinalUTopLeft = uPlane[targetUvOffset];
            diagnostics.FirstMacroblockFinalVTopLeft = vPlane[targetUvOffset];

            var targetBlockYOffset = targetYOffset + (diagnostics.TargetSubblockY * 4 * yStride) + (diagnostics.TargetSubblockX * 4);
            CopyBlock4x4(yPlane, yStride, targetBlockYOffset, diagnostics.TargetFinalY4x4);

            var targetBlockUvOffset = targetUvOffset + ((diagnostics.TargetSubblockY / 2) * 4 * uvStride) + ((diagnostics.TargetSubblockX / 2) * 4);
            CopyBlock2x2(uPlane, uvStride, targetBlockUvOffset, diagnostics.TargetFinalU2x2);
            CopyBlock2x2(vPlane, uvStride, targetBlockUvOffset, diagnostics.TargetFinalV2x2);
        }

        // ── YUV -> RGBA conversion ──
        ConvertYuvToRgba(yPlane, uPlane, vPlane, yStride, uvStride, ref frame);

        if (diagnostics is not null)
        {
            var targetX = diagnostics.TargetMacroblockX * 16;
            var targetY = diagnostics.TargetMacroblockY * 16;
            var row = frame.PackedData.GetRow(targetY);
            var offset = targetX * 4;
            diagnostics.FirstMacroblockFinalRgbaTopLeft = $"{row[offset]},{row[offset + 1]},{row[offset + 2]},{row[offset + 3]}";
        }

        return CodecResult.Success;
    }

    internal bool DecodeBlockForDiagnostics(ReadOnlySpan<byte> data, short[] coeffs, int blockType, int firstCoeff, int initialContext)
    {
        ctx.InitDefaultProbs();
        var bd = new Vp8BoolDecoder(data);
        Array.Clear(coeffs);
        return DecodeBlock(ref bd, coeffs, blockType, 0, firstCoeff, initialContext);
    }

    // ── Header parsing helpers ──

    private void ParseSegmentHeader(ref Vp8BoolDecoder bd)
    {
        ctx.Segment.Enabled = bd.DecodeBit(128) != 0;
        if (!ctx.Segment.Enabled)
        {
            return;
        }

        ctx.Segment.UpdateMap = bd.DecodeBit(128) != 0;
        ctx.Segment.UpdateData = bd.DecodeBit(128) != 0;

        if (ctx.Segment.UpdateData)
        {
            ctx.Segment.AbsoluteDelta = bd.DecodeBit(128) != 0;
            ctx.Segment.QuantizerLevel = new int[Vp8Constants.MaxMbSegments];
            ctx.Segment.FilterLevel = new int[Vp8Constants.MaxMbSegments];

            for (var i = 0; i < Vp8Constants.MaxMbSegments; i++)
            {
                ctx.Segment.QuantizerLevel[i] = bd.DecodeBit(128) != 0 ? bd.DecodeSigned(7) : 0;
            }

            for (var i = 0; i < Vp8Constants.MaxMbSegments; i++)
            {
                ctx.Segment.FilterLevel[i] = bd.DecodeBit(128) != 0 ? bd.DecodeSigned(6) : 0;
            }
        }

        if (ctx.Segment.UpdateMap)
        {
            ctx.Segment.TreeProbs = new byte[3];
            for (var i = 0; i < 3; i++)
            {
                ctx.Segment.TreeProbs[i] = bd.DecodeBit(128) != 0 ? (byte)bd.DecodeLiteral(8) : (byte)255;
            }
        }
    }

    private void ParseFilterHeader(ref Vp8BoolDecoder bd)
    {
        ctx.Filter.UseNormalFilter = bd.DecodeBit(128) == 0;
        ctx.Filter.Level = (int)bd.DecodeLiteral(6);
        ctx.Filter.Sharpness = (int)bd.DecodeLiteral(3);

        ctx.Filter.AdjustEnabled = bd.DecodeBit(128) != 0;
        if (ctx.Filter.AdjustEnabled)
        {
            ctx.Filter.DeltaUpdate = bd.DecodeBit(128) != 0;
            if (ctx.Filter.DeltaUpdate)
            {
                ctx.Filter.RefDelta = new int[4];
                for (var i = 0; i < 4; i++)
                {
                    if (bd.DecodeBit(128) != 0)
                    {
                        ctx.Filter.RefDelta[i] = bd.DecodeSigned(6);
                    }
                }

                ctx.Filter.ModeDelta = new int[4];
                for (var i = 0; i < 4; i++)
                {
                    if (bd.DecodeBit(128) != 0)
                    {
                        ctx.Filter.ModeDelta[i] = bd.DecodeSigned(6);
                    }
                }
            }
        }
    }

    private static int ReadDeltaQ(ref Vp8BoolDecoder bd)
    {
        if (bd.DecodeBit(128) != 0)
        {
            return bd.DecodeSigned(4);
        }

        return 0;
    }

    private void UpdateCoeffProbs(ref Vp8BoolDecoder bd)
    {
        for (var t = 0; t < Vp8Constants.BlockTypes; t++)
        {
            for (var b = 0; b < Vp8Constants.CoeffBands; b++)
            {
                for (var c = 0; c < Vp8Constants.PrevCoeffContexts; c++)
                {
                    for (var n = 0; n < Vp8Constants.EntropyNodes; n++)
                    {
                        if (bd.DecodeBit(Vp8Constants.CoeffUpdateProbs[t, b, c, n]) != 0)
                        {
                            ctx.CoeffProbs[t, b, c, n] = (byte)bd.DecodeLiteral(8);
                        }
                    }
                }
            }
        }
    }

    // ── Mode decoding (keyframe context-based) ──

    private static int DecodeKfYMode(ref Vp8BoolDecoder bd) =>
        // Keyframe Y mode probs are fixed, but the keyframe tree differs from the generic Y tree.
        bd.DecodeTree(Vp8Constants.KfYModeTree, [145, 156, 163, 128]);

    private static int DecodeKfUvMode(ref Vp8BoolDecoder bd) =>
        // Keyframe UV mode probs: P(DC)=142, P(V)=114, P(H)=183
        bd.DecodeTree(Vp8Constants.UvModeTree, [142, 114, 183]);

    // ── Coefficient decoding (§13) ──

    private bool DecodeBlock(ref Vp8BoolDecoder bd, short[] coeffs, int blockType,
        int blockIndex, int firstCoeff, int initialContext, System.Collections.Generic.List<string>? tokenTrace = null)
    {
        var ctx2 = initialContext;
        var hasNonZeroCoefficients = false;
        var prevCoeffWasZero = false;
        for (var i = firstCoeff; i < 16; i++)
        {
            var band = Vp8Constants.CoeffBandIndex[i];
            var probs = GetCoeffProbs(blockType, band, ctx2);

            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",band=" + band.ToString(CultureInfo.InvariantCulture) + ",ctx=" + ctx2.ToString(CultureInfo.InvariantCulture) + ",probs=" + string.Join(',', probs.ToArray()));

            string? branchTrace = null;
            var token = tokenTrace is null
                ? DecodeCoeffToken(ref bd, probs, prevCoeffWasZero)
                : DecodeCoeffToken(ref bd, probs, prevCoeffWasZero, out branchTrace);
            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",band=" + band.ToString(CultureInfo.InvariantCulture) + ",ctx=" + ctx2.ToString(CultureInfo.InvariantCulture) + ",token=" + token.ToString(CultureInfo.InvariantCulture));
            if (branchTrace is not null)
            {
                tokenTrace!.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",branches=" + branchTrace);
            }

            if (token == 0)
            {
                break;
            }

            if (token == 1)
            {
                ctx2 = 0;
                prevCoeffWasZero = true;
                continue;
            }

            int value;
            if (token <= 5)
            {
                value = token - 1;
            }
            else
            {
                value = DecodeExtraValue(ref bd, token);
            }

            if (bd.DecodeBit(128) != 0)
            {
                value = -value;
            }

            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",value=" + value.ToString(CultureInfo.InvariantCulture));

            var zigzagIndex = Vp8Constants.Zigzag[i];
            coeffs[zigzagIndex] = (short)value;
            hasNonZeroCoefficients = true;

            if (value == 0)
            {
                ctx2 = 0;
                prevCoeffWasZero = true;
            }
            else
            {
                ctx2 = Math.Abs(value) == 1 ? 1 : 2;
                prevCoeffWasZero = false;
            }
        }

        return hasNonZeroCoefficients;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetCoeffProbs(int blockType, int band, int ctx2)
    {
        // Return the 11-probability span for this context
        return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref ctx.CoeffProbs[blockType, band, ctx2, 0],
            Vp8Constants.EntropyNodes);
    }

    private static int DecodeCoeffToken(ref Vp8BoolDecoder bd, ReadOnlySpan<byte> probs, bool prevCoeffWasZero)
    {
        if (prevCoeffWasZero)
        {
            return DecodeCoeffTokenAfterEob(ref bd, probs);
        }

        if (bd.DecodeBit(probs[0]) == 0) { return 0; }
        return DecodeCoeffTokenAfterEob(ref bd, probs);
    }

    private static int DecodeCoeffToken(ref Vp8BoolDecoder bd, ReadOnlySpan<byte> probs, bool prevCoeffWasZero, out string branchTrace)
    {
        var branches = new System.Text.StringBuilder();

        if (prevCoeffWasZero)
        {
            branches.Append("skip_eob=1");
            var tokenAfterZero = DecodeCoeffTokenAfterEob(ref bd, probs, branches);
            branchTrace = branches.ToString();
            return tokenAfterZero;
        }

        var bit = bd.DecodeBit(probs[0]);
        branches.Append("p0=").Append(bit);
        if (bit == 0)
        {
            branchTrace = branches.ToString();
            return 0;
        }

        var token = DecodeCoeffTokenAfterEob(ref bd, probs, branches);
        branchTrace = branches.ToString();
        return token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeCoeffTokenAfterEob(ref Vp8BoolDecoder bd, ReadOnlySpan<byte> probs)
    {
        if (bd.DecodeBit(probs[1]) == 0) { return 1; }
        if (bd.DecodeBit(probs[2]) == 0) { return 2; }

        if (bd.DecodeBit(probs[3]) == 0)
        {
            if (bd.DecodeBit(probs[4]) == 0) { return 3; }
            if (bd.DecodeBit(probs[5]) == 0) { return 4; }
            return 5;
        }

        if (bd.DecodeBit(probs[6]) == 0)
        {
            if (bd.DecodeBit(probs[7]) == 0) { return 6; }
            return 7;
        }

        if (bd.DecodeBit(probs[8]) == 0)
        {
            if (bd.DecodeBit(probs[9]) == 0) { return 8; }
            return 9;
        }

        if (bd.DecodeBit(probs[10]) == 0) { return 10; }
        return 11;
    }

    private static int DecodeCoeffTokenAfterEob(ref Vp8BoolDecoder bd, ReadOnlySpan<byte> probs, System.Text.StringBuilder branches)
    {
        var bit = bd.DecodeBit(probs[1]);
        branches.Append(",p1=").Append(bit);
        if (bit == 0) { return 1; }

        bit = bd.DecodeBit(probs[2]);
        branches.Append(",p2=").Append(bit);
        if (bit == 0) { return 2; }

        bit = bd.DecodeBit(probs[3]);
        branches.Append(",p3=").Append(bit);
        if (bit == 0)
        {
            bit = bd.DecodeBit(probs[4]);
            branches.Append(",p4=").Append(bit);
            if (bit == 0) { return 3; }

            bit = bd.DecodeBit(probs[5]);
            branches.Append(",p5=").Append(bit);
            return bit == 0 ? 4 : 5;
        }

        bit = bd.DecodeBit(probs[6]);
        branches.Append(",p6=").Append(bit);
        if (bit == 0)
        {
            bit = bd.DecodeBit(probs[7]);
            branches.Append(",p7=").Append(bit);
            return bit == 0 ? 6 : 7;
        }

        bit = bd.DecodeBit(probs[8]);
        branches.Append(",p8=").Append(bit);
        if (bit == 0)
        {
            bit = bd.DecodeBit(probs[9]);
            branches.Append(",p9=").Append(bit);
            return bit == 0 ? 8 : 9;
        }

        bit = bd.DecodeBit(probs[10]);
        branches.Append(",p10=").Append(bit);
        return bit == 0 ? 10 : 11;
    }

    private static int DecodeExtraValue(ref Vp8BoolDecoder bd, int token)
    {
        var cat = token - 6;
        var baseValue = Vp8Constants.CategoryBase[cat];

        var probs = cat switch
        {
            0 => Vp8Constants.ProbsCat1,
            1 => Vp8Constants.ProbsCat2,
            2 => Vp8Constants.ProbsCat3,
            3 => Vp8Constants.ProbsCat4,
            4 => Vp8Constants.ProbsCat5,
            _ => Vp8Constants.ProbsCat6,
        };

        var extra = 0;
        for (var i = 0; i < probs.Length; i++)
        {
            extra = (extra << 1) | bd.DecodeBit(probs[i]);
        }

        return baseValue + extra;
    }

    private bool DecodeBlockReferenceStyle(ref Vp8BoolDecoder bd, short[] coeffs, int blockType,
        int firstCoeff, int initialContext, System.Collections.Generic.List<string>? tokenTrace = null)
    {
        var context = initialContext;
        var prevCoeffWasZero = false;
        var hasNonZeroCoefficients = false;

        for (var i = firstCoeff; i < 16; i++)
        {
            var band = Vp8Constants.CoeffBandIndex[i];
            var probs = GetCoeffProbs(blockType, band, context);
            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",band=" + band.ToString(CultureInfo.InvariantCulture) + ",ctx=" + context.ToString(CultureInfo.InvariantCulture) + ",probs=" + string.Join(',', probs.ToArray()));

            if (!prevCoeffWasZero)
            {
                var eobBit = bd.DecodeBit(probs[0]);
                tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p0=" + eobBit.ToString(CultureInfo.InvariantCulture));
                if (eobBit == 0)
                {
                    tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",token=0");
                    break;
                }
            }
            else
            {
                tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",skip_eob=1");
            }

            var zeroBit = bd.DecodeBit(probs[1]);
            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p1=" + zeroBit.ToString(CultureInfo.InvariantCulture));
            if (zeroBit == 0)
            {
                tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",token=1");
                context = 0;
                prevCoeffWasZero = true;
                continue;
            }

            int token;
            int value;

            var oneBit = bd.DecodeBit(probs[2]);
            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p2=" + oneBit.ToString(CultureInfo.InvariantCulture));
            if (oneBit == 0)
            {
                token = 2;
                value = 1;
            }
            else
            {
                var lowValBit = bd.DecodeBit(probs[3]);
                tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p3=" + lowValBit.ToString(CultureInfo.InvariantCulture));
                if (lowValBit == 0)
                {
                    var twoBit = bd.DecodeBit(probs[4]);
                    tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p4=" + twoBit.ToString(CultureInfo.InvariantCulture));
                    if (twoBit == 0)
                    {
                        token = 3;
                        value = 2;
                    }
                    else
                    {
                        var threeBit = bd.DecodeBit(probs[5]);
                        tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p5=" + threeBit.ToString(CultureInfo.InvariantCulture));
                        token = threeBit == 0 ? 4 : 5;
                        value = token - 1;
                    }
                }
                else
                {
                    var highLowBit = bd.DecodeBit(probs[6]);
                    tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p6=" + highLowBit.ToString(CultureInfo.InvariantCulture));
                    if (highLowBit == 0)
                    {
                        var catOneBit = bd.DecodeBit(probs[7]);
                        tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p7=" + catOneBit.ToString(CultureInfo.InvariantCulture));
                        token = catOneBit == 0 ? 6 : 7;
                    }
                    else
                    {
                        var catThreeFourBit = bd.DecodeBit(probs[8]);
                        tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p8=" + catThreeFourBit.ToString(CultureInfo.InvariantCulture));
                        if (catThreeFourBit == 0)
                        {
                            var catThreeBit = bd.DecodeBit(probs[9]);
                            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p9=" + catThreeBit.ToString(CultureInfo.InvariantCulture));
                            token = catThreeBit == 0 ? 8 : 9;
                        }
                        else
                        {
                            var catFiveBit = bd.DecodeBit(probs[10]);
                            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",p10=" + catFiveBit.ToString(CultureInfo.InvariantCulture));
                            token = catFiveBit == 0 ? 10 : 11;
                        }
                    }

                    value = DecodeExtraValue(ref bd, token);
                }
            }

            if (bd.DecodeBit(128) != 0)
            {
                value = -value;
            }

            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",token=" + token.ToString(CultureInfo.InvariantCulture));
            tokenTrace?.Add("i=" + i.ToString(CultureInfo.InvariantCulture) + ",value=" + value.ToString(CultureInfo.InvariantCulture));

            coeffs[Vp8Constants.Zigzag[i]] = (short)value;
            hasNonZeroCoefficients = true;
            context = Math.Abs(value) == 1 ? 1 : 2;
            prevCoeffWasZero = false;
        }

        return hasNonZeroCoefficients;
    }

    // ── B_PRED block decoding (extracted to avoid stackalloc-in-loop) ──

    private void DecodeBPredBlocks(ref Vp8BoolDecoder tokenBd, byte[] yPlane, int yStride,
        int yOffset, bool hasAbove, bool hasLeft, ref Vp8Macroblock mb, ref Vp8QuantMatrix dqm,
        short[] coeffs, byte[] aboveYNonZero, byte[] leftYNonZero, byte[] currentMbYNonZero, int mbX,
        Vp8DecodeDiagnostics? diagnostics, bool captureCurrentMacroblock)
    {
        // Pre-allocate context buffers outside the loop (CA2014)
        var above4 = new byte[8];
        var left4 = new byte[4];

        for (var by = 0; by < 4; by++)
        {
            for (var bx = 0; bx < 4; bx++)
            {
                var subOff = yOffset + (by * 4 * yStride) + (bx * 4);
                var mode = mb.SubblockModes[(by * 4) + bx];

                // Gather context from reconstructed neighbors
                GatherBPredAbovePredictors(yPlane, yStride, yOffset, subOff, hasAbove, bx, by, above4);

                left4.AsSpan().Fill(129);
                if (bx > 0 || hasLeft)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        left4[i] = yPlane[subOff + (i * yStride) - 1];
                    }
                }

                var al = (by > 0 || hasAbove) && (bx > 0 || hasLeft)
                    ? yPlane[subOff - yStride - 1]
                    : GetOutOfFrameAboveLeft(by > 0 || hasAbove, bx > 0 || hasLeft);
                var subHasAbove = by > 0 || hasAbove;
                var subHasLeft = bx > 0 || hasLeft;

                // 1. Prediction fills the 4x4 block
                Vp8Prediction.Predict4x4(mode, above4, left4, al, subHasAbove, subHasLeft,
                    yPlane.AsSpan(subOff), yStride);

                var captureCurrentSubblock = captureCurrentMacroblock
                    && by == diagnostics!.TargetSubblockY
                    && bx == diagnostics.TargetSubblockX;

                if (captureCurrentMacroblock && by == 0 && bx == 0)
                {
                    diagnostics!.FirstMacroblockPredictedYTopLeft = yPlane[subOff];
                }

                if (captureCurrentSubblock)
                {
                    diagnostics!.TargetYSubblockPredictedTopLeft = yPlane[subOff];
                    diagnostics.TargetYSubblockMode = mode;
                    diagnostics.TargetYSubblockHasAbove = subHasAbove;
                    diagnostics.TargetYSubblockHasLeft = subHasLeft;
                    diagnostics.TargetYSubblockAboveLeftSample = al;
                    Array.Copy(above4, diagnostics.TargetYSubblockAbovePredictor, diagnostics.TargetYSubblockAbovePredictor.Length);
                    Array.Copy(left4, diagnostics.TargetYSubblockLeftPredictor, diagnostics.TargetYSubblockLeftPredictor.Length);
                    Copy4x4Block(yPlane, yStride, subOff, diagnostics.TargetYSubblockPredicted4x4);
                }

                // 2. Decode + dequantize coefficients
                if (!mb.IsSkip)
                {
                    Array.Clear(coeffs);
                    var aboveContext = by > 0
                        ? currentMbYNonZero[((by - 1) * 4) + bx]
                        : aboveYNonZero[(mbX * 4) + bx];
                    var leftContext = bx > 0
                        ? currentMbYNonZero[(by * 4) + bx - 1]
                        : leftYNonZero[by];
                    System.Collections.Generic.List<string>? yBlockTokenTrace = null;
                    if (captureCurrentMacroblock && by == 0 && bx == 0)
                    {
                        yBlockTokenTrace = diagnostics!.FirstYBlockTokenTrace;
                        diagnostics.FirstYBlockAboveNonZeroContext = aboveContext;
                        diagnostics.FirstYBlockLeftNonZeroContext = leftContext;
                        diagnostics.FirstYBlockInitialContext = aboveContext + leftContext;

                        if (captureCurrentSubblock)
                        {
                            diagnostics.TargetYSubblockTokenDecoderStateBefore = tokenBd.DebugState;
                            var forcedContext0Bd = tokenBd;
                            Array.Clear(diagnostics.TargetYSubblockForcedContext0RawCoeffs);
                            diagnostics.TargetYSubblockForcedContext0TokenTrace.Clear();
                            diagnostics.TargetYSubblockForcedContext0NonZero = DecodeBlock(
                                ref forcedContext0Bd,
                                diagnostics.TargetYSubblockForcedContext0RawCoeffs,
                                3,
                                (by * 4) + bx,
                                0,
                                0,
                                diagnostics.TargetYSubblockForcedContext0TokenTrace);
                            diagnostics.TargetYSubblockForcedContext0RawDc = diagnostics.TargetYSubblockForcedContext0RawCoeffs[0];

                            var forcedContext1Bd = tokenBd;
                            Array.Clear(diagnostics.TargetYSubblockForcedContext1RawCoeffs);
                            diagnostics.TargetYSubblockForcedContext1TokenTrace.Clear();
                            diagnostics.TargetYSubblockForcedContext1NonZero = DecodeBlock(
                                ref forcedContext1Bd,
                                diagnostics.TargetYSubblockForcedContext1RawCoeffs,
                                3,
                                (by * 4) + bx,
                                0,
                                1,
                                diagnostics.TargetYSubblockForcedContext1TokenTrace);
                            diagnostics.TargetYSubblockForcedContext1RawDc = diagnostics.TargetYSubblockForcedContext1RawCoeffs[0];

                            var forcedContext2Bd = tokenBd;
                            Array.Clear(diagnostics.TargetYSubblockForcedContext2RawCoeffs);
                            diagnostics.TargetYSubblockForcedContext2TokenTrace.Clear();
                            diagnostics.TargetYSubblockForcedContext2NonZero = DecodeBlock(
                                ref forcedContext2Bd,
                                diagnostics.TargetYSubblockForcedContext2RawCoeffs,
                                3,
                                (by * 4) + bx,
                                0,
                                2,
                                diagnostics.TargetYSubblockForcedContext2TokenTrace);
                            diagnostics.TargetYSubblockForcedContext2RawDc = diagnostics.TargetYSubblockForcedContext2RawCoeffs[0];
                        }
                    }

                    if (captureCurrentSubblock && !(by == 0 && bx == 0))
                    {
                        yBlockTokenTrace = diagnostics!.TargetYSubblockTokenTrace;
                        diagnostics.TargetYSubblockAboveNonZeroContext = aboveContext;
                        diagnostics.TargetYSubblockLeftNonZeroContext = leftContext;
                        diagnostics.TargetYSubblockInitialContext = aboveContext + leftContext;
                        diagnostics.TargetYSubblockTokenDecoderStateBefore = tokenBd.DebugState;

                        var forcedContext0Bd = tokenBd;
                        Array.Clear(diagnostics.TargetYSubblockForcedContext0RawCoeffs);
                        diagnostics.TargetYSubblockForcedContext0TokenTrace.Clear();
                        diagnostics.TargetYSubblockForcedContext0NonZero = DecodeBlock(
                            ref forcedContext0Bd,
                            diagnostics.TargetYSubblockForcedContext0RawCoeffs,
                            3,
                            (by * 4) + bx,
                            0,
                            0,
                            diagnostics.TargetYSubblockForcedContext0TokenTrace);
                        diagnostics.TargetYSubblockForcedContext0RawDc = diagnostics.TargetYSubblockForcedContext0RawCoeffs[0];

                        var forcedContext1Bd = tokenBd;
                        Array.Clear(diagnostics.TargetYSubblockForcedContext1RawCoeffs);
                        diagnostics.TargetYSubblockForcedContext1TokenTrace.Clear();
                        diagnostics.TargetYSubblockForcedContext1NonZero = DecodeBlock(
                            ref forcedContext1Bd,
                            diagnostics.TargetYSubblockForcedContext1RawCoeffs,
                            3,
                            (by * 4) + bx,
                            0,
                            1,
                            diagnostics.TargetYSubblockForcedContext1TokenTrace);
                        diagnostics.TargetYSubblockForcedContext1RawDc = diagnostics.TargetYSubblockForcedContext1RawCoeffs[0];

                        var forcedContext2Bd = tokenBd;
                        Array.Clear(diagnostics.TargetYSubblockForcedContext2RawCoeffs);
                        diagnostics.TargetYSubblockForcedContext2TokenTrace.Clear();
                        diagnostics.TargetYSubblockForcedContext2NonZero = DecodeBlock(
                            ref forcedContext2Bd,
                            diagnostics.TargetYSubblockForcedContext2RawCoeffs,
                            3,
                            (by * 4) + bx,
                            0,
                            2,
                            diagnostics.TargetYSubblockForcedContext2TokenTrace);
                        diagnostics.TargetYSubblockForcedContext2RawDc = diagnostics.TargetYSubblockForcedContext2RawCoeffs[0];

                        var referenceStyleBd = tokenBd;
                        Array.Clear(diagnostics.TargetYSubblockReferenceStyleRawCoeffs);
                        diagnostics.TargetYSubblockReferenceStyleTokenTrace.Clear();
                        diagnostics.TargetYSubblockReferenceStyleNonZero = DecodeBlockReferenceStyle(
                            ref referenceStyleBd,
                            diagnostics.TargetYSubblockReferenceStyleRawCoeffs,
                            3,
                            0,
                            aboveContext + leftContext,
                            diagnostics.TargetYSubblockReferenceStyleTokenTrace);
                        diagnostics.TargetYSubblockReferenceStyleRawDc = diagnostics.TargetYSubblockReferenceStyleRawCoeffs[0];
                    }

                    currentMbYNonZero[(by * 4) + bx] = DecodeBlock(ref tokenBd, coeffs, 3, (by * 4) + bx, 0, aboveContext + leftContext, yBlockTokenTrace)
                        ? (byte)1
                        : (byte)0;

                    if (captureCurrentSubblock && !(by == 0 && bx == 0))
                    {
                        diagnostics!.TargetYSubblockTokenDecoderStateAfter = tokenBd.DebugState;
                    }

                    if (captureCurrentSubblock && by == 0 && bx == 0)
                    {
                        diagnostics!.TargetYSubblockTokenDecoderStateAfter = tokenBd.DebugState;
                    }

                    if (captureCurrentMacroblock && by == 0 && bx == 0)
                    {
                        diagnostics!.FirstYBlockNonZero = currentMbYNonZero[0] != 0;
                        diagnostics.FirstYBlockRawDc = coeffs[0];
                    }

                    if (captureCurrentSubblock)
                    {
                        diagnostics!.TargetYSubblockNonZero = currentMbYNonZero[(by * 4) + bx] != 0;
                        diagnostics.TargetYSubblockRawDc = coeffs[0];
                        Array.Copy(coeffs, diagnostics.TargetYSubblockRawCoeffs, 16);

                        if (by == 0 && bx == 0)
                        {
                            diagnostics.TargetYSubblockTokenTrace.Clear();
                            diagnostics.TargetYSubblockTokenTrace.AddRange(diagnostics.FirstYBlockTokenTrace);
                            diagnostics.TargetYSubblockAboveNonZeroContext = diagnostics.FirstYBlockAboveNonZeroContext;
                            diagnostics.TargetYSubblockLeftNonZeroContext = diagnostics.FirstYBlockLeftNonZeroContext;
                            diagnostics.TargetYSubblockInitialContext = diagnostics.FirstYBlockInitialContext;
                        }
                    }

                    Vp8Quantization.Dequantize(coeffs, dqm.Y1DcDequant, dqm.Y1AcDequant);

                    if (captureCurrentMacroblock && by == 0 && bx == 0)
                    {
                        diagnostics!.FirstYBlockDequantDcBeforeY2 = coeffs[0];
                        diagnostics.FirstYBlockDcAfterY2Injection = coeffs[0];
                    }

                    if (captureCurrentSubblock)
                    {
                        diagnostics!.TargetYSubblockDequantDcBeforeY2 = coeffs[0];
                        diagnostics.TargetYSubblockDcAfterY2Injection = coeffs[0];
                        Array.Copy(coeffs, diagnostics.TargetYSubblockDequantCoeffs, 16);
                    }

                    // 3. IDCT adds residuals to prediction
                    Vp8Dct.InverseDct4x4(coeffs, yPlane.AsSpan(subOff), yStride);

                    if (captureCurrentMacroblock && by == 0 && bx == 0)
                    {
                        diagnostics!.FirstYBlockOutputTopLeft = yPlane[subOff];
                    }

                    if (captureCurrentSubblock)
                    {
                        diagnostics!.TargetYSubblockOutputTopLeft = yPlane[subOff];
                        Copy4x4Block(yPlane, yStride, subOff, diagnostics.TargetYSubblockOutput4x4);
                    }
                }
            }
        }
    }

    private static void Copy4x4Block(byte[] plane, int stride, int offset, byte[] destination)
    {
        for (var y = 0; y < 4; y++)
        {
            Array.Copy(plane, offset + (y * stride), destination, y * 4, 4);
        }
    }

    private static void GatherBPredAbovePredictors(byte[] yPlane, int yStride, int yOffset, int subOff,
        bool hasAbove, int bx, int by, byte[] destination)
    {
        destination.AsSpan().Fill(127);

        if (by == 0 && !hasAbove)
        {
            return;
        }

        var aboveOffset = subOff - yStride;
        for (var index = 0; index < 4; index++)
        {
            destination[index] = yPlane[aboveOffset + index];
        }

        if (bx < 3)
        {
            for (var index = 0; index < 4; index++)
            {
                destination[4 + index] = yPlane[aboveOffset + 4 + index];
            }

            return;
        }

        var lastAbovePixel = yPlane[aboveOffset + 3];

        // For lower rows inside a B_PRED macroblock, VP8 copies the top-row top-right
        // extension downward rather than reusing the current row's last reconstructed pixel.
        if (by > 0)
        {
            if (!hasAbove)
            {
                return;
            }

            var topAboveOffset = yOffset - yStride;
            var topAboveRightOffset = topAboveOffset + 16;
            var topRowStart = topAboveOffset - (topAboveOffset % yStride);
            var topRowEnd = topRowStart + yStride;
            var lastTopAbovePixel = yPlane[topAboveOffset + 15];

            for (var index = 0; index < 4; index++)
            {
                var src = topAboveRightOffset + index;
                destination[4 + index] = src < topRowEnd ? yPlane[src] : lastTopAbovePixel;
            }

            return;
        }

        var aboveRightOffset = aboveOffset + 4;
        var rowStart = aboveOffset - (aboveOffset % yStride);
        var rowEnd = rowStart + yStride;

        for (var index = 0; index < 4; index++)
        {
            var src = aboveRightOffset + index;
            destination[4 + index] = src < rowEnd ? yPlane[src] : lastAbovePixel;
        }
    }

    // ── Intra prediction helpers ──

    private static void Apply16x16Prediction(byte[] yPlane, int yStride, int yOff,
        bool hasAbove, bool hasLeft, int yMode)
    {
        Span<byte> above = stackalloc byte[16];
        above.Fill(127);
        if (hasAbove)
        {
            yPlane.AsSpan(yOff - yStride, 16).CopyTo(above);
        }

        Span<byte> left = stackalloc byte[16];
        left.Fill(129);
        if (hasLeft)
        {
            for (var i = 0; i < 16; i++)
            {
                left[i] = yPlane[yOff + (i * yStride) - 1];
            }
        }

        var aboveLeft = (hasAbove && hasLeft) ? yPlane[yOff - yStride - 1] : GetOutOfFrameAboveLeft(hasAbove, hasLeft);

        switch (yMode)
        {
            case Vp8Constants.DcPred:
                Vp8Prediction.Predict16x16Dc(above, left, hasAbove, hasLeft, yPlane.AsSpan(yOff), yStride);
                break;
            case Vp8Constants.VPred:
                Vp8Prediction.Predict16x16V(above, yPlane.AsSpan(yOff), yStride);
                break;
            case Vp8Constants.HPred:
                Vp8Prediction.Predict16x16H(left, yPlane.AsSpan(yOff), yStride);
                break;
            case Vp8Constants.TmPred:
                Vp8Prediction.Predict16x16Tm(above, left, aboveLeft, yPlane.AsSpan(yOff), yStride);
                break;
        }
    }

    private static void ApplyUvPrediction(byte[] uPlane, byte[] vPlane, int uvStride,
        int uvOff, bool hasAbove, bool hasLeft, int uvMode)
    {
        Span<byte> aboveU = stackalloc byte[8];
        Span<byte> aboveV = stackalloc byte[8];
        Span<byte> leftU = stackalloc byte[8];
        Span<byte> leftV = stackalloc byte[8];
        aboveU.Fill(127);
        aboveV.Fill(127);
        leftU.Fill(129);
        leftV.Fill(129);

        if (hasAbove)
        {
            uPlane.AsSpan(uvOff - uvStride, 8).CopyTo(aboveU);
            vPlane.AsSpan(uvOff - uvStride, 8).CopyTo(aboveV);
        }

        if (hasLeft)
        {
            for (var i = 0; i < 8; i++)
            {
                leftU[i] = uPlane[uvOff + (i * uvStride) - 1];
                leftV[i] = vPlane[uvOff + (i * uvStride) - 1];
            }
        }

        var aboveLeftU = (hasAbove && hasLeft) ? uPlane[uvOff - uvStride - 1] : GetOutOfFrameAboveLeft(hasAbove, hasLeft);
        var aboveLeftV = (hasAbove && hasLeft) ? vPlane[uvOff - uvStride - 1] : GetOutOfFrameAboveLeft(hasAbove, hasLeft);

        switch (uvMode)
        {
            case Vp8Constants.DcPred:
                Vp8Prediction.Predict8x8Dc(aboveU, leftU, hasAbove, hasLeft, uPlane.AsSpan(uvOff), uvStride);
                Vp8Prediction.Predict8x8Dc(aboveV, leftV, hasAbove, hasLeft, vPlane.AsSpan(uvOff), uvStride);
                break;
            case Vp8Constants.VPred:
                Vp8Prediction.Predict8x8V(aboveU, uPlane.AsSpan(uvOff), uvStride);
                Vp8Prediction.Predict8x8V(aboveV, vPlane.AsSpan(uvOff), uvStride);
                break;
            case Vp8Constants.HPred:
                Vp8Prediction.Predict8x8H(leftU, uPlane.AsSpan(uvOff), uvStride);
                Vp8Prediction.Predict8x8H(leftV, vPlane.AsSpan(uvOff), uvStride);
                break;
            case Vp8Constants.TmPred:
                Vp8Prediction.Predict8x8Tm(aboveU, leftU, aboveLeftU, uPlane.AsSpan(uvOff), uvStride);
                Vp8Prediction.Predict8x8Tm(aboveV, leftV, aboveLeftV, vPlane.AsSpan(uvOff), uvStride);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetOutOfFrameAboveLeft(bool hasAbove, bool hasLeft)
    {
        if (!hasAbove)
        {
            return 127;
        }

        if (!hasLeft)
        {
            return 129;
        }

        return 128;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MapMacroblockModeToSubblockContext(int yMode) => yMode switch
    {
        Vp8Constants.DcPred => Vp8Constants.BDcPred,
        Vp8Constants.VPred => Vp8Constants.BVePred,
        Vp8Constants.HPred => Vp8Constants.BHePred,
        Vp8Constants.TmPred => Vp8Constants.BTmPred,
        _ => Vp8Constants.BDcPred,
    };

    // ── Loop filter ──

    private void ApplyLoopFilter(byte[] yPlane, byte[] uPlane, byte[] vPlane,
        int yStride, int uvStride, Vp8Macroblock[] macroblocks)
    {
        var mbW = ctx.MbWidth;
        var mbH = ctx.MbHeight;

        for (var mbY = 0; mbY < mbH; mbY++)
        {
            for (var mbX = 0; mbX < mbW; mbX++)
            {
                ref readonly var macroblock = ref macroblocks[(mbY * mbW) + mbX];
                var level = macroblock.FilterLevel;
                if (level == 0)
                {
                    continue;
                }

                var fp = Vp8LoopFilter.ComputeParams(level, ctx.Filter.Sharpness, ctx.IsKeyFrame);
                var yOff = (mbY * 16 * yStride) + (mbX * 16);
                var uvOff = (mbY * 8 * uvStride) + (mbX * 8);

                if (ctx.Filter.UseNormalFilter)
                {
                    if (mbX > 0)
                    {
                        Vp8LoopFilter.FilterMbEdgeV(yPlane, yOff, yStride, 2, in fp);
                        Vp8LoopFilter.FilterMbEdgeV(uPlane, uvOff, uvStride, 1, in fp);
                        Vp8LoopFilter.FilterMbEdgeV(vPlane, uvOff, uvStride, 1, in fp);
                    }

                    if (macroblock.HasFilterSubblocks)
                    {
                        for (var col = 1; col < 4; col++)
                        {
                            Vp8LoopFilter.FilterSubEdgeV(yPlane, yOff + (col * 4), yStride, 2, in fp);
                        }
                        Vp8LoopFilter.FilterSubEdgeV(uPlane, uvOff + 4, uvStride, 1, in fp);
                        Vp8LoopFilter.FilterSubEdgeV(vPlane, uvOff + 4, uvStride, 1, in fp);
                    }

                    if (mbY > 0)
                    {
                        Vp8LoopFilter.FilterMbEdgeH(yPlane, yOff, yStride, 2, in fp);
                        Vp8LoopFilter.FilterMbEdgeH(uPlane, uvOff, uvStride, 1, in fp);
                        Vp8LoopFilter.FilterMbEdgeH(vPlane, uvOff, uvStride, 1, in fp);
                    }

                    if (macroblock.HasFilterSubblocks)
                    {
                        for (var row = 1; row < 4; row++)
                        {
                            Vp8LoopFilter.FilterSubEdgeH(yPlane, yOff + (row * 4 * yStride), yStride, 2, in fp);
                        }

                        Vp8LoopFilter.FilterSubEdgeH(uPlane, uvOff + 4 * uvStride, uvStride, 1, in fp);
                        Vp8LoopFilter.FilterSubEdgeH(vPlane, uvOff + 4 * uvStride, uvStride, 1, in fp);
                    }
                }
                else
                {
                    var mbLimit = ((level + 2) * 2) + fp.InteriorLimit;
                    var bLimit = (level * 2) + fp.InteriorLimit;

                    if (mbX > 0)
                    {
                        Vp8LoopFilter.SimpleFilterEdgeV(yPlane, yOff, yStride, mbLimit);
                    }

                    if (macroblock.HasFilterSubblocks)
                    {
                        for (var col = 1; col < 4; col++)
                        {
                            Vp8LoopFilter.SimpleFilterEdgeV(yPlane, yOff + (col * 4), yStride, bLimit);
                        }
                    }

                    if (mbY > 0)
                    {
                        Vp8LoopFilter.SimpleFilterEdgeH(yPlane, yOff, yStride, mbLimit);
                    }

                    if (macroblock.HasFilterSubblocks)
                    {
                        for (var row = 1; row < 4; row++)
                        {
                            Vp8LoopFilter.SimpleFilterEdgeH(yPlane, yOff + (row * 4 * yStride), yStride, bLimit);
                        }
                    }
                }
            }
        }
    }

    private int GetMacroblockFilterLevel(Vp8Macroblock macroblock)
    {
        var filterLevel = ctx.Filter.Level;

        if (ctx.Segment.Enabled)
        {
            filterLevel = ctx.Segment.AbsoluteDelta
                ? ctx.Segment.FilterLevel[macroblock.Segment]
                : filterLevel + ctx.Segment.FilterLevel[macroblock.Segment];
        }

        if (ctx.Filter.AdjustEnabled)
        {
            filterLevel += ctx.Filter.RefDelta[0];
            if (macroblock.YMode == Vp8Constants.BPred)
            {
                filterLevel += ctx.Filter.ModeDelta[0];
            }
        }

        return Math.Clamp(filterLevel, 0, Vp8Constants.MaxLoopFilterLevel);
    }

    // ── YUV->RGBA conversion (BT.601 limited range, as used by VP8) ──

    private static void ConvertYuvToRgba(byte[] yPlane, byte[] uPlane, byte[] vPlane,
        int yStride, int uvStride, ref VideoFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var dst = frame.PackedData;

        for (var y = 0; y < height; y++)
        {
            var row = dst.GetRow(y);
            var yRowOff = y * yStride;
            var uvRowOff = (y >> 1) * uvStride;

            for (var x = 0; x < width; x++)
            {
                var yVal = yPlane[yRowOff + x];
                var u = uPlane[uvRowOff + (x >> 1)] - 128;
                var v = vPlane[uvRowOff + (x >> 1)] - 128;
                var c = Math.Max(0, yVal - 16);

                var r = (298 * c + 409 * v + 128) >> 8;
                var g = (298 * c - 100 * u - 208 * v + 128) >> 8;
                var b = (298 * c + 516 * u + 128) >> 8;

                var px = x * 4;
                row[px] = ClampByte(r);
                row[px + 1] = ClampByte(g);
                row[px + 2] = ClampByte(b);
                row[px + 3] = 255; // alpha
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);

    private static void CopyBlock4x4(byte[] plane, int stride, int offset, byte[] destination)
    {
        for (var row = 0; row < 4; row++)
        {
            plane.AsSpan(offset + row * stride, 4).CopyTo(destination.AsSpan(row * 4, 4));
        }
    }

    private static void CopyBlock2x2(byte[] plane, int stride, int offset, byte[] destination)
    {
        for (var row = 0; row < 2; row++)
        {
            plane.AsSpan(offset + row * stride, 2).CopyTo(destination.AsSpan(row * 2, 2));
        }
    }
}

internal sealed class Vp8DecodeDiagnostics
{
    public bool FilterUseNormal { get; private set; }

    public int FilterLevel { get; private set; }

    public int FilterSharpness { get; private set; }

    public bool FilterAdjustEnabled { get; private set; }

    public int[] FilterRefDelta { get; } = new int[4];

    public int[] FilterModeDelta { get; } = new int[4];

    public bool SegmentEnabled { get; private set; }

    public int[] SegmentQuantizerLevel { get; } = new int[4];

    public int[] SegmentFilterLevel { get; } = new int[4];

    public int Log2Partitions { get; set; }

    public int BaseQp { get; set; }

    public bool MbNoCoeffSkip { get; set; }

    public int ProbSkipFalse { get; set; }

    public bool DisableCoeffProbUpdates { get; set; }

    public int TargetMacroblockX { get; set; }

    public int TargetMacroblockY { get; set; }

    public int TargetSubblockX { get; set; }

    public int TargetSubblockY { get; set; }

    public byte TargetMacroblockSegment { get; set; }

    public short TargetY1DcDequant { get; set; }

    public short TargetY1AcDequant { get; set; }

    public short TargetY2DcDequant { get; set; }

    public short TargetY2AcDequant { get; set; }

    public short TargetUvDcDequant { get; set; }

    public short TargetUvAcDequant { get; set; }

    public byte FirstMacroblockYMode { get; set; }

    public byte FirstMacroblockUvMode { get; set; }

    public bool FirstMacroblockIsSkip { get; set; }

    public byte FirstMacroblockPredictedYTopLeft { get; set; }

    public byte[] FirstMacroblockPredictionAbove16 { get; } = new byte[16];

    public byte[] FirstMacroblockPredictionLeft16 { get; } = new byte[16];

    public byte FirstMacroblockPredictionAboveLeft { get; set; }

    public byte FirstBPredSubblockMode { get; set; }

    public byte FirstBPredSubblockAboveMode { get; set; }

    public byte FirstBPredSubblockLeftMode { get; set; }

    public bool FirstMacroblockY2NonZero { get; set; }

    public short FirstMacroblockY2RawDc { get; set; }

    public short FirstMacroblockY2DequantDc { get; set; }

    public short FirstMacroblockY2WhtDc { get; set; }

    public short[] FirstMacroblockY2RawCoeffs { get; } = new short[16];

    public short[] FirstMacroblockY2DequantCoeffs { get; } = new short[16];

    public short[] FirstMacroblockY2WhtCoeffs { get; } = new short[16];

    public System.Collections.Generic.List<string> FirstMacroblockY2TokenTrace { get; } = [];

    public System.Collections.Generic.List<string> FirstYBlockTokenTrace { get; } = [];

    public bool FirstYBlockNonZero { get; set; }

    public int FirstYBlockAboveNonZeroContext { get; set; }

    public int FirstYBlockLeftNonZeroContext { get; set; }

    public int FirstYBlockInitialContext { get; set; }

    public short FirstYBlockRawDc { get; set; }

    public short FirstYBlockDequantDcBeforeY2 { get; set; }

    public short FirstYBlockDcAfterY2Injection { get; set; }

    public byte FirstYBlockOutputTopLeft { get; set; }

    public byte TargetYSubblockMode { get; set; }

    public byte TargetYSubblockAboveMode { get; set; }

    public byte TargetYSubblockLeftMode { get; set; }

    public bool TargetYSubblockHasAbove { get; set; }

    public bool TargetYSubblockHasLeft { get; set; }

    public byte TargetYSubblockAboveLeftSample { get; set; }

    public bool TargetYSubblockNonZero { get; set; }

    public int TargetYSubblockAboveNonZeroContext { get; set; }

    public int TargetYSubblockLeftNonZeroContext { get; set; }

    public int TargetYSubblockInitialContext { get; set; }

    public short TargetYSubblockRawDc { get; set; }

    public short TargetYSubblockDequantDcBeforeY2 { get; set; }

    public short TargetYSubblockDcAfterY2Injection { get; set; }

    public string TargetYSubblockTokenDecoderStateBefore { get; set; } = string.Empty;

    public string TargetYSubblockTokenDecoderStateAfter { get; set; } = string.Empty;

    public bool TargetYSubblockForcedContext0NonZero { get; set; }

    public short TargetYSubblockForcedContext0RawDc { get; set; }

    public short[] TargetYSubblockForcedContext0RawCoeffs { get; } = new short[16];

    public System.Collections.Generic.List<string> TargetYSubblockForcedContext0TokenTrace { get; } = [];

    public bool TargetYSubblockForcedContext1NonZero { get; set; }

    public short TargetYSubblockForcedContext1RawDc { get; set; }

    public short[] TargetYSubblockForcedContext1RawCoeffs { get; } = new short[16];

    public System.Collections.Generic.List<string> TargetYSubblockForcedContext1TokenTrace { get; } = [];

    public bool TargetYSubblockForcedContext2NonZero { get; set; }

    public short TargetYSubblockForcedContext2RawDc { get; set; }

    public short[] TargetYSubblockForcedContext2RawCoeffs { get; } = new short[16];

    public System.Collections.Generic.List<string> TargetYSubblockForcedContext2TokenTrace { get; } = [];

    public bool TargetYSubblockReferenceStyleNonZero { get; set; }

    public short TargetYSubblockReferenceStyleRawDc { get; set; }

    public short[] TargetYSubblockReferenceStyleRawCoeffs { get; } = new short[16];

    public System.Collections.Generic.List<string> TargetYSubblockReferenceStyleTokenTrace { get; } = [];

    public short[] TargetYSubblockRawCoeffs { get; } = new short[16];

    public short[] TargetYSubblockDequantCoeffs { get; } = new short[16];

    public byte TargetYSubblockPredictedTopLeft { get; set; }

    public byte TargetYSubblockOutputTopLeft { get; set; }

    public byte[] TargetMacroblockSubblockModes { get; } = new byte[16];

    public byte[] TargetYSubblockPredicted4x4 { get; } = new byte[16];

    public byte[] TargetYSubblockAbovePredictor { get; } = new byte[8];

    public byte[] TargetYSubblockLeftPredictor { get; } = new byte[4];

    public byte[] TargetYSubblockOutput4x4 { get; } = new byte[16];

    public int TargetUvSubblockX { get; set; }

    public int TargetUvSubblockY { get; set; }

    public bool TargetUBlockNonZero { get; set; }

    public bool TargetVBlockNonZero { get; set; }

    public short TargetUBlockRawDc { get; set; }

    public short TargetVBlockRawDc { get; set; }

    public short TargetUBlockDequantDc { get; set; }

    public short TargetVBlockDequantDc { get; set; }

    public byte TargetUBlockOutputTopLeft { get; set; }

    public byte TargetVBlockOutputTopLeft { get; set; }

    public short[] TargetUBlockRawCoeffs { get; } = new short[16];

    public short[] TargetVBlockRawCoeffs { get; } = new short[16];

    public short[] TargetUBlockDequantCoeffs { get; } = new short[16];

    public short[] TargetVBlockDequantCoeffs { get; } = new short[16];

    public byte[] TargetUBlockPredicted4x4 { get; } = new byte[16];

    public byte[] TargetVBlockPredicted4x4 { get; } = new byte[16];

    public byte[] TargetUBlockOutput4x4 { get; } = new byte[16];

    public byte[] TargetVBlockOutput4x4 { get; } = new byte[16];

    public byte[] TargetFinalY4x4 { get; } = new byte[16];

    public byte[] TargetFinalU2x2 { get; } = new byte[4];

    public byte[] TargetFinalV2x2 { get; } = new byte[4];

    public System.Collections.Generic.List<string> TargetYSubblockTokenTrace { get; } = [];

    public byte FirstMacroblockPredictedUTopLeft { get; set; }

    public byte FirstMacroblockPredictedVTopLeft { get; set; }

    public byte FirstUBlockOutputTopLeft { get; set; }

    public byte FirstVBlockOutputTopLeft { get; set; }

    public bool FirstUBlockNonZero { get; set; }

    public bool FirstVBlockNonZero { get; set; }

    public short FirstUBlockRawDc { get; set; }

    public short FirstVBlockRawDc { get; set; }

    public short FirstUBlockDequantDc { get; set; }

    public short FirstVBlockDequantDc { get; set; }

    public byte FirstMacroblockFinalYTopLeft { get; set; }

    public byte FirstMacroblockFinalUTopLeft { get; set; }

    public byte FirstMacroblockFinalVTopLeft { get; set; }

    public string FirstMacroblockFinalRgbaTopLeft { get; set; } = string.Empty;

    public void Reset()
    {
        FilterUseNormal = false;
        FilterLevel = 0;
        FilterSharpness = 0;
        FilterAdjustEnabled = false;
        Array.Clear(FilterRefDelta);
        Array.Clear(FilterModeDelta);
        SegmentEnabled = false;
        Array.Clear(SegmentQuantizerLevel);
        Array.Clear(SegmentFilterLevel);
        Log2Partitions = 0;
        BaseQp = 0;
        MbNoCoeffSkip = false;
        ProbSkipFalse = 0;
        DisableCoeffProbUpdates = false;
        TargetMacroblockSegment = 0;
        TargetY1DcDequant = 0;
        TargetY1AcDequant = 0;
        TargetY2DcDequant = 0;
        TargetY2AcDequant = 0;
        TargetUvDcDequant = 0;
        TargetUvAcDequant = 0;
        FirstMacroblockYMode = 0;
        FirstMacroblockUvMode = 0;
        FirstMacroblockIsSkip = false;
        FirstMacroblockPredictedYTopLeft = 0;
        Array.Clear(FirstMacroblockPredictionAbove16);
        Array.Clear(FirstMacroblockPredictionLeft16);
        FirstMacroblockPredictionAboveLeft = 0;
        FirstBPredSubblockMode = 0;
        FirstBPredSubblockAboveMode = 0;
        FirstBPredSubblockLeftMode = 0;
        FirstMacroblockY2NonZero = false;
        FirstMacroblockY2RawDc = 0;
        FirstMacroblockY2DequantDc = 0;
        FirstMacroblockY2WhtDc = 0;
        Array.Clear(FirstMacroblockY2RawCoeffs);
        Array.Clear(FirstMacroblockY2DequantCoeffs);
        Array.Clear(FirstMacroblockY2WhtCoeffs);
        FirstMacroblockY2TokenTrace.Clear();
        FirstYBlockTokenTrace.Clear();
        FirstYBlockNonZero = false;
        FirstYBlockAboveNonZeroContext = 0;
        FirstYBlockLeftNonZeroContext = 0;
        FirstYBlockInitialContext = 0;
        FirstYBlockRawDc = 0;
        FirstYBlockDequantDcBeforeY2 = 0;
        FirstYBlockDcAfterY2Injection = 0;
        FirstYBlockOutputTopLeft = 0;
        TargetYSubblockMode = 0;
        TargetYSubblockAboveMode = 0;
        TargetYSubblockLeftMode = 0;
        TargetYSubblockHasAbove = false;
        TargetYSubblockHasLeft = false;
        TargetYSubblockAboveLeftSample = 0;
        TargetYSubblockNonZero = false;
        TargetYSubblockAboveNonZeroContext = 0;
        TargetYSubblockLeftNonZeroContext = 0;
        TargetYSubblockInitialContext = 0;
        TargetYSubblockRawDc = 0;
        TargetYSubblockDequantDcBeforeY2 = 0;
        TargetYSubblockDcAfterY2Injection = 0;
        TargetYSubblockTokenDecoderStateBefore = string.Empty;
        TargetYSubblockTokenDecoderStateAfter = string.Empty;
        TargetYSubblockForcedContext0NonZero = false;
        TargetYSubblockForcedContext0RawDc = 0;
        Array.Clear(TargetYSubblockForcedContext0RawCoeffs);
        TargetYSubblockForcedContext0TokenTrace.Clear();
        TargetYSubblockForcedContext1NonZero = false;
        TargetYSubblockForcedContext1RawDc = 0;
        Array.Clear(TargetYSubblockForcedContext1RawCoeffs);
        TargetYSubblockForcedContext1TokenTrace.Clear();
        TargetYSubblockForcedContext2NonZero = false;
        TargetYSubblockForcedContext2RawDc = 0;
        Array.Clear(TargetYSubblockForcedContext2RawCoeffs);
        TargetYSubblockForcedContext2TokenTrace.Clear();
        TargetYSubblockReferenceStyleNonZero = false;
        TargetYSubblockReferenceStyleRawDc = 0;
        Array.Clear(TargetYSubblockReferenceStyleRawCoeffs);
        TargetYSubblockReferenceStyleTokenTrace.Clear();
        Array.Clear(TargetYSubblockRawCoeffs);
        Array.Clear(TargetYSubblockDequantCoeffs);
        TargetYSubblockPredictedTopLeft = 0;
        TargetYSubblockOutputTopLeft = 0;
        Array.Clear(TargetMacroblockSubblockModes);
        Array.Clear(TargetYSubblockPredicted4x4);
        Array.Clear(TargetYSubblockAbovePredictor);
        Array.Clear(TargetYSubblockLeftPredictor);
        Array.Clear(TargetYSubblockOutput4x4);
        TargetUvSubblockX = 0;
        TargetUvSubblockY = 0;
        TargetUBlockNonZero = false;
        TargetVBlockNonZero = false;
        TargetUBlockRawDc = 0;
        TargetVBlockRawDc = 0;
        TargetUBlockDequantDc = 0;
        TargetVBlockDequantDc = 0;
        TargetUBlockOutputTopLeft = 0;
        TargetVBlockOutputTopLeft = 0;
        Array.Clear(TargetUBlockRawCoeffs);
        Array.Clear(TargetVBlockRawCoeffs);
        Array.Clear(TargetUBlockDequantCoeffs);
        Array.Clear(TargetVBlockDequantCoeffs);
        Array.Clear(TargetUBlockPredicted4x4);
        Array.Clear(TargetVBlockPredicted4x4);
        Array.Clear(TargetUBlockOutput4x4);
        Array.Clear(TargetVBlockOutput4x4);
        Array.Clear(TargetFinalY4x4);
        Array.Clear(TargetFinalU2x2);
        Array.Clear(TargetFinalV2x2);
        TargetYSubblockTokenTrace.Clear();
        FirstMacroblockPredictedUTopLeft = 0;
        FirstMacroblockPredictedVTopLeft = 0;
        FirstUBlockOutputTopLeft = 0;
        FirstVBlockOutputTopLeft = 0;
        FirstUBlockNonZero = false;
        FirstVBlockNonZero = false;
        FirstUBlockRawDc = 0;
        FirstVBlockRawDc = 0;
        FirstUBlockDequantDc = 0;
        FirstVBlockDequantDc = 0;
        FirstMacroblockFinalYTopLeft = 0;
        FirstMacroblockFinalUTopLeft = 0;
        FirstMacroblockFinalVTopLeft = 0;
        FirstMacroblockFinalRgbaTopLeft = string.Empty;
    }

    public void SetFilterInfo(bool useNormalFilter, int level, int sharpness, bool adjustEnabled, int[]? refDelta, int[]? modeDelta, bool segmentEnabled, int[]? segmentFilterLevel)
    {
        FilterUseNormal = useNormalFilter;
        FilterLevel = level;
        FilterSharpness = sharpness;
        FilterAdjustEnabled = adjustEnabled;
        SegmentEnabled = segmentEnabled;

        Array.Clear(FilterRefDelta);
        if (refDelta is not null)
        {
            Array.Copy(refDelta, FilterRefDelta, Math.Min(refDelta.Length, FilterRefDelta.Length));
        }

        Array.Clear(FilterModeDelta);
        if (modeDelta is not null)
        {
            Array.Copy(modeDelta, FilterModeDelta, Math.Min(modeDelta.Length, FilterModeDelta.Length));
        }

        Array.Clear(SegmentFilterLevel);
        if (segmentFilterLevel is not null)
        {
            Array.Copy(segmentFilterLevel, SegmentFilterLevel, Math.Min(segmentFilterLevel.Length, SegmentFilterLevel.Length));
        }
    }

    public void SetPartitionInfo(int log2Partitions) =>
        Log2Partitions = log2Partitions;

    public void SetQuantizerInfo(int baseQp, int[]? segmentQuantizerLevel)
    {
        BaseQp = baseQp;
        Array.Clear(SegmentQuantizerLevel);
        if (segmentQuantizerLevel is not null)
        {
            Array.Copy(segmentQuantizerLevel, SegmentQuantizerLevel, Math.Min(segmentQuantizerLevel.Length, SegmentQuantizerLevel.Length));
        }
    }

    public void SetSkipInfo(bool mbNoCoeffSkip, int probSkipFalse)
    {
        MbNoCoeffSkip = mbNoCoeffSkip;
        ProbSkipFalse = probSkipFalse;
    }
}

/// <summary>
/// Extension to access 3D slice of a multidimensional array.
/// </summary>
internal static class Array3DExtensions
{
    /// <summary>Gets a ReadOnlySpan over one row of a 3D array [dim0][dim1][dim2].</summary>
#pragma warning disable CA1814
    public static ReadOnlySpan<byte> AsSpan3D(this byte[,,] array, int i, int j)
#pragma warning restore CA1814
    {
        var dim2 = array.GetLength(2);
        return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref array[i, j, 0], dim2);
    }
}
