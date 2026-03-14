#pragma warning disable S109, MA0051

using System.Numerics;

namespace Atom.Media;

/// <summary>
/// Константы для VP8L (WebP Lossless) битового потока.
/// </summary>
/// <remarks>
/// Спецификация: https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification
/// </remarks>
internal static class Vp8LConstants
{
    #region Header

    /// <summary>VP8L signature byte.</summary>
    internal const byte Signature = 0x2F;

    /// <summary>Размер заголовка VP8L (signature + packed width/height/alpha/version).</summary>
    internal const int HeaderSize = 5;

    /// <summary>Максимальный размер изображения (14-bit).</summary>
    internal const int MaxImageSize = 16384;

    /// <summary>Версия формата (должна быть 0).</summary>
    internal const int FormatVersion = 0;

    #endregion

    #region Transforms

    /// <summary>Тип преобразования: Predictor.</summary>
    internal const int TransformPredictor = 0;

    /// <summary>Тип преобразования: Color (Cross-Color).</summary>
    internal const int TransformColor = 1;

    /// <summary>Тип преобразования: Subtract Green.</summary>
    internal const int TransformSubtractGreen = 2;

    /// <summary>Тип преобразования: Color Indexing (Palette).</summary>
    internal const int TransformColorIndexing = 3;

    /// <summary>Максимальное количество преобразований.</summary>
    internal const int MaxTransforms = 4;

    /// <summary>Количество режимов предсказания.</summary>
    internal const int NumPredictorModes = 14;

    #endregion

    #region Entropy Coding

    /// <summary>Количество литеральных символов (Green channel).</summary>
    internal const int NumLiteralSymbols = 256;

    /// <summary>Количество LZ77 length prefix codes.</summary>
    internal const int NumLengthPrefixCodes = 24;

    /// <summary>Количество distance prefix codes.</summary>
    internal const int NumDistancePrefixCodes = 40;

    /// <summary>Количество prefix codes в одной группе.</summary>
    internal const int NumPrefixCodesPerGroup = 5;

    /// <summary>Индекс: Green + Length + Color Cache.</summary>
    internal const int PrefixGroupGreen = 0;

    /// <summary>Индекс: Red.</summary>
    internal const int PrefixGroupRed = 1;

    /// <summary>Индекс: Blue.</summary>
    internal const int PrefixGroupBlue = 2;

    /// <summary>Индекс: Alpha.</summary>
    internal const int PrefixGroupAlpha = 3;

    /// <summary>Индекс: Distance.</summary>
    internal const int PrefixGroupDistance = 4;

    /// <summary>Размер алфавита для каналов R/B/A.</summary>
    internal const int AlphabetSizeColor = 256;

    /// <summary>Размер алфавита для Distance.</summary>
    internal const int AlphabetSizeDistance = 40;

    /// <summary>Максимальная длина LZ77 back-reference.</summary>
    internal const int MaxLz77Length = 4096;

    /// <summary>Максимальный размер палитры (Color Indexing).</summary>
    internal const int MaxPaletteSize = 256;

    /// <summary>Максимальные биты цветового кэша.</summary>
    internal const int MaxColorCacheBits = 11;

    /// <summary>Минимальные биты цветового кэша.</summary>
    internal const int MinColorCacheBits = 1;

    #endregion

    #region Code Length Coding

    /// <summary>Количество символов в алфавите длин кодов (как в DEFLATE).</summary>
    internal const int NumCodeLengthCodes = 19;

    /// <summary>Порядок чтения длин кодов из битового потока.</summary>
    internal static ReadOnlySpan<byte> CodeLengthCodeOrder =>
    [
        17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    ];

    /// <summary>Максимальная длина Huffman кода.</summary>
    internal const int MaxCodeLength = 15;

    #endregion

    #region Distance Mapping

    /// <summary>Количество специальных 2D distance codes.</summary>
    internal const int NumDistanceMapEntries = 120;

