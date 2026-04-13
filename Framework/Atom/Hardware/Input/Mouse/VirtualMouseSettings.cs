using System.Drawing;

namespace Atom.Hardware.Input;

/// <summary>
/// Настройки виртуальной мыши.
/// </summary>
public sealed record VirtualMouseSettings
{
    /// <summary>
    /// Размер экрана в пикселях (для абсолютного позиционирования).
    /// </summary>
    public Size ScreenSize { get; init; } = new(1920, 1080);

    /// <summary>
    /// Отображаемое имя устройства в системе.
    /// </summary>
    public string Name { get; init; } = "Virtual Mouse";

    /// <summary>
    /// USB Vendor ID (VID) для эмуляции устройства.
    /// </summary>
    public int? UsbVendorId { get; init; }

    /// <summary>
    /// USB Product ID (PID) для эмуляции устройства.
    /// </summary>
    public int? UsbProductId { get; init; }

    /// <summary>
    /// Разрешает создание отдельного MPX-курсора на Linux/X11.
    /// Отключение оставляет устройство на общем системном курсоре.
    /// </summary>
    public bool UseSeparateCursor { get; init; } = true;
}
