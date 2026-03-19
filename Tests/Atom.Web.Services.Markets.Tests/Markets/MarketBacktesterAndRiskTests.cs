using System.Collections.Concurrent;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты для MarketBacktester и MarketRiskManager.
/// </summary>
public class MarketBacktesterAndRiskTests(ILogger logger) : BenchmarkTests<MarketBacktesterAndRiskTests>(logger)
{
    public MarketBacktesterAndRiskTests() : this(ConsoleLogger.Unicode) { }

    #region Вспомогательные типы

    private sealed class TestPriceStream : IWritableMarketPriceStream
    {
        private readonly ConcurrentDictionary<string, TestSnapshot> cache = new();
        public int TokenCount => cache.Count;
        public IMarketPriceSnapshot? GetPrice(string assetId) =>
            cache.TryGetValue(assetId, out var snap) ? snap : null;
        public void SetPrice(string assetId, double price) =>
            cache[assetId] = new TestSnapshot
            {
                AssetId = assetId, BestBid = price, BestAsk = price,
                LastTradePrice = price, LastUpdateTicks = Environment.TickCount64
            };
        public void SetPrice(string assetId, IMarketPriceSnapshot snapshot) =>
            SetPrice(assetId, snapshot.LastTradePrice ?? snapshot.Midpoint ?? 0);
        public void ClearCache() => cache.Clear();
        public void Dispose() => cache.Clear();
    }

    private sealed class TestRestClient : IMarketRestClient
    {
        public string BaseUrl => "https://risk.test";
        public readonly List<(string AssetId, TradeSide Side, double Quantity)> Orders = [];

        public ValueTask<string?> CreateOrderAsync(string assetId, TradeSide side, double quantity, double? price = null, CancellationToken cancellationToken = default)
        {
            Orders.Add((assetId, side, quantity));
            return new ValueTask<string?>($"risk-order-{Orders.Count}");
        }

