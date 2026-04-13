namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация endpoint для hide-my-name proxy list.
/// </summary>
public sealed class HideMyNameProxyListProviderOptions
{
    /// <summary>
    /// Максимальное количество стартов запросов в секунду для одного provider instance.
    /// </summary>
    public int RequestsPerSecondLimit { get; init; } = HideMyNameProxyListProvider.DefaultRequestsPerSecondLimit;

    /// <summary>
    /// Upstream country filter в формате hide-my-name, например USJPDE.
    /// </summary>
    public string? CountryFilter { get; init; }

    /// <summary>
    /// Максимально допустимое время ответа в миллисекундах.
    /// </summary>
    public int MaximumSpeedMilliseconds { get; init; }

    /// <summary>
    /// Upstream type filter в формате hide-my-name, например h, s, 4, 5 или их комбинация.
    /// </summary>
    public string? TypeFilter { get; init; }

    /// <summary>
    /// Upstream anonymity filter в формате hide-my-name, например 4 или 34.
    /// </summary>
    public string? AnonymityFilter { get; init; }

    /// <summary>
    /// Смещение стартовой страницы.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Загружать все страницы hide-my-name через пагинацию start.
    /// </summary>
    public bool FetchAllPages { get; init; }
}