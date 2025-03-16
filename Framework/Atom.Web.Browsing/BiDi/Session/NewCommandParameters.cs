using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Provides parameters for the session.new command.
/// </summary>
public class NewCommandParameters : CommandParameters<NewCommandResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "session.new";

    /// <summary>
    /// Gets or sets the capabilities to use for the new session.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public CapabilitiesRequest Capabilities { get; set; } = new CapabilitiesRequest();

    /// <inheritdoc/>
    public override void ClearForPool()
    {
        base.ClearForPool();
        Capabilities.ClearForPool();
    }
}