using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Ридер битов (LSB-first) для чтения в обратном направлении (с конца к началу).
/// Используется для декодирования FSE/Sequences по спецификации zstd.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct LittleEndianReverseBitReader
{
    private readonly ReadOnlySpan<byte> src;
    private int index;          // текущий байт (включительно), двигается влево: src[index], index--
    private uint bitContainer;  // младшие биты
    private int bitCount;       // число валидных бит в контейнере

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LittleEndianReverseBitReader(ReadOnlySpan<byte> data)
    {
        src = data;
        index = src.Length - 1;
        bitContainer = 0u;
        bitCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadInternal(int nb, out uint value)
    {
        while (bitCount < nb)
        {
            if (index < 0)
            {
                value = 0;
                return false;
            }
            bitContainer |= (uint)src[index] << bitCount;
            bitCount += 8;
            index--;
        }

        value = bitContainer & ((nb == 32) ? 0xFFFF_FFFFu : ((1u << nb) - 1u));
        bitContainer >>= nb;
        bitCount -= nb;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBits(int nb, out uint value) => TryReadInternal(nb, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int nb)
    {
        if (!TryReadInternal(nb, out var val))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow in reverse bit reader (need={nb}, have={bitCount}, index={index})"));
        return val;
    }

    /// <summary>
    /// В начале битстрима (самый конец данных) спецификация требует: сначала идут 0..7 нулевых битов, затем один бит '1'.
    /// Этот метод пропускает нули и '1' и позиционирует ридер на начало полезного битстрима.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySkipPadding()
    {
        while (true)
        {
            if (!TryReadInternal(1, out var bit))
            {
                return false;
            }
            if (bit == 1) break;
        }
        // Отбросить остаток последнего байта (старшие нули после '1'),
        // чтобы последующее чтение начиналось с предыдущего байта.
        bitContainer = 0u;
        bitCount = 0;
        return true;
    }
}
