namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для r00tee/Proxy-List.
/// </summary>
public sealed class R00teeProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = R00teeProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Тип списка: https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "https";
}