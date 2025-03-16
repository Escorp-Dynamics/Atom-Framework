using Atom.Web.Proxies.Services;
using Atom.Web.Services;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет фабрику 
/// </summary>
public class ProxyFactory : WebServiceFactory<IProxyService, ProxyFactory>, IProxyFactory<IProxyService, ProxyFactory>
{

}