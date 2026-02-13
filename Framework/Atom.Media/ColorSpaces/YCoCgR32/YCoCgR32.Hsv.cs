#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Hsv.
/// Идёт через промежуточный RGB24.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Hsv → YCoCgR32 (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromHsv(Hsv hsv)
    {
        // Hsv → RGB → YCoCgR32
        var rgb = hsv.ToRgb24();
        return FromRgb24(rgb);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Hsv (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv()
    {
        // YCoCgR32 → RGB → Hsv
        var rgb = ToRgb24();
        return Hsv.FromRgb24(rgb);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Hsv → YCoCgR32.</summary>
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<YCoCgR32> destination) =>
        FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → YCoCgR32 с явным ускорителем.</summary>
    public static void FromHsv(
        ReadOnlySpan<Hsv> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        FromHsvScalar(source, destination);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Hsv.</summary>
    public static void ToHsv(ReadOnlySpan<YCoCgR32> source, Span<Hsv> destination) =>
        ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Hsv с явным ускорителем.</summary>
    public static void ToHsv(
        ReadOnlySpan<YCoCgR32> source,
        Span<Hsv> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        ToHsvScalar(source, destination);
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromHsvScalar(ReadOnlySpan<Hsv> source, Span<YCoCgR32> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = FromHsv(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToHsvScalar(ReadOnlySpan<YCoCgR32> source, Span<Hsv> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = (*src++).ToHsv();
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Hsv → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явная конвертация YCoCgR32 → Hsv.</summary>
    public static explicit operator Hsv(YCoCgR32 ycocg) => ycocg.ToHsv();

    #endregion
}
