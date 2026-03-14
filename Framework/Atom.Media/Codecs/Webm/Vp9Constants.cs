#pragma warning disable CA1814, CA1819, IDE0048, IDE0055, MA0007, S109, S1450, S2386

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 bitstream constants per RFC 7741 / VP9 Specification §7, §9, §10.
/// </summary>
internal static class Vp9Constants
{
    #region Block Sizes

    /// <summary>Number of block sizes in VP9 (4×4 to 64×64).</summary>
    internal const int NumBlockSizes = 13;

    /// <summary>Maximum superblock size (64×64 pixels).</summary>
    internal const int MaxSbSize = 64;

    /// <summary>Minimum block size (4×4 pixels).</summary>
    internal const int MinBlockSize = 4;

    /// <summary>Block size enumeration: 4×4, 4×8, 8×4, 8×8, 8×16, 16×8, 16×16, 16×32, 32×16, 32×32, 32×64, 64×32, 64×64.</summary>
    internal static readonly (int W, int H)[] BlockSizeLookup =
    [
        (4, 4), (4, 8), (8, 4), (8, 8),
        (8, 16), (16, 8), (16, 16), (16, 32),
        (32, 16), (32, 32), (32, 64), (64, 32),
        (64, 64)
    ];

    /// <summary>Block size index for 4×4.</summary>
    internal const int Block4x4 = 0;

    /// <summary>Block size index for 8×8.</summary>
    internal const int Block8x8 = 3;

    /// <summary>Block size index for 16×16.</summary>
    internal const int Block16x16 = 6;

    /// <summary>Block size index for 32×32.</summary>
    internal const int Block32x32 = 9;

    /// <summary>Block size index for 64×64.</summary>
    internal const int Block64x64 = 12;

    #endregion

    #region Intra Prediction Modes

    /// <summary>Number of intra prediction modes.</summary>
    internal const int NumIntraModes = 10;

    /// <summary>DC prediction.</summary>
    internal const int DcPred = 0;

    /// <summary>Vertical prediction.</summary>
    internal const int VPred = 1;

    /// <summary>Horizontal prediction.</summary>
    internal const int HPred = 2;

    /// <summary>D45 prediction (diagonal 45°).</summary>
    internal const int D45Pred = 3;

    /// <summary>D135 prediction (diagonal 135°).</summary>
    internal const int D135Pred = 4;

    /// <summary>D117 prediction (diagonal 117°).</summary>
    internal const int D117Pred = 5;

    /// <summary>D153 prediction (diagonal 153°).</summary>
    internal const int D153Pred = 6;

    /// <summary>D207 prediction (diagonal 207°).</summary>
    internal const int D207Pred = 7;

    /// <summary>D63 prediction (diagonal 63°).</summary>
    internal const int D63Pred = 8;

    /// <summary>TM prediction (TrueMotion).</summary>
    internal const int TmPred = 9;

    #endregion

    #region Transform Sizes

    /// <summary>Number of transform sizes: 4×4, 8×8, 16×16, 32×32.</summary>
    internal const int NumTxSizes = 4;

    /// <summary>4×4 transform.</summary>
    internal const int Tx4x4 = 0;

    /// <summary>8×8 transform.</summary>
    internal const int Tx8x8 = 1;

    /// <summary>16×16 transform.</summary>
    internal const int Tx16x16 = 2;

    /// <summary>32×32 transform.</summary>
    internal const int Tx32x32 = 3;

    /// <summary>Maximum transform size allowed for each block size (log2).</summary>
    internal static readonly int[] MaxTxSizeLookup =
    [
        Tx4x4,   // 4×4
        Tx4x4,   // 4×8
        Tx4x4,   // 8×4
        Tx8x8,   // 8×8
        Tx8x8,   // 8×16
        Tx8x8,   // 16×8
        Tx16x16, // 16×16
        Tx16x16, // 16×32
        Tx16x16, // 32×16
        Tx32x32, // 32×32
        Tx32x32, // 32×64
        Tx32x32, // 64×32
        Tx32x32, // 64×64
    ];

