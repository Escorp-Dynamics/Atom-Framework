#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Gray16.
/// </summary>
public readonly partial struct Gray8
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Gray8 (Value >> 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromGray16(Gray16 gray) => gray.ToGray8();

    /// <summary>Конвертирует Gray8 в Gray16 (Value * 257).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => Gray16.FromGray8(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Gray8 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Gray8> source, Span<Gray16> destination) =>
        Gray16.FromGray8(source, destination);

    /// <summary>Пакетная конвертация Gray8 → Gray16 с явным указанием ускорителя.</summary>
    public static void ToGray16(ReadOnlySpan<Gray8> source, Span<Gray16> destination, HardwareAcceleration acceleration) =>
        Gray16.FromGray8(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray16 → Gray8.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Gray8> destination) =>
        Gray16.ToGray8(source, destination);

    /// <summary>Пакетная конвертация Gray16 → Gray8 с явным указанием ускорителя.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Gray8> destination, HardwareAcceleration acceleration) =>
        Gray16.ToGray8(source, destination, acceleration);

    #endregion
}
