#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Hsv.
/// Делегирует реализацию в Hsv.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion (Hsv)

    /// <summary>Конвертирует Hsv в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromHsv(Hsv hsv) => hsv.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => Hsv.FromRgba32(this);

    #endregion

    #region Batch Conversion (Rgba32 ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Rgba32> destination)
        => FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
        => Hsv.ToRgba32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgba32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Rgba32> source, Span<Hsv> destination)
        => ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Rgba32> source, Span<Hsv> destination, HardwareAcceleration acceleration)
        => Hsv.FromRgba32(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование Rgba32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Rgba32 rgba) => rgba.ToHsv();

    #endregion
}
