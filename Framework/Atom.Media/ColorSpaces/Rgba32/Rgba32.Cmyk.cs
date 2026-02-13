#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Cmyk.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromCmyk(Cmyk cmyk) => cmyk.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Cmyk (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromRgba32(this);

    #endregion

    #region Batch Conversion (Rgba32 ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → Rgba32.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination) =>
        Cmyk.ToRgba32(source, destination);

    /// <summary>Пакетная конвертация Cmyk → Rgba32 с явным ускорителем.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToRgba32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgba32 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination) =>
        Cmyk.FromRgba32(source, destination);

    /// <summary>Пакетная конвертация Rgba32 → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromRgba32(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → Rgba32.</summary>
    public static explicit operator Rgba32(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация Rgba32 → Cmyk.</summary>
    public static explicit operator Cmyk(Rgba32 rgba) => rgba.ToCmyk();

    #endregion
}
