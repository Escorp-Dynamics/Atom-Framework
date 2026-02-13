#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Цветовое пространство YCbCr (YUV) - 8 бит на компонент.
/// Используется в JPEG, MPEG и других стандартах сжатия.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
public readonly partial struct YCbCr(byte y, byte cb, byte cr) : IColorSpace<YCbCr>, IEquatable<YCbCr>
{
    /// <summary>Компонента яркости (Luma, 0-255).</summary>
    public readonly byte Y = y;

    /// <summary>Компонента синей цветности (Cb, 0-255, центр = 128).</summary>
    public readonly byte Cb = cb;

    /// <summary>Компонента красной цветности (Cr, 0-255, центр = 128).</summary>
    public readonly byte Cr = cr;

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(YCbCr))
            return Unsafe.As<T, YCbCr>(ref source);

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(Rgba32))
        {
            var rgba = Unsafe.As<T, Rgba32>(ref source);
            return FromRgb24(new Rgb24(rgba.R, rgba.G, rgba.B));
        }

        if (typeof(T) == typeof(Bgr24))
        {
            var bgr = Unsafe.As<T, Bgr24>(ref source);
            return FromRgb24(new Rgb24(bgr.R, bgr.G, bgr.B));
        }

        if (typeof(T) == typeof(Bgra32))
        {
            var bgra = Unsafe.As<T, Bgra32>(ref source);
            return FromRgb24(new Rgb24(bgra.R, bgra.G, bgra.B));
        }

        if (typeof(T) == typeof(Hsv))
            return Unsafe.As<T, Hsv>(ref source).ToYCbCr();

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToYCbCr();

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в YCbCr не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<YCbCr> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(YCbCr))
        {
            MemoryMarshal.Cast<T, YCbCr>(source).CopyTo(destination);
            return;
        }

        if (typeof(T) == typeof(Rgb24))
        {
            FromRgb24(MemoryMarshal.Cast<T, Rgb24>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            Rgba32.ToYCbCr(MemoryMarshal.Cast<T, Rgba32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            // Bgr24 → Rgb24 → YCbCr
            for (var i = 0; i < source.Length; i++)
            {
                var bgr = Unsafe.As<T, Bgr24>(ref Unsafe.AsRef(in source[i]));
                destination[i] = FromRgb24(new Rgb24(bgr.R, bgr.G, bgr.B));
            }
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.ToYCbCr(MemoryMarshal.Cast<T, Bgra32>(source), destination, acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.ToYCbCr(MemoryMarshal.Cast<T, Hsv>(source), destination);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToYCbCr(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<YCbCr> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<YCbCr> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(YCbCr))
        {
            source.CopyTo(MemoryMarshal.Cast<T, YCbCr>(destination));
            return;
        }

        if (typeof(T) == typeof(Rgb24))
        {
            Rgb24.FromYCbCr(source, MemoryMarshal.Cast<T, Rgb24>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Rgba32))
        {
            Rgba32.FromYCbCr(source, MemoryMarshal.Cast<T, Rgba32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Bgr24))
        {
            // YCbCr → Rgb24 → Bgr24
            for (var i = 0; i < source.Length; i++)
            {
                var rgb = Rgb24.FromYCbCr(source[i]);
                Unsafe.As<T, Bgr24>(ref destination[i]) = new Bgr24(rgb.B, rgb.G, rgb.R);
            }
            return;
        }

        if (typeof(T) == typeof(Bgra32))
        {
            Bgra32.FromYCbCr(source, MemoryMarshal.Cast<T, Bgra32>(destination), acceleration);
            return;
        }

        if (typeof(T) == typeof(Hsv))
        {
            Hsv.FromYCbCr(source, MemoryMarshal.Cast<T, Hsv>(destination));
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromYCbCr(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
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
    public bool Equals(YCbCr other) => Y == other.Y && Cb == other.Cb && Cr == other.Cr;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is YCbCr other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Y, Cb, Cr);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(YCbCr left, YCbCr right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(YCbCr left, YCbCr right) => !left.Equals(right);

    #endregion

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"YCbCr({Y}, {Cb}, {Cr})";
}
