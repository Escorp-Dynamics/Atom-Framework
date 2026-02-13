#pragma warning disable CA1819, CA1051

using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Deflate;

/// <summary>
/// Статические таблицы Deflate (RFC 1951).
/// </summary>
internal static class DeflateTables
{
    #region Fixed Huffman Code Lengths (RFC 1951 §3.2.6)

    /// <summary>
    /// Длины кодов для Fixed Huffman (literal/length alphabet).
    /// 0-143: 8 бит, 144-255: 9 бит, 256-279: 7 бит, 280-287: 8 бит.
    /// </summary>
    public static ReadOnlySpan<byte> FixedLitLenCodeLengths => FixedLitLenCodeLengthsArray;

    private static readonly byte[] FixedLitLenCodeLengthsArray = CreateFixedLitLenCodeLengths();

    private static byte[] CreateFixedLitLenCodeLengths()
    {
        var lengths = new byte[288];

        for (var i = 0; i <= 143; i++) lengths[i] = 8;
        for (var i = 144; i <= 255; i++) lengths[i] = 9;
        for (var i = 256; i <= 279; i++) lengths[i] = 7;
        for (var i = 280; i <= 287; i++) lengths[i] = 8;

        return lengths;
    }

    /// <summary>
    /// Длины кодов для Fixed Huffman (distance alphabet).
    /// Все 32 символа имеют длину 5 бит.
    /// </summary>
    public static ReadOnlySpan<byte> FixedDistCodeLengths => FixedDistCodeLengthsArray;

    private static readonly byte[] FixedDistCodeLengthsArray = CreateFixedDistCodeLengths();

    private static byte[] CreateFixedDistCodeLengths()
    {
        var lengths = new byte[32];
        Array.Fill(lengths, (byte)5);
        return lengths;
    }

    #endregion

    #region Length Table (RFC 1951 §3.2.5)

    /// <summary>
    /// Базовые длины для кодов 257-285.
    /// </summary>
    public static ReadOnlySpan<ushort> LengthBase =>
    [
        3, 4, 5, 6, 7, 8, 9, 10,           // 257-264
        11, 13, 15, 17,                     // 265-268
        19, 23, 27, 31,                     // 269-272
        35, 43, 51, 59,                     // 273-276
        67, 83, 99, 115,                    // 277-280
        131, 163, 195, 227,                 // 281-284
        258,                                  // 285
    ];

    /// <summary>
    /// Количество extra bits для длин (коды 257-285).
    /// </summary>
    public static ReadOnlySpan<byte> LengthExtraBits =>
    [
        0, 0, 0, 0, 0, 0, 0, 0,  // 257-264
        1, 1, 1, 1,              // 265-268
        2, 2, 2, 2,              // 269-272
        3, 3, 3, 3,              // 273-276
        4, 4, 4, 4,              // 277-280
        5, 5, 5, 5,              // 281-284
        0,                         // 285
    ];

    /// <summary>
    /// Packed length info: baseLength | (extraBits shl 16).
    /// Индексируется по (lengthCode - 257).
    /// </summary>
    public static ReadOnlySpan<uint> LengthPacked => LengthPackedArray;

    private static readonly uint[] LengthPackedArray = CreateLengthPacked();

    private static uint[] CreateLengthPacked()
    {
        var packed = new uint[29];
        for (var i = 0; i < 29; i++)
        {
            packed[i] = LengthBase[i] | ((uint)LengthExtraBits[i] << 16);
        }
        return packed;
    }

    /// <summary>
    /// Packed distance info: baseDistance | (extraBits shl 16).
    /// </summary>
    public static ReadOnlySpan<uint> DistancePacked => DistancePackedArray;

    private static readonly uint[] DistancePackedArray = CreateDistancePacked();

    private static uint[] CreateDistancePacked()
    {
        var packed = new uint[30];
        for (var i = 0; i < 30; i++)
        {
            packed[i] = DistanceBase[i] | ((uint)DistanceExtraBits[i] << 16);
        }
        return packed;
    }

    #endregion

    #region Distance Table (RFC 1951 §3.2.5)

