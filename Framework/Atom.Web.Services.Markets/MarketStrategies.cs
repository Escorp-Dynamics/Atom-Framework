using System.Collections.Concurrent;
using System.Globalization;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Конкретные реализации IMarketStrategy для Markets/.
// Каждая стратегия: оценивает поток цен → генерирует IMarketTradeSignal.
// ═══════════════════════════════════════════════════════════════════

#region Общая модель сигнала

/// <summary>Торговый сигнал, общий для всех стратегий.</summary>
public sealed class MarketTradeSignal : IMarketTradeSignal
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public TradeAction Action { get; init; }

    /// <inheritdoc />
    public double Quantity { get; init; }

    /// <inheritdoc />
    public string? Price { get; init; }

    /// <inheritdoc />
    public double Confidence { get; init; }

    /// <inheritdoc />
    public string? Reason { get; init; }

    /// <summary>Создаёт Hold-сигнал (нет действий).</summary>
    public static MarketTradeSignal Hold(string assetId, string? reason = null) => new()
    {
        AssetId = assetId,
        Action = TradeAction.Hold,
        Quantity = 0,
        Confidence = 0,
        Reason = reason
    };
}

#endregion

#region Momentum Strategy

/// <summary>
/// Стратегия Momentum: следует за трендом.
/// Покупает при росте цены на <see cref="BuyThresholdPercent"/>, продаёт при падении на <see cref="SellThresholdPercent"/>.
/// </summary>
/// <remarks>
/// Использует скользящее среднее (SMA) по N последним ценам.
/// Если текущая цена > SMA * (1 + threshold) → Buy.
/// Если текущая цена &lt; SMA * (1 - threshold) → Sell.
/// </remarks>
public sealed class MomentumStrategy : IMarketStrategy
{
    private readonly ConcurrentDictionary<string, PriceWindow> windows = new();
    private bool isDisposed;

    /// <summary>Имя стратегии.</summary>
    public string Name => "Momentum";

    /// <summary>Размер окна SMA (кол-во точек).</summary>
    public int WindowSize { get; init; } = 20;

    /// <summary>Порог покупки (0.01 = 1%).</summary>
    public double BuyThresholdPercent { get; init; } = 0.02;

    /// <summary>Порог продажи (0.01 = 1%).</summary>
    public double SellThresholdPercent { get; init; } = 0.02;

    /// <summary>Объём ордера по умолчанию.</summary>
    public double DefaultQuantity { get; init; } = 1.0;

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot is null) return MarketTradeSignal.Hold(assetId, "Нет данных");

        var price = snapshot.Midpoint ?? snapshot.LastTradePrice;
        if (price is null) return MarketTradeSignal.Hold(assetId, "Нет цены");

        var window = windows.GetOrAdd(assetId, _ => new PriceWindow(WindowSize));

        if (window.Count < WindowSize)
            return MarketTradeSignal.Hold(assetId, $"Недостаточно данных ({window.Count}/{WindowSize})");

        var sma = window.Average;
        var deviation = (price.Value - sma) / sma;

        if (deviation > BuyThresholdPercent)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = DefaultQuantity,
                Price = price.Value.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(deviation / BuyThresholdPercent * 0.5, 1.0),
                Reason = $"Momentum вверх: цена {price.Value:F2} > SMA({WindowSize}) {sma:F2} на {deviation * 100:F2}%"
            };
        }

        if (deviation < -SellThresholdPercent)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = DefaultQuantity,
                Price = price.Value.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(Math.Abs(deviation) / SellThresholdPercent * 0.5, 1.0),
                Reason = $"Momentum вниз: цена {price.Value:F2} < SMA({WindowSize}) {sma:F2} на {Math.Abs(deviation) * 100:F2}%"
            };
        }

        return MarketTradeSignal.Hold(assetId, $"В пределах SMA ({deviation * 100:F2}%)");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        var price = snapshot.Midpoint ?? snapshot.LastTradePrice;
        if (price is null) return;

        var window = windows.GetOrAdd(snapshot.AssetId, _ => new PriceWindow(WindowSize));
        window.Add(price.Value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        windows.Clear();
    }
}

#endregion

#region Mean Reversion Strategy

