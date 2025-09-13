using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;

namespace Atom.Net.Https.Headers.QPack;

/// <summary>
/// Применяет команды encoder stream (RFC 9204 §4.2) к динамической таблице QPACK.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal sealed class QPackDecoderStream(QPackDynamicTable t)
{
    private readonly QPackDynamicTable table = t;

    /// <summary>Разобрать и применить последовательность инструкций encoder stream.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void Apply(ReadOnlySpan<byte> stream)
    {
        var r = new BufferReader(stream);

        while (!r.Eof)
        {
            var b = r.PeekByte();

            if ((b & 0b1000_0000) != 0)
            {
                // 1 T NameIdx(6+) + Value — Insert With Name Reference
                var tStatic = (b & 0b0100_0000) != 0;
                var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref r, 6, 0x3F);

                var name = tStatic
                    ? QPackStaticTable.Get(nameIndex).Name
                    : table.GetByRelative_EncoderStream(nameIndex).Name;

                var value = HPackDecoder.ReadStringBytes(ref r);

                table.Add(name, value);
                table.OnInsertCountIncrement(1);
                continue;
            }

            if ((b & 0b1100_0000) == 0b0100_0000)
            {
                // 01 N H NameLen(5+) + Name + H ValueLen(7+) + Value — Insert Without Name Reference
                var name = ReadStringBytes(ref r, 5, 0x1F, 0x20);
                var value = HPackDecoder.ReadStringBytes(ref r);

                table.Add(name, value);
                table.OnInsertCountIncrement(1);
                continue;
            }

            if ((b & 0b1111_0000) == 0b0001_0000)
            {
                // 0001 Index(4+) — Duplicate
                var relIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
                table.Duplicate(relIdx);
                table.OnInsertCountIncrement(1);
                continue;
            }

            if ((b & 0b1110_0000) == 0b0010_0000)
            {
                // 001 Capacity(5+) — Set Dynamic Table Capacity
                var cap = HeadersBinaryPrimitives.ReadVarInt(ref r, 5, 0x1F);
                table.SetCapacity(cap);
                // KnownReceivedCount не увеличивается
                continue;
            }

            throw new InvalidOperationException("QPACK encoder stream: неизвестный тип инструкции");
        }
    }

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