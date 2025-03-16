using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing the successful evaluation of a script.
/// </summary>
public class EvaluateResultSuccess : EvaluateResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluateResultSuccess"/> class.
    /// </summary>
    [JsonConstructor]
    internal EvaluateResultSuccess() : base() { }

    /// <summary>
    /// Gets the result of the script evaluation.
    /// </summary>
    [JsonPropertyName("result")]
    [JsonInclude]
    public RemoteValue Result { get; internal set; } = new("null");
}