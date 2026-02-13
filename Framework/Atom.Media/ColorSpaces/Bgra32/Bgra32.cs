#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// 32-битный BGRA (8 бит на канал, packed).
/// Формат хранения: B, G, R, A (4 байта).
/// Используется в Windows GDI, DirectX, Cairo, WPF.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly partial struct Bgra32(byte b, byte g, byte r, byte a) : IColorSpace<Bgra32>, IEquatable<Bgra32>
{
    /// <summary>Синий канал (0-255).</summary>
    public readonly byte B = b;

    /// <summary>Зелёный канал (0-255).</summary>
    public readonly byte G = g;

    /// <summary>Красный канал (0-255).</summary>
    public readonly byte R = r;

    /// <summary>Альфа-канал (0-255, 0 = прозрачный, 255 = непрозрачный).</summary>
    public readonly byte A = a;

    /// <summary>
    /// Создаёт непрозрачный BGRA32 пиксель (A = 255).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32(byte b, byte g, byte r) : this(b, g, r, 255) { }

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Bgra32))
            return Unsafe.As<T, Bgra32>(ref source);

        if (typeof(T) == typeof(Rgba32))
            return FromRgba32(Unsafe.As<T, Rgba32>(ref source));

        if (typeof(T) == typeof(Bgr24))
            return FromBgr24(Unsafe.As<T, Bgr24>(ref source));

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(YCbCr))
            return FromYCbCr(Unsafe.As<T, YCbCr>(ref source));

        if (typeof(T) == typeof(Hsv))
            return Unsafe.As<T, Hsv>(ref source).ToBgra32();

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToBgra32();

        if (typeof(T) == typeof(Gray8))
            return FromGray8(Unsafe.As<T, Gray8>(ref source));

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Bgra32 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Bgra32> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Bgra32))
        {
            MemoryMarshal.Cast<T, Bgra32>(source).CopyTo(destination);
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

        if (typeof(T) == typeof(Rgb24))
        {
            FromRgb24(MemoryMarshal.Cast<T, Rgb24>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            FromYCbCr(MemoryMarshal.Cast<T, YCbCr>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.ToBgra32(MemoryMarshal.Cast<T, Hsv>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToBgra32(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.ToBgra32(MemoryMarshal.Cast<T, Gray8>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Bgra32> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Bgra32> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Bgra32))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Bgra32>(destination));
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

        if (typeof(T) == typeof(Rgb24))
        {
            ToRgb24(source, MemoryMarshal.Cast<T, Rgb24>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCbCr))
        {
            ToYCbCr(source, MemoryMarshal.Cast<T, YCbCr>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.FromBgra32(source, MemoryMarshal.Cast<T, Hsv>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromBgra32(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            Gray8.FromBgra32(source, MemoryMarshal.Cast<T, Gray8>(destination), acceleration);
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
    public bool Equals(Bgra32 other) => B == other.B && G == other.G && R == other.R && A == other.A;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Bgra32 other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(B, G, R, A);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Bgra32 left, Bgra32 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Bgra32 left, Bgra32 right) => !left.Equals(right);

    #endregion

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"BGRA({B}, {G}, {R}, {A})";
}
