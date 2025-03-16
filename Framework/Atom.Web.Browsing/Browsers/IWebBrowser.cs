using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации веб-браузеров.
/// </summary>
public interface IWebBrowser : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент создания контекста веб-браузера.
    /// </summary>
    event MutableEventHandler<IWebBrowserContext>? ContextCreated;

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    event MutableEventHandler<IWebBrowserContext>? ContextDestroyed;

    /// <summary>
    /// Происходит в момент открытия окна браузера.
    /// </summary>
    event MutableEventHandler<IWebWindow>? WindowOpened;

    /// <summary>
    /// Происходит в момент закрытия окна браузера.
    /// </summary>
    event MutableEventHandler<IWebWindow>? WindowClosed;

    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    event MutableEventHandler<IWebPage>? PageOpened;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event MutableEventHandler<IWebPage>? PageClosed;

    /// <summary>
    /// Настройки веб-браузера.
    /// </summary>
    IWebBrowserSettings Settings { get; }

    /// <summary>
    /// Текущие активные контексты веб-браузера.
    /// </summary>
    IEnumerable<IWebBrowserContext> Contexts { get; }

    /// <summary>
    /// Текущие активные окна веб-браузера.
    /// </summary>
    /// <value></value>
    IEnumerable<IWebWindow> Windows { get; }

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
    /// Текущий экземпляр окна веб-браузера.
    /// </summary>
    IWebWindow CurrentWindow { get; }

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
    ValueTask<IWebBrowserContext> CreateContextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Создает новый контекст браузера.
    /// </summary>
    /// <returns>Новый контекст браузера.</returns>
    ValueTask<IWebBrowserContext> CreateContextAsync() => CreateContextAsync(CancellationToken.None);

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