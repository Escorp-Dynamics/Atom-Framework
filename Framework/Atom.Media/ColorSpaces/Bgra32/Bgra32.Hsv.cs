#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Hsv.
/// Делегирует реализацию в Hsv.
/// </summary>
public readonly partial struct Bgra32
{
    #region Single Pixel Conversion (Hsv)

    /// <summary>Конвертирует Hsv в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromHsv(Hsv hsv) => hsv.ToBgra32();

    /// <summary>Конвертирует Bgra32 в Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => Hsv.FromBgra32(this);

    #endregion

    #region Batch Conversion (Bgra32 ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Bgra32> destination)
        => FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => Hsv.ToBgra32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgra32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Bgra32> source, Span<Hsv> destination)
        => ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Bgra32> source, Span<Hsv> destination, HardwareAcceleration acceleration)
        => Hsv.FromBgra32(source, destination, acceleration);

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование Bgra32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Bgra32 bgra) => bgra.ToHsv();

    #endregion
}
