using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Atom.Architect.Components;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Atom.Web.Proxies.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет фабрику
/// </summary>
[ComponentOwner(typeof(IProxyProvider))]
public partial class ProxyFactory : IProxyFactory<IProxyProvider, ProxyFactory>
{
    private readonly ConcurrentDictionary<IProxyProvider, ProviderRegistration> providerRegistrations = [];
    private readonly ConcurrentDictionary<long, DateTime> blockedProxyIds = [];
    private readonly ConcurrentDictionary<string, long> proxyIdsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<long, IProxyProvider> providersById = [];
    private readonly Channel<byte> rebuildSignals = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly CancellationTokenSource rebuildLoopSource = new();
    private readonly Task rebuildLoopTask;
    private Meter? meter;
    private Counter<long>? refreshSuccessCounter;
    private Counter<long>? refreshFailureCounter;
    private Counter<long>? rebuildCounter;
    private Counter<long>? leaseGrantedCounter;
    private Counter<long>? leaseReleasedCounter;
    private Counter<long>? targetedSelectionCounter;
    private UpDownCounter<int>? activeProxyCountCounter;
    private UpDownCounter<int>? blockedLeaseCountCounter;
    private UpDownCounter<int>? providerCountCounter;

    private bool isDisposed;
    private int nextProxyIndex = -1;
    private int nextProviderOrder;
    private int activeProxyCount;
    private int publishedActiveProxyCount;
    private int publishedBlockedLeaseCount;
    private int publishedProviderCount;
    private long nextProxyId;
    private IMeterFactory? meterFactory;
    private TimeSpan refreshInterval = TimeSpan.FromSeconds(30);
    private TimeSpan refreshErrorBackoff = TimeSpan.FromSeconds(30);
    private bool preservePoolOnRefreshFailure = true;
    private TimeSpan blockedLeaseTimeout = TimeSpan.FromHours(24);
    private ProxyType[] allowedProtocols = [];
    private Country[] allowedCountries = [];
    private AnonymityLevel[] allowedAnonymityLevels = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProxyFactory"/>.
    /// </summary>
    public ProxyFactory()
        => rebuildLoopTask = RunRebuildLoopAsync(rebuildLoopSource.Token);

    /// <inheritdoc/>
    public IEnumerable<IProxyProvider> Providers => TryGetAll<IProxyProvider>(out var providers) ? providers : [];

    /// <summary>
    /// Resolver, определяющий dedup key для aggregate proxy pool.
    /// </summary>
    public IProxyDedupKeyResolver DedupKeyResolver { get; set; } = ProxyDedupKeyResolvers.Literal;

    /// <summary>
    /// Логгер фабрики для runtime-диагностики refresh и rebuild процессов.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Источник Meter для диагностики и внешней инструментализации фабрики.
    /// </summary>
    public IMeterFactory? MeterFactory
    {
        get => meterFactory;
        set => SetMeterFactory(value);
    }

    /// <summary>
    /// Число deduped proxy в последнем перестроенном aggregate snapshot, соответствующих активным factory filters.
    /// Значение обновляется после фонового rebuild и не гарантирует мгновенную синхронность сразу после смены filter-ов или provider stack.
    /// </summary>
    public int Count => Volatile.Read(ref activeProxyCount);

    /// <summary>
    /// Разрешённые протоколы aggregate pool. Пустой набор разрешает все протоколы.
    /// </summary>
    public IEnumerable<ProxyType> AllowedProtocols
    {
        get => Volatile.Read(ref allowedProtocols);
        set
        {
            Volatile.Write(ref allowedProtocols, value is null ? [] : [.. value.Distinct()]);
            SignalRebuild();
        }
    }

    /// <summary>
    /// Разрешённые страны. Пустой набор разрешает все страны.
    /// </summary>
    public IEnumerable<Country> AllowedCountries
    {
        get => Volatile.Read(ref allowedCountries);
        set
        {
            Volatile.Write(
                ref allowedCountries,
                value is null
                    ? []
                    : [.. value.Where(static item => item is not null).Distinct()]);
            SignalRebuild();
        }
    }

