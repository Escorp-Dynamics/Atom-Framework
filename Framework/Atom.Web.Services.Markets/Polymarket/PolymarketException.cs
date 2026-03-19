using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Исключение, возникающее при ошибках взаимодействия с WebSocket API Polymarket.
/// </summary>
public sealed class PolymarketException : MarketException
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketException"/>.
    /// </summary>
    public PolymarketException() : base("Ошибка Polymarket.") { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketException"/> с указанным сообщением.
    /// </summary>
    /// <param name="message">Описание ошибки.</param>
    public PolymarketException(string? message) : base(message ?? "Ошибка Polymarket.") { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketException"/> с сообщением и внутренним исключением.
    /// </summary>
    /// <param name="message">Описание ошибки.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public PolymarketException(string? message, Exception? innerException) : base(message ?? "Ошибка Polymarket.", innerException ?? new Exception("Неизвестная внутренняя ошибка.")) { }
}
