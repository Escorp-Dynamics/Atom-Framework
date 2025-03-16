using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// The result type of a script evaluation.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<EvaluateResultType>))]
public enum EvaluateResultType
{
    /// <summary>
    /// The script evaluation was successful.
    /// </summary>
    Success,

    /// <summary>
    /// The script evaluation threw an exception.
    /// </summary>
    Exception,
}