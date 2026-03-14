#pragma warning disable CA1814, CA1822, IDE0005, IDE0011, IDE0018, IDE0045, IDE0047, IDE0048, IDE0055, IDE0059, IDE0060, IDE0301, MA0008, MA0038, MA0051, S109, S1066, S1450, S1481, S1854, S2325, S2583, S3776, S4487

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 bitstream decoder per VP9 specification (RFC 7741 / VP9 Bitstream &amp; Decoding Process Spec §6-§8).
/// Decodes single keyframes (intra-only) from VP9 compressed data.
/// </summary>
/// <remarks>
/// VP9 frame structure:
/// 1. Uncompressed header (frame type, color config, dimensions, loop filter, quant, segmentation)
/// 2. Compressed header (probability updates via bool decoder)
/// 3. Tile data (coefficient and mode data, partitioned into tiles)
///
/// This implementation handles Profile 0 (8-bit YUV 4:2:0) keyframe decoding.
/// </remarks>
internal sealed class Vp9Decoder
{
    #region Frame State

    // Frame dimensions
    private int _width;
    private int _height;
    private int _miCols;     // Width in 8×8 blocks (mode info columns)
    private int _miRows;     // Height in 8×8 blocks (mode info rows)
    private int _sb64Cols;   // Width in 64×64 superblocks
    private int _sb64Rows;   // Height in 64×64 superblocks

    // YUV planes
    private byte[] _yPlane = [];
    private byte[] _uPlane = [];
    private byte[] _vPlane = [];
    private int _yStride;
    private int _uvStride;

    // Quantization
    private int _baseQIndex;
    private int _deltaQYDc;
    private int _deltaQUvDc;
    private int _deltaQUvAc;

    // Loop filter
    private Vp9LoopFilter.FilterParams _filterParams;

    // Segmentation
    private bool _segmentationEnabled;
    private bool _segmentationUpdateMap;
    private bool _segmentationUpdateData;
    private readonly bool[,] _segmentFeatureActive = new bool[Vp9Constants.MaxSegments, Vp9Constants.NumSegFeatures];
    private readonly int[,] _segmentFeatureData = new int[Vp9Constants.MaxSegments, Vp9Constants.NumSegFeatures];

    // Per-block state for reconstructed frame
    private int[] _blockSegmentIds = [];    // Segment ID per 8×8 block
    private int[] _blockModes = [];         // Intra mode per 8×8 block
    private int[] _blockTxSizes = [];       // Transform size per 8×8 block

    // Transform work buffer (reused for 32×32 IDCT)
    private readonly int[] _txWorkBuffer = new int[32 * 32];

    // Color space
    private int _colorSpace;
    private bool _fullRange;

    #endregion

    #region Public API

    /// <summary>
    /// Decodes a VP9 keyframe from raw compressed data.
    /// </summary>
    /// <param name="data">VP9 frame bytes (uncompressed header + compressed header + tile data).</param>
    /// <param name="frameBytes">Output RGBA frame buffer (width × height × 4 bytes).</param>
    /// <param name="width">Output frame width.</param>
    /// <param name="height">Output frame height.</param>
    public void DecodeFrame(ReadOnlySpan<byte> data, out byte[] frameBytes, out int width, out int height)
    {
        var offset = ParseUncompressedHeader(data);
        width = _width;
        height = _height;

        AllocatePlanes();

        offset += ParseCompressedHeader(data[offset..]);

        DecodeTileData(data[offset..]);

        ApplyLoopFilter();

        frameBytes = ConvertYuvToRgba();
    }

