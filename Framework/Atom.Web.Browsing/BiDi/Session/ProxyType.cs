using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// The type of proxy.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ProxyType>))]
public enum ProxyType
{
    /// <summary>
    /// No proxy value has been set.
    /// TODO (Issue #19): Remove this enum value once https://bugzilla.mozilla.org/show_bug.cgi?id=1916463 is fixed.
    /// </summary>
    Unset,

    /// <summary>
    /// Direct connection with no proxy.
    /// </summary>
    Direct,

    /// <summary>
    /// Use the proxy registered in the system.
    /// </summary>
    System,

    /// <summary>
    /// Use a manually configured proxy.
    /// </summary>
    Manual,

    /// <summary>
    /// Automatically detect the type of proxy to use.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Use a proxy autoconfig (PAC) file.
    /// </summary>
    [JsonEnumValue("pac")]
    ProxyAutoConfig,
}