using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Headers;

[StructLayout(LayoutKind.Auto)]
internal ref struct BufferReader
{
    private readonly ReadOnlySpan<byte> span;

    // Поле, а не автосвойство: читатель — ref-структура, и позиция меняется по ссылке.
#pragma warning disable IDE0032
    private int position;
#pragma warning restore IDE0032

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferReader(ReadOnlySpan<byte> s)
    {
        span = s;
        position = 0;
    }

    public readonly bool Eof => position >= span.Length;

    /// <summary>Сколько байт уже прочитано.</summary>
    public readonly int Position => position;

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