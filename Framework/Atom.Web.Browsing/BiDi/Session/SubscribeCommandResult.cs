using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Result for getting the status of a remote end using the session.status command.
/// </summary>
public class SubscribeCommandResult : CommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubscribeCommandResult"/> class.
    /// </summary>
    [JsonConstructor]
    internal SubscribeCommandResult() { }

    /// <summary>
    /// Gets the ID of the subscription.
    /// </summary>
    [JsonPropertyName("subscription")]
    // TODO (Issue #38): Uncomment once https://bugzilla.mozilla.org/show_bug.cgi?id=1938576 is implemented.
    // [JsonRequired]
    [JsonInclude]
    public string SubscriptionId { get; internal set; } = string.Empty;
}