#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Bgra32.
/// Делегирует в Bgra32 (прямая SIMD реализация).
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromBgra32(Bgra32 bgra) => bgra.ToYCbCr();

    /// <summary>Конвертирует YCbCr в Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32() => Bgra32.FromYCbCr(this);

    #endregion

    #region Batch Conversion (YCbCr ↔ Bgra32)

    /// <summary>
    /// Пакетная конвертация Bgra32 → YCbCr.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination)
        => Bgra32.ToYCbCr(source, destination);

    /// <summary>
    /// Пакетная конвертация Bgra32 → YCbCr с явным ускорителем.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
        => Bgra32.ToYCbCr(source, destination, acceleration);

    /// <summary>
    /// Пакетная конвертация YCbCr → Bgra32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination)
        => Bgra32.FromYCbCr(source, destination);

    /// <summary>
    /// Пакетная конвертация YCbCr → Bgra32 с явным ускорителем.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => Bgra32.FromYCbCr(source, destination, acceleration);

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgra32 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явное преобразование YCbCr → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(YCbCr ycbcr) => ycbcr.ToBgra32();

    #endregion
}
