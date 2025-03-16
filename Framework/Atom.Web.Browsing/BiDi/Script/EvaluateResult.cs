using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Base class for the result of a script evaluation.
/// </summary>
[JsonConverter(typeof(ScriptEvaluateResultJsonConverter))]
public class EvaluateResult : CommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluateResult"/> class.
    /// </summary>
    protected EvaluateResult() { }

    /// <summary>
    /// Gets the type of the result of the script execution.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonRequired]
    [JsonInclude]
    public EvaluateResultType ResultType { get; internal set; } = EvaluateResultType.Success;

    /// <summary>
    /// Gets the ID of the realm in which the script was executed.
    /// </summary>
    [JsonPropertyName("realm")]
    [JsonRequired]
    [JsonInclude]
    public string RealmId { get; internal set; } = string.Empty;
}