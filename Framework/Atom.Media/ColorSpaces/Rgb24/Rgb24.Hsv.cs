#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Hsv.
/// Делегирует реализацию в Hsv.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (Hsv)

    /// <summary>Конвертирует Hsv в Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromHsv(Hsv hsv) => hsv.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => Hsv.FromRgb24(this);

    #endregion

    #region Batch Conversion (Rgb24 ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Rgb24> destination)
        => FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        => Hsv.ToRgb24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgb24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Rgb24> source, Span<Hsv> destination)
        => ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgb24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Rgb24> source, Span<Hsv> destination, HardwareAcceleration acceleration)
        => Hsv.FromRgb24(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgb24(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование Rgb24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Rgb24 rgb) => rgb.ToHsv();

    #endregion
}
