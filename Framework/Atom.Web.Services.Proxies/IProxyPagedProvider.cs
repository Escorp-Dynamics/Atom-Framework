namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет провайдера, который получает данные частями через continuation/page cursor.
/// </summary>
public interface IProxyPagedProvider
{
    /// <summary>
    /// Загружает следующую страницу provider feed.
    /// </summary>
    /// <param name="continuationToken">Токен продолжения предыдущей страницы или <see langword="null"/> для первой загрузки.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<ProxyProviderFetchPage> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken);
}