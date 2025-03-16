using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.callFunction command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CallFunctionCommandParameters"/> class.
/// </remarks>
/// <param name="functionDeclaration">The function declaration.</param>
/// <param name="scriptTarget">The script target in which to call the function.</param>
/// <param name="awaitPromise"><see langword="true"/> to await the script evaluation as a Promise; otherwise, <see langword="false"/>.</param>
public class CallFunctionCommandParameters(string functionDeclaration, Target scriptTarget, bool awaitPromise) : CommandParameters<EvaluateResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.callFunction";

    /// <summary>
    /// Gets or sets the function declaration.
    /// </summary>
    public string FunctionDeclaration { get; set; } = functionDeclaration;

    /// <summary>
    /// Gets or sets the script target against which to call the function.
    /// </summary>
    [JsonPropertyName("target")]
    public Target ScriptTarget { get; set; } = scriptTarget;

    /// <summary>
    /// Gets or sets a value indicating whether to wait for the function execution to complete.
    /// </summary>
    [JsonPropertyName("awaitPromise")]
    public bool IsAwaitPromise { get; set; } = awaitPromise;

    /// <summary>
    /// Gets or sets the item to use as the 'this' object when the function is called.
    /// </summary>
    [JsonPropertyName("this")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArgumentValue? ThisObject { get; set; }

    /// <summary>
    /// Gets the list of arguments to pass to the function.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ArgumentValue> Arguments { get; set; } = [];

    /// <summary>
    /// Gets or sets the ownership model to use for objects in the function call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResultOwnership? ResultOwnership { get; set; }

    /// <summary>
    /// Gets or sets the serialization options for serializing results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SerializationOptions? SerializationOptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to activate the browsing context when calling the function. When omitted, is treated as if false.
    /// </summary>
    [JsonPropertyName("userActivation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsUserActivation { get; set; }

    /// <summary>
    /// Gets the list of arguments for serialization purposes.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal IList<ArgumentValue>? SerializableArguments => !Arguments.Any() ? null : [.. Arguments];
}