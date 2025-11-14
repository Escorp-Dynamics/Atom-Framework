using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Headers;

[StructLayout(LayoutKind.Auto)]
internal ref struct BufferReader
{
    private readonly ReadOnlySpan<byte> span;
    private int position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferReader(ReadOnlySpan<byte> s)
    {
        span = s;
        position = 0;
    }

    public readonly bool Eof => position >= span.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (position >= span.Length) throw new InvalidOperationException("Headers: недостаёт данных");
        return span[position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte PeekByte()
    {
        if (position >= span.Length) throw new InvalidOperationException("Headers: недостаёт данных");
        return span[position];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int len)
    {
        if (position + len > span.Length) throw new InvalidOperationException("Headers: недостаёт данных");

        var slice = span.Slice(position, len);
        position += len;

        return slice;
    }
}