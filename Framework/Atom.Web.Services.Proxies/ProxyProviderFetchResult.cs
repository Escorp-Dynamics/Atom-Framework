namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет результат targeted provider fetch.
/// </summary>
public sealed record ProxyProviderFetchResult(
    IReadOnlyList<ServiceProxy> Proxies,
    bool IsPartial = false,
    bool SourceExhausted = false);