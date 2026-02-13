#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Gray8.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Bgr24 (B = G = R = Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromGray8(Gray8 gray) => gray.ToBgr24();

    /// <summary>Конвертирует Bgr24 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => Gray8.FromBgr24(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgr24 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Bgr24> source, Span<Gray8> destination) =>
        Gray8.FromBgr24(source, destination);

    /// <summary>Пакетная конвертация Bgr24 → Gray8 с явным указанием ускорителя.</summary>
    public static void ToGray8(ReadOnlySpan<Bgr24> source, Span<Gray8> destination, HardwareAcceleration acceleration) =>
        Gray8.FromBgr24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray8 → Bgr24.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Bgr24> destination) =>
        Gray8.ToBgr24(source, destination);

    /// <summary>Пакетная конвертация Gray8 → Bgr24 с явным указанием ускорителя.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Bgr24> destination, HardwareAcceleration acceleration) =>
        Gray8.ToBgr24(source, destination, acceleration);

    #endregion
}
