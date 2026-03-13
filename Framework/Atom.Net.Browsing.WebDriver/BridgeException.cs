namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет исключение, возникшее при взаимодействии с мостом браузера.
/// </summary>
public class BridgeException : Exception
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BridgeException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public BridgeException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BridgeException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public BridgeException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BridgeException"/>.
    /// </summary>
    public BridgeException() : base() { }
}
