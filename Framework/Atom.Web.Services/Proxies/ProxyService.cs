using Atom.Collections;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовую реализацию сервиса прокси.
/// </summary>
public abstract class ProxyService : IProxyService
{
    private readonly SparseArray<IProxyValidator> validators = new(1024);

    private bool isDisposed;

    /// <summary>
    /// Происходит в момент высвобождения ресурсов.
    /// </summary>
    /// <param name="disposing">Указывает, следует ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;
    }

    /// <inheritdoc/>
    public abstract ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ServiceProxy> GetAsync() => GetAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public IProxyService UseValidator<T>(T validator) where T : IProxyValidator
    {
        validators.Add(validator);
        return this;
    }

    /// <inheritdoc/>
    public IProxyService UseValidator<T>() where T : IProxyValidator, new()
    {
        if (!TryGetValidator<T>(out _)) validators.Add(T.Rent<T>());
    }
    public IProxyService UnUseValidator<T>(T validator) where T : IProxyValidator => throw new NotImplementedException();
    public IProxyService UnUseValidator<T>() where T : IProxyValidator => throw new NotImplementedException();
    public bool TryGetValidator<T>(out T? validator) where T : IProxyValidator => throw new NotImplementedException();
    public IProxyValidator? GetValidator<T>() where T : IProxyValidator => throw new NotImplementedException();
    public bool TryGetAllValidators<T>(out IEnumerable<T> validators) where T : IProxyValidator => throw new NotImplementedException();
    public IEnumerable<IProxyValidator> GetAllValidators<T>() where T : IProxyValidator => throw new NotImplementedException();
}