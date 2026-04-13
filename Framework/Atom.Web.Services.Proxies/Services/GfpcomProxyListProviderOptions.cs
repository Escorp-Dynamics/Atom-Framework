namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для gfpcom/free-proxy-list.
/// </summary>
public sealed class GfpcomProxyListProviderOptions
{
    /// <summary>
    /// Целевой лимит старта HTTP-запросов в секунду для инстанса provider-а.
    /// </summary>
    public int RequestsPerSecondLimit { get; set; } = GfpcomProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Целевой протокол списка. Поддерживаются http, https, socks4 и socks5.
    /// </summary>
    public string? Protocol { get; set; }
}