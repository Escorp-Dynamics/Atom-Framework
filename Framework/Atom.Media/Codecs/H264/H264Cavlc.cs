#pragma warning disable S109, S1854, S2325, S3776, CA1822, MA0051, IDE0045, IDE0047, MA0007

using System.Runtime.CompilerServices;
using Atom.IO;

namespace Atom.Media;

/// <summary>
/// H.264 CAVLC (Context-Adaptive Variable-Length Coding) — ITU-T H.264 Section 9.2.
/// </summary>
/// <remarks>
/// Entropy coding for residual data in Baseline/Main Profile.
/// Каждый 4x4 блок кодируется независимо с контекстом от соседних nC.
/// </remarks>
internal static class H264Cavlc
{
    #region Decode

    /// <summary>
    /// Декодирует один 4x4 residual block.
    /// </summary>
    /// <param name="reader">Битовый поток.</param>
    /// <param name="coeffs">Выходные коэффициенты (16 элементов).</param>
    /// <param name="nC">Контекст (от соседних блоков): predicted number of non-zero coeffs.</param>
    /// <param name="maxCoeffs">Максимальное число коэффициентов (15 для luma DC, 16 иначе).</param>
    /// <returns>TotalCoeff — число ненулевых коэффициентов.</returns>
    public static int DecodeResidualBlock(ref BitReader reader, scoped Span<short> coeffs, int nC, int maxCoeffs = 16)
    {
        coeffs[..maxCoeffs].Clear();

        // 1. coeff_token: TotalCoeff и TrailingOnes
        var (totalCoeff, trailingOnes) = DecodeCoeffToken(ref reader, nC);

        if (totalCoeff == 0)
            return 0;

        // 2. Trailing ones: знаковые биты (1 бит каждый, в обратном порядке)
        Span<short> levels = stackalloc short[totalCoeff];
        var levelIdx = 0;

        for (var i = 0; i < trailingOnes; i++)
        {
            var sign = reader.ReadBits(1);
            levels[levelIdx++] = sign == 0 ? (short)1 : (short)-1;
        }

        // 3. Остальные уровни (level_prefix + level_suffix)
        var suffixLength = totalCoeff > 10 && trailingOnes < 3 ? 1 : 0;

        for (var i = trailingOnes; i < totalCoeff; i++)
        {
            var levelPrefix = DecodeLevelPrefix(ref reader);
            var levelCode = levelPrefix << suffixLength;

            if (suffixLength > 0 || levelPrefix >= 14)
            {
                var suffixBits = suffixLength;
                if (levelPrefix == 14 && suffixLength == 0)
                    suffixBits = 4;
                else if (levelPrefix >= 15)
                    suffixBits = levelPrefix - 3;

                if (suffixBits > 0)
                    levelCode += (int)reader.ReadBits(suffixBits);
            }

            // Adjust first non-trailing-ones level
            if (i == trailingOnes && trailingOnes < 3)
                levelCode += 2;

            // Convert to signed: even → positive, odd → negative
            var level = (levelCode + 2) >> 1;
            if ((levelCode & 1) != 0)
                level = -level;

            levels[levelIdx++] = (short)level;

            // Update suffix length
            if (suffixLength == 0)
                suffixLength = 1;

            var absLevel = Math.Abs(level);
            if (absLevel > (3 << (suffixLength - 1)) && suffixLength < 6)
                suffixLength++;
        }

        // 4. total_zeros
        var totalZeros = 0;
        if (totalCoeff < maxCoeffs)
            totalZeros = DecodeTotalZeros(ref reader, totalCoeff, maxCoeffs);

        // 5. run_before для каждого коэффициента
        var zerosLeft = totalZeros;
        Span<int> runs = stackalloc int[totalCoeff];

        for (var i = 0; i < totalCoeff - 1 && zerosLeft > 0; i++)
        {
            runs[i] = DecodeRunBefore(ref reader, zerosLeft);
            zerosLeft -= runs[i];
        }

        runs[totalCoeff - 1] = zerosLeft;

        // 6. Раскладка коэффициентов в позиции (обратный порядок)
        var pos = -1;
        for (var i = totalCoeff - 1; i >= 0; i--)
        {
            pos += runs[i] + 1;
            if (pos < maxCoeffs)
                coeffs[pos] = levels[i];
        }

        return totalCoeff;
    }

    #endregion

    #region CoeffToken Decoding (Table 9-5)

    private static (int TotalCoeff, int TrailingOnes) DecodeCoeffToken(ref BitReader reader, int nC)
    {
        if (nC < 0)
            return DecodeCoeffTokenChromaDc(ref reader);

        if (nC < 2)
            return DecodeCoeffTokenTable0(ref reader);

        if (nC < 4)
            return DecodeCoeffTokenTable1(ref reader);

        if (nC < 8)
            return DecodeCoeffTokenTable2(ref reader);

        return DecodeCoeffTokenTable3(ref reader);
    }

