#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Gray16.
/// </summary>
public readonly partial struct Bgra32
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Bgra32 (B = G = R = Value >> 8, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromGray16(Gray16 gray) => gray.ToBgra32();

    /// <summary>Конвертирует Bgra32 в Gray16 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => Gray16.FromBgra32(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgra32 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Bgra32> source, Span<Gray16> destination) =>
        Gray16.FromBgra32(source, destination);

    /// <summary>Пакетная конвертация Bgra32 → Gray16 с явным указанием ускорителя.</summary>
    public static void ToGray16(ReadOnlySpan<Bgra32> source, Span<Gray16> destination, HardwareAcceleration acceleration) =>
        Gray16.FromBgra32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray16 → Bgra32.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Bgra32> destination) =>
        Gray16.ToBgra32(source, destination);

    /// <summary>Пакетная конвертация Gray16 → Bgra32 с явным указанием ускорителя.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Bgra32> destination, HardwareAcceleration acceleration) =>
        Gray16.ToBgra32(source, destination, acceleration);

    #endregion
}