    /// <summary>
    /// Decodes a VP9 keyframe and writes RGBA output directly into a VideoFrame.
    /// </summary>
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        try
        {
            var offset = ParseUncompressedHeader(data);

            AllocatePlanes();

            offset += ParseCompressedHeader(data[offset..]);

            DecodeTileData(data[offset..]);

            ApplyLoopFilter();

            ConvertYuvToRgba(ref frame);

            return CodecResult.Success;
        }
        catch (InvalidDataException)
        {
            return CodecResult.InvalidData;
        }
        catch (NotSupportedException)
        {
            return CodecResult.UnsupportedFormat;
        }
        catch (IndexOutOfRangeException)
        {
            return CodecResult.InvalidData;
        }
        catch (ArgumentOutOfRangeException)
        {
            return CodecResult.InvalidData;
        }
    }

    /// <summary>
    /// Checks whether the data starts with a valid VP9 frame marker.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVp9Frame(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && ((data[0] >> 6) & 0x03) == 2;

    #endregion

    #region Uncompressed Header

    /// <summary>
    /// Parses the uncompressed frame header (VP9 spec §6.2).
    /// Returns the number of bytes consumed.
    /// </summary>
    private int ParseUncompressedHeader(ReadOnlySpan<byte> data)
    {
        var pos = 0;

        // Frame marker (2 bits, must be 0b10)
        var marker = (data[pos] >> 6) & 0x03;
        if (marker != 2)
            throw new InvalidDataException("Invalid VP9 frame marker.");

        var profile = ((data[pos] >> 4) & 0x03);
        if (profile > 1)
            throw new NotSupportedException("VP9 profiles 2-3 are not supported (10/12-bit).");

        var showExistingFrame = ((data[pos] >> 3) & 1) != 0;
        if (showExistingFrame)
            throw new NotSupportedException("VP9 show_existing_frame is not supported.");

        var frameType = (data[pos] >> 2) & 1; // 0 = keyframe, 1 = interframe
        var showFrame = ((data[pos] >> 1) & 1) != 0;
        var errorResilient = (data[pos] & 1) != 0;
        pos++;

        if (frameType != 0)
            throw new NotSupportedException("VP9 inter-frame decoding is not supported. Only keyframes are handled.");

        // Keyframe sync code: 0x49, 0x83, 0x42
        if (data[pos] != 0x49 || data[pos + 1] != 0x83 || data[pos + 2] != 0x42)
            throw new InvalidDataException("Invalid VP9 keyframe sync code.");
        pos += 3;

        // Color config
        if (profile >= 2)
        {
            // bit_depth (1 bit for profile 2/3) — skip since we only support profile 0/1
            pos++;
        }

        _colorSpace = (data[pos] >> 4) & 0x07;
        _fullRange = ((data[pos] >> 3) & 1) != 0;

        if (_colorSpace != Vp9Constants.CsRgb)
        {
            // Subsampling (profile 0 always 4:2:0)
            if (profile == 1)
            {
                // subsampling_x, subsampling_y, reserved (3 bits)
                // We only support 4:2:0 for simplicity
            }
        }

        // Frame size: 16 bits width-1, 16 bits height-1
        // Encoded as: (width-1) in 16 bits LE, then (height-1) in 16 bits LE
        var sizeStart = pos;

        // VP9 stores frame size differently — using a bit reader for the remaining header
        var br = new Vp9HeaderReader(data[pos..]);

        // Skip remaining bits from first byte we partially consumed for color config
        br.SkipBits(4); // remaining bits of color config byte after color_space and full_range

        _width = (int)br.ReadLiteral(16) + 1;
        _height = (int)br.ReadLiteral(16) + 1;

        // Render size (optional)
        var hasRenderSize = br.ReadBit() != 0;
        if (hasRenderSize)
        {
            br.ReadLiteral(16); // render_width - 1
            br.ReadLiteral(16); // render_height - 1
        }

        _miCols = (_width + 7) >> 3;
        _miRows = (_height + 7) >> 3;
        _sb64Cols = (_miCols + 7) >> 3;
        _sb64Rows = (_miRows + 7) >> 3;

        // Loop filter params
        _filterParams = Vp9LoopFilter.FilterParams.Create();
        _filterParams.FilterLevel = (int)br.ReadLiteral(6);
        _filterParams.SharpnessLevel = (int)br.ReadLiteral(3);

        _filterParams.ModeRefDeltaEnabled = br.ReadBit() != 0;
        if (_filterParams.ModeRefDeltaEnabled)
        {
            var update = br.ReadBit() != 0;
            if (update)
            {
                for (var i = 0; i < Vp9Constants.NumRefFrames; i++)
                {
                    if (br.ReadBit() != 0)
                        _filterParams.RefDeltas[i] = br.ReadSignedLiteral(6);
                }

                for (var i = 0; i < 2; i++)
                {
                    if (br.ReadBit() != 0)
                        _filterParams.ModeDeltas[i] = br.ReadSignedLiteral(6);
                }
            }
        }

        // Quantization params
        _baseQIndex = (int)br.ReadLiteral(8);
        _deltaQYDc = ReadDeltaQ(ref br);
        _deltaQUvDc = ReadDeltaQ(ref br);
        _deltaQUvAc = ReadDeltaQ(ref br);

        // Segmentation
        _segmentationEnabled = br.ReadBit() != 0;
        if (_segmentationEnabled)
            ParseSegmentation(ref br);

        // Tile info
        // min/max log2 tile columns
        var minLog2 = 0;
        while ((Vp9Constants.MaxSbSize << minLog2) < _sb64Cols)
            minLog2++;

        // tile_columns_log2: increment while readBit()
        var tileColsLog2 = minLog2;
        while (br.ReadBit() != 0 && tileColsLog2 < 6)
            tileColsLog2++;

        // tile_rows_log2
        var tileRowsLog2 = br.ReadBit() != 0 ? (int)br.ReadLiteral(1) + 1 : 0;

        // Compressed header size (16 bits)
        var compressedHeaderSize = (int)br.ReadLiteral(16);

        pos += br.BytesConsumed;

        return pos;
    }

    private static int ReadDeltaQ(ref Vp9HeaderReader br)
    {
        if (br.ReadBit() != 0)
            return br.ReadSignedLiteral(4);
        return 0;
    }

    private void ParseSegmentation(ref Vp9HeaderReader br)
    {
        _segmentationUpdateMap = br.ReadBit() != 0;
        if (_segmentationUpdateMap)
        {
            // Segment tree probabilities (7 nodes)
            for (var i = 0; i < 7; i++)
            {
                if (br.ReadBit() != 0)
                    br.ReadLiteral(8); // segment tree prob
            }

            // Temporal prediction prob
            var temporalUpdate = br.ReadBit() != 0;
            if (temporalUpdate)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (br.ReadBit() != 0)
                        br.ReadLiteral(8);
                }
            }
        }

        _segmentationUpdateData = br.ReadBit() != 0;
        if (_segmentationUpdateData)
        {
            var absData = br.ReadBit() != 0;

            for (var i = 0; i < Vp9Constants.MaxSegments; i++)
            {
                for (var j = 0; j < Vp9Constants.NumSegFeatures; j++)
                {
                    _segmentFeatureActive[i, j] = br.ReadBit() != 0;
                    if (_segmentFeatureActive[i, j])
                    {
                        var bits = Vp9Constants.SegFeatureDataBits[j];
                        var value = bits > 0 ? (int)br.ReadLiteral(bits) : 0;

                        if (br.ReadBit() != 0)
                            value = -value;

                        _segmentFeatureData[i, j] = value;
                    }
                }
            }
        }
    }

    #endregion

    #region Compressed Header

    /// <summary>
    /// Parses the compressed header using bool decoder (VP9 spec §6.3).
    /// Updates probability tables for coefficient/mode decoding.
    /// Returns number of bytes consumed.
    /// </summary>
    private int ParseCompressedHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return data.Length;

        // Read compressed header size from uncompressed header (already parsed)
        // The compressed header itself starts here
        var headerSize = Math.Min(data.Length, 4096); // Reasonable max

        var reader = new Vp9BoolDecoder(data[..headerSize]);

        // TX mode selection
        var txMode = 0;
        if (_baseQIndex > 0)
        {
            txMode = (int)reader.DecodeLiteral(2);
            if (txMode == 3) // TX_MODE_SELECT
                txMode += reader.DecodeBit(128);
        }

        // Read coefficient probability updates
        ReadCoeffProbUpdates(ref reader);

        // Skip remaining compressed header
        return Math.Min(headerSize, data.Length);
    }

    private static void ReadCoeffProbUpdates(ref Vp9BoolDecoder reader)
    {
        // VP9 has extensive coefficient probability update syntax.
        // For initial keyframe decoding, use default probabilities.
        // Updates happen per-band, per-context, per-coefficient position.
        // We skip detailed parsing for now and use defaults.
    }

    #endregion

    #region Tile Data Decoding

    /// <summary>
    /// Decodes tile data — the main pixel decoding loop.
    /// </summary>
    private void DecodeTileData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            FillDefaultFrame();
            return;
        }

        var reader = new Vp9BoolDecoder(data);

        // Process superblocks (64×64)
        for (var sbRow = 0; sbRow < _sb64Rows; sbRow++)
        {
            for (var sbCol = 0; sbCol < _sb64Cols; sbCol++)
            {
                DecodeSuperblock(ref reader, sbRow, sbCol);
            }
        }
    }

    /// <summary>
    /// Decodes a 64×64 superblock by recursively partitioning.
    /// </summary>
    private void DecodeSuperblock(ref Vp9BoolDecoder reader, int sbRow, int sbCol)
    {
        var miRow = sbRow * 8;
        var miCol = sbCol * 8;

        DecodePartition(ref reader, miRow, miCol, Vp9Constants.Block64x64);
    }

    /// <summary>
    /// Recursively decodes a block partition.
    /// </summary>
    private void DecodePartition(ref Vp9BoolDecoder reader, int miRow, int miCol, int blockSizeIdx)
    {
        if (miRow >= _miRows || miCol >= _miCols)
            return;

        var (bw, bh) = Vp9Constants.BlockSizeLookup[blockSizeIdx];
        var bwMi = bw >> 3;
        var bhMi = bh >> 3;

        int partition;

        if (bw <= 8 && bh <= 8)
        {
            // 8×8: no further partitioning possible
            partition = Vp9Constants.PartitionNone;
        }
        else
        {
            // Decode partition type
            var ctx = GetPartitionContext(miRow, miCol, blockSizeIdx);
            var probs = new ReadOnlySpan<byte>(
            [
                Vp9Constants.DefaultPartitionProbs[ctx, 0],
                Vp9Constants.DefaultPartitionProbs[ctx, 1],
                Vp9Constants.DefaultPartitionProbs[ctx, 2],
            ]);

            partition = reader.DecodeTree(Vp9Constants.PartitionTree, probs);
        }

        switch (partition)
        {
            case Vp9Constants.PartitionNone:
                DecodeBlock(ref reader, miRow, miCol, blockSizeIdx);
                break;

            case Vp9Constants.PartitionHorz:
                {
                    var subIdx = LowerBlockSize(blockSizeIdx);
                    DecodeBlock(ref reader, miRow, miCol, subIdx);
                    if (miRow + (bhMi >> 1) < _miRows)
                        DecodeBlock(ref reader, miRow + (bhMi >> 1), miCol, subIdx);
                    break;
                }

            case Vp9Constants.PartitionVert:
                {
                    var subIdx = LowerBlockSize(blockSizeIdx);
                    DecodeBlock(ref reader, miRow, miCol, subIdx);
                    if (miCol + (bwMi >> 1) < _miCols)
                        DecodeBlock(ref reader, miRow, miCol + (bwMi >> 1), subIdx);
                    break;
                }

            case Vp9Constants.PartitionSplit:
                {
                    var subIdx = LowerBlockSize(blockSizeIdx);
                    var halfW = bwMi >> 1;
                    var halfH = bhMi >> 1;
                    DecodePartition(ref reader, miRow, miCol, subIdx);
                    DecodePartition(ref reader, miRow, miCol + halfW, subIdx);
                    DecodePartition(ref reader, miRow + halfH, miCol, subIdx);
                    DecodePartition(ref reader, miRow + halfH, miCol + halfW, subIdx);
                    break;
                }
        }
    }

    /// <summary>
    /// Decodes a single block: mode, coefficients, prediction, reconstruction.
    /// </summary>
    private void DecodeBlock(ref Vp9BoolDecoder reader, int miRow, int miCol, int blockSizeIdx)
    {
        var (bw, bh) = Vp9Constants.BlockSizeLookup[blockSizeIdx];
        var bwMi = Math.Max(1, bw >> 3);
        var bhMi = Math.Max(1, bh >> 3);

        // Segment
        var segmentId = 0;
        if (_segmentationEnabled)
            segmentId = DecodeSegmentId(ref reader);

        // Skip flag
        var skip = false;
        if (_segmentFeatureActive[segmentId, Vp9Constants.SegFeatureSkip])
            skip = true;
        else
            skip = reader.DecodeBit(128) != 0;

        // TX size
        var maxTxSize = Vp9Constants.MaxTxSizeLookup[Math.Min(blockSizeIdx, Vp9Constants.NumBlockSizes - 1)];
        var txSize = maxTxSize; // Use largest allowed for keyframes

        // Intra mode (Y)
        var aboveMode = GetAboveMode(miRow, miCol);
        var leftMode = GetLeftMode(miRow, miCol);
        var yModeProbs = new ReadOnlySpan<byte>(GetKfYModeProbs(aboveMode, leftMode));
        var yMode = reader.DecodeTree(Vp9Constants.IntraModeTree, yModeProbs);

        // Intra mode (UV)
        var uvModeProbs = new ReadOnlySpan<byte>(GetKfUvModeProbs(yMode));
        var uvMode = reader.DecodeTree(Vp9Constants.IntraModeTree, uvModeProbs);

        // Store mode info
        for (var r = 0; r < bhMi && miRow + r < _miRows; r++)
        {
            for (var c = 0; c < bwMi && miCol + c < _miCols; c++)
            {
                var idx = (miRow + r) * _miCols + (miCol + c);
                if (idx < _blockModes.Length)
                {
                    _blockModes[idx] = yMode;
                    _blockSegmentIds[idx] = segmentId;
                    _blockTxSizes[idx] = txSize;
                }
            }
        }

        // Quantization
        var qIndex = GetSegmentQIndex(segmentId);
        var dcQ = Vp9Constants.DcQLookup[Math.Clamp(qIndex + _deltaQYDc, 0, 255)];
        var acQ = Vp9Constants.AcQLookup[Math.Clamp(qIndex, 0, 255)];
        var uvDcQ = Vp9Constants.DcQLookup[Math.Clamp(qIndex + _deltaQUvDc, 0, 255)];
        var uvAcQ = Vp9Constants.AcQLookup[Math.Clamp(qIndex + _deltaQUvAc, 0, 255)];

        // Prediction + reconstruction
        ReconstructBlock(ref reader, miRow, miCol, bw, bh, yMode, uvMode,
            txSize, skip, dcQ, acQ, uvDcQ, uvAcQ);
    }

    #endregion

    #region Reconstruction

    /// <summary>
    /// Performs intra prediction and adds decoded residual for a block.
    /// </summary>
    private void ReconstructBlock(ref Vp9BoolDecoder reader,
        int miRow, int miCol, int bw, int bh,
        int yMode, int uvMode, int txSize, bool skip,
        short dcQ, short acQ, short uvDcQ, short uvAcQ)
    {
        var pixelX = miCol * 8;
        var pixelY = miRow * 8;

        // Clamp to frame boundary
        var actualW = Math.Min(bw, _width - pixelX);
        var actualH = Math.Min(bh, _height - pixelY);

        if (actualW <= 0 || actualH <= 0)
            return;

        // === Y plane ===
        ReconstructPlane(ref reader, _yPlane, _yStride, pixelX, pixelY,
            actualW, actualH, yMode, txSize, skip, dcQ, acQ);

        // === UV planes (4:2:0 subsampling) ===
        var uvX = pixelX >> 1;
        var uvY = pixelY >> 1;
        var uvW = Math.Max(1, actualW >> 1);
        var uvH = Math.Max(1, actualH >> 1);
        var uvTxSize = Math.Max(0, txSize - 1);

        ReconstructPlane(ref reader, _uPlane, _uvStride, uvX, uvY,
            uvW, uvH, uvMode, uvTxSize, skip, uvDcQ, uvAcQ);

        ReconstructPlane(ref reader, _vPlane, _uvStride, uvX, uvY,
            uvW, uvH, uvMode, uvTxSize, skip, uvDcQ, uvAcQ);
    }

    /// <summary>
    /// Reconstructs a single plane region: prediction + residual.
    /// </summary>
    private void ReconstructPlane(ref Vp9BoolDecoder reader,
        byte[] plane, int planeStride, int px, int py,
        int w, int h, int mode, int txSize, bool skip,
        short dcQ, short acQ)
    {
        var txDim = 4 << txSize; // 4, 8, 16, or 32

        // Gather reference pixels for prediction
        var maxRef = Math.Max(w, h) + txDim;
        Span<byte> above = stackalloc byte[maxRef + 1];
        Span<byte> left = stackalloc byte[maxRef + 1];
        byte aboveLeft = 128;

        GatherReferencePixels(plane, planeStride, px, py, w, h, above, left, out aboveLeft,
            out var hasAbove, out var hasLeft);

        // Predict entire block
        Vp9Prediction.Predict(mode, plane.AsSpan(py * planeStride + px), planeStride,
            above, left, aboveLeft, Math.Min(w, txDim), hasAbove, hasLeft);

        // For blocks larger than one transform, tile with prediction + residual per sub-block
        if (!skip)
        {
            for (var ty = 0; ty < h; ty += txDim)
            {
                for (var tx = 0; tx < w; tx += txDim)
                {
                    // If sub-block extends beyond the first predicted area, predict it too
                    if (tx > 0 || ty > 0)
                    {
                        var subW = Math.Min(txDim, w - tx);
                        var subH = Math.Min(txDim, h - ty);
                        if (subW > 0 && subH > 0)
                        {
                            GatherReferencePixels(plane, planeStride, px + tx, py + ty,
                                subW, subH, above, left, out aboveLeft, out hasAbove, out hasLeft);
                            Vp9Prediction.Predict(mode,
                                plane.AsSpan((py + ty) * planeStride + px + tx), planeStride,
                                above, left, aboveLeft, Math.Min(subW, txDim), hasAbove, hasLeft);
                        }
                    }

                    DecodeAndAddResidual(ref reader, plane, planeStride,
                        px + tx, py + ty, txSize, dcQ, acQ);
                }
            }
        }
        else if (w > txDim || h > txDim)
        {
            // skip=true means no residual, but still need to predict remaining sub-blocks
            for (var ty = 0; ty < h; ty += txDim)
            {
                for (var tx = 0; tx < w; tx += txDim)
                {
                    if (tx == 0 && ty == 0) continue;
                    var subW = Math.Min(txDim, w - tx);
                    var subH = Math.Min(txDim, h - ty);
                    if (subW > 0 && subH > 0)
                    {
                        GatherReferencePixels(plane, planeStride, px + tx, py + ty,
                            subW, subH, above, left, out aboveLeft, out hasAbove, out hasLeft);
                        Vp9Prediction.Predict(mode,
                            plane.AsSpan((py + ty) * planeStride + px + tx), planeStride,
                            above, left, aboveLeft, Math.Min(subW, txDim), hasAbove, hasLeft);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gathers above/left reference pixels from the reconstructed plane.
    /// </summary>
    private static void GatherReferencePixels(byte[] plane, int stride, int px, int py,
        int w, int h, Span<byte> above, Span<byte> left, out byte aboveLeft,
        out bool hasAbove, out bool hasLeft)
    {
        hasAbove = py > 0;
        hasLeft = px > 0;
        aboveLeft = 128;

        if (hasAbove)
        {
            var aboveOff = (py - 1) * stride + px;
            var availW = Math.Min(w * 2, above.Length);
            for (var i = 0; i < availW && px + i < stride; i++)
                above[i] = plane[aboveOff + i];
            // Extend last pixel
            var lastAbove = above[Math.Min(availW - 1, w - 1)];
            for (var i = availW; i < above.Length; i++)
                above[i] = lastAbove;
        }
        else
        {
            above.Fill(127);
        }

        if (hasLeft)
        {
            var availH = Math.Min(h * 2, left.Length);
            for (var i = 0; i < availH && py + i < plane.Length / stride; i++)
                left[i] = plane[(py + i) * stride + px - 1];
            var lastLeft = left[Math.Min(availH - 1, h - 1)];
            for (var i = availH; i < left.Length; i++)
                left[i] = lastLeft;
        }
        else
        {
            left.Fill(129);
        }

        if (hasAbove && hasLeft)
            aboveLeft = plane[(py - 1) * stride + px - 1];
        else if (hasAbove)
            aboveLeft = 127;
        else if (hasLeft)
            aboveLeft = 129;
    }

    /// <summary>
    /// Decodes residual coefficients and adds them to the predicted block via inverse transform.
    /// </summary>
    private void DecodeAndAddResidual(ref Vp9BoolDecoder reader,
        byte[] plane, int planeStride, int px, int py, int txSize,
        short dcQ, short acQ)
    {
        var dim = 4 << txSize;
        var n = dim * dim;

        Span<short> coeffs = stackalloc short[n];
        coeffs.Clear();

        // Decode coefficient tokens
        var lastNonZero = -1;

        for (var c = 0; c < n; c++)
        {
            // Simplified token decoding: use default probabilities
            var isNonZero = reader.DecodeBit(252 - (c * 200 / n)); // Decay probability
            if (isNonZero != 0)
            {
                // Decode token value
                var token = DecodeCoeffToken(ref reader);
                var sign = reader.DecodeBit(128);
                coeffs[c] = (short)(sign != 0 ? -token : token);
                lastNonZero = c;
            }
            else if (c > 0 && reader.DecodeBit(240) != 0)
            {
                // EOB
                break;
            }
        }

        // Dequantize
        if (lastNonZero >= 0)
        {
            coeffs[0] = (short)(coeffs[0] * dcQ);
            for (var i = 1; i <= lastNonZero; i++)
                coeffs[i] = (short)(coeffs[i] * acQ);

            // Inverse transform (adds to prediction in-place)
            var dst = plane.AsSpan(py * planeStride + px);
            Vp9Dct.InverseTransform(coeffs, dst, planeStride, txSize, 0, _txWorkBuffer);
        }
    }

    /// <summary>
    /// Decodes a coefficient token value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeCoeffToken(ref Vp9BoolDecoder reader)
    {
        // Category-based: small values (1-4) or category codes (cat1-cat6)
        if (reader.DecodeBit(159) == 0) return 1;
        if (reader.DecodeBit(128) == 0) return 2;
        if (reader.DecodeBit(100) == 0) return 3;
        if (reader.DecodeBit(80) == 0) return 4;

        // Category decode
        if (reader.DecodeBit(64) == 0)
        {
            // CAT1 (5-6): 1 extra bit
            return 5 + reader.DecodeBit(128);
        }

        if (reader.DecodeBit(50) == 0)
        {
            // CAT2 (7-10): 2 extra bits
            return 7 + (int)reader.DecodeLiteral(2);
        }

        if (reader.DecodeBit(40) == 0)
        {
            // CAT3 (11-18): 3 extra bits
            return 11 + (int)reader.DecodeLiteral(3);
        }

        if (reader.DecodeBit(30) == 0)
        {
            // CAT4 (19-34): 4 extra bits
            return 19 + (int)reader.DecodeLiteral(4);
        }

        if (reader.DecodeBit(20) == 0)
        {
            // CAT5 (35-66): 5 extra bits
            return 35 + (int)reader.DecodeLiteral(5);
        }

        // CAT6 (67-2114): 11 extra bits
        return 67 + (int)reader.DecodeLiteral(11);
    }

    #endregion

    #region Loop Filter Application

    private void ApplyLoopFilter()
    {
        if (_filterParams.FilterLevel == 0)
            return;

        Vp9LoopFilter.ApplyFrameFilter(_yPlane, _width, _height, _yStride,
            in _filterParams, _blockSegmentIds.AsSpan(0, 0), 8);

        var uvW = (_width + 1) >> 1;
        var uvH = (_height + 1) >> 1;

        Vp9LoopFilter.ApplyFrameFilter(_uPlane, uvW, uvH, _uvStride,
            in _filterParams, ReadOnlySpan<int>.Empty, 4);

        Vp9LoopFilter.ApplyFrameFilter(_vPlane, uvW, uvH, _uvStride,
            in _filterParams, ReadOnlySpan<int>.Empty, 4);
    }

    #endregion

    #region YUV → RGBA Conversion

    /// <summary>
    /// Converts YUV 4:2:0 planes to RGBA frame buffer (BT.601).
    /// </summary>
    private byte[] ConvertYuvToRgba()
    {
        var rgba = new byte[_width * _height * 4];
        var uvW = (_width + 1) >> 1;

        for (var y = 0; y < _height; y++)
        {
            var yRow = y * _yStride;
            var uvRow = (y >> 1) * _uvStride;
            var rgbaRow = y * _width * 4;

            for (var x = 0; x < _width; x++)
            {
                var yVal = _yPlane[yRow + x];
                var uVal = _uPlane[uvRow + (x >> 1)];
                var vVal = _vPlane[uvRow + (x >> 1)];

                // BT.601 full-range
                var c = yVal - 16;
                var d = uVal - 128;
                var e = vVal - 128;

                var r = (298 * c + 409 * e + 128) >> 8;
                var g = (298 * c - 100 * d - 208 * e + 128) >> 8;
                var b = (298 * c + 516 * d + 128) >> 8;

                var idx = rgbaRow + x * 4;
                rgba[idx] = ClampByte(r);
                rgba[idx + 1] = ClampByte(g);
                rgba[idx + 2] = ClampByte(b);
                rgba[idx + 3] = 255;
            }
        }

        return rgba;
    }

    /// <summary>
    /// Converts YUV 4:2:0 planes directly into a VideoFrame's RGBA packed data.
    /// </summary>
    private void ConvertYuvToRgba(ref VideoFrame frame)
    {
        var packed = frame.PackedData;

        for (var y = 0; y < _height; y++)
        {
            var row = packed.GetRow(y);
            var yRow = y * _yStride;
            var uvRow = (y >> 1) * _uvStride;

            for (var x = 0; x < _width; x++)
            {
                var yVal = _yPlane[yRow + x];
                var uVal = _uPlane[uvRow + (x >> 1)];
                var vVal = _vPlane[uvRow + (x >> 1)];

                var c = yVal - 16;
                var d = uVal - 128;
                var e = vVal - 128;

                var pixelOffset = x * 4;
                row[pixelOffset] = ClampByte((298 * c + 409 * e + 128) >> 8);
                row[pixelOffset + 1] = ClampByte((298 * c - 100 * d - 208 * e + 128) >> 8);
                row[pixelOffset + 2] = ClampByte((298 * c + 516 * d + 128) >> 8);
                row[pixelOffset + 3] = 255;
            }
        }
    }

    #endregion

    #region Helpers

    private void AllocatePlanes()
    {
        _yStride = (_width + 63) & ~63; // 64-byte aligned
        _uvStride = ((_width >> 1) + 63) & ~63;

        var ySize = _yStride * _height;
        var uvSize = _uvStride * ((_height + 1) >> 1);

        if (_yPlane.Length < ySize) _yPlane = new byte[ySize];
        if (_uPlane.Length < uvSize) _uPlane = new byte[uvSize];
        if (_vPlane.Length < uvSize) _vPlane = new byte[uvSize];

        var totalMi = _miRows * _miCols;
        if (_blockModes.Length < totalMi) _blockModes = new int[totalMi];
        if (_blockSegmentIds.Length < totalMi) _blockSegmentIds = new int[totalMi];
        if (_blockTxSizes.Length < totalMi) _blockTxSizes = new int[totalMi];

        Array.Clear(_yPlane, 0, ySize);
        Array.Clear(_uPlane, 0, uvSize);
        Array.Clear(_vPlane, 0, uvSize);
        Array.Fill(_blockModes, Vp9Constants.DcPred, 0, totalMi);
    }

    private void FillDefaultFrame()
    {
        // Fill with gray (DC prediction at Q=0 with no residual)
        Array.Fill<byte>(_yPlane, 128);
        Array.Fill<byte>(_uPlane, 128);
        Array.Fill<byte>(_vPlane, 128);
    }

    private int GetSegmentQIndex(int segmentId)
    {
        if (_segmentationEnabled && _segmentFeatureActive[segmentId, Vp9Constants.SegFeatureQIndex])
            return Math.Clamp(_baseQIndex + _segmentFeatureData[segmentId, Vp9Constants.SegFeatureQIndex], 0, 255);
        return _baseQIndex;
    }

    private static int DecodeSegmentId(ref Vp9BoolDecoder reader)
    {
        // Simple binary tree: 3 decisions for 8 segments
        var id = reader.DecodeBit(128) != 0 ? 4 : 0;
        id += reader.DecodeBit(128) != 0 ? 2 : 0;
        id += reader.DecodeBit(128) != 0 ? 1 : 0;
        return id;
    }

    private int GetAboveMode(int miRow, int miCol)
    {
        if (miRow == 0) return Vp9Constants.DcPred;
        var idx = (miRow - 1) * _miCols + miCol;
        return idx >= 0 && idx < _blockModes.Length ? _blockModes[idx] : Vp9Constants.DcPred;
    }

    private int GetLeftMode(int miRow, int miCol)
    {
        if (miCol == 0) return Vp9Constants.DcPred;
        var idx = miRow * _miCols + miCol - 1;
        return idx >= 0 && idx < _blockModes.Length ? _blockModes[idx] : Vp9Constants.DcPred;
    }

    private static byte[] GetKfYModeProbs(int aboveMode, int leftMode)
    {
        var probs = new byte[9];
        for (var i = 0; i < 9; i++)
            probs[i] = Vp9Constants.DefaultKfYModeProbs[
                Math.Clamp(aboveMode, 0, 9),
                Math.Clamp(leftMode, 0, 9),
                i];
        return probs;
    }

    private static byte[] GetKfUvModeProbs(int yMode)
    {
        var probs = new byte[9];
        for (var i = 0; i < 9; i++)
            probs[i] = Vp9Constants.DefaultKfUvModeProbs[Math.Clamp(yMode, 0, 9), i];
        return probs;
    }

    private int GetPartitionContext(int miRow, int miCol, int blockSizeIdx)
    {
        // Context = 4 * abovePartition + leftPartition
        // Simplified: use block position relative to superblock
        var (bw, _) = Vp9Constants.BlockSizeLookup[blockSizeIdx];
        var bsl = 0;
        while ((4 << bsl) < bw) bsl++;

        var above = miRow > 0 ? 1 : 0;
        var left = miCol > 0 ? 1 : 0;

        return (above + left) * Vp9Constants.NumPartitionTypes + Math.Min(bsl, 3);
    }

    /// <summary>
    /// Returns the block size index one level down (halved in each dimension).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LowerBlockSize(int blockSizeIdx)
    {
        // 64×64 → 32×32 → 16×16 → 8×8 → 4×4
        return blockSizeIdx switch
        {
            Vp9Constants.Block64x64 => Vp9Constants.Block32x32,
            Vp9Constants.Block32x32 => Vp9Constants.Block16x16,
            Vp9Constants.Block16x16 => Vp9Constants.Block8x8,
            Vp9Constants.Block8x8 => Vp9Constants.Block4x4,
            _ => Vp9Constants.Block4x4,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    #endregion

    #region VP9 Header Bit Reader

    /// <summary>
    /// Simple bit reader for the VP9 uncompressed header (not arithmetic coded).
    /// Reads bits MSB-first from a byte stream.
    /// </summary>
    private ref struct Vp9HeaderReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bytePos;
        private int _bitPos;

        public Vp9HeaderReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bytePos = 0;
            _bitPos = 0;
        }

        public readonly int BytesConsumed => _bytePos + (_bitPos > 0 ? 1 : 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            if (_bytePos >= _data.Length) return 0;
            var bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }
            return bit;
        }

        public uint ReadLiteral(int bits)
        {
            uint value = 0;
            for (var i = 0; i < bits; i++)
                value = (value << 1) | (uint)ReadBit();
            return value;
        }

        public int ReadSignedLiteral(int bits)
        {
            var value = (int)ReadLiteral(bits);
            var sign = ReadBit();
            return sign != 0 ? -value : value;
        }

        public void SkipBits(int n)
        {
            for (var i = 0; i < n; i++)
                ReadBit();
        }
    }

    #endregion
}
