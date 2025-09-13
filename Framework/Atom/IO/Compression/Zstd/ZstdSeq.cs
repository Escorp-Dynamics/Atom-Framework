using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Одна команда (последовательность): LL, ML, Offset (сырые значения).
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal readonly struct ZstdSeq(int ll, int ml, int off, RepKind rep)
{
    public readonly int LL = ll;
    public readonly int ML = ml;
    public readonly int Offset = off;   // фактическая дистанция (для None и Rep1Minus1 = rep1-1)
    public readonly RepKind Rep = rep;
}