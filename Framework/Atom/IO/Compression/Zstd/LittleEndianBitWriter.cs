using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Высокопроизводительный writer битов в формате "little-endian bits" (LSB-first),
/// как требует RFC 8878 для FSE/доп.битов последовательностей.
/// </summary>
internal ref struct LittleEndianBitWriter
{
    private Span<byte> dst;
    private uint bitContainer; // накапливает младшие биты
    private int bitCount;      // сколько полезных бит сейчас в контейнере (0..31)

    public int BytesWritten { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LittleEndianBitWriter(Span<byte> dst)
    {
        this.dst = dst;
        BytesWritten = 0;
        bitContainer = 0;
        bitCount = 0;
    }

    /// <summary>Записать nbBits младших бит значения value (LSB-first).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBits(uint value, int nbBits)
    {
        // Контракт: nbBits в диапазоне 0..24 (в нашем применении).
        bitContainer |= (value & ((nbBits == 32) ? 0xFFFFFFFFu : ((1u << nbBits) - 1u))) << bitCount;
        bitCount += nbBits;

        // Сбрасываем целые байты
        while (bitCount >= 8)
        {
            if ((uint)BytesWritten >= (uint)dst.Length) ThrowNoSpace();
            dst[BytesWritten++] = (byte)(bitContainer & 0xFF);
            bitContainer >>= 8;
            bitCount -= 8;
        }
    }

    /// <summary>
    /// Финализация по RFC: записать завершающий '1' бит и добить нулями до конца байта.
    /// Последний байт не может быть 0 (см. спецификацию).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FinishWithOnePadding()
    {
        WriteBits(1, 1);

        if (bitCount > 0)
        {
            // добиваем текущий байт нулями
            if ((uint)BytesWritten >= (uint)dst.Length) ThrowNoSpace();

            dst[BytesWritten++] = (byte)(bitContainer & 0xFF);
            bitContainer = 0;
            bitCount = 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoSpace() => throw new InvalidOperationException("Недостаточно места в выходном буфере");
}