using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через публичный API GeoNode.
/// </summary>
public sealed class GeoNodeProxyProvider : NetworkProxyProvider, IProxyPagedProvider
{
    /// <summary>
    /// Базовый endpoint GeoNode proxy-list API.
    /// </summary>
    public const string DefaultEndpoint = "https://proxylist.geonode.com/api/proxy-list?limit=100&page=1&sort_by=lastChecked&sort_type=desc";

    /// <summary>
    /// Рабочий дефолтный лимит старта запросов в секунду для полного page-walk GeoNode.
    /// </summary>
    public const int DefaultRequestsPerSecondLimit = 4;

    /// <summary>
    /// Число повторов по умолчанию для retryable ответов GeoNode.
    /// </summary>
    public const int DefaultRetryAttempts = 2;

    /// <summary>
    /// Базовая задержка по умолчанию между retryable запросами GeoNode.
    /// </summary>
    public const int DefaultRetryDelayMilliseconds = 750;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly bool fetchAllPages;
    private readonly GeoNodeProxyProviderOptions? options;
    private readonly int retryAttempts;
    private readonly TimeSpan retryDelay;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Указывает, должен ли провайдер пройти все страницы GeoNode API.
    /// </summary>
    public bool FetchAllPages => fetchAllPages;

    /// <summary>
    /// Создаёт провайдер GeoNode.
    /// </summary>
    public GeoNodeProxyProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        fetchAllPages = false;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
        retryAttempts = DefaultRetryAttempts;
        retryDelay = TimeSpan.FromMilliseconds(DefaultRetryDelayMilliseconds);
    }

    /// <summary>
    /// Создаёт провайдер GeoNode из явной конфигурации endpoint.
    /// </summary>
    public GeoNodeProxyProvider(GeoNodeProxyProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : this(CreateEndpoint(options), httpClient, logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options;
        fetchAllPages = options.FetchAllPages;
        RequestsPerSecondLimit = Math.Max(1, options.RequestsPerSecondLimit);
        retryAttempts = Math.Max(0, options.RetryAttempts);
        retryDelay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryDelayMilliseconds));
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
        var payload = await LoadPayloadAsync(Endpoint, cancellationToken).ConfigureAwait(false);
        return Parse(payload).ToList();
    }

    /// <inheritdoc/>
    public async ValueTask<ProxyProviderFetchPage> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        var page = string.IsNullOrWhiteSpace(continuationToken)
            ? ProviderEndpointBuilder.PositiveOrDefault(options?.Page ?? 1, 1)
            : int.Parse(continuationToken, CultureInfo.InvariantCulture);

        var endpoint = page == ProviderEndpointBuilder.PositiveOrDefault(options?.Page ?? 1, 1)
            ? Endpoint
            : CreateEndpoint(new GeoNodeProxyProviderOptions
            {
                Limit = options?.Limit ?? 100,
                Page = page,
                SortBy = options?.SortBy ?? "lastChecked",
                SortType = options?.SortType ?? "desc",
            });

        var payload = await LoadPayloadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var proxies = Parse(payload).ToArray();
        if (!fetchAllPages || options is null)
        {
            return new ProxyProviderFetchPage(proxies);
        }

        var metadata = ParsePageMetadata(payload, options);
        var nextPage = metadata.Total > 0 && metadata.PageCount > metadata.Page
            ? (metadata.Page + 1).ToString(CultureInfo.InvariantCulture)
            : null;

        return new ProxyProviderFetchPage(proxies, nextPage);
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

    private async Task<string> LoadPayloadAsync(string endpoint, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await RunRateLimitedAsync(async token =>
            {
                return await httpClient.GetAsync(endpoint, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            if (attempt >= retryAttempts || !IsRetryableStatusCode(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
            }

            await Task.Delay(GetRetryDelay(response.Headers.RetryAfter, attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        var exponentialDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * Math.Pow(2, attempt));
        if (retryAfter is null)
        {
            return exponentialDelay;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > exponentialDelay ? delta : exponentialDelay;
        }

        if (retryAfter.Date is { } date)
        {
            var dateDelay = date - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay > exponentialDelay ? dateDelay : exponentialDelay;
            }
        }

        return exponentialDelay;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
           || statusCode == HttpStatusCode.RequestTimeout
           || statusCode == HttpStatusCode.BadGateway
           || statusCode == HttpStatusCode.ServiceUnavailable
           || statusCode == HttpStatusCode.GatewayTimeout;

    private static GeoNodePageMetadata ParsePageMetadata(string payload, GeoNodeProxyProviderOptions options)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var limit = TryParseInt32(root, "limit") ?? ProviderEndpointBuilder.PositiveOrDefault(options.Limit, 100);
        var page = TryParseInt32(root, "page") ?? ProviderEndpointBuilder.PositiveOrDefault(options.Page, 1);
        var total = TryParseInt32(root, "total") ?? 0;

        return new(total, page, limit);
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

    private readonly record struct GeoNodePageMetadata(int Total, int Page, int Limit)
    {
        public int PageCount => Limit <= 0 ? 1 : (int)Math.Ceiling((double)Total / Limit);
    }
}