using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
internal readonly unsafe struct HuffmanDecodeTable(int tableLog, byte* symbolsPtr, byte* nbBitsPtr)
{
    private readonly byte* symbols = symbolsPtr;
    private readonly byte* nbBits = nbBitsPtr;

    public readonly int TableLog = tableLog;

    public ReadOnlySpan<byte> Symbols
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(symbols, 1 << TableLog);
    }

    public ReadOnlySpan<byte> NbBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(nbBits, 1 << TableLog);
    }
}
