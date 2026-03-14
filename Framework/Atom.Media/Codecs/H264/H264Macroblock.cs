#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0017, IDE0045, IDE0047, IDE0048, MA0008, S3218, IDE0078, MA0182

using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 Macroblock data structures and decoding (ITU-T H.264 Section 7.3.5, 7.4.5).
/// </summary>
internal static class H264Macroblock
{
    #region Types

    /// <summary>
    /// Тип слайса.
    /// </summary>
    internal enum SliceType
    {
        P = 0,
        B = 1,
        I = 2,
        Sp = 3,
        Si = 4,
    }

    /// <summary>
    /// Тип макроблока для I-слайсов (Table 7-11).
    /// </summary>
    internal enum IMbType
    {
        INxN = 0,
        I16x16_0_0_0 = 1,
        I16x16_1_0_0 = 2,
        I16x16_2_0_0 = 3,
        I16x16_3_0_0 = 4,
        I16x16_0_1_0 = 5,
        I16x16_1_1_0 = 6,
        I16x16_2_1_0 = 7,
        I16x16_3_1_0 = 8,
        I16x16_0_2_0 = 9,
        I16x16_1_2_0 = 10,
        I16x16_2_2_0 = 11,
        I16x16_3_2_0 = 12,
        I16x16_0_0_1 = 13,
        I16x16_1_0_1 = 14,
        I16x16_2_0_1 = 15,
        I16x16_3_0_1 = 16,
        I16x16_0_1_1 = 17,
        I16x16_1_1_1 = 18,
        I16x16_2_1_1 = 19,
        I16x16_3_1_1 = 20,
        I16x16_0_2_1 = 21,
        I16x16_1_2_1 = 22,
        I16x16_2_2_1 = 23,
        I16x16_3_2_1 = 24,
        IPcm = 25,
    }

    /// <summary>
    /// Информация о декодированном макроблоке.
    /// </summary>
    internal struct MbInfo
    {
        /// <summary>mb_type из битстрима.</summary>
        public int MbType;

        /// <summary>Intra 16×16 prediction mode (0-3) if I16×16.</summary>
        public int Intra16x16PredMode;

        /// <summary>Coded Block Pattern luma.</summary>
        public int CbpLuma;

        /// <summary>Coded Block Pattern chroma.</summary>
        public int CbpChroma;

        /// <summary>mb_qp_delta.</summary>
        public int QpDelta;

        /// <summary>4×4 intra prediction modes (16 sub-blocks).</summary>
        public unsafe fixed int Intra4x4PredMode[16];

        /// <summary>Chroma intra prediction mode.</summary>
        public int IntraChromaPredMode;

        /// <summary>Non-zero coefficient counts per 4×4 block (for CAVLC nC context).</summary>
        public unsafe fixed int Nnz[24]; // 16 luma + 4 Cb + 4 Cr

        /// <summary>Is this an I_PCM macroblock.</summary>
        public bool IsPcm;
    }

    #endregion

    #region Macroblock Type Parsing

    /// <summary>
    /// Извлекает Intra16x16PredMode из mb_type (Table 7-11).
    /// </summary>
    public static int GetIntra16x16PredMode(int mbType) =>
        mbType >= 1 && mbType <= 24 ? (mbType - 1) % 4 : 0;

    /// <summary>
    /// Извлекает CBP luma из I16×16 mb_type.
    /// </summary>
    public static int GetI16x16CbpLuma(int mbType) =>
        mbType >= 13 ? 15 : 0;

    /// <summary>
    /// Извлекает CBP chroma из I16×16 mb_type.
    /// </summary>
    public static int GetI16x16CbpChroma(int mbType)
    {
        if (mbType is < 1 or > 24)
        {
            return 0;
        }

        return ((mbType - 1) / 4) % 3;
    }

    /// <summary>
    /// Является ли mb_type Intra 16×16.
    /// </summary>
    public static bool IsIntra16x16(int mbType) => mbType is >= 1 and <= 24;

    /// <summary>
    /// Является ли mb_type I_PCM.
    /// </summary>
    public static bool IsPcm(int mbType) => mbType is 25;

    #endregion

    #region CBP Parsing

    /// <summary>
    /// Coded Block Pattern lookup (Table 9-4): maps codeNum → (luma CBP, chroma CBP).
    /// </summary>
    public static (int Luma, int Chroma) DecodeCbpIntra(uint codeNum)
    {
        if (codeNum >= 48)
        {
            return (15, 2);
        }

        return (CbpIntraLuma[(int)codeNum], CbpIntraChroma[(int)codeNum]);
    }

    private static ReadOnlySpan<int> CbpIntraLuma =>
    [
        47,  31,  15,   0,  23,  27,  29,  30,
         7,  11,  13,  14,  39,  43,  45,  46,
        16,   3,   5,  10,  12,  19,  21,  26,
        28,  35,  37,  42,  44,   1,   2,   4,
         8,  17,  18,  20,  24,   6,   9,  22,
        25,  32,  33,  34,  36,  40,  38,  41,
    ];

    private static ReadOnlySpan<int> CbpIntraChroma =>
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2,
    ];

    #endregion

    #region Intra 4×4 Prediction Mode

    /// <summary>
    /// Декодирует intra 4×4 prediction mode для одного sub-block.
    /// </summary>
    public static int DecodeIntra4x4PredMode(ref BitReader reader, int predicted)
    {
        var prevIntra4x4PredModeFlag = reader.ReadBits(1) != 0;

        if (prevIntra4x4PredModeFlag)
        {
            return predicted;
        }

        var remIntra4x4PredMode = (int)reader.ReadBits(3);

        return remIntra4x4PredMode < predicted
            ? remIntra4x4PredMode
            : remIntra4x4PredMode + 1;
    }

    /// <summary>
    /// Вычисляет predicted intra 4×4 mode из соседних блоков.
    /// </summary>
    public static int PredictIntra4x4Mode(int modeA, int modeB)
    {
        if (modeA < 0 || modeB < 0)
        {
            return 2; // DC mode default
        }

        return Math.Min(modeA, modeB);
    }

    #endregion

    #region Coordinate Mapping

    /// <summary>
    /// Маппинг 4×4 luma block index → (x, y) внутри 16×16 MB (raster scan).
    /// </summary>
    public static ReadOnlySpan<int> Block4x4X =>
    [
        0, 4, 0, 4, 8, 12,  8, 12,
        0, 4, 0, 4, 8, 12,  8, 12,
    ];

    public static ReadOnlySpan<int> Block4x4Y =>
    [
         0,  0,  4,  4,  0,  0,  4,  4,
         8,  8, 12, 12,  8,  8, 12, 12,
    ];

    /// <summary>
    /// Порядок обхода 4×4 блоков внутри MB (raster scan order).
    /// </summary>
    public static ReadOnlySpan<int> RasterScanOrder =>
    [
        0, 1, 4, 5, 2, 3, 6, 7,
        8, 9, 12, 13, 10, 11, 14, 15,
    ];

    #endregion
}