    #endregion

    #region Segments

    /// <summary>Maximum number of segments.</summary>
    internal const int MaxSegments = 8;

    /// <summary>Segment feature: quantizer delta.</summary>
    internal const int SegFeatureQIndex = 0;

    /// <summary>Segment feature: loop filter delta.</summary>
    internal const int SegFeatureLoopFilter = 1;

    /// <summary>Segment feature: reference frame.</summary>
    internal const int SegFeatureRefFrame = 2;

    /// <summary>Segment feature: skip residual.</summary>
    internal const int SegFeatureSkip = 3;

    /// <summary>Number of segment features.</summary>
    internal const int NumSegFeatures = 4;

    /// <summary>Maximum absolute value for each segment feature.</summary>
    internal static readonly int[] SegFeatureDataMax = [255, 63, 3, 0];

    /// <summary>Number of bits for each segment feature data (signed).</summary>
    internal static readonly int[] SegFeatureDataBits = [8, 6, 2, 0];

    #endregion

    #region Partition Tree

    /// <summary>Partition types.</summary>
    internal const int PartitionNone = 0;
    internal const int PartitionHorz = 1;
    internal const int PartitionVert = 2;
    internal const int PartitionSplit = 3;
    internal const int NumPartitionTypes = 4;

    /// <summary>
    /// Binary tree for partition type decoding.
    /// Leaf = negative value = symbol.
    /// </summary>
    internal static readonly sbyte[] PartitionTree =
    [
        -0, 2,          // node 0: bit 0 → NONE, bit 1 → node 2
        -1, 4,          // node 2: bit 0 → HORZ,  bit 1 → node 4
        -2, -3,         // node 4: bit 0 → VERT,  bit 1 → SPLIT
    ];

    #endregion

    #region Intra Mode Tree

    /// <summary>
    /// Binary tree for VP9 intra prediction mode decoding.
    /// 10 modes: DC, V, H, D45, D135, D117, D153, D207, D63, TM.
    /// </summary>
    internal static readonly sbyte[] IntraModeTree =
    [
        -0, 2,          // DC vs rest
        -9, 4,          // TM vs rest
        -1, 6,          // V vs rest
        -2, 8,          // H vs rest
        -3, 10,         // D45 vs rest
        -4, 12,         // D135 vs rest
        -5, 14,         // D117 vs rest
        -6, 16,         // D153 vs rest
        -7, -8,         // D207 vs D63
    ];

    #endregion

    #region Default Probabilities

    /// <summary>
    /// Default partition probabilities. Indexed by [ctx][partition_type].
    /// VP9 spec §9.3. 16 contexts × 3 probabilities per tree (4 partition types, 3-node tree).
    /// </summary>
    internal static readonly byte[,] DefaultPartitionProbs = new byte[16, 3]
    {
        // ctx 0..15
        { 199, 122, 141 }, { 147, 63, 159 }, { 148, 133, 118 }, { 121, 104, 114 },
        { 174, 73, 87 },  { 92, 41, 83 },   { 82, 99, 50 },   { 53, 39, 39 },
        { 177, 58, 59 },  { 68, 26, 63 },   { 52, 79, 25 },   { 17, 14, 12 },
        { 222, 34, 30 },  { 72, 16, 44 },   { 58, 32, 12 },   { 10, 7, 6 },
    };

