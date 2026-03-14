using System.Collections.Concurrent;
using System.Globalization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Трекер портфеля Polymarket — отслеживает позиции, P&amp;L и статистику в реальном времени.
/// </summary>
/// <remarks>
/// Интегрируется с <see cref="PolymarketClient"/> (WebSocket) для получения данных о сделках и ордерах,
/// и с <see cref="PolymarketPriceStream"/> для обновления рыночных цен.
/// Совместим с NativeAOT. Потокобезопасен.
/// </remarks>
public sealed class PolymarketPortfolioTracker : IMarketPortfolioTracker, IAsyncDisposable, IDisposable
{
    private readonly PolymarketClient client;
    private readonly PolymarketPriceStream priceStream;
    private readonly bool disposeClient;
    private readonly bool disposePriceStream;
    private readonly ConcurrentDictionary<string, PolymarketPosition> positions = new();
    private bool isDisposed;

    /// <summary>
    /// Событие обновления позиции.
    /// </summary>
    public event AsyncEventHandler<PolymarketPortfolioTracker, PolymarketPositionChangedEventArgs>? PositionChanged;

    /// <summary>
    /// Инициализирует трекер портфеля с новыми WebSocket- и PriceStream-клиентами.
    /// </summary>
    public PolymarketPortfolioTracker()
    {
        client = new PolymarketClient();
        priceStream = new PolymarketPriceStream(client);
        disposeClient = true;
        disposePriceStream = true;
        AttachHandlers();
    }

    /// <summary>
    /// Инициализирует трекер портфеля с существующими клиентами.
    /// </summary>
    /// <param name="client">WebSocket-клиент Polymarket для подписки на user-канал.</param>
    /// <param name="priceStream">Поток цен для live-обновления рыночной стоимости.</param>
    public PolymarketPortfolioTracker(PolymarketClient client, PolymarketPriceStream priceStream)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(priceStream);

