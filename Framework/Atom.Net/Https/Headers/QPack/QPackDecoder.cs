using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;
using Atom.Net.Https.Headers.QPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет декодировщик QPACK.
/// </summary>
public sealed class QPackDecoder : IHeadersDecoder
{
    IEnumerable<KeyValuePair<string, string>> IHeadersDecoder.Decode(ReadOnlySpan<byte> block)
    {
        // Копируем в Memory для избежания захвата Span через yield/await
        var mem = new byte[block.Length];
        block.CopyTo(mem);
        return Decode(mem);
    }
    private readonly QPackDynamicTable dynamicTable;

    /// <summary>
    /// Размер динамической таблицы.
    /// </summary>
    public int DynamicTableSize { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackDecoder"/>.
    /// </summary>
    /// <param name="dynamicTableSize">Размер динамической таблицы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QPackDecoder(int dynamicTableSize = 4096)
    {
        if (dynamicTableSize < 0) dynamicTableSize = 0;

        DynamicTableSize = dynamicTableSize;
        dynamicTable = new QPackDynamicTable(dynamicTableSize);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public IEnumerable<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block)
    {
        // Копируем входные данные в массив, чтобы не захватывать stackalloc/Span через yield
        var arr = block.ToArray();
        var r = new BufferReader(arr);
        // ----- Prefix -----
        var encodedRic = HeadersBinaryPrimitives.ReadVarInt(ref r, 8, 0xFF);
        var signAndDeltaPeek = r.PeekByte();
        var sign = (signAndDeltaPeek & 0x80) is not 0;
        var deltaBase = HeadersBinaryPrimitives.ReadVarInt(ref r, 7, 0x7F);
        var known = dynamicTable.KnownReceivedCount;
        var maxEntries = dynamicTable.MaxEntries;
        int requiredInsertCount;
        int baseValue;
        if (maxEntries > 0 && encodedRic is not 0)
        {
            var fullRange = 2 * maxEntries;
            var ric = encodedRic + (known / fullRange * fullRange);
            if (ric > known) ric -= fullRange;
            requiredInsertCount = ric;
            baseValue = sign
                ? (requiredInsertCount - deltaBase - 1)
                : (requiredInsertCount + deltaBase);
            if (requiredInsertCount > known) throw new QPackBlockedException(requiredInsertCount);
        }
        else
        {
            baseValue = 0;
        }

        var result = new List<KeyValuePair<string, string>>();

        while (!r.Eof)
        {
            var b = r.PeekByte();
            if (TryProcessRepresentation(ref r, b, baseValue, result))
                continue;

            throw new InvalidOperationException("QPACK: unsupported representation (enable dynamic table path)");
        }

        return result;
    }

    private bool TryProcessRepresentation(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        if (TryProcessIndexedOrIndexedName(ref r, b, baseValue, result)) return true;
        if (TryProcessLiteralOrPostBase(ref r, b, baseValue, result)) return true;
        return false;
    }

    private bool TryProcessIndexedOrIndexedName(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        // Indexed representation (static or dynamic)
        if ((b & 0b1000_0000) is not 0)
        {
            var tStatic = (b & 0b0100_0000) is not 0;
            var index = HeadersBinaryPrimitives.ReadVarInt(ref r, 6, 0x3F);
            if (tStatic)
            {
                var e = QPackStaticTable.Get(index);
                result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            }
            else
            {
                var e = dynamicTable.GetByRelative_RepresentationBase(baseValue, index);
                result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            }

            return true;
        }

        // Literal with name indexed
        if ((b & 0b1100_0000) is 0b0100_0000)
        {
            var nFlag = (b & 0b0010_0000) is not 0;
            var tStatic = (b & 0b0001_0000) is not 0;
            _ = nFlag;
            var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
            var nameBytes = tStatic ? QPackStaticTable.Get(nameIndex).Name : dynamicTable.GetByRelative_RepresentationBase(baseValue, nameIndex).Name;
            var value = HPackDecoder.ReadStringBytes(ref r);
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(nameBytes), HPackDecoder.AsciiString(value)));
            return true;
        }

        return false;
    }

    private bool TryProcessLiteralOrPostBase(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        // Literal with literal name
        if ((b & 0b1110_0000) is 0b0010_0000)
        {
            var name = ReadStringBytes(ref r, 3, 0x07, 0x08);
            var value = HPackDecoder.ReadStringBytes(ref r);
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value)));
            return true;
        }

        // Post-base indexed
        if ((b & 0b1111_0000) is 0b0001_0000)
        {
            var postIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
            var e = dynamicTable.GetByAbsolute(baseValue + postIdx);
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            return true;
        }

        // Post-base literal name
        if ((b & 0b1111_0000) is 0b0000_0000)
        {
            var nFlag = (b & 0b0000_1000) is not 0;
            _ = nFlag;
            var postNameIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 3, 0x07);
            var name = dynamicTable.GetByAbsolute(baseValue + postNameIdx).Name;
            var value = HPackDecoder.ReadStringBytes(ref r);
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value)));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Чтение строкового литерала QPACK с произвольной шириной префикса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> ReadStringBytes(ref BufferReader r, int prefixBits, byte prefixMask, byte hBitMask)
    {
        var peek = r.PeekByte();
        var huffman = (peek & hBitMask) != 0;
        var len = HeadersBinaryPrimitives.ReadVarInt(ref r, prefixBits, prefixMask);
        var data = r.ReadSpan(len);

        return huffman ? HPackHuffman.Decode(data) : data;
    }
}