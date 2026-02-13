#pragma warning disable CA1000, IDE0290

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// Представляет код Хаффмана для одного символа.
/// </summary>
/// <remarks>
/// Компактная структура (8 байт) для хранения:
/// - Символ (до 16 бит для расширенных алфавитов)
/// - Код (до 32 бит)
/// - Длина кода (до 16 бит, обычно ≤16)
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public readonly struct HuffmanCode : IEquatable<HuffmanCode>
{
    #region Fields

    /// <summary>Символ (индекс в алфавите).</summary>
    public readonly ushort Symbol;

    /// <summary>Длина кода в битах.</summary>
    public readonly byte Length;

    /// <summary>Зарезервировано для выравнивания.</summary>
    private readonly byte reserved;

    /// <summary>Битовый код (MSB или LSB в зависимости от контекста).</summary>
    public readonly uint Code;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт код Хаффмана.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HuffmanCode(ushort symbol, uint code, byte length)
    {
        Symbol = symbol;
        Code = code;
        Length = length;
        reserved = 0;
    }

    /// <summary>
    /// Создаёт код Хаффмана для 8-битного символа.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HuffmanCode(byte symbol, uint code, byte length)
        : this((ushort)symbol, code, length)
    {
    }

    #endregion

    #region Properties

    /// <summary>Признак отсутствующего символа (нулевая длина).</summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Length == 0;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Возвращает код с обратным порядком бит (LSB ↔ MSB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HuffmanCode Reverse()
    {
        if (Length == 0) return this;

        var reversed = ReverseBits(Code, Length);
        return new HuffmanCode(Symbol, reversed, Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReverseBits(uint value, int bitCount)
    {
        // Обращаем биты через swap-маски
        value = ((value & 0x55555555u) << 1) | ((value >> 1) & 0x55555555u);
        value = ((value & 0x33333333u) << 2) | ((value >> 2) & 0x33333333u);
        value = ((value & 0x0F0F0F0Fu) << 4) | ((value >> 4) & 0x0F0F0F0Fu);
        value = ((value & 0x00FF00FFu) << 8) | ((value >> 8) & 0x00FF00FFu);
        value = (value << 16) | (value >> 16);
        return value >> (32 - bitCount);
    }

    #endregion

    #region Equality

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(HuffmanCode other) =>
        Symbol == other.Symbol &&
        Code == other.Code &&
        Length == other.Length;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HuffmanCode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Symbol, Code, Length);

    /// <summary>Оператор равенства.</summary>
    public static bool operator ==(HuffmanCode left, HuffmanCode right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    public static bool operator !=(HuffmanCode left, HuffmanCode right) => !left.Equals(right);

    #endregion

    #region ToString

    /// <inheritdoc />
    public override string ToString() =>
        Length == 0
            ? $"HuffmanCode(Symbol={Symbol}, Empty)"
            : $"HuffmanCode(Symbol={Symbol}, Code=0b{Convert.ToString(Code, 2).PadLeft(Length, '0')}, Length={Length})";

    #endregion
}
