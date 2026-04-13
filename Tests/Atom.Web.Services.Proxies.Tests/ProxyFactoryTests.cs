using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Atom.Architect.Components;
using Atom.Architect.Factories;
using Atom.Net.Proxies;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyFactoryTests(ILogger logger) : BenchmarkTests<ProxyFactoryTests>(logger)
{
    public ProxyFactoryTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Фабрика агрегирует пул и применяет критерии"), Benchmark]
    public async Task AggregatePoolAndFilterTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http, Anonymity = AnonymityLevel.Medium },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
        ]));
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http, Anonymity = AnonymityLevel.Low },
        ]));

        var all = (await factory.GetAsync(count: 10)).ToArray();
        foreach (var proxy in all)
        {
            factory.Return(proxy);
        }

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
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));

        var first = await factory.GetAsync();
        factory.Return(first);
        var second = await factory.GetAsync();
        factory.Return(second);
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
        };

        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8082, Type = ProxyType.Http },
        ]));

        var first = await factory.GetAsync();
        factory.Return(first);
        var second = await factory.GetAsync();
        factory.Return(second);
        var third = await factory.GetAsync();
        factory.Return(third);
        var fourth = await factory.GetAsync();

        Assert.That(
            new[] { first.Host, second.Host, third.Host, fourth.Host },
            Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2", "10.0.1.1", "10.0.0.1" }));
    }

    [TestCase(TestName = "Фабрика подключает провайдера как component owner"), Benchmark]
    public void FactoryAttachesAndDetachesProviderTest()
    {
        using var factory = new ProxyFactory();
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.10", Port = 8080, Type = ProxyType.Http },
        ]);

        factory.Use(provider);

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

    [TestCase(TestName = "Провайдер возвращает fetch-only snapshot, фабрика реализует IAsyncFactory"), Benchmark]
    public async Task ProviderFetchesSnapshotAndFactoryImplementsAsyncFactoryContractTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]);
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http },
        ]));

        IAsyncFactory<ServiceProxy> aggregateFactory = factory;

        var provided = (await provider.FetchAsync()).Single();
        var aggregated = await aggregateFactory.GetAsync();

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

    private sealed class FakePagedProxyProvider(params ProxyProviderFetchPage[] pages) : ProxyProvider, IProxyPagedProvider
    {
        public ValueTask<ProxyProviderFetchPage> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken)
        {
            var index = string.IsNullOrWhiteSpace(continuationToken)
                ? 0
                : int.Parse(continuationToken, System.Globalization.CultureInfo.InvariantCulture);
            return ValueTask.FromResult(pages[index]);
        }

        protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
            => ValueTask.FromException<IEnumerable<ServiceProxy>>(new InvalidOperationException("Paged provider should use FetchPageAsync."));
    }

    private sealed class FakeTargetedProxyProvider(IEnumerable<ServiceProxy> proxies) : ProxyProvider, IProxyTargetedProvider
    {
        public ValueTask<ProxyProviderFetchResult> FetchAsync(ProxyProviderFetchRequest request, CancellationToken cancellationToken)
        {
            var filtered = proxies
                .Where(proxy => request.Protocols.Count == 0 || request.Protocols.Contains(proxy.Type))
                .Where(proxy => request.AnonymityLevels.Count == 0 || request.AnonymityLevels.Contains(proxy.Anonymity))
                .Take(request.RequestedCount)
                .ToArray();

            return ValueTask.FromResult(new ProxyProviderFetchResult(filtered, IsPartial: filtered.Length < request.RequestedCount));
        }

        protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
            => ValueTask.FromException<IEnumerable<ServiceProxy>>(new InvalidOperationException("Targeted provider should use targeted fetch path."));
    }

    [TestCase(TestName = "Провайдер возвращает свежий snapshot на каждом fetch"), Benchmark]
    public async Task ProviderFetchReturnsFreshSnapshotEachCallTest()
    {
        using var provider = new FakeRefreshingProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ],
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Http },
        ]);

        var first = (await provider.FetchAsync()).Select(static proxy => proxy.Host).ToArray();
        var refreshed = (await provider.FetchAsync()).Select(static proxy => proxy.Host).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2" }));
            Assert.That(refreshed, Is.EqualTo(new[] { "10.0.0.3" }));
        }
    }

    [TestCase(TestName = "Провайдер может собрать полный snapshot через continuation pages"), Benchmark]
    public async Task ProviderFetchCollectsPagedSourceUntilContinuationEndsTest()
    {
        using var provider = new FakePagedProxyProvider(
            new ProxyProviderFetchPage(
            [
                new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            ],
            "1"),
            new ProxyProviderFetchPage(
            [
                new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
            ],
            "2"),
            new ProxyProviderFetchPage(
            [
                new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Http },
            ]));

        var snapshot = (await provider.FetchAsync()).Select(static proxy => proxy.Host).ToArray();

        Assert.That(snapshot, Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" }));
    }

    [TestCase(TestName = "Фабрика может использовать targeted fetch на cold-start без полного snapshot"), Benchmark]
    public async Task FactoryUsesTargetedFetchOnColdStartTest()
    {
        using var factory = new ProxyFactory
        {
            AllowedProtocols = [ProxyType.Https],
        };

        factory.Use(new FakeTargetedProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 9443, Type = ProxyType.Https },
        ]));

        var batch = (await factory.GetAsync(2)).Select(static proxy => proxy.Host).ToArray();

        Assert.That(batch, Is.EqualTo(new[] { "10.0.0.2", "10.0.0.3" }));
    }

    [TestCase(TestName = "Фабрика публикует Count для deduped aggregate snapshot под текущими фильтрами"), Benchmark]
    public async Task FactoryCountReflectsFilteredAggregateSnapshotTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https },
        ]));

        var leased = (await factory.GetAsync(10)).ToArray();
        foreach (var proxy in leased)
        {
            factory.Return(proxy);
        }

        await WaitForAsync(() => factory.Count == 2).ConfigureAwait(false);

        factory.AllowedProtocols = [ProxyType.Https];
        await WaitForAsync(() => factory.Count == 1).ConfigureAwait(false);

        Assert.That(factory.Count, Is.EqualTo(1));
    }

    [TestCase(TestName = "Фабрика публикует ключевые counters и up-down metrics через MeterFactory"), Benchmark]
    public async Task FactoryPublishesMetricsViaMeterFactoryTest()
    {
        var longMeasurements = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        var intMeasurements = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Escorp.Atom.Web.Services.Proxies.ProxyFactory")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            longMeasurements.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
        });
        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
        {
            intMeasurements.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
        });
        listener.Start();

        using var meterFactory = new TestMeterFactory();
        using var factory = new ProxyFactory
        {
            MeterFactory = meterFactory,
        };
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]));

        var leased = await factory.GetAsync();
        factory.Return(leased);

        await WaitForAsync(() =>
            longMeasurements.GetValueOrDefault("proxy.factory.rebuild") >= 1
            && longMeasurements.GetValueOrDefault("proxy.factory.lease.granted") >= 1
            && longMeasurements.GetValueOrDefault("proxy.factory.lease.released") >= 1
            && intMeasurements.GetValueOrDefault("proxy.factory.count.active") >= 1
            && intMeasurements.GetValueOrDefault("proxy.factory.count.providers") >= 1).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(longMeasurements.GetValueOrDefault("proxy.factory.rebuild"), Is.GreaterThanOrEqualTo(1));
            Assert.That(longMeasurements.GetValueOrDefault("proxy.factory.lease.granted"), Is.EqualTo(1));
            Assert.That(longMeasurements.GetValueOrDefault("proxy.factory.lease.released"), Is.EqualTo(1));
            Assert.That(intMeasurements.GetValueOrDefault("proxy.factory.count.active"), Is.EqualTo(1));
            Assert.That(intMeasurements.GetValueOrDefault("proxy.factory.count.providers"), Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "Фабрика логирует targeted path и cleanup flows"), Benchmark]
    public async Task FactoryLogsDiagnosticFlowsTest()
    {
        using var logger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = logger,
            AllowedProtocols = [ProxyType.Https],
        };
        factory.Use(new FakeTargetedProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https },
        ]));

        var leased = await factory.GetAsync();
        var cleaned = factory.CleanupLeasedProxies([leased]);

        await WaitForAsync(() =>
            logger.Messages.Any(static message => message.Contains("адресного холодного старта", StringComparison.Ordinal))
            && logger.Messages.Any(static message => message.Contains("вручную очистила", StringComparison.Ordinal))).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleaned, Is.EqualTo(1));
            Assert.That(logger.Messages.Any(static message => message.Contains("адресного холодного старта", StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Messages.Any(static message => message.Contains("вручную очистила", StringComparison.Ordinal)), Is.True);
        }
    }

    [TestCase(TestName = "Фабрика использует зарезервированные event id и ожидаемые уровни логов"), Benchmark]
    public async Task FactoryUsesReservedEventIdsAndExpectedLogLevelsTest()
    {
        using var logger = new TestLogger();
        using var warningLogger = new TestLogger();

        using (var factory = new ProxyFactory
        {
            Logger = logger,
            AllowedProtocols = [ProxyType.Https],
        })
        {
            factory.Use(new FakeTargetedProxyProvider(
            [
                new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8443, Type = ProxyType.Https },
            ]));

            var leased = await factory.GetAsync();
            factory.CleanupLeasedProxies([leased]);

            await WaitForAsync(() => logger.Entries.Count >= 2).ConfigureAwait(false);
        }

        using (var factory = new ProxyFactory
        {
            Logger = warningLogger,
            RefreshInterval = TimeSpan.FromMilliseconds(20),
            RefreshErrorBackoff = TimeSpan.Zero,
            PreservePoolOnRefreshFailure = true,
        })
        {
            factory.Use(new FaultingProxyProvider(
                [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
                new InvalidOperationException("remote feed unavailable")));

            var leased = await factory.GetAsync();
            factory.Return(leased);

            await WaitForAsync(() => warningLogger.Entries.Any(static entry => entry.Message.Contains("сохранён", StringComparison.Ordinal))).ConfigureAwait(false);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries, Is.Not.Empty);
            Assert.That(logger.Entries.All(static entry => entry.EventId.Id is >= 1000 and < 1100), Is.True);
            Assert.That(logger.Entries.Any(static entry => entry.EventId.Id == 1000 && entry.Level == Microsoft.Extensions.Logging.LogLevel.Debug && entry.Message.Contains("адресного холодного старта", StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Entries.Any(static entry => entry.EventId.Id == 1002 && entry.Level == Microsoft.Extensions.Logging.LogLevel.Debug && entry.Message.Contains("вручную очистила", StringComparison.Ordinal)), Is.True);
            Assert.That(warningLogger.Entries.Any(static entry => entry.EventId.Id == 1006 && entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning), Is.True);
        }
    }

    [TestCase(TestName = "Фабрика логирует attach provider без дублирования"), Benchmark]
    public async Task FactoryLogsSingleAttachMessageTest()
    {
        using var logger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = logger,
        };

        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]));

        _ = await factory.GetAsync();

        await WaitForAsync(() =>
            logger.Messages.Count(static message => message.Contains("подключила провайдера", StringComparison.Ordinal)) == 1).ConfigureAwait(false);

        Assert.That(logger.Messages.Count(static message => message.Contains("подключила провайдера", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [TestCase(TestName = "Фабрика логирует сохранение последнего snapshot после refresh failure"), Benchmark]
    public async Task FactoryLogsPreservedSnapshotOnRefreshFailureTest()
    {
        using var logger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = logger,
            RefreshInterval = TimeSpan.FromMilliseconds(20),
            RefreshErrorBackoff = TimeSpan.Zero,
            PreservePoolOnRefreshFailure = true,
        };
        factory.Use(new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable")));

        var leased = await factory.GetAsync();
        factory.Return(leased);

        await WaitForAsync(() =>
            logger.Messages.Any(static message => message.Contains("сохранён", StringComparison.Ordinal))).ConfigureAwait(false);

        Assert.That(logger.Messages.Any(static message => message.Contains("сохранён", StringComparison.Ordinal)), Is.True);
    }

    [TestCase(TestName = "Фабрика логирует очистку snapshot после refresh failure при disabled preserve"), Benchmark]
    public async Task FactoryLogsClearedSnapshotOnRefreshFailureTest()
    {
        using var logger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = logger,
            RefreshInterval = TimeSpan.FromMilliseconds(20),
            RefreshErrorBackoff = TimeSpan.Zero,
            PreservePoolOnRefreshFailure = false,
        };
        factory.Use(new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable")));

        var leased = await factory.GetAsync();
        factory.Return(leased);

        await WaitForAsync(() =>
            logger.Messages.Any(static message => message.Contains("Снимок будет очищен", StringComparison.Ordinal))).ConfigureAwait(false);

        Assert.That(logger.Messages.Any(static message => message.Contains("Снимок будет очищен", StringComparison.Ordinal)), Is.True);
    }

    [TestCase(TestName = "Провайдер возвращает полный snapshot, фильтрация живёт снаружи"), Benchmark]
    public async Task ProviderFetchReturnsRawSnapshotForCallerSideFilteringTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http, Anonymity = AnonymityLevel.Low },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.3", Port = 8082, Type = ProxyType.Https, Anonymity = AnonymityLevel.High },
        ]);

        var fetched = (await provider.FetchAsync()).ToArray();
        var filteredSingle = fetched.First(proxy => proxy.Type == ProxyType.Https && proxy.Anonymity == AnonymityLevel.High);
        var filteredBatch = fetched.Where(proxy => proxy.Type == ProxyType.Https).Take(2).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filteredSingle.Host, Is.EqualTo("10.0.0.2"));
            Assert.That(filteredBatch.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.2", "10.0.0.3" }));
        }
    }

    [TestCase(TestName = "Провайдер дедуплицирует snapshot при fetch"), Benchmark]
    public async Task ProviderFetchDeduplicatesHostsTest()
    {
        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 3128, Type = ProxyType.Https },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]);

        var pool = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(2));
            Assert.That(pool.Select(proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.1", "10.0.0.2" }));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Провайдер не делит cached state между fetch-вызовами"), Benchmark]
    public async Task ProviderDoesNotShareCachedStateAcrossFetchCallsTest()
    {
        using var provider = new SingleFlightProxyProvider(
            TimeSpan.FromMilliseconds(120),
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }]);

        var firstTask = provider.FetchAsync().AsTask();
        var secondTask = provider.FetchAsync().AsTask();
        var thirdTask = provider.FetchAsync().AsTask();

        await Task.WhenAll(firstTask, secondTask, thirdTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.LoadCount, Is.EqualTo(3));
            Assert.That(firstTask.Result.Single().Host, Is.EqualTo("10.0.0.1"));
            Assert.That(secondTask.Result.Single().Host, Is.EqualTo("10.0.0.1"));
            Assert.That(thirdTask.Result.Single().Host, Is.EqualTo("10.0.0.1"));
        }
    }

    [TestCase(TestName = "Провайдер не кэширует успешный fetch после ошибки источника"), Benchmark]
    public async Task ProviderDoesNotPreserveSuccessfulSnapshotAfterFailureTest()
    {
        using var provider = new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable"));

        var beforeFailure = (await provider.FetchAsync()).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeFailure.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(async () => await provider.FetchAsync(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [TestCase(TestName = "Контейнер сохраняет последний успешный snapshot при ошибке провайдера"), Benchmark]
    public async Task FactoryPreservesLastSuccessfulSnapshotOnRefreshFailureTest()
    {
        using var factory = new ProxyFactory
        {
            RefreshInterval = TimeSpan.FromMilliseconds(20),
            RefreshErrorBackoff = TimeSpan.Zero,
            PreservePoolOnRefreshFailure = true,
        };
        factory.Use(new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable")));

        var first = await factory.GetAsync();
        factory.Return(first);
        await Task.Delay(60);
        var afterFailure = (await factory.GetAsync(10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(afterFailure, Has.Length.EqualTo(1));
            Assert.That(afterFailure[0].Host, Is.EqualTo("10.0.0.1"));
        }
    }

    [TestCase(TestName = "Контейнер очищает snapshot после ошибки провайдера, если preserve выключен"), Benchmark]
    public async Task FactoryClearsSnapshotOnRefreshFailureWhenPreserveDisabledTest()
    {
        using var factory = new ProxyFactory
        {
            RefreshInterval = TimeSpan.FromMilliseconds(20),
            RefreshErrorBackoff = TimeSpan.Zero,
            PreservePoolOnRefreshFailure = false,
        };
        factory.Use(new FaultingProxyProvider(
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }],
            new InvalidOperationException("remote feed unavailable")));

        var first = await factory.GetAsync();
        factory.Return(first);
        await Task.Delay(60);
        var afterFailure = (await factory.GetAsync(10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Host, Is.EqualTo("10.0.0.1"));
            Assert.That(afterFailure, Is.Empty);
        }
    }

    [TestCase(TestName = "Фабрика не сохраняет дубликаты IP между провайдерами"), Benchmark]
    public async Task FactoryPoolDeduplicatesHostsAcrossProvidersTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.2", Port = 8081, Type = ProxyType.Http },
        ]));
        factory.Use(new FakeProxyProvider(
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
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "[2001:0db8:0000:0000:0000:ff00:0042:8329]", Port = 8080, Type = ProxyType.Http },
        ]));
        factory.Use(new FakeProxyProvider(
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

        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "edge-a.test", Port = 8080, Type = ProxyType.Http },
        ]));
        factory.Use(new FakeProxyProvider(
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

        factory.Use(provider);
        var leased = await factory.GetAsync();
        factory.Return(leased);

        var pool = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Length.EqualTo(1));
            Assert.That(pool[0].Host, Is.EqualTo("edge-a.test"));
            Assert.That(pool[0].Port, Is.EqualTo(8080));
        }
    }

    [TestCase(TestName = "Фабрика прокидывает logger в provider при первом runtime-use, если у него ещё нет своего"), Benchmark]
    public async Task FactoryPropagatesLoggerToProviderWhenMissingTest()
    {
        using var factoryLogger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = factoryLogger,
        };

        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]);

        factory.Use(provider);

        var leased = await factory.GetAsync();
        factory.Return(leased);

        Assert.That(provider.Logger, Is.SameAs(factoryLogger));
    }

    [TestCase(TestName = "Фабрика не перезаписывает logger, явно заданный provider-у, при первом runtime-use"), Benchmark]
    public async Task FactoryKeepsExplicitProviderLoggerTest()
    {
        using var factoryLogger = new TestLogger();
        using var providerLogger = new TestLogger();
        using var factory = new ProxyFactory
        {
            Logger = factoryLogger,
        };

        using var provider = new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ])
        {
            Logger = providerLogger,
        };

        factory.Use(provider);

        var leased = await factory.GetAsync();
        factory.Return(leased);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Logger, Is.SameAs(providerLogger));
            Assert.That(provider.Logger, Is.Not.SameAs(factoryLogger));
        }
    }

    [TestCase(TestName = "Контейнер сохраняет порядок провайдеров при параллельном сборе"), Benchmark]
    public async Task FactoryPreservesProviderOrderWhenCollectingInParallelTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new DelayedProxyProvider(
            TimeSpan.FromMilliseconds(120),
            [new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http }]));
        factory.Use(new DelayedProxyProvider(
            TimeSpan.FromMilliseconds(10),
            [new ServiceProxy { Provider = "ProxyScrape", Host = "10.0.1.1", Port = 8081, Type = ProxyType.Http }]));

        var batch = (await factory.GetAsync(count: 10)).Select(proxy => proxy.Host).ToArray();

        Assert.That(batch, Is.EqualTo(new[] { "10.0.0.1", "10.0.1.1" }));
    }

    [TestCase(TestName = "Фабрика назначает стабильные proxy IDs и не возвращает арендованный proxy до Return"), Benchmark]
    public async Task FactoryAssignsStableIdsAndBlocksLeasedProxiesTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeRefreshingProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ],
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]));

        var first = await factory.GetAsync();
        var whileLeased = (await factory.GetAsync(10)).ToArray();
        factory.Return(first);
        var second = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(whileLeased, Is.Empty);
            Assert.That(second.Host, Is.EqualTo(first.Host));
            Assert.That(second, Is.Not.SameAs(first));
        }
    }

    [TestCase(TestName = "Runtime filters фабрики применяются немедленно"), Benchmark]
    public async Task FactoryRuntimeFiltersApplyImmediatelyTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy
            {
                Provider = "GeoNode",
                Host = "10.0.0.1",
                Port = 8080,
                Type = ProxyType.Http,
                Anonymity = AnonymityLevel.Low,
                Geolocation = new Geolocation { Country = Country.USA },
            },
            new ServiceProxy
            {
                Provider = "GeoNode",
                Host = "10.0.0.2",
                Port = 8443,
                Type = ProxyType.Https,
                Anonymity = AnonymityLevel.High,
                Geolocation = new Geolocation { Country = Country.DE },
            },
        ]));

        factory.AllowedProtocols = [ProxyType.Https];
        factory.AllowedAnonymityLevels = [AnonymityLevel.High];
        factory.AllowedCountries = [Country.DE];

        var filtered = (await factory.GetAsync(10)).ToArray();
        foreach (var proxy in filtered)
        {
            factory.Return(proxy);
        }

        factory.AllowedProtocols = [];
        factory.AllowedAnonymityLevels = [];
        factory.AllowedCountries = [];

        var all = (await factory.GetAsync(10)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filtered.Select(static proxy => proxy.Host), Is.EqualTo(new[] { "10.0.0.2" }));
            Assert.That(all.Select(static proxy => proxy.Host), Is.EquivalentTo(new[] { "10.0.0.1", "10.0.0.2" }));
        }
    }

    [TestCase(TestName = "Factory cleanup утёкшего lease снимает блокировку по proxy ID"), Benchmark]
    public async Task FactoryCleanupBlockedProxyIdsTest()
    {
        using var factory = new ProxyFactory();
        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]));

        var leased = await factory.GetAsync();
        var unavailable = (await factory.GetAsync(10)).ToArray();
        var cleaned = factory.CleanupLeasedProxies([leased]);
        var availableAgain = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unavailable, Is.Empty);
            Assert.That(cleaned, Is.EqualTo(1));
            Assert.That(availableAgain.Host, Is.EqualTo(leased.Host));
        }
    }

    [TestCase(TestName = "Factory auto cleanup возвращает невозвращённый proxy после timeout"), Benchmark]
    public async Task FactoryAutoCleanupReleasesExpiredLeaseTest()
    {
        using var factory = new ProxyFactory
        {
            BlockedLeaseTimeout = TimeSpan.FromMilliseconds(30),
        };

        factory.Use(new FakeProxyProvider(
        [
            new ServiceProxy { Provider = "GeoNode", Host = "10.0.0.1", Port = 8080, Type = ProxyType.Http },
        ]));

        var leased = await factory.GetAsync();
        await Task.Delay(60);
        var availableAgain = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(availableAgain.Host, Is.EqualTo(leased.Host));
            Assert.That(availableAgain, Is.Not.SameAs(leased));
        }
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

    private sealed class TestMeterFactory : IMeterFactory, IDisposable
    {
        private readonly List<Meter> meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name);
            meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in meters)
            {
                meter.Dispose();
            }
        }
    }

    private sealed class TestLogger : Microsoft.Extensions.Logging.ILogger, IDisposable
    {
        public List<TestLogEntry> Entries { get; } = [];

        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => this;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
            => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Entries.Add(new TestLogEntry(logLevel, eventId, message));
            Messages.Add(message);
        }

        public void Dispose()
        {
        }
    }

    private sealed record TestLogEntry(Microsoft.Extensions.Logging.LogLevel Level, Microsoft.Extensions.Logging.EventId EventId, string Message);

    private static async Task WaitForAsync(Func<bool> predicate, int attempts = 50, int delayMilliseconds = 10)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
        }

        Assert.Fail("Condition was not satisfied within the expected time.");
    }
}