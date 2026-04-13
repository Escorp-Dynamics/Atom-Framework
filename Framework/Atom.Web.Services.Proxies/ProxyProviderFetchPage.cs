namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет одну страницу provider feed и continuation token для следующего запроса.
/// </summary>
/// <param name="Proxies">Прокси, полученные на текущей странице.</param>
/// <param name="ContinuationToken">Токен следующей страницы или <see langword="null"/>, если feed завершён.</param>
public sealed record ProxyProviderFetchPage(IReadOnlyList<ServiceProxy> Proxies, string? ContinuationToken = null);