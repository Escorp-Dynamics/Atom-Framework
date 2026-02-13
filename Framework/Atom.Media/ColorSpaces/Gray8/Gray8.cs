#pragma warning disable CA1000, CA2208, CA2225, IDE0280, IDE0290, MA0051, S4136

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Представляет пиксель в 8-битном grayscale формате.
/// Используется в PNG (grayscale), видео (luma), сканированных изображениях.
/// </summary>
/// <remarks>
/// ITU-R BT.601 коэффициенты для RGB → Gray:
/// Y = 0.299×R + 0.587×G + 0.114×B
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly partial struct Gray8 : IColorSpace<Gray8>, IEquatable<Gray8>
{
    #region Fields

    /// <summary>Значение яркости (0 = чёрный, 255 = белый).</summary>
    public readonly byte Value;

    #endregion

    #region Constructors

    /// <summary>Создаёт Gray8 с указанным значением яркости.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8(byte value) => Value = value;

    #endregion

    #region IColorSpace Implementation

    /// <summary>Количество компонентов (1 для grayscale).</summary>
    public static int ComponentCount => 1;

    /// <summary>Значение по умолчанию (чёрный).</summary>
    public static Gray8 Default => default;

    #endregion

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Gray8))
            return Unsafe.As<T, Gray8>(ref source);

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

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Gray8 не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Gray8> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Gray8> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Gray8))
        {
            MemoryMarshal.Cast<T, Gray8>(source).CopyTo(destination);
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
    public static void To<T>(ReadOnlySpan<Gray8> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Gray8> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Gray8))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Gray8>(destination));
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

    /// <inheritdoc/>
    public bool Equals(Gray8 other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Gray8 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Оператор равенства.</summary>
    public static bool operator ==(Gray8 left, Gray8 right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    public static bool operator !=(Gray8 left, Gray8 right) => !left.Equals(right);

    #endregion

    #region ToString

    /// <inheritdoc/>
    public override string ToString() => $"Gray8({Value})";

    #endregion
}
