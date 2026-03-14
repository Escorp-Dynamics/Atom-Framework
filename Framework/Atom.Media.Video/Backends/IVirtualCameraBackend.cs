namespace Atom.Media.Video.Backends;

/// <summary>
/// Платформенный бэкенд виртуальной камеры.
/// Отвечает за создание виртуального устройства, управление захватом
/// и передачу фреймов в систему.
/// </summary>
internal interface IVirtualCameraBackend : IAsyncDisposable
{
    /// <summary>
    /// Идентификатор виртуального устройства камеры (путь, имя ноды и т.д.).
    /// </summary>
    string DeviceIdentifier { get; }

    /// <summary>
    /// Определяет, активен ли захват.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Инициализирует виртуальное устройство камеры.
    /// </summary>
    /// <param name="settings">Настройки камеры.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Запускает захват видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask StartCaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Записывает сырые данные кадра.
    /// </summary>
    /// <param name="frameData">Данные кадра (все плоскости последовательно).</param>
    void WriteFrame(ReadOnlySpan<byte> frameData);

    /// <summary>
    /// Останавливает захват видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask StopCaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Устанавливает значение контрола камеры.
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <param name="value">Значение контрола.</param>
    void SetControl(CameraControlType control, float value);

    /// <summary>
    /// Получает текущее значение контрола камеры.
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Текущее значение контрола.</returns>
    float GetControl(CameraControlType control);

    /// <summary>
    /// Получает диапазон контрола камеры (min, max, default).
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Диапазон контрола или null, если диапазон неизвестен.</returns>
    CameraControlRange? GetControlRange(CameraControlType control);

    /// <summary>
    /// Событие изменения контрола камеры внешним приложением.
    /// </summary>
    event EventHandler<CameraControlChangedEventArgs>? ControlChanged;
}
