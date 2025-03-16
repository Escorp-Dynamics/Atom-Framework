using System.Net;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет исключение веб-браузера.
/// </summary>
public class WebBrowserException : WebException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public WebBrowserException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public WebBrowserException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserException"/>.
    /// </summary>
    public WebBrowserException() : base() { }
}