using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Represents the mode of the shadow root for a node.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ShadowRootMode>))]
public enum ShadowRootMode
{
    /// <summary>
    /// Shadow root is open.
    /// </summary>
    Open,

    /// <summary>
    /// Shadow root is closed.
    /// </summary>
    Closed,
}