namespace Atom.Web.Proxies.Services;

/// <summary>
/// Явная конфигурация выборки для ProxyNova proxylist API.
/// </summary>
public sealed class ProxyNovaProviderOptions
{
    /// <summary>
    /// ISO-2 код страны. Игнорируется, если указаны координаты near.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Координаты для фильтра near. При указании подавляют country.
    /// </summary>
    public ProxyNovaNearLocation? Near { get; init; }

    /// <summary>
    /// Размер выборки. Будет нормализован в диапазон 1..1000.
    /// </summary>
    public int Limit { get; init; } = ProxyNovaProvider.DefaultLimit;
}