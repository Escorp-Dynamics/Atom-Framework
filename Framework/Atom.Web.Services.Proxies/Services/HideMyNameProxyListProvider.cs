using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через HTML-таблицу hide-my-name.app.
/// </summary>
public sealed partial class HideMyNameProxyListProvider : NetworkProxyProvider, IProxyPagedProvider
{
    /// <summary>
    /// Базовый endpoint hide-my-name proxy list.
    /// </summary>
    public const string DefaultEndpoint = "https://hide-my-name.app/proxy-list/";

    /// <summary>
    /// Консервативный лимит старта запросов в секунду для HTML-источника hide-my-name.
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
    /// Указывает, должен ли провайдер пройти все доступные страницы hide-my-name.
    /// </summary>
    public bool FetchAllPages => fetchAllPages;

    /// <summary>
    /// Создаёт провайдер hide-my-name.
    /// </summary>
    public HideMyNameProxyListProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
    }

    /// <summary>
    /// Создаёт провайдер hide-my-name из явной конфигурации endpoint.
    /// </summary>
    public HideMyNameProxyListProvider(HideMyNameProxyListProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
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
    /// Преобразует HTML-ответ hide-my-name в нормализованный набор <see cref="ServiceProxy"/>.
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

        var now = DateTime.UtcNow;
        var proxies = new List<ServiceProxy>();
        var rowMatches = RowRegex().Matches(bodyMatch.Groups["body"].Value);
        for (var rowIndex = 0; rowIndex < rowMatches.Count; rowIndex++)
        {
            var cellMatches = CellRegex().Matches(rowMatches[rowIndex].Groups["row"].Value);
            if (cellMatches.Count < 7)
            {
                continue;
            }

            var hostCell = cellMatches[0].Groups["cell"].Value;
            var portCell = cellMatches[1].Groups["cell"].Value;
            var locationCell = cellMatches[2].Groups["cell"].Value;
            var typeCell = cellMatches[4].Groups["cell"].Value;
            var anonymityCell = cellMatches[5].Groups["cell"].Value;
            var lastCheckedCell = cellMatches[6].Groups["cell"].Value;

            var host = NormalizeCellText(hostCell);
            if (!int.TryParse(NormalizeCellText(portCell), CultureInfo.InvariantCulture, out var port)
                || string.IsNullOrWhiteSpace(host)
                || port <= 0
                || port > ushort.MaxValue)
            {
                continue;
            }

            var protocolName = ParseProtocolName(typeCell);
            if (!TryParseProxyType(protocolName, out var proxyType))
            {
                continue;
            }

            var anonymity = ParseAnonymity(NormalizeCellText(anonymityCell));
            var geolocation = CreateGeolocation(locationCell);
            var alive = ParseAlive(NormalizeCellText(lastCheckedCell), now);

            proxies.Add(new ServiceProxy
            {
                Provider = nameof(HideMyNameProxyListProvider),
                Host = host,
                Port = port,
                Type = proxyType,
                Anonymity = anonymity,
                Geolocation = geolocation,
                Alive = alive,
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт hide-my-name endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(HideMyNameProxyListProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = new Dictionary<string, string>(capacity: 5, comparer: StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.CountryFilter))
        {
            query["country"] = ProviderEndpointBuilder.UpperOrDefault(options.CountryFilter, string.Empty);
        }

        if (options.MaximumSpeedMilliseconds > 0)
        {
            query["maxtime"] = options.MaximumSpeedMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(options.TypeFilter))
        {
            query["type"] = ProviderEndpointBuilder.LowerOrDefault(options.TypeFilter, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(options.AnonymityFilter))
        {
            query["anon"] = ProviderEndpointBuilder.PreserveOrDefault(options.AnonymityFilter, string.Empty);
        }

        if (options.Start > 0)
        {
            query["start"] = options.Start.ToString(CultureInfo.InvariantCulture);
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
        var currentStart = ExtractStartValue(currentEndpoint);
        var matches = StartValueRegex().Matches(payload);
        var nextStart = int.MaxValue;
        for (var index = 0; index < matches.Count; index++)
        {
            if (!int.TryParse(matches[index].Groups["start"].Value, CultureInfo.InvariantCulture, out var start))
            {
                continue;
            }

            if (start > currentStart && start < nextStart)
            {
                nextStart = start;
            }
        }

        return nextStart == int.MaxValue ? null : CreateContinuationEndpoint(currentEndpoint, nextStart);
    }

    private static string CreateContinuationEndpoint(string currentEndpoint, int start)
    {
        var uri = new Uri(currentEndpoint, UriKind.Absolute);
        var query = ParseQuery(uri.Query);
        query["start"] = start.ToString(CultureInfo.InvariantCulture);
        return ProviderEndpointBuilder.Create(uri.GetLeftPart(UriPartial.Path), query);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return result;
        }

        var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            var separatorIndex = parts[index].IndexOf('=');
            if (separatorIndex < 0)
            {
                result[WebUtility.UrlDecode(parts[index])] = string.Empty;
                continue;
            }

            var key = WebUtility.UrlDecode(parts[index][..separatorIndex]);
            var value = WebUtility.UrlDecode(parts[index][(separatorIndex + 1)..]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static int ExtractStartValue(string endpoint)
    {
        var match = StartValueRegex().Match(endpoint);
        return match.Success && int.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, out var start)
            ? start
            : 0;
    }

    private static string ParseProtocolName(string typeCell)
    {
        var value = NormalizeCellText(typeCell);
        if (string.IsNullOrWhiteSpace(value))
        {
            return "all";
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            var normalized = NormalizeProtocol(parts[index]);
            if (string.Equals(normalized, "socks5", StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        for (var index = 0; index < parts.Length; index++)
        {
            var normalized = NormalizeProtocol(parts[index]);
            if (string.Equals(normalized, "socks4", StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        for (var index = 0; index < parts.Length; index++)
        {
            var normalized = NormalizeProtocol(parts[index]);
            if (string.Equals(normalized, "https", StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        for (var index = 0; index < parts.Length; index++)
        {
            var normalized = NormalizeProtocol(parts[index]);
            if (string.Equals(normalized, "http", StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        return "all";
    }

    private static Geolocation? CreateGeolocation(string locationCell)
    {
        Country? country = null;
        var countryMatch = FlagCountryRegex().Match(locationCell);
        if (countryMatch.Success)
        {
            var countryCode = countryMatch.Groups["country"].Value;
            if (Country.TryParse(countryCode, CultureInfo.InvariantCulture, out var parsedCountry))
            {
                country = parsedCountry;
            }
        }

        var cityMatch = CityRegex().Match(locationCell);
        var city = cityMatch.Success ? NormalizeCellText(cityMatch.Groups["city"].Value) : string.Empty;

        if (country is null && string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        return new Geolocation
        {
            Country = country,
            City = string.IsNullOrWhiteSpace(city) ? null : city,
        };
    }

    private static DateTime ParseAlive(string lastChecked, DateTime now)
    {
        var match = RelativeTimeRegex().Match(lastChecked);
        if (!match.Success
            || !int.TryParse(match.Groups["value"].Value, CultureInfo.InvariantCulture, out var value))
        {
            return default;
        }

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var delta = unit switch
        {
            "секунда" or "секунды" or "секунд" => TimeSpan.FromSeconds(value),
            "минута" or "минуты" or "минут" => TimeSpan.FromMinutes(value),
            "час" or "часа" or "часов" => TimeSpan.FromHours(value),
            "день" or "дня" or "дней" => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero,
        };

        return delta == TimeSpan.Zero ? default : now - delta;
    }

    private static AnonymityLevel ParseAnonymity(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "нет" => AnonymityLevel.Transparent,
            "средняя" => AnonymityLevel.Medium,
            "высокая" => AnonymityLevel.High,
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

    private static string NormalizeCellText(string value)
    {
        var decoded = WebUtility.HtmlDecode(StripTagsRegex().Replace(value, " "));
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string NormalizeProtocol(string? value)
        => value?.Trim().ToLowerInvariant().Replace(" ", string.Empty) switch
        {
            "http" => "http",
            "https" => "https",
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "all",
        };

    [GeneratedRegex("<tbody\\b[^>]*>(?<body>.*?)</tbody>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ResultTableRegex();

    [GeneratedRegex("<tr\\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<td\\b[^>]*>(?<cell>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CellRegex();

    [GeneratedRegex("[?&]start=(?<start>\\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StartValueRegex();

    [GeneratedRegex("flag-icon-(?<country>[a-z]{2})", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FlagCountryRegex();

    [GeneratedRegex("<span\\b[^>]*class=\"city\"[^>]*>(?<city>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CityRegex();

    [GeneratedRegex("(?<value>\\d+)\\s*(?<unit>секунда|секунды|секунд|минута|минуты|минут|час|часа|часов|день|дня|дней)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RelativeTimeRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StripTagsRegex();

    [GeneratedRegex("\\s+", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}