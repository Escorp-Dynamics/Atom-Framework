using Atom.Web.Browsing.BiDi;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации контекстов драйвера веб-браузера.
/// </summary>
public interface IWebDriverContext : IWebBrowserContext
{
    /// <summary>
    /// Соединение с протоколом WebDriver BiDi.
    /// </summary>
    BiDiDriver BiDi { get; }

    /// <summary>
    /// Устанавливает соединение с браузером.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Устанавливает соединение с браузером.
    /// </summary>
    ValueTask ConnectAsync() => ConnectAsync(CancellationToken.None);
}