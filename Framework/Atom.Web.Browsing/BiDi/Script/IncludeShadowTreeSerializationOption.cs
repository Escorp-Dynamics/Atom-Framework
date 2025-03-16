using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Values for the serialization of shadow trees when serializing nodes.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<IncludeShadowTreeSerializationOption>))]
public enum IncludeShadowTreeSerializationOption
{
    /// <summary>
    /// Do not include shadow trees when serializing nodes.
    /// </summary>
    None,

    /// <summary>
    /// Only include open shadow tress when serializing nodes.
    /// </summary>
    Open,

    /// <summary>
    /// Include all shadow trees, open or closed, when serializing nodes.
    /// </summary>
    All,
}