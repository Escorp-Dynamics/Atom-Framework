using System.IO;
using Atom.Net.Proxies;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через raw mixed-scheme список proxifly/free-proxy-list.
/// </summary>
public sealed class ProxiflyProxyListProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint proxifly/free-proxy-list.
    /// </summary>
    public const string DefaultEndpoint = "https://raw.githubusercontent.com/proxifly/free-proxy-list/main/proxies/all/data.txt";

    /// <summary>
    /// Рабочий дефолтный лимит старта запросов в секунду для proxifly/free-proxy-list.
    /// </summary>
    public const int DefaultRequestsPerSecondLimit = 2;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly ProxyType? filterType;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер proxifly/free-proxy-list.
    /// </summary>
    public ProxiflyProxyListProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
        filterType = null;
    }

    /// <summary>
    /// Создаёт провайдер proxifly/free-proxy-list из явной конфигурации.
    /// </summary>
    public ProxiflyProxyListProvider(ProxiflyProxyListProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : this(CreateEndpoint(options), httpClient, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequestsPerSecondLimit = Math.Max(1, options.RequestsPerSecondLimit);
        filterType = ParseFilterType(options.Protocol);
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
        return await RunRateLimitedAsync(async token =>
        {
            using var response = await httpClient.GetAsync(Endpoint, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return Parse(payload, filterType);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Преобразует mixed-scheme plain-text ответ proxifly/free-proxy-list в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    public static IEnumerable<ServiceProxy> Parse(string payload, ProxyType? filterType = null)
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
            var schemeSeparatorIndex = line.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparatorIndex <= 0 || schemeSeparatorIndex >= line.Length - 3)
            {
                continue;
            }

            if (!TryParseProxyType(line[..schemeSeparatorIndex], out var proxyType))
            {
                continue;
            }

            if (filterType.HasValue && filterType.Value != proxyType)
            {
                continue;
            }

            var address = line[(schemeSeparatorIndex + 3)..];
            var separatorIndex = address.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= address.Length - 1)
            {
                continue;
            }

            var host = address[..separatorIndex].Trim();
            if (!int.TryParse(address[(separatorIndex + 1)..], out var port) || port <= 0)
            {
                continue;
            }

            proxies.Add(new ServiceProxy
            {
                Provider = nameof(ProxiflyProxyListProvider),
                Host = host,
                Port = port,
                Type = proxyType,
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт proxifly/free-proxy-list endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(ProxiflyProxyListProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DefaultEndpoint;
    }

    private static ProxyType? ParseFilterType(string? protocol) => NormalizeProtocol(protocol) switch
    {
        "http" => ProxyType.Http,
        "https" => ProxyType.Https,
        "socks4" => ProxyType.Socks4,
        "socks5" => ProxyType.Socks5,
        _ => null,
    };

    private static bool TryParseProxyType(string? protocol, out ProxyType proxyType)
    {
        switch (NormalizeProtocol(protocol))
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

    private static string NormalizeProtocol(string? protocol)
        => protocol?.Trim().ToLowerInvariant() switch
        {
            "http" => "http",
            "https" => "https",
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "all",
        };
}