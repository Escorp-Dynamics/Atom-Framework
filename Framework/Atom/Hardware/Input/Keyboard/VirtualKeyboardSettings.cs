namespace Atom.Hardware.Input;

/// <summary>
/// Настройки виртуальной клавиатуры.
/// </summary>
public sealed record VirtualKeyboardSettings
{
    /// <summary>
    /// Отображаемое имя устройства в системе.
    /// </summary>
    public string Name { get; init; } = "Virtual Keyboard";

    /// <summary>
    /// USB Vendor ID (VID) для эмуляции устройства.
    /// </summary>
    public int? UsbVendorId { get; init; }

    /// <summary>
    /// USB Product ID (PID) для эмуляции устройства.
    /// </summary>
    public int? UsbProductId { get; init; }
}
