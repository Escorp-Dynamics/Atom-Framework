using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.continueResponse command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContinueWithAuthCommandParameters"/> class.
/// </remarks>
/// <param name="requestId">The ID of the request to continue.</param>
public class ContinueWithAuthCommandParameters(string requestId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.continueWithAuth";

    /// <summary>
    /// Gets or sets the ID of the request to continue.
    /// </summary>
    [JsonPropertyName("request")]
    public string RequestId { get; set; } = requestId;

    /// <summary>
    /// Gets or sets the action to use with continuing this request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ContinueWithAuthActionType Action { get; set; }

    /// <summary>
    /// Gets or sets the credentials to be used when continuing this request.
    /// Credentials are only sent when the action is set to <see cref="ContinueWithAuthActionType.ProvideCredentials"/>.
    /// </summary>
    [JsonIgnore]
    public AuthCredentials Credentials { get; set; } = new();

    /// <summary>
    /// Gets the credentials to be used for continuing the request for authorization purposes.
    /// Credentials are only sent when the action is set to <see cref="ContinueWithAuthActionType.ProvideCredentials"/>.
    /// </summary>
    [JsonPropertyName("credentials")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal AuthCredentials? SerializableCredentials => Action == ContinueWithAuthActionType.ProvideCredentials ? Credentials : null;
}