/// <summary>
/// Стратегия Mean Reversion: ожидает возврата к среднему.
/// Покупает при отклонении вниз от SMA, продаёт при отклонении вверх.
/// Противоположна Momentum.
/// </summary>
/// <remarks>
/// Если цена &lt; SMA * (1 - threshold) → Buy (ожидаем возврат вверх).
/// Если цена > SMA * (1 + threshold) → Sell (ожидаем возврат вниз).
/// Использует стандартное отклонение для расчёта полос Боллинджера.
/// </remarks>
public sealed class MeanReversionStrategy : IMarketStrategy
{
    private readonly ConcurrentDictionary<string, PriceWindow> windows = new();
    private bool isDisposed;

    /// <summary>Имя стратегии.</summary>
    public string Name => "MeanReversion";

    /// <summary>Размер окна SMA (кол-во точек).</summary>
    public int WindowSize { get; init; } = 20;

    /// <summary>Множитель стандартного отклонения для полос Боллинджера.</summary>
    public double BollingerMultiplier { get; init; } = 2.0;

    /// <summary>Объём ордера по умолчанию.</summary>
    public double DefaultQuantity { get; init; } = 1.0;

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot is null) return MarketTradeSignal.Hold(assetId, "Нет данных");

        var price = snapshot.Midpoint ?? snapshot.LastTradePrice;
        if (price is null) return MarketTradeSignal.Hold(assetId, "Нет цены");

        var window = windows.GetOrAdd(assetId, _ => new PriceWindow(WindowSize));

        if (window.Count < WindowSize)
            return MarketTradeSignal.Hold(assetId, $"Недостаточно данных ({window.Count}/{WindowSize})");

        var sma = window.Average;
        var stdDev = window.StandardDeviation;
        var upperBand = sma + BollingerMultiplier * stdDev;
        var lowerBand = sma - BollingerMultiplier * stdDev;

        if (price.Value < lowerBand)
        {
            var deviation = (sma - price.Value) / stdDev;
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = DefaultQuantity,
                Price = price.Value.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(deviation / BollingerMultiplier * 0.5, 1.0),
                Reason = $"Mean Reversion: цена {price.Value:F2} < нижняя полоса {lowerBand:F2} (σ={stdDev:F2})"
            };
        }

        if (price.Value > upperBand)
        {
            var deviation = (price.Value - sma) / stdDev;
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = DefaultQuantity,
                Price = price.Value.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(deviation / BollingerMultiplier * 0.5, 1.0),
                Reason = $"Mean Reversion: цена {price.Value:F2} > верхняя полоса {upperBand:F2} (σ={stdDev:F2})"
            };
        }

        return MarketTradeSignal.Hold(assetId, $"В пределах полос [{lowerBand:F2}, {upperBand:F2}]");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        var price = snapshot.Midpoint ?? snapshot.LastTradePrice;
        if (price is null) return;

        var window = windows.GetOrAdd(snapshot.AssetId, _ => new PriceWindow(WindowSize));
        window.Add(price.Value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        windows.Clear();
    }
}

#endregion

#region Arbitrage Strategy

/// <summary>
/// Стратегия Arbitrage: обнаруживает разницу спредов между парой стримов.
/// Если bid одного стрима > ask другого — арбитражная возможность.
/// </summary>
/// <remarks>
/// Требует два источника цен (Primary + Secondary).
/// Сигнал Buy: покупить на бирже с низким ask, продать на бирже с высоким bid.
/// </remarks>
public sealed class ArbitrageStrategy : IMarketStrategy
{
    private readonly IMarketPriceStream secondaryStream;
    private bool isDisposed;

    /// <summary>Имя стратегии.</summary>
    public string Name => "Arbitrage";

    /// <summary>Минимальный спред для арбитража (0.001 = 0.1%).</summary>
    public double MinSpreadPercent { get; init; } = 0.001;

    /// <summary>Объём ордера по умолчанию.</summary>
    public double DefaultQuantity { get; init; } = 1.0;

    /// <summary>
    /// Создаёт стратегию арбитража.
    /// </summary>
    /// <param name="secondaryStream">Вторичный источник цен (вторая биржа).</param>
    public ArbitrageStrategy(IMarketPriceStream secondaryStream)
    {
        this.secondaryStream = secondaryStream;
    }

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        var primary = priceStream.GetPrice(assetId);
        var secondary = secondaryStream.GetPrice(assetId);

        if (primary is null || secondary is null)
            return MarketTradeSignal.Hold(assetId, "Недостаточно данных для арбитража");