        this.client = client;
        this.priceStream = priceStream;
        disposeClient = false;
        disposePriceStream = false;
        AttachHandlers();
    }

    /// <summary>
    /// Текущие позиции портфеля (только чтение).
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketPosition> Positions => positions;

    /// <summary>
    /// Количество открытых позиций.
    /// </summary>
    public int OpenPositionCount => positions.Values.Count(p => !p.IsClosed);

    /// <summary>
    /// Получает позицию по идентификатору токена.
    /// </summary>
    /// <param name="assetId">Идентификатор токена.</param>
    /// <returns>Позиция или null, если не найдена.</returns>
    public PolymarketPosition? GetPosition(string assetId) =>
        positions.TryGetValue(assetId, out var position) ? position : null;

    // IMarketPortfolioTracker — явная реализация
    IMarketPosition? IMarketPortfolioTracker.GetPosition(string assetId) => GetPosition(assetId);
    IMarketPortfolioSummary IMarketPortfolioTracker.GetSummary() => GetSummary();

    /// <summary>
    /// Формирует итоговую статистику портфеля.
    /// </summary>
    public PolymarketPortfolioSummary GetSummary()
    {
        var allPositions = positions.Values.ToArray();
        var open = allPositions.Where(p => !p.IsClosed).ToArray();
        var closed = allPositions.Where(p => p.IsClosed).ToArray();

        return new PolymarketPortfolioSummary
        {
            OpenPositions = open.Length,
            ClosedPositions = closed.Length,
            TotalMarketValue = open.Sum(p => p.MarketValue),
            TotalCostBasis = open.Sum(p => p.TotalCost),
            TotalUnrealizedPnL = open.Sum(p => p.UnrealizedPnL),
            TotalRealizedPnL = allPositions.Sum(p => p.RealizedPnL),
            TotalFees = allPositions.Sum(p => p.TotalFees)
        };
    }

    /// <summary>
    /// Подписывается на live-обновления позиций по указанным рынкам.
    /// Для user-канала (сделки/ордера) требуются учётные данные.
    /// </summary>
    /// <param name="credentials">Учётные данные API Polymarket для user-канала.</param>
    /// <param name="conditionIds">Идентификаторы рынков (condition IDs).</param>
    /// <param name="assetIds">Идентификаторы токенов (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SubscribeAsync(
        PolymarketAuth credentials,
        string[] conditionIds,
        string[]? assetIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(credentials);

        // Подписка на цены для обновления рыночной стоимости
        await priceStream.SubscribeAsync(conditionIds, assetIds, cancellationToken).ConfigureAwait(false);

        // Подписка на пользовательские события (ордера/трейды)
        await client.SubscribeUserAsync(credentials, conditionIds, assetIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отписывается от обновлений по указанным рынкам.
    /// </summary>
    /// <param name="conditionIds">Идентификаторы рынков.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask UnsubscribeAsync(
        string[] conditionIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await priceStream.UnsubscribeAsync(conditionIds, cancellationToken).ConfigureAwait(false);
        await client.UnsubscribeUserAsync(conditionIds, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Синхронизирует позиции из существующих сделок (REST API).
    /// Используется для загрузки начального состояния портфеля.
    /// </summary>
    /// <param name="trades">Массив сделок из REST API.</param>
    public void SyncFromTrades(PolymarketTrade[] trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        foreach (var trade in trades)
        {
            if (trade.AssetId is null || trade.Status != PolymarketTradeStatus.Confirmed)
                continue;

            ApplyTrade(
                trade.AssetId,
                trade.Market,
                ParseDouble(trade.Size),
                ParseDouble(trade.Price),
                ParseDouble(trade.FeeRateBps) / 10_000, // bps → доля
                trade.Side,
                PolymarketPositionChangeReason.ManualSync);
        }
    }

    /// <summary>
    /// Записывает выплату по разрешённому рынку для указанного токена.
    /// </summary>
    /// <param name="assetId">Идентификатор токена.</param>
    /// <param name="isWinner">Является ли этот токен победителем (выплата = Quantity × 1.0).</param>
    public void ApplyResolution(string assetId, bool isWinner)
    {
        if (!positions.TryGetValue(assetId, out var position) || position.IsClosed)
            return;

        // Победитель: получает $1 за каждый токен. Проигравший: получает $0.
        var payout = isWinner ? position.Quantity * 1.0 : 0;
        position.RealizedPnL += payout - position.TotalCost;
        position.Quantity = 0;
        position.CurrentPrice = isWinner ? 1.0 : 0;
        position.LastUpdateTicks = Environment.TickCount64;

        NotifyPositionChanged(position, PolymarketPositionChangeReason.MarketResolved);
    }

    /// <summary>
    /// Загружает начальное состояние портфеля из REST API (сделки по всем или указанным рынкам).
    /// </summary>
    /// <param name="restClient">REST-клиент Polymarket.</param>
    /// <param name="market">Фильтр по рынку (condition ID, необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SyncFromRestAsync(
        PolymarketRestClient restClient,
        string? market = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restClient);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var trades = await restClient.GetTradesAsync(market, cancellationToken).ConfigureAwait(false);
        if (trades is { Length: > 0 })
            SyncFromTrades(trades);
    }

    /// <summary>
    /// Подключает EventResolver для автоматического применения разрешений к портфелю.
    /// При разрешении рынка автоматически вызывает <see cref="ApplyResolution"/> для всех затронутых позиций.
    /// </summary>
    /// <param name="resolver">EventResolver для отслеживания разрешений.</param>
    public void ConnectResolver(PolymarketEventResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        resolver.MarketResolved += OnMarketResolved;
    }

    /// <summary>
    /// Отключает EventResolver.
    /// </summary>
    /// <param name="resolver">EventResolver для отключения.</param>
    public void DisconnectResolver(PolymarketEventResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        resolver.MarketResolved -= OnMarketResolved;
    }

    /// <summary>
    /// Обработка разрешения рынка из EventResolver.
    /// </summary>
    private ValueTask OnMarketResolved(PolymarketEventResolver sender, PolymarketMarketResolvedEventArgs e)
    {
        var resolution = e.Resolution;

        // Применяем результат к победителю
        if (resolution.WinnerTokenId is not null)
            ApplyResolution(resolution.WinnerTokenId, isWinner: true);

        // Применяем результат к проигравшему
        if (resolution.LoserTokenId is not null)
            ApplyResolution(resolution.LoserTokenId, isWinner: false);

        return default;
    }

    /// <summary>
    /// Очищает все позиции.
    /// </summary>
    public void ClearPositions() => positions.Clear();

    #region Обработчики WebSocket-событий

    private void AttachHandlers()
    {
        client.TradeReceived += OnTradeReceived;
        priceStream.PriceUpdated += OnPriceUpdated;
    }

    private void DetachHandlers()
    {
        client.TradeReceived -= OnTradeReceived;
        priceStream.PriceUpdated -= OnPriceUpdated;
    }

    /// <summary>
    /// Обработка новой сделки из WebSocket user-канала.
    /// </summary>
    private ValueTask OnTradeReceived(PolymarketClient sender, PolymarketTradeEventArgs e)
    {
        var trade = e.Trade;
        if (trade.AssetId is null)
            return default;

        ApplyTrade(
            trade.AssetId,
            trade.Market,
            ParseDouble(trade.Size),
            ParseDouble(trade.Price),
            ParseDouble(trade.FeeRateBps) / 10_000,
            trade.Side,
            PolymarketPositionChangeReason.Trade);

        return default;
    }

    /// <summary>
    /// Обработка обновления цены — пересчёт рыночной стоимости всех позиций.
    /// </summary>
    private ValueTask OnPriceUpdated(PolymarketPriceStream sender, PolymarketPriceUpdatedEventArgs e)
    {
        var snapshot = e.Snapshot;
        if (!positions.TryGetValue(snapshot.AssetId, out var position) || position.IsClosed)
            return default;

        // Обновляем текущую цену из midpoint или last trade price
        var newPrice = ParseDouble(snapshot.Midpoint) is > 0 and var mid ? mid
            : ParseDouble(snapshot.LastTradePrice) is > 0 and var ltp ? ltp
            : position.CurrentPrice;

        if (Math.Abs(newPrice - position.CurrentPrice) < 1e-10)
            return default; // Цена не изменилась

        position.CurrentPrice = newPrice;
        position.LastUpdateTicks = Environment.TickCount64;

        NotifyPositionChanged(position, PolymarketPositionChangeReason.PriceUpdate);
        return default;
    }

    #endregion

    #region Логика позиций

    /// <summary>
    /// Применяет сделку к позиции, обновляя количество и среднюю цену входа.
    /// </summary>
    private void ApplyTrade(
        string assetId,
        string? market,
        double size,
        double price,
        double feeRate,
        PolymarketSide side,
        PolymarketPositionChangeReason reason)
    {
        if (size <= 0 || price <= 0)
            return;

        var position = positions.GetOrAdd(assetId, id => new PolymarketPosition
        {
            AssetId = id,
            Market = market,
            OpenedAtTicks = Environment.TickCount64
        });

        var fee = size * price * feeRate;
        position.TotalFees += fee;
        position.TradeCount++;
        position.LastUpdateTicks = Environment.TickCount64;

        if (side == PolymarketSide.Buy)
        {
            // Покупка: увеличиваем позицию, пересчитываем среднюю цену входа
            var totalCostBefore = position.Quantity * position.AverageCostBasis;
            var newCost = size * price;
            position.Quantity += size;
            position.AverageCostBasis = position.Quantity > 0
                ? (totalCostBefore + newCost) / position.Quantity
                : 0;
        }
        else
        {
            // Продажа: уменьшаем позицию, фиксируем реализованный P&amp;L
            var sellQuantity = Math.Min(size, position.Quantity);
            var realizedPnl = sellQuantity * (price - position.AverageCostBasis);
            position.RealizedPnL += realizedPnl;
            position.Quantity -= sellQuantity;

            if (position.Quantity <= 0)
            {
                position.Quantity = 0;
                position.AverageCostBasis = 0;
            }
        }

        NotifyPositionChanged(position, reason);
    }

    private void NotifyPositionChanged(PolymarketPosition position, PolymarketPositionChangeReason reason)
    {
        PositionChanged?.Invoke(this, new PolymarketPositionChangedEventArgs(position, reason));
    }

    private static double ParseDouble(string? value) =>
        value is not null && double.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : 0;

    #endregion

    /// <summary>
    /// Освобождает ресурсы асинхронно.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        DetachHandlers();

        if (disposePriceStream)
            await priceStream.DisposeAsync().ConfigureAwait(false);

        if (disposeClient)
            await client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Освобождает ресурсы синхронно.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        DetachHandlers();

        if (disposePriceStream)
            priceStream.Dispose();

        if (disposeClient)
            client.Dispose();
    }
}
