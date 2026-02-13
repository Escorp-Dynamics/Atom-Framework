#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Cmyk.
/// </summary>
public readonly partial struct Hsv
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromCmyk(Cmyk cmyk) => cmyk.ToHsv();

    /// <summary>Конвертирует Hsv в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromHsv(this);

    #endregion

    #region Batch Conversion (Hsv ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → Hsv.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Hsv> destination) =>
        Cmyk.ToHsv(source, destination);

    /// <summary>Пакетная конвертация Cmyk → Hsv с явным ускорителем.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Hsv> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToHsv(source, destination, acceleration);

    /// <summary>Пакетная конвертация Hsv → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Hsv> source, Span<Cmyk> destination) =>
        Cmyk.FromHsv(source, destination);

    /// <summary>Пакетная конвертация Hsv → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(ReadOnlySpan<Hsv> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromHsv(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → Hsv.</summary>
    public static explicit operator Hsv(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация Hsv → Cmyk.</summary>
    public static explicit operator Cmyk(Hsv hsv) => hsv.ToCmyk();

    #endregion
}
