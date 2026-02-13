#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Cmyk.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromCmyk(Cmyk cmyk) => cmyk.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromRgb24(this);

    #endregion

    #region Batch Conversion (Rgb24 ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → Rgb24.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination) =>
        Cmyk.ToRgb24(source, destination);

    /// <summary>Пакетная конвертация Cmyk → Rgb24 с явным ускорителем.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Rgb24> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToRgb24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgb24 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination) =>
        Cmyk.FromRgb24(source, destination);

    /// <summary>Пакетная конвертация Rgb24 → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(ReadOnlySpan<Rgb24> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromRgb24(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → Rgb24.</summary>
    public static explicit operator Rgb24(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация Rgb24 → Cmyk.</summary>
    public static explicit operator Cmyk(Rgb24 rgb) => rgb.ToCmyk();

    #endregion
}
