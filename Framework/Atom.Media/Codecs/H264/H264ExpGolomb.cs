#pragma warning disable S109, MA0051, IDE0048

using System.Runtime.CompilerServices;
using Atom.IO;

namespace Atom.Media;

/// <summary>
/// Exp-Golomb кодирование/декодирование (ITU-T H.264 Section 9.1).
/// </summary>
/// <remarks>
/// Используется для синтаксических элементов H.264: SPS, PPS, slice header.
/// Работает в MSB-first bit order.
/// </remarks>
internal static class H264ExpGolomb
{
    /// <summary>
    /// Декодирует unsigned Exp-Golomb code (ue(v)).
    /// </summary>
    /// <remarks>
    /// Формат: [M нулей][1][M info бит] → значение = 2^M + info - 1.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUe(ref BitReader reader)
    {
        var leadingZeros = 0;

        while (reader.ReadBits(1) == 0)
        {
            leadingZeros++;

            if (leadingZeros > 31)
                return uint.MaxValue;
        }

        if (leadingZeros == 0)
            return 0;

        var info = reader.ReadBits(leadingZeros);
        return (1u << leadingZeros) - 1 + info;
    }

    /// <summary>
    /// Декодирует signed Exp-Golomb code (se(v)).
    /// </summary>
    /// <remarks>
    /// codeNum → value: 0→0, 1→1, 2→-1, 3→2, 4→-2, ...
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadSe(ref BitReader reader)
    {
        var codeNum = ReadUe(ref reader);

        // Odd → positive, Even → negative
        var value = (int)((codeNum + 1) >> 1);
        return (codeNum & 1) == 0 ? -value : value;
    }

    /// <summary>
    /// Декодирует mapped Exp-Golomb code (me(v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadMe(ref BitReader reader) => ReadUe(ref reader);

    /// <summary>
    /// Декодирует truncated Exp-Golomb code (te(v)) с заданным range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadTe(ref BitReader reader, uint range)
    {
        if (range <= 0)
            return 0;

        if (range == 1)
            return 1u - reader.ReadBits(1);

        return ReadUe(ref reader);
    }

    /// <summary>
    /// Записывает unsigned Exp-Golomb code (ue(v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUe(ref BitWriter writer, uint value)
    {
        if (value == 0)
        {
            writer.WriteBits(1, 1);
            return;
        }

        var codeNum = value + 1;
        var leadingZeros = 31 - BitOperations.LeadingZeroCount(codeNum);

        // Write M zeros
        writer.WriteBits(0, leadingZeros);

        // Write 1 + M info bits
        writer.WriteBits(codeNum, leadingZeros + 1);
    }

    /// <summary>
    /// Записывает signed Exp-Golomb code (se(v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSe(ref BitWriter writer, int value)
    {
        var codeNum = value <= 0
            ? (uint)(-value * 2)
            : (uint)((value * 2) - 1);

        WriteUe(ref writer, codeNum);
    }
}

/// <summary>
/// Вспомогательные bit-level операции.
/// </summary>
file static class BitOperations
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LeadingZeroCount(uint value) =>
        System.Numerics.BitOperations.LeadingZeroCount(value);
}
