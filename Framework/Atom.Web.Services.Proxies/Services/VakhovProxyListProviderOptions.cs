namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для vakhov/fresh-proxy-list.
/// </summary>
public sealed class VakhovProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = VakhovProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Тип списка: http, https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "https";
}