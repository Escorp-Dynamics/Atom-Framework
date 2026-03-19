using System.Collections.Concurrent;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты для MarketPortfolioTracker, MarketAlertSystem, MarketDataExporter.
/// </summary>
public class MarketServicesTests(ILogger logger) : BenchmarkTests<MarketServicesTests>(logger)
{
    public MarketServicesTests() : this(ConsoleLogger.Unicode) { }

    #region Вспомогательный PriceStream

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

    #region MarketPortfolioTracker

    [TestCase(TestName = "PortfolioTracker: RecordTrade Buy создаёт позицию")]
    public void TrackerRecordBuy()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000, fee: 5);

        var pos = tracker.GetPosition("BTC");
        using var scope = Assert.EnterMultipleScope();
        Assert.That(pos, Is.Not.Null);
        Assert.That(pos!.Quantity, Is.EqualTo(1.0));
        Assert.That(pos.AverageCostBasis, Is.EqualTo(50_000));
        Assert.That(pos.TotalFees, Is.EqualTo(5));
        Assert.That(pos.TradeCount, Is.EqualTo(1));
    }

    [TestCase(TestName = "PortfolioTracker: RecordTrade Sell закрывает long → RealizedPnL")]
    public void TrackerSellClosesLong()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        tracker.RecordTrade("BTC", TradeSide.Sell, 1.0, 55_000);

        var pos = tracker.GetPosition("BTC");
        using var scope = Assert.EnterMultipleScope();
        Assert.That(pos!.RealizedPnL, Is.EqualTo(5_000));
        Assert.That(pos.Quantity, Is.EqualTo(0));
        Assert.That(pos.IsClosed, Is.True);
    }

    [TestCase(TestName = "PortfolioTracker: Усреднение long")]
    public void TrackerAverageLong()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 60_000);

        var pos = tracker.GetPosition("BTC");
        using var scope = Assert.EnterMultipleScope();
        Assert.That(pos!.Quantity, Is.EqualTo(2.0));
        Assert.That(pos.AverageCostBasis, Is.EqualTo(55_000));
    }

    [TestCase(TestName = "PortfolioTracker: UnrealizedPnL при росте цены")]
    public void TrackerUnrealizedPnL()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 2.0, 50_000);
        stream.SetPrice("BTC", 55_000);

        var summary = tracker.GetSummary();
        // Unrealized PnL = (55000 - 50000) * 2 = 10000
        Assert.That(summary.TotalUnrealizedPnL, Is.EqualTo(10_000).Within(0.01));
    }

    [TestCase(TestName = "PortfolioTracker: OpenPositionCount корректен")]
    public void TrackerOpenPositionCount()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        tracker.RecordTrade("ETH", TradeSide.Buy, 10.0, 3_000);
        Assert.That(tracker.OpenPositionCount, Is.EqualTo(2));

        tracker.RecordTrade("BTC", TradeSide.Sell, 1.0, 55_000);
        Assert.That(tracker.OpenPositionCount, Is.EqualTo(1));
    }

    [TestCase(TestName = "PortfolioTracker: ClearPositions очищает всё")]
    public void TrackerClearPositions()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        tracker.ClearPositions();

        Assert.That(tracker.GetPosition("BTC"), Is.Null);
    }

    [TestCase(TestName = "PortfolioTracker: GetSummary NetPnL = Unrealized + Realized - Fees")]
    public void TrackerSummaryNetPnL()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000, fee: 10);
        tracker.RecordTrade("BTC", TradeSide.Sell, 1.0, 55_000, fee: 10);

        var summary = tracker.GetSummary();
        // Realized = 5000, Fees = 20, Unrealized = 0, Net = 5000 - 20 = 4980
        Assert.That(summary.NetPnL, Is.EqualTo(4_980).Within(0.01));
    }

    #endregion

    #region MarketPnLHistory

    [TestCase(TestName = "PnLHistory: TakeSnapshot записывает снимок")]
    public void HistoryTakeSnapshot()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);
        using var history = new MarketPnLHistory(tracker);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        stream.SetPrice("BTC", 52_000);

        var snap = history.TakeSnapshot();
        using var scope = Assert.EnterMultipleScope();
        Assert.That(history.Count, Is.EqualTo(1));
        Assert.That(snap.TotalMarketValue, Is.GreaterThan(0));
    }

    [TestCase(TestName = "PnLHistory: Latest возвращает последний снимок")]
    public void HistoryLatest()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);
        using var history = new MarketPnLHistory(tracker);

        Assert.That(history.Latest, Is.Null);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        history.TakeSnapshot();

        Assert.That(history.Latest, Is.Not.Null);
    }

    [TestCase(TestName = "PnLHistory: Clear обнуляет счётчик")]
    public void HistoryClear()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);
        using var history = new MarketPnLHistory(tracker);

        history.TakeSnapshot();
        history.TakeSnapshot();
        Assert.That(history.Count, Is.EqualTo(2));

        history.Clear();
        Assert.That(history.Count, Is.EqualTo(0));
    }

    #endregion

    #region MarketAlertSystem

    [TestCase(TestName = "AlertSystem: AddAlert / GetAlert")]
    public void AlertAddGet()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        var alert = new AlertDefinition
        {
            Id = "btc-high", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 60_000, AssetId = "BTC"
        };
        system.AddAlert(alert);

        Assert.That(system.GetAlert("btc-high"), Is.Not.Null);
    }

    [TestCase(TestName = "AlertSystem: RemoveAlert удаляет алерт")]
    public void AlertRemove()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        system.AddAlert(new AlertDefinition
        {
            Id = "a1", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 100, AssetId = "X"
        });
        system.RemoveAlert("a1");

        Assert.That(system.GetAlert("a1"), Is.Null);
    }

    [TestCase(TestName = "AlertSystem: PriceThreshold Above срабатывает")]
    public void AlertPriceAboveTriggered()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        var alert = new AlertDefinition
        {
            Id = "btc-up", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 60_000, AssetId = "BTC"
        };
        system.AddAlert(alert);

        double? triggeredValue = null;
        system.OnAlertTriggered += (a, val) => triggeredValue = val;

        stream.SetPrice("BTC", 65_000);
        system.CheckAlerts();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(alert.HasTriggered, Is.True);
        Assert.That(triggeredValue, Is.EqualTo(65_000));
    }

    [TestCase(TestName = "AlertSystem: PriceThreshold Below срабатывает")]
    public void AlertPriceBelowTriggered()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        var alert = new AlertDefinition
        {
            Id = "btc-down", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Below, Threshold = 50_000, AssetId = "BTC"
        };
        system.AddAlert(alert);

        stream.SetPrice("BTC", 48_000);
        system.CheckAlerts();

        Assert.That(alert.HasTriggered, Is.True);
    }

    [TestCase(TestName = "AlertSystem: OneShot отключается после срабатывания")]
    public void AlertOneShotDisabled()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        var alert = new AlertDefinition
        {
            Id = "once", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 100, AssetId = "X",
            OneShot = true
        };
        system.AddAlert(alert);

        stream.SetPrice("X", 200);
        system.CheckAlerts();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(alert.HasTriggered, Is.True);
        Assert.That(alert.IsEnabled, Is.False);
    }

    [TestCase(TestName = "AlertSystem: ActiveCount считает активные")]
    public void AlertActiveCount()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        system.AddAlert(new AlertDefinition
        {
            Id = "a1", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 100, AssetId = "X"
        });
        system.AddAlert(new AlertDefinition
        {
            Id = "a2", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 200, AssetId = "Y",
            IsEnabled = false
        });

        Assert.That(system.ActiveCount, Is.EqualTo(1));
    }

    [TestCase(TestName = "AlertSystem: DisconnectAll отключает все")]
    public void AlertDisconnectAll()
    {
        using var stream = new TestPriceStream();
        using var system = new MarketAlertSystem(stream);

        system.AddAlert(new AlertDefinition
        {
            Id = "a1", Condition = AlertCondition.PriceThreshold,
            Direction = AlertDirection.Above, Threshold = 100, AssetId = "X"
        });
        system.DisconnectAll();

        Assert.That(system.ActiveCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "AlertSystem: PnLThreshold с PortfolioTracker")]
    public void AlertPnLThreshold()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);
        using var system = new MarketAlertSystem(stream, tracker);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);

        var alert = new AlertDefinition
        {
            Id = "pnl-alert", Condition = AlertCondition.PnLThreshold,
            Direction = AlertDirection.Above, Threshold = 5_000, AssetId = "BTC"
        };
        system.AddAlert(alert);

        // Цена выросла → unrealized PnL = 10000 > threshold 5000
        stream.SetPrice("BTC", 60_000);
        system.CheckAlerts();

        Assert.That(alert.HasTriggered, Is.True);
    }

    #endregion

    #region MarketDataExporter

    [TestCase(TestName = "DataExporter: ExportPositions CSV содержит заголовок")]
    public void ExporterPositionsCsvHeader()
    {
        var exporter = new MarketDataExporter();
        var positions = new[] { CreateTestPosition("BTC", 1.0, 50_000, 55_000) };

        var csv = exporter.ExportPositions(positions, ExportFormat.Csv);

        Assert.That(csv, Does.StartWith("AssetId,Quantity,AverageCostBasis"));
    }

    [TestCase(TestName = "DataExporter: ExportPositions CSV содержит данные")]
    public void ExporterPositionsCsvData()
    {
        var exporter = new MarketDataExporter();
        var positions = new[] { CreateTestPosition("BTC", 1.0, 50_000, 55_000) };

        var csv = exporter.ExportPositions(positions, ExportFormat.Csv);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines, Has.Length.EqualTo(2)); // header + 1 data row
    }

    [TestCase(TestName = "DataExporter: ExportPositions JSON корректный")]
    public void ExporterPositionsJson()
    {
        var exporter = new MarketDataExporter();
        var positions = new[] { CreateTestPosition("ETH", 10.0, 3_000, 3_500) };

        var json = exporter.ExportPositions(positions, ExportFormat.Json);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.StartWith("["));
        Assert.That(json, Does.Contain("\"assetId\": \"ETH\""));
        Assert.That(json, Does.Contain("\"quantity\": 10"));
    }

    [TestCase(TestName = "DataExporter: ExportPnLHistory CSV содержит заголовок")]
    public void ExporterPnLCsvHeader()
    {
        var exporter = new MarketDataExporter();
        var snapshots = Array.Empty<IMarketPnLSnapshot>();

        var csv = exporter.ExportPnLHistory(snapshots, ExportFormat.Csv);

        Assert.That(csv, Does.StartWith("Timestamp,TotalMarketValue"));
    }

    [TestCase(TestName = "DataExporter: ExportPositions пустой массив → только заголовок")]
    public void ExporterEmptyPositionsCsv()
    {
        var exporter = new MarketDataExporter();
        var csv = exporter.ExportPositions([], ExportFormat.Csv);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(1)); // только заголовок
    }

    [TestCase(TestName = "DataExporter: ExportPositions JSON пустой массив")]
    public void ExporterEmptyPositionsJson()
    {
        var exporter = new MarketDataExporter();
        var json = exporter.ExportPositions([], ExportFormat.Json);

        Assert.That(json.Trim(), Is.EqualTo("[]"));
    }

    private static MarketPosition CreateTestPosition(string assetId, double qty, double costBasis, double currentPrice)
    {
        return new MarketPosition
        {
            AssetId = assetId,
            Quantity = qty,
            AverageCostBasis = costBasis,
            CurrentPrice = currentPrice,
            TradeCount = 1
        };
    }

    #endregion

    #region MarketVisualizer

    [TestCase(TestName = "Visualizer: RenderChart пустой массив → (нет данных)")]
    public void VisualizerEmptyChart()
    {
        var viz = new MarketVisualizer();
        var output = viz.RenderChart([], "Test");

        Assert.That(output, Does.Contain("нет данных"));
    }

    [TestCase(TestName = "Visualizer: RenderChart содержит заголовок")]
    public void VisualizerChartTitle()
    {
        var viz = new MarketVisualizer { Width = 20, Height = 5 };
        var output = viz.RenderChart([1, 2, 3, 4, 5], "My Chart");

        Assert.That(output, Does.Contain("My Chart"));
    }

    [TestCase(TestName = "Visualizer: RenderChart рендерит оси")]
    public void VisualizerChartAxes()
    {
        var viz = new MarketVisualizer { Width = 20, Height = 5 };
        var output = viz.RenderChart([10, 20, 30, 40, 50]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(output, Does.Contain("│"));   // вертикальная ось
        Assert.That(output, Does.Contain("└"));   // начало горизонтальной оси
        Assert.That(output, Does.Contain("─"));   // горизонтальная ось
    }

    [TestCase(TestName = "Visualizer: RenderChart с одинаковыми значениями")]
    public void VisualizerChartFlat()
    {
        var viz = new MarketVisualizer { Width = 10, Height = 3 };
        var output = viz.RenderChart([100, 100, 100, 100]);

        Assert.That(output, Does.Contain("█")); // должны быть блоки
    }

    [TestCase(TestName = "Visualizer: RenderBacktestSummary содержит метрики")]
    public void VisualizerBacktestSummary()
    {
        var viz = new MarketVisualizer();
        var result = new TestBacktestResult
        {
            StrategyName = "Momentum",
            InitialBalance = 10_000,
            FinalBalance = 12_000,
            NetPnL = 2_000,
            ReturnPercent = 20.0,
            TotalTrades = 15,
            WinRate = 60.0,
            SharpeRatio = 1.5,
            MaxDrawdownPercent = 8.0,
            ProfitFactor = 2.5,
            EquityCurve = [10000, 10500, 11000, 12000]
        };

        var output = viz.RenderBacktestSummary(result);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(output, Does.Contain("Momentum"));
        Assert.That(output, Does.Contain("10000"));
        Assert.That(output, Does.Contain("12000"));
        Assert.That(output, Does.Contain("Win Rate"));
        Assert.That(output, Does.Contain("Sharpe"));
    }

    [TestCase(TestName = "Visualizer: RenderPositionsTable содержит заголовок")]
    public void VisualizerPositionsTable()
    {
        var viz = new MarketVisualizer();
        var positions = new[] { CreateTestPosition("BTC", 1.0, 50_000, 55_000) };

        var output = viz.RenderPositionsTable(positions);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(output, Does.Contain("Asset"));
        Assert.That(output, Does.Contain("BTC"));
        Assert.That(output, Does.Contain("Open"));
    }

    [TestCase(TestName = "Visualizer: RenderPortfolioSummary содержит метрики")]
    public void VisualizerPortfolioSummary()
    {
        var viz = new MarketVisualizer();
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        stream.SetPrice("BTC", 55_000);

        var summary = tracker.GetSummary();
        var output = viz.RenderPortfolioSummary(summary);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(output, Does.Contain("Portfolio Summary"));
        Assert.That(output, Does.Contain("Open Positions"));
        Assert.That(output, Does.Contain("Market Value"));
    }

    [TestCase(TestName = "Visualizer: RenderEquityCurve использует RenderChart")]
    public void VisualizerEquityCurve()
    {
        var viz = new MarketVisualizer { Width = 20, Height = 5 };
        var result = new TestBacktestResult
        {
            StrategyName = "RSI",
            InitialBalance = 10_000, FinalBalance = 11_000,
            NetPnL = 1_000, ReturnPercent = 10.0,
            TotalTrades = 5, WinRate = 60.0,
            SharpeRatio = 1.0, MaxDrawdownPercent = 3.0,
            ProfitFactor = 2.0,
            EquityCurve = [10000, 10100, 10200, 10500, 11000]
        };

        var output = viz.RenderEquityCurve(result);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(output, Does.Contain("Equity Curve"));
        Assert.That(output, Does.Contain("RSI"));
    }

    private sealed class TestBacktestResult : IMarketBacktestResult
    {
        public required string StrategyName { get; init; }
        public required double InitialBalance { get; init; }
        public required double FinalBalance { get; init; }
        public required double NetPnL { get; init; }
        public required double ReturnPercent { get; init; }
        public required int TotalTrades { get; init; }
        public required double WinRate { get; init; }
        public required double SharpeRatio { get; init; }
        public required double MaxDrawdownPercent { get; init; }
        public required double ProfitFactor { get; init; }
        public required double[] EquityCurve { get; init; }
    }

    #endregion

    #region MarketOrderExecutor

    private sealed class FakeRestClient : IMarketRestClient
    {
        public string BaseUrl => "https://fake.api";
        public readonly List<(string AssetId, TradeSide Side, double Quantity)> Orders = [];
        public Func<Task>? BeforeCreateOrderAsync { get; set; }

        public ValueTask<string?> CreateOrderAsync(string assetId, TradeSide side, double quantity, double? price = null, CancellationToken cancellationToken = default)
        {
            return CreateOrderCoreAsync(assetId, side, quantity);
        }

        private async ValueTask<string?> CreateOrderCoreAsync(string assetId, TradeSide side, double quantity)
        {
            if (BeforeCreateOrderAsync is not null)
                await BeforeCreateOrderAsync().ConfigureAwait(false);

            Orders.Add((assetId, side, quantity));
            return $"order-{Orders.Count}";
        }

        public ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
            new(true);

        public ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default) =>
            new((double?)100.0);

        public ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default) =>
            new((IMarketOrderBookSnapshot?)null);

        public void Dispose() { }
    }

    [TestCase(TestName = "OrderExecutor: AddStrategy / RemoveStrategy")]
    public void ExecutorAddRemoveStrategy()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient);

        using var strategy = new MomentumStrategy { WindowSize = 3 };
        executor.AddStrategy(strategy, ["BTC"]);
        executor.RemoveStrategy("Momentum");

        // Не бросает исключений
        Assert.Pass();
    }

    [TestCase(TestName = "OrderExecutor: DryRun не создаёт реальный ордер")]
    public async Task ExecutorDryRun()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = true,
            MinConfidence = 0.0 // принимаем любой сигнал
        };

        // Создаём стратегию с очень маленьким окном для быстрого сигнала
        using var strategy = new MomentumStrategy { WindowSize = 2, BuyThresholdPercent = 0.001 };
        executor.AddStrategy(strategy, ["BTC"]);

        // Растущая цена → momentum buy
        stream.SetPrice("BTC", 100);
        await executor.EvaluateOnceAsync();
        stream.SetPrice("BTC", 110);
        await executor.EvaluateOnceAsync();

        // DryRun → FakeRestClient не должен иметь реальных ордеров
        Assert.That(restClient.Orders, Has.Count.EqualTo(0));
    }

    [TestCase(TestName = "OrderExecutor: EvaluateOnceAsync с реальным ордером")]
    public async Task ExecutorRealOrder()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = false,
            MinConfidence = 0.0,
            OrderCooldown = TimeSpan.Zero
        };

        // Используем пользовательскую стратегию что всегда возвращает Buy
        var strategy = new AlwaysBuyStrategy();
        executor.AddStrategy(strategy, ["BTC"]);
        stream.SetPrice("BTC", 100);

        await executor.EvaluateOnceAsync();

        Assert.That(restClient.Orders, Has.Count.GreaterThan(0));
    }

    [TestCase(TestName = "OrderExecutor: MinConfidence фильтрует слабые сигналы")]
    public async Task ExecutorMinConfidenceFilter()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = false,
            MinConfidence = 1.01 // выше максимально возможной уверенности
        };

        using var strategy = new MomentumStrategy { WindowSize = 2, BuyThresholdPercent = 0.001 };
        executor.AddStrategy(strategy, ["BTC"]);

        stream.SetPrice("BTC", 100);
        await executor.EvaluateOnceAsync();
        stream.SetPrice("BTC", 110);
        await executor.EvaluateOnceAsync();

        // Высокий MinConfidence → ордера не выставляются
        Assert.That(restClient.Orders, Has.Count.EqualTo(0));
    }

    [TestCase(TestName = "OrderExecutor: OnSignalGenerated вызывается")]
    public async Task ExecutorSignalEvent()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = true,
            MinConfidence = 0.0
        };

        var strategy = new AlwaysBuyStrategy();
        executor.AddStrategy(strategy, ["BTC"]);
        stream.SetPrice("BTC", 100);

        IMarketTradeSignal? captured = null;
        executor.OnSignalGenerated += s => captured = s;

        await executor.EvaluateOnceAsync();

        Assert.That(captured, Is.Not.Null);
    }

    [TestCase(TestName = "OrderExecutor: OnOrderExecuted вызывается в DryRun")]
    public async Task ExecutorOrderEvent()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = true,
            MinConfidence = 0.0
        };

        var strategy = new AlwaysBuyStrategy();
        executor.AddStrategy(strategy, ["BTC"]);
        stream.SetPrice("BTC", 100);

        string? orderId = null;
        executor.OnOrderExecuted += (_, id) => orderId = id;

        await executor.EvaluateOnceAsync();

        Assert.That(orderId, Does.StartWith("DRY-"));
    }

    [TestCase(TestName = "OrderExecutor: OrderCooldown блокирует повторные ордера")]
    public async Task ExecutorCooldown()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = false,
            MinConfidence = 0.0,
            OrderCooldown = TimeSpan.FromHours(1) // большой cooldown
        };

        var strategy = new AlwaysBuyStrategy();
        executor.AddStrategy(strategy, ["BTC"]);
        stream.SetPrice("BTC", 100);

        await executor.EvaluateOnceAsync(); // первый ордер
        await executor.EvaluateOnceAsync(); // cooldown → нет ордера

        Assert.That(restClient.Orders, Has.Count.EqualTo(1));
    }

    [TestCase(TestName = "OrderExecutor: DisposeAsync останавливает")]
    public async Task ExecutorDispose()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var executor = new MarketOrderExecutor(stream, restClient);
        executor.Start();

        await executor.DisposeAsync();
        // Не бросает исключений
        Assert.Pass();
    }

    [TestCase(TestName = "OrderExecutor: параллельная оценка не дублирует ордер")]
    public async Task ExecutorConcurrentEvaluateDoesNotDuplicateOrders()
    {
        using var stream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restClient.BeforeCreateOrderAsync = async () => await gate.Task.ConfigureAwait(false);

        var executor = new MarketOrderExecutor(stream, restClient)
        {
            DryRun = false,
            MinConfidence = 0.0,
            OrderCooldown = TimeSpan.Zero
        };

        var strategy = new AlwaysBuyStrategy();
        executor.AddStrategy(strategy, ["BTC"]);
        stream.SetPrice("BTC", 100);

        var first = executor.EvaluateOnceAsync().AsTask();
        var second = executor.EvaluateOnceAsync().AsTask();
        await Task.Yield();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.That(restClient.Orders, Has.Count.EqualTo(1));
    }

    /// <summary>Стратегия, всегда возвращающая Buy.</summary>
    private sealed class AlwaysBuyStrategy : IMarketStrategy
    {
        public string Name => "AlwaysBuy";
        public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId) =>
            new MarketTradeSignal
            {
                AssetId = assetId, Action = TradeAction.Buy, Quantity = 1.0,
                Confidence = 0.8, Reason = "test"
            };
        public void OnPriceUpdated(IMarketPriceSnapshot snapshot) { }
        public void Dispose() { }
    }

    #endregion

    #region E2E Integration

    [TestCase(TestName = "E2E: Strategy → Executor → Tracker → Alerts → Exporter → Visualizer")]
    public async Task EndToEndPipeline()
    {
        // ═══ 1. Настраиваем инфраструктуру ═══
        using var priceStream = new TestPriceStream();
        using var restClient = new FakeRestClient();
        using var tracker = new MarketPortfolioTracker(priceStream);
        using var alerts = new MarketAlertSystem(priceStream, tracker);
        var exporter = new MarketDataExporter();
        var visualizer = new MarketVisualizer { Width = 30, Height = 5 };

        // ═══ 2. Настраиваем стратегию + исполнитель ═══
        var strategy = new AlwaysBuyStrategy();
        var executor = new MarketOrderExecutor(priceStream, restClient)
        {
            DryRun = false,
            MinConfidence = 0.0,
            OrderCooldown = TimeSpan.Zero
        };
        executor.AddStrategy(strategy, ["BTCUSDT", "ETHUSDT"]);

        // ═══ 3. Настраиваем алерты ═══
        var btcAlert = new AlertDefinition
        {
            Id = "btc-profit",
            Condition = AlertCondition.PnLThreshold,
            Direction = AlertDirection.Above,
            Threshold = 1_000,
            AssetId = "BTCUSDT"
        };
        alerts.AddAlert(btcAlert);

        var portfolioAlert = new AlertDefinition
        {
            Id = "portfolio-pnl",
            Condition = AlertCondition.PortfolioPnLThreshold,
            Direction = AlertDirection.Above,
            Threshold = 5_000
        };
        alerts.AddAlert(portfolioAlert);

        // ═══ 4. Симулируем рыночные данные + исполнение ═══
        priceStream.SetPrice("BTCUSDT", 50_000);
        priceStream.SetPrice("ETHUSDT", 3_000);

        // Executor оценит стратегию → создаст ордера
        await executor.EvaluateOnceAsync();

        // Записываем ордера в трекер (имитируем callback)
        foreach (var (assetId, side, qty) in restClient.Orders)
        {
            var price = assetId == "BTCUSDT" ? 50_000.0 : 3_000.0;
            tracker.RecordTrade(assetId, side, qty, price, fee: price * qty * 0.001);
        }

        // ═══ 5. Цена растёт ═══
        priceStream.SetPrice("BTCUSDT", 55_000);
        priceStream.SetPrice("ETHUSDT", 3_500);

        // Проверяем алерты
        alerts.CheckAlerts();

        // ═══ 6. Проверяем состояние портфеля ═══
        var summary = tracker.GetSummary();

        using var scope = Assert.EnterMultipleScope();

        // Ордера были созданы
        Assert.That(restClient.Orders, Has.Count.GreaterThan(0));

        // Позиции существуют
        Assert.That(tracker.OpenPositionCount, Is.GreaterThan(0));

        // BTC позиция: unrealized PnL = (55000 - 50000) * 1.0 = 5000
        var btcPos = tracker.GetPosition("BTCUSDT");
        Assert.That(btcPos, Is.Not.Null);
        Assert.That(btcPos!.UnrealizedPnL, Is.EqualTo(5_000).Within(0.01));

        // BTC алерт: unrealized PnL 5000 > threshold 1000 → сработал
        Assert.That(btcAlert.HasTriggered, Is.True);

        // Сводка портфеля корректна
        Assert.That(summary.OpenPositions, Is.GreaterThan(0));
        Assert.That(summary.TotalUnrealizedPnL, Is.GreaterThan(0));

        // ═══ 7. Экспорт данных ═══
        var positions = new[]
        {
            tracker.GetPosition("BTCUSDT")!,
            tracker.GetPosition("ETHUSDT")!
        };

        var csv = exporter.ExportPositions(positions, ExportFormat.Csv);
        Assert.That(csv, Does.Contain("BTCUSDT"));
        Assert.That(csv, Does.Contain("ETHUSDT"));

        var json = exporter.ExportPositions(positions, ExportFormat.Json);
        Assert.That(json, Does.Contain("\"assetId\": \"BTCUSDT\""));

        // ═══ 8. Визуализация ═══
        var tableOutput = visualizer.RenderPositionsTable(positions);
        Assert.That(tableOutput, Does.Contain("BTCUSDT"));
        Assert.That(tableOutput, Does.Contain("Open"));

        var summaryOutput = visualizer.RenderPortfolioSummary(summary);
        Assert.That(summaryOutput, Does.Contain("Portfolio Summary"));
        Assert.That(summaryOutput, Does.Contain("Net P&L"));
    }

    [TestCase(TestName = "PortfolioTracker: закрытая позиция не даёт NaN в UnrealizedPnLPercent")]
    public void TrackerClosedPositionHasZeroUnrealizedPercent()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);

        tracker.RecordTrade("BTC", TradeSide.Buy, 1.0, 50_000);
        tracker.RecordTrade("BTC", TradeSide.Sell, 1.0, 55_000);

        var position = tracker.GetPosition("BTC");
        Assert.That(position!.UnrealizedPnLPercent, Is.EqualTo(0));
    }

    [TestCase(TestName = "E2E: Backtest → Visualizer → Exporter")]
    public void EndToEndBacktest()
    {
        // ═══ 1. Бэктест стратегии ═══
        var backtester = new MarketBacktester { InitialBalance = 10_000, FeeRateBps = 10 };
        using var strategy = new MomentumStrategy { WindowSize = 5, BuyThresholdPercent = 0.01 };

        var prices = new MarketBacktester.PricePoint[30];
        var baseTime = DateTimeOffset.UtcNow.AddDays(-30);
        for (int i = 0; i < 30; i++)
        {
            prices[i] = new MarketBacktester.PricePoint
            {
                Midpoint = 100 + i * 2.0 + Math.Sin(i) * 5,
                Timestamp = baseTime.AddDays(i)
            };
        }

        var result = backtester.Run(strategy, "BTCUSDT", prices);

        using var scope = Assert.EnterMultipleScope();

        // Бэктест вернул осмысленные данные
        Assert.That(result.StrategyName, Is.EqualTo("Momentum"));
        Assert.That(result.EquityCurve, Has.Length.EqualTo(30));
        Assert.That(result.FinalBalance, Is.EqualTo(result.InitialBalance + result.NetPnL).Within(0.01));

        // ═══ 2. Визуализация ═══
        var viz = new MarketVisualizer { Width = 30, Height = 5 };

        var equityChart = viz.RenderEquityCurve(result);
        Assert.That(equityChart, Does.Contain("Equity Curve"));
        Assert.That(equityChart, Does.Contain("Momentum"));

        var summaryText = viz.RenderBacktestSummary(result);
        Assert.That(summaryText, Does.Contain("Momentum"));
        Assert.That(summaryText, Does.Contain("Win Rate"));
        Assert.That(summaryText, Does.Contain("Sharpe"));
    }

    [TestCase(TestName = "E2E: RiskManager + Tracker + Alerts")]
    public async Task EndToEndRiskManagement()
    {
        using var stream = new TestPriceStream();
        using var tracker = new MarketPortfolioTracker(stream);
        using var riskManager = new MarketRiskManager(stream, limits: new PortfolioLimits { MaxDailyLoss = 2_000 });
        using var alerts = new MarketAlertSystem(stream, tracker);

        // Добавляем правило Stop-Loss
        var rule = new RiskRule { AssetId = "BTCUSDT", StopLossPrice = 48_000, TakeProfitPrice = 55_000 };
        riskManager.AddRule(rule);

        // Алерт на убыток портфеля
        alerts.AddAlert(new AlertDefinition
        {
            Id = "loss-alert",
            Condition = AlertCondition.PortfolioPnLThreshold,
            Direction = AlertDirection.Below,
            Threshold = -1_000
        });

        // Начальная позиция
        stream.SetPrice("BTCUSDT", 50_000);
        tracker.RecordTrade("BTCUSDT", TradeSide.Buy, 1.0, 50_000);

        // ═══ Цена падает до SL ═══
        stream.SetPrice("BTCUSDT", 47_000);

        string? triggeredReason = null;
        riskManager.OnRuleTriggered += (r, reason) => triggeredReason = reason;

        await riskManager.CheckAllRulesAsync();
        alerts.CheckAlerts();

        using var scope = Assert.EnterMultipleScope();

        // Stop-Loss сработал
        Assert.That(rule.IsTriggered, Is.True);
        Assert.That(triggeredReason, Does.Contain("Stop-Loss"));

        // Позиция показывает убыток
        var pos = tracker.GetPosition("BTCUSDT");
        Assert.That(pos!.UnrealizedPnL, Is.LessThan(0));

        // Портфельный алерт сработал (PnL -3000 < threshold -1000)
        var lossAlert = alerts.GetAlert("loss-alert");
        Assert.That(lossAlert!.HasTriggered, Is.True);
    }

    #endregion
}
