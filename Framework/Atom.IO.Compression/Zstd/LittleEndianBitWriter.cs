using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Высокопроизводительный writer битов в формате "little-endian bits" (LSB-first),
/// как требует RFC 8878 для FSE/доп.битов последовательностей.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct LittleEndianBitWriter
{
    private Span<byte> dst;
    private uint bitContainer; // накапливает младшие биты
    private int bitCount;      // сколько полезных бит сейчас в контейнере (0..31)

    public int BytesWritten { get; private set; }
    public bool IsOverflow { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LittleEndianBitWriter(Span<byte> dst)
    {
        this.dst = dst;
        BytesWritten = 0;
        bitContainer = 0;
        bitCount = 0;
        IsOverflow = false;
    }

    /// <summary>Записать nbBits младших бит значения value (LSB-first).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteBits(uint value, int nbBits)
    {
        if (IsOverflow) return false;
        // Контракт: nbBits в диапазоне 0..24 (в нашем применении).
        bitContainer |= (value & ((nbBits == 32) ? 0xFFFFFFFFu : ((1u << nbBits) - 1u))) << bitCount;
        bitCount += nbBits;

        // Сбрасываем целые байты
        while (bitCount >= 8)
        {
            if ((uint)BytesWritten >= (uint)dst.Length)
            {
                IsOverflow = true;
                return false;
            }
            dst[BytesWritten++] = (byte)(bitContainer & 0xFF);
            bitContainer >>= 8;
            bitCount -= 8;
        }
        return true;
    }

    /// <summary>
    /// Финализация по RFC: записать завершающий '1' бит и добить нулями до конца байта.
    /// Последний байт не может быть 0 (см. спецификацию).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFinishWithOnePadding()
    {
        if (IsOverflow) return false;
        // 1) Доводим текущий контейнер до границы байта нулями (если нужно),
        //    чтобы последний полезный байт был уже записан.
        if ((bitCount & 7) != 0)
        {
            var pad = 8 - (bitCount & 7);
            if (!TryWriteBits(0, pad)) return false; // это вызовет сброс неполного байта при необходимости
        }

        // 2) Записываем завершающий бит '1' (LSB-first) в пустой контейнер
        if (!TryWriteBits(1, 1)) return false;

        // 3) Сбрасываем последний байт (он будет иметь вид 0b0000_0001)
        if (bitCount > 0)
        {
            if ((uint)BytesWritten >= (uint)dst.Length)
            {
                IsOverflow = true;
                return false;
            }
            dst[BytesWritten++] = (byte)(bitContainer & 0xFF);
            bitContainer = 0;
            bitCount = 0;
        }
        return true;
    }
}
