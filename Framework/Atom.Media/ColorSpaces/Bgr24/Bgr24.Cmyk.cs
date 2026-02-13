#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Cmyk.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (Cmyk)

    /// <summary>Конвертирует Cmyk в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromCmyk(Cmyk cmyk) => cmyk.ToBgr24();

    /// <summary>Конвертирует Bgr24 в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => Cmyk.FromBgr24(this);

    #endregion

    #region Batch Conversion (Bgr24 ↔ Cmyk)

    /// <summary>Пакетная конвертация Cmyk → Bgr24.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination) =>
        Cmyk.ToBgr24(source, destination);

    /// <summary>Пакетная конвертация Cmyk → Bgr24 с явным ускорителем.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Bgr24> destination, HardwareAcceleration acceleration) =>
        Cmyk.ToBgr24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgr24 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination) =>
        Cmyk.FromBgr24(source, destination);

    /// <summary>Пакетная конвертация Bgr24 → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(ReadOnlySpan<Bgr24> source, Span<Cmyk> destination, HardwareAcceleration acceleration) =>
        Cmyk.FromBgr24(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Cmyk)

    /// <summary>Явная конвертация Cmyk → Bgr24.</summary>
    public static explicit operator Bgr24(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация Bgr24 → Cmyk.</summary>
    public static explicit operator Cmyk(Bgr24 bgr) => bgr.ToCmyk();

    #endregion
}