    /// <summary>
    /// Default keyframe intra mode probabilities (Y mode). Indexed by [above_mode][left_mode][node].
    /// VP9 spec §10.1. 10×10×9 array (10 above modes × 10 left modes × 9 tree nodes).
    /// </summary>
    internal static readonly byte[,,] DefaultKfYModeProbs = new byte[10, 10, 9]
    {
        // above_mode = DC_PRED (0)
        {
            { 137, 30, 42, 148, 151, 207, 70, 52, 91 },  // left=DC
            { 92, 45, 102, 136, 116, 180, 74, 90, 100 },  // left=V
            { 73, 32, 19, 187, 222, 215, 46, 34, 100 },   // left=H
            { 91, 30, 32, 116, 121, 186, 93, 86, 94 },    // left=D45
            { 72, 35, 36, 149, 68, 206, 68, 63, 105 },    // left=D135
            { 73, 31, 28, 138, 57, 124, 55, 122, 151 },   // left=D117
            { 67, 23, 21, 140, 126, 197, 40, 37, 171 },   // left=D153
            { 86, 27, 28, 128, 154, 212, 45, 43, 53 },    // left=D207
            { 74, 32, 27, 107, 86, 160, 80, 78, 101 },    // left=D63
            { 59, 67, 44, 140, 161, 202, 78, 67, 119 },   // left=TM
        },
        // above_mode = V_PRED (1)
        {
            { 63, 36, 126, 146, 123, 158, 60, 90, 96 },
            { 43, 46, 168, 134, 107, 128, 69, 142, 92 },
            { 44, 29, 68, 159, 201, 177, 50, 57, 77 },
            { 58, 38, 76, 114, 97, 172, 78, 133, 92 },
            { 46, 41, 76, 140, 63, 184, 69, 112, 57 },
            { 38, 32, 85, 140, 46, 112, 54, 151, 133 },
            { 39, 27, 61, 131, 110, 175, 44, 75, 136 },
            { 52, 30, 74, 113, 130, 175, 51, 64, 58 },
            { 47, 35, 80, 100, 74, 143, 64, 105, 97 },
            { 36, 61, 116, 114, 128, 162, 80, 125, 82 },
        },
        // above_mode = H_PRED (2)
        {
            { 82, 26, 26, 171, 208, 204, 44, 32, 105 },
            { 55, 44, 68, 166, 179, 192, 57, 57, 108 },
            { 42, 26, 11, 199, 241, 228, 23, 15, 85 },
            { 68, 42, 19, 131, 160, 199, 55, 52, 83 },
            { 58, 50, 25, 139, 115, 232, 39, 52, 118 },
            { 50, 35, 33, 153, 104, 162, 64, 59, 131 },
            { 44, 24, 16, 150, 177, 202, 33, 19, 156 },
            { 55, 27, 12, 153, 203, 218, 26, 27, 49 },
            { 53, 49, 21, 110, 116, 168, 59, 80, 76 },
            { 38, 72, 19, 168, 203, 212, 50, 50, 107 },
        },
        // above_mode = D45_PRED (3)
        {
            { 103, 26, 36, 129, 132, 201, 83, 80, 93 },
            { 59, 38, 83, 112, 103, 162, 83, 121, 97 },
            { 56, 25, 16, 172, 183, 196, 58, 47, 97 },
            { 79, 39, 28, 106, 110, 178, 100, 115, 96 },
            { 51, 36, 29, 141, 69, 200, 79, 84, 100 },
            { 60, 30, 44, 130, 56, 149, 68, 137, 137 },
            { 46, 23, 28, 122, 128, 189, 61, 55, 132 },
            { 68, 28, 29, 115, 156, 204, 67, 53, 60 },
            { 59, 29, 30, 106, 84, 162, 84, 108, 100 },
            { 47, 61, 45, 112, 131, 185, 87, 92, 105 },
        },
        // above_mode = D135_PRED (4)
        {
            { 69, 23, 29, 128, 83, 199, 46, 44, 101 },
            { 53, 40, 55, 139, 69, 183, 61, 80, 110 },
            { 40, 29, 19, 161, 180, 207, 43, 24, 91 },
            { 60, 34, 19, 105, 61, 198, 53, 64, 89 },
            { 52, 31, 22, 158, 40, 209, 58, 62, 89 },
            { 44, 31, 29, 147, 46, 158, 56, 102, 198 },
            { 35, 19, 12, 135, 87, 209, 41, 45, 167 },
            { 55, 25, 21, 118, 95, 215, 38, 39, 66 },
            { 51, 38, 25, 113, 58, 164, 70, 93, 97 },
            { 47, 54, 34, 146, 108, 203, 72, 103, 151 },
        },
        // above_mode = D117_PRED (5)
        {
            { 64, 19, 37, 156, 66, 138, 49, 95, 133 },
            { 46, 27, 80, 150, 55, 124, 55, 121, 135 },
            { 36, 23, 27, 165, 149, 166, 54, 64, 118 },
            { 53, 21, 21, 113, 52, 132, 75, 111, 119 },
            { 40, 26, 29, 147, 34, 168, 56, 79, 107 },
            { 31, 28, 38, 143, 38, 65, 55, 148, 206 },
            { 33, 18, 17, 127, 67, 163, 41, 61, 198 },
            { 40, 18, 22, 135, 69, 148, 37, 82, 102 },
            { 41, 25, 25, 104, 36, 119, 66, 102, 135 },
            { 32, 33, 31, 140, 80, 157, 70, 107, 187 },
        },
        // above_mode = D153_PRED (6)
        {
            { 75, 17, 22, 136, 131, 186, 31, 25, 159 },
            { 56, 39, 58, 133, 117, 173, 48, 53, 187 },
            { 35, 21, 12, 161, 212, 207, 20, 14, 145 },
            { 56, 29, 19, 117, 109, 181, 55, 68, 112 },
            { 47, 29, 17, 153, 64, 220, 59, 51, 114 },
            { 46, 16, 24, 136, 76, 147, 41, 64, 172 },
            { 34, 17, 11, 108, 152, 187, 13, 15, 209 },
            { 51, 24, 14, 115, 133, 209, 32, 26, 104 },
            { 55, 30, 18, 122, 79, 179, 44, 88, 116 },
            { 37, 49, 25, 129, 168, 164, 41, 54, 148 },
        },
        // above_mode = D207_PRED (7)
        {
            { 82, 22, 32, 127, 143, 213, 39, 41, 70 },
            { 62, 44, 61, 123, 105, 189, 48, 57, 64 },
            { 47, 25, 17, 175, 222, 220, 24, 30, 86 },
            { 68, 36, 17, 106, 102, 206, 59, 74, 74 },
            { 57, 39, 23, 151, 68, 216, 55, 63, 58 },
            { 49, 30, 35, 141, 70, 168, 82, 40, 115 },
            { 40, 18, 19, 137, 134, 175, 26, 27, 159 },
            { 54, 22, 19, 127, 182, 220, 29, 36, 41 },
            { 52, 31, 29, 117, 88, 178, 62, 68, 76 },
            { 44, 62, 30, 137, 147, 198, 60, 57, 102 },
        },
        // above_mode = D63_PRED (8)
        {
            { 65, 23, 26, 120, 96, 182, 72, 65, 96 },
            { 57, 39, 73, 121, 80, 162, 55, 96, 90 },
            { 38, 23, 13, 151, 184, 200, 34, 38, 100 },
            { 53, 28, 26, 100, 78, 180, 75, 82, 102 },
            { 47, 33, 24, 138, 46, 196, 61, 69, 80 },
            { 47, 27, 26, 124, 46, 146, 63, 93, 144 },
            { 39, 17, 14, 120, 94, 182, 38, 48, 141 },
            { 51, 23, 26, 111, 116, 193, 42, 55, 65 },
            { 47, 29, 27, 97, 64, 158, 74, 91, 109 },
            { 48, 52, 37, 116, 116, 179, 60, 80, 100 },
        },
        // above_mode = TM_PRED (9)
        {
            { 91, 38, 45, 166, 164, 205, 64, 55, 112 },
            { 56, 41, 87, 139, 132, 168, 60, 83, 110 },
            { 51, 30, 20, 188, 217, 214, 37, 29, 105 },
            { 72, 37, 28, 124, 127, 205, 71, 72, 84 },
            { 55, 32, 23, 152, 89, 214, 51, 55, 97 },
            { 47, 27, 28, 143, 74, 148, 49, 95, 154 },
            { 47, 22, 24, 151, 131, 207, 33, 30, 178 },
            { 59, 24, 25, 139, 169, 220, 34, 33, 53 },
            { 56, 29, 19, 110, 93, 171, 68, 71, 92 },
            { 46, 63, 28, 159, 185, 210, 70, 58, 124 },
        },
    };

