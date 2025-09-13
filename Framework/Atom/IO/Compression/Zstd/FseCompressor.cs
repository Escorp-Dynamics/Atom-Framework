using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Сжатие FSE для одного алфавита (LL/ML/OF) с предопределённым распределением.
/// Таблица строится однократно; затем можно эмитить символы в обратном порядке (как требует FSE).
/// </summary>
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
        for (var i = 0; i < stateTable.Length; i++) stateTable[i] = 0xFFFF; // EMPTY

        var tableSize = 1 << accuracyLog;
        var maxSymbol = norm.Length - 1;

        // 1) Заполнить распределение по кругу (spread), как в FSE: шаг = step = (tableSize >> 1) + (tableSize >> 3) + 3
        Span<int> position = stackalloc int[1]; // служебное (ноль)
        var step = (tableSize >> 1) + (tableSize >> 3) + 3;
        var mask = tableSize - 1;

        // Текущая позиция в круговой таблице
        var pos = 0;

        // Подсчёт положительных норм и багаж для (-1) — «full reset» символов
        var remaining = tableSize;
        for (var s = 0; s <= maxSymbol; s++)
        {
            int count = norm[s];

            if (count > 0)
            {
                remaining -= count;
            }
            else if (count == 0)
            {
                /* пропуск */
            }
            else /*-1*/
            {
                remaining -= 1;
            }
        }

        if (remaining != 0) throw new InvalidOperationException("Нарушена сумма нормализованных вероятностей для FSE");

        // Массив для "частичных" (-1) и обычных позиций
        // Сначала раскладываем символы с count = -1: занимают 1 ячейку в хвосте таблицы.
        var highPos = tableSize - 1;

        for (var s = 0; s <= maxSymbol; s++)
        {
            if (norm[s] == -1) stateTable[highPos--] = (ushort)s;
        }

        // Затем раскладываем остальные (count > 0) циклическим шагом.
        for (var s = 0; s <= maxSymbol; s++)
        {
            int count = norm[s];
            if (count <= 0) continue;

            for (var i = 0; i < count; i++)
            {
                pos = (pos + step) & mask;
                // избегаем коллизий занятых (-1)
                while (stateTable[pos] != 0xFFFF) pos = (pos + step) & mask;
                stateTable[pos] = (ushort)s;
            }
        }

        // Для -1 ячейки уже стоят в конце (заполнены выше).

        // 2) Построить символические трансформации.
        // Рассчитываем deltaNbBits и deltaFindState для каждого символа,
        // а также составляем stateTable как "следующее состояние" по порядку появления символов.

        // Подсчёт сколько состояний на символ
        Span<int> cumulate = stackalloc int[maxSymbol + 2];
        cumulate[0] = 0;

        for (var s = 0; s <= maxSymbol; s++)
        {
            int cnt = norm[s];
            var add = cnt == -1 ? 1 : (cnt < 0 ? 0 : cnt);
            cumulate[s + 1] = cumulate[s] + add;
        }

        // deltaNbBits для каждого символа
        for (var s = 0; s <= maxSymbol; s++)
        {
            int count = norm[s];
            var cnt = count == -1 ? 1 : (count < 0 ? 0 : count);
            var deltaNbBits = (uint)((accuracyLog << 16) - (cnt << accuracyLog));
            var deltaFindState = cumulate[s] - 1;
            symTT[s] = new FseSymbolTransform(deltaNbBits, deltaFindState);
        }

        // 3) Преобразуем stateTable: в нём пока символы; надо заменить их на индексы состояния (rank)
        // согласно порядку размещения (как в FSE reference).
        // Создадим ранги: сколько ячеек уже использовано на символ s.
        Span<int> rank = stackalloc int[maxSymbol + 1];

        for (var i = 0; i < tableSize; i++)
        {
            int s = stateTable[i];
            var idxInSym = rank[s]++;
            // положение этого подынтервала среди символа:
            var stateNumber = cumulate[s] + idxInSym;
            stateTable[i] = (ushort)stateNumber; // в дальнейшем: nextState = stateTable[deltaFindState + interval]
        }

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
    public readonly void EncodeSymbol(ref uint nPlusState /*N+state*/, int symbol, ref LittleEndianBitWriter bw)
    {
        // nbBitsOut = (N_plus_state + deltaNbBits) >> 16
        var t = _symTT[symbol];
        var nbBitsOut = (nPlusState + t.DeltaNbBits) >> 16;

        // записываем nbBitsOut младших бит
        bw.WriteBits(nPlusState, (int)nbBitsOut);

        // interval = (N_plus_state >> nbBitsOut)
        var interval = nPlusState >> (int)nbBitsOut;

        // новое состояние: N + state' = stateTable[deltaFindState + interval]
        nPlusState = _stateTable[t.DeltaFindState + (int)interval];
    }
}