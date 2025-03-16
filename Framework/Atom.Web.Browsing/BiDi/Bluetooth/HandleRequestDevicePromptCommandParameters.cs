using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Предоставляет параметры для команды bluetooth.handleRequestDevicePrompt.
/// </summary>
public class HandleRequestDevicePromptCommandParameters : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Идентификатор устройства для целей сериализации.
    /// </summary>
    [JsonPropertyName("device")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? SerializableDeviceId { get; set; }

    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "bluetooth.handleRequestDevicePrompt";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого обрабатывается запрос.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonInclude]
    public string BrowsingContextId { get; set; }

    /// <summary>
    /// Идентификатор запроса для обработки.
    /// </summary>
    [JsonPropertyName("prompt")]
    [JsonInclude]
    public string PromptId { get; set; }

    /// <summary>
    /// Определяет, следует ли принять запрос.
    /// </summary>
    [JsonPropertyName("accept")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool IsAccept { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HandleRequestDevicePromptCommandParameters"/>.
    /// </summary>
    /// <param name="browsingContextId">Идентификатор контекста просмотра, для которого обрабатывается запрос.</param>
    /// <param name="promptId">Идентификатор запроса для обработки.</param>
    /// <param name="isAccept">Значение, указывающее, следует ли принять запрос.</param>
    /// <param name="deviceId">Идентификатор устройства, для которого принимается запрос, если запрос принимается.</param>
    protected HandleRequestDevicePromptCommandParameters(string browsingContextId, string promptId, bool isAccept, string? deviceId = null)
    {
        BrowsingContextId = browsingContextId;
        PromptId = promptId;
        IsAccept = isAccept;
        SerializableDeviceId = deviceId;
    }
}