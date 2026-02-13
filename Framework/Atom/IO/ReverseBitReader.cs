#pragma warning disable CA1000, CA1815, S109, S4136, S4275, MA0071, S1066, MA0015, S3928, IDE0032

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO;

/// <summary>
/// Высокопроизводительный читатель битового потока в обратном направлении.
/// </summary>
/// <remarks>
/// <para>
/// Читает биты с конца данных к началу (reverse reading).
/// Используется в алгоритмах, где битовый поток кодируется в обратном порядке:
/// <list type="bullet">
///   <item><b>Zstd FSE:</b> Finite State Entropy декодирование</item>
///   <item><b>Zstd Sequences:</b> декодирование последовательностей</item>
///   <item><b>ANS:</b> Asymmetric Numeral Systems</item>
/// </list>
/// </para>
/// <para>
/// Особенности:
/// <list type="bullet">
///   <item>LSB-first: биты читаются от младшего к старшему</item>
///   <item>Reverse: чтение идёт с конца буфера к началу</item>
///   <item>64-bit буфер для минимизации обращений к памяти</item>
///   <item>Zero-allocation (ref struct)</item>
/// </list>
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public ref struct ReverseBitReader
{
    #region Fields

    private readonly ReadOnlySpan<byte> data;
    private int index;           // текущий байт (двигается влево: index--)
    private ulong buffer;        // накопленные биты (LSB)
    private int bufferBits;      // количество валидных бит в буфере

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт ReverseBitReader для чтения с конца данных.
    /// </summary>
    /// <param name="data">Данные для чтения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReverseBitReader(ReadOnlySpan<byte> data)
    {
        this.data = data;
        index = data.Length - 1;
        buffer = 0;
        bufferBits = 0;
    }

    #endregion

    #region Properties

    /// <summary>Текущий индекс байта (двигается от конца к началу).</summary>
    public readonly int ByteIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index;
    }

    /// <summary>Количество доступных бит (включая буфер и оставшиеся байты).</summary>
    public readonly int AvailableBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bufferBits + ((index + 1) * 8);
    }

    /// <summary>Достигнуто ли начало данных.</summary>
    public readonly bool IsAtStart
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bufferBits == 0 && index < 0;
    }

    /// <summary>Количество бит в буфере.</summary>
    public readonly int BufferedBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bufferBits;
    }

    /// <summary>Общая длина данных в байтах.</summary>
    public readonly int Length => data.Length;

    #endregion

    #region Read Methods

    /// <summary>
    /// Читает указанное количество бит (0-32).
    /// </summary>
    /// <param name="count">Количество бит для чтения (0-32).</param>
    /// <returns>Прочитанное значение.</returns>
    /// <exception cref="InvalidOperationException">Недостаточно данных.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        if (!TryReadBits(count, out var value))
            ThrowUnderflow(count);
        return value;
    }

    /// <summary>
    /// Пробует прочитать указанное количество бит.
    /// </summary>
    /// <param name="count">Количество бит для чтения (0-32).</param>
    /// <param name="value">Прочитанное значение.</param>
    /// <returns>true если успешно, false если недостаточно данных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBits(int count, out uint value)
    {
        // Заполняем буфер пока недостаточно бит
        while (bufferBits < count)
        {
            if (index < 0)
            {
                value = 0;
                return false;
            }

            buffer |= (ulong)data[index] << bufferBits;
            bufferBits += 8;
            index--;
        }

        // Маска для извлечения count бит
        var mask = count == 32 ? 0xFFFF_FFFFUL : (1UL << count) - 1;
        value = (uint)(buffer & mask);

        // Сдвигаем буфер
        buffer >>= count;
        bufferBits -= count;

        return true;
    }

    /// <summary>
    /// Читает один бит.
    /// </summary>
    /// <returns>true если бит = 1, false если бит = 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBit()
    {
        if (!TryReadBits(1, out var value))
            ThrowUnderflow(1);
        return value != 0;
    }

    /// <summary>
    /// Пробует прочитать один бит.
    /// </summary>
    /// <param name="bit">Прочитанный бит.</param>
    /// <returns>true если успешно.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBit(out bool bit)
    {
        if (TryReadBits(1, out var value))
        {
            bit = value != 0;
            return true;
        }

        bit = false;
        return false;
    }

    /// <summary>
    /// Просматривает биты без продвижения позиции.
    /// </summary>
    /// <param name="count">Количество бит для просмотра (0-32).</param>
    /// <returns>Значение бит без изменения позиции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint PeekBits(int count)
    {
        if (count == 0)
            return 0;

        // Создаём копию для чтения без изменения состояния
        var temp = this;
        return temp.TryReadBits(count, out var value) ? value : 0;
    }

    #endregion

    #region Padding Methods

    /// <summary>
    /// Пропускает padding в начале битового потока (RFC 8878).
    /// </summary>
    /// <remarks>
    /// <para>
    /// По спецификации Zstd/FSE в конце данных (начале битового потока при обратном чтении)
    /// находится: 0-7 нулевых битов, затем один бит '1'.
    /// </para>
    /// <para>
    /// Этот метод пропускает нули и '1', позиционируя ридер на начало полезных данных.
    /// </para>
    /// </remarks>
    /// <returns>true если padding успешно пропущен, false если некорректный формат.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySkipPadding()
    {
        // Ищем бит '1' среди padding-нулей
        while (true)
        {
            if (!TryReadBits(1, out var bit))
                return false;

            if (bit == 1)
                break;
        }

        // Сбрасываем остаток буфера (старшие нули после '1')
        // чтобы следующее чтение начиналось с предыдущего байта
        buffer = 0;
        bufferBits = 0;

        return true;
    }

    /// <summary>
    /// Пропускает padding, бросает исключение при ошибке.
    /// </summary>
    /// <exception cref="InvalidOperationException">Некорректный формат padding.</exception>
    public void SkipPadding()
    {
        if (!TrySkipPadding())
            ThrowInvalidPadding();
    }

    #endregion

    #region Reset

    /// <summary>
    /// Сбрасывает ридер в начальное состояние (конец данных).
    /// </summary>
    public void Reset()
    {
        index = data.Length - 1;
        buffer = 0;
        bufferBits = 0;
    }

    #endregion

    #region Exception Helpers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUnderflow(int needed) =>
        throw new InvalidOperationException(
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Недостаточно данных в обратном битовом потоке (требуется {needed} бит)"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidPadding() =>
        throw new InvalidOperationException(
            "Некорректный padding в битовом потоке (не найден завершающий бит '1')");

    #endregion
}
