using Atom.Web.Browsers.NativeMessaging.Signals;

namespace Atom.Web.Browsers.NativeMessaging;

/// <summary>
/// Представляет базовый интерфейс для сервера веб-браузера.
/// </summary>
public interface IWebBrowserServer : IAsyncDisposable
{
    /// <summary>
    /// Манифест сервера.
    /// </summary>
    Manifest Manifest { get; }

    /// <summary>
    /// Определяет, запущен ли сервер.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Происходит в момент запуска сервера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer>? Started;

    /// <summary>
    /// Происходит в момент остановки сервера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer>? Stopped;

    /// <summary>
    /// Происходит в момент получения сигнала с клиента.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer, SignalReceivedAsyncEventArgs>? SignalReceived;

    /// <summary>
    /// Происходит в момент неудачного получения сигнала.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer, FailedEventArgs>? SignalReceiveFailed;

    /// <summary>
    /// Запускает сервер веб-браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Запускает сервер веб-браузера.
    /// </summary>
    ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <summary>
    /// Останавливает сервер веб-браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Останавливает сервер веб-браузера.
    /// </summary>
    ValueTask StopAsync() => StopAsync(CancellationToken.None);
}