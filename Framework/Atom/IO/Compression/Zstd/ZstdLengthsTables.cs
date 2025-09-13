namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Таблицы для преобразования кодов LL/ML в (Baseline, NbBits) и предопределённые распределения FSE.
/// Данные соответствуют RFC 8878, раздел 3.1.1.3.2.1 (табл. 16 и 17) и 3.1.1.3.2.2.
/// </summary>
internal static class ZstdLengthsTables
{
    internal const int LL_AccuracyLog = 6; // 64 состояний
    internal const int ML_AccuracyLog = 6; // 64 состояний
    internal const int OffsetsAccuracyLog = 5; // 32 состояния
    internal const int OffsetsMaxN = 28;       // макс. код смещения (подходит для стандартных окон)

    // ----- Literal Lengths (LL): коды 0..35 -> (base, addBits)
    internal static ReadOnlySpan<uint> LLBase => [
        // 0..15: length = code, addBits=0 (base не используется)
        0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,
        // 16..23:
        16,18,20,22,24,28,32,40,
        // 24..31:
        48,64,128,256,512,1024,2048,4096,
        // 32..35:
        8192,16384,32768,65536,
    ];

    internal static ReadOnlySpan<byte> LLAddBits => [
        // 0..15:
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        // 16..23:
        1,1,1,1,2,2,3,3,
        // 24..31:
        4,6,7,8,9,10,11,12,
        // 32..35:
        13,14,15,16,
    ];

    // ----- Match Lengths (ML): коды 0..52 -> (base, addBits)
    internal static ReadOnlySpan<uint> MLBase => [
        // 0..31: value = code + 3 (addBits=0)
        3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,
        19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,
        // 32..39:
        35,37,39,41,43,47,51,59,
        // 40..47:
        67,83,99,131,258,514,1026,2050,
        // 48..52:
        4098,8194,16486,32770,65538,
    ];

    internal static ReadOnlySpan<byte> MLAddBits => [
        // 0..31:
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        // 32..39:
        1,1,1,1,2,2,3,3,
        // 40..47:
        4,4,5,7,8,9,10,11,
        // 48..52:
        12,13,14,15,16,
    ];

    /// <summary>
    /// Предопределённые нормализованные распределения FSE (LL, ML, OF), как в спецификации.
    /// Отрицательные значения означают "меньше 1" (=-1).
    /// </summary>
    internal static ReadOnlySpan<short> LL_DefaultNorm => [
        4,3,2,2,2,2,2,2,2,2,2,2,2,1,1,1,
        2,2,2,2,2,2,2,2,2,3,2,1,1,1,1,1,
        -1,-1,-1,-1,
    ];

    internal static ReadOnlySpan<short> ML_DefaultNorm => [
        1,4,3,2,2,2,2,2,2,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,1,1,1,1,1,1,-1,-1,
        -1,-1,-1,
    ];

    /// <summary>
    /// OF: таблица норм для offset codes; точность 5 бит (32 состояния), максимум N=28.
    /// </summary>
    internal static ReadOnlySpan<short> OffsetsDefaultNorm => [
        1,1,1,1,1,1,2,2,2,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1,-1,-1,-1,-1,-1,
    ];
}