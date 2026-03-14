#pragma warning disable S109, S3776, MA0051, MA0182, IDE0010, IDE0045, IDE0047, IDE0048, CA1822, S1172, IDE0060

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
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
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
        UpdateCoeffProbs(ref bd);

        // ── Skip coeff (§9.11) ──
        var mbNoCoeffSkip = bd.DecodeBit(128) != 0;
        var probSkipFalse = mbNoCoeffSkip ? (int)bd.DecodeLiteral(8) : 0;

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

        for (var mbY = 0; mbY < mbH; mbY++)
        {
            var leftBModes = new byte[4]; // right column of 4x4 modes
            ref var tokenBd = ref tokenDecoders[mbY % ctx.Partitions.Count];

            for (var mbX = 0; mbX < mbW; mbX++)
            {
                ref var mb = ref mbInfo[mbX];

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
                mb.UvMode = (byte)DecodeKfUvMode(ref bd);
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
                        }
                    }

                    // Update context
                    for (var i = 0; i < 4; i++)
                    {
                        aboveBModes[(mbX * 4) + i] = mb.SubblockModes[12 + i]; // bottom row
                        leftBModes[i] = mb.SubblockModes[(i * 4) + 3]; // right column
                    }
                }

                // ── Reconstruct: Prediction -> Coefficients -> IDCT (adds residuals to prediction) ──
                var yOffset = (mbY * 16 * yStride) + (mbX * 16);
                var uvOffset = (mbY * 8 * uvStride) + (mbX * 8);
                ref var dqm = ref ctx.DequantMatrices[mb.Segment];
                var hasAbove = mbY > 0;
                var hasLeft = mbX > 0;

                if (mb.YMode == Vp8Constants.BPred)
                {
                    // B_PRED: per-4x4 interleaved predict->decode->IDCT
                    DecodeBPredBlocks(ref tokenBd, yPlane, yStride, yOffset, hasAbove, hasLeft, ref mb, ref dqm, coeffs);
                }
                else
                {
                    // 16x16 prediction first
                    Apply16x16Prediction(yPlane, yStride, yOffset, hasAbove, hasLeft, mb.YMode);

                    if (!mb.IsSkip)
                    {
                        // Decode Y2 (DC of 16 Y subblocks)
                        Array.Clear(y2Coeffs);
                        Array.Clear(coeffs);
                        DecodeBlock(ref tokenBd, coeffs, 1, 0, 0);
                        Vp8Quantization.Dequantize(coeffs, dqm.Y2DcDequant, dqm.Y2AcDequant);
                        Vp8Dct.InverseWht4x4(coeffs, y2Coeffs);

                        // 16 Y subblocks: decode, dequant, inject Y2 DC, IDCT adds to prediction
                        for (var by = 0; by < 4; by++)
                        {
                            for (var bx = 0; bx < 4; bx++)
                            {
                                Array.Clear(coeffs);
                                DecodeBlock(ref tokenBd, coeffs, 0, (by * 4) + bx, 1);
                                Vp8Quantization.Dequantize(coeffs, dqm.Y1DcDequant, dqm.Y1AcDequant);
                                coeffs[0] = y2Coeffs[(by * 4) + bx];

                                var subOff = yOffset + (by * 4 * yStride) + (bx * 4);
                                Vp8Dct.InverseDct4x4(coeffs, yPlane.AsSpan(subOff), yStride);
                            }
                        }
                    }
                }

                // UV prediction first, then decode + IDCT adds residuals
                ApplyUvPrediction(uPlane, vPlane, uvStride, uvOffset, hasAbove, hasLeft, mb.UvMode);

                if (!mb.IsSkip)
                {
                    // 4 U subblocks
                    for (var by = 0; by < 2; by++)
                    {
                        for (var bx = 0; bx < 2; bx++)
                        {
                            Array.Clear(coeffs);
                            DecodeBlock(ref tokenBd, coeffs, 2, (by * 2) + bx, 0);
                            Vp8Quantization.Dequantize(coeffs, dqm.UvDcDequant, dqm.UvAcDequant);
                            var subOff = uvOffset + (by * 4 * uvStride) + (bx * 4);
                            Vp8Dct.InverseDct4x4(coeffs, uPlane.AsSpan(subOff), uvStride);
                        }
                    }

                    // 4 V subblocks
                    for (var by = 0; by < 2; by++)
                    {
                        for (var bx = 0; bx < 2; bx++)
                        {
                            Array.Clear(coeffs);
                            DecodeBlock(ref tokenBd, coeffs, 2, (by * 2) + bx, 0);
                            Vp8Quantization.Dequantize(coeffs, dqm.UvDcDequant, dqm.UvAcDequant);
                            var subOff = uvOffset + (by * 4 * uvStride) + (bx * 4);
                            Vp8Dct.InverseDct4x4(coeffs, vPlane.AsSpan(subOff), uvStride);
                        }
                    }
                }
            }
        }

        // ── Loop filter ──
        if (ctx.Filter.Level > 0)
        {
            ApplyLoopFilter(yPlane, uPlane, vPlane, yStride, uvStride);
        }

        // ── YUV -> RGBA conversion ──
        ConvertYuvToRgba(yPlane, uPlane, vPlane, yStride, uvStride, ref frame);

        return CodecResult.Success;
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
        ctx.Filter.UseNormalFilter = bd.DecodeBit(128) != 0;
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
        // Keyframe Y mode probs are fixed: P(DC)=145, P(V)=156, P(H)=163, P(TM)=128
        bd.DecodeTree(Vp8Constants.YModeTree, [145, 156, 163, 128]);

    private static int DecodeKfUvMode(ref Vp8BoolDecoder bd) =>
        // Keyframe UV mode probs: P(DC)=142, P(V)=114, P(H)=183
        bd.DecodeTree(Vp8Constants.UvModeTree, [142, 114, 183]);

    // ── Coefficient decoding (§13) ──

    private void DecodeBlock(ref Vp8BoolDecoder bd, short[] coeffs, int blockType,
        int blockIndex, int firstCoeff) // blockIndex reserved for future non-zero context
    {
        var ctx2 = 0; // previous coefficient context

        for (var i = firstCoeff; i < 16; i++)
        {
            var band = Vp8Constants.CoeffBandIndex[i];
            var probs = GetCoeffProbs(blockType, band, ctx2);

            // Decode token from tree
            var token = DecodeCoeffToken(ref bd, probs);

            if (token == 0) // EOB
            {
                break;
            }

            if (token == 1) // DCT_0 (zero coefficient)
            {
                ctx2 = 0;
                continue;
            }

            int value;
            if (token <= 5) // DCT_1..DCT_4 (literal values 1-4)
            {
                value = token - 1; // token 2->1, 3->2, 4->3, 5->4
            }
            else // token 6..11 = CAT1..CAT6
            {
                value = DecodeExtraValue(ref bd, token);
            }

            // Sign bit
            if (bd.DecodeBit(128) != 0)
            {
                value = -value;
            }

            var zigzagIndex = Vp8Constants.Zigzag[i];
            coeffs[zigzagIndex] = (short)value;
            if (value == 0)
            {
                ctx2 = 0;
            }
            else
            {
                ctx2 = Math.Abs(value) == 1 ? 1 : 2;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetCoeffProbs(int blockType, int band, int ctx2)
    {
        // Return the 11-probability span for this context
        return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref ctx.CoeffProbs[blockType, band, ctx2, 0],
            Vp8Constants.EntropyNodes);
    }

    private static int DecodeCoeffToken(ref Vp8BoolDecoder bd, ReadOnlySpan<byte> probs)
    {
        // Tree walk matching RFC 6386 §13.2 coeff_tree:
        //   prob[0]: 0=EOB, 1=continue
        //   prob[1]: 0=ZERO, 1=continue
        //   prob[2]: 0=ONE, 1=continue
        //   prob[3]: 0->{prob[4]: 0=TWO, 1->{prob[5]: 0=THREE, 1=FOUR}}, 1->categories
        //   prob[6]: 0->{prob[7]: 0=CAT1, 1=CAT2}, 1->{prob[8]: ...}
        //   prob[8]: 0->{prob[9]: 0=CAT3, 1=CAT4}, 1->{prob[10]: 0=CAT5, 1=CAT6}

        if (bd.DecodeBit(probs[0]) == 0) { return 0; }  // EOB
        if (bd.DecodeBit(probs[1]) == 0) { return 1; }  // DCT_0 (zero)
        if (bd.DecodeBit(probs[2]) == 0) { return 2; }  // DCT_1 (one)

        if (bd.DecodeBit(probs[3]) == 0)
        {
            // Left subtree: small values (2, 3, 4)
            if (bd.DecodeBit(probs[4]) == 0) { return 3; }  // DCT_2 (two)
            if (bd.DecodeBit(probs[5]) == 0) { return 4; }  // DCT_3 (three)
            return 5;                                         // DCT_4 (four)
        }

        // Right subtree: categories
        if (bd.DecodeBit(probs[6]) == 0)
        {
            if (bd.DecodeBit(probs[7]) == 0) { return 6; }  // CAT1
            return 7;                                         // CAT2
        }

        if (bd.DecodeBit(probs[8]) == 0)
        {
            if (bd.DecodeBit(probs[9]) == 0) { return 8; }  // CAT3
            return 9;                                         // CAT4
        }

        if (bd.DecodeBit(probs[10]) == 0) { return 10; }    // CAT5
        return 11;                                            // CAT6
    }

    private static int DecodeExtraValue(ref Vp8BoolDecoder bd, int token)
    {
        // token 6..11 = CAT1..CAT6 -> cat index = token - 6
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

    // ── B_PRED block decoding (extracted to avoid stackalloc-in-loop) ──

    private void DecodeBPredBlocks(ref Vp8BoolDecoder tokenBd, byte[] yPlane, int yStride,
        int yOffset, bool hasAbove, bool hasLeft, ref Vp8Macroblock mb, ref Vp8QuantMatrix dqm,
        short[] coeffs)
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
                Array.Clear(above4);
                if (by > 0 || hasAbove)
                {
                    var srcOff = subOff - yStride;
                    var count = Math.Min(8, yPlane.Length - srcOff);
                    Array.Copy(yPlane, srcOff, above4, 0, count);
                }

                Array.Clear(left4);
                if (bx > 0 || hasLeft)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        left4[i] = yPlane[subOff + (i * yStride) - 1];
                    }
                }

                var al = (by > 0 || hasAbove) && (bx > 0 || hasLeft)
                    ? yPlane[subOff - yStride - 1]
                    : (byte)128;

                // 1. Prediction fills the 4x4 block
                Vp8Prediction.Predict4x4(mode, above4, left4, al,
                    yPlane.AsSpan(subOff), yStride);

                // 2. Decode + dequantize coefficients
                if (!mb.IsSkip)
                {
                    Array.Clear(coeffs);
                    DecodeBlock(ref tokenBd, coeffs, 3, (by * 4) + bx, 0);
                    Vp8Quantization.Dequantize(coeffs, dqm.Y1DcDequant, dqm.Y1AcDequant);

                    // 3. IDCT adds residuals to prediction
                    Vp8Dct.InverseDct4x4(coeffs, yPlane.AsSpan(subOff), yStride);
                }
            }
        }
    }

    // ── Intra prediction helpers ──

    private static void Apply16x16Prediction(byte[] yPlane, int yStride, int yOff,
        bool hasAbove, bool hasLeft, int yMode)
    {
        Span<byte> above = stackalloc byte[16];
        if (hasAbove)
        {
            yPlane.AsSpan(yOff - yStride, 16).CopyTo(above);
        }

        Span<byte> left = stackalloc byte[16];
        if (hasLeft)
        {
            for (var i = 0; i < 16; i++)
            {
                left[i] = yPlane[yOff + (i * yStride) - 1];
            }
        }

        var aboveLeft = (hasAbove && hasLeft) ? yPlane[yOff - yStride - 1] : (byte)128;

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

        var aboveLeftU = (hasAbove && hasLeft) ? uPlane[uvOff - uvStride - 1] : (byte)128;
        var aboveLeftV = (hasAbove && hasLeft) ? vPlane[uvOff - uvStride - 1] : (byte)128;

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

    // ── Loop filter ──

    private void ApplyLoopFilter(byte[] yPlane, byte[] uPlane, byte[] vPlane,
        int yStride, int uvStride)
    {
        var mbW = ctx.MbWidth;
        var mbH = ctx.MbHeight;
        var level = ctx.Filter.Level;
        var fp = Vp8LoopFilter.ComputeParams(level, ctx.Filter.Sharpness, ctx.IsKeyFrame);

        for (var mbY = 0; mbY < mbH; mbY++)
        {
            for (var mbX = 0; mbX < mbW; mbX++)
            {
                var yOff = (mbY * 16 * yStride) + (mbX * 16);

                if (ctx.Filter.UseNormalFilter)
                {
                    // Horizontal MB edges (top of macroblock)
                    if (mbY > 0)
                    {
                        Vp8LoopFilter.FilterMbEdgeH(yPlane, yOff, yStride, in fp);
                    }

                    // Vertical MB edges (left of macroblock)
                    if (mbX > 0)
                    {
                        Vp8LoopFilter.FilterMbEdgeV(yPlane, yOff, yStride, in fp);
                    }

                    // Subblock edges (inner)
                    for (var row = 1; row < 4; row++)
                    {
                        Vp8LoopFilter.FilterSubEdgeH(yPlane, yOff + (row * 4 * yStride), yStride, in fp);
                    }

                    for (var col = 1; col < 4; col++)
                    {
                        Vp8LoopFilter.FilterSubEdgeV(yPlane, yOff + (col * 4), yStride, in fp);
                    }
                }
                else
                {
                    // Simple filter (Y only)
                    if (mbY > 0)
                    {
                        Vp8LoopFilter.SimpleFilterMbEdgeH(yPlane, yOff, yStride, level);
                    }

                    if (mbX > 0)
                    {
                        Vp8LoopFilter.SimpleFilterMbEdgeV(yPlane, yOff, yStride, level);
                    }
                }
            }
        }
    }

    // ── YUV->RGBA conversion (BT.601) ──

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

                // BT.601 YUV->RGB: R = Y + 1.402*V, G = Y - 0.344*U - 0.714*V, B = Y + 1.772*U
                var r = yVal + ((91881 * v + 32768) >> 16);
                var g = yVal - (((22554 * u) + (46802 * v) + 32768) >> 16);
                var b = yVal + ((116130 * u + 32768) >> 16);

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
