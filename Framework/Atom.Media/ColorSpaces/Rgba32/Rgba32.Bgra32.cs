#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Bgra32.
/// Делегирует реализацию в Bgra32 (swap B и R, альфа остаётся).
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в Rgba32 (swap B и R).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromBgra32(Bgra32 bgra) => bgra.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Bgra32 (swap R и B).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32() => Bgra32.FromRgba32(this);

    #endregion

    #region Batch Conversion (Rgba32 ↔ Bgra32)

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgba32 с SIMD.
    /// Делегирует в Bgra32.ToRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Rgba32> destination)
        => FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgba32 с SIMD.
    /// Делегирует в Bgra32.ToRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
        => Bgra32.ToRgba32(source, destination, acceleration);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgra32 с SIMD.
    /// Делегирует в Bgra32.FromRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Rgba32> source, Span<Bgra32> destination)
        => ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgra32 с SIMD.
    /// Делегирует в Bgra32.FromRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Rgba32> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => Bgra32.FromRgba32(source, destination, acceleration);

    #endregion

    #region Conversion Operators

    /// <summary>Неявное преобразование Bgra32 → Rgba32 (lossless).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rgba32(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Неявное преобразование Rgba32 → Bgra32 (lossless).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Bgra32(Rgba32 rgba) => rgba.ToBgra32();

    #endregion
}
