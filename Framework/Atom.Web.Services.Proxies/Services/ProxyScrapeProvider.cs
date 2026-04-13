using System.Globalization;
using System.IO;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через публичный plain-text endpoint ProxyScrape.
/// </summary>
public sealed class ProxyScrapeProvider : NetworkProxyProvider, IProxyTargetedProvider
{
    /// <summary>
    /// Базовый endpoint ProxyScrape для бесплатных HTTP proxy.
    /// </summary>
    public const string DefaultEndpoint = "https://api.proxyscrape.com/v2/?request=getproxies&protocol=http&timeout=15000&country=all&ssl=all&anonymity=all";

    /// <summary>
    /// Рабочий дефолтный лимит старта запросов в секунду для ProxyScrape.
    /// </summary>
    public const int DefaultRequestsPerSecondLimit = 2;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly ProxyScrapeProviderOptions endpointOptions;
    private readonly ProxyType proxyType;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер ProxyScrape.
    /// </summary>
    public ProxyScrapeProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        endpointOptions = ParseEndpointOptions(endpoint);
        proxyType = ParseProxyType(endpointOptions.Protocol);
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
    }

    /// <summary>
    /// Создаёт провайдер ProxyScrape из явной конфигурации endpoint.
    /// </summary>
    public ProxyScrapeProvider(ProxyScrapeProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : this(CreateEndpoint(options), httpClient, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        endpointOptions = NormalizeOptions(options);
        proxyType = ParseProxyType(endpointOptions.Protocol);
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
        return await LoadEndpointAsync(Endpoint, proxyType, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<ProxyProviderFetchResult> FetchAsync(ProxyProviderFetchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedCount = Math.Max(1, request.RequestedCount);
        var protocols = ResolveProtocols(request.Protocols);
        if (protocols.Count == 0)
        {
            return new ProxyProviderFetchResult([], IsPartial: request.AllowPartial, SourceExhausted: true);
        }

        var countries = ResolveCountries(request.Countries);
        var anonymity = ResolveAnonymity(request.AnonymityLevels);
        var proxies = new List<ServiceProxy>(requestedCount);

        for (var countryIndex = 0; countryIndex < countries.Count && proxies.Count < requestedCount; countryIndex++)
        {
            for (var protocolIndex = 0; protocolIndex < protocols.Count && proxies.Count < requestedCount; protocolIndex++)
            {
                var protocol = protocols[protocolIndex];
                var endpoint = CreateEndpoint(new ProxyScrapeProviderOptions
                {
                    Protocol = protocol.QueryValue,
                    TimeoutMilliseconds = endpointOptions.TimeoutMilliseconds,
                    Country = countries[countryIndex],
                    Ssl = endpointOptions.Ssl,
                    Anonymity = anonymity,
                });

                var page = await LoadEndpointAsync(endpoint, protocol.ProxyType, cancellationToken).ConfigureAwait(false);
                proxies.AddRange(page.Take(requestedCount - proxies.Count));
            }
        }

        return new ProxyProviderFetchResult(
            [.. proxies],
            IsPartial: proxies.Count < requestedCount,
            SourceExhausted: proxies.Count < requestedCount);
    }

    /// <summary>
    /// Преобразует plain-text ответ ProxyScrape в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    /// <param name="payload">Текстовый ответ ProxyScrape, содержащий строки host:port.</param>
    /// <param name="proxyType">Тип прокси, соответствующий protocol query, из которого был получен payload.</param>
    public static IEnumerable<ServiceProxy> Parse(string payload, ProxyType proxyType = ProxyType.Http)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var proxies = new List<ServiceProxy>();
        using var reader = new StringReader(payload);
        while (reader.ReadLine() is { } rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.Trim();
            var separatorIndex = line.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
            {
                continue;
            }

            var host = line[..separatorIndex].Trim();
            if (!int.TryParse(line[(separatorIndex + 1)..], out var port) || port <= 0)
            {
                continue;
            }

            proxies.Add(new ServiceProxy
            {
                Provider = nameof(ProxyScrapeProvider),
                Host = host,
                Port = port,
                Type = proxyType,
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт ProxyScrape endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(ProxyScrapeProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = new Dictionary<string, string>(capacity: 6, comparer: StringComparer.OrdinalIgnoreCase)
        {
            ["request"] = "getproxies",
            ["protocol"] = ProviderEndpointBuilder.LowerOrDefault(options.Protocol, "http"),
            ["timeout"] = ProviderEndpointBuilder.PositiveOrDefault(options.TimeoutMilliseconds, 15000).ToString(CultureInfo.InvariantCulture),
            ["country"] = ProviderEndpointBuilder.LowerOrDefault(options.Country, "all"),
            ["ssl"] = ProviderEndpointBuilder.LowerOrDefault(options.Ssl, "all"),
            ["anonymity"] = ProviderEndpointBuilder.LowerOrDefault(options.Anonymity, "all"),
        };

        return ProviderEndpointBuilder.Create("https://api.proxyscrape.com/v2/", query);
    }

    private async ValueTask<IEnumerable<ServiceProxy>> LoadEndpointAsync(string endpoint, ProxyType endpointProxyType, CancellationToken cancellationToken)
    {
        return await RunRateLimitedAsync(async token =>
        {
            using var response = await httpClient.GetAsync(endpoint, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return Parse(payload, endpointProxyType);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ProxyScrapeProviderOptions NormalizeOptions(ProxyScrapeProviderOptions options)
    {
        return new ProxyScrapeProviderOptions
        {
            RequestsPerSecondLimit = Math.Max(1, options.RequestsPerSecondLimit),
            Protocol = NormalizeProtocol(options.Protocol),
            TimeoutMilliseconds = ProviderEndpointBuilder.PositiveOrDefault(options.TimeoutMilliseconds, 15000),
            Country = NormalizeCountry(options.Country),
            Ssl = NormalizeSsl(options.Ssl),
            Anonymity = NormalizeAnonymity(options.Anonymity),
        };
    }

    private static ProxyScrapeProviderOptions ParseEndpointOptions(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return NormalizeOptions(new ProxyScrapeProviderOptions());
        }

        var query = ParseQuery(uri.Query);
        return NormalizeOptions(new ProxyScrapeProviderOptions
        {
            Protocol = query.TryGetValue("protocol", out var protocol) ? protocol : "http",
            TimeoutMilliseconds = query.TryGetValue("timeout", out var timeout)
                                  && int.TryParse(timeout, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout)
                ? parsedTimeout
                : 15000,
            Country = query.TryGetValue("country", out var country) ? country : "all",
            Ssl = query.TryGetValue("ssl", out var ssl) ? ssl : "all",
            Anonymity = query.TryGetValue("anonymity", out var anonymity) ? anonymity : "all",
        });
    }

    private IReadOnlyList<ProxyScrapeProtocolTarget> ResolveProtocols(IReadOnlyList<ProxyType> requestedProtocols)
    {
        if (requestedProtocols.Count == 0)
        {
            return [new ProxyScrapeProtocolTarget(endpointOptions.Protocol, proxyType)];
        }

        var protocols = new List<ProxyScrapeProtocolTarget>(requestedProtocols.Count);
        var seen = new HashSet<ProxyType>();
        for (var index = 0; index < requestedProtocols.Count; index++)
        {
            var requestedProtocol = requestedProtocols[index];
            if (!seen.Add(requestedProtocol) || !TryMapProtocol(requestedProtocol, out var queryValue))
            {
                continue;
            }

            protocols.Add(new ProxyScrapeProtocolTarget(queryValue, requestedProtocol));
        }

        return protocols;
    }

    private IReadOnlyList<string> ResolveCountries(IReadOnlyList<Country> requestedCountries)
    {
        if (requestedCountries.Count == 0)
        {
            return [endpointOptions.Country];
        }

        return requestedCountries
            .Where(static country => country is not null)
            .Select(static country => NormalizeCountry(country.IsoCode2))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolveAnonymity(IReadOnlyList<AnonymityLevel> requestedAnonymityLevels)
    {
        if (requestedAnonymityLevels.Count == 0)
        {
            return endpointOptions.Anonymity;
        }

        return requestedAnonymityLevels
            .Distinct()
            .ToArray() is [var onlyLevel] && TryMapAnonymity(onlyLevel, out var mapped)
                ? mapped
                : "all";
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

    private static bool TryMapProtocol(ProxyType proxyType, out string queryValue)
    {
        switch (proxyType)
        {
            case ProxyType.Http:
                queryValue = "http";
                return true;
            case ProxyType.Https:
                queryValue = "https";
                return true;
            case ProxyType.Socks4:
                queryValue = "socks4";
                return true;
            case ProxyType.Socks5:
                queryValue = "socks5";
                return true;
            default:
                queryValue = string.Empty;
                return false;
        }
    }

    private static ProxyType ParseProxyType(string? protocol) => NormalizeProtocol(protocol) switch
    {
        "https" => ProxyType.Https,
        "socks4" => ProxyType.Socks4,
        "socks5" => ProxyType.Socks5,
        _ => ProxyType.Http,
    };

    private static bool TryMapAnonymity(AnonymityLevel level, out string queryValue)
    {
        switch (level)
        {
            case AnonymityLevel.Transparent:
                queryValue = "transparent";
                return true;
            case AnonymityLevel.Medium:
                queryValue = "anonymous";
                return true;
            case AnonymityLevel.High:
                queryValue = "elite";
                return true;
            default:
                queryValue = string.Empty;
                return false;
        }
    }

    private static string NormalizeProtocol(string? protocol)
        => protocol?.Trim().ToLowerInvariant() switch
        {
            "https" => "https",
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "http",
        };

    private static string NormalizeCountry(string? country)
        => string.IsNullOrWhiteSpace(country) ? "all" : country.Trim().ToLowerInvariant();

    private static string NormalizeSsl(string? ssl)
        => ssl?.Trim().ToLowerInvariant() switch
        {
            "yes" => "yes",
            "no" => "no",
            _ => "all",
        };

    private static string NormalizeAnonymity(string? anonymity)
        => anonymity?.Trim().ToLowerInvariant() switch
        {
            "elite" => "elite",
            "anonymous" => "anonymous",
            "transparent" => "transparent",
            _ => "all",
        };

    private readonly record struct ProxyScrapeProtocolTarget(string QueryValue, ProxyType ProxyType);
}