#pragma warning disable CA1000, CA2208, MA0051, S4136, IDE0004, IDE0059

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Rgba32.
/// Прямой SIMD int32 математикой (без промежуточных буферов).
/// </summary>
public readonly partial struct Hsv
{
    #region SIMD Constants (Rgba32)

    /// <summary>
    /// Реализованные ускорители для конвертации HSV ↔ RGBA32.
    /// ВРЕМЕННО отключено: SIMD использует h6=1536, scalar — h6=1530.
    /// </summary>
    private const HardwareAcceleration HsvRgba32Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromRgba32(Rgba32 rgba) => FromRgb24(new Rgb24(rgba.R, rgba.G, rgba.B));

    /// <summary>Конвертирует Hsv в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32()
    {
        var rgb = ToRgb24();
        return new Rgba32(rgb.R, rgb.G, rgb.B, 255);
    }

    #endregion

    #region Batch Conversion (Hsv ↔ Rgba32)

    /// <summary>Пакетная конвертация Rgba32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Hsv> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → Hsv с явным указанием ускорителя.</summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvRgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            FromRgba32Parallel(source, destination, selected);
            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Hsv → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgba32(ReadOnlySpan<Hsv> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Rgba32 с явным указанием ускорителя.</summary>
    public static void ToRgba32(ReadOnlySpan<Hsv> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvRgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            ToRgba32Parallel(source, destination, selected);
            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementation (Rgba32 → Hsv)

    /// <summary>
    /// Rgba32 → Hsv: скалярная реализация.
    /// SIMD отложен: структура Hsv = 4 байта (ushort H + byte S + byte V),
    /// требует int32/int64 математику для lossless H16.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD будет добавлен позже

        for (var i = 0; i < source.Length; i++)
            destination[i] = FromRgba32(source[i]);
    }

    #endregion

    #region Core Implementation (Hsv → Rgba32)

    /// <summary>
    /// Hsv → Rgba32: скалярная реализация.
    /// SIMD отложен: структура Hsv = 4 байта (ushort H + byte S + byte V),
    /// требует int32/int64 математику для lossless H16.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ToRgba32Core(ReadOnlySpan<Hsv> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD будет добавлен позже

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].ToRgba32();
    }

    #endregion

    #region Parallel Processing (Rgba32)

    private static unsafe void FromRgba32Parallel(ReadOnlySpan<Rgba32> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Rgba32* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                FromRgba32Core(new ReadOnlySpan<Rgba32>(srcLocal + start, size), new Span<Hsv>(dstLocal + start, size), selected);
            });
        }
    }

    private static unsafe void ToRgba32Parallel(ReadOnlySpan<Hsv> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Hsv* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                ToRgba32Core(new ReadOnlySpan<Hsv>(srcLocal + start, size), new Span<Rgba32>(dstLocal + start, size), selected);
            });
        }
    }

    #endregion

    #region Conversion Operators (Rgba32)

    /// <summary>Явное преобразование Rgba32 → Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Rgba32 rgba) => FromRgba32(rgba);

    /// <summary>Явное преобразование Hsv → Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Hsv hsv) => hsv.ToRgba32();

    #endregion
}
