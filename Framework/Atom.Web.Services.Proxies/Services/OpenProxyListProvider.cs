using System.IO;
using Atom.Net.Proxies;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через raw plain-text списки roosterkid/openproxylist.
/// </summary>
public sealed class OpenProxyListProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint roosterkid/openproxylist для бесплатных HTTPS proxy.
    /// </summary>
    public const string DefaultEndpoint = "https://raw.githubusercontent.com/roosterkid/openproxylist/main/HTTPS_RAW.txt";

    /// <summary>
    /// Рабочий дефолтный лимит старта запросов в секунду для roosterkid/openproxylist.
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
    /// Создаёт провайдер roosterkid/openproxylist.
    /// </summary>
    public OpenProxyListProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
        RequestsPerSecondLimit = DefaultRequestsPerSecondLimit;
        proxyType = InferProxyType(endpoint);
    }

    /// <summary>
    /// Создаёт провайдер roosterkid/openproxylist из явной конфигурации endpoint.
    /// </summary>
    public OpenProxyListProvider(OpenProxyListProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
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
    /// Преобразует plain-text ответ roosterkid/openproxylist в нормализованный набор <see cref="ServiceProxy"/>.
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
                Provider = nameof(OpenProxyListProvider),
                Host = host,
                Port = port,
                Type = proxyType,
            });
        }

        return proxies;
    }

    /// <summary>
    /// Создаёт roosterkid/openproxylist endpoint из явной конфигурации.
    /// </summary>
    public static string CreateEndpoint(OpenProxyListProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return NormalizeProtocol(options.Protocol) switch
        {
            "socks4" => "https://raw.githubusercontent.com/roosterkid/openproxylist/main/SOCKS4_RAW.txt",
            "socks5" => "https://raw.githubusercontent.com/roosterkid/openproxylist/main/SOCKS5_RAW.txt",
            _ => DefaultEndpoint,
        };
    }

    private static ProxyType InferProxyType(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ProxyType.Https;
        }

        return endpoint.Contains("SOCKS4", StringComparison.OrdinalIgnoreCase)
            ? ProxyType.Socks4
            : endpoint.Contains("SOCKS5", StringComparison.OrdinalIgnoreCase)
                ? ProxyType.Socks5
                : ProxyType.Https;
    }

    private static ProxyType ParseProxyType(string? protocol) => NormalizeProtocol(protocol) switch
    {
        "socks4" => ProxyType.Socks4,
        "socks5" => ProxyType.Socks5,
        _ => ProxyType.Https,
    };

    private static string NormalizeProtocol(string? protocol)
        => protocol?.Trim().ToLowerInvariant() switch
        {
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "https",
        };
}