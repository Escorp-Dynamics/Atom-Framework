using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression;

/// <summary>
/// Инкрементальный xxHash64 (SEED=0), как требует RFC 8878 для Content Checksum.
/// В конце кадра пишутся младшие 4 байта результата (LE).
/// </summary>
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
        var p0 = BinaryPrimitives.ReadUInt64LittleEndian(chunk[..8]);
        var p1 = BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(8, 8));
        var p2 = BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(16, 8));
        var p3 = BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(24, 8));

        v1 = Round(v1, p0);
        v2 = Round(v2, p1);
        v3 = Round(v3, p2);
        v4 = Round(v4, p3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe ulong UnsafeReadUInt64(int index)
    {
        fixed (byte* p = &memory[index])
            return BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(p, 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe uint UnsafeReadUInt32(int index)
    {
        fixed (byte* p = &memory[index])
            return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(p, 4));
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
        ulong h64;

        unchecked
        {
            if (totalLen >= 32)
            {
                h64 = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                h64 = MergeRound(h64, v1);
                h64 = MergeRound(h64, v2);
                h64 = MergeRound(h64, v3);
                h64 = MergeRound(h64, v4);
            }
            else
            {
                h64 = Prime5;
            }

            h64 += totalLen;
            var idx = 0;

            while (idx + 8 <= memSize)
            {
                var k1 = UnsafeReadUInt64(idx);
                h64 ^= Round(0, k1);
                h64 = (RotateLeft(h64, 27) * Prime1) + Prime4;
                idx += 8;
            }
            if (idx + 4 <= memSize)
            {
                h64 ^= UnsafeReadUInt32(idx) * Prime1;
                h64 = (RotateLeft(h64, 23) * Prime2) + Prime3;
                idx += 4;
            }
            while (idx < memSize)
            {
                h64 ^= memory[idx] * Prime5;
                h64 = RotateLeft(h64, 11) * Prime1;
                idx++;
            }

            h64 ^= h64 >> 33;
            h64 *= Prime2;
            h64 ^= h64 >> 29;
            h64 *= Prime3;
            h64 ^= h64 >> 32;
        }

        return h64;
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
    private static ulong RotateLeft(ulong v, int n) => (v << n) | (v >> (64 - n));
}