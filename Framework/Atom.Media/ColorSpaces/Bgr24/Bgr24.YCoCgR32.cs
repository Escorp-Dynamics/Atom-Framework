#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ YCoCgR32.
/// Делегирует в YCoCgR32.FromBgr24/ToBgr24.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (YCoCgR32)

    /// <summary>Конвертирует YCoCgR32 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromYCoCgR32(YCoCgR32 ycocg) => ycocg.ToBgr24();

    /// <summary>Конвертирует Bgr24 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => YCoCgR32.FromBgr24(this);

    #endregion

    #region Batch Conversion (YCoCgR32 ↔ Bgr24)

    /// <summary>Пакетная конвертация YCoCgR32 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination)
        => FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Bgr24 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
        => YCoCgR32.ToBgr24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgr24 → YCoCgR32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination)
        => ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → YCoCgR32 с ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCoCgR32(ReadOnlySpan<Bgr24> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        => YCoCgR32.FromBgr24(source, destination, acceleration);

    #endregion
}
