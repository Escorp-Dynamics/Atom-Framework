namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет исключение драйвера веб-браузера.
/// </summary>
public class WebDriverException : WebBrowserException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public WebDriverException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public WebDriverException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverException"/>.
    /// </summary>
    public WebDriverException() : base() { }
}