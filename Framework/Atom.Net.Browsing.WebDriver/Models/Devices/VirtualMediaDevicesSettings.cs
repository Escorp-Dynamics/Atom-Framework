namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает tab-local модель enumerateDevices и routed getUserMedia.
/// </summary>
public sealed class VirtualMediaDevicesSettings
{
    /// <summary>
    /// Получает или задаёт признак наличия виртуального микрофона.
    /// </summary>
    public bool AudioInputEnabled { get; set; } = true;

    /// <summary>
    /// Получает или задаёт label виртуального микрофона.
    /// </summary>
    public string AudioInputLabel { get; set; } = "Virtual Microphone";

    /// <summary>
    /// Получает или задаёт browser-visible deviceId виртуального микрофона.
    /// </summary>
    public string? AudioInputBrowserDeviceId { get; set; }

    /// <summary>
    /// Получает или задаёт признак наличия виртуальной камеры.
    /// </summary>
    public bool VideoInputEnabled { get; set; } = true;

    /// <summary>
    /// Получает или задаёт label виртуальной камеры.
    /// </summary>
    public string VideoInputLabel { get; set; } = "Virtual Camera";

    /// <summary>
    /// Получает или задаёт browser-visible deviceId виртуальной камеры.
    /// </summary>
    public string? VideoInputBrowserDeviceId { get; set; }

    /// <summary>
    /// Получает или задаёт признак наличия виртуального audio output.
    /// </summary>
    public bool AudioOutputEnabled { get; set; }

    /// <summary>
    /// Получает или задаёт label виртуального audio output.
    /// </summary>
    public string AudioOutputLabel { get; set; } = "Virtual Speakers";

    /// <summary>
    /// Получает или задаёт groupId виртуальных устройств.
    /// </summary>
    public string? GroupId { get; set; }
}