using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет декодировщик для HPACK.
/// </summary>
public sealed class HPackDecoder : IHeadersDecoder
{
    IEnumerable<KeyValuePair<string, string>> IHeadersDecoder.Decode(ReadOnlySpan<byte> block)
    {
        // Копируем в Memory для избежания захвата Span через yield/await
        var mem = new byte[block.Length];
        block.CopyTo(mem);
        return Decode(mem);
    }
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
        // Копируем входные данные в массив, чтобы не захватывать stackalloc/Span через iterator state
        var arr = block.ToArray();
        var result = new List<KeyValuePair<string, string>>();
        int pos = 0;
        while (pos < arr.Length)
        {
            var b = arr[pos];
            if (IsIndexedHeader(b))
            {
                ProcessIndexedHeader(arr, ref pos, result);
                continue;
            }
            if (IsDynamicTableSizeUpdate(b))
            {
                ProcessDynamicTableSizeUpdate(arr, ref pos);
                continue;
            }
            if (IsLiteralHeader(b))
            {
                ProcessLiteralHeader(arr, ref pos, b, result);
                continue;
            }
            throw new InvalidOperationException("HPACK: неизвестный контрольный байт " + b.ToString("X2"));
        }
        return result;
    }

    /// <summary>
    /// Проверяет, является ли байт индексированным заголовком.
    /// </summary>
    private static bool IsIndexedHeader(byte b) => (b & 0b1000_0000) != 0;

    /// <summary>
    /// Проверяет, является ли байт обновлением размера динамической таблицы.
    /// </summary>
    private static bool IsDynamicTableSizeUpdate(byte b) => (b & 0b1110_0000) == 0b0010_0000;

    /// <summary>
    /// Проверяет, является ли байт литеральным заголовком.
    /// </summary>
    private static bool IsLiteralHeader(byte b)
    {
        var incremental = (b & 0b1100_0000) == 0b0100_0000;
        var neverIndexed = (b & 0b1111_0000) == 0b0001_0000;
        var withoutIndex = (b & 0b1111_0000) == 0b0000_0000;
        return incremental || neverIndexed || withoutIndex;
    }

    /// <summary>
    /// Обрабатывает индексированный заголовок.
    /// </summary>
    private void ProcessIndexedHeader(byte[] arr, ref int pos, List<KeyValuePair<string, string>> result)
    {
        var index = ReadVarIntFromArray(arr, ref pos, 7, 0b0111_1111);
        var e = ResolveIndex(index);
        var nameArr = e.Name.ToArray();
        var valueArr = e.Value.ToArray();
        result.Add(
            new KeyValuePair<string, string>(
                AsciiLowerString(nameArr, 0, nameArr.Length),
                AsciiString(valueArr, 0, valueArr.Length)
            )
        );
    }

    /// <summary>
    /// Обрабатывает обновление размера динамической таблицы.
    /// </summary>
    private void ProcessDynamicTableSizeUpdate(byte[] arr, ref int pos)
    {
        var size = ReadVarIntFromArray(arr, ref pos, 5, 0b0001_1111);
        DynamicTableSize = size;
        dynamicTable.SetCapacity(size);
    }

    /// <summary>
    /// Обрабатывает литеральный заголовок.
    /// </summary>
    private void ProcessLiteralHeader(byte[] arr, ref int pos, byte b, List<KeyValuePair<string, string>> result)
    {
        var incremental = (b & 0b1100_0000) == 0b0100_0000;
        var prefix = incremental ? 6 : 4;
        const byte Mask6 = 0b_0011_1111;
        const byte Mask4 = 0b_0000_1111;
        var mask = incremental ? Mask6 : Mask4;
        byte[] nameArr = Array.Empty<byte>();
        int nameOffset = 0, nameLen = 0;
        var nameIndexed = (b & mask) != 0;
        if (nameIndexed)
        {
            var nameIndex = ReadVarIntFromArray(arr, ref pos, prefix, mask);
            var entry = ResolveIndex(nameIndex);
            nameArr = entry.Name.ToArray();
            nameOffset = 0;
            nameLen = nameArr.Length;
        }
        else
        {
            pos++;
            var nameSpan = ReadStringBytesFromArray(arr, pos, out pos);
            nameArr = nameSpan.ToArray();
            nameOffset = 0;
            nameLen = nameArr.Length;
        }
        var valueSpan = ReadStringBytesFromArray(arr, pos, out pos);
        var valueArr = valueSpan.ToArray();
        result.Add(new KeyValuePair<string, string>(AsciiLowerString(nameArr, nameOffset, nameLen), AsciiString(valueArr, 0, valueArr.Length)));
        if (incremental) dynamicTable.Add(nameArr, valueArr);
    }
    /// <summary>
    /// Читает переменную длину int из массива байт.
    /// </summary>
    private static int ReadVarIntFromArray(byte[] arr, ref int pos, int prefix, int mask)
    {
        int value = arr[pos++] & mask;
        if (value < mask) return value;
        int m = 0;
        int b;
        do
        {
            b = arr[pos++];
            value += (b & 0x7F) << m;
            m += 7;
        } while ((b & 0x80) != 0);
        return value;
    }

    /// <summary>
    /// Читает строку HPACK из массива байт.
    /// </summary>
    private static ReadOnlySpan<byte> ReadStringBytesFromArray(byte[] arr, int pos, out int newPos)
    {
        var peek = arr[pos];
        var huffman = (peek & 0x80) != 0;
        var len = ReadVarIntFromArray(arr, ref pos, 7, 0x7F);
        var data = new ReadOnlySpan<byte>(arr, pos, len);
        pos += len;
        newPos = pos;
        return huffman ? HPackHuffman.Decode(data) : data;
    }

    /// <summary>
    /// Быстрое преобразование ASCII-буфера в нижний регистр в string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string AsciiLowerString(byte[] arr, int offset, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            var b = arr[offset + i];
            if ((uint)(b - (byte)'A') <= ('Z' - 'A')) b = (byte)(b + 32);
            chars[i] = (char)b;
        }
        return new string(chars);
    }

    /// <summary>
    /// Быстрое преобразование ASCII-буфера в string (не-ASCII → '?').
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string AsciiString(byte[] arr, int offset, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            var b = arr[offset + i];
            chars[i] = (char)(b <= 0x7F ? b : (byte)'?');
        }
        return new string(chars);
    }

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