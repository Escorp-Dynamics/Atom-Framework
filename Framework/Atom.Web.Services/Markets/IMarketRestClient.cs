namespace Atom.Web.Services.Markets;

/// <summary>
/// REST-клиент рынка — публичные и авторизованные HTTP-операции.
/// </summary>
/// <remarks>
/// Определяет базовые операции, общие для всех торговых площадок.
/// Платформо-специфичные методы добавляются в конкретных реализациях.
/// </remarks>
public interface IMarketRestClient : IDisposable
{
    /// <summary>Базовый URL API.</summary>
    string BaseUrl { get; }

    /// <summary>
    /// Создаёт ордер.
    /// </summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="side">Сторона (Buy/Sell).</param>
    /// <param name="quantity">Объём.</param>
    /// <param name="price">Цена (null для рыночного ордера).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного ордера.</returns>
    ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет ордер.
    /// </summary>
    /// <param name="orderId">Идентификатор ордера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает текущую цену актива.
    /// </summary>
    ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает книгу ордеров.
    /// </summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Снимок книги ордеров.
/// </summary>
public interface IMarketOrderBookSnapshot
{
    /// <summary>Идентификатор актива.</summary>
    string AssetId { get; }

    /// <summary>Временная метка.</summary>
    DateTimeOffset Timestamp { get; }
}
