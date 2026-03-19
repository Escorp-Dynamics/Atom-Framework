namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Аргументы события получения снимка стакана ордеров.
/// </summary>
/// <param name="Snapshot">Снимок стакана.</param>
public sealed class PolymarketBookEventArgs(PolymarketBookSnapshot Snapshot) : EventArgs
{
    /// <summary>
    /// Снимок стакана ордеров.
    /// </summary>
    public PolymarketBookSnapshot Snapshot { get; } = Snapshot;
}

/// <summary>
/// Аргументы события изменения уровней цены.
/// </summary>
/// <param name="PriceChange">Данные изменения цены.</param>
public sealed class PolymarketPriceChangeEventArgs(PolymarketPriceChange PriceChange) : EventArgs
{
    /// <summary>
    /// Данные изменения уровня цены.
    /// </summary>
    public PolymarketPriceChange PriceChange { get; } = PriceChange;
}

/// <summary>
/// Аргументы события обновления цены последней сделки.
/// </summary>
/// <param name="LastTradePrice">Данные последней цены сделки.</param>
public sealed class PolymarketLastTradePriceEventArgs(PolymarketLastTradePrice LastTradePrice) : EventArgs
{
    /// <summary>
    /// Данные последней цены сделки.
    /// </summary>
    public PolymarketLastTradePrice LastTradePrice { get; } = LastTradePrice;
}

/// <summary>
/// Аргументы события изменения минимального шага цены.
/// </summary>
/// <param name="TickSizeChange">Данные изменения шага цены.</param>
public sealed class PolymarketTickSizeChangeEventArgs(PolymarketTickSizeChange TickSizeChange) : EventArgs
{
    /// <summary>
    /// Данные изменения минимального шага цены.
    /// </summary>
    public PolymarketTickSizeChange TickSizeChange { get; } = TickSizeChange;
}

/// <summary>
/// Аргументы события обновления ордера пользователя.
/// </summary>
/// <param name="Order">Данные ордера.</param>
public sealed class PolymarketOrderEventArgs(PolymarketOrder Order) : EventArgs
{
    /// <summary>
    /// Обновлённый ордер.
    /// </summary>
    public PolymarketOrder Order { get; } = Order;
}

/// <summary>
/// Аргументы события исполнения сделки пользователя.
/// </summary>
/// <param name="Trade">Данные сделки.</param>
public sealed class PolymarketTradeEventArgs(PolymarketTrade Trade) : EventArgs
{
    /// <summary>
    /// Исполненная сделка.
    /// </summary>
    public PolymarketTrade Trade { get; } = Trade;
}

/// <summary>
/// Аргументы события разрыва соединения с каналом.
/// </summary>
/// <param name="Channel">Канал, с которым произошёл разрыв.</param>
public sealed class PolymarketDisconnectedEventArgs(PolymarketChannel Channel) : EventArgs
{
    /// <summary>
    /// Канал, с которым произошёл разрыв соединения.
    /// </summary>
    public PolymarketChannel Channel { get; } = Channel;
}

/// <summary>
/// Аргументы события ошибки обработки входящего сообщения.
/// </summary>
/// <param name="Exception">Исключение, описывающее ошибку.</param>
/// <param name="Channel">Канал, в котором произошла ошибка.</param>
public sealed class PolymarketErrorEventArgs(Exception Exception, PolymarketChannel Channel) : EventArgs
{
    /// <summary>
    /// Исключение, описывающее ошибку.
    /// </summary>
    public Exception Exception { get; } = Exception;

    /// <summary>
    /// Канал, в котором произошла ошибка.
    /// </summary>
    public PolymarketChannel Channel { get; } = Channel;
}

/// <summary>
/// Аргументы события успешного автоматического переподключения.
/// </summary>
/// <param name="Channel">Канал, к которому произошло переподключение.</param>
/// <param name="Attempt">Номер попытки, с которой удалось переподключиться.</param>
public sealed class PolymarketReconnectedEventArgs(PolymarketChannel Channel, int Attempt) : EventArgs
{
    /// <summary>
    /// Канал, к которому произошло переподключение.
    /// </summary>
    public PolymarketChannel Channel { get; } = Channel;

    /// <summary>
    /// Номер попытки, с которой удалось переподключиться.
    /// </summary>
    public int Attempt { get; } = Attempt;
}
