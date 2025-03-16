using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Value of the ownership model of values returned from script execution.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ResultOwnership>))]
public enum ResultOwnership
{
    /// <summary>
    /// Use no ownership model.
    /// </summary>
    None,

    /// <summary>
    /// Values are owned by root.
    /// </summary>
    Root,
}