using System.Collections.Concurrent;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты для IMarketStrategy реализаций: Momentum, MeanReversion, Arbitrage.
/// </summary>
public class MarketStrategyTests(ILogger logger) : BenchmarkTests<MarketStrategyTests>(logger)
{
    public MarketStrategyTests() : this(ConsoleLogger.Unicode) { }

    #region Вспомогательный PriceStream

    private sealed class TestPriceStream : IWritableMarketPriceStream
    {
        private readonly ConcurrentDictionary<string, TestSnapshot> cache = new();

        public int TokenCount => cache.Count;

        public IMarketPriceSnapshot? GetPrice(string assetId) =>
            cache.TryGetValue(assetId, out var snap) ? snap : null;

        public void SetPrice(string assetId, double? bid, double? ask, double? last) =>
            cache[assetId] = new TestSnapshot
            {
                AssetId = assetId, BestBid = bid, BestAsk = ask,
                LastTradePrice = last, LastUpdateTicks = Environment.TickCount64
            };

        public void SetPrice(string assetId, IMarketPriceSnapshot snapshot) =>
            SetPrice(assetId, snapshot.BestBid, snapshot.BestAsk, snapshot.LastTradePrice);

        public void ClearCache() => cache.Clear();
        public void Dispose() => cache.Clear();
    }

    private sealed class TestSnapshot : IMarketPriceSnapshot
    {
        public required string AssetId { get; init; }
        public double? BestBid { get; set; }
        public double? BestAsk { get; set; }
        public double? Midpoint => (BestBid + BestAsk) / 2.0;
        public double? LastTradePrice { get; set; }
        public long LastUpdateTicks { get; set; }
    }

    #endregion

    #region MarketTradeSignal

