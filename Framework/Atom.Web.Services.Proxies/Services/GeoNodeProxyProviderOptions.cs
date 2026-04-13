namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для GeoNode proxy-list API.
/// </summary>
public sealed class GeoNodeProxyProviderOptions
{
    /// <summary>
    /// Размер страницы.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Номер страницы.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Загружать все страницы GeoNode до исчерпания total.
    /// </summary>
    public bool FetchAllPages { get; init; }

    /// <summary>
    /// Максимальная частота старта page-request при FetchAllPages.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = GeoNodeProxyProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Максимальное число повторов при retryable ответах GeoNode вроде 429/5xx.
    /// </summary>
    public int RetryAttempts { get; init; } = GeoNodeProxyProvider.DefaultRetryAttempts;

    /// <summary>
    /// Базовая задержка между повторами, если upstream не прислал Retry-After.
    /// </summary>
    public int RetryDelayMilliseconds { get; init; } = GeoNodeProxyProvider.DefaultRetryDelayMilliseconds;

    /// <summary>
    /// Поле сортировки.
    /// </summary>
    public string SortBy { get; init; } = "lastChecked";

    /// <summary>
    /// Направление сортировки.
    /// </summary>
    public string SortType { get; init; } = "desc";
}