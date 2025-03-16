using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The initiator of a network traffic item.
/// </summary>
public class Initiator
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Initiator"/> class.
    /// </summary>
    [JsonConstructor]
    internal Initiator() { }

    /// <summary>
    /// Gets the type of entity initiating the request.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonInclude]
    public InitiatorType? Type { get; internal set; }

    /// <summary>
    /// Gets the column number of the script initiating the request.
    /// </summary>
    [JsonPropertyName("columnNumber")]
    [JsonInclude]
    public ulong? ColumnNumber { get; internal set; }

    /// <summary>
    /// Gets the column number of the script initiating the request.
    /// </summary>
    [JsonPropertyName("lineNumber")]
    [JsonInclude]
    public ulong? LineNumber { get; internal set; }

    /// <summary>
    /// Gets the stack trace of the script initiating the request.
    /// </summary>
    [JsonPropertyName("stackTrace")]
    [JsonInclude]
    public StackTrace? StackTrace { get; internal set; }

    /// <summary>
    /// Gets the ID of the request.
    /// </summary>
    [JsonPropertyName("request")]
    [JsonInclude]
    public string? RequestId { get; internal set; }
}