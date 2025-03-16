using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.create.
/// </summary>
public class Viewport
{
    /// <summary>
    /// Высота области просмотра.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ulong Height { get; set; }

    /// <summary>
    /// Ширина области просмотра.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ulong Width { get; set; }
}