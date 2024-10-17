namespace Atom.Web.Browsers;

/// <summary>
/// Представляет исключение, возникшее в процессе парсинга HTML.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="DOMParserException"/>.
/// </remarks>
/// <param name="message">Сообщение об ошибке.</param>
/// <param name="innerException">Внутреннее исключение.</param>
public class DOMParserException(string? message, Exception? innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DOMParserException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public DOMParserException(string? message) : this(message, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DOMParserException"/>.
    /// </summary>
    public DOMParserException() : this(default) { }
}