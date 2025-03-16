namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет исключение для всех ошибок протокола WebDriver BiDi.
/// </summary>
public class BiDiException : Exception
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BiDiException"/>.
    /// </summary>
    /// <param name="message">Сообщение исключения.</param>
    /// <param name="innerException">Внутреннее исключение, вызвавшее данное исключение.</param>
    public BiDiException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BiDiException"/>.
    /// </summary>
    /// <param name="message">Сообщение исключения.</param>
    public BiDiException(string message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="BiDiException"/>.
    /// </summary>
    public BiDiException() : base() { }
}