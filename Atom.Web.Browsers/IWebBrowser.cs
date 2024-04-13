using Atom.Web.Browsers.BOM;
using Atom.Web.Browsers.NativeMessaging;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет базовый интерфейс для реализации браузеров.
/// </summary>
/// <typeparam name="TSettings">Тип настроек браузера.</typeparam>
/// <typeparam name="TServer">Тип сервера браузера.</typeparam>
public interface IWebBrowser<TSettings, TServer> : IAsyncDisposable
    where TSettings : IWebBrowserSettings
    where TServer : IWebBrowserServer
{
    /// <summary>
    /// Сервер браузера.
    /// </summary>
    TServer Server { get; set; }

    /// <summary>
    /// Настройки браузера.
    /// </summary>
    TSettings Settings { get; }

    /// <summary>
    /// Определяет, запущен ли браузер.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Определяет, запущен ли браузер в режиме администратора.
    /// </summary>
    bool IsRunningAsAdmin { get; }

    /// <summary>
    /// Происходит в момент запуска процесса браузера.
    /// </summary>
    event AsyncEventHandler<IWebBrowser<TSettings, TServer>, WebBrowserProcessAsyncEventArgs>? ProcessStarted;

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Экземпляр окна браузера.</returns>
    ValueTask<IWindow> OpenWindowAsync(TSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    /// <returns>Экземпляр окна браузера.</returns>
    ValueTask<IWindow> OpenWindowAsync(TSettings settings) => OpenWindowAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Экземпляр окна браузера.</returns>
    ValueTask<IWindow> OpenWindowAsync(CancellationToken cancellationToken) => OpenWindowAsync(Settings, cancellationToken);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <returns>Экземпляр окна браузера.</returns>
    ValueTask<IWindow> OpenWindowAsync() => OpenWindowAsync(CancellationToken.None);
}