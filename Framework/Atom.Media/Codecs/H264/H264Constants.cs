#pragma warning disable S109, S2325, CA1822, MA0051

namespace Atom.Media;

/// <summary>
/// H.264/AVC константы: NAL типы, профили, уровни, таблицы сканирования.
/// </summary>
internal static class H264Constants
{
    #region NAL Unit Types (ITU-T H.264 Table 7-1)

    public const byte NalSliceNonIdr = 1;
    public const byte NalSliceDataPartA = 2;
    public const byte NalSliceDataPartB = 3;
    public const byte NalSliceDataPartC = 4;
    public const byte NalSliceIdr = 5;
    public const byte NalSei = 6;
    public const byte NalSps = 7;
    public const byte NalPps = 8;
    public const byte NalAccessUnitDelimiter = 9;
    public const byte NalEndOfSequence = 10;
    public const byte NalEndOfStream = 11;
    public const byte NalFillerData = 12;
    public const byte NalSpsExtension = 13;
    public const byte NalPrefixNalUnit = 14;
    public const byte NalSubsetSps = 15;
    public const byte NalStapA = 24;
    public const byte NalStapB = 25;
    public const byte NalMtap16 = 26;
    public const byte NalMtap24 = 27;
    public const byte NalFuA = 28;
    public const byte NalFuB = 29;

    #endregion

    #region Profile IDC (ITU-T H.264 Annex A)

    public const byte ProfileBaseline = 66;
    public const byte ProfileMain = 77;
    public const byte ProfileExtended = 88;
    public const byte ProfileHigh = 100;
    public const byte ProfileHigh10 = 110;
    public const byte ProfileHigh422 = 122;
    public const byte ProfileHigh444Predictive = 244;

    #endregion

    #region Macroblock Constants

    public const int MbSize = 16;
    public const int MbSize4 = 4;
    public const int MbSizeChroma = 8;
    public const int MaxMbTypes = 26;

    #endregion

    #region Slice Types (ITU-T H.264 Table 7-6)

    public const int SliceTypeP = 0;
    public const int SliceTypeB = 1;
    public const int SliceTypeI = 2;
    public const int SliceTypeSp = 3;
    public const int SliceTypeSi = 4;

    #endregion

    #region Sub-macroblock partition sizes

    public const int PartSize16x16 = 0;
    public const int PartSize16x8 = 1;
    public const int PartSize8x16 = 2;
    public const int PartSize8x8 = 3;

    #endregion

    #region Intra Prediction Modes 4x4 (Table 8-2)

    public const int Intra4x4Vertical = 0;
    public const int Intra4x4Horizontal = 1;
    public const int Intra4x4Dc = 2;
    public const int Intra4x4DiagonalDownLeft = 3;
    public const int Intra4x4DiagonalDownRight = 4;
    public const int Intra4x4VerticalRight = 5;
    public const int Intra4x4HorizontalDown = 6;
    public const int Intra4x4VerticalLeft = 7;
    public const int Intra4x4HorizontalUp = 8;

    #endregion

    #region Intra Prediction Modes 16x16 (Table 8-3)

    public const int Intra16x16Vertical = 0;
    public const int Intra16x16Horizontal = 1;
    public const int Intra16x16Dc = 2;
    public const int Intra16x16Plane = 3;

    #endregion

    #region Intra Chroma Prediction Modes (Table 8-4)

    public const int IntraChromaDc = 0;
    public const int IntraChromaHorizontal = 1;
    public const int IntraChromaVertical = 2;
    public const int IntraChromaPlane = 3;

    #endregion

    #region Zigzag Scan Orders

    /// <summary>4x4 zigzag scan order (Table 8-13).</summary>
    public static ReadOnlySpan<byte> ZigzagScan4x4 =>
    [
        0,  1,  4,  8,
        5,  2,  3,  6,
        9, 12, 13, 10,
        7, 11, 14, 15,
    ];

    /// <summary>8x8 zigzag scan order (Table 8-14).</summary>
    public static ReadOnlySpan<byte> ZigzagScan8x8 =>
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    #endregion

    #region Default Quantization Matrices

    /// <summary>Default 4x4 scaling list flat (all 16s).</summary>
    public static ReadOnlySpan<byte> DefaultScaling4x4 =>
    [
        16, 16, 16, 16,
        16, 16, 16, 16,
        16, 16, 16, 16,
        16, 16, 16, 16,
    ];

    /// <summary>Default 8x8 intra scaling list.</summary>
    public static ReadOnlySpan<byte> DefaultScaling8x8Intra =>
    [
         6, 10, 13, 16, 18, 23, 25, 27,
        10, 11, 16, 18, 23, 25, 27, 29,
        13, 16, 18, 23, 25, 27, 29, 31,
        16, 18, 23, 25, 27, 29, 31, 33,
        18, 23, 25, 27, 29, 31, 33, 36,
        23, 25, 27, 29, 31, 33, 36, 38,
        25, 27, 29, 31, 33, 36, 38, 40,
        27, 29, 31, 33, 36, 38, 40, 42,
    ];

    /// <summary>Default 8x8 inter scaling list.</summary>
    public static ReadOnlySpan<byte> DefaultScaling8x8Inter =>
    [
         9, 13, 15, 17, 19, 21, 22, 24,
        13, 13, 17, 19, 21, 22, 24, 25,
        15, 17, 19, 21, 22, 24, 25, 27,
        17, 19, 21, 22, 24, 25, 27, 28,
        19, 21, 22, 24, 25, 27, 28, 30,
        21, 22, 24, 25, 27, 28, 30, 32,
        22, 24, 25, 27, 28, 30, 32, 33,
        24, 25, 27, 28, 30, 32, 33, 35,
    ];

    #endregion

    #region QP Constants

    public const int QpMax = 51;
    public const int QpBdOffsetY = 0;

    /// <summary>QPc from QPI for chroma (Table 8-15).</summary>
    public static ReadOnlySpan<byte> QpChromaFromQi =>
    [
         0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 29, 30,
        31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38,
        39, 39, 39, 39,
    ];

    /// <summary>QPc from QP index for chroma — alias for decoder.</summary>
    public static ReadOnlySpan<byte> ChromaQp => QpChromaFromQi;

    #endregion

    #region Start Code

    /// <summary>3-byte start code: 0x00 0x00 0x01.</summary>
    public static ReadOnlySpan<byte> StartCode3 => [0x00, 0x00, 0x01];

    /// <summary>4-byte start code: 0x00 0x00 0x00 0x01.</summary>
    public static ReadOnlySpan<byte> StartCode4 => [0x00, 0x00, 0x00, 0x01];

    #endregion
}
