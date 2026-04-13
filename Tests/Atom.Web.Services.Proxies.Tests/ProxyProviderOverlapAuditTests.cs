using System.Net;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyProviderOverlapAuditTests(ILogger logger) : BenchmarkTests<ProxyProviderOverlapAuditTests>(logger)
{
    private readonly ILogger testLogger = logger;

    public ProxyProviderOverlapAuditTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Diagnostic overlap audit for Proxymania against curated raw/HTML union")]
    [Explicit("Manual live diagnostic for additive coverage of raw/HTML proxy providers.")]
    public async Task ProxymaniaOverlapAuditTest()
    {
        await RunOverlapAuditAsync(
            nameof(ProxymaniaProxyListProvider),
            new ProxymaniaProxyListProvider(new ProxymaniaProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
            })).ConfigureAwait(false);
    }

    [TestCase(TestName = "Diagnostic overlap audit for HideMyName against curated raw/HTML union")]
    [Explicit("Manual live diagnostic for additive coverage of raw/HTML proxy providers.")]
    public async Task HideMyNameOverlapAuditTest()
    {
        await RunOverlapAuditAsync(
            nameof(HideMyNameProxyListProvider),
            new HideMyNameProxyListProvider(new HideMyNameProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
            })).ConfigureAwait(false);
    }

    private async Task RunOverlapAuditAsync(string providerName, IProxyProvider candidate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var curatedProviders = CreateCuratedRawProviders();
        using var disposableCandidate = candidate;

        try
        {
            var curatedUnion = await FetchUnionAsync(curatedProviders, timeout.Token).ConfigureAwait(false);
            var candidateProxies = (await disposableCandidate.FetchAsync(timeout.Token).ConfigureAwait(false)).ToArray();
            var audit = BuildAudit(candidateProxies, curatedUnion);

            testLogger.WriteLine(LogKind.Default,
                $"proxy overlap audit {providerName}: candidateTotal={audit.CandidateEndpointCount}, candidateHosts={audit.CandidateHostCount}, additiveEndpoints={audit.AdditiveEndpointCount}, additiveHosts={audit.AdditiveHostCount}");

            if (audit.SampleAdditiveEndpoints.Length > 0)
            {
                testLogger.WriteLine(LogKind.Default,
                    $"proxy overlap audit {providerName} sample additive endpoints: {string.Join(", ", audit.SampleAdditiveEndpoints)}");
            }

            Assert.That(candidateProxies, Is.Not.Empty, "Candidate provider должен вернуть хотя бы часть публичного inventory для overlap-аудита.");
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.Ignore($"Overlap audit skipped due to upstream throttling: {exception.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            Assert.Ignore("Overlap audit skipped because the live provider inventory exceeded the diagnostic timeout.");
        }
        finally
        {
            DisposeProviders(curatedProviders);
        }
    }

    private static IProxyProvider[] CreateCuratedRawProviders()
    {
        return
        [
            new OpenProxyListProvider(new OpenProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
            new ProxiflyProxyListProvider(new ProxiflyProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "all",
            }),
            new IplocateProxyListProvider(new IplocateProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
            new R00teeProxyListProvider(new R00teeProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
            new VakhovProxyListProvider(new VakhovProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
            new GfpcomProxyListProvider(new GfpcomProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
            new ZaeemProxyListProvider(new ZaeemProxyListProviderOptions
            {
                RequestsPerSecondLimit = 1,
                Protocol = "https",
            }),
        ];
    }

    private static async Task<ServiceProxy[]> FetchUnionAsync(IProxyProvider[] providers, CancellationToken cancellationToken)
    {
        var result = new List<ServiceProxy>();
        for (var index = 0; index < providers.Length; index++)
        {
            var proxies = await providers[index].FetchAsync(cancellationToken).ConfigureAwait(false);
            result.AddRange(proxies);
        }

        return [.. result];
    }

    private static OverlapAuditResult BuildAudit(IEnumerable<ServiceProxy> candidate, IEnumerable<ServiceProxy> baseline)
    {
        var baselineEndpointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baselineHostKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in baseline)
        {
            if (TryCreateEndpointKey(proxy, out var endpointKey))
            {
                baselineEndpointKeys.Add(endpointKey);
            }

            if (TryCreateLiteralHostKey(proxy, out var literalHostKey))
            {
                baselineHostKeys.Add(literalHostKey);
            }
        }

        var candidateEndpointCount = 0;
        var candidateHostKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additiveEndpointCount = 0;
        var additiveHostKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sampleAdditiveEndpoints = new List<string>(capacity: 8);
        foreach (var proxy in candidate)
        {
            if (TryCreateEndpointKey(proxy, out var endpointKey))
            {
                candidateEndpointCount++;
                if (!baselineEndpointKeys.Contains(endpointKey))
                {
                    additiveEndpointCount++;
                    if (sampleAdditiveEndpoints.Count < 8)
                    {
                        sampleAdditiveEndpoints.Add(endpointKey);
                    }
                }
            }

            if (TryCreateLiteralHostKey(proxy, out var literalHostKey))
            {
                candidateHostKeys.Add(literalHostKey);
                if (!baselineHostKeys.Contains(literalHostKey))
                {
                    additiveHostKeys.Add(literalHostKey);
                }
            }
        }

        return new OverlapAuditResult(
            candidateEndpointCount,
            candidateHostKeys.Count,
            additiveEndpointCount,
            additiveHostKeys.Count,
            [.. sampleAdditiveEndpoints]);
    }

    private static bool TryCreateEndpointKey(ServiceProxy proxy, out string key)
    {
        if (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port <= 0)
        {
            key = string.Empty;
            return false;
        }

        key = $"{proxy.Host}:{proxy.Port}:{proxy.Type}";
        return true;
    }

    private static bool TryCreateLiteralHostKey(ServiceProxy proxy, out string key)
    {
        var host = proxy.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            key = string.Empty;
            return false;
        }

        var candidate = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        if (IPAddress.TryParse(candidate, out var address))
        {
            key = address.ToString();
            return true;
        }

        key = host;
        return true;
    }

    private static void DisposeProviders(IProxyProvider[] providers)
    {
        for (var index = 0; index < providers.Length; index++)
        {
            providers[index].Dispose();
        }
    }

    private sealed record OverlapAuditResult(
        int CandidateEndpointCount,
        int CandidateHostCount,
        int AdditiveEndpointCount,
        int AdditiveHostCount,
        string[] SampleAdditiveEndpoints);
}