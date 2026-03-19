using Atom.Architect.Components;
using Atom.Web.Proxies.Services;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет фабрику
/// </summary>
[ComponentOwner(typeof(IProxyProvider))]
public partial class ProxyFactory : IProxyFactory<IProxyProvider, ProxyFactory>
{
    private bool isDisposed;
    private int nextProxyIndex = -1;
    private TimeSpan refreshInterval = TimeSpan.FromMinutes(5);
    private TimeSpan refreshErrorBackoff = TimeSpan.FromSeconds(30);
    private bool preservePoolOnRefreshFailure = true;
    private ProxyRotationStrategy serviceRotationStrategy = ProxyRotationStrategy.RoundRobin;

    /// <inheritdoc/>
    public IEnumerable<IProxyProvider> Services => TryGetAll<IProxyProvider>(out var providers) ? providers : [];

    /// <summary>
    /// Resolver, определяющий dedup key для aggregate proxy pool.
    /// </summary>
    public IProxyDedupKeyResolver DedupKeyResolver { get; set; } = ProxyDedupKeyResolvers.Literal;

    /// <summary>
    /// Интервал автообновления, который контейнер применяет ко всем подключаемым сервисам.
    /// </summary>
    public TimeSpan RefreshInterval
    {
        get => refreshInterval;
        set
        {
            refreshInterval = value;
            ApplyToServices(static (service, interval) => service.RefreshInterval = interval, value);
        }
    }

    /// <summary>
    /// Интервал ожидания перед повторным фоновым обновлением после ошибки.
    /// </summary>
    public TimeSpan RefreshErrorBackoff
    {
        get => refreshErrorBackoff;
        set
        {
            refreshErrorBackoff = value;
            ApplyToServices(static (service, backoff) => service.RefreshErrorBackoff = backoff, value);
        }
    }

    /// <summary>
    /// Указывает, должен ли сервис сохранять последний успешный пул при ошибке обновления.
    /// </summary>
    public bool PreservePoolOnRefreshFailure
    {
        get => preservePoolOnRefreshFailure;
        set
        {
            preservePoolOnRefreshFailure = value;
            ApplyToServices(static (service, preserve) => service.PreservePoolOnRefreshFailure = preserve, value);
        }
    }

    /// <summary>
    /// Стратегия выбора следующего прокси из объединённого контейнерного пула.
    /// Для <see cref="ProxyRotationStrategy.Random"/> контейнер не хранит межвызовное random-состояние и не гарантирует отсутствие повторов между вызовами.
    /// </summary>
    public ProxyRotationStrategy RotationStrategy { get; set; } = ProxyRotationStrategy.RoundRobin;

    /// <summary>
    /// Стратегия ротации, которую контейнер применяет ко всем подключаемым сервисам.
    /// </summary>
    public ProxyRotationStrategy ServiceRotationStrategy
    {
        get => serviceRotationStrategy;
        set
        {
            serviceRotationStrategy = value;
            ApplyToServices(static (service, strategy) => service.RotationStrategy = strategy, value);
        }
    }

    /// <summary>
    /// Подключает провайдера и сразу применяет к нему container-level defaults.
    /// </summary>
    public ProxyFactory UseProvider(IProxyProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        ApplyReliabilitySettings(provider);
        Use<IProxyProvider>(provider);
        return this;
    }

    /// <summary>
    /// Подключает провайдера и позволяет сразу переопределить его настройки поверх контейнерных defaults.
    /// </summary>
    public ProxyFactory Use<T>(T component, Action<T> configure) where T : class, IProxyProvider
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(configure);

