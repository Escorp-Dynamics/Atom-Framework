using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.evaluate command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EvaluateCommandParameters"/> class.
/// </remarks>
/// <param name="expression">The expression to evaluate.</param>
/// <param name="scriptTarget">The target of the script to evaluate against.</param>
/// <param name="awaitPromise"><see langword="true"/> to await the script evaluation as a Promise; otherwise, <see langword="false"/>.</param>
public class EvaluateCommandParameters(string expression, Target scriptTarget, bool awaitPromise) : CommandParameters<EvaluateResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.evaluate";

    /// <summary>
    /// Gets or sets the expression to evaluate.
    /// </summary>
    [JsonPropertyName("expression")]
    public string Expression { get; set; } = expression;

    /// <summary>
    /// Gets or sets the target against which to evaluate the script.
    /// </summary>
    [JsonPropertyName("target")]
    public Target ScriptTarget { get; set; } = scriptTarget;

    /// <summary>
    /// Gets or sets a value indicating whether to await the completion of the evaluation of the script.
    /// </summary>
    [JsonPropertyName("awaitPromise")]
    public bool AwaitPromise { get; set; } = awaitPromise;

    /// <summary>
    /// Gets or sets the value of the model of ownership of the handles of the values in the script.
    /// </summary>
    [JsonPropertyName("resultOwnership")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public ResultOwnership? ResultOwnership { get; set; }

    /// <summary>
    /// Gets or sets the serialization options for serializing results.
    /// </summary>
    [JsonPropertyName("serializationOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public SerializationOptions? SerializationOptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to activate the browsing context when evaluating the script. When omitted, is treated as if false.
    /// </summary>
    [JsonPropertyName("userActivation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public bool? UserActivation { get; set; }
}