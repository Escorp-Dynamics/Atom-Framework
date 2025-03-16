using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Provides parameters for the network.continueRequest command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContinueRequestCommandParameters"/> class.
/// </remarks>
/// <param name="requestId">The ID of the request to continue.</param>
public class ContinueRequestCommandParameters(string requestId) : CommandParameters<EmptyResult>
{

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "network.continueRequest";

    /// <summary>
    /// Gets or sets the ID of the request to continue.
    /// </summary>
    [JsonPropertyName("request")]
    public string RequestId { get; set; } = requestId;

    /// <summary>
    /// Gets or sets the body of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public BytesValue? Body { get; set; }

    /// <summary>
    /// Gets or sets the headers of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<Header>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the cookie headers of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<CookieHeader>? Cookies { get; set; }

    /// <summary>
    /// Gets or sets the HTTP method of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; set; }

    /// <summary>
    /// Gets or sets the URL of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Url { get; set; }
}