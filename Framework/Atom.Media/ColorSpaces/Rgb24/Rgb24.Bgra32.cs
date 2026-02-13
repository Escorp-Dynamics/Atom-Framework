#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Bgra32.
/// Делегирует реализацию в Bgra32.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в Rgb24 (swap B и R, отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromBgra32(Bgra32 bgra) => bgra.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Bgra32 (swap R и B, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32() => Bgra32.FromRgb24(this);

    #endregion

    #region Batch Conversion (Rgb24 ↔ Bgra32)

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgb24 с SIMD.
    /// Делегирует в Bgra32.ToRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Rgb24> destination)
        => FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgb24 с SIMD.
    /// Делегирует в Bgra32.ToRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        => Bgra32.ToRgb24(source, destination, acceleration);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgra32 с SIMD.
    /// Делегирует в Bgra32.FromRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Rgb24> source, Span<Bgra32> destination)
        => ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgra32 с SIMD.
    /// Делегирует в Bgra32.FromRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Rgb24> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => Bgra32.FromRgb24(source, destination, acceleration);

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgra32 → Rgb24 (отбрасывается альфа).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgb24(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Неявное преобразование Rgb24 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Bgra32(Rgb24 rgb) => rgb.ToBgra32();

    #endregion
}
