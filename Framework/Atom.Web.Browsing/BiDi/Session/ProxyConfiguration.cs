using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Atom.Collections;
using Atom.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a proxy to be used by the browser.
/// </summary>
[JsonConverter(typeof(ProxyConfigurationJsonConverter))]
public class ProxyConfiguration
{
    /// <summary>
    /// Represents a proxy value that is unset in the remote end.
    /// TODO (Issue #19): Remove this static property once https://bugzilla.mozilla.org/show_bug.cgi?id=1916463 is fixed.
    /// </summary>
    public static readonly ProxyConfiguration UnsetProxy = new(ProxyType.Unset, JsonContext.Default.ProxyConfiguration);

    internal JsonTypeInfo TypeInfo { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyConfiguration"/> class.
    /// </summary>
    /// <param name="proxyType">The type of proxy configuration.</param>
    /// <param name="typeInfo">Информация о типе конфигурации прокси.</param>
    protected ProxyConfiguration(ProxyType proxyType, JsonTypeInfo typeInfo)
    {
        ProxyType = proxyType;
        TypeInfo = typeInfo;
    }

    /// <summary>
    /// Gets the type of proxy configuration.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ProxyType ProxyType { get; internal set; }

    /// <summary>
    /// Gets read-only dictionary of additional properties deserialized with this message.
    /// </summary>
    [JsonExtensionData]
    [JsonInclude]
    public IDictionary<string, object?> AdditionalData { get; } = new Dictionary<string, object?>();
}