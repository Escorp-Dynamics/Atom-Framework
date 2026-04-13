namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для roosterkid/openproxylist.
/// </summary>
public sealed class OpenProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = OpenProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Тип списка: https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "https";
}