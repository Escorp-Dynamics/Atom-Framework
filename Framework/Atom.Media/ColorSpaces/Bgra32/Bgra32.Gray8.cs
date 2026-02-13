#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Gray8.
/// </summary>
public readonly partial struct Bgra32
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Bgra32 (B = G = R = Value, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromGray8(Gray8 gray) => gray.ToBgra32();

    /// <summary>Конвертирует Bgra32 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => Gray8.FromBgra32(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgra32 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Bgra32> source, Span<Gray8> destination) =>
        Gray8.FromBgra32(source, destination);

    /// <summary>Пакетная конвертация Bgra32 → Gray8 с явным указанием ускорителя.</summary>
    public static void ToGray8(ReadOnlySpan<Bgra32> source, Span<Gray8> destination, HardwareAcceleration acceleration) =>
        Gray8.FromBgra32(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray8 → Bgra32.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Bgra32> destination) =>
        Gray8.ToBgra32(source, destination);

    /// <summary>Пакетная конвертация Gray8 → Bgra32 с явным указанием ускорителя.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Bgra32> destination, HardwareAcceleration acceleration) =>
        Gray8.ToBgra32(source, destination, acceleration);

    #endregion
}
