using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The enumerated value of types for a BytesValue.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<CacheBehavior>))]
public enum CacheBehavior
{
    /// <summary>
    /// The browser uses the default cache behavior.
    /// </summary>
    Default,

    /// <summary>
    /// The browser bypasses the cache.
    /// </summary>
    Bypass,
}