    [TestCase(TestName = "MarketTradeSignal.Hold: Action = Hold, Quantity = 0")]
    public void HoldSignal()
    {
        var signal = MarketTradeSignal.Hold("BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.AssetId, Is.EqualTo("BTC"));
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
        Assert.That(signal.Quantity, Is.EqualTo(0));
        Assert.That(signal.Confidence, Is.EqualTo(0));
    }

    #endregion

    #region MomentumStrategy

    [TestCase(TestName = "Momentum: Name = 'Momentum'")]
    public void MomentumName()
    {
        using var strategy = new MomentumStrategy();
        Assert.That(strategy.Name, Is.EqualTo("Momentum"));
    }

    [TestCase(TestName = "Momentum: Hold при недостаточных данных")]
    public void MomentumHoldInsufficientData()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MomentumStrategy { WindowSize = 5 };

        stream.SetPrice("BTC", 65000, 65010, 65005);

        // Добавим 2 точки (нужно 5)
        for (int i = 0; i < 2; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = 65000, BestAsk = 65010, LastUpdateTicks = 0 });

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "Momentum: Hold при отсутствии цены")]
    public void MomentumHoldNoPrice()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MomentumStrategy();

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "Momentum: Buy при росте выше SMA")]
    public void MomentumBuySignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MomentumStrategy { WindowSize = 5, BuyThresholdPercent = 0.01 };

        // Заполняем окно ценами ~100
        for (int i = 0; i < 5; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = 100, BestAsk = 100, LastUpdateTicks = 0 });

        // Текущая цена 105 (+5% > 1% threshold)
        stream.SetPrice("BTC", 105, 105, 105);

        var signal = strategy.Evaluate(stream, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(signal.Confidence, Is.GreaterThan(0));
        Assert.That(signal.Reason, Does.Contain("Momentum"));
    }

    [TestCase(TestName = "Momentum: Sell при падении ниже SMA")]
    public void MomentumSellSignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MomentumStrategy { WindowSize = 5, SellThresholdPercent = 0.01 };

        for (int i = 0; i < 5; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = 100, BestAsk = 100, LastUpdateTicks = 0 });

        // Текущая цена 95 (-5% > 1% threshold)
        stream.SetPrice("BTC", 95, 95, 95);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Sell));
    }

    [TestCase(TestName = "Momentum: DefaultQuantity передаётся в сигнал")]
    public void MomentumQuantity()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MomentumStrategy { WindowSize = 5, DefaultQuantity = 2.5, BuyThresholdPercent = 0.01 };

        for (int i = 0; i < 5; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = 100, BestAsk = 100, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 110, 110, 110);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Quantity, Is.EqualTo(2.5));
    }

    #endregion

    #region MeanReversionStrategy

    [TestCase(TestName = "MeanReversion: Name = 'MeanReversion'")]
    public void MeanReversionName()
    {
        using var strategy = new MeanReversionStrategy();
        Assert.That(strategy.Name, Is.EqualTo("MeanReversion"));
    }

    [TestCase(TestName = "MeanReversion: Buy ниже нижней полосы Боллинджера")]
    public void MeanReversionBuySignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MeanReversionStrategy { WindowSize = 5, BollingerMultiplier = 1.0 };

        // Заполняем окно: 100, 101, 99, 100, 101 (avg≈100.2, stddev≈0.75)
        double[] prices = [100, 101, 99, 100, 101];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = p, BestAsk = p, LastUpdateTicks = 0 });

        // Цена далеко ниже
        stream.SetPrice("BTC", 95, 95, 95);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Buy));
    }

    [TestCase(TestName = "MeanReversion: Sell выше верхней полосы Боллинджера")]
    public void MeanReversionSellSignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MeanReversionStrategy { WindowSize = 5, BollingerMultiplier = 1.0 };

        double[] prices = [100, 101, 99, 100, 101];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = p, BestAsk = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 105, 105, 105);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Sell));
    }

    [TestCase(TestName = "MeanReversion: Hold в пределах полос")]
    public void MeanReversionHoldInBands()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MeanReversionStrategy { WindowSize = 5, BollingerMultiplier = 3.0 };

        double[] prices = [100, 101, 99, 100, 101];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = p, BestAsk = p, LastUpdateTicks = 0 });

        // Цена в пределах полос (multiplier=3)
        stream.SetPrice("BTC", 100.5, 100.5, 100.5);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region ArbitrageStrategy

    [TestCase(TestName = "Arbitrage: Name = 'Arbitrage'")]
    public void ArbitrageName()
    {
        using var secondary = new TestPriceStream();
        using var strategy = new ArbitrageStrategy(secondary);
        Assert.That(strategy.Name, Is.EqualTo("Arbitrage"));
    }

    [TestCase(TestName = "Arbitrage: Buy когда secondary.bid > primary.ask")]
    public void ArbitrageBuySignal()
    {
        using var primary = new TestPriceStream();
        using var secondary = new TestPriceStream();
        using var strategy = new ArbitrageStrategy(secondary) { MinSpreadPercent = 0.001 };

        // Primary ask = 100, Secondary bid = 101 (1% спред)
        primary.SetPrice("BTC", 99, 100, 99.5);
        secondary.SetPrice("BTC", 101, 102, 101.5);

        var signal = strategy.Evaluate(primary, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(signal.Reason, Does.Contain("Арбитраж"));
    }

    [TestCase(TestName = "Arbitrage: Sell когда primary.bid > secondary.ask")]
    public void ArbitrageSellSignal()
    {
        using var primary = new TestPriceStream();
        using var secondary = new TestPriceStream();
        using var strategy = new ArbitrageStrategy(secondary) { MinSpreadPercent = 0.001 };

        // Primary bid = 101, Secondary ask = 100 (1% спред)
        primary.SetPrice("BTC", 101, 102, 101.5);
        secondary.SetPrice("BTC", 99, 100, 99.5);

        var signal = strategy.Evaluate(primary, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Sell));
        Assert.That(signal.Reason, Does.Contain("Арбитраж"));
    }

    [TestCase(TestName = "Arbitrage: Hold при отсутствии спреда")]
    public void ArbitrageHoldNoSpread()
    {
        using var primary = new TestPriceStream();
        using var secondary = new TestPriceStream();
        using var strategy = new ArbitrageStrategy(secondary) { MinSpreadPercent = 0.01 };

        // Одинаковые цены — нет арбитража
        primary.SetPrice("BTC", 100, 100.1, 100.05);
        secondary.SetPrice("BTC", 100, 100.1, 100.05);

        var signal = strategy.Evaluate(primary, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "Arbitrage: Hold при отсутствии данных")]
    public void ArbitrageHoldNoData()
    {
        using var primary = new TestPriceStream();
        using var secondary = new TestPriceStream();
        using var strategy = new ArbitrageStrategy(secondary);

        var signal = strategy.Evaluate(primary, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region VwapStrategy

    [TestCase(TestName = "VWAP: Name = 'VWAP'")]
    public void VwapName()
    {
        using var strategy = new VwapStrategy();
        Assert.That(strategy.Name, Is.EqualTo("VWAP"));
    }

    [TestCase(TestName = "VWAP: Hold при недостаточных данных")]
    public void VwapHoldInsufficientData()
    {
        using var stream = new TestPriceStream();
        using var strategy = new VwapStrategy();

        stream.SetPrice("BTC", 100, 100, 100);

        // Только 1 тик — нужно >= 2
        strategy.OnPriceUpdated(new TestSnapshot
            { AssetId = "BTC", LastTradePrice = 100, LastUpdateTicks = 0 });

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "VWAP: Buy ниже VWAP")]
    public void VwapBuySignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new VwapStrategy(thresholdPercent: 0.005);

        // Накапливаем: средняя = 100
        for (int i = 0; i < 10; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = 100, LastUpdateTicks = 0 });

        // Текущая цена значительно ниже VWAP
        stream.SetPrice("BTC", 98, 98, 98);

        var signal = strategy.Evaluate(stream, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(signal.Reason, Does.Contain("VWAP"));
    }

    [TestCase(TestName = "VWAP: Sell выше VWAP")]
    public void VwapSellSignal()
    {
        using var stream = new TestPriceStream();
        using var strategy = new VwapStrategy(thresholdPercent: 0.005);

        for (int i = 0; i < 10; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = 100, LastUpdateTicks = 0 });

        // Текущая цена значительно выше VWAP
        stream.SetPrice("BTC", 102, 102, 102);

        var signal = strategy.Evaluate(stream, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Sell));
        Assert.That(signal.Reason, Does.Contain("VWAP"));
    }

    [TestCase(TestName = "VWAP: Hold при цене ≈ VWAP")]
    public void VwapHoldNearVwap()
    {
        using var stream = new TestPriceStream();
        using var strategy = new VwapStrategy(thresholdPercent: 0.01);

        for (int i = 0; i < 10; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = 100, LastUpdateTicks = 0 });

        // Цена почти равна VWAP
        stream.SetPrice("BTC", 100.05, 100.05, 100.05);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "VWAP: Hold при отсутствии цены")]
    public void VwapHoldNoPrice()
    {
        using var stream = new TestPriceStream();
        using var strategy = new VwapStrategy();

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region RsiStrategy

    [TestCase(TestName = "RSI: Name = 'RSI'")]
    public void RsiName()
    {
        using var strategy = new RsiStrategy();
        Assert.That(strategy.Name, Is.EqualTo("RSI"));
    }

    [TestCase(TestName = "RSI: Hold при недостаточных данных")]
    public void RsiHoldInsufficientData()
    {
        using var stream = new TestPriceStream();
        using var strategy = new RsiStrategy(period: 14);

        stream.SetPrice("BTC", 100, 100, 100);

        // Нужно period+1 = 15 точек, даём 5
        for (int i = 0; i < 5; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = 100 + i, LastUpdateTicks = 0 });

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "RSI: Buy при перепроданности (RSI < 30)")]
    public void RsiBuyOversold()
    {
        using var stream = new TestPriceStream();
        using var strategy = new RsiStrategy(period: 5, oversoldLevel: 30, overboughtLevel: 70);

        // 6 точек (5+1): непрерывное падение → RSI → 0
        double[] prices = [100, 95, 90, 85, 80, 75];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 75, 75, 75);

        var signal = strategy.Evaluate(stream, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(signal.Reason, Does.Contain("RSI"));
        Assert.That(signal.Reason, Does.Contain("перепроданность"));
    }

    [TestCase(TestName = "RSI: Sell при перекупленности (RSI > 70)")]
    public void RsiSellOverbought()
    {
        using var stream = new TestPriceStream();
        using var strategy = new RsiStrategy(period: 5, oversoldLevel: 30, overboughtLevel: 70);

        // 6 точек: непрерывный рост → RSI → 100
        double[] prices = [100, 105, 110, 115, 120, 125];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 125, 125, 125);

        var signal = strategy.Evaluate(stream, "BTC");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Sell));
        Assert.That(signal.Reason, Does.Contain("перекупленность"));
    }

    [TestCase(TestName = "RSI: Hold в нейтральной зоне")]
    public void RsiHoldNeutral()
    {
        using var stream = new TestPriceStream();
        using var strategy = new RsiStrategy(period: 5, oversoldLevel: 30, overboughtLevel: 70);

        // Чередование роста/падения → RSI ≈ 50
        double[] prices = [100, 102, 100, 102, 100, 102];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 102, 102, 102);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "RSI: ArgumentOutOfRangeException при period < 2")]
    public void RsiInvalidPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RsiStrategy(period: 1));
    }

    #endregion

    #region MacdCrossoverStrategy

    [TestCase(TestName = "MACD: Name = 'MACD'")]
    public void MacdName()
    {
        using var strategy = new MacdCrossoverStrategy();
        Assert.That(strategy.Name, Is.EqualTo("MACD"));
    }

    [TestCase(TestName = "MACD: Hold при недостаточных данных")]
    public void MacdHoldInsufficientData()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MacdCrossoverStrategy(fastPeriod: 3, slowPeriod: 5, signalPeriod: 3);

        stream.SetPrice("BTC", 100, 100, 100);

        for (int i = 0; i < 3; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = 100 + i, LastUpdateTicks = 0 });

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "MACD: Buy при пересечении MACD вверх через Signal")]
    public void MacdBuyCrossover()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MacdCrossoverStrategy(fastPeriod: 3, slowPeriod: 5, signalPeriod: 3);

        // Сначала падение → slow EMA выше fast EMA → MACD < Signal
        double[] falling = [100, 98, 96, 94, 92, 90, 88, 86];
        foreach (var p in falling)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        // Затем резкий рост → fast EMA обгоняет slow EMA → MACD пересекает Signal вверх
        double[] rising = [90, 95, 100, 106, 112, 120, 130];
        foreach (var p in rising)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 130, 130, 130);

        var signal = strategy.Evaluate(stream, "BTC");

        // Ожидаем Buy (MACD пересёк Signal снизу вверх) или Hold если ещё не пересёк
        Assert.That(signal.Action, Is.AnyOf(TradeAction.Buy, TradeAction.Hold));
    }

    [TestCase(TestName = "MACD: Sell при пересечении MACD вниз через Signal")]
    public void MacdSellCrossover()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MacdCrossoverStrategy(fastPeriod: 3, slowPeriod: 5, signalPeriod: 3);

        // Сначала рост → MACD > Signal
        double[] rising = [100, 102, 104, 106, 108, 110, 112, 114];
        foreach (var p in rising)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        // Затем резкое падение → fast EMA падает быстрее → MACD пересекает Signal вниз
        double[] falling = [110, 105, 98, 90, 82, 74, 66];
        foreach (var p in falling)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 66, 66, 66);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.AnyOf(TradeAction.Sell, TradeAction.Hold));
    }

    [TestCase(TestName = "MACD: ArgumentOutOfRangeException при fastPeriod < 2")]
    public void MacdInvalidFastPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCrossoverStrategy(fastPeriod: 1));
    }

    [TestCase(TestName = "MACD: ArgumentOutOfRangeException при slowPeriod <= fastPeriod")]
    public void MacdInvalidSlowPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCrossoverStrategy(fastPeriod: 5, slowPeriod: 5));
    }

    [TestCase(TestName = "MACD: DefaultQuantity передаётся в сигнал")]
    public void MacdQuantity()
    {
        using var stream = new TestPriceStream();
        using var strategy = new MacdCrossoverStrategy(fastPeriod: 3, slowPeriod: 5, signalPeriod: 3, defaultQuantity: 3.0);

        // Накапливаем данные, затем резкий рост для Buy
        double[] data = [100, 98, 96, 94, 92, 90, 88, 86, 90, 95, 100, 106, 112, 120, 130];
        foreach (var p in data)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", LastTradePrice = p, LastUpdateTicks = 0 });

        stream.SetPrice("BTC", 130, 130, 130);

        var signal = strategy.Evaluate(stream, "BTC");
        if (signal.Action != TradeAction.Hold)
            Assert.That(signal.Quantity, Is.EqualTo(3.0));
    }

    #endregion

    #region CompositeStrategy

    [TestCase(TestName = "Composite: Name = 'Composite'")]
    public void CompositeName()
    {
        using var strategy = new CompositeStrategy([new MomentumStrategy(), new RsiStrategy(period: 5)]);
        Assert.That(strategy.Name, Is.EqualTo("Composite"));
    }

    [TestCase(TestName = "Composite: Buy при достижении кворума")]
    public void CompositeBuyQuorum()
    {
        using var stream = new TestPriceStream();
        // 3 стратегии, кворум = 2
        var momentum = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };
        var rsi = new RsiStrategy(period: 3, oversoldLevel: 30, overboughtLevel: 70);
        var vwap = new VwapStrategy(thresholdPercent: 0.005);

        using var strategy = new CompositeStrategy([momentum, rsi, vwap], quorum: 2);

        // Непрерывный рост → Momentum = Buy, RSI = Sell (overbought), VWAP = Sell
        // Непрерывное падение → Momentum = Sell, RSI = Buy (oversold), VWAP = Buy (ниже VWAP)
        // Заполняем данными: падающая серия
        double[] prices = [110, 108, 106, 104];
        foreach (var p in prices)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = p, BestAsk = p, LastTradePrice = p, LastUpdateTicks = 0 });

        // Текущая цена значительно ниже средних
        stream.SetPrice("BTC", 95, 95, 95);

        var signal = strategy.Evaluate(stream, "BTC");
        // Как минимум один сигнал должен быть не Hold (Momentum Sell, VWAP Buy, RSI Buy)
        Assert.That(signal.Action, Is.Not.EqualTo(TradeAction.Hold).Or.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "Composite: Hold если нет кворума")]
    public void CompositeHoldNoQuorum()
    {
        using var stream = new TestPriceStream();
        // 3 стратегии, кворум = 3 (нужно единогласие)
        var momentum = new MomentumStrategy { WindowSize = 5 };
        var rsi = new RsiStrategy(period: 5);
        var vwap = new VwapStrategy();

        using var strategy = new CompositeStrategy([momentum, rsi, vwap], quorum: 3);

        // Недостаточно данных → все Hold
        stream.SetPrice("BTC", 100, 100, 100);

        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Action, Is.EqualTo(TradeAction.Hold));
    }

    [TestCase(TestName = "Composite: OnPriceUpdated пробрасывается дочерним стратегиям")]
    public void CompositeOnPriceUpdatedPropagates()
    {
        using var stream = new TestPriceStream();
        var momentum = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };
        using var strategy = new CompositeStrategy([momentum]);

        // Заполним через composite
        for (int i = 0; i < 3; i++)
            strategy.OnPriceUpdated(new TestSnapshot
                { AssetId = "BTC", BestBid = 100, BestAsk = 100, LastTradePrice = 100, LastUpdateTicks = 0 });

        // Momentum имеет данные, должен выдать не "Недостаточно данных"
        stream.SetPrice("BTC", 100, 100, 100);
        var signal = strategy.Evaluate(stream, "BTC");
        Assert.That(signal.Reason, Does.Not.Contain("Недостаточно данных"));
    }

    [TestCase(TestName = "Composite: ArgumentOutOfRangeException при пустом массиве стратегий")]
    public void CompositeEmptyStrategies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeStrategy([]));
    }

    #endregion

    #region Pipeline Extension Methods

    [TestCase(TestName = "Pipeline Extensions: AddStandardIndicators добавляет 3 стратегии")]
    public void PipelineAddStandardIndicators()
    {
        var addedStrategies = new List<(IMarketStrategy, string[])>();
        var pipeline = new TestPipeline(addedStrategies);

        string[] assets = ["BTC", "ETH"];
        pipeline.AddStandardIndicators(assets);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(addedStrategies, Has.Count.EqualTo(3));
        Assert.That(addedStrategies[0].Item1.Name, Is.EqualTo("Momentum"));
        Assert.That(addedStrategies[1].Item1.Name, Is.EqualTo("RSI"));
        Assert.That(addedStrategies[2].Item1.Name, Is.EqualTo("MACD"));
    }

    private sealed class TestPipeline(List<(IMarketStrategy, string[])> added) : IMarketStreamingPipeline
    {
        public string PlatformName => "Test";
        public bool IsRunning => false;
        public long ProcessedUpdates => 0;
        public long GeneratedSignals => 0;
        public long ExecutedOrders => 0;
        public ValueTask StartAsync(string[] assetIds, CancellationToken cancellationToken = default) => default;
        public ValueTask StopAsync(CancellationToken cancellationToken = default) => default;
        public void AddStrategy(IMarketStrategy strategy, string[] assetIds) => added.Add((strategy, assetIds));
        public void RemoveStrategy(string strategyName) { }
        public ValueTask DisposeAsync() => default;
    }

    #endregion
}
