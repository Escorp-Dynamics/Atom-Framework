using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.disown command.
/// </summary>
public class DisownCommandParameters : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisownCommandParameters"/> class.
    /// </summary>
    /// <param name="target">The script target containing handles to disown.</param>
    /// <param name="handleValues">The handles to disown.</param>
    public DisownCommandParameters(Target target, [NotNull] params string[] handleValues)
    {
        Target = target;
        foreach (var handleValue in handleValues) Handles.Add(handleValue);
    }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.disown";

    /// <summary>
    /// Gets or sets the target for which to disown handles.
    /// </summary>
    [JsonPropertyName("target")]
    public Target Target { get; set; }

    /// <summary>
    /// Gets or sets the list of handles to disown.
    /// </summary>
    [JsonPropertyName("handles")]
    public IList<string> Handles { get; } = [];
}