    /// <summary>
    /// Разрешённые уровни анонимности aggregate pool. Пустой набор разрешает все уровни.
    /// </summary>
    public IEnumerable<AnonymityLevel> AllowedAnonymityLevels
    {
        get => Volatile.Read(ref allowedAnonymityLevels);
        set
        {
            Volatile.Write(ref allowedAnonymityLevels, value is null ? [] : [.. value.Distinct()]);
            SignalRebuild();
        }
    }

    /// <summary>
    /// Интервал, через который фабрика опрашивает подключённые провайдеры и обновляет их снимки.
    /// </summary>
    public TimeSpan RefreshInterval
    {
        get => refreshInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);

            refreshInterval = value;
            RestartProviderPolling();
        }
    }

    /// <summary>
    /// Интервал ожидания перед повторным опросом после ошибки обновления снимка.
    /// </summary>
    public TimeSpan RefreshErrorBackoff
    {
        get => refreshErrorBackoff;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);

            refreshErrorBackoff = value;
        }
    }

    /// <summary>
    /// Указывает, должна ли фабрика сохранять последний успешный snapshot при ошибке обновления провайдера.
    /// </summary>
    public bool PreservePoolOnRefreshFailure
    {
        get => preservePoolOnRefreshFailure;
        set
        {
            preservePoolOnRefreshFailure = value;
        }
    }

    /// <summary>
    /// Стратегия выбора следующего прокси из объединённого контейнерного пула.
    /// Для <see cref="ProxyRotationStrategy.Random"/> контейнер не хранит межвызовное random-состояние и не гарантирует отсутствие повторов между вызовами.
    /// </summary>
    public ProxyRotationStrategy RotationStrategy { get; set; } = ProxyRotationStrategy.RoundRobin;

    /// <summary>
    /// Интервал, после которого невозвращённые leased proxy снимаются с блокировки автоматически.
    /// </summary>
    public TimeSpan BlockedLeaseTimeout
    {
        get => blockedLeaseTimeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            blockedLeaseTimeout = value;
        }
    }

    /// <summary>
    /// Возвращает следующий прокси из общего контейнерного пула.
    /// </summary>
    public async ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken = default)
    {
        var targeted = await TryGetColdTargetedSelectionAsync(1, cancellationToken).ConfigureAwait(false);
        if (targeted.Length != 0)
        {
            targetedSelectionCounter?.Add(1);
            if (Logger is { } logger)
            {
                logger.SingleTargetedColdStartSatisfied();
            }

            return targeted[0];
        }

        var proxy = await SelectSingleAsync(static _ => true, cancellationToken).ConfigureAwait(false);
        return proxy ?? throw new InvalidOperationException("Контейнер не содержит доступных прокси.");
    }

    /// <inheritdoc/>
    public void Return(ServiceProxy item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Id <= 0)
        {
            return;
        }

        if (CleanupBlockedProxyId(item.Id))
        {
            leaseReleasedCounter?.Add(1);
            PublishBlockedLeaseCount(blockedProxyIds.Count);
        }
    }

    /// <summary>
    /// Вручную очищает утёкшие lease для указанных прокси.
    /// </summary>
    public int CleanupLeasedProxies(IEnumerable<ServiceProxy> proxies)
    {
        ArgumentNullException.ThrowIfNull(proxies);

        return CleanupBlockedProxyIds(proxies.Where(static proxy => proxy is not null).Select(static proxy => proxy.Id));
    }

    /// <summary>
    /// Вручную очищает все утёкшие lease.
    /// </summary>
    public int CleanupLeasedProxies()
        => CleanupBlockedProxyIds();

    internal int CleanupBlockedProxyIds(IEnumerable<long> proxyIds)
    {
        ArgumentNullException.ThrowIfNull(proxyIds);

        var cleaned = 0;
        foreach (var proxyId in proxyIds)
        {
            if (proxyId > 0 && CleanupBlockedProxyId(proxyId))
            {
                cleaned++;
            }
        }

        if (cleaned != 0)
        {
            leaseReleasedCounter?.Add(cleaned);
            PublishBlockedLeaseCount(blockedProxyIds.Count);
            if (Logger is { } logger)
            {
                logger.ExplicitLeaseCleanup(cleaned);
            }
        }

        return cleaned;
    }

    internal int CleanupBlockedProxyIds()
    {
        var cleaned = 0;
        foreach (var proxyId in blockedProxyIds.Keys)
        {
            if (CleanupBlockedProxyId(proxyId))
            {
                cleaned++;
            }
        }

        if (cleaned != 0)
        {
            leaseReleasedCounter?.Add(cleaned);
            PublishBlockedLeaseCount(blockedProxyIds.Count);
            if (Logger is { } logger)
            {
                logger.FullLeaseCleanup(cleaned);
            }
        }

        return cleaned;
    }

    /// <summary>
    /// Освобождает ресурсы фабрики и подключенных провайдеров.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, false))
        {
            return;
        }

        foreach (var registration in providerRegistrations.Values)
        {
            registration.Stop();
        }

        rebuildLoopSource.Cancel();
        rebuildSignals.Writer.TryComplete();

        try
        {
            rebuildLoopTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var provider in GetProvidersSnapshot())
        {
            if (ReferenceEquals(provider.Owner, this))
            {
                UnUse<IProxyProvider>(provider);
            }

            provider.Dispose();
        }

        rebuildLoopSource.Dispose();
        meter?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Возвращает следующий прокси из общего контейнерного пула, удовлетворяющий фильтру.
    /// </summary>
    public async ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var proxy = await SelectSingleAsync(filter, cancellationToken).ConfigureAwait(false);
        return proxy ?? throw new InvalidOperationException("Контейнер не содержит прокси, удовлетворяющих фильтру.");
    }

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула.
    /// </summary>
    public async ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, CancellationToken cancellationToken = default)
    {
        var targeted = await TryGetColdTargetedSelectionAsync(count, cancellationToken).ConfigureAwait(false);
        if (targeted.Length != 0)
        {
            targetedSelectionCounter?.Add(targeted.Length);
            if (Logger is { } logger)
            {
                logger.BatchTargetedColdStartSatisfied(targeted.Length);
            }

            return targeted;
        }

        return await GetAsync(count, static _ => true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула, удовлетворяющую фильтру.
    /// </summary>
    public async ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(filter);

        var snapshot = await CollectAvailableSnapshotAsync(filter, includeBlocked: false, cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return [];
        }

        var selected = ProxyPoolSelection.Select(snapshot, count, static _ => true, RotationStrategy, ref nextProxyIndex);
        return LeaseSelection(selected, count);
    }

    private void ApplyReliabilitySettings(IProxyProvider provider)
    {
        if (provider is ProxyProvider proxyProvider)
        {
            proxyProvider.DedupKeyResolver = DedupKeyResolver;
            proxyProvider.Logger ??= Logger;
        }
    }

    private async ValueTask<ServiceProxy[]> CollectAvailableSnapshotAsync(
        Func<ServiceProxy, bool> filter,
        bool includeBlocked,
        CancellationToken cancellationToken)
    {
        await EnsureWarmSnapshotAsync(cancellationToken).ConfigureAwait(false);
        CleanupExpiredBlockedEntries();

        return await CollectSnapshotCoreAsync(filter, includeBlocked, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ServiceProxy[]> CollectSnapshotCoreAsync(
        Func<ServiceProxy, bool> filter,
        bool includeBlocked,
        CancellationToken cancellationToken)
    {
        var registrations = GetOrderedRegistrations();
        if (registrations.Length == 0)
        {
            return [];
        }

        var protocols = Volatile.Read(ref allowedProtocols);
        var countries = Volatile.Read(ref allowedCountries);
        var anonymityLevels = Volatile.Read(ref allowedAnonymityLevels);
        var candidates = new List<(ServiceProxy Proxy, IProxyProvider Provider)>();
        for (var registrationIndex = 0; registrationIndex < registrations.Length; registrationIndex++)
        {
            var registration = registrations[registrationIndex];
            var snapshot = Volatile.Read(ref registration.Snapshot);
            for (var proxyIndex = 0; proxyIndex < snapshot.Length; proxyIndex++)
            {
                var proxy = snapshot[proxyIndex];
                if (!MatchesFactoryFilters(proxy, protocols, countries, anonymityLevels) || !filter(proxy))
                {
                    continue;
                }

                candidates.Add((proxy, registration.Provider));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        return await FinalizeCandidatesAsync(candidates, includeBlocked, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureWarmSnapshotAsync(CancellationToken cancellationToken)
    {
        var registrations = GetOrderedRegistrations();
        if (registrations.Length == 0)
        {
            return;
        }

        if (registrations.All(static registration => Volatile.Read(ref registration.Snapshot).Length != 0))
        {
            return;
        }

        var warmed = false;
        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
            if (registration.IsInitialized)
            {
                continue;
            }

            if (Volatile.Read(ref registration.Snapshot).Length != 0)
            {
                continue;
            }

            var snapshot = await CollectServicePoolSnapshotAsync(registration.Provider, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref registration.Snapshot, snapshot);
            registration.MarkInitialized();
            warmed = true;
        }

        if (!warmed)
        {
            return;
        }

        SignalRebuild();
    }

    private async Task<ServiceProxy?> SelectSingleAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken)
    {
        var snapshot = await CollectAvailableSnapshotAsync(filter, includeBlocked: false, cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return null;
        }

        var selected = ProxyPoolSelection.SelectSingle(snapshot, static _ => true, RotationStrategy, ref nextProxyIndex);
        if (selected is null)
        {
            return null;
        }

        var leased = LeaseSelection([selected], 1);
        return leased.Length == 0 ? null : leased[0];
    }

    private async ValueTask<ServiceProxy[]> TryGetColdTargetedSelectionAsync(int requestedCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCount);

        var registrations = GetOrderedRegistrations();
        if (registrations.Length == 0
            || registrations.Any(static registration => Volatile.Read(ref registration.Snapshot).Length != 0)
            || registrations.Any(static registration => registration.Provider is not IProxyTargetedProvider))
        {
            return [];
        }

        var protocols = Volatile.Read(ref allowedProtocols);
        var countries = Volatile.Read(ref allowedCountries);
        var anonymityLevels = Volatile.Read(ref allowedAnonymityLevels);
        var request = new ProxyProviderFetchRequest(requestedCount, protocols, countries, anonymityLevels);
        var candidates = new List<(ServiceProxy Proxy, IProxyProvider Provider)>();

        for (var index = 0; index < registrations.Length; index++)
        {
            var targetedProvider = (IProxyTargetedProvider)registrations[index].Provider;
            var result = await targetedProvider.FetchAsync(request, cancellationToken).ConfigureAwait(false);
            for (var proxyIndex = 0; proxyIndex < result.Proxies.Count; proxyIndex++)
            {
                var proxy = result.Proxies[proxyIndex];
                if (MatchesFactoryFilters(proxy, protocols, countries, anonymityLevels))
                {
                    candidates.Add((proxy, registrations[index].Provider));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var snapshot = await FinalizeCandidatesAsync(candidates, includeBlocked: false, cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return [];
        }

        var selected = ProxyPoolSelection.Select(snapshot, requestedCount, static _ => true, RotationStrategy, ref nextProxyIndex);
        return LeaseSelection(selected, requestedCount);
    }

    private async ValueTask<ServiceProxy[]> FinalizeCandidatesAsync(
        IReadOnlyList<(ServiceProxy Proxy, IProxyProvider Provider)> candidates,
        bool includeBlocked,
        CancellationToken cancellationToken)
    {
        var keyTasks = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidates.Count; index++)
        {
            var host = candidates[index].Proxy.Host ?? string.Empty;
            if (!keyTasks.ContainsKey(host))
            {
                keyTasks.Add(host, DedupKeyResolver.GetKeyAsync(candidates[index].Proxy, cancellationToken).AsTask());
            }
        }

        await Task.WhenAll(keyTasks.Values).ConfigureAwait(false);

        var result = new List<ServiceProxy>(candidates.Count);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var key = keyTasks[candidate.Proxy.Host ?? string.Empty].Result;
            if (!seenKeys.Add(key))
            {
                continue;
            }

            var proxyId = proxyIdsByKey.GetOrAdd(key, _ => Interlocked.Increment(ref nextProxyId));
            providersById[proxyId] = candidate.Provider;

            var proxy = new ServiceProxy(proxyId, candidate.Proxy);
            if (!includeBlocked && blockedProxyIds.ContainsKey(proxy.Id))
            {
                continue;
            }

            result.Add(proxy);
        }

        return [.. result];
    }

    private ServiceProxy[] LeaseSelection(IReadOnlyList<ServiceProxy> selected, int requestedCount)
    {
        if (selected.Count == 0 || requestedCount <= 0)
        {
            return [];
        }

        var leased = new List<ServiceProxy>(Math.Min(requestedCount, selected.Count));
        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < selected.Count && leased.Count < requestedCount; index++)
        {
            var proxy = selected[index];
            if (proxy.Id <= 0)
            {
                continue;
            }

            if (blockedProxyIds.TryAdd(proxy.Id, nowUtc))
            {
                leased.Add(proxy);
            }
        }

        if (leased.Count != 0)
        {
            leaseGrantedCounter?.Add(leased.Count);
            PublishBlockedLeaseCount(blockedProxyIds.Count);
        }

        return [.. leased];
    }

    private ProxyFactory AttachProvider(IProxyProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        ApplyReliabilitySettings(provider);
        Use<IProxyProvider>(provider);
        EnsureProviderRegistrationStarted(provider);
        PublishProviderCount(providerRegistrations.Count);
        SignalRebuild();
        return this;
    }

    private IProxyProvider[] GetProvidersSnapshot()
    {
        if (!TryGetAll<IProxyProvider>(out var providers))
        {
            return [];
        }

        return [.. providers];
    }

    private ProviderRegistration EnsureProviderRegistration(IProxyProvider provider)
        => providerRegistrations.GetOrAdd(provider, static (service, factory) =>
        {
            factory.ApplyReliabilitySettings(service);
            var created = new ProviderRegistration(service, Interlocked.Increment(ref factory.nextProviderOrder));
            if (service is IComponent component)
            {
                component.Detached += factory.OnProviderDetached;
            }

            return created;
        }, this);

    private void EnsureAttachedProvidersRegistered()
    {
        if (!TryGetAll<IProxyProvider>(out var providers))
        {
            return;
        }

        foreach (var provider in providers)
        {
            EnsureProviderRegistrationStarted(provider);
        }
    }

    private ProviderRegistration EnsureProviderRegistrationStarted(IProxyProvider provider)
    {
        var registration = EnsureProviderRegistration(provider);
        if (!registration.IsStarted)
        {
            if (Logger is { } logger)
            {
                logger.ProviderAttached(provider.GetType().Name, providerRegistrations.Count);
            }

            StartProviderPolling(registration, registration.Restart());
        }

        return registration;
    }

    private void RestartProviderPolling()
    {
        foreach (var registration in providerRegistrations.Values)
        {
            StartProviderPolling(registration, registration.Restart());
        }
    }

    private void StartProviderPolling(ProviderRegistration registration, CancellationToken cancellationToken)
    {
        registration.MarkStarted();
        _ = RefreshProviderSnapshotAsync(registration, cancellationToken);
        registration.PollTask = RefreshInterval > TimeSpan.Zero
            ? RunProviderPollingAsync(registration, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RunProviderPollingAsync(ProviderRegistration registration, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, cancellationToken).ConfigureAwait(false);
                await RefreshProviderSnapshotAsync(registration, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshProviderSnapshotAsync(ProviderRegistration registration, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await CollectServicePoolSnapshotAsync(registration.Provider, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref registration.Snapshot, snapshot);
            refreshSuccessCounter?.Add(1);
            if (Logger is { } logger)
            {
                logger.ProviderSnapshotRefreshed(registration.Provider.GetType().Name, snapshot.Length);
            }

            SignalRebuild();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            refreshFailureCounter?.Add(1);
            var preservedSnapshotCount = Volatile.Read(ref registration.Snapshot).Length;
            if (PreservePoolOnRefreshFailure)
            {
                if (Logger is { } logger)
                {
                    logger.ProviderSnapshotRefreshFailedPreserved(exception, registration.Provider.GetType().Name, preservedSnapshotCount);
                }
            }
            else
            {
                if (Logger is { } logger)
                {
                    logger.ProviderSnapshotRefreshFailedCleared(exception, registration.Provider.GetType().Name);
                }
            }

            if (!PreservePoolOnRefreshFailure)
            {
                Volatile.Write(ref registration.Snapshot, []);
                SignalRebuild();
            }

            if (RefreshErrorBackoff > TimeSpan.Zero)
            {
                await Task.Delay(RefreshErrorBackoff, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            registration.MarkInitialized();
        }
    }

    private void OnProviderDetached(object? sender, ComponentEventArgs args)
    {
        if (args.Component is not IProxyProvider provider)
        {
            return;
        }

        if (provider is IComponent component)
        {
            component.Detached -= OnProviderDetached;
        }

        if (providerRegistrations.TryRemove(provider, out var registration))
        {
            registration.Stop();
            PublishProviderCount(providerRegistrations.Count);
            if (Logger is { } logger)
            {
                logger.ProviderDetached(provider.GetType().Name, providerRegistrations.Count);
            }

            SignalRebuild();
        }
    }

    private void SignalRebuild()
        => rebuildSignals.Writer.TryWrite(0);

    private async Task RunRebuildLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await rebuildSignals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (rebuildSignals.Reader.TryRead(out _))
                {
                }

                await RefreshProxyIndexAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshProxyIndexAsync(CancellationToken cancellationToken)
    {
        rebuildCounter?.Add(1);
        var registrations = GetOrderedRegistrations();
        if (registrations.Length == 0)
        {
            Volatile.Write(ref activeProxyCount, 0);
            PublishActiveProxyCount(0);
            PublishProviderCount(0);
            proxyIdsByKey.Clear();
            providersById.Clear();
            return;
        }

        var protocols = Volatile.Read(ref allowedProtocols);
        var countries = Volatile.Read(ref allowedCountries);
        var anonymityLevels = Volatile.Read(ref allowedAnonymityLevels);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeIds = new HashSet<long>();
        for (var registrationIndex = 0; registrationIndex < registrations.Length; registrationIndex++)
        {
            var registration = registrations[registrationIndex];
            var snapshot = Volatile.Read(ref registration.Snapshot);
            for (var proxyIndex = 0; proxyIndex < snapshot.Length; proxyIndex++)
            {
                var proxy = snapshot[proxyIndex];
                if (!MatchesFactoryFilters(proxy, protocols, countries, anonymityLevels))
                {
                    continue;
                }

                var key = await DedupKeyResolver.GetKeyAsync(proxy, cancellationToken).ConfigureAwait(false);
                if (!activeKeys.Add(key))
                {
                    continue;
                }

                var id = proxyIdsByKey.GetOrAdd(key, _ => Interlocked.Increment(ref nextProxyId));
                providersById[id] = registration.Provider;
                activeIds.Add(id);
            }
        }

        Volatile.Write(ref activeProxyCount, activeIds.Count);
        PublishActiveProxyCount(activeIds.Count);
        PublishProviderCount(registrations.Length);
        if (Logger is { } logger)
        {
            logger.RebuildCompleted(activeIds.Count, registrations.Length, blockedProxyIds.Count);
        }

        foreach (var pair in proxyIdsByKey)
        {
            if (activeKeys.Contains(pair.Key) || blockedProxyIds.ContainsKey(pair.Value))
            {
                continue;
            }

            proxyIdsByKey.TryRemove(pair.Key, out _);
            providersById.TryRemove(pair.Value, out _);
        }

        foreach (var pair in providersById)
        {
            if (activeIds.Contains(pair.Key) || blockedProxyIds.ContainsKey(pair.Key))
            {
                continue;
            }

            providersById.TryRemove(pair.Key, out _);
        }
    }

    private ProviderRegistration[] GetOrderedRegistrations()
    {
        EnsureAttachedProvidersRegistered();
        return [.. providerRegistrations.Values.OrderBy(static registration => registration.Order)];
    }

    private void SetMeterFactory(IMeterFactory? value)
    {
        meter?.Dispose();
        meter = null;
        refreshSuccessCounter = null;
        refreshFailureCounter = null;
        rebuildCounter = null;
        leaseGrantedCounter = null;
        leaseReleasedCounter = null;
        targetedSelectionCounter = null;
        activeProxyCountCounter = null;
        blockedLeaseCountCounter = null;
        providerCountCounter = null;
        meterFactory = value;
        publishedActiveProxyCount = 0;
        publishedBlockedLeaseCount = 0;
        publishedProviderCount = 0;

        if (value is null)
        {
            return;
        }

        meter = value.Create(new MeterOptions("Escorp.Atom.Web.Services.Proxies.ProxyFactory"));
        refreshSuccessCounter = meter.CreateCounter<long>("proxy.factory.refresh.success");
        refreshFailureCounter = meter.CreateCounter<long>("proxy.factory.refresh.failure");
        rebuildCounter = meter.CreateCounter<long>("proxy.factory.rebuild");
        leaseGrantedCounter = meter.CreateCounter<long>("proxy.factory.lease.granted");
        leaseReleasedCounter = meter.CreateCounter<long>("proxy.factory.lease.released");
        targetedSelectionCounter = meter.CreateCounter<long>("proxy.factory.selection.targeted");
        activeProxyCountCounter = meter.CreateUpDownCounter<int>("proxy.factory.count.active");
        blockedLeaseCountCounter = meter.CreateUpDownCounter<int>("proxy.factory.count.blocked");
        providerCountCounter = meter.CreateUpDownCounter<int>("proxy.factory.count.providers");

        PublishActiveProxyCount(Volatile.Read(ref activeProxyCount));
        PublishBlockedLeaseCount(blockedProxyIds.Count);
        PublishProviderCount(providerRegistrations.Count);
    }

    private static async Task<ServiceProxy[]> CollectServicePoolSnapshotAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        var proxies = await CollectServicePoolAsync(provider, cancellationToken).ConfigureAwait(false);
        if (proxies is ServiceProxy[] array)
        {
            return array;
        }

        if (proxies is IReadOnlyList<ServiceProxy> list)
        {
            var snapshot = new ServiceProxy[list.Count];
            for (var index = 0; index < list.Count; index++)
            {
                snapshot[index] = list[index];
            }

            return snapshot;
        }

        if (proxies is ICollection<ServiceProxy> collection)
        {
            var snapshot = new ServiceProxy[collection.Count];
            collection.CopyTo(snapshot, 0);
            return snapshot;
        }

        var materialized = new List<ServiceProxy>();
        foreach (var proxy in proxies)
        {
            materialized.Add(proxy);
        }

        return [.. materialized];
    }

    private static async ValueTask<IEnumerable<ServiceProxy>> CollectServicePoolAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        if (provider is IProxyPoolSnapshotSource snapshotSource)
        {
            return await snapshotSource.GetPoolSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }

        return await provider.FetchAsync(cancellationToken).ConfigureAwait(false);
    }

    private void CleanupExpiredBlockedEntries()
    {
        var cleaned = 0;
        var nowUtc = DateTime.UtcNow;
        foreach (var pair in blockedProxyIds)
        {
            if (nowUtc - pair.Value >= BlockedLeaseTimeout)
            {
                blockedProxyIds.TryRemove(pair.Key, out _);
                cleaned++;
            }
        }

        if (cleaned != 0)
        {
            leaseReleasedCounter?.Add(cleaned);
            PublishBlockedLeaseCount(blockedProxyIds.Count);
            if (Logger is { } logger)
            {
                logger.ExpiredBlockedLeases(cleaned);
            }
        }
    }

    private bool CleanupBlockedProxyId(long proxyId)
        => blockedProxyIds.TryRemove(proxyId, out _);

    private void PublishActiveProxyCount(int currentCount)
        => PublishUpDownDelta(activeProxyCountCounter, ref publishedActiveProxyCount, currentCount);

    private void PublishBlockedLeaseCount(int currentCount)
        => PublishUpDownDelta(blockedLeaseCountCounter, ref publishedBlockedLeaseCount, currentCount);

    private void PublishProviderCount(int currentCount)
        => PublishUpDownDelta(providerCountCounter, ref publishedProviderCount, currentCount);

    private static void PublishUpDownDelta(UpDownCounter<int>? counter, ref int publishedValue, int currentValue)
    {
        var previousValue = Interlocked.Exchange(ref publishedValue, currentValue);
        var delta = currentValue - previousValue;
        if (delta != 0)
        {
            counter?.Add(delta);
        }
    }

    private static bool MatchesFactoryFilters(
        ServiceProxy proxy,
        ProxyType[] protocols,
        Country[] countries,
        AnonymityLevel[] anonymityLevels)
    {
        if (protocols.Length != 0 && Array.IndexOf(protocols, proxy.Type) < 0)
        {
            return false;
        }

        if (anonymityLevels.Length != 0 && Array.IndexOf(anonymityLevels, proxy.Anonymity) < 0)
        {
            return false;
        }

        if (countries.Length == 0)
        {
            return true;
        }

        var countryCode = proxy.Geolocation?.Country?.IsoCode2;
        return !string.IsNullOrWhiteSpace(countryCode)
            && countries.Any(country => string.Equals(country.IsoCode2, countryCode, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ProviderRegistration(IProxyProvider provider, int order)
    {
        private int initialized;
        private CancellationTokenSource? source;
        private int started;

        public IProxyProvider Provider { get; } = provider;

        public int Order { get; } = order;

        public ServiceProxy[] Snapshot = [];

        public Task PollTask { get; set; } = Task.CompletedTask;

        public bool IsStarted => Volatile.Read(ref started) == 1;

        public bool IsInitialized => Volatile.Read(ref initialized) == 1;

        public void MarkStarted()
            => Interlocked.Exchange(ref started, 1);

        public void MarkInitialized()
            => Interlocked.Exchange(ref initialized, 1);

        public CancellationToken Restart()
        {
            var nextSource = new CancellationTokenSource();
            var currentSource = Interlocked.Exchange(ref source, nextSource);
            currentSource?.Cancel();
            currentSource?.Dispose();
            return nextSource.Token;
        }

        public void Stop()
        {
            var currentSource = Interlocked.Exchange(ref source, null);
            currentSource?.Cancel();
            currentSource?.Dispose();
        }
    }
}