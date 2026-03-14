using System.Text.Json;

namespace Atom.Net.Browsing;

/// <summary>
/// Представляет вкладку браузера.
/// </summary>
public interface IWebPage : IDomContext, IAsyncDisposable
{
    /// <summary>
    /// Главный фрейм страницы.
    /// Все DOM-операции (поиск элементов, выполнение скриптов и т.д.)
    /// по умолчанию делегируются через этот фрейм.
    /// </summary>
    IFrame MainFrame { get; }

    /// <summary>
    /// Выполняет навигацию по указанному адресу.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, CancellationToken)"/>
    ValueTask NavigateAsync(Uri url) => NavigateAsync(url, CancellationToken.None);

    /// <summary>
    /// Делает снимок экрана вкладки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Снимок в формате PNG, закодированный в Base64.</returns>
    ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="CaptureScreenshotAsync(CancellationToken)"/>
    ValueTask<string?> CaptureScreenshotAsync() => CaptureScreenshotAsync(CancellationToken.None);

    /// <summary>
    /// Ожидает завершения навигации.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask WaitForNavigationAsync(TimeSpan? timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync(TimeSpan? timeout)
        => WaitForNavigationAsync(timeout, CancellationToken.None);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync(CancellationToken cancellationToken)
        => WaitForNavigationAsync(timeout: null, cancellationToken);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync()
        => WaitForNavigationAsync(timeout: null, CancellationToken.None);

    /// <summary>
    /// Устанавливает cookie.
    /// </summary>
    /// <param name="name">Имя cookie.</param>
    /// <param name="value">Значение cookie.</param>
    /// <param name="domain">Домен.</param>
    /// <param name="path">Путь.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask SetCookieAsync(string name, string value, string? domain, string? path, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value, string? domain, string? path)
        => SetCookieAsync(name, value, domain, path, CancellationToken.None);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value, CancellationToken cancellationToken)
        => SetCookieAsync(name, value, domain: null, path: null, cancellationToken);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value)
        => SetCookieAsync(name, value, domain: null, path: null, CancellationToken.None);

    /// <summary>
    /// Получает все cookies текущей страницы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<JsonElement?> GetCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetCookiesAsync(CancellationToken)"/>
    ValueTask<JsonElement?> GetCookiesAsync() => GetCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Удаляет все cookies.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask DeleteCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="DeleteCookiesAsync(CancellationToken)"/>
    ValueTask DeleteCookiesAsync() => DeleteCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Происходит при получении консольного сообщения от вкладки.
    /// </summary>
#pragma warning disable CA1003
    event AsyncEventHandler<IWebPage, ConsoleMessageEventArgs>? ConsoleMessage;
#pragma warning restore CA1003
}