#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Cmyk.
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromCmyk(Cmyk cmyk) => cmyk.ToYCbCr();

    /// <summary>Конвертирует YCbCr в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromYCbCr(this);

    #endregion

    #region Batch Conversion (YCbCr ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination) =>
        Cmyk.ToYCbCr(source, destination);

    /// <summary>Пакетная конвертация Cmyk → YCbCr с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<YCbCr> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToYCbCr(source, destination, acceleration);

    /// <summary>Пакетная конвертация YCbCr → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToCmyk(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination) =>
        Cmyk.FromYCbCr(source, destination);

    /// <summary>Пакетная конвертация YCbCr → Cmyk с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToCmyk(ReadOnlySpan<YCbCr> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromYCbCr(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация YCbCr → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(YCbCr ycbcr) => ycbcr.ToCmyk();

    #endregion
}
