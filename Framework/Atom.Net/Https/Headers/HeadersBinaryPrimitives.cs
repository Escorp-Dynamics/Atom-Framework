using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

internal static class HeadersBinaryPrimitives
{
    /// <summary>
    /// Префиксный varint (совместим HPACK/QPACK): value с N-битным префиксом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteVarInt(ref BufferWriter w, int value, int prefixBits, byte firstByteMask, byte prefixMask)
    {
        var maxInPrefix = prefixMask & ((1 << prefixBits) - 1);

        if (value < maxInPrefix)
        {
            w.WriteByte((byte)(firstByteMask | value));
            return;
        }

        w.WriteByte((byte)(firstByteMask | maxInPrefix));
        value -= maxInPrefix;

        while (value >= 128)
        {
            w.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        w.WriteByte((byte)value);
    }

    /// <summary>
    /// Чтение префиксного varint.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadVarInt(ref BufferReader r, int prefixBits, byte prefixMask)
    {
        var mask = prefixMask & ((1 << prefixBits) - 1);
        var first = r.ReadByte();
        var value = first & mask;

        if (value < mask) return value;

        var shift = 0;
        var add = 0;

        do
        {
            var b = r.ReadByte();
            add |= (b & 0x7F) << shift;

            if ((b & 0x80) == 0) break;

            shift += 7;

            if (shift > 28) throw new InvalidOperationException("VarInt too long");
        }
        while (true);

        return value + add;
    }

    /// <summary>
    /// Быстрая запись ASCII: не-ASCII → '?'.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteAscii(ref BufferWriter w, ReadOnlySpan<char> s)
    {
        Span<byte> tmp = stackalloc byte[256];
        var pos = 0;

        for (var i = 0; i < s.Length; i++)
        {
            if (pos == tmp.Length)
            {
                w.Write(tmp);
                pos = 0;
            }

            var c = s[i];
            tmp[pos++] = (byte)(c <= 0x7Fu ? c : 0x3Fu);
        }

        if (pos > 0) w.Write(tmp[..pos]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AsciiEquals(scoped ReadOnlySpan<char> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return default;

        for (var i = 0; i < a.Length; i++)
        {
            var c = a[i];

            if (c > 0x7F) return default;
            if ((byte)c != b[i]) return default;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AsciiLowerEquals(scoped ReadOnlySpan<char> a, ReadOnlySpan<byte> bLowerAscii)
    {
        if (a.Length != bLowerAscii.Length) return default;

        for (var i = 0; i < a.Length; i++)
        {
            var c = a[i];

            if (c > 0x7F) return default;
            if (c is >= 'A' and <= 'Z') c = (char)(c + 32);
            if ((byte)c != bLowerAscii[i]) return default;
        }

        return true;
    }
}