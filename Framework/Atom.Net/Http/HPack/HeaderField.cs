using System.Runtime.CompilerServices;
using System.Text;

namespace Atom.Net.Http.HPack;

internal struct HeaderField
{
    public const int RfcOverhead = 32;

    private readonly byte[] name;
    private readonly byte[] value;

    public int? StaticTableIndex { get; }

    public readonly ReadOnlySpan<byte> Name => name;

    public readonly ReadOnlySpan<byte> Value => value;

    public readonly int Length => GetLength(name.Length, value.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeaderField(int? staticTableIndex, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        StaticTableIndex = staticTableIndex;
        this.name = name.ToArray();
        this.value = value.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override readonly string ToString()
    {
        if (!Name.IsEmpty) return Encoding.Latin1.GetString(Name) + ": " + Encoding.Latin1.GetString(Value);
        return "<empty>";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLength(int nameLength, int valueLength) => nameLength + valueLength + RfcOverhead;
}