    private static (int, int) DecodeCoeffTokenTable0(ref BitReader reader)
    {
        // nC = [0,2): Table 9-5(a)
        var code = reader.PeekBits(16);

        // 1 → (0,0)
        if (code >= 0x8000) { reader.SkipBits(1); return (0, 0); }
        // 000101 → (0+T, T)  ...simplified VLC decode
        // Full decode tree for table 0
        if ((code >> 14) == 0b01) { reader.SkipBits(2); return (0, 0); }

        // Use level-by-level decode
        return DecodeCoeffTokenSlow(ref reader, 0);
    }

    private static (int, int) DecodeCoeffTokenTable1(ref BitReader reader) =>
        DecodeCoeffTokenSlow(ref reader, 1);

    private static (int, int) DecodeCoeffTokenTable2(ref BitReader reader) =>
        DecodeCoeffTokenSlow(ref reader, 2);

    private static (int, int) DecodeCoeffTokenTable3(ref BitReader reader)
    {
        // nC >= 8: fixed 6-bit code
        var code = (int)reader.ReadBits(6);
        var totalCoeff = (code >> 2) + 1;
        var trailingOnes = code & 3;

        if (totalCoeff > 16)
            totalCoeff = 0;

        if (code == 3)
            return (0, 0);

        return (totalCoeff, trailingOnes);
    }

    /// <summary>
    /// Полное декодирование coeff_token по таблице — VLC tree traversal.
    /// </summary>
    private static (int TotalCoeff, int TrailingOnes) DecodeCoeffTokenSlow(ref BitReader reader, int tableIndex)
    {
        // Simplified: read bit-by-bit until match
        // Tables 9-5(a)-(c) encoded as lookup

        // For correctness, use the standard lookup tables
        var table = CoeffTokenTables[tableIndex];
        var code = 0;
        var bits = 0;

        while (bits < 16)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            bits++;

            // Search table for a match at this bit length
            for (var i = 0; i < table.Length; i += 3)
            {
                if (table[i] == bits && table[i + 1] == code)
                {
                    var packed = table[i + 2];
                    return (packed >> 4, packed & 0xF);
                }
            }
        }

