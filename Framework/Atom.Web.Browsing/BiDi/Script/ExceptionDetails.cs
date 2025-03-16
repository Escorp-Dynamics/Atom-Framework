using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing the details of an exception thrown by a script.
/// </summary>
public class ExceptionDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionDetails"/> class.
    /// </summary>
    [JsonConstructor]
    internal ExceptionDetails() { }

    /// <summary>
    /// Gets the text of the exception.
    /// </summary>
    [JsonPropertyName("text")]
    [JsonRequired]
    [JsonInclude]
    public string Text { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the column number of the statement that caused the exception.
    /// </summary>
    [JsonPropertyName("columnNumber")]
    [JsonRequired]
    [JsonInclude]
    public int ColumnNumber { get; internal set; } = -1;

    /// <summary>
    /// Gets the line number of the statement that caused the exception.
    /// </summary>
    [JsonPropertyName("lineNumber")]
    [JsonRequired]
    [JsonInclude]
    public int LineNumber { get; internal set; } = -1;

    /// <summary>
    /// Gets the stack trace of the exception.
    /// </summary>
    [JsonPropertyName("stackTrace")]
    [JsonRequired]
    [JsonInclude]
    public StackTrace StackTrace { get; internal set; } = new();

    /// <summary>
    /// Gets the RemoteValue representing the value of the exception.
    /// </summary>
    [JsonPropertyName("exception")]
    [JsonRequired]
    [JsonInclude]
    public RemoteValue Exception { get; internal set; } = new("null");
}