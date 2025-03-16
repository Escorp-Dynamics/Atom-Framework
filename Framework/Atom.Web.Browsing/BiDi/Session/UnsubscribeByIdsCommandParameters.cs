using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Provides parameters for the session.unsubscribe command.
/// </summary>
public class UnsubscribeByIdsCommandParameters : UnsubscribeCommandParameters
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsubscribeByIdsCommandParameters"/> class.
    /// </summary>
    public UnsubscribeByIdsCommandParameters() : base() { }

    /// <summary>
    /// Gets the list of events to which to subscribe or unsubscribe.
    /// </summary>
    [JsonPropertyName("subscriptions")]
    public IList<string> SubscriptionIds { get; } = [];
}