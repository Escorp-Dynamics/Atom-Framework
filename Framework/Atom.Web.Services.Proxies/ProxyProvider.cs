using Atom.Architect.Components;
using Atom.Collections;
using System.Net;

#pragma warning disable CS4014

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовую реализацию провайдера прокси.
/// </summary>
[Component]
public abstract partial class ProxyProvider : IProxyProvider, IProxyPoolSnapshotSource
{
    private readonly SparseArray<IProxyValidator> validators = new(1024);

    private bool isDisposed;
    private ServiceProxy[] pool = [];
    private int nextProxyIndex = -1;
    private CancellationTokenSource? refreshLoopSource;
    private Task? refreshLoopTask;
    private Task<ServiceProxy[]>? refreshTask;

    /// <inheritdoc/>
    public IEnumerable<IProxyValidator> Validators => validators;

    /// <summary>
    /// Resolver, определяющий dedup key для элементов внутреннего пула.
    /// </summary>
    public IProxyDedupKeyResolver DedupKeyResolver { get; set; } = ProxyDedupKeyResolvers.Literal;

    /// <inheritdoc/>
    public ProxyRotationStrategy RotationStrategy { get; set; } = ProxyRotationStrategy.RoundRobin;

    /// <inheritdoc/>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    public TimeSpan RefreshErrorBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public bool PreservePoolOnRefreshFailure { get; set; } = true;

    /// <inheritdoc/>
    public DateTime LastRefreshUtc { get; private set; }

    /// <inheritdoc/>
    public Exception? LastRefreshException { get; private set; }

    /// <inheritdoc/>
    public int PoolCount
    {
        get => Volatile.Read(ref pool).Length;
    }

    /// <summary>
    /// Происходит в момент высвобождения ресурсов.
    /// </summary>
    /// <param name="disposing">Указывает, следует ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(location1: ref isDisposed, value: true, comparand: default)) return;

        if (disposing)
        {
            var source = Interlocked.Exchange(ref refreshLoopSource, null);
            source?.Cancel();
            source?.Dispose();
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await GetOrStartRefreshTask(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual void Return(ServiceProxy item)
        => ArgumentNullException.ThrowIfNull(item);

    /// <inheritdoc/>
    public virtual async ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken)
    {
        var proxy = await SelectSingleAsync(static _ => true, cancellationToken).ConfigureAwait(false);
        return proxy ?? throw new InvalidOperationException("Сервис не смог вернуть прокси из внутреннего пула.");
    }

    /// <inheritdoc/>
    public ValueTask<ServiceProxy> GetAsync() => GetAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var proxy = await SelectSingleAsync(filter, cancellationToken).ConfigureAwait(false);
        return proxy ?? throw new InvalidOperationException("Внутренний пул не содержит прокси, удовлетворяющих фильтру.");
    }

