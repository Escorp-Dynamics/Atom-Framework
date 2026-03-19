using System.Text.Json.Serialization;

namespace Atom.Web.Proxies.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(ProxyNovaProxyListResponse))]
internal sealed partial class ProxyNovaJsonSerializerContext : JsonSerializerContext;