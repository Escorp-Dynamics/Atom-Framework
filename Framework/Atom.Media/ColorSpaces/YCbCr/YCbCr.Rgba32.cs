#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Rgba32.
/// Прямой SIMD без промежуточного буфера.
/// </summary>
public readonly partial struct YCbCr
{
    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в YCbCr (игнорирует альфа-канал).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromRgba32(Rgba32 rgba) => rgba.ToYCbCr();

    /// <summary>Конвертирует YCbCr в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32() => Rgba32.FromYCbCr(this);

    #endregion

    #region Batch Conversion (Rgba32 → YCbCr)

    /// <summary>
    /// Пакетная конвертация Rgba32 → YCbCr с прямым SIMD.
    /// Делегирует в Rgba32.ToYCbCr.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<YCbCr> destination)
        => Rgba32.ToYCbCr(source, destination);

    /// <summary>
    /// Пакетная конвертация YCbCr → Rgba32 с прямым SIMD.
    /// Делегирует в Rgba32.FromYCbCr.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgba32(ReadOnlySpan<YCbCr> source, Span<Rgba32> destination)
        => Rgba32.FromYCbCr(source, destination);

    #endregion

}