        // Арбитраж 1: buy на primary (ask), sell на secondary (bid)
        if (primary.BestAsk is not null && secondary.BestBid is not null)
        {
            var spread = (secondary.BestBid.Value - primary.BestAsk.Value) / primary.BestAsk.Value;
            if (spread > MinSpreadPercent)
            {
                return new MarketTradeSignal
                {
                    AssetId = assetId,
                    Action = TradeAction.Buy,
                    Quantity = DefaultQuantity,
                    Price = primary.BestAsk.Value.ToString("G", CultureInfo.InvariantCulture),
                    Confidence = Math.Min(spread / MinSpreadPercent * 0.5, 1.0),
                    Reason = $"Арбитраж: buy @{primary.BestAsk.Value:F2} → sell @{secondary.BestBid.Value:F2} (спред {spread * 100:F3}%)"
                };
            }
        }

        // Арбитраж 2: buy на secondary (ask), sell на primary (bid)
        if (secondary.BestAsk is not null && primary.BestBid is not null)
        {
            var spread = (primary.BestBid.Value - secondary.BestAsk.Value) / secondary.BestAsk.Value;
            if (spread > MinSpreadPercent)
            {
                return new MarketTradeSignal
                {
                    AssetId = assetId,
                    Action = TradeAction.Sell,
                    Quantity = DefaultQuantity,
                    Price = primary.BestBid.Value.ToString("G", CultureInfo.InvariantCulture),
                    Confidence = Math.Min(spread / MinSpreadPercent * 0.5, 1.0),
                    Reason = $"Арбитраж: sell @{primary.BestBid.Value:F2} → buy @{secondary.BestAsk.Value:F2} (спред {spread * 100:F3}%)"
                };
            }
        }

        return MarketTradeSignal.Hold(assetId, "Нет арбитражной возможности");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot) { }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
    }
}

#endregion

#region VWAP

/// <summary>
/// Volume-Weighted Average Price (VWAP) стратегия.
/// Без реальных данных объёма использует тиковый VWAP — среднюю цену за период.
/// Buy ниже VWAP (цена занижена), Sell выше VWAP (цена завышена).
/// </summary>
public sealed class VwapStrategy : IMarketStrategy
{
    private readonly ConcurrentDictionary<string, VwapAccumulator> accumulators = new();
    private readonly double thresholdPercent;
    private readonly double defaultQuantity;
    private bool isDisposed;

    /// <summary>
    /// Создаёт VWAP-стратегию.
    /// </summary>
    /// <param name="thresholdPercent">Порог отклонения цены от VWAP для генерации сигнала.</param>
    /// <param name="defaultQuantity">Объём ордера по умолчанию.</param>
    public VwapStrategy(double thresholdPercent = 0.005, double defaultQuantity = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdPercent);
        this.thresholdPercent = thresholdPercent;
        this.defaultQuantity = defaultQuantity;
    }

    /// <inheritdoc />
    public string Name => "VWAP";

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot?.LastTradePrice is not { } price || price <= 0)
            return MarketTradeSignal.Hold(assetId, "Нет данных о цене");

        var acc = accumulators.GetOrAdd(assetId, static _ => new VwapAccumulator());
        var vwap = acc.Vwap;

        if (acc.Count < 2 || vwap <= 0)
            return MarketTradeSignal.Hold(assetId, $"Накопление данных VWAP ({acc.Count} тиков)");

        var deviation = (price - vwap) / vwap;

        if (deviation < -thresholdPercent)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(Math.Abs(deviation) / thresholdPercent * 0.5, 1.0),
                Reason = $"Цена {price:F2} ниже VWAP {vwap:F2} на {Math.Abs(deviation) * 100:F3}%"
            };
        }

        if (deviation > thresholdPercent)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(deviation / thresholdPercent * 0.5, 1.0),
                Reason = $"Цена {price:F2} выше VWAP {vwap:F2} на {deviation * 100:F3}%"
            };
        }

        return MarketTradeSignal.Hold(assetId, $"Цена {price:F2} ≈ VWAP {vwap:F2}");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        if (isDisposed || snapshot.LastTradePrice is not { } price || price <= 0)
            return;
        accumulators.GetOrAdd(snapshot.AssetId, static _ => new VwapAccumulator()).Add(price);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        accumulators.Clear();
    }
}

#endregion

#region RSI

