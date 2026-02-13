#pragma warning disable CA1000, CA1716, IDE0040, MA0018

using System.Runtime.CompilerServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Контракт цветового пространства.
/// Определяет методы конвертации между различными цветовыми пространствами.
/// </summary>
/// <typeparam name="TSelf">Тип реализующей структуры (CRTP паттерн).</typeparam>
public interface IColorSpace<TSelf> where TSelf : unmanaged, IColorSpace<TSelf>
{
    #region Constants

    /// <summary>Минимальный размер буфера для параллельной обработки.</summary>
    public const int ParallelThreshold = 1024;

    /// <summary>
    /// Аппаратные ускорители, поддерживаемые реализацией для пакетных конвертаций.
    /// Возвращает флаги всех реализованных SIMD-путей.
    /// </summary>
    static virtual HardwareAcceleration SupportedAccelerations => HardwareAcceleration.None;

    /// <summary>Размер чанка для малых буферов (меньше 720p).</summary>
    private const int SmallBufferChunkSize = 32768;

    /// <summary>Размер чанка для средних буферов (720p).</summary>
    private const int MediumBufferChunkSize = 65536;

    /// <summary>Размер чанка для больших буферов (1080p+).</summary>
    private const int LargeBufferChunkSize = 131072;

    /// <summary>Порог пикселей для средних буферов (~500K, выше 480p).</summary>
    private const int MediumBufferThreshold = 400_000;

    /// <summary>Порог пикселей для больших буферов (~1.5M, 1080p и выше).</summary>
    private const int LargeBufferThreshold = 1_500_000;

    #endregion

    #region Static Helper Methods

    /// <summary>
    /// Вычисляет оптимальное количество потоков с адаптивным chunk size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOptimalThreadCount(int pixelCount)
    {
        var chunkSize = GetOptimalChunkSize(pixelCount);
        return Math.Max(1, Math.Min(Environment.ProcessorCount, pixelCount / chunkSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetOptimalChunkSize(int pixelCount)
    {
        if (pixelCount >= LargeBufferThreshold)
            return LargeBufferChunkSize;

        if (pixelCount >= MediumBufferThreshold)
            return MediumBufferChunkSize;

        return SmallBufferChunkSize;
    }

    /// <summary>
    /// Проверяет, нужна ли параллельная обработка для буфера.
    /// </summary>
    /// <param name="pixelCount">Количество пикселей в буфере.</param>
    /// <returns>True, если нужна параллельная обработка.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldParallelize(int pixelCount) =>
        pixelCount >= ParallelThreshold && Environment.ProcessorCount > 1;

    /// <summary>
    /// Ограничивает значение в диапазоне [0, 255].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int value) => Math.Max(0, Math.Min(255, value));

    #endregion

    #region Abstract Members

    /// <summary>
    /// Преобразует текущее цветовое пространство в целевое.
    /// </summary>
    /// <typeparam name="T">Целевое цветовое пространство.</typeparam>
    /// <returns>Значение в целевом цветовом пространстве.</returns>
    T To<T>() where T : unmanaged, IColorSpace<T>;

    /// <summary>
    /// Создаёт значение текущего цветового пространства из исходного.
    /// </summary>
    /// <typeparam name="T">Исходное цветовое пространство.</typeparam>
    /// <param name="source">Значение в исходном цветовом пространстве.</param>
    /// <returns>Значение в текущем цветовом пространстве.</returns>
    static abstract TSelf From<T>(T source) where T : unmanaged, IColorSpace<T>;

    /// <summary>
    /// Пакетная конвертация из исходного цветового пространства.
    /// Использует SIMD-оптимизации при наличии поддержки.
    /// </summary>
    /// <typeparam name="T">Исходное цветовое пространство.</typeparam>
    /// <param name="source">Исходный буфер.</param>
    /// <param name="destination">Целевой буфер.</param>
    static abstract void From<T>(ReadOnlySpan<T> source, Span<TSelf> destination)
        where T : unmanaged, IColorSpace<T>;

    /// <summary>
    /// Пакетная конвертация из исходного цветового пространства с явным указанием ускорителя.
    /// </summary>
    /// <typeparam name="T">Исходное цветовое пространство.</typeparam>
    /// <param name="source">Исходный буфер.</param>
    /// <param name="destination">Целевой буфер.</param>
    /// <param name="acceleration">Флаги разрешённых ускорителей. Auto = выбор лучшего доступного.</param>
    static virtual void From<T>(ReadOnlySpan<T> source, Span<TSelf> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T> =>
        TSelf.From(source, destination); // По умолчанию игнорирует флаг, реализации переопределяют

    /// <summary>
    /// Пакетная конвертация в целевое цветовое пространство.
    /// Использует SIMD-оптимизации при наличии поддержки.
    /// </summary>
    /// <typeparam name="T">Целевое цветовое пространство.</typeparam>
    /// <param name="source">Исходный буфер.</param>
    /// <param name="destination">Целевой буфер.</param>
    static abstract void To<T>(ReadOnlySpan<TSelf> source, Span<T> destination)
        where T : unmanaged, IColorSpace<T>;

    /// <summary>
    /// Пакетная конвертация в целевое цветовое пространство с явным указанием ускорителя.
    /// </summary>
    /// <typeparam name="T">Целевое цветовое пространство.</typeparam>
    /// <param name="source">Исходный буфер.</param>
    /// <param name="destination">Целевой буфер.</param>
    /// <param name="acceleration">Флаги разрешённых ускорителей. Auto = выбор лучшего доступного.</param>
    static virtual void To<T>(ReadOnlySpan<TSelf> source, Span<T> destination, HardwareAcceleration acceleration)
        where T : unmanaged, IColorSpace<T> =>
        TSelf.To(source, destination); // По умолчанию игнорирует флаг, реализации переопределяют

    #endregion
}
