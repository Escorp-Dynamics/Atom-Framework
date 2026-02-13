#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Hsv.
/// Делегирует реализацию в Hsv.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (Hsv)

    /// <summary>Конвертирует Hsv в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromHsv(Hsv hsv) => hsv.ToBgr24();

    /// <summary>Конвертирует Bgr24 в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => Hsv.FromBgr24(this);

    #endregion

    #region Batch Conversion (Bgr24 ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Bgr24> destination)
        => Hsv.ToBgr24(source, destination);

    /// <summary>Пакетная конвертация Bgr24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Bgr24> source, Span<Hsv> destination)
        => Hsv.FromBgr24(source, destination);

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование Bgr24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Bgr24 bgr) => bgr.ToHsv();

    #endregion
}
