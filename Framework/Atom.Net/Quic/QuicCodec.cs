using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Quic;

internal static class QuicCodec
{
    /// <summary>Возвращает закодированную длину (1,2,4,8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodedLength(ulong value)
    {
        if (value <= 0x3Fu) return 1;          // 00
        if (value <= 0x3FFFu) return 2;        // 01
        if (value <= 0x3FFF_FFFFu) return 4;   // 10
        return 8;                               // 11
    }

    /// <summary>
    /// Пробует считать VarInt из <paramref name="src"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead(ReadOnlySpan<byte> src, out ulong value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        if (src.IsEmpty) return false;

        var b0 = src[0];
        var prefix = (b0 & 0b1100_0000) >> 6;

        switch (prefix)
        {
            case 0: // 1 байт, 6 бит данных
                value = (ulong)(b0 & 0x3F);
                bytesRead = 1;
                return true;

            case 1: // 2 байта, 14 бит данных
                if (src.Length < 2) return false;
                value = (ulong)(((b0 & 0x3F) << 8) | src[1]);
                bytesRead = 2;
                return true;

            case 2: // 4 байта, 30 бит данных
                if (src.Length < 4) return false;
                value = BinaryPrimitives.ReadUInt32BigEndian(src) & 0x3FFF_FFFFu;
                bytesRead = 4;
                return true;

            default: // 8 байт, 62 бита данных
                if (src.Length < 8) return false;
                value = BinaryPrimitives.ReadUInt64BigEndian(src) & 0x3FFF_FFFF_FFFF_FFFFul;
                bytesRead = 8;
                return true;
        }
    }

    /// <summary>
    /// Пишет VarInt в <paramref name="dst"/>. Возвращает число записанных байт.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWrite(Span<byte> dst, ulong value, out int written)
    {
        written = 0;
        if (dst.IsEmpty) return false;

        if (value <= 0x3Fu)
        {
            dst[0] = (byte)(value & 0x3F);
            written = 1;
            return true;
        }

        if (value <= 0x3FFFu)
        {
            if (dst.Length < 2) return false;
            dst[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            dst[1] = (byte)value;
            written = 2;
            return true;
        }

        if (value <= 0x3FFF_FFFFu)
        {
            if (dst.Length < 4) return false;
            var v = (uint)(0x8000_0000u | value);
            BinaryPrimitives.WriteUInt32BigEndian(dst, v);
            written = 4;
            return true;
        }
        else
        {
            if (dst.Length < 8) return false;
            var v = 0xC000_0000_0000_0000ul | value;
            BinaryPrimitives.WriteUInt64BigEndian(dst, v);
            written = 8;
            return true;
        }
    }
}