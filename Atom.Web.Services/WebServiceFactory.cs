
namespace Atom.Web.Services;

/// <summary>
/// Представляет базовую реализацию фабрики веб-сервисов.
/// </summary>
/// <typeparam name="TFactory">Тип фабрики.</typeparam>
/// <typeparam name="TService">Тип сервиса.</typeparam>
public abstract class WebServiceFactory<TFactory, TService> : IWebServiceFactory<TFactory, TService>
    where TFactory : IFactory
    where TService : IWebService
{
    private bool isDisposed;

    /// <summary>
    /// Блокировщик.
    /// </summary>
    protected SemaphoreSlim Locker { get; } = new(1, 1);

    /// <inheritdoc/>
    public IEnumerable<TService> Services { get; protected set; } = [];

    /// <summary>
    /// Освобождает неуправляемые ресурсы и выполняет другие задачи очистки.
    /// </summary>
    /// <param name="disposing">Указывает, вызывается ли метод из метода Dispose или из финализатора.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;
        isDisposed = true;

        if (disposing)
        {
            foreach (var service in Services) service?.Dispose();
            Locker.Dispose();
        }
    }

    /// <inheritdoc/>
    public IModular<TService> Use<T>(T module) where T : TService
    {
        Services = Services.Append(module);
        return this;
    }

    /// <inheritdoc/>
    public IModular<TService> Use<T>() where T : TService, new()
    {
        if (!Services.Any(x => x.GetType().FullName!.Equals(typeof(T).FullName))) Services = Services.Append(new T());
        return this;
    }

    /// <inheritdoc/>
    public IModular<TService> UnUse<T>(T? module) where T : TService
    {
        if (module is null) return this;

        var tmp = Services.ToList();
        if (tmp.Remove(module)) Services = tmp.AsEnumerable();

        return this;
    }

    /// <inheritdoc/>
    public IModular<TService> UnUse<T>() where T : TService => UnUse(Get<T>());

    /// <inheritdoc/>
    public TService? Get<T>() where T : TService => GetAll<T>().FirstOrDefault();

    /// <inheritdoc/>
    public IEnumerable<TService> GetAll<T>() where T : TService => Services.Where(x => x.GetType().FullName!.Equals(typeof(T).FullName!));

    /// <summary>
    /// Выполняет освобождение неуправляемых ресурсов и других задач очистки.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}