#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Cmyk.
/// Идёт через промежуточный RGB24.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Cmyk → YCoCgR32 (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromCmyk(Cmyk cmyk)
    {
        // Cmyk → RGB → YCoCgR32
        var rgb = cmyk.ToRgb24();
        return FromRgb24(rgb);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Cmyk (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk()
    {
        // YCoCgR32 → RGB → Cmyk
        var rgb = ToRgb24();
        return Cmyk.FromRgb24(rgb);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Cmyk → YCoCgR32.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<YCoCgR32> destination) =>
        FromCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → YCoCgR32 с явным ускорителем.</summary>
    public static void FromCmyk(
        ReadOnlySpan<Cmyk> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        FromCmykScalar(source, destination);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<YCoCgR32> source, Span<Cmyk> destination) =>
        ToCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Cmyk с явным ускорителем.</summary>
    public static void ToCmyk(
        ReadOnlySpan<YCoCgR32> source,
        Span<Cmyk> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        ToCmykScalar(source, destination);
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromCmykScalar(ReadOnlySpan<Cmyk> source, Span<YCoCgR32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = FromCmyk(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToCmykScalar(ReadOnlySpan<YCoCgR32> source, Span<Cmyk> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = (*src++).ToCmyk();
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Cmyk → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Cmyk cmyk) => FromCmyk(cmyk);

    /// <summary>Явная конвертация YCoCgR32 → Cmyk.</summary>
    public static explicit operator Cmyk(YCoCgR32 ycocg) => ycocg.ToCmyk();

    #endregion
}
