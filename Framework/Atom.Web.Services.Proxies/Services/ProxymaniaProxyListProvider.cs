using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через HTML-таблицу ProxyMania.
/// </summary>
public sealed partial class ProxymaniaProxyListProvider : NetworkProxyProvider, IProxyPagedProvider
{
    /// <summary>
    /// Базовый endpoint ProxyMania free proxy list.
    /// </summary>
    public const string DefaultEndpoint = "https://proxymania.su/en/free-proxy";

    /// <summary>
    /// Консервативный лимит старта запросов в секунду для HTML-источника ProxyMania.
    /// </summary>
    public const int DefaultRequestsPerSecondLimit = 1;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly bool fetchAllPages;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Указывает, должен ли провайдер пройти все доступные страницы ProxyMania.
    /// </summary>
    public bool FetchAllPages => fetchAllPages;

    /// <summary>
    /// Создаёт провайдер ProxyMania.
    /// </summary>
    public ProxymaniaProxyListProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
    }

    /// <summary>
    /// Создаёт провайдер ProxyMania из явной конфигурации endpoint.
    /// </summary>
    public ProxymaniaProxyListProvider(ProxymaniaProxyListProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : this(CreateEndpoint(options), httpClient, logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        fetchAllPages = options.FetchAllPages;
        RequestsPerSecondLimit = Math.Max(1, options.RequestsPerSecondLimit);
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
        return Parse(payload);
    }

    /// <inheritdoc/>
    public async ValueTask<ProxyProviderFetchPage> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrWhiteSpace(continuationToken) ? Endpoint : continuationToken;
        var payload = await LoadPayloadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var proxies = Parse(payload).ToArray();
        var nextEndpoint = fetchAllPages ? ExtractNextPageEndpoint(payload, endpoint) : null;
        return new ProxyProviderFetchPage(proxies, nextEndpoint);
    }

    /// <summary>
    /// Преобразует HTML-ответ ProxyMania в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    public static IEnumerable<ServiceProxy> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var bodyMatch = ResultTableRegex().Match(payload);
        if (!bodyMatch.Success)
        {
            return [];
        }

        var proxies = new List<ServiceProxy>();
        var body = bodyMatch.Groups["body"].Value;
        var rowMatches = RowRegex().Matches(body);
        for (var rowIndex = 0; rowIndex < rowMatches.Count; rowIndex++)
        {
            var cellMatches = CellRegex().Matches(rowMatches[rowIndex].Groups["row"].Value);
            if (cellMatches.Count < 6)
            {
                continue;
            }

            var proxyCell = cellMatches[0].Groups["cell"].Value;
            var countryCell = cellMatches[1].Groups["cell"].Value;
            var typeCell = cellMatches[2].Groups["cell"].Value;
            var anonymityCell = cellMatches[3].Groups["cell"].Value;
            var lastCheckedCell = cellMatches[5].Value;

            if (!TryParseHostPort(NormalizeCellText(proxyCell), out var host, out var port)
                || !TryParseProxyType(NormalizeCellText(typeCell), out var proxyType))
            {
                continue;
            }

            proxies.Add(new ServiceProxy
            {
                Provider = nameof(ProxymaniaProxyListProvider),
                Host = host,
                Port = port,
                Type = proxyType,
                Anonymity = ParseAnonymity(NormalizeCellText(anonymityCell)),
                Geolocation = CreateGeolocation(countryCell),
                Alive = ParseAlive(lastCheckedCell),
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт ProxyMania endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(ProxymaniaProxyListProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = new Dictionary<string, string>(capacity: 4, comparer: StringComparer.OrdinalIgnoreCase);
        var protocol = NormalizeProtocol(options.Protocol);
        if (!string.Equals(protocol, "all", StringComparison.Ordinal))
        {
            query["type"] = protocol.ToUpperInvariant();
        }

        var country = ProviderEndpointBuilder.UpperOrDefault(options.Country, string.Empty);
        if (!string.IsNullOrWhiteSpace(country))
        {
            query["country"] = country;
        }

        if (options.MaximumSpeedMilliseconds > 0)
        {
            query["speed"] = options.MaximumSpeedMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        if (options.Page > 1)
        {
            query["page"] = options.Page.ToString(CultureInfo.InvariantCulture);
        }

        return ProviderEndpointBuilder.Create(DefaultEndpoint, query);
    }

    private async Task<string> LoadPayloadAsync(string endpoint, CancellationToken cancellationToken)
    {
        return await RunRateLimitedAsync(async token =>
        {
            using var response = await httpClient.GetAsync(endpoint, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? ExtractNextPageEndpoint(string payload, string currentEndpoint)
    {
        var match = NextPageRegex().Match(payload);
        if (!match.Success)
        {
            return null;
        }

        var href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsoluteUri;
        }

        return Uri.TryCreate(new Uri(currentEndpoint, UriKind.Absolute), href, out var relative)
            ? relative.AbsoluteUri
            : null;
    }

    private static Geolocation? CreateGeolocation(string countryCell)
    {
        var flagMatch = FlagCountryRegex().Match(countryCell);
        if (!flagMatch.Success)
        {
            return null;
        }

        var countryCode = flagMatch.Groups["country"].Value;
        if (!Country.TryParse(countryCode, CultureInfo.InvariantCulture, out var country) || country is null)
        {
            return null;
        }

        return new Geolocation
        {
            Country = country,
        };
    }

    private static DateTime ParseAlive(string lastCheckedCell)
    {
        var match = TimestampRegex().Match(lastCheckedCell);
        if (!match.Success
            || !long.TryParse(match.Groups["timestamp"].Value, CultureInfo.InvariantCulture, out var timestamp))
        {
            return default;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }
    }

    private static AnonymityLevel ParseAnonymity(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "transparent" => AnonymityLevel.Transparent,
            "medium" => AnonymityLevel.Medium,
            "high" => AnonymityLevel.High,
            _ => AnonymityLevel.Low,
        };

    private static bool TryParseProxyType(string? value, out ProxyType proxyType)
    {
        switch (NormalizeProtocol(value))
        {
            case "http":
                proxyType = ProxyType.Http;
                return true;
            case "https":
                proxyType = ProxyType.Https;
                return true;
            case "socks4":
                proxyType = ProxyType.Socks4;
                return true;
            case "socks5":
                proxyType = ProxyType.Socks5;
                return true;
            default:
                proxyType = default;
                return false;
        }
    }

    private static bool TryParseHostPort(string? value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        string portPart;
        if (candidate[0] == '[')
        {
            var bracketIndex = candidate.IndexOf(']');
            if (bracketIndex <= 0 || bracketIndex >= candidate.Length - 2 || candidate[bracketIndex + 1] != ':')
            {
                return false;
            }

            host = candidate[..(bracketIndex + 1)];
            portPart = candidate[(bracketIndex + 2)..];
        }
        else
        {
            var separatorIndex = candidate.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= candidate.Length - 1)
            {
                return false;
            }

            host = candidate[..separatorIndex].Trim();
            portPart = candidate[(separatorIndex + 1)..];
        }

        return !string.IsNullOrWhiteSpace(host)
               && int.TryParse(portPart, CultureInfo.InvariantCulture, out port)
               && port > 0
               && port <= ushort.MaxValue;
    }

    private static string NormalizeCellText(string value)
    {
        var decoded = WebUtility.HtmlDecode(StripTagsRegex().Replace(value, " "));
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string NormalizeProtocol(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "http" => "http",
            "https" => "https",
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "all",
        };

    [GeneratedRegex("<tbody\\b[^>]*id=[\"']resultTable[\"'][^>]*>(?<body>.*?)</tbody>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ResultTableRegex();

    [GeneratedRegex("<tr\\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<td\\b[^>]*>(?<cell>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CellRegex();

    [GeneratedRegex("<a\\b[^>]*href=(?<quote>[\"'])(?<href>.*?)(?:\\k<quote>)[^>]*>\\s*Next\\s*</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NextPageRegex();

    [GeneratedRegex("/img/flags/(?<country>[a-z]{2})\\.svg", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FlagCountryRegex();

    [GeneratedRegex("data-timestamp=(?<quote>[\"'])(?<timestamp>\\d+)(?:\\k<quote>)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StripTagsRegex();

    [GeneratedRegex("\\s+", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}