namespace Atom.Web.Proxies.Services;

/// <summary>
/// Координаты для фильтра near в ProxyNova proxylist API.
/// </summary>
public readonly record struct ProxyNovaNearLocation(double Latitude, double Longitude);