/// <summary>
/// Relative Strength Index (RSI) стратегия.
/// RSI &lt; OversoldLevel → Buy (перепроданность), RSI &gt; OverboughtLevel → Sell (перекупленность).
/// Используется сглаживание Уайлдера с периодом 14 по умолчанию.
/// </summary>
public sealed class RsiStrategy : IMarketStrategy
{
    private readonly ConcurrentDictionary<string, RsiState> states = new();
    private readonly int period;
    private readonly double oversoldLevel;
    private readonly double overboughtLevel;
    private readonly double defaultQuantity;
    private bool isDisposed;

    /// <summary>
    /// Создаёт RSI-стратегию.
    /// </summary>
    /// <param name="period">Период расчёта RSI.</param>
    /// <param name="oversoldLevel">Уровень перепроданности.</param>
    /// <param name="overboughtLevel">Уровень перекупленности.</param>
    /// <param name="defaultQuantity">Объём ордера по умолчанию.</param>
    public RsiStrategy(int period = 14, double oversoldLevel = 30, double overboughtLevel = 70, double defaultQuantity = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 2);
        this.period = period;
        this.oversoldLevel = oversoldLevel;
        this.overboughtLevel = overboughtLevel;
        this.defaultQuantity = defaultQuantity;
    }

    /// <inheritdoc />
    public string Name => "RSI";

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot?.LastTradePrice is not { } price || price <= 0)
            return MarketTradeSignal.Hold(assetId, "Нет данных о цене");

        var state = states.GetOrAdd(assetId, _ => new RsiState(period));

        if (!state.IsReady)
            return MarketTradeSignal.Hold(assetId, $"Накопление данных RSI ({state.Count}/{period + 1})");

        var rsi = state.CurrentRsi;

        if (rsi < oversoldLevel)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min((oversoldLevel - rsi) / oversoldLevel, 1.0),
                Reason = $"RSI = {rsi:F1} < {oversoldLevel} (перепроданность)"
            };
        }

        if (rsi > overboughtLevel)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min((rsi - overboughtLevel) / (100 - overboughtLevel), 1.0),
                Reason = $"RSI = {rsi:F1} > {overboughtLevel} (перекупленность)"
            };
        }

        return MarketTradeSignal.Hold(assetId, $"RSI = {rsi:F1} — нейтральная зона");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        if (isDisposed || snapshot.LastTradePrice is not { } price || price <= 0)
            return;
        states.GetOrAdd(snapshot.AssetId, _ => new RsiState(period)).Add(price);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        states.Clear();
    }
}

#endregion

#region MACD

/// <summary>
/// MACD Crossover стратегия.
/// MACD Line = EMA(fast) − EMA(slow), Signal Line = EMA(signal) of MACD Line.
/// MACD пересекает Signal снизу вверх → Buy, сверху вниз → Sell.
/// </summary>
public sealed class MacdCrossoverStrategy : IMarketStrategy
{
    private readonly ConcurrentDictionary<string, MacdState> states = new();
    private readonly int fastPeriod;
    private readonly int slowPeriod;
    private readonly int signalPeriod;
    private readonly double defaultQuantity;
    private bool isDisposed;

    /// <summary>
    /// Создаёт MACD crossover стратегию.
    /// </summary>
    /// <param name="fastPeriod">Период быстрой EMA.</param>
    /// <param name="slowPeriod">Период медленной EMA.</param>
    /// <param name="signalPeriod">Период сигнальной EMA.</param>
    /// <param name="defaultQuantity">Объём ордера по умолчанию.</param>
    public MacdCrossoverStrategy(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, double defaultQuantity = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fastPeriod, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slowPeriod, fastPeriod);
        this.fastPeriod = fastPeriod;
        this.slowPeriod = slowPeriod;
        this.signalPeriod = signalPeriod;
        this.defaultQuantity = defaultQuantity;
    }

    /// <inheritdoc />
    public string Name => "MACD";

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot?.LastTradePrice is not { } price || price <= 0)
            return MarketTradeSignal.Hold(assetId, "Нет данных о цене");

        var state = states.GetOrAdd(assetId, _ => new MacdState(fastPeriod, slowPeriod, signalPeriod));

        if (!state.IsReady)
            return MarketTradeSignal.Hold(assetId, $"Накопление данных MACD ({state.Count}/{slowPeriod + signalPeriod})");

        var macdLine = state.MacdLine;
        var signalLine = state.SignalLine;
        var prevMacd = state.PreviousMacdLine;
        var prevSignal = state.PreviousSignalLine;

        // Пересечение MACD вверх через Signal → Buy
        if (prevMacd <= prevSignal && macdLine > signalLine)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(Math.Abs(macdLine - signalLine) / price * 1000, 1.0),
                Reason = $"MACD ({macdLine:F4}) пересёк Signal ({signalLine:F4}) снизу вверх"
            };
        }

        // Пересечение MACD вниз через Signal → Sell
        if (prevMacd >= prevSignal && macdLine < signalLine)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = defaultQuantity,
                Price = price.ToString("G", CultureInfo.InvariantCulture),
                Confidence = Math.Min(Math.Abs(signalLine - macdLine) / price * 1000, 1.0),
                Reason = $"MACD ({macdLine:F4}) пересёк Signal ({signalLine:F4}) сверху вниз"
            };
        }

        var histogram = macdLine - signalLine;
        return MarketTradeSignal.Hold(assetId, $"MACD = {macdLine:F4}, Signal = {signalLine:F4}, Histogram = {histogram:F4}");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        if (isDisposed || snapshot.LastTradePrice is not { } price || price <= 0)
            return;
        states.GetOrAdd(snapshot.AssetId, _ => new MacdState(fastPeriod, slowPeriod, signalPeriod)).Add(price);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        states.Clear();
    }
}

