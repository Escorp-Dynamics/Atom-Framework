using System.Net;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет веб-страницу браузера.
/// </summary>
public interface IWebPage : IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент начала навигации по странице.
    /// </summary>
    event AsyncEventHandler<Uri>? Navigate;

    /// <summary>
    /// Происходит в момент окончания навигации по странице.
    /// </summary>
    event AsyncEventHandler<Uri>? Navigated;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event AsyncEventHandler? Closed;

    /// <summary>
    /// Контекст веб-страницы браузера.
    /// </summary>
    IWebBrowserContext Context { get; }

    /// <summary>
    /// Настройки веб-страницы.
    /// </summary>
    IWebPageSettings Settings { get; }

    /// <summary>
    /// Указывает, была ли веб-страница закрыта.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Исходный код веб-страницы браузера.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Текущий адрес страницы.
    /// </summary>
    Uri UrL { get; }

    /// <summary>
    /// Консоль отладки страницы.
    /// </summary>
    IConsole Console { get; }

    /// <summary>
    /// Куки страницы.
    /// </summary>
    CookieContainer Cookies { get; }

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа главной страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <returns>Статус-код ответа главной страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <summary>
    /// Закрывает веб-страницу.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает веб-страницу.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}