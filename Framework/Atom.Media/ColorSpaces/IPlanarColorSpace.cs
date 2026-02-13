#pragma warning disable CA1000, CA1716, IDE0040, MA0018, MA0008, S2743

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Информация о plane в planar формате.
/// </summary>
/// <param name="WidthDivisor">Делитель ширины относительно базовой (1 = полная, 2 = половина).</param>
/// <param name="HeightDivisor">Делитель высоты относительно базовой (1 = полная, 2 = половина).</param>
/// <param name="BytesPerSample">Байт на сэмпл в этом plane (1 для 8-bit, 2 для 16-bit).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PlaneInfo(int WidthDivisor, int HeightDivisor, int BytesPerSample)
{
    /// <summary>
    /// Вычисляет размер plane в байтах для заданных размеров кадра.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPlaneSize(int width, int height) =>
        width / WidthDivisor * (height / HeightDivisor) * BytesPerSample;

    /// <summary>
    /// Вычисляет stride (ширину в байтах) для plane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPlaneStride(int width) => width / WidthDivisor * BytesPerSample;

    /// <summary>
    /// Вычисляет высоту plane в строках.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPlaneHeight(int height) => height / HeightDivisor;
}

/// <summary>
/// Контракт для planar цветовых пространств (YUV420P, NV12, YUV422P и т.д.).
/// Planar форматы хранят компоненты в отдельных planes разных размеров.
/// </summary>
/// <typeparam name="TSelf">Тип реализующей структуры (CRTP паттерн).</typeparam>
/// <remarks>
/// В отличие от <see cref="IColorSpace{TSelf}"/>, planar форматы не могут быть представлены
/// как единый пиксель фиксированного размера, т.к. chroma planes имеют субдискретизацию (4:2:0, 4:2:2).
/// Вместо этого конвертации работают с кадрами (frame-level).
/// </remarks>
public interface IPlanarColorSpace<TSelf> where TSelf : IPlanarColorSpace<TSelf>
{
    #region Constants

    /// <summary>Минимальный размер буфера для параллельной обработки.</summary>
    public const int ParallelThreshold = 1024;

    #endregion

    #region Plane Information

    /// <summary>
    /// Количество planes в формате.
    /// </summary>
    /// <remarks>
    /// YUV420P: 3 planes (Y, U, V).
    /// NV12: 2 planes (Y, UV interleaved).
    /// </remarks>
    static abstract int PlaneCount { get; }

    /// <summary>
    /// Возвращает информацию о plane по индексу.
    /// </summary>
    /// <param name="planeIndex">Индекс plane (0-based).</param>
    static abstract PlaneInfo GetPlaneInfo(int planeIndex);

    /// <summary>
    /// Вычисляет общий размер всех planes для кадра в байтах.
    /// </summary>
    /// <param name="width">Ширина кадра в пикселях.</param>
    /// <param name="height">Высота кадра в пикселях.</param>
    /// <returns>Суммарный размер всех planes.</returns>
    static virtual int GetTotalSize(int width, int height)
    {
        var total = 0;
        for (var i = 0; i < TSelf.PlaneCount; i++)
            total += TSelf.GetPlaneInfo(i).GetPlaneSize(width, height);
        return total;
    }

    #endregion

    #region Hardware Acceleration

    /// <summary>
    /// Аппаратные ускорители, поддерживаемые реализацией.
    /// </summary>
    static virtual HardwareAcceleration SupportedAccelerations => HardwareAcceleration.None;

    #endregion

    #region Parallelization Helpers

    /// <summary>
    /// Вычисляет оптимальное количество потоков для обработки.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOptimalThreadCount(int pixelCount)
    {
        var chunkSize = GetChunkSize(pixelCount);
        return Math.Max(1, Math.Min(Environment.ProcessorCount, pixelCount / chunkSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetChunkSize(int pixelCount)
    {
        if (pixelCount >= 1_500_000)
            return 131072;
        if (pixelCount >= 400_000)
            return 65536;
        return 32768;
    }

    /// <summary>
    /// Проверяет, нужна ли параллельная обработка.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldParallelize(int pixelCount) =>
        pixelCount >= ParallelThreshold && Environment.ProcessorCount > 1;

    #endregion
}
