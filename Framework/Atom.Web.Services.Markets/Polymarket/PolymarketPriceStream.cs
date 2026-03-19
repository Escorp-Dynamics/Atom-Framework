using System.Collections.Concurrent;
using System.Globalization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Запись кэша цен для одного токена.
/// </summary>
public sealed class PolymarketPriceSnapshot
    : IMarketPriceSnapshot
{
    /// <summary>
    /// Идентификатор токена (asset ID).
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Лучшая цена покупки (bid).
    /// </summary>
    public string? BestBid { get; set; }

    /// <summary>
    /// Лучшая цена продажи (ask).
    /// </summary>
    public string? BestAsk { get; set; }

    /// <summary>
    /// Цена последней сделки.
    /// </summary>
    public string? LastTradePrice { get; set; }

    /// <summary>
    /// Середина спреда.
    /// </summary>
    public string? Midpoint { get; set; }

    /// <summary>
    /// Минимальный шаг цены.
    /// </summary>
    public string? TickSize { get; set; }

    /// <summary>
    /// Время последнего обновления (UNIX timestamp).
    /// </summary>
    public long LastUpdateTicks { get; set; }

    // IMarketPriceSnapshot — явная реализация (парсинг string → double)
    double? IMarketPriceSnapshot.BestBid => ParsePrice(BestBid);
    double? IMarketPriceSnapshot.BestAsk => ParsePrice(BestAsk);
    double? IMarketPriceSnapshot.Midpoint => ParsePrice(Midpoint);
    double? IMarketPriceSnapshot.LastTradePrice => ParsePrice(LastTradePrice);

    private static double? ParsePrice(string? value) =>
        value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;
}

/// <summary>
/// Аргументы события обновления цены в PolymarketPriceStream.
/// </summary>
public sealed class PolymarketPriceUpdatedEventArgs(PolymarketPriceSnapshot snapshot) : EventArgs
{
    /// <summary>
    /// Обновлённый снимок цены.
    /// </summary>
    public PolymarketPriceSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// Потоковая подписка на live-цены Polymarket с автоматической агрегацией в памяти.
/// </summary>
/// <remarks>
/// Построен поверх <see cref="PolymarketClient"/> (WebSocket).
/// Автоматически подписывается на события price_change, book, last_trade_price и tick_size_change.
/// Поддерживает потокобезопасный кэш <see cref="PolymarketPriceSnapshot"/> для каждого токена.
/// Совместим с NativeAOT.
/// </remarks>
public sealed class PolymarketPriceStream : IMarketPriceStream, IAsyncDisposable, IDisposable
{
    private readonly PolymarketClient client;
    private readonly bool disposeClient;
    private readonly ConcurrentDictionary<string, PolymarketPriceSnapshot> priceCache = new();
    private bool isDisposed;

    /// <summary>
    /// Событие обновления цены по любому из отслеживаемых токенов.
    /// </summary>
    public event AsyncEventHandler<PolymarketPriceStream, PolymarketPriceUpdatedEventArgs>? PriceUpdated;

    /// <summary>
    /// Инициализирует поток цен с новым WebSocket-клиентом.
    /// </summary>
    public PolymarketPriceStream()
    {
        client = new PolymarketClient();
        disposeClient = true;
        AttachHandlers();
    }

    /// <summary>
    /// Инициализирует поток цен с существующим WebSocket-клиентом.
    /// </summary>
    /// <param name="client">WebSocket-клиент Polymarket.</param>
    public PolymarketPriceStream(PolymarketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
        disposeClient = false;
        AttachHandlers();
    }

    /// <summary>
    /// Текущие снимки цен всех отслеживаемых токенов.
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketPriceSnapshot> Prices => priceCache;

    /// <summary>
    /// Количество отслеживаемых токенов.
    /// </summary>
    public int TokenCount => priceCache.Count;

    /// <summary>
    /// Получает текущий снимок цены для указанного токена.
    /// </summary>
    /// <param name="assetId">Идентификатор токена.</param>
    /// <returns>Снимок цены или null, если токен не отслеживается.</returns>
    public PolymarketPriceSnapshot? GetPrice(string assetId) =>
        priceCache.TryGetValue(assetId, out var snapshot) ? snapshot : null;

    // IMarketPriceStream — явная реализация
    IMarketPriceSnapshot? IMarketPriceStream.GetPrice(string assetId) => GetPrice(assetId);

