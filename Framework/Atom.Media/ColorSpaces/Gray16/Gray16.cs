#pragma warning disable CA1000, CA2208, IDE0290, S4136

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Представляет пиксель в 16-bit grayscale цветовом пространстве.
/// Используется для PNG 16-bit grayscale изображений.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly partial struct Gray16 : IColorSpace<Gray16>, IEquatable<Gray16>
{
    #region Fields

    /// <summary>Значение яркости (0-65535).</summary>
    public readonly ushort Value;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт Gray16 пиксель с указанной яркостью.
    /// </summary>
    /// <param name="value">Яркость (0-65535).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16(ushort value) => Value = value;

    #endregion

    #region IColorSpace Implementation

    /// <inheritdoc />
    public static int ComponentCount => 1;

    /// <inheritdoc />
    public static Gray16 Default => default;

    /// <inheritdoc/>
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Gray16))
            return Unsafe.As<T, Gray16>(ref source);

        if (typeof(T) == typeof(Gray8))
            return FromGray8(Unsafe.As<T, Gray8>(ref source));

        if (typeof(T) == typeof(Rgb24))
            return FromRgb24(Unsafe.As<T, Rgb24>(ref source));

        if (typeof(T) == typeof(Rgba32))
            return FromRgba32(Unsafe.As<T, Rgba32>(ref source));

        if (typeof(T) == typeof(Bgr24))
            return FromBgr24(Unsafe.As<T, Bgr24>(ref source));

        if (typeof(T) == typeof(Bgra32))
            return FromBgra32(Unsafe.As<T, Bgra32>(ref source));

        if (typeof(T) == typeof(YCoCgR32))
            return FromYCoCgR32(Unsafe.As<T, YCoCgR32>(ref source));

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Gray16 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Gray16> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Gray16> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Gray16))
        {
            MemoryMarshal.Cast<T, Gray16>(source).CopyTo(destination);
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            FromGray8(MemoryMarshal.Cast<T, Gray8>(source), destination, acceleration);
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

        if (typeof(T) == typeof(YCoCgR32))
        {
            FromYCoCgR32(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Gray16> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Gray16> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Gray16))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Gray16>(destination));
            return;
        }

        if (typeof(T) == typeof(Gray8))
        {
            ToGray8(source, MemoryMarshal.Cast<T, Gray8>(destination), acceleration);
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

        if (typeof(T) == typeof(YCoCgR32))
        {
            ToYCoCgR32(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].To<T>();
    }

    [DoesNotReturn]
    private static void ThrowDestinationTooShort() =>
        throw new InvalidOperationException("Destination is too short");

    #endregion

    #region Equality

    /// <inheritdoc />
    public bool Equals(Gray16 other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Gray16 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Оператор равенства.</summary>
    public static bool operator ==(Gray16 left, Gray16 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    public static bool operator !=(Gray16 left, Gray16 right) => !left.Equals(right);

    #endregion

    #region ToString

    /// <inheritdoc />
    public override string ToString() => $"Gray16({Value})";

    #endregion
}
