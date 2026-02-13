#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromRgb24/ToRgb24.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToRgb24();

    /// <summary>Конвертирует Rgb24 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromRgb24(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Rgb24)

    /// <summary>Пакетная конвертация YCoCgR32 → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Rgb24 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToRgb24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Rgb24 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgb24 → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Rgb24> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromRgb24(source, destination, acceleration);

    #endregion
}
