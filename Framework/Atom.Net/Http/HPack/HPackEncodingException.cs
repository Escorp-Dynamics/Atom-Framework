namespace Atom.Net.Http.HPack;

/// <summary>
/// Представляет исключении при кодировании HPACK.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HPackEncodingException"/>.
/// </remarks>
/// <param name="message">Сообщение об ошибке.</param>
/// <param name="innerException">Внутреннее исключение.</param>
public sealed class HPackEncodingException(string? message, Exception? innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackEncodingException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public HPackEncodingException(string? message) : this(message, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackEncodingException"/>.
    /// </summary>
    public HPackEncodingException() : this(default) { }
}