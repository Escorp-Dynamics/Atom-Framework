#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Gray16.
/// </summary>
public readonly partial struct Rgb24
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Rgb24 (R = G = B = Value >> 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 FromGray16(Gray16 gray) => gray.ToRgb24();

    /// <summary>Конвертирует Rgb24 в Gray16 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => Gray16.FromRgb24(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgb24 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Rgb24> source, Span<Gray16> destination) =>
        Gray16.FromRgb24(source, destination);

    /// <summary>Пакетная конвертация Rgb24 → Gray16 с явным указанием ускорителя.</summary>
    public static void ToGray16(ReadOnlySpan<Rgb24> source, Span<Gray16> destination, HardwareAcceleration acceleration) =>
        Gray16.FromRgb24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray16 → Rgb24.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Rgb24> destination) =>
        Gray16.ToRgb24(source, destination);

    /// <summary>Пакетная конвертация Gray16 → Rgb24 с явным указанием ускорителя.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Rgb24> destination, HardwareAcceleration acceleration) =>
        Gray16.ToRgb24(source, destination, acceleration);

    #endregion
}
