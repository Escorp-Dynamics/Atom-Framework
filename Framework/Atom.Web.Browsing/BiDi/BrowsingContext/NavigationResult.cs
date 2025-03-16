using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат навигации.
/// </summary>
public class NavigationResult : CommandResult
{
    /// <summary>
    /// Идентификатор навигации.
    /// </summary>
    [JsonPropertyName("navigation")]
    [JsonInclude]
    public string? NavigationId { get; internal set; }

    /// <summary>
    /// URL навигации.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; } = new("about:blank");

    [JsonConstructor]
    internal NavigationResult() { }
}