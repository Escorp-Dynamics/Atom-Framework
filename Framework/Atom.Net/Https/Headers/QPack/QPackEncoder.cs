using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;
using Atom.Net.Https.Headers.QPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет кодировщик QPACK.
/// </summary>
public sealed class QPackEncoder : IHeadersEncoder
{
    /// <inheritdoc/>
    public bool UseHuffman { get; set; } = true;

    /// <summary>
    /// Пользовательская стратегия выбора режима индексации по имени заголовка.
    /// </summary>
    public Func<string, bool>? NeverIndexedSelector { get; set; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void Encode(IBufferWriter<byte> writer, [NotNull] IEnumerable<KeyValuePair<string, string>> headers)
    {
        var bw = new BufferWriter(writer);

        // Required Insert Count (8+) = 0
        HeadersBinaryPrimitives.WriteVarInt(ref bw, 0, 8, 0x00, 0xFF);
        // Base: S=0, DeltaBase(7+) = 0
        HeadersBinaryPrimitives.WriteVarInt(ref bw, 0, 7, 0x00, 0x7F);

        foreach (var kv in headers)
        {
            var name = kv.Key.AsSpan();
            var value = kv.Value.AsSpan();
            var nFlag = NeverIndexedSelector is null ? DefaultNeverIndexed(name) : NeverIndexedSelector(kv.Key);

            // Попытка сослаться на имя из статической таблицы (T=1). Если не нашли — пишем буквальное имя.
            var nameIndex = QPackStaticTable.TryFindNameIndex(name);

            if (nameIndex >= 0)
            {
                // 01 N T NameIdx(4+) — Literal Field Line with Name Reference (T=1 → static)
                var firstMask = (byte)(0b0100_0000 | (nFlag ? 0b0010_0000 : 0) | 0b0001_0000);
                HeadersBinaryPrimitives.WriteVarInt(ref bw, nameIndex, 4, firstMask, 0x0F);

                // Value: H + len(7+) + bytes
                HPackEncoder.WriteString(ref bw, value, UseHuffman);
            }
            else
            {
                // 001 N H NameLen(3+) + Name + H ValueLen(7+) + Value — Literal with Literal Name
                // Имя (prefix=3, H-бит = 0x08; первые биты '001' и N кладём в firstByteMask)
                var firstMask = (byte)(0b0010_0000 | (nFlag ? 0b0001_0000 : 0));
                WriteString(ref bw, name, UseHuffman, 3, firstMask, 0x07, 0x08);

                // Значение — стандартный литерал H/Len(7+)
                HPackEncoder.WriteString(ref bw, value, UseHuffman);
            }
        }

        bw.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DefaultNeverIndexed(ReadOnlySpan<char> name)
    {
        if (name.Length is 6 && name.SequenceEqual("cookie".AsSpan())) return true;
        if (name.Length is 13 && name.SequenceEqual("authorization".AsSpan())) return true;
        if (name.Length is 19 && name.SequenceEqual("proxy-authorization".AsSpan())) return true;

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteString(ref BufferWriter w, ReadOnlySpan<char> s, bool useHuffman, int prefixBits, byte firstByteMask, byte prefixMask, byte hBitMask)
    {
        if (!useHuffman)
        {
            // H=0
            HeadersBinaryPrimitives.WriteVarInt(ref w, s.Length, prefixBits, firstByteMask, prefixMask);
            HeadersBinaryPrimitives.WriteAscii(ref w, s);
            return;
        }

        // H=1
        var bitLen = HPackHuffman.GetEncodedBitLength(s);
        var byteLen = (bitLen + 7) >> 3;

        // Установим H-бит в первом октете
        HeadersBinaryPrimitives.WriteVarInt(ref w, byteLen, prefixBits, (byte)(firstByteMask | hBitMask), prefixMask);
        HPackHuffman.Encode(ref w, s);
    }
}