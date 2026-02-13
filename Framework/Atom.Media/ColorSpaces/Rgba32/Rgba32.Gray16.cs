#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgba32 ↔ Gray16.
/// </summary>
public readonly partial struct Rgba32
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Rgba32 (R = G = B = Value >> 8, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromGray16(Gray16 gray) => gray.ToRgba32();

    /// <summary>Конвертирует Rgba32 в Gray16 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => Gray16.FromRgba32(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgba32 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Rgba32> source, Span<Gray16> destination) =>
        Gray16.FromRgba32(source, destination);

    /// <summary>Пакетная конвертация Rgba32 → Gray16 с явным указанием ускорителя.</summary>
    public static void ToGray16(ReadOnlySpan<Rgba32> source, Span<Gray16> destination, HardwareAcceleration acceleration) =>
        Gray16.FromRgba32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray16 → Rgba32.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Rgba32> destination) =>
        Gray16.ToRgba32(source, destination);

    /// <summary>Пакетная конвертация Gray16 → Rgba32 с явным указанием ускорителя.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Rgba32> destination, HardwareAcceleration acceleration) =>
        Gray16.ToRgba32(source, destination, acceleration);

    #endregion
}
