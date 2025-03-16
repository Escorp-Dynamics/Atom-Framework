using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.create.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SetViewportCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого устанавливается область просмотра.</param>
public class SetViewportCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.setViewport";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого устанавливается область просмотра.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonInclude]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Размеры области просмотра для установки. Значение null устанавливает область просмотра в размеры по умолчанию.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Viewport? Viewport { get; set; }

    /// <summary>
    /// Коэффициент плотности пикселей устройства для области просмотра.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DevicePixelRatio { get; set; }
}