        UseProvider(component);
        configure(component);
        return this;
    }

    /// <summary>
    /// Переопределяет настройки уже подключённого сервиса.
    /// </summary>
    public ProxyFactory Configure<T>(Action<T> configure) where T : class, IProxyProvider
    {
        ArgumentNullException.ThrowIfNull(configure);

        var service = FindProvider<T>();
        if (service is not null)
        {
            configure(service);
        }

        return this;
    }

    /// <summary>
    /// Собирает и валидирует единый пул прокси из всех подключённых proxy provider сервисов.
    /// </summary>
    /// <param name="validationUri">Адрес, на котором нужно проверить прокси.</param>
    /// <param name="filter">Фильтр выборки. Если не указан, возвращаются все прокси.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask<IEnumerable<ServiceProxy>> GetValidatedPoolAsync(
        Uri validationUri,
        Func<ServiceProxy, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validationUri);
        filter ??= static _ => true;

        var providers = GetProvidersSnapshot(applyReliabilitySettings: true);
        if (providers.Length == 0)
        {
            return [];
        }

        var tasks = new Task<List<ServiceProxy>>[providers.Length];
        for (var index = 0; index < providers.Length; index++)
        {
            tasks[index] = CollectValidatedProxiesAsync(providers[index], validationUri, filter, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var result = new ServiceProxy[GetTotalCount(tasks)];
        var resultIndex = 0;
        for (var index = 0; index < tasks.Length; index++)
        {
            var validated = tasks[index].Result;
            for (var validatedIndex = 0; validatedIndex < validated.Count; validatedIndex++)
            {
                result[resultIndex++] = validated[validatedIndex];
            }
        }

        return await DeduplicateAsync(result, resultIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает следующий прокси из общего контейнерного пула.
    /// </summary>
    public async ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken = default)
    {
        var proxy = await SelectSingleAsync(static _ => true, cancellationToken).ConfigureAwait(false);
        return proxy ?? throw new InvalidOperationException("Контейнер не содержит доступных прокси.");
    }

    /// <inheritdoc/>
    public void Return(ServiceProxy item)
        => ArgumentNullException.ThrowIfNull(item);

    /// <summary>
    /// Освобождает ресурсы фабрики и подключенных провайдеров.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, false))
        {
            return;
        }

        foreach (var provider in GetProvidersSnapshot())
        {
            if (ReferenceEquals(provider.Owner, this))
            {
                UnUse<IProxyProvider>(provider);
            }

            provider.Dispose();
        }

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
    public ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, CancellationToken cancellationToken = default)
        => GetAsync(count, static _ => true, cancellationToken);

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула, удовлетворяющую фильтру.
    /// Контейнер применяет стратегию выбора к уже агрегированному snapshot и не делегирует container-level rotation отдельным сервисам.
    /// </summary>
    public async ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(filter);

        var snapshot = await CollectAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return [];
        }

        return ProxyPoolSelection.Select(snapshot, count, filter, RotationStrategy, ref nextProxyIndex);
    }

    private void ApplyReliabilitySettings(IProxyProvider provider)
    {
        provider.RefreshInterval = RefreshInterval;
        provider.RefreshErrorBackoff = RefreshErrorBackoff;
        provider.PreservePoolOnRefreshFailure = PreservePoolOnRefreshFailure;
        provider.RotationStrategy = ServiceRotationStrategy;

        if (provider is ProxyProvider proxyProvider)
        {
            proxyProvider.DedupKeyResolver = DedupKeyResolver;
        }
    }

    private void ApplyToServices<TValue>(Action<IProxyProvider, TValue> apply, TValue value)
    {
        foreach (var provider in GetProvidersSnapshot(applyReliabilitySettings: true))
        {
            apply(provider, value);
        }
    }

    private async ValueTask<ServiceProxy[]> CollectAsync(CancellationToken cancellationToken)
    {
        var providers = GetProvidersSnapshot(applyReliabilitySettings: true);
        if (providers.Length == 0)
        {
            return [];
        }

        var tasks = new Task<IReadOnlyList<ServiceProxy>>[providers.Length];
        for (var index = 0; index < providers.Length; index++)
        {
            tasks[index] = CollectServicePoolSnapshotAsync(providers[index], cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var result = new ServiceProxy[GetTotalCount(tasks)];
        var resultIndex = 0;
        for (var index = 0; index < tasks.Length; index++)
        {
            var proxies = tasks[index].Result;
            for (var proxyIndex = 0; proxyIndex < proxies.Count; proxyIndex++)
            {
                result[resultIndex++] = proxies[proxyIndex];
            }
        }

        return await DeduplicateAsync(result, resultIndex, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<ServiceProxy>> CollectValidatedProxiesAsync(
        IProxyProvider provider,
        Uri validationUri,
        Func<ServiceProxy, bool> filter,
        CancellationToken cancellationToken)
    {
        var proxies = await CollectServicePoolAsync(provider, cancellationToken).ConfigureAwait(false);
        var result = new List<ServiceProxy>(GetProxyCount(proxies));
        foreach (var proxy in proxies)
        {
            if (!filter(proxy))
            {
                continue;
            }

            if (await provider.ValidateAsync(proxy, validationUri, cancellationToken).ConfigureAwait(false))
            {
                result.Add(proxy);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<ServiceProxy>> CollectServicePoolSnapshotAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        var proxies = await CollectServicePoolAsync(provider, cancellationToken).ConfigureAwait(false);
        if (proxies is IReadOnlyList<ServiceProxy> list)
        {
            return list;
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

        return materialized;
    }

    private static async ValueTask<IEnumerable<ServiceProxy>> CollectServicePoolAsync(IProxyProvider provider, CancellationToken cancellationToken)
    {
        if (provider is IProxyPoolSnapshotSource snapshotSource)
        {
            return await snapshotSource.GetPoolSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }

        return await provider.GetAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
    }

    private T? FindProvider<T>() where T : class, IProxyProvider
    {
        foreach (var provider in GetProvidersSnapshot())
        {
            if (provider is T typedProvider)
            {
                return typedProvider;
            }
        }

        return null;
    }

    private IProxyProvider[] GetProvidersSnapshot(bool applyReliabilitySettings = false)
    {
        if (!TryGetAll<IProxyProvider>(out var providers))
        {
            return [];
        }

        IProxyProvider[] snapshot = [.. providers];
        if (!applyReliabilitySettings)
        {
            return snapshot;
        }

        foreach (var provider in snapshot)
        {
            ApplyReliabilitySettings(provider);
        }

        return snapshot;
    }

    private static int GetProxyCount(IEnumerable<ServiceProxy> proxies)
    {
        if (proxies is IReadOnlyCollection<ServiceProxy> readOnlyCollection)
        {
            return readOnlyCollection.Count;
        }

        if (proxies is ICollection<ServiceProxy> collection)
        {
            return collection.Count;
        }

        return 0;
    }

    private static int GetTotalCount(Task<List<ServiceProxy>>[] tasks)
    {
        var totalCount = 0;
        for (var index = 0; index < tasks.Length; index++)
        {
            totalCount += tasks[index].Result.Count;
        }

        return totalCount;
    }

    private static int GetTotalCount(Task<IReadOnlyList<ServiceProxy>>[] tasks)
    {
        var totalCount = 0;
        for (var index = 0; index < tasks.Length; index++)
        {
            totalCount += tasks[index].Result.Count;
        }

        return totalCount;
    }

    private async ValueTask<ServiceProxy[]> DeduplicateAsync(ServiceProxy[] proxies, int count, CancellationToken cancellationToken)
    {
        if (count <= 1)
        {
            return TrimResult(proxies, count);
        }

        var keyTasks = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var proxy = proxies[index];
            var host = proxy.Host ?? string.Empty;
            if (!keyTasks.ContainsKey(host))
            {
                keyTasks.Add(host, DedupKeyResolver.GetKeyAsync(proxy, cancellationToken).AsTask());
            }
        }

        await Task.WhenAll(keyTasks.Values).ConfigureAwait(false);

        var result = new ServiceProxy[count];
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resultIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var proxy = proxies[index];
            var host = proxy.Host ?? string.Empty;
            if (seenKeys.Add(keyTasks[host].Result))
            {
                result[resultIndex++] = proxy;
            }
        }

        return TrimResult(result, resultIndex);
    }

    private static ServiceProxy[] TrimResult(ServiceProxy[] result, int count)
    {
        if (count == 0)
        {
            return [];
        }

        if (count == result.Length)
        {
            return result;
        }

        var trimmed = new ServiceProxy[count];
        Array.Copy(result, trimmed, count);
        return trimmed;
    }

    private async ValueTask<ServiceProxy?> SelectSingleAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken)
    {
        var snapshot = await CollectAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return null;
        }

        return ProxyPoolSelection.SelectSingle(snapshot, filter, RotationStrategy, ref nextProxyIndex);
    }
}