#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Cmyk.
/// </summary>
public readonly partial struct Bgra32
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromCmyk(Cmyk cmyk) => cmyk.ToBgra32();

    /// <summary>Конвертирует Bgra32 в Cmyk (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromBgra32(this);

    #endregion

    #region Batch Conversion (Bgra32 ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → Bgra32.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination) =>
        Cmyk.ToBgra32(source, destination);

    /// <summary>Пакетная конвертация Cmyk → Bgra32 с явным ускорителем.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Bgra32> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToBgra32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgra32 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination) =>
        Cmyk.FromBgra32(source, destination);

    /// <summary>Пакетная конвертация Bgra32 → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(ReadOnlySpan<Bgra32> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromBgra32(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → Bgra32.</summary>
    public static explicit operator Bgra32(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация Bgra32 → Cmyk.</summary>
    public static explicit operator Cmyk(Bgra32 bgra) => bgra.ToCmyk();

    #endregion
}
