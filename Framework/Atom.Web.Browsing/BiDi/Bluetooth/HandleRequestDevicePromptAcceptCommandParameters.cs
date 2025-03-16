using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет параметры для команды bluetooth.handleRequestDevicePrompt, чтобы принять запрос на подключение.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HandleRequestDevicePromptAcceptCommandParameters"/> для принятия запроса на устройство.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого принимается запрос.</param>
/// <param name="promptId">Идентификатор запроса, который нужно принять.</param>
/// <param name="deviceId">Идентификатор устройства, для которого принимается запрос.</param>
public class HandleRequestDevicePromptAcceptCommandParameters(string browsingContextId, string promptId, string deviceId) : HandleRequestDevicePromptCommandParameters(browsingContextId, promptId, true, deviceId)
{
    /// <summary>
    /// Идентификатор устройства, для которого принимается запрос.
    /// </summary>
    [JsonIgnore]
    public string DeviceId
    {
        get => SerializableDeviceId!;
        set => SerializableDeviceId = value;
    }
}