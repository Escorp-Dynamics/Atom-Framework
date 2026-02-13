#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ YCbCr.
/// Через промежуточный Rgb24.
/// </summary>
public readonly partial struct Hsv
{
    #region Single Pixel Conversion (YCbCr)

    /// <summary>Конвертирует YCbCr в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromYCbCr(YCbCr ycbcr) => FromRgb24(Rgb24.FromYCbCr(ycbcr));

    /// <summary>Конвертирует Hsv в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr() => YCbCr.FromRgb24(ToRgb24());

    #endregion

    #region Batch Conversion (Hsv ↔ YCbCr)

    /// <summary>Пакетная конвертация YCbCr → Hsv.</summary>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Hsv> destination)
    {
        if (source.IsEmpty) return;

        fixed (YCbCr* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromYCbCr(*src++);
        }
    }

    /// <summary>Пакетная конвертация Hsv → YCbCr.</summary>
    public static unsafe void ToYCbCr(ReadOnlySpan<Hsv> source, Span<YCbCr> destination)
    {
        if (source.IsEmpty) return;

        fixed (Hsv* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToYCbCr();
        }
    }

    #endregion

    #region Conversion Operators (YCbCr)

    /// <summary>Явное преобразование YCbCr → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(YCbCr ycbcr) => FromYCbCr(ycbcr);

    /// <summary>Явное преобразование Hsv → YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator YCbCr(Hsv hsv) => hsv.ToYCbCr();

    #endregion
}
