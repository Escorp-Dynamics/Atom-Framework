using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.addPreloadScript command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AddPreloadScriptCommandParameters"/> class.
/// </remarks>
/// <param name="functionDeclaration">The function declaration defining the preload script.</param>
public class AddPreloadScriptCommandParameters(string functionDeclaration) : CommandParameters<AddPreloadScriptCommandResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.addPreloadScript";

    /// <summary>
    /// Gets or sets the function declaration defining the preload script.
    /// </summary>
    public string FunctionDeclaration { get; set; } = functionDeclaration;

    /// <summary>
    /// Gets or sets the arguments for the function declaration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ChannelValue>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets the browsing contexts for which to add the preload script.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Contexts { get; set; }

    /// <summary>
    /// Gets or sets the user contexts for which to add the preload script.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? UserContexts { get; set; }

    /// <summary>
    /// Gets or sets the sandbox name of the preload script.
    /// </summary>
    [JsonPropertyName("sandbox")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sandbox { get; set; }
}