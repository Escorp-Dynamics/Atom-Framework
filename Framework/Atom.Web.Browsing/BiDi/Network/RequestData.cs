using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// A network request.
/// </summary>
public class RequestData
{
    private List<ReadOnlyHeader>? readOnlyHeaders;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestData"/> class.
    /// </summary>
    [JsonConstructor]
    internal RequestData() { }

    /// <summary>
    /// Gets the ID of the request.
    /// </summary>
    [JsonPropertyName("request")]
    [JsonRequired]
    [JsonInclude]
    public string RequestId { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the URL of the request.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; } = new("about:blank");

    /// <summary>
    /// Gets the method of the request.
    /// </summary>
    [JsonPropertyName("method")]
    [JsonRequired]
    [JsonInclude]
    public string Method { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the destination of the request.
    /// </summary>
    [JsonPropertyName("destination")]
    [JsonRequired]
    [JsonInclude]
    public string Destination { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the initiator type of the request.
    /// </summary>
    [JsonPropertyName("initiatorType")]
    [JsonRequired]
    [JsonInclude]
    public string? InitiatorType { get; internal set; }

    /// <summary>
    /// Gets the headers of the request.
    /// </summary>
    [JsonIgnore]
    public IList<ReadOnlyHeader> Headers
    {
        get
        {
            readOnlyHeaders ??= [];
            foreach (var header in SerializableHeaders) readOnlyHeaders.Add(new ReadOnlyHeader(header));
            return readOnlyHeaders.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets the cookies of the request.
    /// </summary>
    [JsonIgnore]
    public IList<Cookie> Cookies => SerializableCookies.AsReadOnly();

    /// <summary>
    /// Gets the size, in bytes, of the headers in the request.
    /// </summary>
    [JsonPropertyName("headersSize")]
    [JsonRequired]
    [JsonInclude]
    public ulong? HeadersSize { get; internal set; }

    /// <summary>
    /// Gets the size, in bytes, of the body in the request.
    /// </summary>
    [JsonPropertyName("bodySize")]
    [JsonRequired]
    [JsonInclude]
    public ulong? BodySize { get; internal set; }

    /// <summary>
    /// Gets the fetch timing info of the request.
    /// </summary>
    [JsonPropertyName("timings")]
    [JsonRequired]
    [JsonInclude]
    public FetchTimingInfo Timings { get; internal set; } = new();

    /// <summary>
    /// Gets or sets the headers of the request for serialization purposes.
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonRequired]
    [JsonInclude]
    internal List<Header> SerializableHeaders { get; set; } = [];

    /// <summary>
    /// Gets or sets the cookies of the request for serialization purposes.
    /// </summary>
    [JsonPropertyName("cookies")]
    [JsonRequired]
    [JsonInclude]
    internal List<Cookie> SerializableCookies { get; set; } = [];
}