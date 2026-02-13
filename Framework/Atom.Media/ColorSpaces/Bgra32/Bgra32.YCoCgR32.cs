#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromBgra32/ToBgra32.
/// </summary>
public readonly partial struct Bgra32
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToBgra32();

    /// <summary>Конвертирует Bgra32 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromBgra32(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Bgra32)

    /// <summary>Пакетная конвертация YCoCgR32 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Bgra32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToBgra32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgra32 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Bgra32> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromBgra32(source, destination, acceleration);

    #endregion
}
