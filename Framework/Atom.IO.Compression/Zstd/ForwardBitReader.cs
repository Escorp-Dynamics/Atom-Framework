using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

[StructLayout(LayoutKind.Auto)]
internal ref struct ForwardBitReader
{
    private readonly ReadOnlySpan<byte> buffer;
    private uint bitContainer;
    private int bitCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ForwardBitReader(ReadOnlySpan<byte> source)
    {
        buffer = source;
        BytesConsumed = 0;
        bitContainer = 0u;
        bitCount = 0;
    }

    public int BytesConsumed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get; private set;
    }

    public readonly int AvailableBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bitCount + ((buffer.Length - BytesConsumed) * 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        EnsureBits(count);
        var mask = count == 32 ? 0xFFFF_FFFFu : (1u << count) - 1u;
        var value = bitContainer & mask;
        bitContainer >>= count;
        bitCount -= count;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureBits(int count)
    {
        while (bitCount < count && BytesConsumed < buffer.Length)
        {
            bitContainer |= (uint)buffer[BytesConsumed++] << bitCount;
            bitCount += 8;
        }
    }
}
