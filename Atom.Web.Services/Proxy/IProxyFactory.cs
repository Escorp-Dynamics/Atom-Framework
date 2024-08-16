namespace Atom.Web.Services.Proxy;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики прокси.
/// </summary>
public interface IProxyFactory : IWebServiceFactory<IProxyFactory, IProxyService>
{
    /// <summary>
    /// Подключает сервис получения прокси.
    /// </summary>
    /// <typeparam name="TProxyService">Тип сервиса прокси.</typeparam>
    /// <param name="service">Экземпляр сервиса прокси.</param>
    /// <returns>Текущий экземпляр фабрики прокси.</returns>
    IProxyFactory UseService<TProxyService>(TProxyService service) where TProxyService : IProxyService, new();

    /// <summary>
    /// Подключает сервис получения прокси.
    /// </summary>
    /// <typeparam name="TProxyService">Тип сервиса прокси.</typeparam>
    /// <returns>Текущий экземпляр фабрики прокси.</returns>
    IProxyFactory UseService<TProxyService>() where TProxyService : IProxyService, new() => UseService(new TProxyService());
}