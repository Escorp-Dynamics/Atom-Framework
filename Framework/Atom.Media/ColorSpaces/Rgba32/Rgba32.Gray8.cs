#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Gray8.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Rgba32 (R = G = B = Value, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromGray8(Gray8 gray) => gray.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => Gray8.FromRgba32(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgba32 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Rgba32> source, Span<Gray8> destination) =>
        Gray8.FromRgba32(source, destination);

    /// <summary>Пакетная конвертация Rgba32 → Gray8 с явным указанием ускорителя.</summary>
    public static void ToGray8(ReadOnlySpan<Rgba32> source, Span<Gray8> destination, HardwareAcceleration acceleration) =>
        Gray8.FromRgba32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray8 → Rgba32.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Rgba32> destination) =>
        Gray8.ToRgba32(source, destination);

    /// <summary>Пакетная конвертация Gray8 → Rgba32 с явным указанием ускорителя.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Rgba32> destination, HardwareAcceleration acceleration) =>
        Gray8.ToRgba32(source, destination, acceleration);

    #endregion
}
