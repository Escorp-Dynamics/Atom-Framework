#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Rgba32.
/// Без вычислений — только добавление/удаление альфа-канала.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в Rgb24 (отбрасывает альфа-канал).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromRgba32(Rgba32 rgba) => rgba.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32() => Rgba32.FromRgb24(this);

    #endregion

    #region Batch Conversion (Rgba32 → Rgb24)

    /// <summary>
    /// Пакетная конвертация Rgba32 → Rgb24 с SIMD.
    /// Делегирует в Rgba32.ToRgb24 — только удаление альфа-канала без вычислений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination)
        => FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Rgb24 с SIMD.
    /// Делегирует в Rgba32.ToRgb24 — только удаление альфа-канала без вычислений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        => Rgba32.ToRgb24(source, destination, acceleration);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Rgba32 с SIMD.
    /// Делегирует в Rgba32.FromRgb24 — только добавление альфа-канала без вычислений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgba32(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination)
        => ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Rgba32 с SIMD.
    /// Делегирует в Rgba32.FromRgb24 — только добавление альфа-канала без вычислений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgba32(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
        => Rgba32.FromRgb24(source, destination, acceleration);

    #endregion

}
