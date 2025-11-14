namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Предвычисленные FSE-декодирующие таблицы для предопределённых распределений (LL/ML/OF).
/// Позволяют избегать аллокаций и построения таблиц при каждой валидации секции последовательностей.
/// </summary>
internal static class ZstdPredef
{
    internal static readonly byte[] LLSym = new byte[1 << ZstdLengthsTables.LL_AccuracyLog];
    internal static readonly byte[] LLNb = new byte[1 << ZstdLengthsTables.LL_AccuracyLog];
    internal static readonly ushort[] LLBase = new ushort[1 << ZstdLengthsTables.LL_AccuracyLog];

    internal static readonly byte[] MLSym = new byte[1 << ZstdLengthsTables.ML_AccuracyLog];
    internal static readonly byte[] MLNb = new byte[1 << ZstdLengthsTables.ML_AccuracyLog];
    internal static readonly ushort[] MLBase = new ushort[1 << ZstdLengthsTables.ML_AccuracyLog];

    internal static readonly byte[] OFSym = new byte[1 << ZstdLengthsTables.OffsetsAccuracyLog];
    internal static readonly byte[] OFNb = new byte[1 << ZstdLengthsTables.OffsetsAccuracyLog];
    internal static readonly ushort[] OFBase = new ushort[1 << ZstdLengthsTables.OffsetsAccuracyLog];

    static ZstdPredef()
    {
        FseDecoder.Build(ZstdLengthsTables.LL_DefaultNorm, ZstdLengthsTables.LL_AccuracyLog,
            LLSym.AsSpan(), LLNb.AsSpan(), LLBase.AsSpan());

        FseDecoder.Build(ZstdLengthsTables.ML_DefaultNorm, ZstdLengthsTables.ML_AccuracyLog,
            MLSym.AsSpan(), MLNb.AsSpan(), MLBase.AsSpan());

        FseDecoder.Build(ZstdLengthsTables.OffsetsDefaultNorm, ZstdLengthsTables.OffsetsAccuracyLog,
            OFSym.AsSpan(), OFNb.AsSpan(), OFBase.AsSpan());
    }
}