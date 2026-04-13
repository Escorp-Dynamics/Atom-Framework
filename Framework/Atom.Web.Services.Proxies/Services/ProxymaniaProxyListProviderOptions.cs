namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для HTML-списка ProxyMania.
/// </summary>
public sealed class ProxymaniaProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = ProxymaniaProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Тип прокси: all, http, https, socks4 или socks5.
    /// </summary>
    public string Protocol { get; init; } = "all";

    /// <summary>
    /// Двухбуквенный ISO-код страны, например US.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Максимально допустимое время ответа в миллисекундах.
    /// </summary>
    public int MaximumSpeedMilliseconds { get; init; }

    /// <summary>
    /// Номер стартовой страницы.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Загружать все страницы HTML-ленты через пагинацию Next.
    /// </summary>
    public bool FetchAllPages { get; init; }
}