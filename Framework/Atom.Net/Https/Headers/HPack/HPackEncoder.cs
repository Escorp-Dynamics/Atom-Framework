using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет кодировщик HPACK.
/// </summary>
public sealed class HPackEncoder : IHeadersEncoder
{
    private readonly HPackDynamicTable dynamicTable;

    /// <inheritdoc/>
    public bool UseHuffman { get; set; } = true;

    /// <summary>
    /// Размер динамической таблицы (байт). RFC 7541 рекомендует по умолчанию 4096.
    /// </summary>
    public int DynamicTableSize { get; private set; }

    /// <summary>
    /// Пользовательская стратегия выбора режима индексирования по имени заголовка.
    /// </summary>
    public Func<string, HPackIndexingMode>? IndexingSelector { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackEncoder"/>.
    /// </summary>
    /// <param name="dynamicTableSize">Размер динамической таблицы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HPackEncoder(int dynamicTableSize = 4096)
    {
        if (dynamicTableSize < 0) dynamicTableSize = 0;

        DynamicTableSize = dynamicTableSize;
        dynamicTable = new HPackDynamicTable(dynamicTableSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFindIndexed(ReadOnlySpan<char> name, ReadOnlySpan<char> value, out int index)
    {
        // 1) Статическая таблица — полное совпадение
        for (var i = 1; i <= HPackStaticTable.Count; i++)
        {
            var e = HPackStaticTable.Get(i);

            if (e.Value.Length == 0) continue; // только имя
            if (!NameEquals(name, e.Name)) continue;

            if (AsciiEquals(value, e.Value))
            {
                index = i;
                return true;
            }
        }

        // 2) Динамическая таблица — полное совпадение
        var dynCount = dynamicTable.Count;

        for (var pos = 1; pos <= dynCount; pos++)
        {
            var e = dynamicTable.Get(pos);

            if (!NameEquals(name, e.Name)) continue;

            if (AsciiEquals(value, e.Value))
            {
                // Индекс в общем пространстве = 61 (static) + позиция в dynamic
                index = HPackStaticTable.Count + pos;
                return true;
            }
        }

        index = 0;
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int TryFindNameIndex(ReadOnlySpan<char> name)
    {
        // 1) Статика — по имени
        for (var i = 1; i <= HPackStaticTable.Count; i++)
        {
            var e = HPackStaticTable.Get(i);
            if (NameEquals(name, e.Name)) return i;
        }

        // 2) Динамика — по имени (берём самую свежую)
        var dynCount = dynamicTable.Count;

        for (var pos = 1; pos <= dynCount; pos++)
        {
            var e = dynamicTable.Get(pos);
            if (NameEquals(name, e.Name)) return HPackStaticTable.Count + pos;
        }

        return 0;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void Encode(IBufferWriter<byte> writer, [NotNull] IEnumerable<KeyValuePair<string, string>> headers)
    {
        var bw = new BufferWriter(writer);

        foreach (var kv in headers)
        {
            var name = kv.Key.AsSpan();
            var value = kv.Value.AsSpan();

            // 1) Пытаемся найти точное совпадение (name+value) в статике/динамике → чисто индексированная форма.
            if (TryFindIndexed(name, value, out var fullIndex))
            {
                // 1xxxxxxx — Indexed Header Field (prefix 7)
                HeadersBinaryPrimitives.WriteVarInt(ref bw, fullIndex, 7, 0b1000_0000, 0b0111_1111);
                continue;
            }

            // 2) Определяем режим индексирования
            var mode = IndexingSelector is null ? DefaultMode(name) : IndexingSelector(kv.Key);

            // 3) Пытаемся найти индекс имени (без значения)
            var nameIndex = TryFindNameIndex(name);

            // 4) Кодируем в выбранном представлении
            switch (mode)
            {
                case HPackIndexingMode.Incremental:
                    // 01xxxxxx — Literal with Incremental Indexing
                    if (nameIndex > 0)
                    {
                        // Indexed Name
                        HeadersBinaryPrimitives.WriteVarInt(ref bw, nameIndex, 6, 0b0100_0000, 0b0011_1111);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    else
                    {
                        // New Name
                        bw.WriteByte(0b0100_0000);
                        WriteString(ref bw, name, UseHuffman);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    // Добавляем (name,value) в динамическую таблицу
                    dynamicTable.Add(name, value);
                    break;

                case HPackIndexingMode.NeverIndexed:
                    // 0001xxxx — Literal Never Indexed (prefix 4)
                    if (nameIndex > 0)
                    {
                        HeadersBinaryPrimitives.WriteVarInt(ref bw, nameIndex, 4, 0b0001_0000, 0b0000_1111);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    else
                    {
                        bw.WriteByte(0b0001_0000);
                        WriteString(ref bw, name, UseHuffman);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    break;

                case HPackIndexingMode.WithoutIndexing:
                default:
                    // 0000xxxx — Literal Without Indexing (prefix 4)
                    if (nameIndex > 0)
                    {
                        HeadersBinaryPrimitives.WriteVarInt(ref bw, nameIndex, 4, 0b0000_0000, 0b0000_1111);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    else
                    {
                        bw.WriteByte(0b0000_0000);
                        WriteString(ref bw, name, UseHuffman);
                        WriteString(ref bw, value, UseHuffman);
                    }
                    break;
            }
        }

        bw.Flush();
    }

    /// <summary>
    /// Обновляет ёмкость динамической таблицы (и закодировать size-update в поток).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateDynamicTableSize(IBufferWriter<byte> writer, int newCapacity)
    {
        if (newCapacity < 0) newCapacity = 0;

        DynamicTableSize = newCapacity;
        dynamicTable.SetCapacity(newCapacity);

        // 001xxxxx — Dynamic Table Size Update (prefix 5)
        // Пишем одно поле varint с префиксом 5 бит.
        var bw = new BufferWriter(writer);
        HeadersBinaryPrimitives.WriteVarInt(ref bw, newCapacity, 5, 0b0010_0000, 0b0001_1111);
        bw.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HPackIndexingMode DefaultMode(ReadOnlySpan<char> name)
    {
        if (name.Length is 6 && name.SequenceEqual("cookie".AsSpan())) return HPackIndexingMode.NeverIndexed;
        if (name.Length is 13 && name.SequenceEqual("authorization".AsSpan())) return HPackIndexingMode.NeverIndexed;
        if (name.Length is 19 && name.SequenceEqual("proxy-authorization".AsSpan())) return HPackIndexingMode.NeverIndexed;

        return HPackIndexingMode.WithoutIndexing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AsciiEquals(ReadOnlySpan<char> s, ReadOnlySpan<byte> lowerAscii)
    {
        if (s.Length != lowerAscii.Length) return default;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var lb = lowerAscii[i];

            if (c > 0x7Fu) return default;
            if ((byte)c != lb) return default;  // Для значения регистр значим (в HPACK строка байтовая) — в браузерах значения обычно ASCII.
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NameEquals(ReadOnlySpan<char> a, ReadOnlySpan<byte> bLowerAscii)
    {
        if (a.Length != bLowerAscii.Length) return default;

        for (var i = 0; i < a.Length; i++)
        {
            var c = a[i];
            var lb = bLowerAscii[i];

            // сравниваем без культуры: верхний регистр → нижний
            if (c <= 0x7Fu)
            {
                if (c is >= 'A' and <= 'Z') c = (char)(c + 32);
                if ((byte)c != lb) return default;
            }
            else
            {
                // не-ASCII: считаем неравными
                return default;
            }
        }

        return true;
    }

    /// <summary>
    /// Запись HPACK-строки: [H(1) + len(7)] + bytes.
    /// Huffman включается флагом <paramref name="useHuffman"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteString(ref BufferWriter w, ReadOnlySpan<char> s, bool useHuffman)
    {
        if (!useHuffman)
        {
            // H=0
            HeadersBinaryPrimitives.WriteVarInt(ref w, s.Length, 7, 0x00, 0x7F);
            HeadersBinaryPrimitives.WriteAscii(ref w, s);
            return;
        }

        // H=1
        var bitLen = HPackHuffman.GetEncodedBitLength(s);
        var byteLen = (bitLen + 7) >> 3;

        HeadersBinaryPrimitives.WriteVarInt(ref w, byteLen, 7, 0x80, 0x7F);
        HPackHuffman.Encode(ref w, s);
    }
}