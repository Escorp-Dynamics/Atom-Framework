using System.Diagnostics;
using System.Net;
using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyFactoryLiveProviderTests : BenchmarkTests<ProxyFactoryLiveProviderTests>
{
    private readonly ILogger testLogger;

    public ProxyFactoryLiveProviderTests(ILogger logger) : base(logger)
    {
        testLogger = logger;
    }

    public ProxyFactoryLiveProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Фабрика собирает unique proxy pool из всех реализованных live providers и измеряет время")]
    public async Task FactoryCollectsUniquePoolFromAllLiveProvidersTest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var aggregateRuns = CreateLiveProviders(fetchFullGeoNodeInventory: true);
            var aggregateRun = await MeasureFactoryAsync(aggregateRuns, timeout.Token);
            testLogger.WriteLine(LogKind.Default,
                $"proxy factory cold aggregate: providers={aggregateRun.ProviderCount}, total={aggregateRun.TotalCount}, endpointUnique={aggregateRun.EndpointUniqueCount}, hostUnique={aggregateRun.HostUniqueCount}, elapsed={aggregateRun.Elapsed.TotalMilliseconds:F0} ms");

            DisposeProviders(aggregateRuns);

            var providerRuns = new List<ProviderRunResult>(capacity: 3);
            var providers = CreateLiveProviders(fetchFullGeoNodeInventory: true);
            try
            {
                for (var index = 0; index < providers.Length; index++)
                {
                    var run = await MeasureProviderAsync(providers[index], timeout.Token);
                    providerRuns.Add(run);
                    testLogger.WriteLine(LogKind.Default,
                        $"proxy provider {run.Name}: total={run.TotalCount}, endpointUnique={run.EndpointUniqueCount}, hostUnique={run.HostUniqueCount}, elapsed={run.Elapsed.TotalMilliseconds:F0} ms");
                }

                var providerTotal = providerRuns.Sum(static run => run.TotalCount);
                var providerEndpointUnique = providerRuns.Sum(static run => run.EndpointUniqueCount);
                var providerHostUnique = providerRuns.Sum(static run => run.HostUniqueCount);

                testLogger.WriteLine(LogKind.Default,
                    $"proxy provider totals: providers={providers.Length}, rawTotal={providerTotal}, endpointUniqueSum={providerEndpointUnique}, hostUniqueSum={providerHostUnique}");

                Assert.Multiple(() =>
                {
                    Assert.That(providerRuns, Has.Count.EqualTo(providers.Length));
                    Assert.That(providerRuns.All(static run => run.Elapsed >= TimeSpan.Zero), Is.True);
                    Assert.That(aggregateRun.TotalCount, Is.EqualTo(aggregateRun.HostUniqueCount), "Factory aggregate должен быть deduped по literal host key.");
                });
            }
            finally
            {
                DisposeProviders(providers);
            }
        }
        catch (HttpRequestException exception) when (IsUpstreamThrottle(exception))
        {
            Assert.Ignore($"Live provider test skipped due to upstream throttling: {exception.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            Assert.Ignore("Live provider test skipped because upstream inventory collection exceeded the diagnostic timeout.");
        }
    }

    [TestCase(TestName = "Live providers оценивают safe request start rate без отказов клиента")]
    public async Task LiveProvidersObserveSafeRequestStartRateTest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            var probes = await Task.WhenAll(
                MeasureRequestStartRateAsync(
                    nameof(GeoNodeProxyProvider),
                    static () => new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
                    {
                        Limit = 1,
                        Page = 1,
                        RequestsPerSecondLimit = 1,
                        SortBy = "lastChecked",
                        SortType = "desc",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(ProxyScrapeProvider),
                    static () => new ProxyScrapeProvider(new ProxyScrapeProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "http",
                        Country = "all",
                        Ssl = "all",
                        Anonymity = "all",
                        TimeoutMilliseconds = 15000,
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(ProxyNovaProvider),
                    static () => new ProxyNovaProvider(new ProxyNovaProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Limit = 1,
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(OpenProxyListProvider),
                    static () => new OpenProxyListProvider(new OpenProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(ProxiflyProxyListProvider),
                    static () => new ProxiflyProxyListProvider(new ProxiflyProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "socks5",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(ProxymaniaProxyListProvider),
                    static () => new ProxymaniaProxyListProvider(new ProxymaniaProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(HideMyNameProxyListProvider),
                    static () => new HideMyNameProxyListProvider(new HideMyNameProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(IplocateProxyListProvider),
                    static () => new IplocateProxyListProvider(new IplocateProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(R00teeProxyListProvider),
                    static () => new R00teeProxyListProvider(new R00teeProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(VakhovProxyListProvider),
                    static () => new VakhovProxyListProvider(new VakhovProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(GfpcomProxyListProvider),
                    static () => new GfpcomProxyListProvider(new GfpcomProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token),
                MeasureRequestStartRateAsync(
                    nameof(ZaeemProxyListProvider),
                    static () => new ZaeemProxyListProvider(new ZaeemProxyListProviderOptions
                    {
                        RequestsPerSecondLimit = 1,
                        Protocol = "https",
                    }),
                    timeout.Token));

            for (var index = 0; index < probes.Length; index++)
            {
                var probe = probes[index];
                for (var observationIndex = 0; observationIndex < probe.Observations.Length; observationIndex++)
                {
                    var observation = probe.Observations[observationIndex];
                    var error = observation.FirstError ?? "none";
                    testLogger.WriteLine(LogKind.Default,
                        $"proxy provider {probe.Name}: rps={observation.RequestsPerSecond}, successes={observation.SuccessCount}/{observation.AttemptCount}, avg={observation.AverageElapsed.TotalMilliseconds:F0} ms, max={observation.MaxElapsed.TotalMilliseconds:F0} ms, error={error}");
                }

                testLogger.WriteLine(LogKind.Default,
                    $"proxy provider {probe.Name}: observed safe start rate={probe.RecommendedRequestsPerSecond} rps");
            }

            if (probes.Any(IsUpstreamThrottledProbe))
            {
                Assert.Ignore("Live rate probe skipped due to upstream throttling on at least one provider.");
            }

            Assert.That(probes.All(static probe => probe.RecommendedRequestsPerSecond > 0), Is.True);
        }
        catch (OperationCanceledException)
        {
            Assert.Ignore("Live rate probe skipped because the upstream diagnostics exceeded the allotted test session time.");
        }
    }

    private static bool IsUpstreamThrottle(HttpRequestException exception)
        => exception.StatusCode == HttpStatusCode.TooManyRequests;

    private static bool IsUpstreamThrottledProbe(RequestRateProbeResult probe)
        => probe.RecommendedRequestsPerSecond == 0
           && probe.Observations.Any(static observation => string.Equals(observation.FirstError, nameof(HttpRequestException), StringComparison.Ordinal));

    private static IProxyProvider[] CreateLiveProviders(bool fetchFullGeoNodeInventory)
    {
        var providers = new List<IProxyProvider>
        {
            new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
            {
                Limit = 500,
                Page = 1,
                FetchAllPages = fetchFullGeoNodeInventory,
                RequestsPerSecondLimit = 2,
                SortBy = "lastChecked",
                SortType = "desc",
            }),
            new ProxyScrapeProvider(new ProxyScrapeProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "http",
                Country = "all",
                Ssl = "all",
                Anonymity = "all",
                TimeoutMilliseconds = 15000,
            }),
            new ProxyNovaProvider(new ProxyNovaProviderOptions
            {
                FetchPublishedCountries = true,
                RequestsPerSecondLimit = 2,
                Limit = ProxyNovaProvider.MaximumLimit,
            }),
            new OpenProxyListProvider(new OpenProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
            new ProxiflyProxyListProvider(new ProxiflyProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "socks5",
            }),
            new ProxymaniaProxyListProvider(new ProxymaniaProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
            }),
            new HideMyNameProxyListProvider(new HideMyNameProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
            }),
            new IplocateProxyListProvider(new IplocateProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
            new R00teeProxyListProvider(new R00teeProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
            new VakhovProxyListProvider(new VakhovProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
            new GfpcomProxyListProvider(new GfpcomProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
            new ZaeemProxyListProvider(new ZaeemProxyListProviderOptions
            {
                RequestsPerSecondLimit = 2,
                Protocol = "https",
            }),
        };

        return [.. providers];
    }

    private static async Task<ProviderRunResult> MeasureProviderAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        if (provider is ProxyProvider proxyProvider)
        {
            proxyProvider.DedupKeyResolver = ProxyDedupKeyResolvers.Literal;
        }

        var watch = Stopwatch.StartNew();
        var proxies = (await provider.FetchAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        watch.Stop();

        return new ProviderRunResult(
            provider.GetType().Name,
            proxies.Length,
            CountEndpointUnique(proxies),
            CountLiteralUnique(proxies),
            watch.Elapsed);
    }

    private static async Task<FactoryRunResult> MeasureFactoryAsync(IProxyProvider[] providers, CancellationToken cancellationToken)
    {
        using var factory = new ProxyFactory
        {
            RefreshInterval = TimeSpan.FromHours(1),
            DedupKeyResolver = ProxyDedupKeyResolvers.Literal,
        };

        for (var index = 0; index < providers.Length; index++)
        {
            factory.Use(providers[index]);
        }

        var watch = Stopwatch.StartNew();
        var aggregate = (await factory.GetAsync(int.MaxValue, cancellationToken).ConfigureAwait(false)).ToArray();
        watch.Stop();

        return new FactoryRunResult(
            providers.Length,
            aggregate.Length,
            CountEndpointUnique(aggregate),
            CountLiteralUnique(aggregate),
            watch.Elapsed);
    }

    private static async Task<RequestRateProbeResult> MeasureRequestStartRateAsync(
        string name,
        Func<IProxyProvider> createProvider,
        CancellationToken cancellationToken)
    {
        int[] candidateRates = [1, 2, 3, 4];
        var observations = new ProviderRateObservation[candidateRates.Length];
        var recommended = 0;

        for (var index = 0; index < candidateRates.Length; index++)
        {
            var observation = await ProbeRateAsync(createProvider, candidateRates[index], attempts: 2, cancellationToken).ConfigureAwait(false);
            observations[index] = observation;
            if (observation.SuccessCount == observation.AttemptCount)
            {
                recommended = observation.RequestsPerSecond;
            }
        }

        return new RequestRateProbeResult(name, observations, recommended);
    }

    private static async Task<ProviderRateObservation> ProbeRateAsync(
        Func<IProxyProvider> createProvider,
        int requestsPerSecond,
        int attempts,
        CancellationToken cancellationToken)
    {
        var tasks = new Task<ProbeAttemptResult>[attempts];
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            tasks[attempt] = RunProbeAttemptAsync(createProvider, attempt, requestsPerSecond, cancellationToken);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var successCount = results.Count(static result => result.Error is null);
        var averageElapsed = results.Length == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(results.Average(static result => result.Elapsed.TotalMilliseconds));
        var maxElapsed = results.Length == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(results.Max(static result => result.Elapsed.TotalMilliseconds));
        string? firstError = null;
        for (var index = 0; index < results.Length; index++)
        {
            if (results[index].Error is { } error)
            {
                firstError = error;
                break;
            }
        }

        return new ProviderRateObservation(requestsPerSecond, attempts, successCount, averageElapsed, maxElapsed, firstError);
    }

    private static async Task<ProbeAttemptResult> RunProbeAttemptAsync(
        Func<IProxyProvider> createProvider,
        int attemptIndex,
        int requestsPerSecond,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(attemptIndex * (1000d / requestsPerSecond));
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        using var provider = createProvider();
        if (provider is ProxyProvider proxyProvider)
        {
            proxyProvider.DedupKeyResolver = ProxyDedupKeyResolvers.Literal;
        }

        switch (provider)
        {
            case GeoNodeProxyProvider geoNodeProvider:
                geoNodeProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case ProxyScrapeProvider proxyScrapeProvider:
                proxyScrapeProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case ProxyNovaProvider proxyNovaProvider:
                proxyNovaProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case OpenProxyListProvider openProxyListProvider:
                openProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case ProxiflyProxyListProvider proxiflyProxyListProvider:
                proxiflyProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case ProxymaniaProxyListProvider proxymaniaProxyListProvider:
                proxymaniaProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case HideMyNameProxyListProvider hideMyNameProxyListProvider:
                hideMyNameProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case IplocateProxyListProvider iplocateProxyListProvider:
                iplocateProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case VakhovProxyListProvider vakhovProxyListProvider:
                vakhovProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case GfpcomProxyListProvider gfpcomProxyListProvider:
                gfpcomProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
            case ZaeemProxyListProvider zaeemProxyListProvider:
                zaeemProxyListProvider.RequestsPerSecondLimit = requestsPerSecond;
                break;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            await provider.FetchAsync(cancellationToken).ConfigureAwait(false);
            watch.Stop();
            return new ProbeAttemptResult(watch.Elapsed, null);
        }
        catch (Exception exception)
        {
            watch.Stop();
            return new ProbeAttemptResult(watch.Elapsed, exception.GetType().Name);
        }
    }

    private static int CountEndpointUnique(IEnumerable<ServiceProxy> proxies)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in proxies)
        {
            if (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port <= 0)
            {
                continue;
            }

            keys.Add($"{proxy.Host}:{proxy.Port}:{proxy.Type}");
        }

        return keys.Count;
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

    private static void DisposeProviders(IProxyProvider[] providers)
    {
        for (var index = 0; index < providers.Length; index++)
        {
            providers[index].Dispose();
        }
    }

    private sealed record ProviderRunResult(string Name, int TotalCount, int EndpointUniqueCount, int HostUniqueCount, TimeSpan Elapsed);

    private sealed record FactoryRunResult(int ProviderCount, int TotalCount, int EndpointUniqueCount, int HostUniqueCount, TimeSpan Elapsed);

    private sealed record RequestRateProbeResult(string Name, ProviderRateObservation[] Observations, int RecommendedRequestsPerSecond);

    private sealed record ProviderRateObservation(int RequestsPerSecond, int AttemptCount, int SuccessCount, TimeSpan AverageElapsed, TimeSpan MaxElapsed, string? FirstError);

    private sealed record ProbeAttemptResult(TimeSpan Elapsed, string? Error);
}