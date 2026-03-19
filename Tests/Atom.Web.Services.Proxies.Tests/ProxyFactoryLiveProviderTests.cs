using System.Diagnostics;
using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyFactoryLiveProviderTests(ILogger logger) : BenchmarkTests<ProxyFactoryLiveProviderTests>(logger)
{
    public ProxyFactoryLiveProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Фабрика собирает unique proxy pool из всех реализованных live providers и измеряет время")]
    public async Task FactoryCollectsUniquePoolFromAllLiveProvidersTest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var providerRuns = new List<ProviderRunResult>(capacity: 3);
        var providers = CreateLiveProviders();
        try
        {
            for (var index = 0; index < providers.Length; index++)
            {
                var run = await MeasureProviderAsync(providers[index], timeout.Token);
                providerRuns.Add(run);
                logger.WriteLine(LogKind.Default,
                    $"proxy provider {run.Name}: total={run.TotalCount}, unique={run.UniqueCount}, elapsed={run.Elapsed.TotalMilliseconds:F0} ms");
            }

            using var factory = new ProxyFactory
            {
                RefreshInterval = TimeSpan.FromHours(1),
                DedupKeyResolver = ProxyDedupKeyResolvers.Literal,
            };

            for (var index = 0; index < providers.Length; index++)
            {
                factory.Use(providers[index]);
            }

            var aggregateWatch = Stopwatch.StartNew();
            var aggregate = (await factory.GetAsync(int.MaxValue, timeout.Token).ConfigureAwait(false)).ToArray();
            aggregateWatch.Stop();

            var aggregateUnique = CountLiteralUnique(aggregate);
            var providerTotal = providerRuns.Sum(static run => run.TotalCount);
            var providerUnique = providerRuns.Sum(static run => run.UniqueCount);

            logger.WriteLine(LogKind.Default,
                $"proxy factory aggregate: providers={providers.Length}, providerTotal={providerTotal}, providerUniqueSum={providerUnique}, aggregateCount={aggregate.Length}, aggregateUnique={aggregateUnique}, elapsed={aggregateWatch.Elapsed.TotalMilliseconds:F0} ms");

            Assert.Multiple(() =>
            {
                Assert.That(providerRuns, Has.Count.EqualTo(providers.Length));
                Assert.That(providerRuns.All(static run => run.Elapsed >= TimeSpan.Zero), Is.True);
                Assert.That(aggregate.Length, Is.EqualTo(aggregateUnique), "Factory aggregate должен уже быть deduped.");
            });
        }
        finally
        {
            for (var index = 0; index < providers.Length; index++)
            {
                providers[index].Dispose();
            }
        }
    }

    private static IProxyProvider[] CreateLiveProviders()
    {
        return
        [
            new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
            {
                Limit = 500,
                Page = 1,
                SortBy = "lastChecked",
                SortType = "desc",
            }),
            new ProxyScrapeProvider(new ProxyScrapeProviderOptions
            {
                Protocol = "http",
                Country = "all",
                Ssl = "all",
                Anonymity = "all",
                TimeoutMilliseconds = 15000,
            }),
            new ProxyNovaProvider(new ProxyNovaProviderOptions
            {
                Limit = ProxyNovaProvider.MaximumLimit,
            }),
        ];
    }

    private static async Task<ProviderRunResult> MeasureProviderAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        if (provider is ProxyProvider proxyProvider)
        {
            proxyProvider.RefreshInterval = TimeSpan.FromHours(1);
            proxyProvider.DedupKeyResolver = ProxyDedupKeyResolvers.Literal;
        }

        var watch = Stopwatch.StartNew();
        var proxies = (await provider.GetAsync(int.MaxValue, cancellationToken).ConfigureAwait(false)).ToArray();
        watch.Stop();

        return new ProviderRunResult(
            provider.GetType().Name,
            proxies.Length,
            CountLiteralUnique(proxies),
            watch.Elapsed);
    }

    private static int CountLiteralUnique(IEnumerable<ServiceProxy> proxies)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in proxies)
        {
            var host = proxy.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }

            var candidate = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
            if (System.Net.IPAddress.TryParse(candidate, out var address))
            {
                keys.Add(address.ToString());
                continue;
            }

            keys.Add(host);
        }

        return keys.Count;
    }

    private sealed record ProviderRunResult(string Name, int TotalCount, int UniqueCount, TimeSpan Elapsed);
}