    /// <summary>Default UV mode probabilities for keyframes, indexed by [y_mode][node].</summary>
    internal static readonly byte[,] DefaultKfUvModeProbs = new byte[10, 9]
    {
        { 144, 11, 54, 157, 195, 130, 46, 58, 108 },  // DC
        { 118, 15, 123, 148, 131, 101, 44, 93, 131 },  // V
        { 113, 12, 23, 188, 226, 142, 26, 32, 125 },   // H
        { 120, 11, 50, 123, 163, 135, 64, 77, 103 },   // D45
        { 113, 9, 36, 155, 111, 157, 32, 44, 161 },    // D135
        { 116, 9, 55, 176, 76, 96, 37, 61, 149 },      // D117
        { 115, 9, 28, 141, 161, 167, 21, 25, 193 },    // D153
        { 120, 12, 32, 145, 195, 142, 32, 38, 86 },    // D207
        { 116, 12, 64, 120, 140, 125, 49, 115, 121 },  // D63
        { 102, 19, 66, 162, 182, 122, 35, 59, 128 },   // TM
    };

    #endregion

    #region Quantization Tables

    /// <summary>
    /// DC quantizer lookup table. Indexed by QIndex (0–255).
    /// VP9 spec §8.6.1.
    /// </summary>
    internal static readonly short[] DcQLookup =
    [
        4, 8, 8, 9, 10, 11, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19,
        20, 21, 22, 23, 24, 25, 26, 26, 27, 28, 29, 30, 31, 32, 32, 33,
        34, 35, 36, 37, 38, 38, 39, 40, 41, 42, 43, 43, 44, 45, 46, 47,
        48, 48, 49, 50, 51, 52, 53, 53, 54, 55, 56, 57, 57, 58, 59, 60,
        61, 62, 62, 63, 64, 65, 66, 66, 67, 68, 69, 70, 70, 71, 72, 73,
        74, 74, 75, 76, 77, 78, 78, 79, 80, 81, 81, 82, 83, 84, 85, 85,
        87, 88, 90, 92, 93, 95, 96, 98, 99, 101, 102, 104, 105, 107, 108, 110,
        111, 113, 114, 116, 117, 118, 120, 121, 123, 125, 127, 129, 131, 134, 136, 138,
        140, 142, 144, 146, 148, 150, 152, 154, 156, 158, 161, 164, 166, 169, 172, 174,
        177, 180, 182, 185, 187, 190, 192, 195, 199, 202, 205, 208, 211, 214, 217, 220,
        223, 226, 230, 233, 237, 240, 243, 247, 250, 253, 257, 261, 265, 269, 272, 276,
        280, 284, 288, 292, 296, 300, 304, 309, 313, 317, 322, 326, 330, 335, 340, 344,
        349, 354, 359, 364, 369, 374, 379, 384, 389, 395, 400, 406, 411, 417, 423, 429,
        435, 441, 447, 454, 461, 467, 475, 482, 489, 497, 505, 513, 522, 530, 539, 549,
        559, 569, 579, 590, 602, 614, 626, 640, 654, 668, 684, 700, 717, 736, 755, 775,
        796, 819, 843, 869, 896, 925, 955, 988, 1022, 1058, 1098, 1139, 1184, 1232, 1282, 1336,
    ];

