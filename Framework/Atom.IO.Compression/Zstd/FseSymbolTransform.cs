using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Трансформация для символа при FSE-сжатии:
/// nbBitsOut = (state + deltaNbBits) >> 16;
/// nextState = stateTable[ deltaFindState + (state >> nbBitsOut) ];
/// Формулы соответствуют описанию автора FSE ("FSE encoding: mapping subranges").
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
internal readonly struct FseSymbolTransform(uint deltaNbBits, int deltaFindState)
{
    public readonly uint DeltaNbBits = deltaNbBits;   // (tableLog << 16) - (count << tableLog)

    public readonly int DeltaFindState = deltaFindState;// смещение в stateTable для текущего символа
}