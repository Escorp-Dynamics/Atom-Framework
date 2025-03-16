using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.continueResponse command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContinueResponseCommandParameters"/> class.
/// </remarks>
/// <param name="requestId">The ID of the request to continue.</param>
public class ContinueResponseCommandParameters(string requestId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.continueResponse";

    /// <summary>
    /// Gets or sets the ID of the request to continue.
    /// </summary>
    [JsonPropertyName("request")]
    public string RequestId { get; set; } = requestId;

    /// <summary>
    /// Gets or sets the credentials to use with this response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthCredentials? Credentials { get; set; }

    /// <summary>
    /// Gets or sets the headers of the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<Header>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the cookies of the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<SetCookieHeader>? Cookies { get; set; }

    /// <summary>
    /// Gets or sets the HTTP reason phrase ('OK', 'Not Found', etc.) of the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonPhrase { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code of the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? StatusCode { get; set; }
}