    /// <summary>
    /// AC quantizer lookup table. Indexed by QIndex (0–255).
    /// VP9 spec §8.6.1.
    /// </summary>
    internal static readonly short[] AcQLookup =
    [
        4, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
        23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38,
        39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54,
        55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
        71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86,
        87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102,
        104, 106, 108, 110, 112, 114, 116, 118, 120, 122, 124, 126, 128, 130, 132, 134,
        136, 138, 140, 142, 144, 146, 148, 150, 152, 155, 158, 161, 164, 167, 170, 173,
        176, 179, 182, 185, 188, 191, 194, 197, 200, 203, 207, 211, 215, 219, 223, 227,
        231, 235, 239, 243, 247, 251, 255, 260, 265, 270, 275, 280, 285, 290, 295, 300,
        305, 311, 317, 323, 329, 335, 341, 347, 353, 359, 366, 373, 380, 387, 394, 401,
        408, 416, 424, 432, 440, 448, 456, 464, 473, 482, 491, 500, 509, 518, 527, 537,
        547, 557, 567, 577, 587, 597, 608, 619, 630, 641, 653, 665, 677, 689, 702, 715,
        729, 743, 757, 771, 786, 801, 816, 832, 848, 864, 881, 898, 915, 933, 951, 969,
        988, 1007, 1026, 1046, 1066, 1087, 1108, 1129, 1151, 1173, 1196, 1219, 1243, 1267, 1292, 1317,
        1343, 1369, 1396, 1423, 1451, 1479, 1508, 1537, 1567, 1597, 1628, 1660, 1692, 1725, 1759, 1793,
    ];

