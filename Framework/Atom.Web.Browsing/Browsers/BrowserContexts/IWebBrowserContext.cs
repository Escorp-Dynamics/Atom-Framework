using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации контекста веб-браузера.
/// </summary>
public interface IWebBrowserContext : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    event MutableEventHandler<IWebWindow>? WindowOpened;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
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
    /// Происходит в момент закрытия контекста.
    /// </summary>
    event MutableEventHandler? Destroyed;

    /// <summary>
    /// Настройки контекста веб-браузера.
    /// </summary>
    IWebBrowserContextSettings Settings { get; }

    /// <summary>
    /// Экземпляр веб-браузера, в котором был создан текущий контекст.
    /// </summary>
    IWebBrowser Browser { get; }

    /// <summary>
    /// Коллекция окон браузера.
    /// </summary>
    IEnumerable<IWebWindow> Windows { get; }

    /// <summary>
    /// Коллекция веб-страниц.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Текущее активное окно браузера.
    /// </summary>
    IWebWindow CurrentWindow { get; }

    /// <summary>
    /// Текущая активная веб-страница.
    /// </summary>
    IWebPage CurrentPage { get; }

    /// <summary>
    /// Указывает, был ли контекст веб-браузера закрыт.
    /// </summary>
    bool IsDestroyed { get; }

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
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="settings">Настройки окна браузера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новое окно браузера.</returns>
    ValueTask<IWebWindow> OpenWindowAsync(IWebWindowSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="settings">Настройки окна браузера.</param>
    /// <returns>Новое окно браузера.</returns>
    ValueTask<IWebWindow> OpenWindowAsync(IWebPageSettings settings) => OpenWindowAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новое окно браузера.</returns>
    ValueTask<IWebWindow> OpenWindowAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <returns>Новое окно браузера.</returns>
    ValueTask<IWebWindow> OpenWindowAsync() => OpenWindowAsync(CancellationToken.None);

    /// <summary>
    /// Закрывает контекст веб-браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask DestroyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает контекст веб-браузера.
    /// </summary>
    ValueTask DestroyAsync() => DestroyAsync(CancellationToken.None);
}