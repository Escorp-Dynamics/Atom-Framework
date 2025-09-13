using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;
using Atom.Net.Https.Headers.QPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет декодировщик QPACK.
/// </summary>
public sealed class QPackDecoder : IHeadersDecoder
{
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
        var r = new BufferReader(block);

        // ----- Prefix -----
        // Encoded Required Insert Count (8+)
        var encodedRic = HeadersBinaryPrimitives.ReadVarInt(ref r, 8, 0xFF);

        // S + Delta Base (7+)
        var signAndDeltaPeek = r.PeekByte();
        var sign = (signAndDeltaPeek & 0x80) is not 0;
        var deltaBase = HeadersBinaryPrimitives.ReadVarInt(ref r, 7, 0x7F);

        // Вычисление RIC и Base по RFC 9204 §4.5.1
        var known = dynamicTable.KnownReceivedCount;
        var maxEntries = dynamicTable.MaxEntries;
        int requiredInsertCount;
        int baseValue;

        if (maxEntries > 0 && encodedRic is not 0)
        {
            // RIC восстановление из закодированного значения (см. RFC 9204 §4.5.1.1)
            var fullRange = 2 * maxEntries;
            var ric = encodedRic + (known / fullRange * fullRange);
            if (ric > known) ric -= fullRange;
            requiredInsertCount = ric;

            // Base (RFC 9204 §4.5.1.2)
            baseValue = sign
                ? (requiredInsertCount - deltaBase - 1)
                : (requiredInsertCount + deltaBase);

            // RIC/Base уже восстановлены
            if (requiredInsertCount > known) throw new QPackBlockedException(requiredInsertCount);
        }
        else
        {
            // динамика не используется в этом блоке
            baseValue = 0;
        }

        while (!r.Eof)
        {
            var b = r.PeekByte();

            if ((b & 0b1000_0000) is not 0)
            {
                // 1 T Index(6+) — Indexed Field Line
                var tStatic = (b & 0b0100_0000) is not 0;
                var index = HeadersBinaryPrimitives.ReadVarInt(ref r, 6, 0x3F);

                if (tStatic)
                {
                    var e = QPackStaticTable.Get(index);
                    yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value));
                }
                else
                {
                    // dynamic pre-base: относительный индекс от Base (RFC 9204 §4.4)
                    var e = dynamicTable.GetByRelative_RepresentationBase(baseValue, index);
                    yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value));
                }

                continue;
            }

            if ((b & 0b1100_0000) is 0b0100_0000)
            {
                // 01 N T NameIdx(4+) + Value — Literal with Name Reference
                var nFlag = (b & 0b0010_0000) is not 0; // никогда не индексировать — влияет на encode-политику, не на decode
                var tStatic = (b & 0b0001_0000) is not 0;
                _ = nFlag;

                var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
                var nameBytes = tStatic ? QPackStaticTable.Get(nameIndex).Name : dynamicTable.GetByRelative_RepresentationBase(baseValue, nameIndex).Name;
                var value = HPackDecoder.ReadStringBytes(ref r);
                yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(nameBytes), HPackDecoder.AsciiString(value));
                continue;
            }

            if ((b & 0b1110_0000) is 0b0010_0000)
            {
                // 001 N H NameLen(3+) + Name + H ValueLen(7+) + Value — Literal with Literal Name
                var name = ReadStringBytes(ref r, 3, 0x07, 0x08);
                var value = HPackDecoder.ReadStringBytes(ref r);
                yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value));
                continue;
            }

            // 0001 Index(4+) — Indexed with Post-Base Index (RFC 9204 §4.4)
            if ((b & 0b1111_0000) is 0b0001_0000)
            {
                var postIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
                var e = dynamicTable.GetByAbsolute(baseValue + postIdx);
                yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value));
                continue;
            }

            // 0000 N NameIdx(3+) + Value — Literal with Post-Base Name Reference (RFC 9204 §4.4)
            if ((b & 0b1111_0000) is 0b0000_0000)
            {
                var nFlag = (b & 0b0000_1000) is not 0;
                _ = nFlag;

                var postNameIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 3, 0x07);
                var name = dynamicTable.GetByAbsolute(baseValue + postNameIdx).Name;
                var value = HPackDecoder.ReadStringBytes(ref r);

                yield return new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value));
                continue;
            }

            throw new InvalidOperationException("QPACK: unsupported representation (enable dynamic table path)");
        }
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