    #endregion

    #region Coefficient Token Trees

    /// <summary>Number of coefficient token values.</summary>
    internal const int NumTokens = 12;

    /// <summary>Token values.</summary>
    internal const int ZeroToken = 0;
    internal const int OneToken = 1;
    internal const int TwoToken = 2;
    internal const int ThreeToken = 3;
    internal const int FourToken = 4;
    internal const int Cat1Token = 5;
    internal const int Cat2Token = 6;
    internal const int Cat3Token = 7;
    internal const int Cat4Token = 8;
    internal const int Cat5Token = 9;
    internal const int Cat6Token = 10;
    internal const int EobToken = 11;

    /// <summary>
    /// Coefficient token tree.
    /// VP9 spec §9.6.
    /// </summary>
    internal static readonly sbyte[] CoeffTokenTree =
    [
        -11, 2,         // EOB vs rest
        -0, 4,          // ZERO vs rest
        -1, 6,          // ONE vs rest
        8, 12,          // 2-4 vs 5+
        -2, 10,         // TWO vs 3-4
        -3, -4,         // THREE vs FOUR
        14, 18,         // CAT1-2 vs CAT3+
        -5, 16,         // CAT1 vs CAT2
        -6, -6,         // CAT2 (duplicate for tree balance)
        -7, 20,         // CAT3 vs CAT4+
        -8, 22,         // CAT4 vs CAT5-6
        -9, -10,        // CAT5 vs CAT6
    ];

    /// <summary>Extra bits for each token category.</summary>
    internal static readonly int[] TokenExtraBits = [0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 11];

    /// <summary>Token base values (value = base + extra_bits).</summary>
    internal static readonly int[] TokenBaseValues = [0, 1, 2, 3, 4, 5, 7, 11, 19, 35, 67];

    #endregion

    #region Scan Orders

    /// <summary>
    /// Default scan order for 4×4 blocks (zigzag).
    /// </summary>
    internal static readonly int[] DefaultScan4x4 =
    [
        0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15,
    ];

