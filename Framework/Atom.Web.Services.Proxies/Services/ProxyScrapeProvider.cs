using System.Globalization;
using System.IO;
using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Провайдер бесплатных прокси через публичный plain-text endpoint ProxyScrape.
/// </summary>
public sealed class ProxyScrapeProvider : NetworkProxyProvider
{
    /// <summary>
    /// Базовый endpoint ProxyScrape для бесплатных HTTP proxy.
    /// </summary>
    public const string DefaultEndpoint = "https://api.proxyscrape.com/v2/?request=getproxies&protocol=http&timeout=15000&country=all&ssl=all&anonymity=all";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;

    /// <summary>
    /// Endpoint, из которого загружается список прокси.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Создаёт провайдер ProxyScrape.
    /// </summary>
    public ProxyScrapeProvider(string endpoint = DefaultEndpoint, HttpClient? httpClient = null)
    {
        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        disposeHttpClient = httpClient is null;
    }

    /// <summary>
    /// Создаёт провайдер ProxyScrape из явной конфигурации endpoint.
    /// </summary>
    public ProxyScrapeProvider(ProxyScrapeProviderOptions options, HttpClient? httpClient = null)
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
    /// Преобразует plain-text ответ ProxyScrape в нормализованный набор <see cref="ServiceProxy"/>.
    /// </summary>
    /// <param name="payload">Текстовый ответ ProxyScrape, содержащий строки host:port.</param>
    public static IEnumerable<ServiceProxy> Parse(string payload)
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
                Type = ProxyType.Http,
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
}