#endregion

#region Composite Strategy

/// <summary>
/// Композитная стратегия — голосование нескольких индикаторов.
/// Агрегирует сигналы дочерних стратегий и выдаёт общий сигнал по большинству.
/// </summary>
/// <remarks>
/// Каждая дочерняя стратегия голосует Buy/Sell/Hold.
/// Если количество Buy-голосов ≥ quorum → Buy. Аналогично для Sell.
/// Уверенность = средняя уверенность голосов.
/// </remarks>
public sealed class CompositeStrategy : IMarketStrategy
{
    private readonly IMarketStrategy[] strategies;
    private readonly int quorum;
    private readonly double defaultQuantity;
    private bool isDisposed;

    /// <summary>
    /// Создаёт композитную стратегию.
    /// </summary>
    /// <param name="strategies">Дочерние стратегии.</param>
    /// <param name="quorum">Минимум голосов для сигнала (по умолчанию: большинство).</param>
    /// <param name="defaultQuantity">Объём ордера по умолчанию.</param>
    public CompositeStrategy(IMarketStrategy[] strategies, int? quorum = null, double defaultQuantity = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(strategies.Length);
        this.strategies = strategies;
        this.quorum = quorum ?? (strategies.Length / 2 + 1);
        this.defaultQuantity = defaultQuantity;
    }

    /// <inheritdoc />
    public string Name => "Composite";

    /// <inheritdoc />
    public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var buyVotes = 0;
        var sellVotes = 0;
        var buyConfidence = 0.0;
        var sellConfidence = 0.0;
        string? lastPrice = null;
        var reasons = new List<string>();

        foreach (var strategy in strategies)
        {
            var signal = strategy.Evaluate(priceStream, assetId);

            if (signal.Action == TradeAction.Buy)
            {
                buyVotes++;
                buyConfidence += signal.Confidence;
                lastPrice ??= signal.Price;
                reasons.Add($"{strategy.Name}:Buy");
            }
            else if (signal.Action == TradeAction.Sell)
            {
                sellVotes++;
                sellConfidence += signal.Confidence;
                lastPrice ??= signal.Price;
                reasons.Add($"{strategy.Name}:Sell");
            }
            else
            {
                reasons.Add($"{strategy.Name}:Hold");
            }
        }

        var reasonStr = string.Join(", ", reasons);

        if (buyVotes >= quorum)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Buy,
                Quantity = defaultQuantity,
                Price = lastPrice,
                Confidence = buyConfidence / buyVotes,
                Reason = $"Composite Buy ({buyVotes}/{strategies.Length}): {reasonStr}"
            };
        }

        if (sellVotes >= quorum)
        {
            return new MarketTradeSignal
            {
                AssetId = assetId,
                Action = TradeAction.Sell,
                Quantity = defaultQuantity,
                Price = lastPrice,
                Confidence = sellConfidence / sellVotes,
                Reason = $"Composite Sell ({sellVotes}/{strategies.Length}): {reasonStr}"
            };
        }

        return MarketTradeSignal.Hold(assetId, $"Composite Hold: {reasonStr}");
    }

    /// <inheritdoc />
    public void OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        if (isDisposed) return;
        foreach (var strategy in strategies)
            strategy.OnPriceUpdated(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        foreach (var strategy in strategies)
            strategy.Dispose();
    }
}

