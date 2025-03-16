namespace Atom.Distribution;

/// <summary>
/// Представляет исключение на неподдерживаемых дистрибутивах.
/// </summary>
public class UnsupportedDistributiveException : Exception
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UnsupportedDistributiveException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public UnsupportedDistributiveException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UnsupportedDistributiveException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public UnsupportedDistributiveException(string? message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UnsupportedDistributiveException"/>.
    /// </summary>
    public UnsupportedDistributiveException() : base() { }
}