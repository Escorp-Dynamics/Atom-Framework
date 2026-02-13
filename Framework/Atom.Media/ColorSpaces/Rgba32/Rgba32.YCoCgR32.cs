#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromRgba32/ToRgba32.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToRgba32();

    /// <summary>Конвертирует Rgba32 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromRgba32(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Rgba32)

    /// <summary>Пакетная конвертация YCoCgR32 → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Rgba32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToRgba32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgba32 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Rgba32> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromRgba32(source, destination, acceleration);

    #endregion
}
