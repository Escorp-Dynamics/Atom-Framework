#pragma warning disable CA1814

using System.Runtime.InteropServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 frame header parsed from bitstream. RFC 6386 §9.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Vp8FrameHeader
{
    /// <summary>True for keyframe, false for interframe.</summary>
    public bool IsKeyFrame;

    /// <summary>VP8 version number (0–3).</summary>
    public int Version;

    /// <summary>True if the frame is meant to be displayed.</summary>
    public bool ShowFrame;

    /// <summary>Size of the first data partition in bytes.</summary>
    public int FirstPartSize;

    /// <summary>Frame width in pixels.</summary>
    public int Width;

    /// <summary>Frame height in pixels.</summary>
    public int Height;

    /// <summary>Horizontal scale (0–3).</summary>
    public int HorizontalScale;

    /// <summary>Vertical scale (0–3).</summary>
    public int VerticalScale;

    /// <summary>Color space: 0 = YCbCr (BT.601), 1 = reserved.</summary>
    public int ColorSpace;

    /// <summary>Clamping type: 0 = clamping required, 1 = no clamping.</summary>
    public int ClampingType;
}

/// <summary>
/// VP8 segment-level parameters (RFC 6386 §9.3).
/// </summary>
internal struct Vp8SegmentHeader
{
    /// <summary>Whether segmentation is enabled.</summary>
    public bool Enabled;

    /// <summary>Whether the segment map is being updated.</summary>
    public bool UpdateMap;

    /// <summary>Whether segment feature data is being updated.</summary>
    public bool UpdateData;

    /// <summary>Absolute (true) or delta (false) mode for segment values.</summary>
    public bool AbsoluteDelta;

    /// <summary>Quantizer value per segment [0..3].</summary>
    public int[] QuantizerLevel;

    /// <summary>Loop filter level per segment [0..3].</summary>
    public int[] FilterLevel;

    /// <summary>Segment map tree probabilities [0..2].</summary>
    public byte[] TreeProbs;
}

/// <summary>
/// VP8 loop filter header (RFC 6386 §9.4).
/// </summary>
internal struct Vp8FilterHeader
{
    /// <summary>True = normal filter, false = simple filter. Bitstream filter_type uses the inverse flag (0 = normal, 1 = simple).</summary>
    public bool UseNormalFilter;

    /// <summary>Base loop filter level (0–63).</summary>
    public int Level;

    /// <summary>Sharpness level (0–7).</summary>
    public int Sharpness;

    /// <summary>Whether loop filter adjustments are enabled per ref/mode.</summary>
    public bool AdjustEnabled;

    /// <summary>Whether the filter level deltas are being updated.</summary>
    public bool DeltaUpdate;

    /// <summary>Reference frame filter level deltas [0..3]: INTRA, LAST, GOLDEN, ALTREF.</summary>
    public int[] RefDelta;

    /// <summary>Encoding mode filter level deltas [0..3].</summary>
    public int[] ModeDelta;
}

/// <summary>
/// Decoded information for a single macroblock, used during decoding.
/// </summary>
internal struct Vp8Macroblock
{
    /// <summary>Segment index (0–3).</summary>
    public byte Segment;

    /// <summary>Y intra prediction mode (DC, V, H, TM, B_PRED).</summary>
    public byte YMode;

    /// <summary>UV intra prediction mode (DC, V, H, TM).</summary>
    public byte UvMode;

    /// <summary>4×4 subblock intra modes for B_PRED (16 values, one per Y subblock).</summary>
    public byte[] SubblockModes;

    /// <summary>Whether this macroblock is skipped (all zero residuals).</summary>
    public bool IsSkip;

    /// <summary>Whether inner subblock edges must be filtered for this macroblock.</summary>
    public bool HasFilterSubblocks;

    /// <summary>Effective loop filter level for this macroblock (after segment/ref adjustments).</summary>
    public int FilterLevel;
}

/// <summary>
/// VP8 token partition info (RFC 6386 §9.5).
/// </summary>
internal struct Vp8PartitionInfo
{
    /// <summary>Number of DCT coefficient partitions (1, 2, 4, or 8).</summary>
    public int Count;

    /// <summary>Partition sizes in bytes.</summary>
    public int[] Sizes;
}

/// <summary>
/// Complete VP8 frame context for decoding.
/// </summary>
internal sealed class Vp8FrameContext
{
    public Vp8FrameHeader Header;
    public Vp8SegmentHeader Segment;
    public Vp8FilterHeader Filter;
    public Vp8PartitionInfo Partitions;

    /// <summary>Base quantizer index (0–127).</summary>
    public int BaseQp;

    /// <summary>Quantizer deltas for y1_dc, y2_dc, y2_ac, uv_dc, uv_ac.</summary>
    public int Y1DcDelta;
    public int Y2DcDelta;
    public int Y2AcDelta;
    public int UvDcDelta;
    public int UvAcDelta;

    /// <summary>Dequantization matrices per segment.</summary>
    public Vp8QuantMatrix[] DequantMatrices = new Vp8QuantMatrix[Vp8Constants.MaxMbSegments];

    /// <summary>Coefficient entropy probabilities [4][8][3][11].</summary>
    public byte[,,,] CoeffProbs = new byte[Vp8Constants.BlockTypes, Vp8Constants.CoeffBands,
        Vp8Constants.PrevCoeffContexts, Vp8Constants.EntropyNodes];

    /// <summary>Width in macroblocks.</summary>
    public int MbWidth;

    /// <summary>Height in macroblocks.</summary>
    public int MbHeight;

    /// <summary>Whether this is a keyframe (cached from header for convenience).</summary>
    public bool IsKeyFrame;

    /// <summary>MV probabilities [2][19] for row/col components.</summary>
    public byte[,] MvProbs = new byte[2, Vp8Constants.MvProbCount];

    /// <summary>Y mode probabilities (inter frames).</summary>
    public byte[] YModeProbs = new byte[Vp8Constants.NumYModes - 1];

    /// <summary>UV mode probabilities.</summary>
    public byte[] UvModeProbs = new byte[Vp8Constants.NumUvModes - 1];

    /// <summary>
    /// Initializes default probability tables from constants.
    /// </summary>
    public void InitDefaultProbs()
    {
        // Coefficient probs
        Buffer.BlockCopy(
            Vp8Constants.DefaultCoeffProbs,
            0,
            CoeffProbs,
            0,
            Vp8Constants.BlockTypes * Vp8Constants.CoeffBands * Vp8Constants.PrevCoeffContexts * Vp8Constants.EntropyNodes);

        // MV probs
        Vp8Constants.DefaultMvProbs0.CopyTo(MvProbs.AsSpan2D(0));
        Vp8Constants.DefaultMvProbs1.CopyTo(MvProbs.AsSpan2D(1));

        // Y/UV mode probs
        Vp8Constants.DefaultYModeProbs.CopyTo(YModeProbs);
        Vp8Constants.DefaultUvModeProbs.CopyTo(UvModeProbs);
    }
}

/// <summary>
/// Helper extensions for 2D array span access.
/// </summary>
internal static class Array2DExtensions
{
    /// <summary>Gets a Span over a single row of a 2D array.</summary>
    public static Span<T> AsSpan2D<T>(this T[,] array, int row)
    {
        var cols = array.GetLength(1);
        return System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
            ref array[row, 0], cols);
    }
}
