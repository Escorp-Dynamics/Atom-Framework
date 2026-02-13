#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Bgra32.
/// Делегирует реализацию в Bgra32 (добавление/удаление альфа, без swap).
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в Bgr24 (отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromBgra32(Bgra32 bgra) => bgra.ToBgr24();

    /// <summary>Конвертирует Bgr24 в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32() => Bgra32.FromBgr24(this);

    #endregion

    #region Batch Conversion (Bgr24 ↔ Bgra32)

    /// <summary>
    /// Пакетная конвертация Bgra32 → Bgr24 с SIMD.
    /// Делегирует в Bgra32.ToBgr24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Bgr24> destination)
        => Bgra32.ToBgr24(source, destination);

    /// <summary>
    /// Пакетная конвертация Bgr24 → Bgra32 с SIMD.
    /// Делегирует в Bgra32.FromBgr24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Bgr24> source, Span<Bgra32> destination)
        => Bgra32.FromBgr24(source, destination);

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgra32 → Bgr24 (отбрасывается альфа).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Неявное преобразование Bgr24 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Bgra32(Bgr24 bgr) => bgr.ToBgra32();

    #endregion
}
