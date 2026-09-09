using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Разбор описания FSE-таблицы (нормализованные счётчики) по RFC 8878, §4.1.1.
/// </summary>
/// <remarks>
/// <para>
/// Единственная реализация на весь декодер: раньше один и тот же алгоритм был скопирован
/// в <c lang="text">ZstdDecoder.ParseFseTable</c> и в <c lang="text">ZstdWeightsParser.ParseFseHeader</c>, и обе копии
/// содержали одинаковые дефекты.
/// </para>
/// <para>
/// Что было не так в старых копиях:
/// </para>
/// <list type="number">
///   <item>
///     порог коротких кодов считался как <c lang="text">(1 &lt;&lt; (bits+1)) - range</c> — на единицу больше
///     эталонного <c lang="text">max = (2*threshold - 1) - remaining</c>, из-за чего одно значение читалось
///     на бит короче и весь дальнейший поток рассинхронизировался;
///   </item>
///   <item>
///     дополнительный бит приклеивался младшим разрядом (<c lang="text">(value &lt;&lt; 1) | extra</c>), хотя поток
///     LSB-first и этот бит — старший разряд поля;
///   </item>
///   <item>
///     порог вычитался безусловно, тогда как эталон вычитает его только при взведённом старшем бите;
///   </item>
///   <item>
///     длина описания возвращалась как <c lang="text">BitReader.BytesConsumed</c> — это позиция предзагрузки
///     64-битного буфера (конструктор сразу тянет 8 байт), а не число разобранных байт. Возвращалось
///     всегда 8, поэтому границы описаний таблиц «уезжали», и битовый поток начинался не там.
///   </item>
/// </list>
/// </remarks>
internal static class ZstdFseTable
{
    /// <summary>
    /// Читает нормализованные счётчики и возвращает длину описания в байтах.
    /// </summary>
    /// <param name="source">Байты, начинающиеся с описания таблицы.</param>
    /// <param name="maxSymbol">Максимальный допустимый символ алфавита.</param>
    /// <param name="maxAccuracyLog">Предел Accuracy_Log для данного алфавита.</param>
    /// <param name="norm">Приёмник нормализованных счётчиков (не короче maxSymbol+1).</param>
    /// <param name="tableLog">Прочитанный Accuracy_Log.</param>
    /// <param name="lastSymbol">Индекс последнего описанного символа.</param>
    /// <returns>Сколько байт занимает описание.</returns>
    public static int ParseNormalizedCounts(
        ReadOnlySpan<byte> source,
        int maxSymbol,
        int maxAccuracyLog,
        Span<short> norm,
        out int tableLog,
        out int lastSymbol)
    {
        if (source.IsEmpty) throw new InvalidDataException("Empty FSE table description");

        var reader = new ForwardBitReader(source);
        tableLog = (int)reader.Read(4) + 5;

        // RFC 8878, §3.1.1.3.2.2.1 и §4.2.1.2: у каждого алфавита свой потолок точности.
        // Без этой проверки повреждённый заголовок давал Accuracy_Log до 20 и вылезал наружу
        // как ArgumentOutOfRangeException из рабочего пространства вместо InvalidDataException.
        if (tableLog > maxAccuracyLog)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"FSE accuracy log {tableLog} exceeds {maxAccuracyLog}"));
        }

        norm[..(maxSymbol + 1)].Clear();

        var remaining = (1 << tableLog) + 1;
        var threshold = 1 << tableLog;
        var nbBits = tableLog + 1;
        var symbol = 0;
        var previousZero = false;

        while (remaining > 1 && symbol <= maxSymbol)
        {
            if (previousZero)
            {
                symbol += ReadZeroRun(ref reader);
                if (symbol > maxSymbol) break;
            }

            var count = ReadCount(ref reader, threshold, nbBits, remaining);
            remaining -= count < 0 ? -count : count;
            norm[symbol++] = (short)count;
            previousZero = count == 0;
            Rescale(remaining, ref threshold, ref nbBits);
        }

        if (remaining != 1) throw new InvalidDataException("FSE table description does not sum up to the table size");
        if (symbol == 0) throw new InvalidDataException("Empty FSE symbol table");

        lastSymbol = symbol - 1;

        // RFC 8878, §4.1.1: описание занимает целое число байт, остаток последнего просто не используется.
        var consumed = (reader.Position + 7) >> 3;
        if (consumed > source.Length) throw new InvalidDataException("Truncated FSE table description");
        return consumed;
    }

    /// <summary>
    /// Сужает ширину поля счётчика по мере расходования очков.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rescale(int remaining, ref int threshold, ref int nbBits)
    {
        while (remaining < threshold)
        {
            nbBits--;
            threshold >>= 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadCount(ref ForwardBitReader reader, int threshold, int nbBits, int remaining)
    {
        // Порог коротких кодов по эталонному FSE_readNCount: удвоенный threshold без единицы
        // минус остаток очков. Всё, что меньше порога, кодируется полем на один бит короче.
        var max = (2 * threshold) - 1 - remaining;
        var low = (int)reader.Read(nbBits - 1);

        int value;
        if (low < max)
        {
            value = low;
        }
        else
        {
            // Дополнительный бит — СТАРШИЙ разряд поля, а порог вычитается только при нём.
            var extra = (int)reader.Read(1);
            var full = low | (extra << (nbBits - 1));
            value = full >= threshold ? full - max : full;
        }

        // Значение сдвинуто на единицу: 0 означает «меньше 1» (счётчик -1), 1 — нулевую вероятность.
        return value - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadZeroRun(ref ForwardBitReader reader)
    {
        // RFC 8878, §4.1.1: после нулевой вероятности идут двухбитные флаги повтора.
        // Флаг со значением три добавляет три пропущенных символа и требует прочитать следующий флаг.
        var run = 0;
        while (true)
        {
            var next = (int)reader.Read(2);
            run += next;
            if (next != 3) return run;
        }
    }

    /// <summary>
    /// Прямой LSB-first читатель бит, отдающий нули за концом данных.
    /// </summary>
    /// <remarks>
    /// Собственный, потому что <c lang="text">Atom.IO.BitReader</c> при нехватке бит молча укорачивает
    /// запрошенное количество и не даёт узнать логическую позицию без учёта предзагрузки.
    /// Эталон (<c lang="text">FSE_readNCount</c>) точно так же дочитывает описание таблицы «сквозь» границу
    /// буфера, считая недостающие биты нулевыми, а корректность проверяет по сумме счётчиков.
    /// </remarks>
    [StructLayout(LayoutKind.Auto)]
    internal ref struct ForwardBitReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> data = source;

        /// <summary>Текущая позиция в битах от начала данных.</summary>
        public int Position { get; private set; }

        /// <summary>
        /// Читает <paramref name="count"/> бит (0..32), продвигая позицию.
        /// </summary>
        /// <param name="count">Сколько бит прочитать.</param>
        /// <returns>Прочитанное значение.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read(int count)
        {
            if (count == 0) return 0;

            var byteIndex = Position >> 3;
            var shift = Position & 7;

            ulong window = 0;
            var available = data.Length - byteIndex;
            var take = available < 8 ? available : 8;
            for (var i = 0; i < take; i++)
            {
                window |= (ulong)data[byteIndex + i] << (i * 8);
            }

            Position += count;
            window >>= shift;
            return (uint)(window & ((1UL << count) - 1));
        }
    }
}
