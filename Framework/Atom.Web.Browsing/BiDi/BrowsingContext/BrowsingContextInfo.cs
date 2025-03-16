using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Предоставляет информацию о контексте просмотра.
/// </summary>
public class BrowsingContextInfo
{
    [JsonPropertyName("children")]
    [JsonRequired]
    [JsonInclude]
    internal IList<BrowsingContextInfo> SerializableChildren { get; set; } = [];

    /// <summary>
    /// Идентификатор контекста просмотра.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = string.Empty;

    /// <summary>
    /// Идентификатор клиентского окна, содержащего этот контекст просмотра.
    /// </summary>
    [JsonPropertyName("clientWindow")]
    // TODO (Issue #31): Раскомментировать после исправления https://bugzilla.mozilla.org/show_bug.cgi?id=1920952.
    // [JsonRequired]
    [JsonInclude]
    public string ClientWindowId { get; internal set; } = string.Empty;

    /// <summary>
    /// Идентификатор контекста просмотра, который изначально открыл этот контекст просмотра.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string? OriginalOpener { get; internal set; }

    /// <summary>
    /// URL контекста просмотра.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; } = new("about:blank");

    /// <summary>
    /// Идентификатор пользовательского контекста для контекста просмотра.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonRequired]
    [JsonInclude]
    public string UserContextId { get; internal set; } = string.Empty;

    /// <summary>
    /// Коллекция дочерних контекстов просмотра для этого контекста просмотра.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<BrowsingContextInfo> Children => SerializableChildren.AsReadOnly();

    /// <summary>
    /// Идентификатор родительского контекста просмотра для этого контекста просмотра.
    /// </summary>
    [JsonInclude]
    public string? Parent { get; internal set; }

    [JsonConstructor]
    internal BrowsingContextInfo() { }
}