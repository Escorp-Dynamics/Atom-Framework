using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации окна браузера.
/// </summary>
public interface IWebWindow : IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    event MutableEventHandler<IWebPage>? PageOpened;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event MutableEventHandler<IWebPage>? PageClosed;

    /// <summary>
    /// Происходит в момент закрытия окна браузера.
    /// </summary>
    event MutableEventHandler? Closed;

    /// <summary>
    /// Настройки окна браузера.
    /// </summary>
    IWebWindowSettings Settings { get; }

    /// <summary>
    /// Контекст браузера, в рамках которого запущено текущее окно.
    /// </summary>
    IWebBrowserContext Context { get; }

    /// <summary>
    /// Веб-браузер, в котором открыто окно.
    /// </summary>
    IWebBrowser Browser => Context.Browser;

    /// <summary>
    /// Страницы, открытые в рамках контекста текущего окна браузера.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Текущая активная веб-страница.
    /// </summary>
    IWebPage CurrentPage { get; }

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
    /// Определяет, было ли окно закрыто.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="settings">Настройки страницы браузера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="settings">Настройки страницы браузера.</param>
    ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    ValueTask<IWebPage> OpenPageAsync() => OpenPageAsync(CancellationToken.None);

    /// <summary>
    /// Закрывает окно браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает окно браузера.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}