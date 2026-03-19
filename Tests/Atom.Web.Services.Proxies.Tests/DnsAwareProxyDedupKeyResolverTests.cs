using System.Net;
using System.Net.Sockets;
using System.Threading;
using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services.Tests;

public class DnsAwareProxyDedupKeyResolverTests(ILogger logger) : BenchmarkTests<DnsAwareProxyDedupKeyResolverTests>(logger)
{
    public DnsAwareProxyDedupKeyResolverTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "DNS-aware resolver канонизирует IP literal без DNS"), Benchmark]
    public async Task CanonicalizesIpLiteralWithoutDnsTest()
    {
        var resolver = new DnsAwareProxyDedupKeyResolver(static (_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var proxy = new ServiceProxy { Host = "[2001:0db8:0000:0000:0000:ff00:0042:8329]", Port = 8080, Type = ProxyType.Http };

        var key = await resolver.GetKeyAsync(proxy);

        Assert.That(key, Is.EqualTo("2001:db8::ff00:42:8329"));
    }

    [TestCase(TestName = "DNS-aware resolver использует single-address DNS key"), Benchmark]
    public async Task UsesSingleAddressDnsKeyTest()
    {
        var resolver = new DnsAwareProxyDedupKeyResolver(static (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }));
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var key = await resolver.GetKeyAsync(proxy);

        Assert.That(key, Is.EqualTo("203.0.113.7"));
    }

    [TestCase(TestName = "DNS-aware resolver оставляет multi-address host на raw host по умолчанию"), Benchmark]
    public async Task LeavesMultiAddressHostAsRawHostByDefaultTest()
    {
        var resolver = new DnsAwareProxyDedupKeyResolver(static (_, _) => Task.FromResult(new[]
        {
            IPAddress.Parse("203.0.113.7"),
            IPAddress.Parse("2001:db8::1"),
        }));
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var key = await resolver.GetKeyAsync(proxy);

        Assert.That(key, Is.EqualTo("edge-a.test"));
    }

    [TestCase(TestName = "DNS-aware resolver может использовать set-based key для multi-address host"), Benchmark]
    public async Task UsesSetBasedKeyForMultiAddressHostWhenEnabledTest()
    {
        var resolver = new DnsAwareProxyDedupKeyResolver(static (_, _) => Task.FromResult(new[]
        {
            IPAddress.Parse("2001:db8::1"),
            IPAddress.Parse("203.0.113.7"),
            IPAddress.Parse("203.0.113.7"),
        }))
        {
            UseMultiAddressKeys = true,
        };
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var key = await resolver.GetKeyAsync(proxy);

        Assert.That(key, Is.EqualTo("2001:db8::1|203.0.113.7"));
    }

    [TestCase(TestName = "DNS-aware resolver не кэширует transient failure fallback"), Benchmark]
    public async Task DoesNotCacheTransientFailureFallbackTest()
    {
        var resolveCount = 0;
        var resolver = new DnsAwareProxyDedupKeyResolver((_, _) =>
        {
            resolveCount++;
            if (resolveCount == 1)
            {
                throw new SocketException();
            }

            return Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") });
        });
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var firstKey = await resolver.GetKeyAsync(proxy);
        var secondKey = await resolver.GetKeyAsync(proxy);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstKey, Is.EqualTo("edge-a.test"));
            Assert.That(secondKey, Is.EqualTo("203.0.113.7"));
            Assert.That(resolveCount, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "DNS-aware resolver не кэширует ответы при zero TTL"), Benchmark]
    public async Task DoesNotCacheWhenEntryLifetimeIsZeroTest()
    {
        var resolveCount = 0;
        var resolver = new DnsAwareProxyDedupKeyResolver((_, _) =>
        {
            resolveCount++;
            return Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") });
        })
        {
            EntryLifetime = TimeSpan.Zero,
        };
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var firstKey = await resolver.GetKeyAsync(proxy);
        var secondKey = await resolver.GetKeyAsync(proxy);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstKey, Is.EqualTo("203.0.113.7"));
            Assert.That(secondKey, Is.EqualTo("203.0.113.7"));
            Assert.That(resolveCount, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "DNS-aware resolver использует single-flight для параллельного hostname"), Benchmark]
    public async Task UsesSingleFlightForConcurrentRequestsTest()
    {
        var resolveCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new DnsAwareProxyDedupKeyResolver(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref resolveCount);
            await gate.Task.WaitAsync(cancellationToken);
            return [IPAddress.Parse("203.0.113.7")];
        });
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var first = resolver.GetKeyAsync(proxy).AsTask();
        var second = resolver.GetKeyAsync(proxy).AsTask();
        var third = resolver.GetKeyAsync(proxy).AsTask();

        gate.SetResult();

        var keys = await Task.WhenAll(first, second, third);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Is.All.EqualTo("203.0.113.7"));
            Assert.That(resolveCount, Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "DNS-aware resolver делает raw-host fallback при null результате resolver"), Benchmark]
    public async Task FallsBackToRawHostWhenResolverReturnsNullTest()
    {
        var resolveCount = 0;
        var resolver = new DnsAwareProxyDedupKeyResolver((_, _) =>
        {
            resolveCount++;
            return Task.FromResult<IPAddress[]?>(null)!;
        });
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        var firstKey = await resolver.GetKeyAsync(proxy);
        var secondKey = await resolver.GetKeyAsync(proxy);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstKey, Is.EqualTo("edge-a.test"));
            Assert.That(secondKey, Is.EqualTo("edge-a.test"));
            Assert.That(resolveCount, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "DNS-aware resolver сохраняет single-flight после отмены одного waiter"), Benchmark]
    public async Task PreservesSingleFlightWhenOneWaiterIsCancelledTest()
    {
        var resolveCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new DnsAwareProxyDedupKeyResolver(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref resolveCount);
            await gate.Task.WaitAsync(cancellationToken);
            return [IPAddress.Parse("203.0.113.7")];
        });
        var proxy = new ServiceProxy { Host = "edge-a.test", Port = 8080, Type = ProxyType.Http };

        using var cancelledCaller = new CancellationTokenSource();
        var first = resolver.GetKeyAsync(proxy, cancelledCaller.Token).AsTask();

        while (Volatile.Read(ref resolveCount) == 0)
        {
            await Task.Yield();
        }

        await cancelledCaller.CancelAsync();
        await Assert.ThatAsync(async () => await first, Throws.TypeOf<OperationCanceledException>());

        var second = resolver.GetKeyAsync(proxy).AsTask();
        gate.SetResult();

        var secondKey = await second;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondKey, Is.EqualTo("203.0.113.7"));
            Assert.That(resolveCount, Is.EqualTo(1));
        }
    }
}