namespace Atom.Media;

/// <summary>
/// Представляет исключение видеопотока.
/// </summary>
public class VideoStreamException : NativeException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStreamException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public VideoStreamException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStreamException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public VideoStreamException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStreamException"/>.
    /// </summary>
    public VideoStreamException() : base() { }
}