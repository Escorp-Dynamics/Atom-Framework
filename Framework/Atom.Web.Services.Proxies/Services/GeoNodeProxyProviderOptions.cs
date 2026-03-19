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
    /// Поле сортировки.
    /// </summary>
    public string SortBy { get; init; } = "lastChecked";

    /// <summary>
    /// Направление сортировки.
    /// </summary>
    public string SortType { get; init; } = "desc";
}