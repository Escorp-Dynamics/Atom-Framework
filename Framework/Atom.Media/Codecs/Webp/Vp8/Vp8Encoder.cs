#pragma warning disable S109, S1450, S2325, S3776, CA1822, MA0038, MA0051, MA0182, IDE0045, IDE0047, IDE0048

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.Media.Codecs.Webp.Vp8;

namespace Atom.Media;

/// <summary>
/// VP8 lossy keyframe encoder per RFC 6386.
/// Encodes a VideoFrame (RGBA32) into a VP8 bitstream wrapped in a WebP container.
/// </summary>
internal sealed class Vp8Encoder
{
    // ── Public configuration ──

    /// <summary>Quality level 0–100 (maps to QP 127–0).</summary>
    public int Quality { get; set; } = 75;

    // ── Internal state ──

    private int width;
    private int height;
    private int mbWidth;
    private int mbHeight;
    private int yStride;
    private int uvStride;

    private byte[] yPlane = [];
    private byte[] uPlane = [];
    private byte[] vPlane = [];

    // Reconstructed planes for prediction context
    private byte[] yRec = [];
    private byte[] uRec = [];
    private byte[] vRec = [];

    /// <summary>
    /// Encodes a single frame as a VP8 keyframe inside a WebP container.
    /// </summary>
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten) =>
        Encode(frame, output, out bytesWritten, diagnostics: null);

    /// <summary>
    /// Encodes a single frame as a VP8 keyframe and optionally captures first-macroblock Y2 diagnostics.
    /// </summary>
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten, Vp8EncodeDiagnostics? diagnostics)
    {
        bytesWritten = 0;
        diagnostics?.Reset();
        width = frame.Width;
        height = frame.Height;

        if (width == 0 || height == 0)
        {
            return CodecResult.InvalidData;
        }

        mbWidth = (width + 15) >> 4;
        mbHeight = (height + 15) >> 4;
        yStride = mbWidth * 16;
        uvStride = mbWidth * 8;

        // Allocate YUV planes
        yPlane = new byte[yStride * mbHeight * 16];
        uPlane = new byte[uvStride * mbHeight * 8];
        vPlane = new byte[uvStride * mbHeight * 8];

        // Reconstructed planes (encoder needs to decode its own output for prediction)
        yRec = new byte[yPlane.Length];
        uRec = new byte[uPlane.Length];
        vRec = new byte[vPlane.Length];

        // Convert RGBA → YUV
        ConvertRgbaToYuv(frame);

        // Map quality to QP (quality 100 → qp 0, quality 0 → qp 127)
        var qp = Vp8Quantization.ClampQp(127 - ((Quality * 127) / 100));
        var dqm = Vp8Quantization.BuildDequantMatrix(qp, 0, 0, 0, 0, 0);

        // Allocate working buffers for bitstream
        var maxFirstPartSize = 256 + (mbWidth * mbHeight * 4);
        var maxTokenPartSize = mbWidth * mbHeight * 16 * 16 * 2;
        var firstPartBuf = new byte[maxFirstPartSize];
        var tokenPartBuf = new byte[maxTokenPartSize];

        var headerBd = new Vp8BoolEncoder(firstPartBuf);
        var tokenBd = new Vp8BoolEncoder(tokenPartBuf);

        // Write first-partition header bits
        WriteFrameHeader(ref headerBd, qp);

        // Encode macroblocks
        var residual = new short[16];
        var dctOut = new short[16];
        var y2Dct = new short[16];
        var y2Quant = new short[16];
        var yQuantBlocks = new short[16 * 16];
        var aboveYNonZero = new byte[mbWidth * 4];
        var aboveY2NonZero = new byte[mbWidth];
        var aboveUNonZero = new byte[mbWidth * 2];
        var aboveVNonZero = new byte[mbWidth * 2];

        for (var mbY = 0; mbY < mbHeight; mbY++)
        {
            var leftYNonZero = new byte[4];
            byte leftY2NonZero = 0;
            var leftUNonZero = new byte[2];
            var leftVNonZero = new byte[2];

            for (var mbX = 0; mbX < mbWidth; mbX++)
            {
                var yOff = (mbY * 16 * yStride) + (mbX * 16);
                var uvOff = (mbY * 8 * uvStride) + (mbX * 8);
                var hasAbove = mbY > 0;
                var hasLeft = mbX > 0;
                var captureCurrentMacroblock = diagnostics is not null && mbX == diagnostics.TargetMacroblockX && mbY == diagnostics.TargetMacroblockY;
                var currentMbYNonZero = new byte[16];
                var currentMbUNonZero = new byte[4];
                var currentMbVNonZero = new byte[4];

                // ── Choose best 16×16 Y prediction mode ──
                var bestYMode = ChooseBest16x16Mode(yOff, hasAbove, hasLeft);

                // ── Choose best 8×8 UV prediction mode ──
                var bestUvMode = ChooseBest8x8Mode(uvOff, hasAbove, hasLeft);

                if (captureCurrentMacroblock)
                {
                    diagnostics!.FirstMacroblockChosenYMode = bestYMode;
                    diagnostics.FirstMacroblockChosenUvMode = bestUvMode;
                    diagnostics.FirstMacroblockOriginalYTopLeft = yPlane[yOff];
                    diagnostics.FirstMacroblockOriginalUTopLeft = uPlane[uvOff];
                    diagnostics.FirstMacroblockOriginalVTopLeft = vPlane[uvOff];
                }

                // mb_no_coeff_skip is enabled in the frame header, so each macroblock must emit a skip bit.
                // This encoder currently reconstructs/encodes every macroblock, so the bit is always "not skipped".
                headerBd.EncodeBit(0, 128);

                // Write Y mode
                EncodeKfYMode(ref headerBd, bestYMode);
                // Write UV mode
                EncodeKfUvMode(ref headerBd, bestUvMode);

                // ── Apply prediction to reconstructed plane ──
                ApplyPrediction16x16(yRec, yStride, yOff, hasAbove, hasLeft, bestYMode);
                ApplyPrediction8x8(uRec, uvStride, uvOff, hasAbove, hasLeft, bestUvMode);
                ApplyPrediction8x8(vRec, uvStride, uvOff, hasAbove, hasLeft, bestUvMode);

                if (captureCurrentMacroblock)
                {
                    diagnostics!.FirstMacroblockPredictedYTopLeft = yRec[yOff];
                    diagnostics.FirstMacroblockPredictedUTopLeft = uRec[uvOff];
                    diagnostics.FirstMacroblockPredictedVTopLeft = vRec[uvOff];
                }

                // ── Encode Y subblocks (16 × 4×4 blocks) ──
                // Compute residuals and quantized AC blocks first. For non-BPred the token stream order is Y2, then 16 Y blocks.
                Array.Clear(y2Quant);
                Array.Clear(yQuantBlocks);

                for (var by = 0; by < 4; by++)
                {
                    for (var bx = 0; bx < 4; bx++)
                    {
                        var subOff = yOff + (by * 4 * yStride) + (bx * 4);

                        // Residual = original - prediction
                        ComputeResidual4x4(yPlane, yRec, subOff, yStride, residual);

                        // Forward DCT
                        Array.Clear(dctOut);
                        Vp8Dct.ForwardDct4x4(residual, dctOut, 4);

                        // Save DC for Y2 block
                        y2Dct[(by * 4) + bx] = dctOut[0];

                        if (captureCurrentMacroblock && by == 0 && bx == 0)
                        {
                            diagnostics!.FirstYBlockForwardDctDc = dctOut[0];
                        }

                        dctOut[0] = 0; // DC will be coded via Y2

                        // Quantize
                        Vp8Quantization.Quantize(dctOut, dqm.Y1DcDequant, dqm.Y1AcDequant);

                        var blockIndex = (by * 4) + bx;
                        currentMbYNonZero[blockIndex] = HasNonZeroCoefficients(dctOut, 1);
                        for (var coefficientIndex = 0; coefficientIndex < 16; coefficientIndex++)
                        {
                            yQuantBlocks[(blockIndex * 16) + coefficientIndex] = dctOut[coefficientIndex];
                        }
                    }
                }

                // ── Y2 block (Walsh-Hadamard of Y DC values) ──
                if (captureCurrentMacroblock)
                {
                    Array.Copy(y2Dct, diagnostics!.FirstMacroblockY2SourceDc, 16);
                }

                Vp8Dct.ForwardWht4x4(y2Dct, y2Quant);

                if (captureCurrentMacroblock)
                {
                    Array.Copy(y2Quant, diagnostics!.FirstMacroblockY2ForwardWht, 16);
                }

                Vp8Quantization.Quantize(y2Quant, dqm.Y2DcDequant, dqm.Y2AcDequant);

                if (captureCurrentMacroblock)
                {
                    Array.Copy(y2Quant, diagnostics!.FirstMacroblockY2Quantized, 16);
                }

                var currentMbY2NonZero = HasNonZeroCoefficients(y2Quant, 0);
                var y2InitialContext = aboveY2NonZero[mbX] + leftY2NonZero;
                EncodeBlock(ref tokenBd, y2Quant, 1, 0, y2InitialContext);

                for (var blockIndex = 0; blockIndex < 16; blockIndex++)
                {
                    var by = blockIndex >> 2;
                    var bx = blockIndex & 3;
                    for (var coefficientIndex = 0; coefficientIndex < 16; coefficientIndex++)
                    {
                        dctOut[coefficientIndex] = yQuantBlocks[(blockIndex * 16) + coefficientIndex];
                    }

                    var aboveContext = by > 0
                        ? currentMbYNonZero[((by - 1) * 4) + bx]
                        : aboveYNonZero[(mbX * 4) + bx];
                    var leftContext = bx > 0
                        ? currentMbYNonZero[(by * 4) + bx - 1]
                        : leftYNonZero[by];
                    EncodeBlock(ref tokenBd, dctOut, 0, 1, aboveContext + leftContext);
                }

                // Reconstruct Y2 and inject DCs back
                var y2Rec = new short[16];
                Array.Copy(y2Quant, y2Rec, 16);
                Vp8Quantization.Dequantize(y2Rec, dqm.Y2DcDequant, dqm.Y2AcDequant);
                var y2Inv = new short[16];
                Vp8Dct.InverseWht4x4(y2Rec, y2Inv);

                if (captureCurrentMacroblock)
                {
                    Array.Copy(y2Inv, diagnostics!.FirstMacroblockY2InverseWht, 16);
                }

                // Now reconstruct each Y subblock with the recovered DC
                for (var by = 0; by < 4; by++)
                {
                    for (var bx = 0; bx < 4; bx++)
                    {
                        var subOff = yOff + (by * 4 * yStride) + (bx * 4);

                        // Re-compute residual (needed again for proper DC injection)
                        ComputeResidual4x4(yPlane, yRec, subOff, yStride, residual);
                        Array.Clear(dctOut);
                        Vp8Dct.ForwardDct4x4(residual, dctOut, 4);
                        dctOut[0] = 0;
                        Vp8Quantization.Quantize(dctOut, dqm.Y1DcDequant, dqm.Y1AcDequant);
                        Vp8Quantization.Dequantize(dctOut, dqm.Y1DcDequant, dqm.Y1AcDequant);

                        // Inject reconstructed Y2 DC
                        dctOut[0] = y2Inv[(by * 4) + bx];

                        // IDCT adds to prediction already in yRec
                        Vp8Dct.InverseDct4x4(dctOut, yRec.AsSpan(subOff), yStride);
                    }
                }

                // ── Encode U subblocks (4 × 4×4 blocks) ──
                EncodeUvPlane(ref tokenBd, uPlane, uRec, uvStride, uvOff, ref dqm,
                    aboveUNonZero, leftUNonZero, currentMbUNonZero, mbX,
                    captureCurrentMacroblock ? diagnostics : null, isVPlane: false);

                // ── Encode V subblocks (4 × 4×4 blocks) ──
                EncodeUvPlane(ref tokenBd, vPlane, vRec, uvStride, uvOff, ref dqm,
                    aboveVNonZero, leftVNonZero, currentMbVNonZero, mbX,
                    captureCurrentMacroblock ? diagnostics : null, isVPlane: true);

                for (var i = 0; i < 4; i++)
                {
                    aboveYNonZero[(mbX * 4) + i] = currentMbYNonZero[12 + i];
                    leftYNonZero[i] = currentMbYNonZero[(i * 4) + 3];
                }

                aboveY2NonZero[mbX] = currentMbY2NonZero;
                leftY2NonZero = currentMbY2NonZero;

                for (var i = 0; i < 2; i++)
                {
                    aboveUNonZero[(mbX * 2) + i] = currentMbUNonZero[2 + i];
                    leftUNonZero[i] = currentMbUNonZero[(i * 2) + 1];
                    aboveVNonZero[(mbX * 2) + i] = currentMbVNonZero[2 + i];
                    leftVNonZero[i] = currentMbVNonZero[(i * 2) + 1];
                }
            }
        }

        // Flush encoders
        headerBd.Flush();
        tokenBd.Flush();

        var firstPartSize = headerBd.BytesWritten;
        var tokenPartSize = tokenBd.BytesWritten;

        // ── Write RIFF/WEBP container ──
        // Layout: RIFF(12) + VP8_chunk_header(8) + frame_tag(3) + sync_code(3) + size_info(4) + firstPart + tokenPart
        var vp8DataSize = 3 + 3 + 4 + firstPartSize + tokenPartSize;
        var totalSize = 12 + 8 + vp8DataSize + (vp8DataSize & 1); // RIFF alignment

        if (output.Length < totalSize)
        {
            bytesWritten = 0;
            return CodecResult.OutputBufferTooSmall;
        }

        var pos = 0;

        // RIFF header
        output[pos++] = (byte)'R';
        output[pos++] = (byte)'I';
        output[pos++] = (byte)'F';
        output[pos++] = (byte)'F';
        WriteUInt32Le(output, pos, (uint)(totalSize - 8));
        pos += 4;
        output[pos++] = (byte)'W';
        output[pos++] = (byte)'E';
        output[pos++] = (byte)'B';
        output[pos++] = (byte)'P';

        // VP8 chunk header
        output[pos++] = (byte)'V';
        output[pos++] = (byte)'P';
        output[pos++] = (byte)'8';
        output[pos++] = (byte)' ';
        WriteUInt32Le(output, pos, (uint)vp8DataSize);
        pos += 4;

        // Frame tag (3 bytes): keyframe=0, version=0, show=1, firstPartSize
        var frameTag = (firstPartSize << 5) | (1 << 4) | 0; // show=1, keyframe(bit0=0)
        output[pos++] = (byte)(frameTag & 0xFF);
        output[pos++] = (byte)((frameTag >> 8) & 0xFF);
        output[pos++] = (byte)((frameTag >> 16) & 0xFF);

        // Sync code
        output[pos++] = Vp8Constants.SyncCode0;
        output[pos++] = Vp8Constants.SyncCode1;
        output[pos++] = Vp8Constants.SyncCode2;

        // Width + horizontal scale (2 bytes), height + vertical scale (2 bytes)
        output[pos++] = (byte)(width & 0xFF);
        output[pos++] = (byte)((width >> 8) & 0x3F); // scale = 0
        output[pos++] = (byte)(height & 0xFF);
        output[pos++] = (byte)((height >> 8) & 0x3F);

        // First partition data
        firstPartBuf.AsSpan(0, firstPartSize).CopyTo(output[pos..]);
        pos += firstPartSize;

        // Token partition data (single partition, no size prefix needed)
        tokenPartBuf.AsSpan(0, tokenPartSize).CopyTo(output[pos..]);
        pos += tokenPartSize;

        // Pad to even size if needed
        if ((vp8DataSize & 1) != 0)
        {
            output[pos++] = 0;
        }

        bytesWritten = pos;
        return CodecResult.Success;
    }

    internal static int EncodeBlockForDiagnostics(ReadOnlySpan<short> coeffs, int blockType, int firstCoeff, Span<byte> output)
    {
        var copy = coeffs.ToArray();
        var encoder = new Vp8BoolEncoder(output);
        EncodeBlock(ref encoder, copy, blockType, firstCoeff);
        encoder.Flush();
        return encoder.BytesWritten;
    }

    // ── RGBA → YUV (BT.601) conversion ──

    private void ConvertRgbaToYuv(in ReadOnlyVideoFrame frame)
    {
        var bpp = frame.PixelFormat == VideoPixelFormat.Rgb24 ? 3 : 4;

        for (var y = 0; y < height; y++)
        {
            var row = frame.PackedData.GetRow(y);
            var yRowOff = y * yStride;
            var uvRowOff = (y >> 1) * uvStride;

            for (var x = 0; x < width; x++)
            {
                var px = x * bpp;
                var r = row[px];
                var g = row[px + 1];
                var b = row[px + 2];

                // BT.601: Y = 0.299R + 0.587G + 0.114B
                var yVal = ((66 * r) + (129 * g) + (25 * b) + 128) >> 8;
                yPlane[yRowOff + x] = (byte)Math.Clamp(yVal + 16, 0, 255);

                // Subsample UV on even rows/columns
                if ((y & 1) == 0 && (x & 1) == 0)
                {
                    var uVal = ((-38 * r) - (74 * g) + (112 * b) + 128) >> 8;
                    var vVal = ((112 * r) - (94 * g) - (18 * b) + 128) >> 8;
                    uPlane[uvRowOff + (x >> 1)] = (byte)Math.Clamp(uVal + 128, 0, 255);
                    vPlane[uvRowOff + (x >> 1)] = (byte)Math.Clamp(vVal + 128, 0, 255);
                }
            }
        }
    }

    // ── Prediction mode selection ──

    private int ChooseBest16x16Mode(int yOff, bool hasAbove, bool hasLeft)
    {
        var bestMode = Vp8Constants.DcPred;
        var bestSad = long.MaxValue;

        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];
        Span<byte> pred = stackalloc byte[16 * 16];

        if (hasAbove)
        {
            yRec.AsSpan(yOff - yStride, 16).CopyTo(above);
        }

        if (hasLeft)
        {
            for (var i = 0; i < 16; i++)
            {
                left[i] = yRec[yOff + (i * yStride) - 1];
            }
        }

        var aboveLeft = (hasAbove && hasLeft) ? yRec[yOff - yStride - 1] : (byte)128;

        // DC always available
        Vp8Prediction.Predict16x16Dc(above, left, hasAbove, hasLeft, pred, 16);
        var sad = ComputeSad16x16(yPlane, yOff, yStride, pred, 16);
        if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.DcPred; }

        // V requires above
        if (hasAbove)
        {
            Vp8Prediction.Predict16x16V(above, pred, 16);
            sad = ComputeSad16x16(yPlane, yOff, yStride, pred, 16);
            if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.VPred; }
        }

        // H requires left
        if (hasLeft)
        {
            Vp8Prediction.Predict16x16H(left, pred, 16);
            sad = ComputeSad16x16(yPlane, yOff, yStride, pred, 16);
            if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.HPred; }
        }

        // TM requires both
        if (hasAbove && hasLeft)
        {
            Vp8Prediction.Predict16x16Tm(above, left, aboveLeft, pred, 16);
            sad = ComputeSad16x16(yPlane, yOff, yStride, pred, 16);
            if (sad < bestSad) { bestMode = Vp8Constants.TmPred; }
        }

        return bestMode;
    }

    private int ChooseBest8x8Mode(int uvOff, bool hasAbove, bool hasLeft)
    {
        var bestMode = Vp8Constants.DcPred;
        var bestSad = long.MaxValue;

        Span<byte> above = stackalloc byte[8];
        Span<byte> left = stackalloc byte[8];
        Span<byte> pred = stackalloc byte[8 * 8];

        if (hasAbove)
        {
            uRec.AsSpan(uvOff - uvStride, 8).CopyTo(above);
        }

        if (hasLeft)
        {
            for (var i = 0; i < 8; i++)
            {
                left[i] = uRec[uvOff + (i * uvStride) - 1];
            }
        }

        var aboveLeft = (hasAbove && hasLeft) ? uRec[uvOff - uvStride - 1] : (byte)128;

        Vp8Prediction.Predict8x8Dc(above, left, hasAbove, hasLeft, pred, 8);
        var sad = ComputeSad8x8(uPlane, uvOff, uvStride, pred, 8);
        if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.DcPred; }

        if (hasAbove)
        {
            Vp8Prediction.Predict8x8V(above, pred, 8);
            sad = ComputeSad8x8(uPlane, uvOff, uvStride, pred, 8);
            if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.VPred; }
        }

        if (hasLeft)
        {
            Vp8Prediction.Predict8x8H(left, pred, 8);
            sad = ComputeSad8x8(uPlane, uvOff, uvStride, pred, 8);
            if (sad < bestSad) { bestSad = sad; bestMode = Vp8Constants.HPred; }
        }

        if (hasAbove && hasLeft)
        {
            Vp8Prediction.Predict8x8Tm(above, left, aboveLeft, pred, 8);
            sad = ComputeSad8x8(uPlane, uvOff, uvStride, pred, 8);
            if (sad < bestSad) { bestMode = Vp8Constants.TmPred; }
        }

        return bestMode;
    }

    // ── SAD computation ──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ComputeSad16x16(byte[] original, int origOff, int origStride,
        ReadOnlySpan<byte> prediction, int predStride)
    {
        var sad = 0L;
        for (var y = 0; y < 16; y++)
        {
            var origRow = origOff + (y * origStride);
            var predRow = y * predStride;
            for (var x = 0; x < 16; x++)
            {
                sad += Math.Abs(original[origRow + x] - prediction[predRow + x]);
            }
        }

        return sad;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ComputeSad8x8(byte[] original, int origOff, int origStride,
        ReadOnlySpan<byte> prediction, int predStride)
    {
        var sad = 0L;
        for (var y = 0; y < 8; y++)
        {
            var origRow = origOff + (y * origStride);
            var predRow = y * predStride;
            for (var x = 0; x < 8; x++)
            {
                sad += Math.Abs(original[origRow + x] - prediction[predRow + x]);
            }
        }

        return sad;
    }

    // ── Prediction application ──

    private static void ApplyPrediction16x16(byte[] dst, int stride, int off,
        bool hasAbove, bool hasLeft, int mode)
    {
        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];

        if (hasAbove) { dst.AsSpan(off - stride, 16).CopyTo(above); }
        if (hasLeft) { for (var i = 0; i < 16; i++) { left[i] = dst[off + (i * stride) - 1]; } }
        var al = (hasAbove && hasLeft) ? dst[off - stride - 1] : (byte)128;

        switch (mode)
        {
            case Vp8Constants.DcPred:
                Vp8Prediction.Predict16x16Dc(above, left, hasAbove, hasLeft, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.VPred:
                Vp8Prediction.Predict16x16V(above, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.HPred:
                Vp8Prediction.Predict16x16H(left, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.TmPred:
                Vp8Prediction.Predict16x16Tm(above, left, al, dst.AsSpan(off), stride);
                break;
        }
    }

    private static void ApplyPrediction8x8(byte[] dst, int stride, int off,
        bool hasAbove, bool hasLeft, int mode)
    {
        Span<byte> above = stackalloc byte[8];
        Span<byte> left = stackalloc byte[8];

        if (hasAbove) { dst.AsSpan(off - stride, 8).CopyTo(above); }
        if (hasLeft) { for (var i = 0; i < 8; i++) { left[i] = dst[off + (i * stride) - 1]; } }
        var al = (hasAbove && hasLeft) ? dst[off - stride - 1] : (byte)128;

        switch (mode)
        {
            case Vp8Constants.DcPred:
                Vp8Prediction.Predict8x8Dc(above, left, hasAbove, hasLeft, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.VPred:
                Vp8Prediction.Predict8x8V(above, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.HPred:
                Vp8Prediction.Predict8x8H(left, dst.AsSpan(off), stride);
                break;
            case Vp8Constants.TmPred:
                Vp8Prediction.Predict8x8Tm(above, left, al, dst.AsSpan(off), stride);
                break;
        }
    }

    // ── Residual computation ──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeResidual4x4(byte[] original, byte[] prediction,
        int offset, int stride, short[] residual)
    {
        for (var y = 0; y < 4; y++)
        {
            var off = offset + (y * stride);
            for (var x = 0; x < 4; x++)
            {
                residual[(y * 4) + x] = (short)(original[off + x] - prediction[off + x]);
            }
        }
    }

    // ── UV plane encoding ──

    private static void EncodeUvPlane(ref Vp8BoolEncoder tokenBd, byte[] origPlane, byte[] recPlane,
        int stride, int baseOff, ref Vp8QuantMatrix dqm, byte[] aboveNonZero, byte[] leftNonZero,
        byte[] currentMbNonZero, int mbX, Vp8EncodeDiagnostics? diagnostics, bool isVPlane)
    {
        var residual = new short[16];
        var dctOut = new short[16];

        for (var by = 0; by < 2; by++)
        {
            for (var bx = 0; bx < 2; bx++)
            {
                var subOff = baseOff + (by * 4 * stride) + (bx * 4);

                ComputeResidual4x4(origPlane, recPlane, subOff, stride, residual);
                Array.Clear(dctOut);
                Vp8Dct.ForwardDct4x4(residual, dctOut, 4);

                if (diagnostics is not null && by == 0 && bx == 0)
                {
                    if (isVPlane)
                    {
                        diagnostics.FirstVBlockForwardDctDc = dctOut[0];
                    }
                    else
                    {
                        diagnostics.FirstUBlockForwardDctDc = dctOut[0];
                    }
                }

                Vp8Quantization.Quantize(dctOut, dqm.UvDcDequant, dqm.UvAcDequant);
                currentMbNonZero[(by * 2) + bx] = HasNonZeroCoefficients(dctOut, 0);

                if (diagnostics is not null && by == 0 && bx == 0)
                {
                    for (var coefficientIndex = 0; coefficientIndex < 16; coefficientIndex++)
                    {
                        if (isVPlane)
                        {
                            diagnostics.FirstVBlockQuantized[coefficientIndex] = dctOut[coefficientIndex];
                        }
                        else
                        {
                            diagnostics.FirstUBlockQuantized[coefficientIndex] = dctOut[coefficientIndex];
                        }
                    }
                }

                var aboveContext = by > 0
                    ? currentMbNonZero[((by - 1) * 2) + bx]
                    : aboveNonZero[(mbX * 2) + bx];
                var leftContext = bx > 0
                    ? currentMbNonZero[(by * 2) + bx - 1]
                    : leftNonZero[by];
                EncodeBlock(ref tokenBd, dctOut, 2, 0, aboveContext + leftContext);

                // Reconstruct
                Vp8Quantization.Dequantize(dctOut, dqm.UvDcDequant, dqm.UvAcDequant);
                Vp8Dct.InverseDct4x4(dctOut, recPlane.AsSpan(subOff), stride);
            }
        }
    }

    // ── First partition: frame header encoding ──

    private static void WriteFrameHeader(ref Vp8BoolEncoder bd, int qp)
    {
        // Color space (0 = YCbCr BT.601)
        bd.EncodeLiteral(0, 1);
        // Clamping type (0 = clamping required)
        bd.EncodeLiteral(0, 1);

        // Segmentation: disabled
        bd.EncodeBit(0, 128); // segment.enabled = false

        // Filter header
        bd.EncodeBit(0, 128); // normal_filter = false (simple filter)
        bd.EncodeLiteral(0, 6); // filter_level = 0 (no loop filter for simplicity)
        bd.EncodeLiteral(0, 3); // sharpness = 0
        bd.EncodeBit(0, 128); // adjust_enabled = false

        // Partitions: 1 partition (log2_partitions = 0)
        bd.EncodeLiteral(0, 2);

        // Quantization
        bd.EncodeLiteral((uint)qp, 7); // base_qp
        // All deltas = 0 (no flag bits needed — each delta has a "present" bit)
        bd.EncodeBit(0, 128); // y1_dc_delta present = no
        bd.EncodeBit(0, 128); // y2_dc_delta present = no
        bd.EncodeBit(0, 128); // y2_ac_delta present = no
        bd.EncodeBit(0, 128); // uv_dc_delta present = no
        bd.EncodeBit(0, 128); // uv_ac_delta present = no

        // Keyframe reference header still carries refresh_entropy.
        bd.EncodeBit(1, 128); // refresh_entropy = true

        // Coefficient probability update: no updates (use defaults)
        // Per RFC 6386 §13.4: for each [type][band][ctx][node], use CoeffUpdateProbs
        // to signal whether a new probability follows
        for (var t = 0; t < Vp8Constants.BlockTypes; t++)
        {
            for (var b = 0; b < Vp8Constants.CoeffBands; b++)
            {
                for (var c = 0; c < Vp8Constants.PrevCoeffContexts; c++)
                {
                    for (var n = 0; n < Vp8Constants.EntropyNodes; n++)
                    {
                        // Signal "no update" for each coefficient probability
                        bd.EncodeBit(0, Vp8Constants.CoeffUpdateProbs[t, b, c, n]);
                    }
                }
            }
        }

        // Skip coeff flag: enabled
        bd.EncodeBit(1, 128); // mb_no_coeff_skip = true
        bd.EncodeLiteral(128, 8); // prob_skip_false = 128 (50/50, we'll encode skip properly)
    }

    // ── Mode encoding (keyframe) ──

    private static void EncodeKfYMode(ref Vp8BoolEncoder bd, int mode)
    {
        // Keyframe Y mode tree: fixed probabilities [145, 156, 163, 128]
        // The keyframe tree differs from the generic Y mode tree and must match RFC kf_y_mode_tree.
        ReadOnlySpan<byte> probs = [145, 156, 163, 128];

        EncodeTree(ref bd, Vp8Constants.KfYModeTree, probs, mode);
    }

    private static void EncodeKfUvMode(ref Vp8BoolEncoder bd, int mode)
    {
        // Keyframe UV mode tree: fixed probabilities [142, 114, 183]
        ReadOnlySpan<byte> probs = [142, 114, 183];

        EncodeTree(ref bd, Vp8Constants.UvModeTree, probs, mode);
    }

    /// <summary>
    /// Encodes a symbol using a binary tree and per-node probabilities.
    /// This is the encoding counterpart to DecodeTree in the decoder.
    /// </summary>
    private static void EncodeTree(ref Vp8BoolEncoder bd, ReadOnlySpan<sbyte> tree,
        ReadOnlySpan<byte> probs, int symbol)
    {
        var i = 0;

        while (true)
        {
            // At node i: tree[i] is left child, tree[i+1] is right child
            // We need to determine if symbol is in left or right subtree
            var leftChild = tree[i];
            var rightChild = tree[i + 1];

            if (leftChild <= 0 && -leftChild == symbol)
            {
                // Symbol is the left leaf → encode bit 0
                bd.EncodeBit(0, probs[i >> 1]);
                return;
            }

            if (rightChild <= 0 && -rightChild == symbol)
            {
                // Symbol is the right leaf → encode bit 1
                bd.EncodeBit(1, probs[i >> 1]);
                return;
            }

            // Symbol must be in one of the subtrees
            if (leftChild > 0 && TreeContains(tree, leftChild, symbol))
            {
                // Go left
                bd.EncodeBit(0, probs[i >> 1]);
                i = leftChild;
            }
            else
            {
                // Go right
                bd.EncodeBit(1, probs[i >> 1]);
                i = rightChild;
            }
        }
    }

    /// <summary>
    /// Checks if a symbol exists as a leaf in a subtree rooted at node index.
    /// </summary>
    private static bool TreeContains(ReadOnlySpan<sbyte> tree, int nodeIndex, int symbol)
    {
        var left = tree[nodeIndex];
        var right = tree[nodeIndex + 1];

        if (left <= 0 && -left == symbol) { return true; }
        if (right <= 0 && -right == symbol) { return true; }
        if (left > 0 && TreeContains(tree, left, symbol)) { return true; }
        if (right > 0 && TreeContains(tree, right, symbol)) { return true; }

        return false;
    }

    // ── Token/coefficient encoding ──

    /// <summary>
    /// Encodes a 4×4 block of quantized coefficients using the VP8 token system.
    /// </summary>
    private static void EncodeBlock(ref Vp8BoolEncoder bd, short[] coeffs,
        int blockType, int firstCoeff, int initialContext = 0)
    {
        // Find last non-zero coefficient
        var last = -1;
        for (var i = 15; i >= firstCoeff; i--)
        {
            var zigzag = Vp8Constants.Zigzag[i];
            if (coeffs[zigzag] != 0)
            {
                last = i;
                break;
            }
        }

        if (last < firstCoeff)
        {
            // All zero — encode EOB
            EncodeCoeffToken(ref bd, 0, blockType, firstCoeff, 0);
            return;
        }

        var ctx = initialContext;

        for (var i = firstCoeff; i < 16; i++)
        {
            var band = Vp8Constants.CoeffBandIndex[i];
            var zigzag = Vp8Constants.Zigzag[i];
            var value = coeffs[zigzag];

            if (i > last)
            {
                // EOB
                EncodeCoeffToken(ref bd, 0, blockType, band, ctx);
                return;
            }

            if (value == 0)
            {
                // Zero token
                EncodeCoeffToken(ref bd, 1, blockType, band, ctx);
                ctx = 0;
                continue;
            }

            var absValue = Math.Abs(value);

            if (absValue == 1)
            {
                EncodeCoeffToken(ref bd, 2, blockType, band, ctx);
            }
            else if (absValue == 2)
            {
                EncodeCoeffToken(ref bd, 3, blockType, band, ctx);
            }
            else if (absValue == 3)
            {
                EncodeCoeffToken(ref bd, 4, blockType, band, ctx);
            }
            else if (absValue == 4)
            {
                EncodeCoeffToken(ref bd, 5, blockType, band, ctx);
            }
            else
            {
                // Category encoding
                int token;
                int baseVal;
                ReadOnlySpan<byte> catProbs;

                if (absValue <= 6)
                {
                    token = 6; baseVal = 5; catProbs = Vp8Constants.ProbsCat1;
                }
                else if (absValue <= 10)
                {
                    token = 7; baseVal = 7; catProbs = Vp8Constants.ProbsCat2;
                }
                else if (absValue <= 18)
                {
                    token = 8; baseVal = 11; catProbs = Vp8Constants.ProbsCat3;
                }
                else if (absValue <= 34)
                {
                    token = 9; baseVal = 19; catProbs = Vp8Constants.ProbsCat4;
                }
                else if (absValue <= 66)
                {
                    token = 10; baseVal = 35; catProbs = Vp8Constants.ProbsCat5;
                }
                else
                {
                    token = 11; baseVal = 67; catProbs = Vp8Constants.ProbsCat6;
                }

                EncodeCoeffToken(ref bd, token, blockType, band, ctx);

                // Encode extra bits
                var extra = absValue - baseVal;
                for (var j = 0; j < catProbs.Length; j++)
                {
                    bd.EncodeBit((extra >> (catProbs.Length - 1 - j)) & 1, catProbs[j]);
                }
            }

            // Sign bit
            bd.EncodeBit(value < 0 ? 1 : 0, 128);

            ctx = absValue == 1 ? 1 : 2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte HasNonZeroCoefficients(ReadOnlySpan<short> coeffs, int firstCoeff)
    {
        for (var i = firstCoeff; i < coeffs.Length; i++)
        {
            if (coeffs[i] != 0)
            {
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Encodes a single coefficient token using the default probability tables.
    /// This mirrors DecodeCoeffToken in the decoder.
    /// </summary>
    private static void EncodeCoeffToken(ref Vp8BoolEncoder bd, int token,
        int blockType, int band, int ctx)
    {
        var probs = MemoryMarshal.CreateReadOnlySpan(
            ref Vp8Constants.DefaultCoeffProbs[blockType, band, ctx, 0],
            Vp8Constants.EntropyNodes);

        switch (token)
        {
            case 0: // EOB
                bd.EncodeBit(0, probs[0]);
                break;
            case 1: // ZERO
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(0, probs[1]);
                break;
            case 2: // ONE
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(0, probs[2]);
                break;
            case 3: // TWO
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(0, probs[3]);
                bd.EncodeBit(0, probs[4]);
                break;
            case 4: // THREE
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(0, probs[3]);
                bd.EncodeBit(1, probs[4]);
                bd.EncodeBit(0, probs[5]);
                break;
            case 5: // FOUR
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(0, probs[3]);
                bd.EncodeBit(1, probs[4]);
                bd.EncodeBit(1, probs[5]);
                break;
            case 6: // CAT1
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(0, probs[6]);
                bd.EncodeBit(0, probs[7]);
                break;
            case 7: // CAT2
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(0, probs[6]);
                bd.EncodeBit(1, probs[7]);
                break;
            case 8: // CAT3
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(1, probs[6]);
                bd.EncodeBit(0, probs[8]);
                bd.EncodeBit(0, probs[9]);
                break;
            case 9: // CAT4
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(1, probs[6]);
                bd.EncodeBit(0, probs[8]);
                bd.EncodeBit(1, probs[9]);
                break;
            case 10: // CAT5
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(1, probs[6]);
                bd.EncodeBit(1, probs[8]);
                bd.EncodeBit(0, probs[10]);
                break;
            case 11: // CAT6
                bd.EncodeBit(1, probs[0]);
                bd.EncodeBit(1, probs[1]);
                bd.EncodeBit(1, probs[2]);
                bd.EncodeBit(1, probs[3]);
                bd.EncodeBit(1, probs[6]);
                bd.EncodeBit(1, probs[8]);
                bd.EncodeBit(1, probs[10]);
                break;
        }
    }

    // ── Utility ──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt32Le(Span<byte> buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

internal sealed class Vp8EncodeDiagnostics
{
    public int TargetMacroblockX { get; set; }

    public int TargetMacroblockY { get; set; }

    public int FirstMacroblockChosenYMode { get; set; }

    public int FirstMacroblockChosenUvMode { get; set; }

    public byte FirstMacroblockOriginalYTopLeft { get; set; }

    public byte FirstMacroblockOriginalUTopLeft { get; set; }

    public byte FirstMacroblockOriginalVTopLeft { get; set; }

    public byte FirstMacroblockPredictedYTopLeft { get; set; }

    public byte FirstMacroblockPredictedUTopLeft { get; set; }

    public byte FirstMacroblockPredictedVTopLeft { get; set; }

    public short FirstYBlockForwardDctDc { get; set; }

    public short FirstUBlockForwardDctDc { get; set; }

    public short FirstVBlockForwardDctDc { get; set; }

    public short[] FirstMacroblockY2SourceDc { get; } = new short[16];

    public short[] FirstMacroblockY2ForwardWht { get; } = new short[16];

    public short[] FirstMacroblockY2Quantized { get; } = new short[16];

    public short[] FirstMacroblockY2InverseWht { get; } = new short[16];

    public short[] FirstUBlockQuantized { get; } = new short[16];

    public short[] FirstVBlockQuantized { get; } = new short[16];

    public void Reset()
    {
        FirstMacroblockChosenYMode = 0;
        FirstMacroblockChosenUvMode = 0;
        FirstMacroblockOriginalYTopLeft = 0;
        FirstMacroblockOriginalUTopLeft = 0;
        FirstMacroblockOriginalVTopLeft = 0;
        FirstMacroblockPredictedYTopLeft = 0;
        FirstMacroblockPredictedUTopLeft = 0;
        FirstMacroblockPredictedVTopLeft = 0;
        FirstYBlockForwardDctDc = 0;
        FirstUBlockForwardDctDc = 0;
        FirstVBlockForwardDctDc = 0;
        Array.Clear(FirstMacroblockY2SourceDc);
        Array.Clear(FirstMacroblockY2ForwardWht);
        Array.Clear(FirstMacroblockY2Quantized);
        Array.Clear(FirstMacroblockY2InverseWht);
        Array.Clear(FirstUBlockQuantized);
        Array.Clear(FirstVBlockQuantized);
    }
}
