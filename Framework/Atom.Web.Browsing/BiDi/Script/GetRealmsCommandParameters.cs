using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.getRealms command.
/// </summary>
public class GetRealmsCommandParameters : CommandParameters<GetRealmsCommandResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetRealmsCommandParameters"/> class.
    /// </summary>
    public GetRealmsCommandParameters() { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.getRealms";

    /// <summary>
    /// Gets or sets the ID of the browsing context of the realms to get.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? BrowsingContextId { get; set; }

    /// <summary>
    /// Gets or sets the type of realms to get.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public RealmType? RealmType { get; set; }
}