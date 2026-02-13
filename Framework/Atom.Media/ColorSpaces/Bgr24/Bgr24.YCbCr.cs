#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ YCbCr.
/// Делегирует в YCbCr.ToBgr24/FromBgr24 с прямой SIMD реализацией.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (YCbCr)

    /// <summary>Конвертирует YCbCr в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromYCbCr(YCbCr ycbcr)
    {
        var rgb = Rgb24.FromYCbCr(ycbcr);
        return new Bgr24(rgb.B, rgb.G, rgb.R);
    }

    /// <summary>Конвертирует Bgr24 в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr() => YCbCr.FromRgb24(new Rgb24(R, G, B));

    #endregion

    #region Batch Conversion (Bgr24 ↔ YCbCr)

    /// <summary>Пакетная конвертация YCbCr → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination) =>
        YCbCr.ToBgr24(source, destination);

    /// <summary>Пакетная конвертация YCbCr → Bgr24 с явным указанием ускорителя.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Bgr24> destination, HardwareAcceleration acceleration) =>
        YCbCr.ToBgr24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Bgr24 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCbCr(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination) =>
        YCbCr.FromBgr24(source, destination);

    /// <summary>Пакетная конвертация Bgr24 → YCbCr с явным указанием ускорителя.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToYCbCr(ReadOnlySpan<Bgr24> source, Span<YCbCr> destination, HardwareAcceleration acceleration) =>
        YCbCr.FromBgr24(source, destination, acceleration);

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование YCbCr → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явное преобразование Bgr24 → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Bgr24 bgr) => bgr.ToYCbCr();

    #endregion
}
