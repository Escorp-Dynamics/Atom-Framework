#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// 24-битный BGR (8 бит на канал, packed).
/// Формат хранения: B, G, R (3 байта).
/// Используется в OpenCV, некоторых видеоформатах и Windows Bitmap.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
public readonly partial struct Bgr24(byte b, byte g, byte r) : IColorSpace<Bgr24>, IEquatable<Bgr24>
{
    /// <summary>Синий канал (0-255).</summary>
    public readonly byte B = b;

    /// <summary>Зелёный канал (0-255).</summary>
    public readonly byte G = g;

    /// <summary>Красный канал (0-255).</summary>
    public readonly byte R = r;

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Bgr24))
            return Unsafe.As<T, Bgr24>(ref source);

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(Rgba32))
            return FromRgba32(Unsafe.As<T, Rgba32>(ref source));

        if (typeof(T) == typeof(YCbCr))
        {
            // YCbCr → Rgb24 → Bgr24
            var rgb = Rgb24.FromYCbCr(Unsafe.As<T, YCbCr>(ref source));
            return FromRgb24(rgb);
        }

        if (typeof(T) == typeof(Bgra32))
            return Unsafe.As<T, Bgra32>(ref source).ToBgr24();

        if (typeof(T) == typeof(Hsv))
            return Unsafe.As<T, Hsv>(ref source).ToBgr24();

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToBgr24();

        if (typeof(T) == typeof(Gray8))
            return FromGray8(Unsafe.As<T, Gray8>(ref source));

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Bgr24 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Bgr24> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Bgr24))
        {
            MemoryMarshal.Cast<T, Bgr24>(source).CopyTo(destination);
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

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.ToBgr24(MemoryMarshal.Cast<T, Bgra32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.ToBgr24(MemoryMarshal.Cast<T, Hsv>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToBgr24(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.ToBgr24(MemoryMarshal.Cast<T, Gray8>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Bgr24> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Bgr24> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Bgr24))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Bgr24>(destination));
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

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.FromBgr24(source, MemoryMarshal.Cast<T, Bgra32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.FromBgr24(source, MemoryMarshal.Cast<T, Hsv>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromBgr24(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.FromBgr24(source, MemoryMarshal.Cast<T, Gray8>(destination), acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].To<T>();
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDestinationTooShort() =>
        throw new InvalidOperationException("Destination is too short");

    #endregion

    #region Equality

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Bgr24 other) => B == other.B && G == other.G && R == other.R;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Bgr24 other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(B, G, R);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Bgr24 left, Bgr24 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Bgr24 left, Bgr24 right) => !left.Equals(right);

    #endregion

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"BGR({B}, {G}, {R})";
}
