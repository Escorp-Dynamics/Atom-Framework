namespace Atom.Media.Audio;

/// <summary>
/// Настройки виртуального микрофона.
/// </summary>
public sealed record VirtualMicrophoneSettings
{
    /// <summary>
    /// Частота дискретизации в герцах.
    /// </summary>
    public int SampleRate { get; init; } = 48000;

    /// <summary>
    /// Количество аудиоканалов.
    /// </summary>
    public int Channels { get; init; } = 1;

    /// <summary>
    /// Формат аудио семплов.
    /// </summary>
    public AudioSampleFormat SampleFormat { get; init; } = AudioSampleFormat.F32;

    /// <summary>
    /// Целевая латентность в миллисекундах.
    /// Определяет размер аудиобуфера PipeWire (quantum).
    /// Меньше — ниже задержка, но выше нагрузка на CPU.
    /// </summary>
    public int LatencyMs { get; init; } = 10;

    /// <summary>
    /// Отображаемое имя устройства в системе.
    /// </summary>
    public string Name { get; init; } = "Virtual Microphone";

    /// <summary>
    /// Идентификатор устройства для связи с <c>VirtualCamera</c>.
    /// Если задан одинаковый DeviceId у камеры и микрофона,
    /// PipeWire группирует их как одно логическое устройство.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>Производитель устройства.</summary>
    public string? Vendor { get; init; }

    /// <summary>Модель устройства.</summary>
    public string? Model { get; init; }

    /// <summary>Серийный номер устройства.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Описание устройства.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// USB Vendor ID (VID) для эмуляции устройства.
    /// </summary>
    public int? UsbVendorId { get; init; }

    /// <summary>
    /// USB Product ID (PID) для эмуляции устройства.
    /// </summary>
    public int? UsbProductId { get; init; }

    /// <summary>Дополнительные PipeWire-свойства (key-value).</summary>
    public IReadOnlyDictionary<string, string>? ExtraProperties { get; init; }
}
