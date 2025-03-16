using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Enumerated values for the sameSite property of a cookie.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<CookieSameSiteValue>))]
public enum CookieSameSiteValue
{
    /// <summary>
    /// The cookie adheres to the Strict same-site policy.
    /// </summary>
    Strict,

    /// <summary>
    /// The cookie adheres to the Lax same-site policy.
    /// </summary>
    Lax,

    /// <summary>
    /// The cookie adheres to no same-site policy.
    /// </summary>
    None,
}