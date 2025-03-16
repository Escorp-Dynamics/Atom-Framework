using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.failRequest command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="FailRequestCommandParameters"/> class.
/// </remarks>
/// <param name="requestId">The ID of the request to fail.</param>
public class FailRequestCommandParameters(string requestId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.failRequest";

    /// <summary>
    /// Gets or sets the ID of the request to fail..
    /// </summary>
    [JsonPropertyName("request")]
    public string RequestId { get; set; } = requestId;
}