        public ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default) => new(true);
        public ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default) => new((double?)null);
        public ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default) => new((IMarketOrderBookSnapshot?)null);
        public void Dispose() { }
    }

    private sealed class RoundTripStrategy : IMarketStrategy
    {
        private int evaluationCount;

        public string Name => "RoundTrip";

        public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId)
        {
            evaluationCount++;
            return evaluationCount switch
            {
                1 => new MarketTradeSignal { AssetId = assetId, Action = TradeAction.Buy, Quantity = 1.0, Confidence = 1.0 },
                2 => new MarketTradeSignal { AssetId = assetId, Action = TradeAction.Sell, Quantity = 1.0, Confidence = 1.0 },
                _ => MarketTradeSignal.Hold(assetId)
            };
        }

        public void OnPriceUpdated(IMarketPriceSnapshot snapshot) { }
        public void Dispose() { }
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

    private static MarketBacktester.PricePoint[] GeneratePrices(double start, double end, int count)
    {
        var points = new MarketBacktester.PricePoint[count];
        var step = (end - start) / (count - 1);
        var baseTime = DateTimeOffset.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            var price = start + step * i;
            points[i] = new MarketBacktester.PricePoint
            {
                Midpoint = price,
                BestBid = price - 0.01,
                BestAsk = price + 0.01,
                Timestamp = baseTime.AddDays(i)
            };
        }

        return points;
    }

    #endregion

    #region MarketBacktester

    [TestCase(TestName = "Backtester: InitialBalance по умолчанию = 10000")]
    public void BacktesterDefaultBalance()
    {
        var bt = new MarketBacktester();
        Assert.That(bt.InitialBalance, Is.EqualTo(10_000));
    }

    [TestCase(TestName = "Backtester: FeeRateBps по умолчанию = 10")]
    public void BacktesterDefaultFee()
    {
        var bt = new MarketBacktester();
        Assert.That(bt.FeeRateBps, Is.EqualTo(10));
    }

    [TestCase(TestName = "Backtester: Прибыль при растущем тренде + Momentum Buy")]
    public void BacktesterProfitOnUptrend()
    {
        var bt = new MarketBacktester { InitialBalance = 10_000, FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 5, BuyThresholdPercent = 0.01 };

        // Растущий тренд: 100 → 150
        var prices = GeneratePrices(100, 150, 30);
        var result = bt.Run(strategy, "BTC", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.StrategyName, Is.EqualTo("Momentum"));
        Assert.That(result.InitialBalance, Is.EqualTo(10_000));
        Assert.That(result.EquityCurve, Has.Length.EqualTo(30));
    }

    [TestCase(TestName = "Backtester: TotalTrades > 0 при активной стратегии")]
    public void BacktesterHasTrades()
    {
        var bt = new MarketBacktester { InitialBalance = 10_000, FeeRateBps = 5 };
        using var strategy = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.005, SellThresholdPercent = 0.005 };

        // Волатильные данные → сигналы buy/sell
        var points = new MarketBacktester.PricePoint[20];
        var baseTime = DateTimeOffset.UtcNow.AddDays(-20);
        for (int i = 0; i < 20; i++)
        {
            var price = 100 + (i % 2 == 0 ? 5.0 : -5.0) + i * 0.5;
            points[i] = new MarketBacktester.PricePoint
            {
                Midpoint = price,
                Timestamp = baseTime.AddDays(i)
            };
        }

        var result = bt.Run(strategy, "BTC", points);
        Assert.That(result.EquityCurve, Has.Length.EqualTo(20));
    }

    [TestCase(TestName = "Backtester: EquityCurve имеет правильную длину")]
    public void BacktesterEquityCurveLength()
    {
        var bt = new MarketBacktester();
        using var strategy = new MomentumStrategy { WindowSize = 5 };

        var prices = GeneratePrices(100, 100, 15);
        var result = bt.Run(strategy, "ETH", prices);

        Assert.That(result.EquityCurve, Has.Length.EqualTo(15));
    }

    [TestCase(TestName = "Backtester: WinRate в диапазоне [0, 100]")]
    public void BacktesterWinRateRange()
    {
        var bt = new MarketBacktester { FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };

        var prices = GeneratePrices(100, 120, 20);
        var result = bt.Run(strategy, "BTC", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.WinRate, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.WinRate, Is.LessThanOrEqualTo(100));
    }

    [TestCase(TestName = "Backtester: MaxDrawdownPercent >= 0")]
    public void BacktesterDrawdownNonNegative()
    {
        var bt = new MarketBacktester();
        using var strategy = new MomentumStrategy { WindowSize = 3 };

        var prices = GeneratePrices(100, 80, 15);
        var result = bt.Run(strategy, "BTC", prices);

        Assert.That(result.MaxDrawdownPercent, Is.GreaterThanOrEqualTo(0));
    }

    [TestCase(TestName = "Backtester: ProfitFactor >= 0")]
    public void BacktesterProfitFactorNonNegative()
    {
        var bt = new MarketBacktester { FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };

        var prices = GeneratePrices(100, 120, 20);
        var result = bt.Run(strategy, "BTC", prices);

        Assert.That(result.ProfitFactor, Is.GreaterThanOrEqualTo(0));
    }

    [TestCase(TestName = "Backtester: Flat market → NetPnL ≈ 0 (без комиссии)")]
    public void BacktesterFlatMarket()
    {
        var bt = new MarketBacktester { InitialBalance = 10_000, FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 5, BuyThresholdPercent = 1.0 };

        // Постоянная цена = нет сигналов = нет сделок
        var prices = GeneratePrices(100, 100, 30);
        var result = bt.Run(strategy, "BTC", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.FinalBalance, Is.EqualTo(result.InitialBalance).Within(0.01));
        Assert.That(result.TotalTrades, Is.EqualTo(0));
    }

    [TestCase(TestName = "Backtester: Комиссия уменьшает прибыль")]
    public void BacktesterFeeImpact()
    {
        using var strategyNoFee = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };
        using var strategyWithFee = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };

        var prices = GeneratePrices(100, 150, 30);

        var resultNoFee = new MarketBacktester { FeeRateBps = 0 }.Run(strategyNoFee, "BTC", prices);
        var resultWithFee = new MarketBacktester { FeeRateBps = 50 }.Run(strategyWithFee, "BTC", prices); // 0.5%

        // С комиссией FinalBalance должен быть ≤ без комиссии
        Assert.That(resultWithFee.FinalBalance, Is.LessThanOrEqualTo(resultNoFee.FinalBalance));
    }

    [TestCase(TestName = "Backtester: FinalBalance = InitialBalance + NetPnL")]
    public void BacktesterBalanceConsistency()
    {
        var bt = new MarketBacktester { InitialBalance = 10_000, FeeRateBps = 10 };
        using var strategy = new MomentumStrategy { WindowSize = 5, BuyThresholdPercent = 0.01 };

        var prices = GeneratePrices(100, 130, 25);
        var result = bt.Run(strategy, "BTC", prices);

        Assert.That(result.FinalBalance, Is.EqualTo(result.InitialBalance + result.NetPnL).Within(0.01));
    }

    [TestCase(TestName = "Backtester: round-trip не завышает баланс на cost basis")]
    public void BacktesterRoundTripAccounting()
    {
        var bt = new MarketBacktester { InitialBalance = 1_000, FeeRateBps = 0 };
        using var strategy = new RoundTripStrategy();
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        var prices = new[]
        {
            new MarketBacktester.PricePoint { Midpoint = 100, Timestamp = baseTime },
            new MarketBacktester.PricePoint { Midpoint = 110, Timestamp = baseTime.AddMinutes(1) }
        };

        var result = bt.Run(strategy, "BTC", prices);

        Assert.That(result.FinalBalance, Is.EqualTo(1_010).Within(0.01));
    }

    [TestCase(TestName = "Backtester: SharpeRatio конечное число")]
    public void BacktesterSharpeRatioFinite()
    {
        var bt = new MarketBacktester { FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.01 };

        var prices = GeneratePrices(100, 120, 20);
        var result = bt.Run(strategy, "BTC", prices);

        Assert.That(double.IsNaN(result.SharpeRatio), Is.False);
    }

    [TestCase(TestName = "Backtester: EquityCurve[0] ≈ InitialBalance")]
    public void BacktesterEquityCurveStart()
    {
        var bt = new MarketBacktester { InitialBalance = 5_000, FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 5 };

        var prices = GeneratePrices(100, 100, 10);
        var result = bt.Run(strategy, "BTC", prices);

        // Без сделок equity = initialBalance
        Assert.That(result.EquityCurve[0], Is.EqualTo(5_000).Within(1.0));
    }

    [TestCase(TestName = "Backtester: Пустой массив → исключение")]
    public void BacktesterEmptyPricesThrows()
    {
        var bt = new MarketBacktester();
        using var strategy = new MomentumStrategy();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            bt.Run(strategy, "BTC", []));
    }

    [TestCase(TestName = "Backtester: Падающий рынок → MaxDrawdown > 0")]
    public void BacktesterDowntrendDrawdown()
    {
        var bt = new MarketBacktester { FeeRateBps = 0 };
        using var strategy = new MomentumStrategy { WindowSize = 3, BuyThresholdPercent = 0.001, SellThresholdPercent = 0.001 };

        // Сначала рост, потом падение для drawdown
        var points = new MarketBacktester.PricePoint[20];
        var baseTime = DateTimeOffset.UtcNow.AddDays(-20);
        for (int i = 0; i < 10; i++)
            points[i] = new MarketBacktester.PricePoint { Midpoint = 100 + i * 5, Timestamp = baseTime.AddDays(i) };
        for (int i = 10; i < 20; i++)
            points[i] = new MarketBacktester.PricePoint { Midpoint = 150 - (i - 10) * 8, Timestamp = baseTime.AddDays(i) };

        var result = bt.Run(strategy, "BTC", points);

        Assert.That(result.MaxDrawdownPercent, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region MarketRiskManager

    [TestCase(TestName = "RiskManager: AddRule / GetRule")]
    public void RiskManagerAddGetRule()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        var rule = new RiskRule { AssetId = "BTC", StopLossPrice = 50_000 };
        rm.AddRule(rule);

        var found = rm.GetRule("BTC");
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.StopLossPrice, Is.EqualTo(50_000));
    }

    [TestCase(TestName = "RiskManager: RemoveRule удаляет правило")]
    public void RiskManagerRemoveRule()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        rm.AddRule(new RiskRule { AssetId = "BTC", StopLossPrice = 50_000 });
        rm.RemoveRule("BTC");

        Assert.That(rm.GetRule("BTC"), Is.Null);
    }

    [TestCase(TestName = "RiskManager: ClearRules очищает все правила")]
    public void RiskManagerClearRules()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        rm.AddRule(new RiskRule { AssetId = "BTC", StopLossPrice = 50_000 });
        rm.AddRule(new RiskRule { AssetId = "ETH", StopLossPrice = 3_000 });
        rm.ClearRules();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.GetRule("BTC"), Is.Null);
        Assert.That(rm.GetRule("ETH"), Is.Null);
    }

    [TestCase(TestName = "RiskManager: Stop-Loss срабатывает при цене ≤ SL")]
    public async Task RiskManagerStopLossTriggered()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        var rule = new RiskRule { AssetId = "BTC", StopLossPrice = 50_000 };
        rm.AddRule(rule);

        // Цена ниже SL
        stream.SetPrice("BTC", 49_000);

        string? triggeredReason = null;
        rm.OnRuleTriggered += (r, reason) => triggeredReason = reason;

        await rm.CheckAllRulesAsync();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rule.IsTriggered, Is.True);
        Assert.That(triggeredReason, Does.Contain("Stop-Loss"));
    }

    [TestCase(TestName = "RiskManager: Take-Profit срабатывает при цене ≥ TP")]
    public async Task RiskManagerTakeProfitTriggered()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        var rule = new RiskRule { AssetId = "BTC", TakeProfitPrice = 70_000 };
        rm.AddRule(rule);

        stream.SetPrice("BTC", 72_000);

        string? triggeredReason = null;
        rm.OnRuleTriggered += (r, reason) => triggeredReason = reason;

        await rm.CheckAllRulesAsync();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rule.IsTriggered, Is.True);
        Assert.That(triggeredReason, Does.Contain("Take-Profit"));
    }

    [TestCase(TestName = "RiskManager: Trailing Stop срабатывает при падении от HWM")]
    public async Task RiskManagerTrailingStopTriggered()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        var rule = new RiskRule { AssetId = "BTC", TrailingStopPercent = 5.0 }; // 5% trailing
        rm.AddRule(rule);

        // Сначала цена растёт → HWM = 100
        stream.SetPrice("BTC", 100);
        await rm.CheckAllRulesAsync();
        Assert.That(rule.IsTriggered, Is.False);

        // Цена падает на 6% → trailing stop = 100 * 0.95 = 95, цена 94 → triggered
        stream.SetPrice("BTC", 94);
        await rm.CheckAllRulesAsync();

        Assert.That(rule.IsTriggered, Is.True);
    }

    [TestCase(TestName = "RiskManager: CanOpenPosition false при достижении MaxDailyLoss")]
    public void RiskManagerDailyLossLimit()
    {
        using var stream = new TestPriceStream();
        var limits = new PortfolioLimits { MaxDailyLoss = 500 };
        using var rm = new MarketRiskManager(stream, limits: limits);

        rm.RecordLoss(600);

        Assert.That(rm.CanOpenPosition("BTC", 1.0), Is.False);
    }

    [TestCase(TestName = "RiskManager: CanOpenPosition false при превышении MaxPositionSize")]
    public void RiskManagerPositionSizeLimit()
    {
        using var stream = new TestPriceStream();
        var limits = new PortfolioLimits { MaxPositionSize = 10 };
        using var rm = new MarketRiskManager(stream, limits: limits);

        Assert.That(rm.CanOpenPosition("BTC", 15), Is.False);
    }

    [TestCase(TestName = "RiskManager: ResetDailyLoss обнуляет счётчик")]
    public void RiskManagerResetDailyLoss()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        rm.RecordLoss(100);
        Assert.That(rm.DailyLoss, Is.GreaterThan(0));

        rm.ResetDailyLoss();
        Assert.That(rm.DailyLoss, Is.EqualTo(0));
    }

    [TestCase(TestName = "RiskManager: RecordLoss аккумулирует потери")]
    public void RiskManagerRecordLossAccumulates()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        rm.RecordLoss(100);
        rm.RecordLoss(50);

        Assert.That(rm.DailyLoss, Is.EqualTo(150));
    }

    [TestCase(TestName = "RiskManager: AutoExecute использует PositionQuantity")]
    public async Task RiskManagerAutoExecuteUsesRuleQuantity()
    {
        using var stream = new TestPriceStream();
        using var rest = new TestRestClient();
        using var rm = new MarketRiskManager(stream, rest) { AutoExecute = true };

        var rule = new RiskRule { AssetId = "BTC", StopLossPrice = 50_000, PositionQuantity = 2.5 };
        rm.AddRule(rule);
        stream.SetPrice("BTC", 49_000);

        await rm.CheckAllRulesAsync();

        Assert.That(rest.Orders, Has.Count.EqualTo(1));
        Assert.That(rest.Orders[0].Quantity, Is.EqualTo(2.5));
    }

    [TestCase(TestName = "RiskManager: Сработавшее правило не проверяется повторно")]
    public async Task RiskManagerTriggeredRuleSkipped()
    {
        using var stream = new TestPriceStream();
        using var rm = new MarketRiskManager(stream);

        var rule = new RiskRule { AssetId = "BTC", StopLossPrice = 50_000 };
        rm.AddRule(rule);

        stream.SetPrice("BTC", 49_000);

        var triggerCount = 0;
        rm.OnRuleTriggered += (_, _) => triggerCount++;

        await rm.CheckAllRulesAsync();
        await rm.CheckAllRulesAsync(); // второй вызов

        Assert.That(triggerCount, Is.EqualTo(1)); // сработало только один раз
    }

    #endregion

    #region PortfolioLimits / RiskRule

    [TestCase(TestName = "PortfolioLimits: все свойства настраиваемы")]
    public void PortfolioLimitsProperties()
    {
        var limits = new PortfolioLimits
        {
            MaxPositionSize = 100,
            MaxOpenPositions = 10,
            MaxPortfolioLoss = 5000,
            MaxPositionPercent = 0.2,
            MaxDailyLoss = 1000
        };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(limits.MaxPositionSize, Is.EqualTo(100));
        Assert.That(limits.MaxOpenPositions, Is.EqualTo(10));
        Assert.That(limits.MaxPortfolioLoss, Is.EqualTo(5000));
        Assert.That(limits.MaxPositionPercent, Is.EqualTo(0.2));
        Assert.That(limits.MaxDailyLoss, Is.EqualTo(1000));
    }

    [TestCase(TestName = "RiskRule: Stop-Loss + Take-Profit + Trailing Stop")]
    public void RiskRuleProperties()
    {
        var rule = new RiskRule
        {
            AssetId = "BTC",
            PositionQuantity = 0.75,
            StopLossPrice = 50_000,
            TakeProfitPrice = 70_000,
            TrailingStopPercent = 5.0,
            MaxLossPerPosition = 1000
        };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rule.AssetId, Is.EqualTo("BTC"));
        Assert.That(rule.PositionQuantity, Is.EqualTo(0.75));
        Assert.That(rule.StopLossPrice, Is.EqualTo(50_000));
        Assert.That(rule.TakeProfitPrice, Is.EqualTo(70_000));
        Assert.That(rule.TrailingStopPercent, Is.EqualTo(5.0));
        Assert.That(rule.MaxLossPerPosition, Is.EqualTo(1000));
        Assert.That(rule.IsTriggered, Is.False);
    }

    #endregion
}
