#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Gray8.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Rgb24 (R = G = B = Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromGray8(Gray8 gray) => gray.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => Gray8.FromRgb24(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgb24 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Rgb24> source, Span<Gray8> destination) =>
        Gray8.FromRgb24(source, destination);

    /// <summary>Пакетная конвертация Rgb24 → Gray8 с явным указанием ускорителя.</summary>
    public static void ToGray8(ReadOnlySpan<Rgb24> source, Span<Gray8> destination, HardwareAcceleration acceleration) =>
        Gray8.FromRgb24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray8 → Rgb24.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Rgb24> destination) =>
        Gray8.ToRgb24(source, destination);

    /// <summary>Пакетная конвертация Gray8 → Rgb24 с явным указанием ускорителя.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Rgb24> destination, HardwareAcceleration acceleration) =>
        Gray8.ToRgb24(source, destination, acceleration);

    #endregion
}
