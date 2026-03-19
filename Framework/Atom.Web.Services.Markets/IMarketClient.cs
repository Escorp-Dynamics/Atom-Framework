namespace Atom.Web.Services.Markets;

/// <summary>
/// Стриминг-клиент рынка (WebSocket и подобные протоколы).
/// Обеспечивает подключение, подписку на рыночные данные и получение обновлений в реальном времени.
/// </summary>
/// <remarks>
/// Конкретные платформы добавляют свои события (BookSnapshot, PriceChange и т.д.)
/// в реализации этого интерфейса.
/// </remarks>
public interface IMarketClient : IAsyncDisposable
{
    /// <summary>Имя платформы (например "Polymarket", "Binance").</summary>
    string PlatformName { get; }

    /// <summary>Подключён ли клиент.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Подписывается на рыночные данные.
    /// </summary>
    /// <param name="marketIds">Идентификаторы рынков/инструментов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask SubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отписывается от рыночных данных.
    /// </summary>
    /// <param name="marketIds">Идентификаторы рынков/инструментов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отключается от рынка.
    /// </summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
}
