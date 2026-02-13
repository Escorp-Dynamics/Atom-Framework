#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// 32-битный CMYK (8 бит на канал).
/// Формат хранения: C, M, Y, K (4 байта).
/// Используется в полиграфии и печати.
/// C: 0-255 соответствует 0%-100% голубого.
/// M: 0-255 соответствует 0%-100% пурпурного.
/// Y: 0-255 соответствует 0%-100% жёлтого.
/// K: 0-255 соответствует 0%-100% чёрного (ключевой цвет).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly partial struct Cmyk(byte c, byte m, byte y, byte k) : IColorSpace<Cmyk>, IEquatable<Cmyk>
{
    /// <summary>Голубой канал (Cyan, 0-255 → 0%-100%).</summary>
    public readonly byte C = c;

    /// <summary>Пурпурный канал (Magenta, 0-255 → 0%-100%).</summary>
    public readonly byte M = m;

    /// <summary>Жёлтый канал (Yellow, 0-255 → 0%-100%).</summary>
    public readonly byte Y = y;

    /// <summary>Чёрный канал (Key/Black, 0-255 → 0%-100%).</summary>
    public readonly byte K = k;

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Cmyk))
            return Unsafe.As<T, Cmyk>(ref source);

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(Rgba32))
            return FromRgba32(Unsafe.As<T, Rgba32>(ref source));

        if (typeof(T) == typeof(Bgr24))
            return FromBgr24(Unsafe.As<T, Bgr24>(ref source));

        if (typeof(T) == typeof(Bgra32))
            return FromBgra32(Unsafe.As<T, Bgra32>(ref source));

        if (typeof(T) == typeof(Hsv))
            return FromHsv(Unsafe.As<T, Hsv>(ref source));

        if (typeof(T) == typeof(YCbCr))
            return FromYCbCr(Unsafe.As<T, YCbCr>(ref source));

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToCmyk();

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Cmyk не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Cmyk> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Cmyk))
        {
            MemoryMarshal.Cast<T, Cmyk>(source).CopyTo(destination);
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

        if (typeof(T) == typeof(Hsv))
        {
            FromHsv(MemoryMarshal.Cast<T, Hsv>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            FromYCbCr(MemoryMarshal.Cast<T, YCbCr>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToCmyk(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Cmyk> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Cmyk> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Cmyk))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Cmyk>(destination));
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

        if (typeof(T) == typeof(Hsv))
        {
            ToHsv(source, MemoryMarshal.Cast<T, Hsv>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            ToYCbCr(source, MemoryMarshal.Cast<T, YCbCr>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromCmyk(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = T.From(source[i]);
    }

    #endregion

    #region Equality

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Equals(Cmyk other) =>
        C == other.C && M == other.M && Y == other.Y && K == other.K;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override bool Equals(object? obj) => obj is Cmyk other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override int GetHashCode() => HashCode.Combine(C, M, Y, K);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool operator ==(Cmyk left, Cmyk right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool operator !=(Cmyk left, Cmyk right) => !left.Equals(right);

    #endregion

    #region ToString

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override string ToString() => $"Cmyk({C}, {M}, {Y}, {K})";

    #endregion

    #region Helper Methods

    /// <summary>Выбрасывает исключение если целевой буфер меньше исходного.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDestinationTooShort() =>
        throw new InvalidOperationException("Destination is too short");

    #endregion
}
