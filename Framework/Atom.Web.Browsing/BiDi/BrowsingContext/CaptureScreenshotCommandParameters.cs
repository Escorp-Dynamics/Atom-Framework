using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.captureScreenshot.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CaptureScreenshotCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого делается скриншот.</param>
public class CaptureScreenshotCommandParameters(string browsingContextId) : CommandParameters<CaptureScreenshotCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.captureScreenshot";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого делается скриншот.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Формат изображения скриншота.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageFormat? Format { get; set; }

    /// <summary>
    /// Прямоугольник обрезки для скриншота, если он есть.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipRectangle? Clip { get; set; }

    /// <summary>
    /// Начало координат для прямоугольника обрезки скриншота, если он есть.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenshotOrigin? Origin { get; set; }
}