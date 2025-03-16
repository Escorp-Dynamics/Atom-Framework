using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.setCacheBehavior command.
/// </summary>
public class SetCacheBehaviorCommandParameters : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.setCacheBehavior";

    /// <summary>
    /// Gets or sets the behavior of the cache.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public CacheBehavior CacheBehavior { get; set; }

    /// <summary>
    /// Gets or sets the contexts, if any, for which to set the cache behavior.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Contexts { get; set; }
}