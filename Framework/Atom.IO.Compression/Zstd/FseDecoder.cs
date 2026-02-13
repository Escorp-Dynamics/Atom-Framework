using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// FSE декодер (таблицы для одного алфавита). Построение из нормализованного распределения.
/// Таблица фиксирована по размеру: 1 &lt;&lt; tableLog состояний.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct FseDecoder
{
    private readonly int _tableLog;
    private readonly ReadOnlySpan<byte> _symbols;     // символ для каждого состояния
    private readonly ReadOnlySpan<byte> _nbBitsOut;   // сколько бит считывать для перехода
    private readonly ReadOnlySpan<ushort> _newStateBase; // базовое смещение для нового состояния

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FseDecoder(int tableLog, ReadOnlySpan<byte> symbols, ReadOnlySpan<byte> nbBitsOut, ReadOnlySpan<ushort> newStateBase)
    {
        _tableLog = tableLog;
        _symbols = symbols;
        _nbBitsOut = nbBitsOut;
        _newStateBase = newStateBase;
    }

    /// <summary>
    /// Построение декод-таблицы из нормализованных вероятностей (norm) точности tableLog.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FseDecoder Build(ReadOnlySpan<short> norm, int tableLog, Span<byte> symbols, Span<byte> nbBitsOut, Span<ushort> newStateBase)
    {
        var tableSize = 1 << tableLog;
        var tableMask = tableSize - 1;
        var step = (tableSize >> 1) + (tableSize >> 3) + 3;

        Span<int> table = stackalloc int[tableSize];
        table.Fill(-1);

        Span<int> symbolNext = stackalloc int[norm.Length];
        PopulateSymbolCounters(norm, symbolNext, tableSize);

        var highThreshold = tableSize - 1;
        highThreshold = PlaceLowestProbabilitySymbols(norm, table, symbolNext, highThreshold);

        SpreadSymbols(norm, table, step, tableMask, highThreshold);

        BuildDecodingEntries(table, symbolNext, tableSize, tableLog, symbols, nbBitsOut, newStateBase);

        return new(tableLog, symbols, nbBitsOut, newStateBase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FseDecoder FromTables(int tableLog, ReadOnlySpan<byte> symbols, ReadOnlySpan<byte> nbBitsOut, ReadOnlySpan<ushort> newStateBase)
        => new(tableLog, symbols, nbBitsOut, newStateBase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte PeekSymbol(uint state) => _symbols[(int)state];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PeekNbBits(uint state) => _nbBitsOut[(int)state];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateState(ref uint state, uint addBits) => state = _newStateBase[(int)state] + addBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte DecodeSymbol(ref uint state, ref ReverseBitReader br)
    {
        var idx = (int)state;
        var sym = _symbols[idx];
        var nb = _nbBitsOut[idx];
        var add = nb != 0 ? br.ReadBits(nb) : 0u;
        state = _newStateBase[idx] + add;
        return sym;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PopulateSymbolCounters(ReadOnlySpan<short> norm, Span<int> symbolNext, int expectedTotal)
    {
        var total = 0;
        for (var s = 0; s < norm.Length; s++)
        {
            var value = norm[s];
            var count = value switch
            {
                -1 => 1,
                > 0 => value,
                _ => 0,
            };

            symbolNext[s] = count;
            total += count;
        }

        if (total != expectedTotal) throw new InvalidOperationException("Bad FSE norm sum");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PlaceLowestProbabilitySymbols(ReadOnlySpan<short> norm, Span<int> table, Span<int> symbolNext, int highThreshold)
    {
        for (var s = 0; s < norm.Length; s++)
        {
            if (norm[s] != -1) continue;

            table[highThreshold] = s;
            symbolNext[s] = 1;
            highThreshold--;
        }

        return highThreshold;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SpreadSymbols(ReadOnlySpan<short> norm, Span<int> table, int step, int tableMask, int highThreshold)
    {
        var position = 0;

        for (var s = 0; s < norm.Length; s++)
        {
            var occurrences = norm[s];
            if (occurrences <= 0) continue;

            for (var i = 0; i < occurrences; i++)
            {
                table[position] = s;
                position = (position + step) & tableMask;

                while (position > highThreshold)
                {
                    position = (position + step) & tableMask;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildDecodingEntries(Span<int> table, Span<int> symbolNext, int tableSize, int tableLog, Span<byte> symbols, Span<byte> nbBitsOut, Span<ushort> newStateBase)
    {
        for (var u = 0; u < tableSize; u++)
        {
            var symbol = table[u];
            if (symbol < 0)
            {
                symbols[u] = 0;
                nbBitsOut[u] = (byte)tableLog;
                newStateBase[u] = 0;
                continue;
            }

            var nextState = symbolNext[symbol]++;
            var leadingZeros = System.Numerics.BitOperations.LeadingZeroCount((uint)nextState);
            var nb = tableLog - (31 - leadingZeros);
            symbols[u] = (byte)symbol;
            nbBitsOut[u] = (byte)nb;
            newStateBase[u] = (ushort)((nextState << nb) - tableSize);
        }
    }
}
