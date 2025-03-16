#pragma warning disable CA1720
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The enumerated value of types for a UrlPattern.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<UrlPatternType>))]
public enum UrlPatternType
{
    /// <summary>
    /// The UrlPattern is defined by a string.
    /// </summary>
    String,

    /// <summary>
    /// The UrlPattern is defined by a pattern.
    /// </summary>
    Pattern,
}