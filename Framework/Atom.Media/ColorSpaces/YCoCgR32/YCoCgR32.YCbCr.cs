#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ YCbCr.
/// Идёт через промежуточный RGB24, так как YCbCr lossy.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует YCbCr → YCoCgR32 (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromYCbCr(YCbCr ycbcr)
    {
        // YCbCr → RGB → YCoCgR32
        var rgb = Rgb24.FromYCbCr(ycbcr);
        return FromRgb24(rgb);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → YCbCr (через RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr()
    {
        // YCoCgR32 → RGB → YCbCr
        var rgb = ToRgb24();
        return YCbCr.FromRgb24(rgb);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация YCbCr → YCoCgR32.</summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<YCoCgR32> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → YCoCgR32 с явным ускорителем.</summary>
    public static void FromYCbCr(
        ReadOnlySpan<YCbCr> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        FromYCbCrScalar(source, destination);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → YCbCr.</summary>
    public static void ToYCbCr(ReadOnlySpan<YCoCgR32> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → YCbCr с явным ускорителем.</summary>
    public static void ToYCbCr(
        ReadOnlySpan<YCoCgR32> source,
        Span<YCbCr> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        ToYCbCrScalar(source, destination);
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<YCoCgR32> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = FromYCbCr(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<YCoCgR32> source, Span<YCbCr> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;

            while (src < end)
                *dst++ = (*src++).ToYCbCr();
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация YCbCr → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явная конвертация YCoCgR32 → YCbCr.</summary>
    public static explicit operator YCbCr(YCoCgR32 ycocg) => ycocg.ToYCbCr();

    #endregion
}
