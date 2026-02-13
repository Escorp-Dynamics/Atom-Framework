#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Gray16.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Bgr24 (B = G = R = Value >> 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromGray16(Gray16 gray) => gray.ToBgr24();

    /// <summary>Конвертирует Bgr24 в Gray16 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => Gray16.FromBgr24(this);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgr24 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Bgr24> source, Span<Gray16> destination) =>
        Gray16.FromBgr24(source, destination);

    /// <summary>Пакетная конвертация Bgr24 → Gray16 с явным указанием ускорителя.</summary>
    public static void ToGray16(ReadOnlySpan<Bgr24> source, Span<Gray16> destination, HardwareAcceleration acceleration) =>
        Gray16.FromBgr24(source, destination, acceleration);

    /// <summary>Пакетная конвертация Gray16 → Bgr24.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Bgr24> destination) =>
        Gray16.ToBgr24(source, destination);

    /// <summary>Пакетная конвертация Gray16 → Bgr24 с явным указанием ускорителя.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Bgr24> destination, HardwareAcceleration acceleration) =>
        Gray16.ToBgr24(source, destination, acceleration);

    #endregion
}
