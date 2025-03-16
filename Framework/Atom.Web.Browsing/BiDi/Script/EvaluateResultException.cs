#pragma warning disable CA1711
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing the evaluation of a script that throws an exception.
/// </summary>
public class EvaluateResultException : EvaluateResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluateResultException"/> class.
    /// </summary>
    [JsonConstructor]
    internal EvaluateResultException() : base() { }

    /// <summary>
    /// Gets the exception details of the script evaluation.
    /// </summary>
    [JsonPropertyName("exceptionDetails")]
    [JsonInclude]
    public ExceptionDetails ExceptionDetails { get; internal set; } = new();
}