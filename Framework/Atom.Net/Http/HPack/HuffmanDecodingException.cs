namespace Atom.Net.Http.HPack;

/// <summary>
/// Представляет исключение при декодировании Хаффмана.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HuffmanDecodingException"/>.
/// </remarks>
/// <param name="message">Сообщение об ошибке.</param>
/// <param name="innerException">Внутреннее исключение.</param>
public sealed class HuffmanDecodingException(string? message, Exception? innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HuffmanDecodingException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public HuffmanDecodingException(string? message) : this(message, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HuffmanDecodingException"/>.
    /// </summary>
    public HuffmanDecodingException() : this("") { }
}