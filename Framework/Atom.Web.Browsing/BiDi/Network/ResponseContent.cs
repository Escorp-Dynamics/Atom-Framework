using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Content of a response.
/// </summary>
public class ResponseContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseContent"/> class.
    /// </summary>
    [JsonConstructor]
    internal ResponseContent() { }

    /// <summary>
    /// Gets the decoded size, in bytes, of the response body.
    /// </summary>
    [JsonPropertyName("size")]
    [JsonRequired]
    [JsonInclude]
    public ulong Size { get; internal set; } = 0;
}