    /// <summary>
    /// Default scan order for 8×8 blocks (zigzag).
    /// </summary>
    internal static readonly int[] DefaultScan8x8 =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>
    /// Default scan order for 16×16 blocks (zigzag).
    /// </summary>
    internal static readonly int[] DefaultScan16x16 =
    [
        0, 1, 16, 32, 17, 2, 3, 18, 33, 48, 64, 49, 34, 19, 4, 5,
        20, 35, 50, 65, 80, 96, 81, 66, 51, 36, 21, 6, 7, 22, 37, 52,
        67, 82, 97, 112, 128, 113, 98, 83, 68, 53, 38, 23, 8, 9, 24, 39,
        54, 69, 84, 99, 114, 129, 144, 160, 145, 130, 115, 100, 85, 70, 55, 40,
        25, 10, 11, 26, 41, 56, 71, 86, 101, 116, 131, 146, 161, 176, 192, 177,
        162, 147, 132, 117, 102, 87, 72, 57, 42, 27, 12, 13, 28, 43, 58, 73,
        88, 103, 118, 133, 148, 163, 178, 193, 208, 224, 209, 194, 179, 164, 149, 134,
        119, 104, 89, 74, 59, 44, 29, 14, 15, 30, 45, 60, 75, 90, 105, 120,
        135, 150, 165, 180, 195, 210, 225, 240, 241, 226, 211, 196, 181, 166, 151, 136,
        121, 106, 91, 76, 61, 46, 31, 47, 62, 77, 92, 107, 122, 137, 152, 167,
        182, 197, 212, 227, 242, 243, 228, 213, 198, 183, 168, 153, 138, 123, 108, 93,
        78, 63, 79, 94, 109, 124, 139, 154, 169, 184, 199, 214, 229, 244, 245, 230,
        215, 200, 185, 170, 155, 140, 125, 110, 95, 111, 126, 141, 156, 171, 186, 201,
        216, 231, 246, 247, 232, 217, 202, 187, 172, 157, 142, 127, 143, 158, 173, 188,
        203, 218, 233, 248, 249, 234, 219, 204, 189, 174, 159, 175, 190, 205, 220, 235,
        250, 251, 236, 221, 206, 191, 207, 222, 237, 252, 253, 238, 223, 239, 254, 255,
    ];

    /// <summary>
    /// Default scan order for 32×32 blocks (zigzag), first 64 entries only — the rest follows the same pattern.
    /// For simplicity, we generate the full scan at runtime for 32×32.
    /// </summary>
    internal static int[] GenerateScan(int size)
    {
        var n = size * size;
        var scan = new int[n];
        var idx = 0;

        for (var sum = 0; sum < 2 * size - 1; sum++)
        {
            if (sum % 2 == 0)
            {
                for (var row = Math.Min(sum, size - 1); row >= 0 && sum - row < size; row--)
                    scan[idx++] = row * size + (sum - row);
            }
            else
            {
                for (var col = Math.Min(sum, size - 1); col >= 0 && sum - col < size; col--)
                    scan[idx++] = (sum - col) * size + col;
            }
        }

        return scan;
    }

    #endregion

    #region Loop Filter

    /// <summary>Maximum loop filter level.</summary>
    internal const int MaxLoopFilterLevel = 63;

    /// <summary>Number of reference frame types (Intra, Last, Golden, AltRef).</summary>
    internal const int NumRefFrames = 4;

    /// <summary>Intra frame reference.</summary>
    internal const int IntraFrame = 0;

    /// <summary>Last frame reference.</summary>
    internal const int LastFrame = 1;

    /// <summary>Golden frame reference.</summary>
    internal const int GoldenFrame = 2;

    /// <summary>AltRef frame reference.</summary>
    internal const int AltRefFrame = 3;

    #endregion

    #region Color Space

    /// <summary>VP9 color spaces.</summary>
    internal const int CsUnknown = 0;
    internal const int CsBt601 = 1;
    internal const int CsBt709 = 2;
    internal const int CsSmpte170 = 3;
    internal const int CsSmpte240 = 4;
    internal const int CsBt2020 = 5;
    internal const int CsReserved = 6;
    internal const int CsRgb = 7;

    #endregion
}
