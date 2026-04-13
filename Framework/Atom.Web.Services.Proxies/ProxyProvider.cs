using System.Net;
using Atom.Architect.Components;
using Atom.Collections;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовую реализацию fetch-only провайдера прокси.
/// </summary>
[Component]
public abstract partial class ProxyProvider : IProxyProvider, IProxyPoolSnapshotSource
{
    private readonly SparseArray<IProxyValidator> validators = new(1024);

    private bool isDisposed;

    /// <summary>
    /// Инициализирует базовый provider surface.
    /// </summary>
    protected ProxyProvider(ILogger? logger = null)
    {
        Logger = logger;
    }

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IEnumerable<IProxyValidator> Validators => validators;

    /// <summary>
    /// Resolver, определяющий dedup key для элементов снимка, который провайдер отдаёт фабрике.
    /// </summary>
    public IProxyDedupKeyResolver DedupKeyResolver { get; set; } = ProxyDedupKeyResolvers.Literal;

    /// <summary>
    /// Происходит в момент высвобождения ресурсов.
    /// </summary>
    /// <param name="disposing">Указывает, следует ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(location1: ref isDisposed, value: true, comparand: default)) return;
    }

    /// <inheritdoc/>
    public virtual async ValueTask<IEnumerable<ServiceProxy>> FetchAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = this is IProxyPagedProvider pagedProvider
            ? await FetchPagedSnapshotAsync(pagedProvider, cancellationToken).ConfigureAwait(false)
            : await LoadPoolAsync(cancellationToken).ConfigureAwait(false);
        return await MaterializePoolAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ServiceProxy>> FetchAsync() => FetchAsync(CancellationToken.None);

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
    /// Загружает актуальный набор прокси из внешнего источника провайдера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected abstract ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Проверяет, не был ли сервис уже освобождён.
    /// </summary>
    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(isDisposed, GetType().Name);

    async ValueTask<ServiceProxy[]> IProxyPoolSnapshotSource.GetPoolSnapshotAsync(CancellationToken cancellationToken)
        => [.. await FetchAsync(cancellationToken).ConfigureAwait(false)];

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

    private static async ValueTask<IEnumerable<ServiceProxy>> FetchPagedSnapshotAsync(IProxyPagedProvider pagedProvider, CancellationToken cancellationToken)
    {
        var collected = new List<ServiceProxy>();
        var visitedTokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;

        while (true)
        {
            var visitedKey = continuationToken ?? string.Empty;
            if (!visitedTokens.Add(visitedKey))
            {
                throw new InvalidOperationException("Провайдер вернул циклический continuation token.");
            }

            var page = await pagedProvider.FetchPageAsync(continuationToken, cancellationToken).ConfigureAwait(false);
            if (page.Proxies is { Count: > 0 })
            {
                collected.AddRange(page.Proxies);
            }

            if (string.IsNullOrWhiteSpace(page.ContinuationToken))
            {
                return collected;
            }

            continuationToken = page.ContinuationToken;
        }
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
}