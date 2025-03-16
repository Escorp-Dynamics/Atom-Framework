using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Provides parameters for the session.unsubscribe command.
/// </summary>
public class UnsubscribeByAttributesCommandParameters : UnsubscribeCommandParameters
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsubscribeByAttributesCommandParameters"/> class.
    /// </summary>
    public UnsubscribeByAttributesCommandParameters() : base() { }

    /// <summary>
    /// Gets the list of events to which to subscribe or unsubscribe.
    /// </summary>
    [JsonPropertyName("events")]
    public IList<string> Events { get; } = [];

    /// <summary>
    /// Gets the list of browsing context IDs for which to subscribe to or unsubscribe from the specified events.
    /// </summary>
    // TODO (issue #36): Remove obsolete property when removed from specification.
    //[Obsolete("This property will be removed when it is removed from the W3C WebDriver BiDi Specification (see https://w3c.github.io/webdriver-bidi/#type-session-UnsubscribeByAttributesRequest)")]
    [JsonIgnore]
    public IList<string> Contexts { get; } = [];

    /// <summary>
    /// Gets the list of browsing context IDs for which to subscribe to or unsubscribe from the specified events for serialization purposes.
    /// </summary>
    [JsonPropertyName("contexts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal IList<string>? SerializableContexts => Contexts.Count is 0 ? null : Contexts;
}
