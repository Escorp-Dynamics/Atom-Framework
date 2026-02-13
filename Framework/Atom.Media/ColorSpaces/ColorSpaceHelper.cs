#pragma warning disable CA2208, MA0015, MA0099, S3236, S3928

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Общие вспомогательные методы для конвертаций цветовых пространств.
/// Объединяет повторяющиеся паттерны: проверки, параллельная обработка, выбор ускорителя.
/// </summary>
internal static class ColorSpaceHelper
{
    #region Validation

    /// <summary>
    /// Выбрасывает исключение если целевой буфер меньше исходного.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowDestinationTooShort() =>
        throw new ArgumentException("Целевой буфер меньше исходного", "destination");

    /// <summary>
    /// Проверяет размеры буферов и выбрасывает исключение при несоответствии.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateBuffers<TSource, TDest>(ReadOnlySpan<TSource> source, Span<TDest> destination)
        where TSource : unmanaged
        where TDest : unmanaged
    {
        if (source.Length > destination.Length)
            ThrowDestinationTooShort();
    }

    #endregion

    #region Acceleration Selection

    /// <summary>
    /// Выбирает лучший ускоритель для заданного размера буфера.
    /// Учитывает минимальные размеры батчей для каждого ускорителя.
    /// </summary>
    /// <param name="requested">Запрошенные флаги ускорения.</param>
    /// <param name="implemented">Реализованные в методе ускорители.</param>
    /// <param name="bufferSize">Размер буфера в элементах.</param>
    /// <returns>Лучший подходящий ускоритель.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HardwareAcceleration SelectAccelerator(
        HardwareAcceleration requested,
        HardwareAcceleration implemented,
        int bufferSize)
    {
        // Пересекаем: запрошенное ∩ реализованное ∩ поддерживаемое CPU
        var available = requested & implemented & HardwareAccelerationInfo.Supported;

        // Проверяем в порядке убывания мощности
        if ((available & HardwareAcceleration.Avx512Vbmi) != 0 && bufferSize >= 64)
            return HardwareAcceleration.Avx512Vbmi;

        if ((available & HardwareAcceleration.Avx512BW) != 0 && bufferSize >= 64)
            return HardwareAcceleration.Avx512BW;

        if ((available & HardwareAcceleration.Avx512F) != 0 && bufferSize >= 64)
            return HardwareAcceleration.Avx512F;

        if ((available & HardwareAcceleration.Avx2) != 0 && bufferSize >= 32)
            return HardwareAcceleration.Avx2;

        if ((available & HardwareAcceleration.Avx) != 0 && bufferSize >= 32)
            return HardwareAcceleration.Avx;

        if ((available & HardwareAcceleration.Sse41) != 0 && bufferSize >= 8)
            return HardwareAcceleration.Sse41;

        if ((available & HardwareAcceleration.Sse2) != 0 && bufferSize >= 8)
            return HardwareAcceleration.Sse2;

        return HardwareAcceleration.None;
    }

    #endregion

    #region Parallel Processing

    /// <summary>
    /// Определяет, нужна ли параллельная обработка для буфера.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldParallelize(int pixelCount) =>
        pixelCount >= IColorSpace<Rgb24>.ParallelThreshold && Environment.ProcessorCount > 1;

    /// <summary>
    /// Вычисляет оптимальное количество потоков.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOptimalThreadCount(int pixelCount) =>
        IColorSpace<Rgb24>.GetOptimalThreadCount(pixelCount);

    /// <summary>
    /// Выполняет параллельную обработку с заданным делегатом для каждого чанка.
    /// </summary>
    /// <typeparam name="TSource">Тип исходных элементов.</typeparam>
    /// <typeparam name="TDest">Тип целевых элементов.</typeparam>
    /// <param name="source">Указатель на исходный буфер.</param>
    /// <param name="destination">Указатель на целевой буфер.</param>
    /// <param name="length">Длина буфера.</param>
    /// <param name="processChunk">Делегат обработки чанка.</param>
    public static unsafe void ParallelProcess<TSource, TDest>(
        TSource* source,
        TDest* destination,
        int length,
        ProcessChunkDelegate<TSource, TDest> processChunk)
        where TSource : unmanaged
        where TDest : unmanaged
    {
        var threadCount = GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);

            processChunk(
                new ReadOnlySpan<TSource>(source + start, size),
                new Span<TDest>(destination + start, size));
        });
    }

    /// <summary>
    /// Делегат для обработки чанка данных.
    /// </summary>
    public delegate void ProcessChunkDelegate<TSource, TDest>(
        ReadOnlySpan<TSource> source,
        Span<TDest> destination)
        where TSource : unmanaged
        where TDest : unmanaged;

    #endregion

    #region Clamping

    /// <summary>
    /// Ограничивает значение в диапазоне [0, 255].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int value) => Math.Max(0, Math.Min(255, value));

    /// <summary>
    /// Ограничивает значение float в диапазоне [0, 255] и конвертирует в byte.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ClampToByte(float value) => (byte)Math.Max(0f, Math.Min(255f, value));

    #endregion
}