using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат выполнения команды browserContext.captureScreenshot для создания скриншота.
/// </summary>
public class CaptureScreenshotCommandResult : CommandResult
{
    /// <summary>
    /// Данные изображения скриншота в виде строки, закодированной в base64.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Data { get; internal set; } = string.Empty;

    [JsonConstructor]
    internal CaptureScreenshotCommandResult() { }
}