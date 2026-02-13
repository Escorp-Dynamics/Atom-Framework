#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromYCbCr/ToYCbCr.
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToYCbCr();

    /// <summary>Конвертирует YCbCr → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromYCbCr(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ YCbCr)

    /// <summary>Пакетная конвертация YCoCgR32 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<YCbCr> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → YCbCr с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToYCbCr(source, destination, acceleration);

    /// <summary>Пакетная конвертация YCbCr → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<YCbCr> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<YCbCr> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromYCbCr(source, destination, acceleration);

    #endregion
}
