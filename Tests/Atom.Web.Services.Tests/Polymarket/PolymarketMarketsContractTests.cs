using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для универсальных контрактов Markets/ и их реализации через Polymarket.
/// Проверяют, что все Polymarket-классы корректно реализуют Markets/ интерфейсы.
/// </summary>
public class PolymarketMarketsContractTests(ILogger logger) : BenchmarkTests<PolymarketMarketsContractTests>(logger)
{
    public PolymarketMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPosition

    [TestCase(TestName = "IMarketPosition: PolymarketPosition кастится к интерфейсу")]
    public void PositionImplementsInterface()
    {
        var pos = new PolymarketPosition
        {
            AssetId = "token-1",
            Quantity = 100,
            AverageCostBasis = 0.55,
            CurrentPrice = 0.72,
            RealizedPnL = 5.0,
            TotalFees = 0.5,
            TradeCount = 3
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.AssetId, Is.EqualTo("token-1"));
        Assert.That(ipos.Quantity, Is.EqualTo(100));
        Assert.That(ipos.AverageCostBasis, Is.EqualTo(0.55));
        Assert.That(ipos.CurrentPrice, Is.EqualTo(0.72));
        Assert.That(ipos.MarketValue, Is.EqualTo(100 * 0.72));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(100 * 0.72 - 100 * 0.55));
        Assert.That(ipos.RealizedPnL, Is.EqualTo(5.0));
        Assert.That(ipos.TotalFees, Is.EqualTo(0.5));
        Assert.That(ipos.TradeCount, Is.EqualTo(3));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new PolymarketPosition { AssetId = "t1", Quantity = 0 };
        IMarketPosition ipos = pos;