    /// <summary>
    /// Подписывается на live-цены указанных рынков.
    /// </summary>
    /// <param name="conditionIds">Идентификаторы рынков (condition IDs).</param>
    /// <param name="assetIds">Идентификаторы токенов (asset IDs, необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SubscribeAsync(
        string[] conditionIds,
        string[]? assetIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await client.SubscribeMarketAsync(conditionIds, assetIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отписывается от рынков.
    /// </summary>
    /// <param name="conditionIds">Идентификаторы рынков.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask UnsubscribeAsync(
        string[] conditionIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await client.UnsubscribeMarketAsync(conditionIds, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Очистка кэша для отписанных рынков
        foreach (var entry in priceCache)
        {
            if (entry.Value.Market is not null && Array.Exists(conditionIds, id => id == entry.Value.Market))
                priceCache.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Очищает кэш цен.
    /// </summary>
    public void ClearCache() => priceCache.Clear();

    #region Обработчики WebSocket-событий

    private void AttachHandlers()
    {
        client.BookSnapshotReceived += OnBookSnapshotReceived;
        client.PriceChanged += OnPriceChanged;
        client.LastTradePriceReceived += OnLastTradePriceReceived;
        client.TickSizeChanged += OnTickSizeChanged;
    }

    private void DetachHandlers()
    {
        client.BookSnapshotReceived -= OnBookSnapshotReceived;
        client.PriceChanged -= OnPriceChanged;
        client.LastTradePriceReceived -= OnLastTradePriceReceived;
        client.TickSizeChanged -= OnTickSizeChanged;
    }

    /// <summary>
    /// Обработка снимка стакана — извлекает лучшие bid/ask.
    /// </summary>
    private async ValueTask OnBookSnapshotReceived(PolymarketClient sender, PolymarketBookEventArgs e)
    {
        var snapshot = GetOrCreateSnapshot(e.Snapshot.AssetId, e.Snapshot.Market);

        // Лучший bid — первый элемент buys (наивысшая цена покупки)
        if (e.Snapshot.Buys is { Length: > 0 })
            snapshot.BestBid = e.Snapshot.Buys[0].Price;

        // Лучший ask — первый элемент sells (наименьшая цена продажи)
        if (e.Snapshot.Sells is { Length: > 0 })
            snapshot.BestAsk = e.Snapshot.Sells[0].Price;

        // Вычисление midpoint
        UpdateMidpoint(snapshot);
        snapshot.LastUpdateTicks = Environment.TickCount64;

        await NotifyPriceUpdated(snapshot).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработка изменения цены — обновляет bid/ask из дельта-обновлений.
    /// </summary>
    private async ValueTask OnPriceChanged(PolymarketClient sender, PolymarketPriceChangeEventArgs e)
    {
        var snapshot = GetOrCreateSnapshot(e.PriceChange.AssetId, e.PriceChange.Market);

        if (e.PriceChange.Changes is { Length: > 0 })
        {
            foreach (var change in e.PriceChange.Changes)
            {
                if (change.Side == PolymarketSide.Buy)
                    snapshot.BestBid = change.Price;
                else if (change.Side == PolymarketSide.Sell)
                    snapshot.BestAsk = change.Price;
            }
        }

        UpdateMidpoint(snapshot);
        snapshot.LastUpdateTicks = Environment.TickCount64;

        await NotifyPriceUpdated(snapshot).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработка цены последней сделки.
    /// </summary>
    private async ValueTask OnLastTradePriceReceived(PolymarketClient sender, PolymarketLastTradePriceEventArgs e)
    {
        var snapshot = GetOrCreateSnapshot(e.LastTradePrice.AssetId, e.LastTradePrice.Market);
        snapshot.LastTradePrice = e.LastTradePrice.Price;
        snapshot.LastUpdateTicks = Environment.TickCount64;

        await NotifyPriceUpdated(snapshot).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработка изменения tick size.
    /// </summary>
    private async ValueTask OnTickSizeChanged(PolymarketClient sender, PolymarketTickSizeChangeEventArgs e)
    {
        var snapshot = GetOrCreateSnapshot(e.TickSizeChange.AssetId, e.TickSizeChange.Market);
        snapshot.TickSize = e.TickSizeChange.NewTickSize;
        snapshot.LastUpdateTicks = Environment.TickCount64;

        await NotifyPriceUpdated(snapshot).ConfigureAwait(false);
    }

    #endregion

    #region Вспомогательные методы

    private PolymarketPriceSnapshot GetOrCreateSnapshot(string? assetId, string? market)
    {
        var key = assetId ?? "unknown";
        return priceCache.GetOrAdd(key, id => new PolymarketPriceSnapshot
        {
            AssetId = id,
            Market = market
        });
    }

    /// <summary>
    /// Вычисляет midpoint как среднее bid и ask.
    /// </summary>
    private static void UpdateMidpoint(PolymarketPriceSnapshot snapshot)
    {
        if (snapshot.BestBid is not null && snapshot.BestAsk is not null
            && double.TryParse(snapshot.BestBid, System.Globalization.CultureInfo.InvariantCulture, out var bid)
            && double.TryParse(snapshot.BestAsk, System.Globalization.CultureInfo.InvariantCulture, out var ask))
        {
            snapshot.Midpoint = ((bid + ask) / 2).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private async ValueTask NotifyPriceUpdated(PolymarketPriceSnapshot snapshot)
    {
        if (PriceUpdated is not null)
            await PriceUpdated.Invoke(this, new PolymarketPriceUpdatedEventArgs(snapshot)).ConfigureAwait(false);
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        DetachHandlers();
        priceCache.Clear();

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
        priceCache.Clear();

        if (disposeClient)
            client.Dispose();
    }
}
