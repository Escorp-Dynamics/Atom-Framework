namespace Atom.Web.Services.Markets;

/// <summary>
/// Базовое исключение для операций на рынке.
/// </summary>
public class MarketException : Exception
{
    /// <summary>
    /// Создаёт исключение с сообщением.
    /// </summary>
    public MarketException(string message) : base(message) { }

    /// <summary>
    /// Создаёт исключение с сообщением и внутренним исключением.
    /// </summary>
    public MarketException(string message, Exception innerException)
        : base(message, innerException) { }
}
