#pragma warning disable CA1000, CA2208, CA2225, IDE0280, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Цветовое пространство HSV (Hue, Saturation, Value) — 4-байтовый lossless формат.
/// <para>
/// Оптимизированное хранение для 100% lossless round-trip RGB↔HSV:
/// <list type="bullet">
///   <item>H: ushort (0-65535) → 0°-360° (циклический, 10923 = 60°). Требует 16 бит для lossless.</item>
///   <item>S: byte (0-255) → 0%-100% насыщенности. 8 бит достаточно.</item>
///   <item>V: byte (0-255) → max(R,G,B). 8 бит достаточно.</item>
/// </list>
/// </para>
/// <para>
/// Размер структуры: 4 байта (Pack = 1).
/// Round-trip RGB→HSV→RGB имеет max error = 0 (lossless).
/// Ключевое открытие: S16 восстанавливается через delta = S8 * V8 / 255.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly partial struct Hsv(ushort h, byte s, byte v) : IColorSpace<Hsv>, IEquatable<Hsv>
{
    /// <summary>Оттенок (Hue, 0-65535 → 0°-360°, где 10923 ≈ 60°).</summary>
    public readonly ushort H = h;

    /// <summary>Насыщенность (Saturation, 0-255 → 0%-100%).</summary>
    public readonly byte S = s;

    /// <summary>Яркость (Value, 0-255 → max(R,G,B)).</summary>
    public readonly byte V = v;

    #region Generic Conversions

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T To<T>() where T : unmanaged, IColorSpace<T> => T.From(this);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv From<T>(T source) where T : unmanaged, IColorSpace<T>
    {
        if (typeof(T) == typeof(Hsv))
            return Unsafe.As<T, Hsv>(ref source);

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

        if (typeof(T) == typeof(YCbCr))
        {
            var rgb = Rgb24.FromYCbCr(Unsafe.As<T, YCbCr>(ref source));
            return FromRgb24(rgb);
        }

        if (typeof(T) == typeof(YCoCgR32))
            return Unsafe.As<T, YCoCgR32>(ref source).ToHsv();

        throw new NotSupportedException($"Конвертация из {typeof(T).Name} в Hsv не поддерживается");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Hsv> destination)
        where T : unmanaged, IColorSpace<T> =>
        From(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void From<T>(ReadOnlySpan<T> source, Span<Hsv> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Hsv))
        {
            MemoryMarshal.Cast<T, Hsv>(source).CopyTo(destination);
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

        if (typeof(T) == typeof(YCbCr))
        {
            FromYCbCr(MemoryMarshal.Cast<T, YCbCr>(source), destination);
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.ToHsv(MemoryMarshal.Cast<T, YCoCgR32>(source), destination, acceleration);
            return;
        }

        for (var i = 0; i < source.Length; i++)
            destination[i] = From(source[i]);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Hsv> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T> =>
        To(source, destination, HardwareAcceleration.Auto);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void To<T>(ReadOnlySpan<Hsv> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T>
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();

        if (typeof(T) == typeof(Hsv))
        {
            source.CopyTo(MemoryMarshal.Cast<T, Hsv>(destination));
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

        if (typeof(T) == typeof(YCbCr))
        {
            ToYCbCr(source, MemoryMarshal.Cast<T, YCbCr>(destination));
            return;
        }

        if (typeof(T) == typeof(YCoCgR32))
        {
            YCoCgR32.FromHsv(source, MemoryMarshal.Cast<T, YCoCgR32>(destination), acceleration);
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
    public bool Equals(Hsv other) => H == other.H && S == other.S && V == other.V;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Hsv other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(H, S, V);

    /// <summary>Оператор равенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Hsv left, Hsv right) => left.Equals(right);

    /// <summary>Оператор неравенства.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Hsv left, Hsv right) => !left.Equals(right);

    #endregion

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"HSV({H}, {S}, {V})";
}
