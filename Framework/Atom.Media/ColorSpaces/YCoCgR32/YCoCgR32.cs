#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Цветовое пространство YCoCg-R — 4-байтовый lossless формат.
/// <para>
/// YCoCg-R — лифтинг-преобразование, обратимое без потерь.
/// В отличие от YCbCr, YCoCg-R использует только целочисленную арифметику
/// и гарантирует 100% lossless round-trip RGB↔YCoCgR32.
/// </para>
/// <para>
/// Хранение компонент:
/// <list type="bullet">
///   <item>Y: byte (0-255) — яркость</item>
///   <item>CoHigh: byte — старшие 8 бит (Co + 255) &gt;&gt; 1</item>
///   <item>CgHigh: byte — старшие 8 бит (Cg + 255) &gt;&gt; 1</item>
///   <item>Frac: byte — bit0 = Co &amp; 1, bit1 = Cg &amp; 1</item>
/// </list>
/// Полные диапазоны: Y: [0, 255], Co: [-255, 255], Cg: [-255, 255].
/// </para>
/// <para>
/// Формулы преобразования:
/// <code>
/// Forward (RGB → YCoCgR32):
///   Co = R - B
///   t  = B + (Co &gt;&gt; 1)
///   Cg = G - t
///   Y  = t + (Cg &gt;&gt; 1)
///
/// Inverse (YCoCgR32 → RGB):
///   t = Y - (Cg &gt;&gt; 1)
///   G = Cg + t
///   B = t - (Co &gt;&gt; 1)
///   R = B + Co
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly partial struct YCoCgR32 : IColorSpace<YCoCgR32>, IEquatable<YCoCgR32>
{
    #region Fields

    /// <summary>Яркость (Y, 0-255).</summary>
    public readonly byte Y;

    /// <summary>Старшие 8 бит оранжево-голубой хроматической компоненты: (Co + 255) &gt;&gt; 1.</summary>
    public readonly byte CoHigh;

    /// <summary>Старшие 8 бит зелёно-пурпурной хроматической компоненты: (Cg + 255) &gt;&gt; 1.</summary>
    public readonly byte CgHigh;

    /// <summary>Дробная часть: bit0 = Co &amp; 1, bit1 = Cg &amp; 1.</summary>
    public readonly byte Frac;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт YCoCgR32 из упакованных компонент.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32(byte y, byte coHigh, byte cgHigh, byte frac)
    {
        Y = y;
        CoHigh = coHigh;
        CgHigh = cgHigh;
        Frac = frac;
    }

    /// <summary>
    /// Создаёт YCoCgR32 из полных значений Co и Cg.
    /// </summary>
    /// <param name="y">Яркость (0-255).</param>
    /// <param name="co">Оранжево-голубая компонента (-255..255).</param>
    /// <param name="cg">Зелёно-пурпурная компонента (-255..255).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32(int y, int co, int cg)
    {
        Y = (byte)y;

        // Сдвигаем в положительный диапазон: [-255, 255] → [0, 510]
        var coShifted = co + 255;
        var cgShifted = cg + 255;

        CoHigh = (byte)(coShifted >> 1);
        CgHigh = (byte)(cgShifted >> 1);
        Frac = (byte)((coShifted & 1) | ((cgShifted & 1) << 1));
    }

    #endregion

    #region Properties

    /// <summary>Полное значение Co (-255..255).</summary>
    public int Co
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((CoHigh << 1) | (Frac & 1)) - 255;
    }

    /// <summary>Полное значение Cg (-255..255).</summary>
    public int Cg
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((CgHigh << 1) | ((Frac >> 1) & 1)) - 255;
    }

    #endregion

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source);

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(Rgba32))
            return FromRgba32(Unsafe.As<T, Rgba32>(ref source));

        if (typeof(T) == typeof(Bgr24))
            return FromBgr24(Unsafe.As<T, Bgr24>(ref source));

        if (typeof(T) == typeof(Bgra32))
            return FromBgra32(Unsafe.As<T, Bgra32>(ref source));

        if (typeof(T) == typeof(YCbCr))
            return FromYCbCr(Unsafe.As<T, YCbCr>(ref source));

        if (typeof(T) == typeof(Hsv))
            return FromHsv(Unsafe.As<T, Hsv>(ref source));

        if (typeof(T) == typeof(Cmyk))
            return FromCmyk(Unsafe.As<T, Cmyk>(ref source));

        if (typeof(T) == typeof(Gray8))
            return FromGray8(Unsafe.As<T, Gray8>(ref source));

        if (typeof(T) == typeof(Gray16))
            return FromGray16(Unsafe.As<T, Gray16>(ref source));

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в YCoCgR32 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<YCoCgR32> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(YCoCgR32))
        {
            MemoryMarshal.Cast<T, YCoCgR32>(source).CopyTo(destination);
            return;
        }

        if (typeof(T) == typeof(Rgb24))
        {
            FromRgb24(MemoryMarshal.Cast<T, Rgb24>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            FromRgba32(MemoryMarshal.Cast<T, Rgba32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            FromBgr24(MemoryMarshal.Cast<T, Bgr24>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            FromBgra32(MemoryMarshal.Cast<T, Bgra32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            FromGray8(MemoryMarshal.Cast<T, Gray8>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray16))
        {
            FromGray16(MemoryMarshal.Cast<T, Gray16>(source), destination, acceleration);
            return;
        }

        // Fallback: используем обычную конвертацию
        From(source, destination);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<YCoCgR32> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<YCoCgR32> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(YCoCgR32))
        {
            source.CopyTo(MemoryMarshal.Cast<T, YCoCgR32>(destination));
            return;
        }

        if (typeof(T) == typeof(Rgb24))
        {
            ToRgb24(source, MemoryMarshal.Cast<T, Rgb24>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            ToRgba32(source, MemoryMarshal.Cast<T, Rgba32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            ToBgr24(source, MemoryMarshal.Cast<T, Bgr24>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            ToBgra32(source, MemoryMarshal.Cast<T, Bgra32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            ToGray8(source, MemoryMarshal.Cast<T, Gray8>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray16))
        {
            ToGray16(source, MemoryMarshal.Cast<T, Gray16>(destination), acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].To<T>();
    }

    #endregion

    #region IColorSpace Implementation

    /// <summary>Количество компонент (4: Y, CoHigh, CgHigh, Frac).</summary>
    public static int ComponentCount => 4;

    /// <summary>Значение по умолчанию (чёрный).</summary>
    public static YCoCgR32 Default => new(0, 127, 127, 0b11); // Y=0, Co=0, Cg=0

    #endregion

    #region Equality

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(YCoCgR32 other) =>
        Y == other.Y &&
        CoHigh == other.CoHigh &&
        CgHigh == other.CgHigh &&
        Frac == other.Frac;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is YCoCgR32 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Y, CoHigh, CgHigh, Frac);

    /// <summary>Оператор равенства.</summary>
    public static bool operator ==(YCoCgR32 left, YCoCgR32 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    public static bool operator !=(YCoCgR32 left, YCoCgR32 right) => !left.Equals(right);

    #endregion

    #region ToString

    /// <inheritdoc/>
    public override string ToString() => $"YCoCgR32(Y={Y}, Co={Co}, Cg={Cg})";

    #endregion

    #region Helpers

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDestinationTooShort() =>
        throw new InvalidOperationException("Destination is too short");

    #endregion
}
