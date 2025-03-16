using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Represents a frame within a stack trace for a script.
/// </summary>
public class StackFrame
{
    [JsonConstructor]
    internal StackFrame() { }

    /// <summary>
    /// Gets the name of the function for this stack frame.
    /// </summary>
    [JsonPropertyName("functionName")]
    [JsonRequired]
    [JsonInclude]
    public string FunctionName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the line number for this stack frame.
    /// </summary>
    [JsonPropertyName("lineNumber")]
    [JsonRequired]
    [JsonInclude]
    public int LineNumber { get; internal set; } = -1;

    /// <summary>
    /// Gets the column number for this stack frame.
    /// </summary>
    [JsonPropertyName("columnNumber")]
    [JsonRequired]
    [JsonInclude]
    public int ColumnNumber { get; internal set; } = -1;

    /// <summary>
    /// Gets the URL for this stack frame.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; } = new("about:blank");
}