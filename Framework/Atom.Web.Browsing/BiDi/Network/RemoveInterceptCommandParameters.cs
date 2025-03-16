using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.removeIntercept command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RemoveInterceptCommandParameters"/> class.
/// </remarks>
/// <param name="interceptId">The ID of the intercept to remove.</param>
public class RemoveInterceptCommandParameters(string interceptId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.removeIntercept";

    /// <summary>
    /// Gets or sets the ID of the intercept to remove.
    /// </summary>
    [JsonPropertyName("intercept")]
    public string InterceptId { get; set; } = interceptId;
}