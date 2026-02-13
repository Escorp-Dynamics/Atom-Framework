#pragma warning disable CA1000, CA2208, MA0051, S4136, IDE0004, IDE0059

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Bgra32.
/// Прямой SIMD int32 математикой (без промежуточных буферов).
/// </summary>
public readonly partial struct Hsv
{
    #region SIMD Constants (Bgra32)

    /// <summary>
    /// Реализованные ускорители для конвертации HSV ↔ BGRA32.
    /// ВРЕМЕННО отключено: SIMD использует h6=1536, scalar — h6=1530.
    /// </summary>
    private const HardwareAcceleration HsvBgra32Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Single Pixel Conversion (Bgra32)

    /// <summary>Конвертирует Bgra32 в Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromBgra32(Bgra32 bgra) => FromRgb24(new Rgb24(bgra.R, bgra.G, bgra.B));

    /// <summary>Конвертирует Hsv в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32()
    {
        var rgb = ToRgb24();
        return new Bgra32(rgb.B, rgb.G, rgb.R, 255);
    }

    #endregion

    #region Batch Conversion (Hsv ↔ Bgra32)

    /// <summary>Пакетная конвертация Bgra32 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Hsv> destination) =>
        FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → Hsv с явным указанием ускорителя.</summary>
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvBgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            FromBgra32Parallel(source, destination, selected);
            return;
        }

        FromBgra32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Hsv → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgra32(ReadOnlySpan<Hsv> source, Span<Bgra32> destination) =>
        ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Bgra32 с явным указанием ускорителя.</summary>
    public static void ToBgra32(ReadOnlySpan<Hsv> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvBgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            ToBgra32Parallel(source, destination, selected);
            return;
        }

        ToBgra32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementation (Bgra32 → Hsv)

    /// <summary>
    /// Bgra32 → Hsv: scalar implementation.
    /// HSV имеет 4-байтную структуру (ushort H + byte S + byte V), что требует
    /// специального SIMD интерлейва. SIMD будет добавлен позже.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FromBgra32Core(ReadOnlySpan<Bgra32> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD будет добавлен позже с учётом 4-байтного формата HSV
        for (var i = 0; i < source.Length; i++)
            destination[i] = FromBgra32(source[i]);
    }

    #endregion

    #region Core Implementation (Hsv → Bgra32)

    /// <summary>
    /// Hsv → Bgra32: scalar implementation.
    /// HSV имеет 4-байтную структуру (ushort H + byte S + byte V), что требует
    /// специального SIMD деинтерлейва. SIMD будет добавлен позже.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ToBgra32Core(ReadOnlySpan<Hsv> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD будет добавлен позже с учётом 4-байтного формата HSV
        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].ToBgra32();
    }

    #endregion

    #region Parallel Processing (Bgra32)

    private static unsafe void FromBgra32Parallel(ReadOnlySpan<Bgra32> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Bgra32* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                FromBgra32Core(new ReadOnlySpan<Bgra32>(srcLocal + start, size), new Span<Hsv>(dstLocal + start, size), selected);
            });
        }
    }

    private static unsafe void ToBgra32Parallel(ReadOnlySpan<Hsv> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Hsv* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                ToBgra32Core(new ReadOnlySpan<Hsv>(srcLocal + start, size), new Span<Bgra32>(dstLocal + start, size), selected);
            });
        }
    }

    #endregion

    #region Conversion Operators (Bgra32)

    /// <summary>Явное преобразование Bgra32 → Hsv (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явное преобразование Hsv → Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(Hsv hsv) => hsv.ToBgra32();

    #endregion
}
