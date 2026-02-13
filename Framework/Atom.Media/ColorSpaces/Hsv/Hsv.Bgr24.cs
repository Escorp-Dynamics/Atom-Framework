#pragma warning disable CA1000, CA2208, MA0051, S4136, IDE0004, IDE0059

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Bgr24.
/// Прямой SIMD int32 математикой (без промежуточных буферов).
/// </summary>
public readonly partial struct Hsv
{
    #region SIMD Constants (Bgr24)

    /// <summary>
    /// Реализованные ускорители для конвертации HSV ↔ BGR24.
    /// ВРЕМЕННО отключено: SIMD использует h6=1536, scalar — h6=1530.
    /// </summary>
    private const HardwareAcceleration HsvBgr24Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromBgr24(Bgr24 bgr) => FromRgb24(new Rgb24(bgr.R, bgr.G, bgr.B));

    /// <summary>Конвертирует Hsv в Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24()
    {
        var rgb = ToRgb24();
        return new Bgr24(rgb.B, rgb.G, rgb.R);
    }

    #endregion

    #region Batch Conversion (Hsv ↔ Bgr24)

    /// <summary>Пакетная конвертация Bgr24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Hsv> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → Hsv с явным указанием ускорителя.</summary>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvBgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            FromBgr24Parallel(source, destination, selected);
            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Hsv → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToBgr24(ReadOnlySpan<Hsv> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Bgr24 с явным указанием ускорителя.</summary>
    public static void ToBgr24(ReadOnlySpan<Hsv> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvBgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            ToBgr24Parallel(source, destination, selected);
            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementation (Bgr24 → Hsv)

    /// <summary>
    /// Bgr24 → Hsv: скалярная реализация.
    /// SIMD отключён: структура Hsv = 4 байта (ushort H + byte S + byte V),
    /// что несовместимо с SIMD интерлейвингом 24-bit форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD не реализован для HSV (формат несовместим)

        for (var i = 0; i < source.Length; i++)
            destination[i] = FromBgr24(source[i]);
    }

    #endregion

    #region Core Implementation (Hsv → Bgr24)

    /// <summary>
    /// Hsv → Bgr24: скалярная реализация.
    /// SIMD отключён: структура Hsv = 4 байта (ushort H + byte S + byte V),
    /// что несовместимо с SIMD интерлейвингом 24-bit форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ToBgr24Core(ReadOnlySpan<Hsv> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        _ = selected; // SIMD не реализован для HSV (формат несовместим)

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i].ToBgr24();
    }

    #endregion

    #region Parallel Processing (Bgr24)

    private static unsafe void FromBgr24Parallel(ReadOnlySpan<Bgr24> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Bgr24* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                FromBgr24Core(new ReadOnlySpan<Bgr24>(srcLocal + start, size), new Span<Hsv>(dstLocal + start, size), selected);
            });
        }
    }

    private static unsafe void ToBgr24Parallel(ReadOnlySpan<Hsv> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        var length = source.Length;
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        fixed (Hsv* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var srcLocal = srcPtr;
            var dstLocal = dstPtr;

            Parallel.For(0, threadCount, i =>
            {
                var start = (i * chunkSize) + Math.Min(i, remainder);
                var size = chunkSize + (i < remainder ? 1 : 0);
                ToBgr24Core(new ReadOnlySpan<Hsv>(srcLocal + start, size), new Span<Bgr24>(dstLocal + start, size), selected);
            });
        }
    }

    #endregion

    #region Conversion Operators (Bgr24)

    /// <summary>Явное преобразование Bgr24 → Hsv.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Hsv(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явное преобразование Hsv → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Hsv hsv) => hsv.ToBgr24();

    #endregion
}