        Assert.That(ipos.IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "IMarketPortfolioSummary: PolymarketPortfolioSummary кастится к интерфейсу")]
    public void PortfolioSummaryImplementsInterface()
    {
        var summary = new PolymarketPortfolioSummary
        {
            OpenPositions = 5,
            ClosedPositions = 2,
            TotalMarketValue = 1000,
            TotalCostBasis = 800,
            TotalUnrealizedPnL = 200,
            TotalRealizedPnL = 50,
            TotalFees = 10
        };

        IMarketPortfolioSummary isum = summary;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isum.OpenPositions, Is.EqualTo(5));
        Assert.That(isum.ClosedPositions, Is.EqualTo(2));
        Assert.That(isum.TotalMarketValue, Is.EqualTo(1000));
        Assert.That(isum.TotalCostBasis, Is.EqualTo(800));
        Assert.That(isum.TotalUnrealizedPnL, Is.EqualTo(200));
        Assert.That(isum.TotalRealizedPnL, Is.EqualTo(50));
        Assert.That(isum.TotalFees, Is.EqualTo(10));
        Assert.That(isum.NetPnL, Is.EqualTo(200 + 50 - 10));
    }

    #endregion

    #region Модели — IMarketPriceSnapshot (строки → double)

    [TestCase(TestName = "IMarketPriceSnapshot: парсинг string → double? через явную реализацию")]
    public void PriceSnapshotExplicitImplementation()
    {
        var snap = new PolymarketPriceSnapshot
        {
            AssetId = "token-abc",
            BestBid = "0.55",
            BestAsk = "0.58",
            Midpoint = "0.565",
            LastTradePrice = "0.56",
            LastUpdateTicks = 12345678
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("token-abc"));
        Assert.That(isnap.BestBid, Is.EqualTo(0.55).Within(0.001));
        Assert.That(isnap.BestAsk, Is.EqualTo(0.58).Within(0.001));
        Assert.That(isnap.Midpoint, Is.EqualTo(0.565).Within(0.001));
        Assert.That(isnap.LastTradePrice, Is.EqualTo(0.56).Within(0.001));
        Assert.That(isnap.LastUpdateTicks, Is.EqualTo(12345678));
    }

    [TestCase(TestName = "IMarketPriceSnapshot: null строки → null double")]
    public void PriceSnapshotNullValues()
    {
        var snap = new PolymarketPriceSnapshot { AssetId = "t1" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.Midpoint, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    [TestCase(TestName = "IMarketPriceSnapshot: невалидная строка → null")]
    public void PriceSnapshotInvalidString()
    {
        var snap = new PolymarketPriceSnapshot
        {
            AssetId = "t1",
            BestBid = "not-a-number",
            BestAsk = ""
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
    }

    #endregion

    #region Модели — IMarketTradeSignal (конверсия enum)

    [TestCase(TestName = "IMarketTradeSignal: конверсия PolymarketTradeAction → TradeAction")]
    public void TradeSignalEnumConversion()
    {
        var signal = new PolymarketTradeSignal
        {
            AssetId = "t1",
            Action = PolymarketTradeAction.Buy,
            Quantity = 50,
            Price = "0.65",
            Confidence = 0.85,
            Reason = "тест"
        };

        IMarketTradeSignal isig = signal;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isig.AssetId, Is.EqualTo("t1"));
        Assert.That(isig.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(isig.Quantity, Is.EqualTo(50));
        Assert.That(isig.Price, Is.EqualTo("0.65"));
        Assert.That(isig.Confidence, Is.EqualTo(0.85));
        Assert.That(isig.Reason, Is.EqualTo("тест"));
    }

    [TestCase(TestName = "IMarketTradeSignal: Hold = 0, Buy = 1, Sell = 2")]
    public void TradeSignalAllActions()
    {
        using var scope = Assert.EnterMultipleScope();

        var hold = new PolymarketTradeSignal { AssetId = "t1", Action = PolymarketTradeAction.Hold };
        Assert.That(((IMarketTradeSignal)hold).Action, Is.EqualTo(TradeAction.Hold));

        var buy = new PolymarketTradeSignal { AssetId = "t1", Action = PolymarketTradeAction.Buy };
        Assert.That(((IMarketTradeSignal)buy).Action, Is.EqualTo(TradeAction.Buy));

        var sell = new PolymarketTradeSignal { AssetId = "t1", Action = PolymarketTradeAction.Sell };
        Assert.That(((IMarketTradeSignal)sell).Action, Is.EqualTo(TradeAction.Sell));
    }

    #endregion

    #region Модели — IMarketAlertDefinition (конверсия enum)

    [TestCase(TestName = "IMarketAlertDefinition: конверсия Polymarket → Markets enum'ов")]
    public void AlertDefinitionEnumConversion()
    {
        var alert = new PolymarketAlertDefinition
        {
            Id = "alert-1",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Above,
            Threshold = 100,
            AssetId = "t1",
            Description = "P&L > 100",
            OneShot = true
        };

        IMarketAlertDefinition ia = alert;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ia.Id, Is.EqualTo("alert-1"));
        Assert.That(ia.Condition, Is.EqualTo(AlertCondition.PnLThreshold));
        Assert.That(ia.Direction, Is.EqualTo(AlertDirection.Above));
        Assert.That(ia.Threshold, Is.EqualTo(100));
        Assert.That(ia.AssetId, Is.EqualTo("t1"));
        Assert.That(ia.Description, Is.EqualTo("P&L > 100"));
        Assert.That(ia.OneShot, Is.True);
        Assert.That(ia.IsEnabled, Is.True);
        Assert.That(ia.HasTriggered, Is.False);
    }

    [TestCase(TestName = "IMarketAlertDefinition: все условия конвертируются корректно")]
    public void AlertConditionAllValues()
    {
        using var scope = Assert.EnterMultipleScope();

        Assert.That(GetCondition(PolymarketAlertCondition.PnLThreshold), Is.EqualTo(AlertCondition.PnLThreshold));
        Assert.That(GetCondition(PolymarketAlertCondition.PriceThreshold), Is.EqualTo(AlertCondition.PriceThreshold));
        Assert.That(GetCondition(PolymarketAlertCondition.PortfolioPnLThreshold), Is.EqualTo(AlertCondition.PortfolioPnLThreshold));
        Assert.That(GetCondition(PolymarketAlertCondition.MarketClosed), Is.EqualTo(AlertCondition.MarketClosed));
        Assert.That(GetCondition(PolymarketAlertCondition.MarketResolved), Is.EqualTo(AlertCondition.MarketResolved));
    }

    [TestCase(TestName = "IMarketAlertDefinition: оба направления конвертируются")]
    public void AlertDirectionAllValues()
    {
        using var scope = Assert.EnterMultipleScope();

        var above = new PolymarketAlertDefinition { Id = "a", Direction = PolymarketAlertDirection.Above };
        Assert.That(((IMarketAlertDefinition)above).Direction, Is.EqualTo(AlertDirection.Above));

        var below = new PolymarketAlertDefinition { Id = "b", Direction = PolymarketAlertDirection.Below };
        Assert.That(((IMarketAlertDefinition)below).Direction, Is.EqualTo(AlertDirection.Below));
    }

    private static AlertCondition GetCondition(PolymarketAlertCondition pc) =>
        ((IMarketAlertDefinition)new PolymarketAlertDefinition { Id = "x", Condition = pc }).Condition;

    #endregion

    #region Модели — IMarketRiskRule

    [TestCase(TestName = "IMarketRiskRule: PolymarketRiskRule реализует интерфейс")]
    public void RiskRuleImplementsInterface()
    {
        var rule = new PolymarketRiskRule
        {
            AssetId = "t1",
            StopLossPrice = 0.20,
            TakeProfitPrice = 0.90,
            TrailingStopPercent = 5.0,
            MaxLossPerPosition = 100
        };

        IMarketRiskRule irule = rule;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(irule.AssetId, Is.EqualTo("t1"));
        Assert.That(irule.StopLossPrice, Is.EqualTo(0.20));
        Assert.That(irule.TakeProfitPrice, Is.EqualTo(0.90));
        Assert.That(irule.TrailingStopPercent, Is.EqualTo(5.0));
        Assert.That(irule.MaxLossPerPosition, Is.EqualTo(100));
        Assert.That(irule.IsTriggered, Is.False);
    }

    #endregion

    #region Модели — IMarketPortfolioLimits

    [TestCase(TestName = "IMarketPortfolioLimits: PolymarketPortfolioLimits реализует интерфейс")]
    public void PortfolioLimitsImplementsInterface()
    {
        var limits = new PolymarketPortfolioLimits
        {
            MaxPositionSize = 5000,
            MaxOpenPositions = 10,
            MaxPortfolioLoss = -500,
            MaxPositionPercent = 20,
            MaxDailyLoss = -200
        };

        IMarketPortfolioLimits il = limits;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(il.MaxPositionSize, Is.EqualTo(5000));
        Assert.That(il.MaxOpenPositions, Is.EqualTo(10));
        Assert.That(il.MaxPortfolioLoss, Is.EqualTo(-500));
        Assert.That(il.MaxPositionPercent, Is.EqualTo(20));
        Assert.That(il.MaxDailyLoss, Is.EqualTo(-200));
    }

    #endregion

    #region Модели — IMarketPnLSnapshot

    [TestCase(TestName = "IMarketPnLSnapshot: PolymarketPnLSnapshot реализует интерфейс")]
    public void PnLSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var snap = new PolymarketPnLSnapshot
        {
            Timestamp = ts,
            TotalMarketValue = 10000,
            UnrealizedPnL = 500,
            RealizedPnL = 200,
            TotalFees = 30
        };

        IMarketPnLSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.Timestamp, Is.EqualTo(ts));
        Assert.That(isnap.TotalMarketValue, Is.EqualTo(10000));
        Assert.That(isnap.UnrealizedPnL, Is.EqualTo(500));
        Assert.That(isnap.RealizedPnL, Is.EqualTo(200));
        Assert.That(isnap.TotalFees, Is.EqualTo(30));
        Assert.That(isnap.NetPnL, Is.EqualTo(500 + 200 - 30));
    }

    #endregion

    #region Модели — IMarketBacktestResult

    [TestCase(TestName = "IMarketBacktestResult: PolymarketBacktestResult реализует интерфейс")]
    public void BacktestResultImplementsInterface()
    {
        var result = new PolymarketBacktestResult
        {
            StrategyName = "TestStrategy",
            InitialBalance = 10000,
            FinalBalance = 12000,
            TotalTrades = 50,
            WinRate = 60,
            SharpeRatio = 1.5,
            MaxDrawdownPercent = 8.3,
            ProfitFactor = 2.1,
            EquityCurve = [10000, 10500, 11000, 12000]
        };

        IMarketBacktestResult ir = result;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ir.StrategyName, Is.EqualTo("TestStrategy"));
        Assert.That(ir.InitialBalance, Is.EqualTo(10000));
        Assert.That(ir.FinalBalance, Is.EqualTo(12000));
        Assert.That(ir.NetPnL, Is.EqualTo(2000));
        Assert.That(ir.ReturnPercent, Is.EqualTo(20));
        Assert.That(ir.TotalTrades, Is.EqualTo(50));
        Assert.That(ir.WinRate, Is.EqualTo(60));
        Assert.That(ir.SharpeRatio, Is.EqualTo(1.5));
        Assert.That(ir.MaxDrawdownPercent, Is.EqualTo(8.3));
        Assert.That(ir.ProfitFactor, Is.EqualTo(2.1));
        Assert.That(ir.EquityCurve, Has.Length.EqualTo(4));
    }

    #endregion

    #region Модели — IMarketPricePoint

    [TestCase(TestName = "IMarketPricePoint: PolymarketPricePoint реализует интерфейс")]
    public void PricePointImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var pp = new PolymarketPricePoint
        {
            Midpoint = 0.55,
            BestBid = 0.54,
            BestAsk = 0.56,
            Timestamp = ts
        };

        IMarketPricePoint ipp = pp;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipp.Midpoint, Is.EqualTo(0.55));
        Assert.That(ipp.BestBid, Is.EqualTo(0.54));
        Assert.That(ipp.BestAsk, Is.EqualTo(0.56));
        Assert.That(ipp.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Сервисы — IMarketAlertSystem

    [TestCase(TestName = "IMarketAlertSystem: добавление/удаление алертов через интерфейс")]
    public void AlertSystemInterfaceOperations()
    {
        using var system = new PolymarketAlertSystem();
        IMarketAlertSystem ias = system;

        var alert = new PolymarketAlertDefinition
        {
            Id = "a1",
            Condition = PolymarketAlertCondition.PriceThreshold,
            Direction = PolymarketAlertDirection.Above,
            Threshold = 0.80
        };

        ias.AddAlert(alert);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ias.ActiveCount, Is.EqualTo(1));
        Assert.That(ias.GetAlert("a1"), Is.Not.Null);
        Assert.That(ias.GetAlert("a1")!.Threshold, Is.EqualTo(0.80));
        Assert.That(ias.GetAlert("nonexistent"), Is.Null);

        ias.RemoveAlert("a1");
        Assert.That(ias.ActiveCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "IMarketAlertSystem: ClearAlerts очищает всё")]
    public void AlertSystemClearAlerts()
    {
        using var system = new PolymarketAlertSystem();
        IMarketAlertSystem ias = system;

        ias.AddAlert(new PolymarketAlertDefinition { Id = "a1" });
        ias.AddAlert(new PolymarketAlertDefinition { Id = "a2" });
        ias.AddAlert(new PolymarketAlertDefinition { Id = "a3" });

        ias.ClearAlerts();
        Assert.That(ias.ActiveCount, Is.EqualTo(0));
    }

    #endregion

    #region Сервисы — IMarketRiskManager

    [TestCase(TestName = "IMarketRiskManager: операции через интерфейс")]
    public void RiskManagerInterfaceOperations()
    {
        using var rm = new PolymarketRiskManager();
        IMarketRiskManager irm = rm;

        irm.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.30 });

        using var scope = Assert.EnterMultipleScope();
        Assert.That(irm.GetRule("t1"), Is.Not.Null);
        Assert.That(irm.GetRule("t1")!.StopLossPrice, Is.EqualTo(0.30));
        Assert.That(irm.GetRule("t2"), Is.Null);

        irm.RemoveRule("t1");
        Assert.That(irm.GetRule("t1"), Is.Null);
    }

    [TestCase(TestName = "IMarketRiskManager: Limits доступны через интерфейс")]
    public void RiskManagerLimitsInterface()
    {
        using var rm = new PolymarketRiskManager();
        rm.Limits.MaxPositionSize = 999;
        rm.Limits.MaxOpenPositions = 5;

        IMarketRiskManager irm = rm;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(irm.Limits.MaxPositionSize, Is.EqualTo(999));
        Assert.That(irm.Limits.MaxOpenPositions, Is.EqualTo(5));
    }

    [TestCase(TestName = "IMarketRiskManager: DailyLoss и ResetDailyLoss")]
    public void RiskManagerDailyLoss()
    {
        using var rm = new PolymarketRiskManager();
        IMarketRiskManager irm = rm;

        Assert.That(irm.DailyLoss, Is.EqualTo(0));
        irm.ResetDailyLoss();
        Assert.That(irm.DailyLoss, Is.EqualTo(0));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "IMarketPriceStream: GetPrice → IMarketPriceSnapshot")]
    public void PriceStreamInterfaceGetPrice()
    {
        using var stream = new PolymarketPriceStream();
        IMarketPriceStream ips = stream;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ips.TokenCount, Is.EqualTo(0));
        Assert.That(ips.GetPrice("nonexistent"), Is.Null);
    }

    [TestCase(TestName = "IMarketPriceStream: ClearCache через интерфейс")]
    public void PriceStreamClearCache()
    {
        using var stream = new PolymarketPriceStream();
        IMarketPriceStream ips = stream;

        ips.ClearCache();
        Assert.That(ips.TokenCount, Is.EqualTo(0));
    }

    #endregion

    #region Сервисы — IMarketPortfolioTracker

    [TestCase(TestName = "IMarketPortfolioTracker: GetPosition/GetSummary через интерфейс")]
    public void PortfolioTrackerInterface()
    {
        using var stream = new PolymarketPriceStream();
        using var tracker = new PolymarketPortfolioTracker(stream);
        IMarketPortfolioTracker ipt = tracker;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipt.OpenPositionCount, Is.EqualTo(0));
        Assert.That(ipt.GetPosition("nonexistent"), Is.Null);

        var summary = ipt.GetSummary();
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.OpenPositions, Is.EqualTo(0));
        Assert.That(summary.TotalMarketValue, Is.EqualTo(0));
    }

    [TestCase(TestName = "IMarketPortfolioTracker: ClearPositions через интерфейс")]
    public void PortfolioTrackerClearPositions()
    {
        using var stream = new PolymarketPriceStream();
        using var tracker = new PolymarketPortfolioTracker(stream);
        IMarketPortfolioTracker ipt = tracker;

        ipt.ClearPositions();
        Assert.That(ipt.OpenPositionCount, Is.EqualTo(0));
    }

    #endregion

    #region Сервисы — IMarketPnLHistory

    [TestCase(TestName = "IMarketPnLHistory: Latest и TakeSnapshot через интерфейс")]
    public void PnLHistoryInterface()
    {
        using var stream = new PolymarketPriceStream();
        using var tracker = new PolymarketPortfolioTracker(stream);
        using var history = new PolymarketPnLHistory(tracker);
        IMarketPnLHistory ih = history;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ih.Count, Is.EqualTo(0));
        Assert.That(ih.Latest, Is.Null);
        Assert.That(ih.IsRecording, Is.False);

        var snap = ih.TakeSnapshot();
        Assert.That(snap, Is.Not.Null);
        Assert.That(snap, Is.InstanceOf<IMarketPnLSnapshot>());
        Assert.That(ih.Count, Is.EqualTo(1));
        Assert.That(ih.Latest, Is.Not.Null);
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "IMarketClient: PlatformName = 'Polymarket'")]
    public void ClientInterfacePlatformName()
    {
        using var client = new PolymarketClient();
        IMarketClient ic = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ic.PlatformName, Is.EqualTo("Polymarket"));
        Assert.That(ic.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "IMarketRestClient: BaseUrl доступен через интерфейс")]
    public void RestClientInterfaceBaseUrl()
    {
        using var client = new PolymarketRestClient();
        IMarketRestClient irc = client;

        Assert.That(irc.BaseUrl, Is.Not.Null.And.Not.Empty);
        Assert.That(irc.BaseUrl, Does.Contain("polymarket"));
    }

    #endregion

    #region Сервисы — IMarketVisualizer

    [TestCase(TestName = "IMarketVisualizer: RenderChart через интерфейс")]
    public void VisualizerInterfaceRenderChart()
    {
        var viz = new PolymarketVisualizer();
        IMarketVisualizer iv = viz;

        iv.Width = 40;
        iv.Height = 10;

        var chart = iv.RenderChart([1.0, 2.0, 3.0, 2.5, 4.0], "Тест");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Is.Not.Null.And.Not.Empty);
        Assert.That(chart, Does.Contain("Тест"));
    }

    [TestCase(TestName = "IMarketVisualizer: RenderPositionsTable через интерфейс")]
    public void VisualizerInterfacePositionsTable()
    {
        var viz = new PolymarketVisualizer();
        IMarketVisualizer iv = viz;

        IEnumerable<IMarketPosition> positions = new PolymarketPosition[]
        {
            new() { AssetId = "t1", Quantity = 100, AverageCostBasis = 0.5, CurrentPrice = 0.6 },
            new() { AssetId = "t2", Quantity = 50, AverageCostBasis = 0.3, CurrentPrice = 0.4 }
        };

        var table = iv.RenderPositionsTable(positions);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(table, Is.Not.Null.And.Not.Empty);
        Assert.That(table, Does.Contain("t1"));
        Assert.That(table, Does.Contain("t2"));
    }

    [TestCase(TestName = "IMarketVisualizer: RenderPortfolioSummary через интерфейс")]
    public void VisualizerInterfacePortfolioSummary()
    {
        var viz = new PolymarketVisualizer();
        IMarketVisualizer iv = viz;

        IMarketPortfolioSummary summary = new PolymarketPortfolioSummary
        {
            OpenPositions = 3,
            ClosedPositions = 1,
            TotalMarketValue = 5000,
            TotalCostBasis = 4000,
            TotalUnrealizedPnL = 1000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        var text = iv.RenderPortfolioSummary(summary);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(text, Is.Not.Null.And.Not.Empty);
        Assert.That(text, Does.Contain("портфеля"));
    }

    #endregion

    #region Сервисы — IMarketDataExporter

    [TestCase(TestName = "IMarketDataExporter: ExportPositions CSV через интерфейс")]
    public void DataExporterInterfacePositionsCsv()
    {
        var exporter = new PolymarketDataExporter();
        IMarketDataExporter ide = exporter;

        IEnumerable<IMarketPosition> positions = new PolymarketPosition[]
        {
            new() { AssetId = "t1", Quantity = 100, AverageCostBasis = 0.50, CurrentPrice = 0.60 }
        };

        var csv = ide.ExportPositions(positions, ExportFormat.Csv);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Is.Not.Null.And.Not.Empty);
        Assert.That(csv, Does.Contain("AssetId"));
        Assert.That(csv, Does.Contain("t1"));
    }

    [TestCase(TestName = "IMarketDataExporter: ExportPositions JSON через интерфейс")]
    public void DataExporterInterfacePositionsJson()
    {
        var exporter = new PolymarketDataExporter();
        IMarketDataExporter ide = exporter;

        IEnumerable<IMarketPosition> positions = new PolymarketPosition[]
        {
            new() { AssetId = "t1", Quantity = 100, AverageCostBasis = 0.50, CurrentPrice = 0.60 }
        };

        var json = ide.ExportPositions(positions, ExportFormat.Json);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Is.Not.Null.And.Not.Empty);
        Assert.That(json, Does.Contain("t1"));
    }

    [TestCase(TestName = "IMarketDataExporter: ExportPnLHistory CSV через интерфейс")]
    public void DataExporterInterfacePnLCsv()
    {
        var exporter = new PolymarketDataExporter();
        IMarketDataExporter ide = exporter;

        IEnumerable<IMarketPnLSnapshot> snapshots = new PolymarketPnLSnapshot[]
        {
            new() { Timestamp = DateTimeOffset.UtcNow, TotalMarketValue = 1000, UnrealizedPnL = 50, RealizedPnL = 20, TotalFees = 5 }
        };

        var csv = ide.ExportPnLHistory(snapshots, ExportFormat.Csv);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Is.Not.Null.And.Not.Empty);
        Assert.That(csv, Does.Contain("Timestamp"));
    }

    #endregion

    #region Сервисы — IMarketBacktester

    [TestCase(TestName = "IMarketBacktester: InitialBalance и FeeRateBps через интерфейс")]
    public void BacktesterInterfaceProperties()
    {
        var bt = new PolymarketBacktester { InitialBalance = 5000, FeeRateBps = 10 };
        IMarketBacktester ibt = bt;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibt.InitialBalance, Is.EqualTo(5000));
        Assert.That(ibt.FeeRateBps, Is.EqualTo(10));
    }

    #endregion

    #region Исключения — MarketException

    [TestCase(TestName = "MarketException: PolymarketException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new PolymarketException("тест");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex, Is.InstanceOf<Exception>());
        Assert.That(ex.Message, Is.EqualTo("тест"));
    }

    [TestCase(TestName = "MarketException: inner exception передаётся")]
    public void ExceptionInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new PolymarketException("outer", inner);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    #endregion

    #region Enum-ы Markets — базовые проверки

    [TestCase(TestName = "Markets Enums: TradeSide Buy/Sell")]
    public void TradeSideValues()
    {
        using var scope = Assert.EnterMultipleScope();
        Assert.That((byte)TradeSide.Buy, Is.EqualTo(0));
        Assert.That((byte)TradeSide.Sell, Is.EqualTo(1));
    }

    [TestCase(TestName = "Markets Enums: TradeAction Hold/Buy/Sell")]
    public void TradeActionValues()
    {
        using var scope = Assert.EnterMultipleScope();
        Assert.That((byte)TradeAction.Hold, Is.EqualTo(0));
        Assert.That((byte)TradeAction.Buy, Is.EqualTo(1));
        Assert.That((byte)TradeAction.Sell, Is.EqualTo(2));
    }

    [TestCase(TestName = "Markets Enums: ExportFormat Csv/Json")]
    public void ExportFormatValues()
    {
        using var scope = Assert.EnterMultipleScope();
        Assert.That((byte)ExportFormat.Csv, Is.EqualTo(0));
        Assert.That((byte)ExportFormat.Json, Is.EqualTo(1));
    }

    [TestCase(TestName = "Markets Enums: MarketOrderStatus все значения")]
    public void MarketOrderStatusValues()
    {
        using var scope = Assert.EnterMultipleScope();
        Assert.That(Enum.GetValues<MarketOrderStatus>(), Has.Length.EqualTo(5));
        Assert.That((byte)MarketOrderStatus.Open, Is.EqualTo(0));
        Assert.That((byte)MarketOrderStatus.Rejected, Is.EqualTo(4));
    }

    [TestCase(TestName = "Markets Enums: MarketStatus все значения")]
    public void MarketStatusValues()
    {
        using var scope = Assert.EnterMultipleScope();
        Assert.That(Enum.GetValues<MarketStatus>(), Has.Length.EqualTo(5));
    }

    #endregion

    #region IMarketOrderBookSnapshot

    [TestCase(TestName = "IMarketOrderBookSnapshot: PolymarketOrderBook реализует интерфейс")]
    public void OrderBookImplementsInterface()
    {
        var book = new PolymarketOrderBook
        {
            AssetId = "token-xyz",
            Timestamp = "1700000000",
            Bids = [new PolymarketBookEntry { Price = "0.55", Size = "100" }],
            Asks = [new PolymarketBookEntry { Price = "0.58", Size = "200" }]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("token-xyz"));
        Assert.That(ibook.Timestamp.Year, Is.EqualTo(2023));
    }

    [TestCase(TestName = "IMarketOrderBookSnapshot: null AssetId → пустая строка")]
    public void OrderBookNullAssetId()
    {
        var book = new PolymarketOrderBook();
        IMarketOrderBookSnapshot ibook = book;

        Assert.That(ibook.AssetId, Is.EqualTo(string.Empty));
    }

    [TestCase(TestName = "IMarketOrderBookSnapshot: невалидный Timestamp → MinValue")]
    public void OrderBookInvalidTimestamp()
    {
        var book = new PolymarketOrderBook { Timestamp = "not-a-number" };
        IMarketOrderBookSnapshot ibook = book;

        Assert.That(ibook.Timestamp, Is.EqualTo(DateTimeOffset.MinValue));
    }

    #endregion
}
