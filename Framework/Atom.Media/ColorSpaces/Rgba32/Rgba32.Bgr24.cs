#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Bgr24.
/// Прямой SIMD: swap R и B + добавление/удаление альфа-канала.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в Rgba32 (swap B и R, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromBgr24(Bgr24 bgr) => bgr.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Bgr24 (swap R и B, отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24() => Bgr24.FromRgba32(this);

    #endregion

    #region Batch Conversion (Rgba32 ↔ Bgr24)

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgba32 с SIMD.
    /// Делегирует в Bgr24.ToRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Rgba32> destination)
        => Bgr24.ToRgba32(source, destination);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgr24 с SIMD.
    /// Делегирует в Bgr24.FromRgba32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<Rgba32> source, Span<Bgr24> destination)
        => Bgr24.FromRgba32(source, destination);

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgr24 → Rgba32 (добавляется альфа).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Неявное преобразование Rgba32 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Bgr24(Rgba32 rgba) => rgba.ToBgr24();

    #endregion
}
