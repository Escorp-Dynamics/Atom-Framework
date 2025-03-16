using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a stack trace from a script.
/// </summary>
public class StackTrace
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StackTrace"/> class.
    /// </summary>
    [JsonConstructor]
    internal StackTrace() { }

    /// <summary>
    /// Gets the read-only list of stack frames for this stack trace.
    /// </summary>
    [JsonIgnore]
    public IList<StackFrame> CallFrames => SerializableCallFrames.AsReadOnly();

    /// <summary>
    /// Gets or sets the list of stack frames for serialization purposes.
    /// </summary>
    [JsonPropertyName("callFrames")]
    [JsonRequired]
    [JsonInclude]
    internal List<StackFrame> SerializableCallFrames { get; set; } = [];
}