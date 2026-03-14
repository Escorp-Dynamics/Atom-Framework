namespace Atom.Media.Audio.Backends;

/// <summary>
/// Внутренний интерфейс бэкенда виртуального микрофона.
/// </summary>
internal interface IVirtualMicrophoneBackend : IAsyncDisposable
{
    /// <summary>
    /// Идентификатор устройства в системе.
    /// </summary>
    string DeviceIdentifier { get; }

    /// <summary>
    /// Активен ли захват аудиопотока.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Инициализирует бэкенд с заданными настройками.
    /// </summary>
    ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Начинает захват аудиопотока.
    /// </summary>
    ValueTask StartCaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Записывает аудио семплы.
    /// </summary>
    void WriteSamples(ReadOnlySpan<byte> sampleData);

    /// <summary>
    /// Останавливает захват аудиопотока.
    /// </summary>
    ValueTask StopCaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Устанавливает значение контрола.
    /// </summary>
    void SetControl(MicrophoneControlType control, float value);

    /// <summary>
    /// Получает текущее значение контрола.
    /// </summary>
    float GetControl(MicrophoneControlType control);

    /// <summary>
    /// Получает диапазон контрола.
    /// </summary>
    MicrophoneControlRange? GetControlRange(MicrophoneControlType control);

    /// <summary>
    /// Событие изменения контрола внешним приложением.
    /// </summary>
    event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;
}
