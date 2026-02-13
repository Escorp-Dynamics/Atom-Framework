#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// 24-битный RGB (8 бит на канал, packed).
/// Формат хранения: R, G, B (3 байта).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
public readonly partial struct Rgb24(byte r, byte g, byte b) : IColorSpace<Rgb24>, IEquatable<Rgb24>
{
    /// <summary>Красный канал (0-255).</summary>
    public readonly byte R = r;

    /// <summary>Зелёный канал (0-255).</summary>
    public readonly byte G = g;

    /// <summary>Синий канал (0-255).</summary>
    public readonly byte B = b;

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgb24 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Rgb24))
            return Unsafe.As<T, Rgb24>(ref source);

        if (typeof(T) == typeof(YCbCr))
            return FromYCbCr(Unsafe.As<T, YCbCr>(ref source));

        if (typeof(T) == typeof(Rgba32))
            return Unsafe.As<T, Rgba32>(ref source).ToRgb24();

        if (typeof(T) == typeof(Bgr24))
            return Unsafe.As<T, Bgr24>(ref source).ToRgb24();

        if (typeof(T) == typeof(Bgra32))
            return Unsafe.As<T, Bgra32>(ref source).ToRgb24();

        if (typeof(T) == typeof(Hsv))
            return Unsafe.As<T, Hsv>(ref source).ToRgb24();

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToRgb24();

        if (typeof(T) == typeof(Gray8))
            return Unsafe.As<T, Gray8>(ref source).ToRgb24();

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Rgb24 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Rgb24> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Rgb24> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Rgb24))
        {
            MemoryMarshal.Cast<T, Rgb24>(source).CopyTo(destination);
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            FromYCbCr(MemoryMarshal.Cast<T, YCbCr>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            Rgba32.ToRgb24(MemoryMarshal.Cast<T, Rgba32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            Bgr24.ToRgb24(MemoryMarshal.Cast<T, Bgr24>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.ToRgb24(MemoryMarshal.Cast<T, Bgra32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.ToRgb24(MemoryMarshal.Cast<T, Hsv>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToRgb24(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.ToRgb24(MemoryMarshal.Cast<T, Gray8>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Rgb24> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Rgb24))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Rgb24>(destination));
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            YCbCr.FromRgb24(source, MemoryMarshal.Cast<T, YCbCr>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            Rgba32.FromRgb24(source, MemoryMarshal.Cast<T, Rgba32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            Bgr24.FromRgb24(source, MemoryMarshal.Cast<T, Bgr24>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.FromRgb24(source, MemoryMarshal.Cast<T, Bgra32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.FromRgb24(source, MemoryMarshal.Cast<T, Hsv>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromRgb24(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.FromRgb24(source, MemoryMarshal.Cast<T, Gray8>(destination), acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].To<T>();
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDestinationTooShort() =>
        throw new InvalidOperationException("Destination is too short");

    #endregion

    #region Equality

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Rgb24 other) => R == other.R && G == other.G && B == other.B;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Rgb24 other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(R, G, B);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Rgb24 left, Rgb24 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Rgb24 left, Rgb24 right) => !left.Equals(right);

    #endregion

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"RGB({R}, {G}, {B})";
}