#endregion

#region Внутренние структуры

/// <summary>
/// Скользящее окно цен для расчёта SMA и стандартного отклонения.
/// </summary>
internal sealed class PriceWindow
{
    private readonly double[] buffer;
    private int head;
    private int count;

    public PriceWindow(int capacity)
    {
        buffer = new double[capacity];
    }

    public int Count => count;

    public void Add(double price)
    {
        buffer[head] = price;
        head = (head + 1) % buffer.Length;
        if (count < buffer.Length) count++;
    }

    public double Average
    {
        get
        {
            if (count == 0) return 0;
            var sum = 0.0;
            for (int i = 0; i < count; i++)
                sum += buffer[i];
            return sum / count;
        }
    }

    public double StandardDeviation
    {
        get
        {
            if (count < 2) return 0;
            var avg = Average;
            var sumSq = 0.0;
            for (int i = 0; i < count; i++)
            {
                var diff = buffer[i] - avg;
                sumSq += diff * diff;
            }
            return Math.Sqrt(sumSq / count);
        }
    }
}

/// <summary>Тиковый аккумулятор для VWAP (без реальных объёмов).</summary>
internal sealed class VwapAccumulator
{
    private double cumulativePrice;
    private int count;

    public int Count => count;
    public double Vwap => count > 0 ? cumulativePrice / count : 0;

    public void Add(double price)
    {
        cumulativePrice += price;
        count++;
    }
}

/// <summary>Состояние RSI: Wilder's smoothed average gain/loss.</summary>
internal sealed class RsiState
{
    private readonly int period;
    private double previousPrice;
    private double averageGain;
    private double averageLoss;
    private int count;
    private bool initialized;

    public RsiState(int period) => this.period = period;

    public int Count => count;
    public bool IsReady => initialized;
    public double CurrentRsi => averageLoss == 0 ? 100 : 100 - 100 / (1 + averageGain / averageLoss);

    public void Add(double price)
    {
        count++;

        if (count == 1)
        {
            previousPrice = price;
            return;
        }

        var change = price - previousPrice;
        previousPrice = price;
        var gain = change > 0 ? change : 0;
        var loss = change < 0 ? -change : 0;

        if (!initialized)
        {
            averageGain += gain;
            averageLoss += loss;

            if (count == period + 1)
            {
                averageGain /= period;
                averageLoss /= period;
                initialized = true;
            }
        }
        else
        {
            // Wilder's smoothing
            averageGain = (averageGain * (period - 1) + gain) / period;
            averageLoss = (averageLoss * (period - 1) + loss) / period;
        }
    }
}

/// <summary>Экспоненциальная скользящая средняя (EMA).</summary>
internal sealed class EmaCalculator
{
    private readonly double multiplier;
    private double value;
    private bool initialized;

    public EmaCalculator(int period)
    {
        multiplier = 2.0 / (period + 1);
    }

    public double Value => value;
    public bool IsInitialized => initialized;

    public void Add(double price)
    {
        if (!initialized)
        {
            value = price;
            initialized = true;
        }
        else
        {
            value = (price - value) * multiplier + value;
        }
    }
}

/// <summary>Состояние MACD: fast EMA, slow EMA, signal EMA.</summary>
internal sealed class MacdState
{
    private readonly EmaCalculator fastEma;
    private readonly EmaCalculator slowEma;
    private readonly EmaCalculator signalEma;
    private readonly int minCount;
    private int count;

    public MacdState(int fastPeriod, int slowPeriod, int signalPeriod)
    {
        fastEma = new EmaCalculator(fastPeriod);
        slowEma = new EmaCalculator(slowPeriod);
        signalEma = new EmaCalculator(signalPeriod);
        minCount = slowPeriod + signalPeriod;
    }

    public int Count => count;
    public bool IsReady => count >= minCount;
    public double MacdLine => fastEma.Value - slowEma.Value;
    public double SignalLine => signalEma.Value;
    public double PreviousMacdLine { get; private set; }
    public double PreviousSignalLine { get; private set; }

    public void Add(double price)
    {
        PreviousMacdLine = MacdLine;
        PreviousSignalLine = SignalLine;

        fastEma.Add(price);
        slowEma.Add(price);
        count++;

        if (fastEma.IsInitialized && slowEma.IsInitialized)
        {
            signalEma.Add(MacdLine);
        }
    }
}

#endregion
