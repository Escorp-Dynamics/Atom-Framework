using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет объект, содержащий данные события, возникающего при запросе на подключение к Bluetooth-устройству.
/// </summary>
public class RequestDevicePromptUpdatedEventArgs : BiDiEventArgs
{
    [JsonPropertyName("devices")]
    [JsonRequired]
    [JsonInclude]
    internal IList<RequestDeviceInfo> SerializableDevices { get; set; } = [];

    /// <summary>
    /// Идентификатор контекста просмотра, запрашивающего обновление.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = string.Empty;

    /// <summary>
    /// Идентификатор запроса.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Prompt { get; internal set; } = string.Empty;

    /// <summary>
    /// Список устройств, запрашиваемых в запросе.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<RequestDeviceInfo> Devices => SerializableDevices.AsReadOnly();

    [JsonConstructor]
    internal RequestDevicePromptUpdatedEventArgs() { }
}