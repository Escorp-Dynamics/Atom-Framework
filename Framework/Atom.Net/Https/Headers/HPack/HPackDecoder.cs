using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет декодировщик для HPACK.
/// </summary>
public sealed class HPackDecoder : IHeadersDecoder
{
    private readonly HPackDynamicTable dynamicTable;

    /// <summary>
    /// Размер динамической таблицы.
    /// </summary>
    public int DynamicTableSize { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackDecoder"/>.
    /// </summary>
    /// <param name="dynamicTableSize">Размер динамической таблицы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HPackDecoder(int dynamicTableSize = 4096)
    {
        if (dynamicTableSize < 0) dynamicTableSize = 0;

        DynamicTableSize = dynamicTableSize;
        dynamicTable = new HPackDynamicTable(dynamicTableSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TableEntry ResolveIndex(int index)
    {
        if (index <= 0) throw new InvalidOperationException("HPACK: недопустимый индекс 0");
        if (index <= HPackStaticTable.Count) return HPackStaticTable.Get(index);

        var dynPos = index - HPackStaticTable.Count; // позиция в динамической (1..N)
        if (dynPos <= 0 || dynPos > dynamicTable.Count) throw new InvalidOperationException("HPACK: индекс вне диапазона динамической таблицы");

        return dynamicTable.Get(dynPos);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public IEnumerable<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block)
    {
        var br = new BufferReader(block);

        while (!br.Eof)
        {
            var b = br.PeekByte();

            if ((b & 0b1000_0000) is not 0)
            {
                // Indexed Header Field — 1xxxxxxx (prefix 7)
                var index = HeadersBinaryPrimitives.ReadVarInt(ref br, 7, 0b0111_1111);
                var e = ResolveIndex(index);
                yield return new KeyValuePair<string, string>(AsciiLowerString(e.Name), AsciiString(e.Value));
                continue;
            }

            if ((b & 0b1110_0000) == 0b0010_0000)
            {
                // Dynamic Table Size Update — 001xxxxx (prefix 5)
                var size = HeadersBinaryPrimitives.ReadVarInt(ref br, 5, 0b0001_1111);
                DynamicTableSize = size;
                dynamicTable.SetCapacity(size);
                continue;
            }

            var incremental = (b & 0b1100_0000) == 0b0100_0000; // 01xxxxxx
            var neverIndexed = (b & 0b1111_0000) == 0b0001_0000; // 0001xxxx
            var withoutIndex = (b & 0b1111_0000) == 0b0000_0000; // 0000xxxx

            if (incremental || neverIndexed || withoutIndex)
            {
                var prefix = incremental ? 6 : 4;
                const byte Mask6 = 0b_0011_1111;
                const byte Mask4 = 0b_0000_1111;
                var mask = incremental ? Mask6 : Mask4;

                // Имя: либо индекс, либо «новое имя»
                ReadOnlySpan<byte> name;
                var nameIndexed = (b & mask) is not 0;

                if (nameIndexed)
                {
                    var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref br, prefix, mask);
                    name = ResolveIndex(nameIndex).Name;
                }
                else
                {
                    // Снимаем первый байт-префикс (0b0100_0000 / 0b0001_0000 / 0b0000_0000)
                    br.ReadByte();
                    name = ReadStringBytes(ref br); // H=0/H=1 обрабатывается внутри
                }

                // Значение
                var value = ReadStringBytes(ref br);
                yield return new KeyValuePair<string, string>(AsciiLowerString(name), AsciiString(value));

                if (incremental) dynamicTable.Add(name, value); // копии сделаются внутри
                continue;
            }

            // Неизвестный контрольный байт — защищённый выход
            throw new InvalidOperationException("HPACK: неизвестный контрольный байт " + b.ToString("X2"));
        }
    }

    /// <summary>
    /// Быстрое преобразование ASCII-буфера в нижний регистр в string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string AsciiLowerString(ReadOnlySpan<byte> src) =>
        string.Create(src.Length, src, static (span, s) =>
        {
            for (var i = 0; i < s.Length; i++)
            {
                var b = s[i];
                if ((uint)(b - (byte)'A') <= ('Z' - 'A')) b = (byte)(b + 32);
                span[i] = (char)b;
            }
        });

    /// <summary>
    /// Быстрое преобразование ASCII-буфера в string (не-ASCII → '?').
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string AsciiString(ReadOnlySpan<byte> src) =>
        string.Create(src.Length, src, static (span, s) =>
        {
            for (var i = 0; i < s.Length; i++)
            {
                var b = s[i];
                span[i] = (char)(b <= 0x7F ? b : (byte)'?');
            }
        });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ReadOnlySpan<byte> ReadStringBytes(scoped ref BufferReader r)
    {
        var peek = r.PeekByte();
        var huffman = (peek & 0x80) != 0;
        var len = HeadersBinaryPrimitives.ReadVarInt(ref r, 7, 0x7F);
        var data = r.ReadSpan(len);

        return huffman ? HPackHuffman.Decode(data) : data;
    }
}