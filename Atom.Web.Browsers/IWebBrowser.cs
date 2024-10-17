using System.Net;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет базовый интерфейс для реализации веб-браузеров.
/// </summary>
public interface IWebBrowser : IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент создания контекста веб-браузера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserContext>? ContextCreated;

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserContext>? ContextClosed;

    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage>? PageOpened;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage>? PageClosed;

    /// <summary>
    /// Настройки веб-браузера.
    /// </summary>
    IWebBrowserSettings Settings { get; }

    /// <summary>
    /// Текущие активные контексты веб-браузера.
    /// </summary>
    IEnumerable<IWebBrowserContext> Contexts { get; }

    /// <summary>
    /// Текущие активные веб-страницы браузера.
    /// </summary>
    /// <value></value>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Текущий экземпляр контекста веб-браузера.
    /// </summary>
    IWebBrowserContext CurrentContext { get; }

    /// <summary>
    /// Текущий экземпляр веб-страницы.
    /// </summary>
    IWebPage CurrentPage { get; }

    /// <summary>
    /// Куки текущей страницы.
    /// </summary>
    CookieContainer Cookies { get; }

    /// <summary>
    /// Консоль отладки текущей страницы.
    /// </summary>
    IConsole Console { get; }

    /// <summary>
    /// Указывает, был ли веб-браузер закрыт.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Исходный код текущей загруженной веб-страницы.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Создает новый контекст браузера.
    /// </summary>
    /// <param name="contextSettings">Настройки контекста браузера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новый контекст браузера.</returns>
    ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings, CancellationToken cancellationToken);

    /// <summary>
    /// Создает новый контекст браузера.
    /// </summary>
    /// <param name="contextSettings">Настройки контекста браузера.</param>
    /// <returns>Новый контекст браузера.</returns>
    ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings) => CreateContextAsync(contextSettings, CancellationToken.None);

    /// <summary>
    /// Создает новый контекст браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новый контекст браузера.</returns>
    ValueTask<IWebBrowserContext> CreateContextAsync(CancellationToken cancellationToken) => CreateContextAsync(WebBrowserContextSettings.FromBrowserSettings(Settings), cancellationToken);

    /// <summary>
    /// Создает новый контекст браузера.
    /// </summary>
    /// <returns>Новый контекст браузера.</returns>
    ValueTask<IWebBrowserContext> CreateContextAsync() => CreateContextAsync(CancellationToken.None);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="pageSettings">Настройки веб-страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings pageSettings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="pageSettings">Настройки веб-страницы.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings pageSettings) => OpenPageAsync(pageSettings, CancellationToken.None);

    /// <summary>
    /// Открывает новую веб-страницу.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новая веб-страница.</returns>
    ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken) => OpenPageAsync(WebPageSettings.Default, cancellationToken);

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
    /// Закрывает браузер.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает браузер.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}