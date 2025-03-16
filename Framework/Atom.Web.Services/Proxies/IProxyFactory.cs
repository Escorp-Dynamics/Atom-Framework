using Atom.Web.Proxies.Services;
using Atom.Web.Services;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики прокси.
/// </summary>
public interface IProxyFactory<TProxyService, out TProxyFactory> : IWebServiceFactory<TProxyService, TProxyFactory>
    where TProxyService : IProxyService
    where TProxyFactory : IProxyFactory<TProxyService, TProxyFactory>
{
    //ValueTask<TProxy> GetAsync(CancellationToken cancellationToken);
}