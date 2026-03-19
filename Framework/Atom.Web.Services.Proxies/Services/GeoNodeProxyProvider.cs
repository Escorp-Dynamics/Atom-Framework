using System.Globalization;
using System.Text.Json;
using Atom.Net.Proxies;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через публичный API GeoNode.
/// </summary>
public sealed class GeoNodeProxyProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint GeoNode proxy-list API.
    /// </summary>
    public const string DefaultEndpoint = "https://proxylist.geonode.com/api/proxy-list?limit=100&page=1&sort_by=lastChecked&sort_type=desc";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер GeoNode.
    /// </summary>
    public GeoNodeProxyProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
    }

    /// <summary>
    /// Создаёт провайдер GeoNode из явной конфигурации endpoint.
    /// </summary>
    public GeoNodeProxyProvider(GeoNodeProxyProviderOptions options, HttpClient? httpClient = null)
        : this(CreateEndpoint(options), httpClient)
    {
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && disposeHttpClient)
        {
            httpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(Endpoint, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(payload);
    }

    /// <summary>
    /// Преобразует ответ GeoNode API в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    /// <param name="payload">JSON-ответ GeoNode.</param>
    public static IEnumerable<ServiceProxy> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var items)
            || items.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var proxies = new List<ServiceProxy>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            var host = TryGetString(item, "ip");
            var port = TryParseInt32(item, "port");
            if (string.IsNullOrWhiteSpace(host) || !port.HasValue || port.Value <= 0)
            {
                continue;
            }

            var anonymity = ParseAnonymity(TryGetString(item, "anonymityLevel"));
            var geolocation = CreateGeolocation(item);
            var alive = TryParseDateTime(item, "lastChecked");
            var uptime = TryParseByte(item, "upTime") ?? 0;

            AddProxies(proxies, item, host, port.Value, anonymity, geolocation, alive, uptime);
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт GeoNode endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(GeoNodeProxyProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = new Dictionary<string, string>(capacity: 4, comparer: StringComparer.OrdinalIgnoreCase)
        {
            ["limit"] = ProviderEndpointBuilder.PositiveOrDefault(options.Limit, 100).ToString(CultureInfo.InvariantCulture),
            ["page"] = ProviderEndpointBuilder.PositiveOrDefault(options.Page, 1).ToString(CultureInfo.InvariantCulture),
            ["sort_by"] = ProviderEndpointBuilder.PreserveOrDefault(options.SortBy, "lastChecked"),
            ["sort_type"] = ProviderEndpointBuilder.LowerOrDefault(options.SortType, "desc"),
        };

        return ProviderEndpointBuilder.Create("https://proxylist.geonode.com/api/proxy-list", query);
    }

    private static Geolocation? CreateGeolocation(JsonElement item)
    {
        var countryCode = TryGetString(item, "country");
        var city = TryGetString(item, "city");

        var geolocation = new Geolocation
        {
            City = city,
        };

        if (!string.IsNullOrWhiteSpace(countryCode)
            && Country.TryParse(countryCode, CultureInfo.InvariantCulture, out var country)
            && country is not null)
        {
            geolocation.Country = country;
        }

        return geolocation.Country is null && string.IsNullOrWhiteSpace(geolocation.City)
            ? null
            : geolocation;
    }

    private static void AddProxies(
        List<ServiceProxy> proxies,
        JsonElement item,
        string host,
        int port,
        AnonymityLevel anonymity,
        Geolocation? geolocation,
        DateTime alive,
        byte uptime)
    {
        if (!item.TryGetProperty("protocols", out var protocolsProperty)
            || protocolsProperty.ValueKind is not JsonValueKind.Array)
        {
            proxies.Add(CreateProxy(host, port, "http", anonymity, geolocation, alive, uptime));
            return;
        }

        var hasProtocol = false;
        foreach (var protocolElement in protocolsProperty.EnumerateArray())
        {
            var protocol = TryGetString(protocolElement);
            if (string.IsNullOrWhiteSpace(protocol))
            {
                continue;
            }

            proxies.Add(CreateProxy(host, port, protocol, anonymity, geolocation, alive, uptime));
            hasProtocol = true;
        }

        if (!hasProtocol)
        {
            proxies.Add(CreateProxy(host, port, "http", anonymity, geolocation, alive, uptime));
        }
    }

    private static ServiceProxy CreateProxy(
        string host,
        int port,
        string protocol,
        AnonymityLevel anonymity,
        Geolocation? geolocation,
        DateTime alive,
        byte uptime)
        => new()
        {
            Provider = nameof(GeoNodeProxyProvider),
            Host = host,
            Port = port,
            Type = ParseProxyType(protocol),
            Anonymity = anonymity,
            Geolocation = geolocation,
            Alive = alive,
            Uptime = uptime,
        };

    private static ProxyType ParseProxyType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "https" => ProxyType.Https,
        "socks4" => ProxyType.Socks4,
        "socks5" => ProxyType.Socks5,
        _ => ProxyType.Http,
    };

    private static AnonymityLevel ParseAnonymity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "elite" => AnonymityLevel.High,
        "high_anonymous" => AnonymityLevel.High,
        "anonymous" => AnonymityLevel.Medium,
        "transparent" => AnonymityLevel.Transparent,
        _ => AnonymityLevel.Low,
    };

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) ? TryGetString(property) : null;

    private static string? TryGetString(JsonElement element)
        => element.ValueKind is JsonValueKind.String ? element.GetString() : element.ToString();

    private static int? TryParseInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    private static byte? TryParseByte(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetByte(out var value) => value,
            JsonValueKind.String when byte.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    private static DateTime TryParseDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not JsonValueKind.String
            || !DateTime.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
        {
            return default;
        }

        return value;
    }
}