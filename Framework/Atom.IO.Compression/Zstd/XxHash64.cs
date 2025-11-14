using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression;

/// <summary>
/// Инкрементальный xxHash64 (SEED=0), как требует RFC 8878 для Content Checksum.
/// В конце кадра пишутся младшие 4 байта результата (LE).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct XxHash64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    private ulong v1, v2, v3, v4, totalLen;

    private int memSize;
    private unsafe fixed byte memory[32];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public XxHash64()
    {
        v1 = unchecked(Prime1 + Prime2);
        v2 = Prime2;
        v3 = 0;
        v4 = unchecked(-(long)Prime1);
        totalLen = 0;
        memSize = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessChunk(ReadOnlySpan<byte> chunk)
    {
        unsafe
        {
            fixed (byte* p = &chunk.GetPinnableReference())
            {
                var p0 = Unsafe.ReadUnaligned<ulong>(p + 0);
                var p1 = Unsafe.ReadUnaligned<ulong>(p + 8);
                var p2 = Unsafe.ReadUnaligned<ulong>(p + 16);
                var p3 = Unsafe.ReadUnaligned<ulong>(p + 24);
                v1 = Round(v1, p0);
                v2 = Round(v2, p1);
                v3 = Round(v3, p2);
                v4 = Round(v4, p3);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe ulong UnsafeReadUInt64(int index)
    {
        fixed (byte* p = &memory[index])
            return Unsafe.ReadUnaligned<ulong>(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe uint UnsafeReadUInt32(int index)
    {
        fixed (byte* p = &memory[index])
            return Unsafe.ReadUnaligned<uint>(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Update(ReadOnlySpan<byte> data)
    {
        totalLen += (ulong)data.Length;
        var index = 0;

        if (memSize + data.Length < 32)
        {
            for (; index < data.Length; index++) memory[memSize++] = data[index];
            return;
        }

        if (memSize > 0)
        {
            while (memSize < 32 && index < data.Length) memory[memSize++] = data[index++];
            ProcessChunk(new ReadOnlySpan<byte>(Unsafe.AsPointer(ref memory[0]), 32));
            memSize = 0;
        }

        while (index + 32 <= data.Length)
        {
            ProcessChunk(data.Slice(index, 32));
            index += 32;
        }

        for (; index < data.Length; index++) memory[memSize++] = data[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateRepeat(byte value, int count)
    {
        const int Temp = 128;
        Span<byte> tmp = stackalloc byte[Temp];
        tmp.Fill(value);

        while (count > 0)
        {
            var n = count > Temp ? Temp : count;
            Update(tmp[..n]);
            count -= n;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong Digest()
    {
        unchecked
        {
            var h64 = totalLen >= 32 ? MixState() : Prime5;
            h64 += totalLen;
            var idx = 0;

            ProcessRemaining(ref h64, ref idx);
            return Avalanche(h64);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong acc, ulong input) => unchecked(RotateLeft(acc + (input * Prime2), 31) * Prime1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeRound(ulong acc, ulong val)
    {
        acc ^= Round(0, val);
        acc = unchecked((acc * Prime1) + Prime4);
        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ulong MixState()
    {
        var result = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
        result = MergeRound(result, v1);
        result = MergeRound(result, v2);
        result = MergeRound(result, v3);
        result = MergeRound(result, v4);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ProcessRemaining(ref ulong hash, ref int index)
    {
        while (index + 8 <= memSize)
        {
            ConsumeEight(ref hash, ref index);
        }

        if (index + 4 <= memSize)
        {
            hash ^= UnsafeReadUInt32(index) * Prime1;
            hash = (RotateLeft(hash, 23) * Prime2) + Prime3;
            index += 4;
        }

        while (index < memSize)
        {
            hash ^= memory[index] * Prime5;
            hash = RotateLeft(hash, 11) * Prime1;
            index++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ConsumeEight(ref ulong hash, ref int index)
    {
        var lane = UnsafeReadUInt64(index);
        hash ^= Round(0, lane);
        hash = (RotateLeft(hash, 27) * Prime1) + Prime4;
        index += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Avalanche(ulong value)
    {
        value ^= value >> 33;
        value *= Prime2;
        value ^= value >> 29;
        value *= Prime3;
        value ^= value >> 32;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong v, int n) => (v << n) | (v >> (64 - n));
}
