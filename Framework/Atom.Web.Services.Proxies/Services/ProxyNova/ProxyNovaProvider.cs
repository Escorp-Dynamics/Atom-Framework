using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atom.Net.Proxies;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных HTTP proxy через публичный API ProxyNova.
/// </summary>
public sealed partial class ProxyNovaProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint ProxyNova proxylist API.
    /// </summary>
    public const string DefaultEndpoint = "https://api.proxynova.com/proxylist";

    /// <summary>
    /// Размер выборки по умолчанию, если limit не указан или невалиден.
    /// </summary>
    public const int DefaultLimit = 100;

    /// <summary>
    /// Максимально поддерживаемый размер выборки ProxyNova API.
    /// </summary>
    public const int MaximumLimit = 1000;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;

    /// <summary>
    /// Endpoint, из которого загружается список proxy.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер ProxyNova.
    /// </summary>
    public ProxyNovaProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
    }

    /// <summary>
    /// Создаёт провайдер ProxyNova из явной конфигурации запроса.
    /// </summary>
    public ProxyNovaProvider(ProxyNovaProviderOptions options, HttpClient? httpClient = null)
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
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(Endpoint));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Atom.ProxyNovaProvider/1.0)");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResponse(payload, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Преобразует ответ ProxyNova в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    /// <param name="payload">JSON-ответ ProxyNova.</param>
    public static IEnumerable<ServiceProxy> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return IsJsonPayload(payload)
            ? ParseJson(payload)
            : [];
    }

    /// <summary>
    /// Создаёт нормализованный ProxyNova endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(ProxyNovaProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProviderEndpointBuilder.Create(DefaultEndpoint, CreateQuery(options));
    }

    private static IEnumerable<ServiceProxy> ParseResponse(string payload, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        if (IsJsonContentType(mediaType))
        {
            return ParseJson(payload);
        }

        if (IsJsonPayload(payload))
        {
            return ParseJson(payload);
        }

        throw new FormatException($"ProxyNova returned unsupported content type '{mediaType ?? "unknown"}'. Payload: {payload.Trim()}");
    }

    private static IEnumerable<ServiceProxy> ParseJson(string payload)
    {
        var response = JsonSerializer.Deserialize(payload, ProxyNovaJsonSerializerContext.Default.ProxyNovaProxyListResponse);
        if (response?.Data is not { Length: > 0 } items)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var proxies = new List<ServiceProxy>(items.Length);
        foreach (var item in items)
        {
            var proxy = CreateProxy(item, now);
            if (proxy is null)
            {
                continue;
            }

            proxies.Add(proxy);
        }

        return proxies;
    }

    private static ServiceProxy? CreateProxy(ProxyNovaProxyListEntry item, DateTime now)
    {
        var host = ParseHost(item.Ip);
        if (string.IsNullOrWhiteSpace(host) || !item.Port.HasValue || item.Port.Value <= 0)
        {
            return null;
        }

        return new ServiceProxy
        {
            Provider = nameof(ProxyNovaProvider),
            Host = host,
            Port = item.Port.Value,
            Type = ProxyType.Http,
            Anonymity = AnonymityLevel.Low,
            Geolocation = CreateGeolocation(item),
            Alive = CreateAlive(item, now),
            Uptime = item.Uptime ?? 0,
        };
    }

    private static string? ParseHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var direct = ExtractIpAddress(value);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        try
        {
            return ExtractIpAddress(new ProxyNovaIpExpressionParser(value).Parse());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Geolocation? CreateGeolocation(ProxyNovaProxyListEntry item)
    {
        var hasCoordinates = item.Latitude.HasValue && item.Longitude.HasValue;
        var geolocation = new Geolocation
        {
            City = item.CityName,
            Latitude = item.Latitude ?? default,
            Longitude = item.Longitude ?? default,
        };

        geolocation.Country = ResolveCountry(item.CountryCode)
            ?? ResolveCountry(item.CountryName);

        if (geolocation.Country is null
            && string.IsNullOrWhiteSpace(geolocation.City)
            && !hasCoordinates)
        {
            return null;
        }

        return geolocation;
    }

    private static Country? ResolveCountry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Country.TryParse(value, CultureInfo.InvariantCulture, out var country)
            ? country
            : null;
    }

    private static DateTime CreateAlive(ProxyNovaProxyListEntry item, DateTime now)
    {
        if (!item.AliveSecondsAgo.HasValue
            || double.IsNaN(item.AliveSecondsAgo.Value)
            || double.IsInfinity(item.AliveSecondsAgo.Value)
            || item.AliveSecondsAgo.Value < 0)
        {
            return default;
        }

        return now - TimeSpan.FromSeconds(item.AliveSecondsAgo.Value);
    }

    private static Uri BuildRequestUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("ProxyNova endpoint must be an absolute URI.", nameof(endpoint));
        }

        if (!IsProxyNovaApiEndpoint(uri))
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Query = ProviderEndpointBuilder.CreateQueryString(NormalizeQuery(ParseQuery(uri.Query))),
        };

        return builder.Uri;
    }

    private static bool IsProxyNovaApiEndpoint(Uri uri)
        => string.Equals(uri.Host, "api.proxynova.com", StringComparison.OrdinalIgnoreCase)
           && string.Equals(uri.AbsolutePath.TrimEnd('/'), "/proxylist", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> CreateQuery(ProxyNovaProviderOptions options)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["limit"] = NormalizeLimit(options.Limit.ToString(CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),
        };

        AddLocationFilter(query, options);
        return query;
    }

    private static void AddLocationFilter(Dictionary<string, string> query, ProxyNovaProviderOptions options)
    {
        if (options.Near.HasValue)
        {
            query["near"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{options.Near.Value.Latitude},{options.Near.Value.Longitude}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.Country))
        {
            query["country"] = ProviderEndpointBuilder.UpperOrDefault(options.Country, "ALL");
        }
    }

    private static Dictionary<string, string> NormalizeQuery(Dictionary<string, string> query)
    {
        query["limit"] = NormalizeLimit(query.TryGetValue("limit", out var limitValue) ? limitValue : null).ToString(CultureInfo.InvariantCulture);

        if (query.ContainsKey("near"))
        {
            query.Remove("country");
        }

        return query;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmedQuery = query.AsSpan();
        if (!trimmedQuery.IsEmpty && trimmedQuery[0] == '?')
        {
            trimmedQuery = trimmedQuery[1..];
        }

        var startIndex = 0;
        while (startIndex < trimmedQuery.Length)
        {
            var endIndex = trimmedQuery[startIndex..].IndexOf('&');
            ReadOnlySpan<char> segment;
            if (endIndex < 0)
            {
                segment = trimmedQuery[startIndex..];
                startIndex = trimmedQuery.Length;
            }
            else
            {
                segment = trimmedQuery.Slice(startIndex, endIndex);
                startIndex += endIndex + 1;
            }

            if (segment.IsEmpty)
            {
                continue;
            }

            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                result[Uri.UnescapeDataString(segment.ToString())] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(segment[..separatorIndex].ToString());
            var value = Uri.UnescapeDataString(segment[(separatorIndex + 1)..].ToString());
            result[key] = value;
        }

        return result;
    }

    private static int NormalizeLimit(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaximumLimit);
    }

    private static bool IsJsonContentType(string? mediaType)
        => !string.IsNullOrWhiteSpace(mediaType)
           && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonPayload(string payload)
    {
        var trimmed = payload.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] == '{';
    }

    private static string? ExtractIpAddress(string value)
    {
        var match = IpRegex().Match(value);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IpRegex();
}