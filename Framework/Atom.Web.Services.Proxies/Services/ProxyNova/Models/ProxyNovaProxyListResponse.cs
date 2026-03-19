using System.Text.Json.Serialization;

namespace Atom.Web.Proxies.Services;

internal sealed class ProxyNovaProxyListResponse
{
    [JsonPropertyName("data")]
    public ProxyNovaProxyListEntry[]? Data { get; init; }
}