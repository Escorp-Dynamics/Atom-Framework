#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromCmyk/ToCmyk.
/// </summary>
public readonly partial struct Cmyk
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToCmyk();

    /// <summary>Конвертирует Cmyk → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromCmyk(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Cmyk)

    /// <summary>Пакетная конвертация YCoCgR32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Cmyk> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Cmyk с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToCmyk(source, destination, acceleration);

    /// <summary>Пакетная конвертация Cmyk → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Cmyk> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Cmyk> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromCmyk(source, destination, acceleration);

    #endregion
}
