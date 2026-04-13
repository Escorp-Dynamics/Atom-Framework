namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для iplocate/free-proxy-list.
/// </summary>
public sealed class IplocateProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = IplocateProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Тип списка: http, https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "https";
}