    /// <summary>
    /// Базовые дистанции для кодов 0-29.
    /// </summary>
    public static ReadOnlySpan<ushort> DistanceBase =>
    [
        1, 2, 3, 4,                          // 0-3
        5, 7,                                 // 4-5
        9, 13,                                // 6-7
        17, 25,                               // 8-9
        33, 49,                               // 10-11
        65, 97,                               // 12-13
        129, 193,                             // 14-15
        257, 385,                             // 16-17
        513, 769,                             // 18-19
        1025, 1537,                           // 20-21
        2049, 3073,                           // 22-23
        4097, 6145,                           // 24-25
        8193, 12289,                          // 26-27
        16385, 24577,                          // 28-29
    ];

    /// <summary>
    /// Количество extra bits для дистанций (коды 0-29).
    /// </summary>
    public static ReadOnlySpan<byte> DistanceExtraBits =>
    [
        0, 0, 0, 0,   // 0-3
        1, 1,          // 4-5
        2, 2,          // 6-7
        3, 3,          // 8-9
        4, 4,          // 10-11
        5, 5,          // 12-13
        6, 6,          // 14-15
        7, 7,          // 16-17
        8, 8,          // 18-19
        9, 9,          // 20-21
        10, 10,        // 22-23
        11, 11,        // 24-25
        12, 12,        // 26-27
        13, 13,         // 28-29
    ];

    #endregion

    #region Code Length Alphabet Order (RFC 1951 §3.2.7)

    /// <summary>
    /// Порядок code length алфавита для динамических блоков.
    /// </summary>
    public static ReadOnlySpan<byte> CodeLengthOrder =>
    [
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15,
    ];

    #endregion

    #region Bit Reversal Table

    /// <summary>
    /// 8-bit reverse lookup table для O(1) bit reversal.
    /// </summary>
    public static ReadOnlySpan<byte> BitReverse8 => BitReverse8Array;

    private static readonly byte[] BitReverse8Array = CreateBitReverse8();

    private static byte[] CreateBitReverse8()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var v = i;
            var r = 0;
            for (var j = 0; j < 8; j++)
            {
                r = (r << 1) | (v & 1);
                v >>= 1;
            }
            table[i] = (byte)r;
        }
        return table;
    }

    /// <summary>
    /// Быстрый реверс N бит (до 16) через lookup таблицу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReverseBits(int value, int numBits)
    {
        // Комбинируем два 8-bit реверса и сдвигаем на нужное количество бит
        ref var table = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(BitReverse8);
        var reversed = (Unsafe.Add(ref table, value & 0xFF) << 8)
                     | Unsafe.Add(ref table, (value >> 8) & 0xFF);
        return reversed >> (16 - numBits);
    }

    #endregion

    #region Lookup Tables

    /// <summary>
    /// Обратная таблица: длина → код (для кодирования).
    /// Коды 257-285 для длин 3-258.
    /// </summary>
    public static ReadOnlySpan<ushort> LengthToCode => LengthToCodeArray;

    private static readonly ushort[] LengthToCodeArray = CreateLengthToCode();

    private static ushort[] CreateLengthToCode()
    {
        var table = new ushort[259]; // 0-258, но 0-2 невалидны

        for (var code = 0; code < LengthBase.Length; code++)
        {
            var baseLen = (int)LengthBase[code];
            var extraBits = LengthExtraBits[code];
            var count = 1 << extraBits;

            // code 0-28 → литеральные коды 257-285
            var literalCode = (ushort)(code + 257);

            for (var i = 0; i < count && baseLen + i <= 258; i++)
            {
                table[baseLen + i] = literalCode;
            }
        }

        return table;
    }

    /// <summary>
    /// Обратная таблица: дистанция → код (для кодирования).
    /// Использует Log2 для O(1) lookup вместо бинарного поиска.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDistanceCode(int distance)
    {
        // Коды 0-3: дистанции 1-4 (без extra bits)
        if (distance <= 4)
            return distance - 1;

        // Для distance > 4: code = 2 * floor(log2(distance - 1)) + бит
        // log2(distance - 1) даёт номер группы, внутри группы 2 кода
        var n = distance - 1;
        var log2 = System.Numerics.BitOperations.Log2((uint)n);

        // Бит перед MSB определяет чётный/нечётный код в группе
        var code = (log2 << 1) + ((n >> (log2 - 1)) & 1);
        return code;
    }

    #endregion
}
