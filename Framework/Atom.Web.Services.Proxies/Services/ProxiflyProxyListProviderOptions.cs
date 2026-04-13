namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для proxifly/free-proxy-list.
/// </summary>
public sealed class ProxiflyProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = ProxiflyProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Фильтр по типу списка: all, http, https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "all";
}