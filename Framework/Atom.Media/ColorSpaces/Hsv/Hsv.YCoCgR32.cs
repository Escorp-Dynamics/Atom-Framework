#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromHsv/ToHsv.
/// </summary>
public readonly partial struct Hsv
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToHsv();

    /// <summary>Конвертирует Hsv → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromHsv(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Hsv)

    /// <summary>Пакетная конвертация YCoCgR32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Hsv> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Hsv с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Hsv> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToHsv(source, destination, acceleration);

    /// <summary>Пакетная конвертация Hsv → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Hsv> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Hsv> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromHsv(source, destination, acceleration);

    #endregion
}
