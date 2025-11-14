using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Сжатие FSE для одного алфавита (LL/ML/OF) с предопределённым распределением.
/// Таблица строится однократно; затем можно эмитить символы в обратном порядке (как требует FSE).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct FseCompressor
{
    private readonly int _tableLog;    // AccuracyLog
    private readonly int _tableSize;   // 1 << tableLog
    private readonly Span<ushort> _stateTable; // next-state для подынтервалов
    private readonly Span<FseSymbolTransform> _symTT; // трансформации символов
    private readonly int _maxSymbol;   // последний используемый символ (включительно)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FseCompressor(int tableLog, Span<ushort> stateTable, Span<FseSymbolTransform> symTT, int maxSymbol)
    {
        _tableLog = tableLog;
        _tableSize = 1 << tableLog;
        _stateTable = stateTable;
        _symTT = symTT;
        _maxSymbol = maxSymbol;
    }

    /// <summary>
    /// Построить таблицы сжатия из нормализованных счётчиков (нормы), как в RFC.
    /// </summary>
    public static FseCompressor Build(ReadOnlySpan<short> norm, int accuracyLog, Span<ushort> stateTable, Span<FseSymbolTransform> symTT)
    {
        stateTable.Fill(0xFFFF);

        var tableSize = 1 << accuracyLog;
        var maxSymbol = norm.Length - 1;
        var step = (tableSize >> 1) + (tableSize >> 3) + 3;
        var mask = tableSize - 1;

        ValidateNormalization(norm, tableSize);

        var highPos = tableSize - 1;
        PlaceMinusOneSymbols(norm, stateTable, ref highPos);
        SpreadPositiveSymbols(norm, stateTable, step, mask);

        Span<int> cumulative = stackalloc int[maxSymbol + 2];
        BuildCumulative(norm, cumulative);
        BuildTransforms(norm, accuracyLog, symTT, cumulative);
        FinalizeStateTable(stateTable, cumulative);

        return new FseCompressor(accuracyLog, stateTable, symTT, maxSymbol);
    }

    /// <summary>
    /// Инициализировать начальное состояние из потока (как будто мы "прочитали" AL бит) — для энкодера
    /// мы наоборот "записываем" эти AL бит в самом конце. Здесь просто возвращаем state, который потом запишем.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint InitState(uint seed) => seed & (uint)((1 << _tableLog) - 1);

    /// <summary>
    /// Эмит символа (в обратном порядке последовательностей). Записывает nbBitsOut младших бит состояния
    /// и обновляет состояние с использованием таблиц.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryEncodeSymbol(ref uint nPlusState /*N+state*/, int symbol, ref LittleEndianBitWriter bw)
    {
        // nbBitsOut = (N_plus_state + deltaNbBits) >> 16
        var t = _symTT[symbol];
        var nbBitsOut = (nPlusState + t.DeltaNbBits) >> 16;

        // записываем nbBitsOut младших бит
        if (!bw.TryWriteBits(nPlusState, (int)nbBitsOut)) return false;

        // interval = (N_plus_state >> nbBitsOut)
        var interval = nPlusState >> (int)nbBitsOut;

        // новое состояние: N + state' = (1<<tableLog) + stateTable[deltaFindState + interval]
        nPlusState = (uint)(1 << _tableLog) + _stateTable[t.DeltaFindState + (int)interval];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateNormalization(ReadOnlySpan<short> norm, int expectedTotal)
    {
        var remaining = expectedTotal;
        for (var s = 0; s < norm.Length; s++)
        {
            var count = norm[s];
            if (count > 0)
            {
                remaining -= count;
            }
            else if (count == -1)
            {
                remaining -= 1;
            }
        }

        if (remaining != 0) throw new InvalidOperationException("Нарушена сумма нормализованных вероятностей для FSE");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PlaceMinusOneSymbols(ReadOnlySpan<short> norm, Span<ushort> stateTable, ref int highPos)
    {
        for (var s = 0; s < norm.Length; s++)
        {
            if (norm[s] != -1) continue;

            stateTable[highPos] = (ushort)s;
            highPos--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SpreadPositiveSymbols(ReadOnlySpan<short> norm, Span<ushort> stateTable, int step, int mask)
    {
        var position = 0;

        for (var s = 0; s < norm.Length; s++)
        {
            var count = norm[s];
            if (count <= 0) continue;

            for (var i = 0; i < count; i++)
            {
                position = (position + step) & mask;
                while (stateTable[position] != 0xFFFF) position = (position + step) & mask;
                stateTable[position] = (ushort)s;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildCumulative(ReadOnlySpan<short> norm, Span<int> cumulative)
    {
        cumulative[0] = 0;
        for (var s = 0; s < norm.Length; s++)
        {
            var add = GetNormalizedCount(norm[s]);
            cumulative[s + 1] = cumulative[s] + add;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildTransforms(ReadOnlySpan<short> norm, int accuracyLog, Span<FseSymbolTransform> symTT, Span<int> cumulative)
    {
        for (var s = 0; s < norm.Length; s++)
        {
            var count = GetNormalizedCount(norm[s]);
            var deltaNbBits = (uint)((accuracyLog << 16) - (count << accuracyLog));
            var deltaFindState = cumulative[s] - 1;
            symTT[s] = new FseSymbolTransform(deltaNbBits, deltaFindState);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FinalizeStateTable(Span<ushort> stateTable, Span<int> cumulative)
    {
        Span<int> rank = stackalloc int[cumulative.Length - 1];

        for (var i = 0; i < stateTable.Length; i++)
        {
            var symbol = stateTable[i];
            if (symbol == 0xFFFF) continue;

            var idxInSymbol = rank[symbol]++;
            var stateNumber = cumulative[symbol] + idxInSymbol;
            stateTable[i] = (ushort)stateNumber;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetNormalizedCount(int value)
    {
        if (value == -1) return 1;
        if (value > 0) return value;
        return 0;
    }
}
