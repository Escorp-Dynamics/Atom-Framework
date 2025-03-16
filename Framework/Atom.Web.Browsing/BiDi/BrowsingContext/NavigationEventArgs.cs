using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет данные события, возникающего во время навигации.
/// </summary>
public class NavigationEventArgs : BiDiEventArgs
{
    /// <summary>
    /// Идентификатор операции навигации.
    /// </summary>
    [JsonPropertyName("navigation")]
    [JsonInclude]
    public string? NavigationId { get; internal set; }

    /// <summary>
    /// Идентификатор контекста просмотра, в котором происходит навигация.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; }

    /// <summary>
    /// URL, по которому происходит навигация.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; }

    /// <summary>
    /// Временная метка навигации в формате UTC.
    /// </summary>
    [JsonIgnore]
    public DateTime Timestamp { get; internal set; }

    /// <summary>
    /// Временная метка в виде общего количества миллисекунд, прошедших с начала Unix-эпохи (1 января 1970 года, 00:00:00 UTC).
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonRequired]
    [JsonInclude]
    public long EpochTimestamp
    {
        get;

        internal set
        {
            field = value;
            Timestamp = DateTime.UnixEpoch.AddMilliseconds(value);
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NavigationEventArgs"/>.
    /// </summary>
    /// <param name="browsingContextId">Идентификатор контекста просмотра, в котором происходит навигация.</param>
    /// <param name="url">URL навигации.</param>
    /// <param name="epochTimestamp">Временная метка навигации.</param>
    /// <param name="navigationId">Идентификатор навигации.</param>
    [JsonConstructor]
    public NavigationEventArgs(string browsingContextId, Uri url, long epochTimestamp, string? navigationId)
    {
        BrowsingContextId = browsingContextId;
        Url = url;
        EpochTimestamp = epochTimestamp;
        NavigationId = navigationId;
    }
}