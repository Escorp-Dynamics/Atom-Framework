using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет информацию о Bluetooth-устройстве во время запроса.
/// </summary>
public class RequestDeviceInfo
{
    /// <summary>
    /// Идентификатор Bluetooth-устройства.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonInclude]
    public string DeviceId { get; internal set; } = string.Empty;

    /// <summary>
    /// Имя Bluetooth-устройства, если оно указано.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    [JsonInclude]
    public string? DeviceName { get; internal set; }

    [JsonConstructor]
    internal RequestDeviceInfo() { }
}