namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для ProxyScrape.
/// </summary>
public sealed class ProxyScrapeProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = ProxyScrapeProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Протокол прокси.
    /// </summary>
    public string Protocol { get; init; } = "http";

    /// <summary>
    /// Таймаут проверки в миллисекундах.
    /// </summary>
    public int TimeoutMilliseconds { get; init; } = 15000;

    /// <summary>
    /// ISO-2 код страны или all.
    /// </summary>
    public string Country { get; init; } = "all";

    /// <summary>
    /// Требование SSL: yes, no или all.
    /// </summary>
    public string Ssl { get; init; } = "all";

    /// <summary>
    /// Уровень анонимности: all, elite, anonymous, transparent.
    /// </summary>
    public string Anonymity { get; init; } = "all";
}