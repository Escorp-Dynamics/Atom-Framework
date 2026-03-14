namespace Atom.Media.Video;

/// <summary>
/// Настройки виртуальной камеры.
/// </summary>
public sealed record VirtualCameraSettings
{
    /// <summary>
    /// Ширина кадра в пикселях.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Высота кадра в пикселях.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Частота кадров в секунду.
    /// </summary>
    public int FrameRate { get; init; } = 30;

    /// <summary>
    /// Формат пикселей.
    /// </summary>
    public VideoPixelFormat PixelFormat { get; init; } = VideoPixelFormat.Yuv420P;

    /// <summary>
    /// Название камеры в системе.
    /// </summary>
    public string Name { get; init; } = "Virtual Camera";

    /// <summary>
    /// Производитель камеры.
    /// </summary>
    public string? Vendor { get; init; }

    /// <summary>
    /// Модель камеры.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Серийный номер камеры.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Описание камеры.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Версия прошивки камеры.
    /// </summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>
    /// USB Vendor ID (VID) для эмуляции устройства.
    /// </summary>
    public int? UsbVendorId { get; init; }

    /// <summary>
    /// USB Product ID (PID) для эмуляции устройства.
    /// </summary>
    public int? UsbProductId { get; init; }

    /// <summary>
    /// Тип шины устройства (например, "usb", "pci", "virtual").
    /// </summary>
    public string? BusType { get; init; }

    /// <summary>
    /// Форм-фактор устройства (например, "webcam", "handset").
    /// </summary>
    public string? FormFactor { get; init; }

    /// <summary>
    /// Имя иконки устройства (freedesktop icon name, например "camera-web").
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// Идентификатор устройства для связи с <c>VirtualMicrophone</c>.
    /// Если задан одинаковый DeviceId у камеры и микрофона,
    /// PipeWire группирует их как одно логическое устройство.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Дополнительные свойства, передаваемые нативному бэкенду.
    /// На Linux — PipeWire node properties (ключ = имя свойства, значение = строковое значение).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExtraProperties { get; init; }
}
