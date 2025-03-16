using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.addIntercept command.
/// </summary>
public class AddInterceptCommandParameters : CommandParameters<AddInterceptCommandResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddInterceptCommandParameters"/> class.
    /// </summary>
    public AddInterceptCommandParameters() { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.addIntercept";

    /// <summary>
    /// Gets the list of phases for which network traffic will be intercepted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IEnumerable<InterceptPhase> Phases { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of top-level browsing context ID for which traffic will be intercepted.
    /// If present, it must contain at least one browsing context ID, and all IDs must represent top-level
    /// browsing contexts, or an error will be thrown by the remote end.
    /// </summary>
    [JsonPropertyName("contexts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IEnumerable<string>? BrowsingContextIds { get; set; }

    /// <summary>
    /// Gets or sets list of URL patterns for which to intercept network traffic.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonInclude]
    public IEnumerable<UrlPattern>? UrlPatterns { get; set; }
}