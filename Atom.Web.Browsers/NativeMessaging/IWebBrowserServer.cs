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
    /// Происходит в момент запуска сервера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer>? Started;

    /// <summary>
    /// Происходит в момент остановки сервера.
    /// </summary>
    event AsyncEventHandler<IWebBrowserServer>? Stopped;

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