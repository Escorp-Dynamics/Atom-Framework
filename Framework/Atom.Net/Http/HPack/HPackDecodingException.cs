namespace Atom.Net.Http.HPack;

/// <summary>
/// Представляет исключении при декодировании HPACK.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HPackDecodingException"/>.
/// </remarks>
/// <param name="message">Сообщение об ошибке.</param>
/// <param name="innerException">Внутреннее исключение.</param>
public sealed class HPackDecodingException(string? message, Exception? innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackDecodingException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public HPackDecodingException(string? message) : this(message, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HPackDecodingException"/>.
    /// </summary>
    public HPackDecodingException() : this(default) { }
}