    /// <inheritdoc/>
    public ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter) => GetAsync(filter, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, CancellationToken cancellationToken)
        => GetAsync(count, static _ => true, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count) => GetAsync(count, CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(filter);

        var snapshot = await EnsurePoolSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return [];
        }

        return Select(snapshot, count, filter);
    }

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter)
        => GetAsync(count, filter, CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(url);

        foreach (var validator in validators)
        {
            if (!await validator.ValidateAsync(proxy, url, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public IProxyProvider UseValidator<T>(T validator) where T : IProxyValidator
    {
        validators.Add(validator);
        return this;
    }

    /// <inheritdoc/>
    public IProxyProvider UseValidator<T>() where T : IProxyValidator, new()
    {
        if (!TryGetValidator<T>(out _)) validators.Add(new T());
        return this;
    }

    /// <summary>
    /// Удаляет конкретный экземпляр валидатора прокси.
    /// </summary>
    /// <param name="validator">Экземпляр валидатора для удаления.</param>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    public IProxyProvider UnUseValidator<T>(T validator) where T : IProxyValidator
    {
        ArgumentNullException.ThrowIfNull(validator);

        RemoveValidators(existing => ReferenceEquals(existing, validator));
        return this;
    }

    /// <summary>
    /// Удаляет все валидаторы указанного типа.
    /// </summary>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    public IProxyProvider UnUseValidator<T>() where T : IProxyValidator
    {
        RemoveValidators(static existing => existing is T);
        return this;
    }

    /// <summary>
    /// Пытается получить первый валидатор указанного типа.
    /// </summary>
    /// <param name="validator">Найденный валидатор, если он существует.</param>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    /// <returns><see langword="true"/>, если валидатор найден.</returns>
    public bool TryGetValidator<T>(out T? validator) where T : IProxyValidator
    {
        foreach (var existing in validators)
        {
            if (existing is T resolved)
            {
                validator = resolved;
                return true;
            }
        }

        validator = default;
        return false;
    }

    /// <summary>
    /// Возвращает первый валидатор указанного типа.
    /// </summary>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    /// <returns>Первый найденный валидатор или <see langword="null"/>.</returns>
    public IProxyValidator? GetValidator<T>() where T : IProxyValidator
    {
        foreach (var existing in validators)
        {
            if (existing is T)
            {
                return existing;
            }
        }

        return null;
    }

    /// <summary>
    /// Пытается получить все валидаторы указанного типа.
    /// </summary>
    /// <param name="validators">Последовательность найденных валидаторов.</param>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    /// <returns><see langword="true"/>, если найден хотя бы один валидатор.</returns>
    public bool TryGetAllValidators<T>(out IEnumerable<T> validators) where T : IProxyValidator
    {
        var resolvedValidators = new List<T>();
        foreach (var existing in this.validators)
        {
            if (existing is T resolved)
            {
                resolvedValidators.Add(resolved);
            }
        }

        validators = resolvedValidators;
        return resolvedValidators.Count > 0;
    }

    /// <summary>
    /// Возвращает все валидаторы указанного типа.
    /// </summary>
    /// <typeparam name="T">Тип валидатора.</typeparam>
    /// <returns>Последовательность найденных валидаторов.</returns>
    public IEnumerable<IProxyValidator> GetAllValidators<T>() where T : IProxyValidator
    {
        var resolvedValidators = new List<IProxyValidator>();
        foreach (var existing in validators)
        {
            if (existing is T)
            {
                resolvedValidators.Add(existing);
            }
        }

        return resolvedValidators;
    }

    /// <summary>
    /// Загружает актуальный набор прокси для внутреннего пула сервиса.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected abstract ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Проверяет, не был ли сервис уже освобождён.
    /// </summary>
    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(isDisposed, GetType().Name);

    async ValueTask<ServiceProxy[]> IProxyPoolSnapshotSource.GetPoolSnapshotAsync(CancellationToken cancellationToken)
        => await EnsurePoolSnapshotAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask<ServiceProxy[]> EnsurePoolSnapshotAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var snapshot = Volatile.Read(ref pool);
        var shouldRefresh = snapshot.Length == 0
            || LastRefreshUtc == default
            || (RefreshInterval > TimeSpan.Zero && DateTime.UtcNow - LastRefreshUtc >= RefreshInterval);

        if (shouldRefresh)
        {
            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (PreservePoolOnRefreshFailure)
            {
                if (Volatile.Read(ref pool).Length == 0)
                {
                    throw;
                }
            }
        }

        return Volatile.Read(ref pool);
    }

    private IEnumerable<ServiceProxy> Select(ServiceProxy[] snapshot, int count, Func<ServiceProxy, bool> filter)
    {
        if (snapshot.Length == 0)
        {
            return [];
        }

        return ProxyPoolSelection.Select(snapshot, count, filter, RotationStrategy, ref nextProxyIndex);
    }

    private async ValueTask<ServiceProxy?> SelectSingleAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken)
    {
        var snapshot = await EnsurePoolSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Length == 0)
        {
            return null;
        }

        return ProxyPoolSelection.SelectSingle(snapshot, filter, RotationStrategy, ref nextProxyIndex);
    }

    private async ValueTask<ServiceProxy[]> MaterializePoolAsync(IEnumerable<ServiceProxy> proxies, CancellationToken cancellationToken)
    {
        if (proxies is ServiceProxy[] array)
        {
            return await FilterNullAndDeduplicateAsync(array, cancellationToken).ConfigureAwait(false);
        }

        if (proxies is ICollection<ServiceProxy> collection)
        {
            return await CopyNonNullDistinctAsync(collection, collection.Count, cancellationToken).ConfigureAwait(false);
        }

        return await CopyNonNullDistinctAsync(proxies, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ServiceProxy[]> FilterNullAndDeduplicateAsync(ServiceProxy[] source, CancellationToken cancellationToken)
    {
        if (source.Length <= 1)
        {
            return source;
        }

        return await CopyNonNullDistinctAsync(source, source.Length, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ServiceProxy[]> CopyNonNullDistinctAsync(
        IEnumerable<ServiceProxy> source,
        int capacity = 0,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<ServiceProxy>(capacity);
        var keyTasks = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in source)
        {
            if (proxy is null)
            {
                continue;
            }

            candidates.Add(proxy);
            var host = proxy.Host ?? string.Empty;
            if (!keyTasks.ContainsKey(host))
            {
                keyTasks.Add(host, DedupKeyResolver.GetKeyAsync(proxy, cancellationToken).AsTask());
            }
        }

        await Task.WhenAll(keyTasks.Values).ConfigureAwait(false);

        var result = new List<ServiceProxy>(candidates.Count);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidates.Count; index++)
        {
            var proxy = candidates[index];
            var host = proxy.Host ?? string.Empty;
            if (seenKeys.Add(keyTasks[host].Result))
            {
                result.Add(proxy);
            }
        }

        return [.. result];
    }


    private void RemoveValidators(Func<IProxyValidator, bool> shouldRemove)
    {
        var retainedValidators = new List<IProxyValidator>();
        foreach (var existing in validators)
        {
            if (!shouldRemove(existing))
            {
                retainedValidators.Add(existing);
            }
        }

        validators.Reset();
        validators.AddRange(retainedValidators);
    }

    private void EnsureRefreshLoopStarted()
    {
        if (RefreshInterval <= TimeSpan.Zero)
        {
            return;
        }

        if (Volatile.Read(ref refreshLoopSource) is not null)
        {
            return;
        }

        var source = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref refreshLoopSource, source, null) is not null)
        {
            source.Dispose();
            return;
        }

        refreshLoopTask = RunRefreshLoopAsync(source.Token);
    }

    private Task<ServiceProxy[]> GetOrStartRefreshTask(CancellationToken cancellationToken)
    {
        while (true)
        {
            var existingTask = Volatile.Read(ref refreshTask);
            if (existingTask is not null)
            {
                return existingTask;
            }

            var refreshCompletion = new TaskCompletionSource<ServiceProxy[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref refreshTask, refreshCompletion.Task, null) is not null)
            {
                continue;
            }

            ExecuteRefreshAsync(refreshCompletion, cancellationToken);
            return refreshCompletion.Task;
        }
    }

    private async void ExecuteRefreshAsync(TaskCompletionSource<ServiceProxy[]> refreshCompletion, CancellationToken cancellationToken)
    {
        try
        {
            var updatedPool = await LoadPoolAsync(cancellationToken).ConfigureAwait(false);
            var materializedPool = await MaterializePoolAsync(updatedPool, cancellationToken).ConfigureAwait(false);

            Interlocked.Exchange(ref pool, materializedPool);
            if (Volatile.Read(ref nextProxyIndex) >= materializedPool.Length)
            {
                Interlocked.Exchange(ref nextProxyIndex, -1);
            }

            LastRefreshUtc = DateTime.UtcNow;
            LastRefreshException = null;
            EnsureRefreshLoopStarted();
            refreshCompletion.SetResult(materializedPool);
        }
        catch (Exception ex)
        {
            LastRefreshException = ex;

            if (!PreservePoolOnRefreshFailure || Volatile.Read(ref pool).Length == 0)
            {
                refreshCompletion.SetException(ex);
                return;
            }

            refreshCompletion.SetResult(Volatile.Read(ref pool));
        }
        finally
        {
            Interlocked.CompareExchange(ref refreshTask, null, refreshCompletion.Task);
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(RefreshErrorBackoff, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

#pragma warning restore CS4014