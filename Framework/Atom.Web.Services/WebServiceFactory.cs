using Atom.Collections;

namespace Atom.Web.Services;

/// <summary>
/// Представляет базовую реализацию фабрики веб-сервисов.
/// </summary>
/// <typeparam name="TService">Тип сервиса.</typeparam>
/// <typeparam name="TFactory">Тип фабрики.</typeparam>
public abstract class WebServiceFactory<TService, TFactory> : IWebServiceFactory<TService, TFactory>
    where TService : IWebService
    where TFactory : IFactory
{
    private readonly SparseArray<TService> services = new(1024);
    private bool isDisposed;

    /// <inheritdoc/>
    public IEnumerable<TService> Services => services;

    /// <summary>
    /// Освобождает неуправляемые ресурсы и выполняет другие задачи очистки.
    /// </summary>
    /// <param name="disposing">Указывает, вызывается ли метод из метода Dispose или из финализатора.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        foreach (var service in services) service?.Dispose();
        services.Release();
    }

    /// <inheritdoc/>
    public TFactory Use<T>(T module) where T : TService
    {
        services.Add(module);
        return (TFactory)this;
    }

    /// <inheritdoc/>
    public TFactory Use<T>() where T : TService, new()
    {
        if (!TryGet<T>(out _)) services.Add(T.Rent<T>());
        return (TFactory)this;
    }

    /// <inheritdoc/>
    public TFactory UnUse<T>(T module) where T : TService => throw new NotImplementedException();

    /// <inheritdoc/>
    public TFactory UnUse<T>() where T : TService => throw new NotImplementedException();

    /// <inheritdoc/>
    public bool TryGet<T>(out T? module) where T : TService => throw new NotImplementedException();

    /// <inheritdoc/>
    public TService? Get<T>() where T : TService => throw new NotImplementedException();

    /// <inheritdoc/>
    public bool TryGetAll<T>(out IEnumerable<T> modules) where T : TService => throw new NotImplementedException();

    /// <inheritdoc/>
    public IEnumerable<TService> GetAll<T>() where T : TService => throw new NotImplementedException();

    /// <summary>
    /// Выполняет освобождение неуправляемых ресурсов и других задач очистки.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}