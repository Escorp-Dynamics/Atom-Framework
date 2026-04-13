namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для Zaeem20/FREE_PROXIES_LIST.
/// </summary>
public sealed class ZaeemProxyListProviderOptions
{
    /// <summary>
    /// Целевой лимит старта HTTP-запросов в секунду для инстанса provider-а.
    /// </summary>
    public int RequestsPerSecondLimit { get; set; } = ZaeemProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Целевой протокол списка. Поддерживаются http, https и socks4.
    /// </summary>
    public string? Protocol { get; set; }
}