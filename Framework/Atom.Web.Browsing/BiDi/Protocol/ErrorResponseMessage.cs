#pragma warning disable CA1024
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Response class that contains the result of a command when an error is encountered.
/// </summary>
public class ErrorResponseMessage : Message
{
    /// <summary>
    /// Gets the ID for the command causing this error during execution, if any.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonInclude]
    public long? CommandId { get; internal set; }

    /// <summary>
    /// Gets the type of error encountered.
    /// </summary>
    [JsonPropertyName("error")]
    [JsonRequired]
    [JsonInclude]
    public string ErrorType { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the message of the error.
    /// </summary>
    [JsonPropertyName("message")]
    [JsonRequired]
    [JsonInclude]
    public string ErrorMessage { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the stack trace associated with this error.
    /// </summary>
    [JsonPropertyName("stacktrace")]
    [JsonInclude]
    public string? StackTrace { get; internal set; }

    /// <summary>
    /// Gets the data associated with the error without the command information.
    /// </summary>
    /// <returns>An ErrorResponseData object containing the data about the error.</returns>
    public ErrorResult GetErrorResponseData() => new(this);
}