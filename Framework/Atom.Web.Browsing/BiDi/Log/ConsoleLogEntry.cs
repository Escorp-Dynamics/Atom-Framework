using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Log;

/// <summary>
/// Represents a console log entry in the browser.
/// </summary>
public class ConsoleLogEntry : LogEntry
{

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLogEntry"/> class.
    /// </summary>
    internal ConsoleLogEntry() : base() { }

    /// <summary>
    /// Gets the method for the console log entry.
    /// </summary>
    [JsonPropertyName("method")]
    [JsonRequired]
    [JsonInclude]
    public string Method { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the read-only list of arguments for the console log entry.
    /// </summary>
    [JsonIgnore]
    public IList<RemoteValue> Args => SerializableArgs.AsReadOnly();

    /// <summary>
    /// Gets or sets the arguments of the console log entry for serialization purposes.
    /// </summary>
    [JsonPropertyName("args")]
    [JsonRequired]
    [JsonInclude]
    internal List<RemoteValue> SerializableArgs { get; set; } = [];
}