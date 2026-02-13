#pragma warning disable CA1000, CA2208, IDE0004, IDE0022, IDE0045, IDE0048, IDE0059, MA0007, MA0026, MA0051, MA0084, S1117, S1135, S4136, S4144

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Hsv.
/// Делегирует через RGB24 для корректной обработки 4-байтового Hsv формата (H16, S8, V8).
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants (Hsv)

    /// <summary>Реализованные ускорители для Cmyk ↔ Hsv.</summary>
    private const HardwareAcceleration HsvImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (Hsv)

    /// <summary>
    /// Конвертирует Hsv в Cmyk через промежуточное RGB.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromHsv(Hsv hsv)
    {
        var rgb = hsv.ToRgb24();
        return FromRgb24(rgb);
    }

    /// <summary>
    /// Конвертирует Cmyk в Hsv через промежуточное RGB.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv()
    {
        var rgb = ToRgb24();
        return Hsv.FromRgb24(rgb);
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ Hsv)

    /// <summary>Пакетная конвертация Hsv → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Cmyk> destination) =>
        FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Cmyk с явным ускорителем.</summary>
    public static unsafe void FromHsv(ReadOnlySpan<Hsv> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Hsv* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromHsvParallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromHsvCore(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToHsv(ReadOnlySpan<Cmyk> source, Span<Hsv> destination) =>
        ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Hsv с явным ускорителем.</summary>
    public static unsafe void ToHsv(ReadOnlySpan<Cmyk> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Hsv* dstPtr = destination)
            {
                ToHsvParallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToHsvCore(source, destination, selected);
    }

    #endregion

    #region Core Implementations (Hsv)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromHsvCore(ReadOnlySpan<Hsv> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        // Все режимы используют scalar до реализации SIMD для 4-байтового Hsv
        _ = selected;
        FromHsvScalar(source, destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToHsvCore(ReadOnlySpan<Cmyk> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        // Все режимы используют scalar до реализации SIMD для 4-байтового Hsv
        _ = selected;
        ToHsvScalar(source, destination);
    }

    #endregion

    #region Parallel Processing (Hsv)

    private static unsafe void FromHsvParallel(Hsv* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromHsvCore(
                new ReadOnlySpan<Hsv>(source + start, size),
                new Span<Cmyk>(destination + start, size),
                selected);
        });
    }

    private static unsafe void ToHsvParallel(Cmyk* source, Hsv* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToHsvCore(
                new ReadOnlySpan<Cmyk>(source + start, size),
                new Span<Hsv>(destination + start, size),
                selected);
        });
    }

    #endregion

    #region Scalar Implementations (Hsv)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromHsvScalar(ReadOnlySpan<Hsv> source, Span<Cmyk> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromHsv(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToHsvScalar(ReadOnlySpan<Cmyk> source, Span<Hsv> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToHsv();
        }
    }

    #endregion

    #region Conversion Operators (Hsv)

    /// <summary>Явное преобразование Hsv → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(Hsv hsv) => FromHsv(hsv);

    /// <summary>Явное преобразование Cmyk → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Cmyk cmyk) => cmyk.ToHsv();

    #endregion
}
