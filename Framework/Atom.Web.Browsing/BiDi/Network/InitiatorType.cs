using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Enumerated values for network traffic initiators.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<InitiatorType>))]
public enum InitiatorType
{
    /// <summary>
    /// Network traffic initiated by the HTML parser.
    /// </summary>
    Parser,

    /// <summary>
    /// Network traffic initiated by the browser JavaScript engine.
    /// </summary>
    Script,

    /// <summary>
    /// Network traffic initiated by a CORS preflight request.
    /// </summary>
    Preflight,

    /// <summary>
    /// Network traffic initiated by the browser.
    /// </summary>
    Other,
}