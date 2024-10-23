using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет базовый интерфейс для реализации контекста веб-браузера.
/// </summary>
public interface IWebBrowserContext : IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage>? PageOpened;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage>? PageClosed;

    /// <summary>
    /// Происходит в момент закрытия контекста.
    /// </summary>
    event AsyncEventHandler? Closed;

    /// <summary>
    /// Настройки контекста веб-браузера.
    /// </summary>
    IWebBrowserContextSettings Settings { get; }

    /// <summary>
    /// Экземпляр веб-браузера, в котором был создан текущий контекст.
    /// </summary>
    IWebBrowser Browser { get; }

    /// <summary>
    /// Коллекция веб-страниц.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Текущая активная веб-страница.
    /// </summary>
    IWebPage CurrentPage { get; }

    /// <summary>
    /// Указывает, был ли контекст веб-браузера закрыт.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Исходный код текущей загруженной веб-страницы.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Консоль отладки текущей страницы.
    /// </summary>
    IConsole Console { get; }

    /// <summary>
    /// Куки текущей страницы.
    /// </summary>
    CookieContainer Cookies { get; }

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="settings">Настройки веб-страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="settings">Настройки веб-страницы.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken) => OpenPageAsync(WebPageSettings.FromContextSettings(Settings), cancellationToken);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync() => OpenPageAsync(CancellationToken.None);

    /// <summary>
    /// Открывает адрес в текущей странице.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа главной страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает адрес в текущей странице.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <returns>Статус-код ответа главной страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <summary>
    /// Закрывает контекст веб-браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает контекст веб-браузера.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}