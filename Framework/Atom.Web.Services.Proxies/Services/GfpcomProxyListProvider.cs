using System.IO;
using Atom.Net.Proxies;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через raw GitHub wiki-списки gfpcom/free-proxy-list.
/// </summary>
public sealed class GfpcomProxyListProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint gfpcom/free-proxy-list для бесплатных HTTPS proxy.
    /// </summary>
    public const string DefaultEndpoint = "https://raw.githubusercontent.com/wiki/gfpcom/free-proxy-list/lists/https.txt";

    /// <summary>
    /// Рабочий дефолтный лимит старта запросов в секунду для gfpcom/free-proxy-list.
    /// </summary>
    public const int DefaultRequestsPerSecondLimit = 2;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly ProxyType proxyType;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер gfpcom/free-proxy-list.
    /// </summary>
    public GfpcomProxyListProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
        proxyType = InferProxyType(endpoint);
    }

    /// <summary>
    /// Создаёт провайдер gfpcom/free-proxy-list из явной конфигурации endpoint.
    /// </summary>
    public GfpcomProxyListProvider(GfpcomProxyListProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : this(CreateEndpoint(options), httpClient, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequestsPerSecondLimit = Math.Max(1, options.RequestsPerSecondLimit);
        proxyType = ParseProxyType(options.Protocol);
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
            return Parse(payload, proxyType);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Преобразует ответ gfpcom/free-proxy-list в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    public static IEnumerable<ServiceProxy> Parse(string payload, ProxyType proxyType = ProxyType.Https)
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

            if (!TryParseLine(rawLine.Trim(), proxyType, out var host, out var port, out var parsedProxyType))
            {
                continue;
            }

            proxies.Add(new ServiceProxy
            {
                Provider = nameof(GfpcomProxyListProvider),
                Host = host,
                Port = port,
                Type = parsedProxyType,
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт gfpcom/free-proxy-list endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(GfpcomProxyListProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return NormalizeProtocol(options.Protocol) switch
        {
            "http" => "https://raw.githubusercontent.com/wiki/gfpcom/free-proxy-list/lists/http.txt",
            "socks4" => "https://raw.githubusercontent.com/wiki/gfpcom/free-proxy-list/lists/socks4.txt",
            "socks5" => "https://raw.githubusercontent.com/wiki/gfpcom/free-proxy-list/lists/socks5.txt",
            _ => DefaultEndpoint,
        };
    }

    private static bool TryParseLine(string line, ProxyType fallbackProxyType, out string host, out int port, out ProxyType proxyType)
    {
        proxyType = fallbackProxyType;
        var address = line;

        var schemeSeparatorIndex = line.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparatorIndex > 0)
        {
            if (!TryParseProxyType(line[..schemeSeparatorIndex], out proxyType))
            {
                host = string.Empty;
                port = 0;
                return false;
            }

            address = line[(schemeSeparatorIndex + 3)..];
        }

        var credentialsSeparatorIndex = address.LastIndexOf('@');
        if (credentialsSeparatorIndex >= 0 && credentialsSeparatorIndex < address.Length - 1)
        {
            address = address[(credentialsSeparatorIndex + 1)..];
        }

        var separatorIndex = address.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= address.Length - 1)
        {
            host = string.Empty;
            port = 0;
            return false;
        }

        host = address[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(address[(separatorIndex + 1)..], out port)
            || port <= 0
            || port > ushort.MaxValue)
        {
            host = string.Empty;
            port = 0;
            return false;
        }

        return true;
    }

    private static ProxyType InferProxyType(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ProxyType.Https;
        }

        return endpoint.Contains("/http.txt", StringComparison.OrdinalIgnoreCase)
            ? ProxyType.Http
            : endpoint.Contains("/socks4.txt", StringComparison.OrdinalIgnoreCase)
                ? ProxyType.Socks4
                : endpoint.Contains("/socks5.txt", StringComparison.OrdinalIgnoreCase)
                    ? ProxyType.Socks5
                    : ProxyType.Https;
    }

    private static ProxyType ParseProxyType(string? protocol) => NormalizeProtocol(protocol) switch
    {
        "http" => ProxyType.Http,
        "socks4" => ProxyType.Socks4,
        "socks5" => ProxyType.Socks5,
        _ => ProxyType.Https,
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
            _ => "https",
        };
}