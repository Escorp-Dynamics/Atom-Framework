using Atom.Architect.Components;
using Atom.Architect.Factories;
using Atom.Web.Proxies.Services;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики прокси-провайдеров.
/// </summary>
public interface IProxyFactory<TProxyProvider, out TProxyFactory> : IComponentOwner<TProxyFactory>, IAsyncFactory<ServiceProxy>, IDisposable
    where TProxyProvider : IProxyProvider
    where TProxyFactory : IProxyFactory<TProxyProvider, TProxyFactory>
{
    /// <summary>
    /// Коллекция подключенных провайдеров.
    /// </summary>
    IEnumerable<TProxyProvider> Services { get; }
}