    /// <summary>
    /// Таблица 2D distance mapping (120 записей, каждая пара: dx, dy).
    /// Расстояние = dx + dy * image_width, если ≥ 1.
    /// </summary>
    internal static ReadOnlySpan<sbyte> DistanceMap =>
    [
        // (dx, dy) pairs — 120 entries
         0, 1,    1, 0,    1, 1,   -1, 1,    0, 2,    2, 0,    1, 2,   -1, 2,
         2, 1,   -2, 1,    2, 2,   -2, 2,    0, 3,    3, 0,    1, 3,   -1, 3,
         3, 1,   -3, 1,    2, 3,   -2, 3,    3, 2,   -3, 2,    0, 4,    4, 0,
         1, 4,   -1, 4,    4, 1,   -4, 1,    3, 3,   -3, 3,    2, 4,   -2, 4,
         4, 2,   -4, 2,    0, 5,    3, 4,   -3, 4,    4, 3,   -4, 3,    5, 0,
         1, 5,   -1, 5,    5, 1,   -5, 1,    2, 5,   -2, 5,    5, 2,   -5, 2,
         4, 4,   -4, 4,    3, 5,   -3, 5,    5, 3,   -5, 3,    0, 6,    6, 0,
         1, 6,   -1, 6,    6, 1,   -6, 1,    2, 6,   -2, 6,    6, 2,   -6, 2,
         4, 5,   -4, 5,    5, 4,   -5, 4,    3, 6,   -3, 6,    6, 3,   -6, 3,
         0, 7,    7, 0,    1, 7,   -1, 7,    5, 5,   -5, 5,    7, 1,   -7, 1,
         4, 6,   -4, 6,    6, 4,   -6, 4,    2, 7,   -2, 7,    7, 2,   -7, 2,
         3, 7,   -3, 7,    7, 3,   -7, 3,    5, 6,   -5, 6,    6, 5,   -6, 5,
         8, 0,    4, 7,   -4, 7,    7, 4,   -7, 4,    8, 1,    8, 2,    6, 6,
        -6, 6,    8, 3,    5, 7,   -5, 7,    7, 5,   -7, 5,    8, 4,    6, 7,
        -6, 7,    7, 6,   -7, 6,    8, 5,    7, 7,   -7, 7,    8, 6,    8, 7,
    ];

    #endregion

    #region LZ77 Prefix Coding

    /// <summary>
    /// Декодирует LZ77 prefix code + extra bits в значение (length или distance).
    /// </summary>
    /// <param name="prefixCode">Prefix code (0..23 для length, 0..39 для distance).</param>
    /// <param name="extraBits">Значение дополнительных бит.</param>
    /// <returns>Декодированное значение (1-based).</returns>
    internal static int PrefixCodeToValue(int prefixCode, uint extraBits)
    {
        if (prefixCode < 4)
        {
            return prefixCode + 1;
        }

        var extraBitCount = (prefixCode - 2) >> 1;
        var offset = (2 + (prefixCode & 1)) << extraBitCount;
        return offset + (int)extraBits + 1;
    }

    /// <summary>
    /// Возвращает количество дополнительных бит для данного prefix code.
    /// </summary>
    internal static int PrefixCodeExtraBits(int prefixCode)
    {
        if (prefixCode < 4)
        {
            return 0;
        }

        return (prefixCode - 2) >> 1;
    }

    /// <summary>
    /// Кодирует значение (length или distance) в prefix code + extra bits.
    /// Обратная операция к <see cref="PrefixCodeToValue"/>.
    /// </summary>
    /// <param name="value">Значение (1-based).</param>
    /// <param name="prefixCode">Выходной prefix code.</param>
    /// <param name="extraBitsValue">Выходное значение дополнительных бит.</param>
    /// <param name="extraBitsCount">Выходное количество дополнительных бит.</param>
    internal static void ValueToPrefixCode(int value, out int prefixCode, out int extraBitsValue, out int extraBitsCount)
    {
        if (value <= 4)
        {
            prefixCode = value - 1;
            extraBitsValue = 0;
            extraBitsCount = 0;
            return;
        }

        var v = value - 1;
        var log2 = BitOperations.Log2((uint)v);
        extraBitsCount = log2 - 1;
        var highBit = v >> extraBitsCount;  // 2 или 3
        prefixCode = (extraBitsCount << 1) + highBit;
        var offset = (2 + (prefixCode & 1)) << extraBitsCount;
        extraBitsValue = v - offset;
    }

    #endregion
}
