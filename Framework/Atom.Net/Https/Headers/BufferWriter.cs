using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

internal ref struct BufferWriter(IBufferWriter<byte> w)
{
    private readonly IBufferWriter<byte> writer = w;
    private Span<byte> span = w.GetSpan(256);
    private int position = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(scoped ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(span[position..]);
        position += data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte b)
    {
        Ensure(1);
        span[position++] = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        if (position <= 0) return;

        writer.Advance(position);
        span = default;
        position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ensure(int need)
    {
        if (span.Length - position >= need) return;

        Flush();
        span = writer.GetSpan(need);
    }
}