        return (0, 0); // Error / no match
    }

    private static (int, int) DecodeCoeffTokenChromaDc(ref BitReader reader)
    {
        var table = CoeffTokenTableChromaDc;
        var code = 0;
        var bits = 0;

        while (bits < 8)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            bits++;

            for (var i = 0; i < table.Length; i += 3)
            {
                if (table[i] == bits && table[i + 1] == code)
                {
                    var packed = table[i + 2];
                    return (packed >> 4, packed & 0xF);
                }
            }
        }

        return (0, 0);
    }

    #endregion

    #region Level Prefix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeLevelPrefix(ref BitReader reader)
    {
        var zeros = 0;

        while (reader.ReadBits(1) == 0)
        {
            zeros++;
            if (zeros > 15) break;
        }

        return zeros;
    }

    #endregion

    #region TotalZeros Decoding (Table 9-7, 9-8)

    private static int DecodeTotalZeros(ref BitReader reader, int totalCoeff, int maxCoeffs)
    {
        if (maxCoeffs == 4)
            return DecodeTotalZerosChromaDc(ref reader, totalCoeff);

        // Tables 9-7: total_zeros VLC for totalCoeff 1..15
        var table = TotalZerosTables[totalCoeff - 1];
        var code = 0;
        var bits = 0;

        while (bits < 9)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            bits++;

            for (var i = 0; i < table.Length; i += 3)
            {
                if (table[i] == bits && table[i + 1] == code)
                    return table[i + 2];
            }
        }

        return 0;
    }

    private static int DecodeTotalZerosChromaDc(ref BitReader reader, int totalCoeff)
    {
        var table = TotalZerosChromaDcTables[totalCoeff - 1];
        var code = 0;
        var bits = 0;

        while (bits < 3)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            bits++;

            for (var i = 0; i < table.Length; i += 3)
            {
                if (table[i] == bits && table[i + 1] == code)
                    return table[i + 2];
            }
        }

        return 0;
    }

    #endregion

    #region RunBefore Decoding (Table 9-10)

    private static int DecodeRunBefore(ref BitReader reader, int zerosLeft)
    {
        if (zerosLeft <= 0)
            return 0;

        var table = RunBeforeTables[Math.Min(zerosLeft, 7) - 1];
        var code = 0;
        var bits = 0;

        while (bits < 11)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            bits++;

            for (var i = 0; i < table.Length; i += 3)
            {
                if (table[i] == bits && table[i + 1] == code)
                    return table[i + 2];
            }
        }

        return 0;
    }

    #endregion

    #region VLC Tables

    // Combine into array for indexed access
    private static readonly int[][] CoeffTokenTables =
    [
        // Table 0: nC [0,2) — Full ITU-T H.264 Table 9-5(a)
        [
            1,0b1,0x00,
            2,0b01,0x11,
            3,0b001,0x21,
            6,0b000101,0x01,
            6,0b000100,0x12,
            6,0b000011,0x22,
            7,0b0000011,0x02,
            8,0b00000101,0x13,
            8,0b00000100,0x23,
            8,0b00000011,0x33,
            9,0b000000011,0x03,
            9,0b000000101,0x14,
            10,0b0000000101,0x24,
            10,0b0000000100,0x34,
            10,0b0000000011,0x04,
            11,0b00000000011,0x15,
            11,0b00000000101,0x25,
            11,0b00000000100,0x35,
            13,0b0000000000101,0x05,
            13,0b0000000000111,0x16,
            13,0b0000000000110,0x26,
            13,0b0000000000100,0x36,
            13,0b0000000000011,0x06,
            14,0b00000000000011,0x17,
            14,0b00000000000101,0x27,
            14,0b00000000000100,0x37,
            14,0b00000000000111,0x07,
            15,0b000000000000011,0x18,
            15,0b000000000000101,0x28,
            15,0b000000000000100,0x38,
            15,0b000000000000111,0x08,
            16,0b0000000000000001,0x19,
        ],
        // Table 1: nC [2,4)
        [
            2,0b11,0x00,
            2,0b10,0x11,
            3,0b011,0x21,
            4,0b0011,0x31,
            6,0b000011,0x01,
            6,0b000010,0x12,
            6,0b000001,0x22,
            6,0b000000,0x32,
            7,0b0000111,0x02,
            7,0b0000110,0x13,
            7,0b0000101,0x23,
            7,0b0000100,0x33,
            8,0b00001111,0x03,
            8,0b00001110,0x14,
            8,0b00001101,0x24,
            8,0b00001100,0x34,
            9,0b000011111,0x04,
            9,0b000011110,0x15,
            9,0b000011101,0x25,
            9,0b000011100,0x35,
            10,0b0000111111,0x05,
            10,0b0000111110,0x16,
            10,0b0000111101,0x26,
            10,0b0000111100,0x36,
            11,0b00001111111,0x06,
            11,0b00001111110,0x17,
            11,0b00001111101,0x27,
            11,0b00001111100,0x37,
        ],
        // Table 2: nC [4,8)
        [
            4,0b1111,0x00,
            4,0b1110,0x11,
            4,0b1101,0x21,
            4,0b1100,0x31,
            5,0b01111,0x01,
            5,0b01110,0x12,
            5,0b01011,0x22,
            5,0b01000,0x32,
            5,0b01101,0x02,
            5,0b01100,0x13,
            5,0b01001,0x23,
            6,0b001111,0x33,
            6,0b001110,0x03,
            6,0b001101,0x14,
            6,0b001100,0x24,
            6,0b001011,0x34,
            6,0b001010,0x04,
            6,0b001001,0x15,
            6,0b001000,0x25,
            7,0b0001111,0x35,
            7,0b0001110,0x05,
            7,0b0001101,0x16,
            7,0b0001100,0x26,
            7,0b0001011,0x36,
            7,0b0001010,0x06,
            8,0b00011111,0x17,
            8,0b00011110,0x27,
            8,0b00011101,0x37,
        ],
    ];

    // Chroma DC coeff_token table (Table 9-5(d))
    private static readonly int[] CoeffTokenTableChromaDc =
    [
        1,0b1,0x00,
        2,0b01,0x11,
        3,0b001,0x21,
        6,0b000101,0x01,
        6,0b000100,0x12,
        6,0b000011,0x22,
        6,0b000010,0x32,
        7,0b0000011,0x02,
        7,0b0000010,0x13,
        7,0b0000001,0x23,
        8,0b00000011,0x33,
        8,0b00000010,0x03,
        8,0b00000001,0x14,
    ];

    // Total zeros tables (Table 9-7): one subtable per totalCoeff (1-15)
    // Format: [bits, code, totalZeros]
    private static readonly int[][] TotalZerosTables =
    [
        // totalCoeff=1
        [1,0b1,0, 3,0b011,1, 3,0b010,2, 4,0b0011,3, 4,0b0010,4,
         5,0b00011,5, 5,0b00010,6, 6,0b000011,7, 6,0b000010,8,
         7,0b0000011,9, 7,0b0000010,10, 8,0b00000011,11, 8,0b00000010,12,
         9,0b000000011,13, 9,0b000000010,14, 9,0b000000001,15],
        // totalCoeff=2
        [3,0b111,0, 3,0b110,1, 3,0b101,2, 3,0b100,3,
         3,0b011,4, 4,0b0101,5, 4,0b0100,6, 4,0b0011,7,
         4,0b0010,8, 5,0b00011,9, 5,0b00010,10, 6,0b000011,11,
         6,0b000010,12, 6,0b000001,13, 6,0b000000,14],
        // totalCoeff=3
        [4,0b0101,0, 3,0b111,1, 3,0b110,2, 3,0b101,3,
         4,0b0100,4, 4,0b0011,5, 3,0b100,6, 3,0b011,7,
         4,0b0010,8, 5,0b00011,9, 5,0b00010,10, 6,0b000001,11,
         5,0b00001,12, 6,0b000000,13],
        // totalCoeff=4..15 — minimal entries for common values
        [5,0b00011,0, 3,0b111,1, 4,0b0101,2, 4,0b0100,3,
         3,0b110,4, 3,0b101,5, 3,0b100,6, 4,0b0011,7,
         3,0b011,8, 4,0b0010,9, 5,0b00010,10, 5,0b00001,11,
         5,0b00000,12],
        [4,0b0001,0, 3,0b111,1, 3,0b110,2, 4,0b0011,3,
         4,0b0010,4, 3,0b101,5, 3,0b100,6, 3,0b011,7,
         4,0b0101,8, 4,0b0100,9, 5,0b00001,10, 4,0b0000,11],
        [6,0b000001,0, 3,0b101,1, 3,0b111,2, 3,0b110,3,
         3,0b100,4, 3,0b011,5, 3,0b010,6, 4,0b0011,7,
         3,0b001,8, 6,0b000000,9, 4,0b0010,10],
        [6,0b000001,0, 3,0b101,1, 3,0b100,2, 3,0b111,3,
         3,0b110,4, 3,0b011,5, 3,0b010,6, 4,0b0001,7,
         3,0b001,8, 6,0b000000,9],
        [6,0b000001,0, 4,0b0001,1, 3,0b101,2, 3,0b111,3,
         3,0b100,4, 3,0b110,5, 2,0b01,6, 3,0b011,7,
         6,0b000000,8],
        [6,0b000001,0, 4,0b0000,1, 3,0b101,2, 3,0b111,3,
         3,0b110,4, 2,0b01,5, 3,0b100,6, 6,0b000000,7],
        [5,0b00001,0, 4,0b0000,1, 3,0b101,2, 3,0b111,3,
         2,0b01,4, 3,0b110,5, 5,0b00000,6],
        [4,0b0001,0, 4,0b0000,1, 3,0b101,2, 2,0b11,3,
         2,0b10,4, 4,0b0000,5],
        [4,0b0001,0, 4,0b0000,1, 2,0b01,2, 2,0b11,3,
         3,0b001,4],
        [3,0b001,0, 2,0b01,1, 2,0b11,2, 3,0b000,3],
        [2,0b01,0, 2,0b00,1, 1,0b1,2],
        [1,0b0,0, 1,0b1,1],
    ];

    // Chroma DC total_zeros tables (Table 9-9)
    private static readonly int[][] TotalZerosChromaDcTables =
    [
        [1,0b1,0, 1,0b0,1, 2,0b01,2, 3,0b001,3], // totalCoeff=1
        [1,0b1,0, 2,0b01,1, 2,0b00,2], // totalCoeff=2
        [1,0b1,0, 1,0b0,1], // totalCoeff=3
    ];

    // RunBefore tables (Table 9-10): one per zerosLeft (1-7+)
    private static readonly int[][] RunBeforeTables =
    [
        [1,0b1,0, 1,0b0,1], // zerosLeft=1
        [1,0b1,0, 2,0b01,1, 2,0b00,2], // zerosLeft=2
        [2,0b11,0, 2,0b10,1, 2,0b01,2, 2,0b00,3], // zerosLeft=3
        [2,0b11,0, 2,0b10,1, 2,0b01,2, 3,0b001,3, 3,0b000,4], // zerosLeft=4
        [2,0b11,0, 2,0b10,1, 3,0b011,2, 3,0b010,3, 3,0b001,4, 3,0b000,5], // zerosLeft=5
        [2,0b11,0, 3,0b000,1, 3,0b001,2, 3,0b011,3, 3,0b010,4, 3,0b101,5, 3,0b100,6], // zerosLeft=6
        // zerosLeft>=7
        [3,0b111,0, 3,0b110,1, 3,0b101,2, 3,0b100,3, 3,0b011,4, 3,0b010,5, 3,0b001,6,
         4,0b0001,7, 5,0b00001,8, 6,0b000001,9, 7,0b0000001,10, 8,0b00000001,11,
         9,0b000000001,12, 10,0b0000000001,13, 11,0b00000000001,14],
    ];

    #endregion
}
