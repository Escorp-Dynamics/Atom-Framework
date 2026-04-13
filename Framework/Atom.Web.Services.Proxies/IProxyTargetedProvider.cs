using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет провайдера, который умеет принимать selection hints и возвращать целевой batch без полного snapshot fetch.
/// </summary>
public interface IProxyTargetedProvider
{
    /// <summary>
    /// Возвращает целевой набор прокси по структурированному запросу.
    /// </summary>
    ValueTask<ProxyProviderFetchResult> FetchAsync(ProxyProviderFetchRequest request, CancellationToken cancellationToken);
}