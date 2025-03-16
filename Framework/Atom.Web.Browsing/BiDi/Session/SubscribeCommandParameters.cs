using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Provides parameters for the session.subscribe command.
/// </summary>
public class SubscribeCommandParameters : CommandParameters<SubscribeCommandResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubscribeCommandParameters"/> class.
    /// </summary>
    public SubscribeCommandParameters() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscribeCommandParameters"/> class.
    /// </summary>
    /// <param name="events">The list of events to which to subscribe or unsubscribe.</param>
    /// <param name="contexts">The list of browsing context IDs for which to subscribe to or unsubscribe from the specified events.</param>
    /// <param name="userContexts">The list of user context IDs for which to subscribe to the specified events.</param>
    public SubscribeCommandParameters([NotNull] IList<string> events, [NotNull] IList<string> contexts, [NotNull] IList<string> userContexts)
    {
        foreach (var e in events) Events.Add(e);
        foreach (var ctx in contexts) Contexts.Add(ctx);
        foreach (var ctx in userContexts) UserContexts.Add(ctx);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscribeCommandParameters"/> class.
    /// </summary>
    /// <param name="events">The list of events to which to subscribe or unsubscribe.</param>
    /// <param name="contexts">The list of browsing context IDs for which to subscribe to or unsubscribe from the specified events.</param>
    public SubscribeCommandParameters([NotNull] IList<string> events, [NotNull] IList<string> contexts) : this(events, contexts, []) { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "session.subscribe";

    /// <summary>
    /// Gets the list of events to which to subscribe or unsubscribe.
    /// </summary>
    [JsonPropertyName("events")]
    public IList<string> Events { get; } = [];

    /// <summary>
    /// Gets the list of browsing context IDs for which to subscribe to the specified events.
    /// </summary>
    [JsonIgnore]
    public IList<string> Contexts { get; } = [];

    /// <summary>
    /// Gets the list of user context IDs for which to subscribe to the specified events.
    /// </summary>
    [JsonIgnore]
    public IList<string> UserContexts { get; } = [];

    /// <summary>
    /// Gets the list of browsing context IDs for which to subscribe to or unsubscribe from the specified events for serialization purposes.
    /// </summary>
    [JsonPropertyName("contexts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal IList<string>? SerializableContexts => Contexts.Count is 0 ? null : Contexts;

    /// <summary>
    /// Gets the list of user context IDs for which to subscribe to the specified events for serialization purposes.
    /// </summary>
    [JsonPropertyName("userContexts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal IList<string>? SerializableUserContexts => UserContexts.Count is 0 ? null : UserContexts;
}