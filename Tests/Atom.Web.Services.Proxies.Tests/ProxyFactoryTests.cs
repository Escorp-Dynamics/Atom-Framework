using System.Net;
using Atom.Architect.Components;
using Atom.Architect.Factories;
using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyFactoryTests(ILogger logger) : BenchmarkTests<ProxyFactoryTests>(logger)
{
    public ProxyFactoryTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Фабрика агрегирует пул и применяет критерии"), Benchmark]
    public async Task AggregatePoolAndFilterTest()
    {
        using var factory = new ProxyFactory();
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http, Anonymity = AnonymityLevel.Medium },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
        ]));
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http, Anonymity = AnonymityLevel.Low },
        ]));

        var all = (await factory.GetAsync(count: 10)).ToArray();
        var filtered = (await factory.GetAsync(count: 10, proxy => proxy.Type == ProxyType.Https && proxy.Anonymity == AnonymityLevel.High)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(all, Has.Length.EqualTo(3));
            Assert.That(filtered, Has.Length.EqualTo(1));
            Assert.That(filtered[0].Host, Is.EqualTo("10.0.0.2"));
        }
    }

    [TestCase(TestName = "Фабрика возвращает следующий прокси по кругу"), Benchmark]
    public async Task CycleNextProxyTest()
    {
        using var factory = new ProxyFactory();
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));

        var first = await factory.GetAsync();
        var second = await factory.GetAsync();
        var third = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(second.Host, Is.EqualTo("10.0.0.2"));
            Assert.That(third.Host, Is.EqualTo("10.0.0.1"));
        }
    }

    [TestCase(TestName = "Container round-robin работает поверх aggregate snapshot, а не service rotation"), Benchmark]
    public async Task ContainerRotationUsesAggregateSnapshotTest()
    {
        using var factory = new ProxyFactory
        {
            RotationStrategy = ProxyRotationStrategy.RoundRobin,
            ServiceRotationStrategy = ProxyRotationStrategy.Random,
        };

        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8082, Type = ProxyType.Http },
        ]));

        var first = await factory.GetAsync();
        var second = await factory.GetAsync();
        var third = await factory.GetAsync();
        var fourth = await factory.GetAsync();

        Assert.That(
            new[] { first.Host, second.Host, third.Host, fourth.Host },
            Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2", "10.0.1.1", "10.0.0.1" }));
    }

    [TestCase(TestName = "Фабрика валидирует пул через валидаторы провайдера"), Benchmark]
    public async Task ValidatePoolTest()
    {
        using var factory = new ProxyFactory();

        var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.10", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.11", Port = 8080, Type = ProxyType.Http },
        ]);
        provider.UseValidator(new AllowHostValidator("10.0.0.11"));

        factory.UseProvider(provider);

        var validated = (await factory.GetValidatedPoolAsync(new Uri("https://example.com/health"))).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(validated, Has.Length.EqualTo(1));
            Assert.That(validated[0].Host, Is.EqualTo("10.0.0.11"));
        }
    }

    [TestCase(TestName = "Фабрика подключает провайдера как component owner"), Benchmark]
    public void FactoryAttachesAndDetachesProviderTest()
    {
        using var factory = new ProxyFactory();
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.10", Port = 8080, Type = ProxyType.Http },
        ]);

        factory.UseProvider(provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Owner, Is.SameAs(factory));
            Assert.That(((IComponent)provider).IsAttached, Is.True);
        }

        factory.UnUse(provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Owner, Is.Null);
            Assert.That(((IComponent)provider).IsAttached, Is.False);
        }
    }

    [TestCase(TestName = "Провайдер и фабрика реализуют IAsyncFactory для одного proxy"), Benchmark]
    public async Task ProviderAndFactoryImplementAsyncFactoryContractTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]);
        using var factory = new ProxyFactory();
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http },
        ]));

        IAsyncFactory<ServiceProxy> providerFactory = provider;
        IAsyncFactory<ServiceProxy> aggregateFactory = factory;

        var provided = await providerFactory.GetAsync();
        var aggregated = await aggregateFactory.GetAsync();

        providerFactory.Return(provided);
        aggregateFactory.Return(aggregated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provided.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(aggregated.Host, Is.EqualTo("10.0.1.1"));
        }
    }

    private sealed class FakeProxyProvider(IEnumerable<ServiceProxy> proxies) : ProxyProvider
    {
        protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(proxies);
    }

    private sealed class FakeRefreshingProxyProvider(params IEnumerable<ServiceProxy>[] refreshBatches) : ProxyProvider
    {
        private int refreshIndex = -1;

        protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
        {
            var nextIndex = Interlocked.Increment(ref refreshIndex);
            return ValueTask.FromResult(refreshBatches[Math.Min(nextIndex, refreshBatches.Length - 1)]);
        }
    }

    [TestCase(TestName = "Сервис автоматически наполняет и обновляет пул"), Benchmark]
    public async Task ServiceAutoFillAndRefreshTest()
    {
        using var provider = new FakeRefreshingProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ],
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Http },
        ]);

        provider.RefreshInterval = TimeSpan.FromHours(1);

        var first = await provider.GetAsync();
        var batch = (await provider.GetAsync(count: 2)).ToArray();
        await provider.RefreshAsync();
        var refreshed = await provider.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(batch.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.2", "10.0.0.1" }));
            Assert.That(refreshed.Host, Is.EqualTo("10.0.0.3"));
            Assert.That(provider.PoolCount, Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "Провайдер поддерживает filtered и counted overload GetAsync"), Benchmark]
    public async Task ServiceFilterAndCountOverloadsTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http, Anonymity = AnonymityLevel.Low },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
        ]);

        provider.RefreshInterval = TimeSpan.FromHours(1);

        var filteredSingle = await provider.GetAsync(proxy => proxy.Type == ProxyType.Https && proxy.Anonymity == AnonymityLevel.High);
        var filteredBatch = (await provider.GetAsync(2, proxy => proxy.Type == ProxyType.Https)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filteredSingle.Host, Is.EqualTo("10.0.0.2"));
            Assert.That(filteredBatch.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.3", "10.0.0.2" }));
        }
    }

    [TestCase(TestName = "Провайдер не сохраняет дубликаты IP в пуле"), Benchmark]
    public async Task ProviderPoolDeduplicatesHostsTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 3128, Type = ProxyType.Https },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]);

        provider.RefreshInterval = TimeSpan.FromHours(1);

        var pool = (await provider.GetAsync(10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(2));
            Assert.That(pool.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2" }));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Провайдер использует single-flight refresh для параллельных запросов"), Benchmark]
    public async Task ProviderUsesSingleFlightRefreshTest()
    {
        using var provider = new SingleFlightProxyProvider(
            TimeSpan.FromMilliseconds(120),
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }]);

        provider.RefreshInterval = TimeSpan.FromHours(1);

        var firstTask = provider.GetAsync();
        var secondTask = provider.GetAsync();
        var thirdTask = provider.GetAsync();

        await Task.WhenAll(firstTask.AsTask(), secondTask.AsTask(), thirdTask.AsTask());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.LoadCount, Is.EqualTo(1));
            Assert.That(firstTask.Result.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(secondTask.Result.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(thirdTask.Result.Host, Is.EqualTo("10.0.0.1"));
        }
    }

    [TestCase(TestName = "PreferFresh выбирает самый свежий proxy"), Benchmark]
    public async Task PreferFreshStrategyTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http, Alive = new DateTime(2026, 3, 17, 10, 0, 0, DateTimeKind.Utc), Uptime = 50 },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http, Alive = new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Utc), Uptime = 40 },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Http, Alive = new DateTime(2026, 3, 18, 11, 0, 0, DateTimeKind.Utc), Uptime = 90 },
        ]);

        provider.RotationStrategy = ProxyRotationStrategy.PreferFresh;

        var proxy = await provider.GetAsync();
        var batch = (await provider.GetAsync(2)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxy.Host, Is.EqualTo("10.0.0.3"));
            Assert.That(batch.Select(item => item.Host), Is.EqualTo(new[] { "10.0.0.3", "10.0.0.2" }));
        }
    }

    [TestCase(TestName = "Random стратегия возвращает batch без дубликатов"), Benchmark]
    public async Task RandomStrategyReturnsUniqueBatchTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Http },
        ]);

        provider.RotationStrategy = ProxyRotationStrategy.Random;

        var batch = (await provider.GetAsync(3)).Select(proxy => proxy.Host).ToArray();

        Assert.That(batch, Is.EquivalentTo(new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" }));
    }

    [TestCase(TestName = "При ошибке refresh сохраняется последний успешный пул"), Benchmark]
    public async Task PreservePoolOnRefreshFailureTest()
    {
        using var provider = new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable"));

        await provider.RefreshAsync();
        var beforeFailure = await provider.GetAsync();
        await provider.RefreshAsync();
        var afterFailure = await provider.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeFailure.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(afterFailure.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(provider.LastRefreshException, Is.Not.Null);
        }
    }

    [TestCase(TestName = "Контейнер применяет reliability settings к провайдерам"), Benchmark]
    public void FactoryPropagatesReliabilitySettingsTest()
    {
        using var factory = new ProxyFactory
        {
            RefreshInterval = TimeSpan.FromMinutes(2),
            RefreshErrorBackoff = TimeSpan.FromSeconds(11),
            PreservePoolOnRefreshFailure = false,
            ServiceRotationStrategy = ProxyRotationStrategy.PreferFresh,
        };

        var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]);

        factory.UseProvider(provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RefreshInterval, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(provider.RefreshErrorBackoff, Is.EqualTo(TimeSpan.FromSeconds(11)));
            Assert.That(provider.PreservePoolOnRefreshFailure, Is.False);
            Assert.That(provider.RotationStrategy, Is.EqualTo(ProxyRotationStrategy.PreferFresh));
        }
    }

    [TestCase(TestName = "Контейнер позволяет локально переопределять настройки провайдера"), Benchmark]
    public void FactoryAllowsPerServiceOverridesTest()
    {
        using var factory = new ProxyFactory
        {
            RefreshInterval = TimeSpan.FromMinutes(2),
            RefreshErrorBackoff = TimeSpan.FromSeconds(11),
            PreservePoolOnRefreshFailure = false,
            ServiceRotationStrategy = ProxyRotationStrategy.RoundRobin,
        };

        var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]);

        factory.Use(provider, configuredProvider =>
        {
            configuredProvider.RefreshInterval = TimeSpan.FromSeconds(5);
            configuredProvider.PreservePoolOnRefreshFailure = true;
            configuredProvider.RotationStrategy = ProxyRotationStrategy.PreferFresh;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RefreshInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(provider.RefreshErrorBackoff, Is.EqualTo(TimeSpan.FromSeconds(11)));
            Assert.That(provider.PreservePoolOnRefreshFailure, Is.True);
            Assert.That(provider.RotationStrategy, Is.EqualTo(ProxyRotationStrategy.PreferFresh));
        }
    }

    [TestCase(TestName = "Фабрика не сохраняет дубликаты IP между провайдерами"), Benchmark]
    public async Task FactoryPoolDeduplicatesHostsAcrossProvidersTest()
    {
        using var factory = new ProxyFactory();
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.0.1", Port = 3128, Type = ProxyType.Https },
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8082, Type = ProxyType.Http },
        ]));

        var pool = (await factory.GetAsync(count: 10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(3));
            Assert.That(pool.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2", "10.0.1.1" }));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Фабрика канонизирует IP при дедупликации пула"), Benchmark]
    public async Task FactoryPoolDeduplicatesCanonicalIpFormsTest()
    {
        using var factory = new ProxyFactory();
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "[2001:0db8:0000:0000:0000:ff00:0042:8329]", Port = 8080, Type = ProxyType.Http },
        ]));
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "[2001:db8::ff00:42:8329]", Port = 3128, Type = ProxyType.Https },
            new ServiceProxy { Provider = "ProxyScrape", Host = "[2001:db8::1]", Port = 8082, Type = ProxyType.Http },
        ]));

        var pool = (await factory.GetAsync(count: 10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(2));
            Assert.That(pool.Select(proxy => proxy.Host), Is.EqualTo(new[] { "[2001:db8::ff00:42:8329]", "[2001:db8::1]" }));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Фабрика использует async dedup resolver для aggregate pool"), Benchmark]
    public async Task FactoryUsesAsyncDedupResolverForAggregatePoolTest()
    {
        using var factory = new ProxyFactory
        {
            DedupKeyResolver = new FakeAsyncDedupKeyResolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["edge-a.test"] = "203.0.113.7",
                ["203.0.113.7"] = "203.0.113.7",
            }),
        };

        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "edge-a.test", Port = 8080, Type = ProxyType.Http },
        ]));
        factory.UseProvider(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "203.0.113.7", Port = 3128, Type = ProxyType.Https },
        ]));

        var pool = (await factory.GetAsync(count: 10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(1));
            Assert.That(pool[0].Host, Is.EqualTo("edge-a.test"));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Фабрика прокидывает dedup resolver в attached provider"), Benchmark]
    public async Task FactoryPropagatesDedupResolverToProviderTest()
    {
        using var factory = new ProxyFactory
        {
            DedupKeyResolver = new FakeAsyncDedupKeyResolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["edge-a.test"] = "203.0.113.7",
                ["203.0.113.7"] = "203.0.113.7",
            }),
        };

        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "edge-a.test", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "203.0.113.7", Port = 3128, Type = ProxyType.Https },
        ]);

        factory.UseProvider(provider);
        provider.RefreshInterval = TimeSpan.FromHours(1);

        var pool = (await provider.GetAsync(10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(1));
            Assert.That(pool[0].Host, Is.EqualTo("edge-a.test"));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Контейнер сохраняет порядок провайдеров при параллельном сборе"), Benchmark]
    public async Task FactoryPreservesProviderOrderWhenCollectingInParallelTest()
    {
        using var factory = new ProxyFactory();
        factory.UseProvider(new DelayedProxyProvider(
            TimeSpan.FromMilliseconds(120),
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }]));
        factory.UseProvider(new DelayedProxyProvider(
            TimeSpan.FromMilliseconds(10),
            [new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http }]));

        var batch = (await factory.GetAsync(count: 10)).Select(proxy => proxy.Host).ToArray();

        Assert.That(batch, Is.EqualTo(new[] { "10.0.0.1", "10.0.1.1" }));
    }

    private sealed class AllowHostValidator(string host) : IProxyValidator
    {
        public TimeSpan Speed { get; set; } = TimeSpan.FromSeconds(5);

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken)
            => ValueTask.FromResult(string.Equals(proxy.Host, host, StringComparison.Ordinal));
    }

    private sealed class FaultingProxyProvider(IEnumerable<ServiceProxy> firstBatch, Exception refreshException) : ProxyProvider
    {
        private int attempt;

        protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref attempt) == 1)
            {
                return ValueTask.FromResult(firstBatch);
            }

            return ValueTask.FromException<IEnumerable<ServiceProxy>>(refreshException);
        }
    }

    private sealed class DelayedProxyProvider(TimeSpan delay, IEnumerable<ServiceProxy> proxies) : ProxyProvider
    {
        protected override async ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return proxies;
        }
    }

    private sealed class SingleFlightProxyProvider(TimeSpan delay, IEnumerable<ServiceProxy> proxies) : ProxyProvider
    {
        private int loadCount;

        public int LoadCount => Volatile.Read(ref loadCount);

        protected override async ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref loadCount);
            await Task.Delay(delay, cancellationToken);
            return proxies;
        }
    }

    private sealed class FakeAsyncDedupKeyResolver(IReadOnlyDictionary<string, string> mapping) : IProxyDedupKeyResolver
    {
        public ValueTask<string> GetKeyAsync(ServiceProxy proxy, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(proxy);

            var host = proxy.Host ?? string.Empty;
            return ValueTask.FromResult(mapping.TryGetValue(host, out var resolved) ? resolved : host);
        }
    }
}