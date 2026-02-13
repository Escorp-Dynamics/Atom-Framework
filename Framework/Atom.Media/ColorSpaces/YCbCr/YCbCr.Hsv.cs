#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Hsv.
/// Делегирует реализацию в Hsv.
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (Hsv)

    /// <summary>Конвертирует Hsv в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromHsv(Hsv hsv) => hsv.ToYCbCr();

    /// <summary>Конвертирует YCbCr в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => Hsv.FromYCbCr(this);

    #endregion

    #region Batch Conversion (YCbCr ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<YCbCr> destination)
        => Hsv.ToYCbCr(source, destination);

    /// <summary>Пакетная конвертация YCbCr → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<YCbCr> source, Span<Hsv> destination)
        => Hsv.FromYCbCr(source, destination);

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование YCbCr → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(YCbCr ycbcr) => ycbcr.ToHsv();

    #endregion
}
