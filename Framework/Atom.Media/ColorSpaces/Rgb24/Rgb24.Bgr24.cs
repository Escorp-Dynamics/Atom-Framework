#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Bgr24.
/// Чистый swap B и R каналов — без вычислений.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в Rgb24 (swap B и R).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromBgr24(Bgr24 bgr) => bgr.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Bgr24 (swap R и B).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24() => Bgr24.FromRgb24(this);

    #endregion

    #region Batch Conversion (Rgb24 ↔ Bgr24)

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgb24 с SIMD.
    /// Делегирует в Bgr24.ToRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Rgb24> destination)
        => FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgb24 с SIMD.
    /// Делегирует в Bgr24.ToRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        => Bgr24.ToRgb24(source, destination, acceleration);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgr24 с SIMD.
    /// Делегирует в Bgr24.FromRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<Rgb24> source, Span<Bgr24> destination)
        => ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgr24 с SIMD.
    /// Делегирует в Bgr24.FromRgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<Rgb24> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
        => Bgr24.FromRgb24(source, destination, acceleration);

    #endregion

    #region Conversion Operators

    /// <summary>Неявное преобразование Bgr24 → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rgb24(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Неявное преобразование Rgb24 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Bgr24(Rgb24 rgb) => rgb.ToBgr24();

    #endregion
}
