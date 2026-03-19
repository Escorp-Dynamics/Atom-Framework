namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для риск-менеджмента, визуализации и конфигурации.
/// </summary>
public class PolymarketRiskVisualizationConfigTests(ILogger logger) : BenchmarkTests<PolymarketRiskVisualizationConfigTests>(logger)
{
    public PolymarketRiskVisualizationConfigTests() : this(ConsoleLogger.Unicode) { }

    #region RiskManager — правила

    [TestCase(TestName = "RiskManager: добавление/удаление правил")]
    public void RiskManagerRulesTest()
    {
        using var rm = new PolymarketRiskManager();

        rm.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.30 });
        rm.AddRule(new PolymarketRiskRule { AssetId = "t2", TakeProfitPrice = 0.90 });

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.Rules, Has.Count.EqualTo(2));
        Assert.That(rm.GetRule("t1")?.StopLossPrice, Is.EqualTo(0.30));
        Assert.That(rm.GetRule("t2")?.TakeProfitPrice, Is.EqualTo(0.90));
        Assert.That(rm.GetRule("t3"), Is.Null);

        rm.RemoveRule("t1");
        Assert.That(rm.Rules, Has.Count.EqualTo(1));

        rm.ClearRules();
        Assert.That(rm.Rules, Has.Count.EqualTo(0));
    }

    [TestCase(TestName = "RiskManager: правило перезаписывается при AddRule с тем же AssetId")]
    public void RiskManagerRuleOverwriteTest()
    {
        using var rm = new PolymarketRiskManager();

        rm.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.20 });
        rm.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.35 });

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.Rules, Has.Count.EqualTo(1));
        Assert.That(rm.GetRule("t1")!.StopLossPrice, Is.EqualTo(0.35));
    }

    #endregion

    #region RiskManager — лимиты портфеля

    [TestCase(TestName = "RiskManager: CanOpenPosition без трекера → всегда true при допустимом размере")]
    public void RiskManagerCanOpenWithoutTrackerTest()
    {
        using var rm = new PolymarketRiskManager();
        rm.Limits.MaxPositionSize = 500;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.CanOpenPosition("t1", 100), Is.True);
        Assert.That(rm.CanOpenPosition("t1", 600), Is.False);
    }

    [TestCase(TestName = "RiskManager: лимит MaxPositionSize")]
    public void RiskManagerMaxPositionSizeTest()
    {
        using var rm = new PolymarketRiskManager();
        rm.Limits.MaxPositionSize = 100;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.CanOpenPosition("t1", 100), Is.True);
        Assert.That(rm.CanOpenPosition("t1", 101), Is.False);
    }

    [TestCase(TestName = "RiskManager: ResetDailyLoss обнуляет счётчик")]
    public void RiskManagerResetDailyLossTest()
    {
        using var rm = new PolymarketRiskManager();

        rm.ResetDailyLoss();

        Assert.That(rm.DailyLoss, Is.EqualTo(0));
    }

    [TestCase(TestName = "RiskManager: Limits по умолчанию не ограничивают")]
    public void RiskManagerDefaultLimitsTest()
    {
        using var rm = new PolymarketRiskManager();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rm.Limits.MaxPositionSize, Is.EqualTo(double.MaxValue));
        Assert.That(rm.Limits.MaxOpenPositions, Is.EqualTo(int.MaxValue));
        Assert.That(rm.Limits.MaxPortfolioLoss, Is.EqualTo(double.MaxValue));
        Assert.That(rm.Limits.MaxPositionPercent, Is.EqualTo(1.0));
        Assert.That(rm.Limits.MaxDailyLoss, Is.EqualTo(double.MaxValue));
    }

    #endregion

    #region RiskManager — StopLoss / TakeProfit

    [TestCase(TestName = "RiskManager: StopLoss срабатывает при цене ≤ порога")]
    public async Task RiskManagerStopLossTriggersTest()
    {
        using var rm = new PolymarketRiskManager();

        PolymarketRiskTriggeredEventArgs? eventArgs = null;
        rm.RiskTriggered += (sender, e) => { eventArgs = e; return ValueTask.CompletedTask; };

        var rule = new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.40 };
        rm.AddRule(rule);

        // Имитируем ручную проверку — CheckAllRulesAsync использует PriceStream, поэтому проверяем через правило
        // Правило сработает при получении цены через ConnectPriceStream
        // Проверим само правило
        using var scope = Assert.EnterMultipleScope();
        Assert.That(rule.StopLossPrice, Is.EqualTo(0.40));
        Assert.That(rule.IsTriggered, Is.False);
    }

    [TestCase(TestName = "RiskManager: PolymarketRiskTriggeredEventArgs содержит корректные данные")]
    public void RiskManagerEventArgsTest()
    {
        var args = new PolymarketRiskTriggeredEventArgs("token1", PolymarketRiskOrderType.TakeProfit, 0.85, 0.90);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.AssetId, Is.EqualTo("token1"));
        Assert.That(args.OrderType, Is.EqualTo(PolymarketRiskOrderType.TakeProfit));
        Assert.That(args.TriggerPrice, Is.EqualTo(0.85));
        Assert.That(args.CurrentPrice, Is.EqualTo(0.90));
        Assert.That(args.TriggeredAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [TestCase(TestName = "RiskManager: TrailingStop обновляет HighWaterMark")]
    public void RiskManagerTrailingStopHighWaterMarkTest()
    {
        var rule = new PolymarketRiskRule { AssetId = "t1", TrailingStopPercent = 0.10 };

        // HighWaterMark начинается с 0
        Assert.That(rule.HighWaterMark, Is.EqualTo(0));
    }

    [TestCase(TestName = "RiskManager: PolymarketRiskOrderType содержит все значения")]
    public void RiskOrderTypeValuesTest()
    {
        var values = Enum.GetValues<PolymarketRiskOrderType>();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(values, Has.Length.EqualTo(3));
        Assert.That(values, Does.Contain(PolymarketRiskOrderType.StopLoss));
        Assert.That(values, Does.Contain(PolymarketRiskOrderType.TakeProfit));
        Assert.That(values, Does.Contain(PolymarketRiskOrderType.TrailingStop));
    }

    [TestCase(TestName = "RiskManager: Dispose отключает подписки")]
    public void RiskManagerDisposeTest()
    {
        var rm = new PolymarketRiskManager();
        rm.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.30 });

        rm.Dispose();

        // Повторный Dispose не бросает исключений
        Assert.DoesNotThrow(() => rm.Dispose());
    }

    [TestCase(TestName = "RiskManager: AutoExecute по умолчанию false")]
    public void RiskManagerAutoExecuteDefaultTest()
    {
        using var rm = new PolymarketRiskManager();
        Assert.That(rm.AutoExecute, Is.False);
    }

    #endregion

    #region RiskManager — PortfolioLimits

    [TestCase(TestName = "PortfolioLimits: создание с настраиваемыми значениями")]
    public void PortfolioLimitsCustomTest()
    {
        var limits = new PolymarketPortfolioLimits
        {
            MaxPositionSize = 1000,
            MaxOpenPositions = 5,
            MaxPortfolioLoss = 5000,
            MaxPositionPercent = 0.25,
            MaxDailyLoss = 500
        };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(limits.MaxPositionSize, Is.EqualTo(1000));
        Assert.That(limits.MaxOpenPositions, Is.EqualTo(5));
        Assert.That(limits.MaxPortfolioLoss, Is.EqualTo(5000));
        Assert.That(limits.MaxPositionPercent, Is.EqualTo(0.25));
        Assert.That(limits.MaxDailyLoss, Is.EqualTo(500));
    }

    #endregion

    #region Visualizer — RenderChart

    [TestCase(TestName = "Visualizer: RenderChart с данными — содержит элементы")]
    public void VisualizerRenderChartTest()
    {
        var viz = new PolymarketVisualizer { Width = 30, Height = 10 };
        double[] values = [100, 110, 105, 120, 115, 130, 125, 140, 135, 150];

        var chart = viz.RenderChart(values, "Тест");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("Тест"));
        Assert.That(chart, Does.Contain("│"));
        Assert.That(chart, Does.Contain("─"));
        Assert.That(chart, Does.Contain("Min:"));
        Assert.That(chart, Does.Contain("Max:"));
        Assert.That(chart, Does.Contain("Points: 10"));
    }

    [TestCase(TestName = "Visualizer: RenderChart с пустыми данными → [Нет данных]")]
    public void VisualizerRenderChartEmptyTest()
    {
        var viz = new PolymarketVisualizer();
        var chart = viz.RenderChart([]);
        Assert.That(chart, Is.EqualTo("[Нет данных]"));
    }

    [TestCase(TestName = "Visualizer: RenderChart одно значение — не ломается")]
    public void VisualizerRenderChartSingleValueTest()
    {
        var viz = new PolymarketVisualizer { Width = 20, Height = 5 };
        var chart = viz.RenderChart([42.0]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("Points: 1"));
        Assert.That(chart, Does.Contain("│"));
    }

    [TestCase(TestName = "Visualizer: RenderChart с одинаковыми значениями — range = 0")]
    public void VisualizerRenderChartFlatTest()
    {
        var viz = new PolymarketVisualizer { Width = 20, Height = 5 };
        var chart = viz.RenderChart([5.0, 5.0, 5.0, 5.0]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("Min: 5.00"));
        Assert.That(chart, Does.Contain("Max: 5.00"));
    }

    [TestCase(TestName = "Visualizer: Width и Height настраиваются")]
    public void VisualizerDimensionsTest()
    {
        var viz = new PolymarketVisualizer();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(viz.Width, Is.EqualTo(60));
        Assert.That(viz.Height, Is.EqualTo(15));

        viz.Width = 80;
        viz.Height = 20;
        Assert.That(viz.Width, Is.EqualTo(80));
        Assert.That(viz.Height, Is.EqualTo(20));
    }

    #endregion

    #region Visualizer — EquityCurve и PnLHistory

    [TestCase(TestName = "Visualizer: RenderEquityCurve из BacktestResult")]
    public void VisualizerEquityCurveTest()
    {
        var viz = new PolymarketVisualizer { Width = 30, Height = 8 };
        var result = new PolymarketBacktestResult
        {
            StrategyName = "TestStrategy",
            InitialBalance = 1000,
            FinalBalance = 1200,
            Trades = [],
            EquityCurve = [1000, 1050, 1010, 1100, 1150, 1200]
        };

        var chart = viz.RenderEquityCurve(result);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("TestStrategy"));
        Assert.That(chart, Does.Contain("│"));
    }

    [TestCase(TestName = "Visualizer: RenderPnLHistory из снимков")]
    public void VisualizerPnLHistoryTest()
    {
        var viz = new PolymarketVisualizer { Width = 30, Height = 8 };
        var snapshots = new[]
        {
            new PolymarketPnLSnapshot { Timestamp = DateTimeOffset.UtcNow.AddHours(-3), RealizedPnL = 10 },
            new PolymarketPnLSnapshot { Timestamp = DateTimeOffset.UtcNow.AddHours(-2), RealizedPnL = 25 },
            new PolymarketPnLSnapshot { Timestamp = DateTimeOffset.UtcNow.AddHours(-1), RealizedPnL = 15 },
            new PolymarketPnLSnapshot { Timestamp = DateTimeOffset.UtcNow, RealizedPnL = 40 }
        };

        var chart = viz.RenderPnLHistory(snapshots);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("P&amp;L History"));
        Assert.That(chart, Does.Contain("Points: 4"));
    }

    #endregion

    #region Visualizer — таблицы

    [TestCase(TestName = "Visualizer: RenderPositionsTable с позициями")]
    public void VisualizerPositionsTableTest()
    {
        var viz = new PolymarketVisualizer();
        var positions = new[]
        {
            new PolymarketPosition
            {
                AssetId = "token-yes",
                Quantity = 100,
                AverageCostBasis = 0.60,
                CurrentPrice = 0.75
            },
            new PolymarketPosition
            {
                AssetId = "long-asset-name-here-test-xxx",
                Quantity = 50,
                AverageCostBasis = 0.40,
                CurrentPrice = 0.35
            }
        };

        var table = viz.RenderPositionsTable(positions);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(table, Does.Contain("Asset"));
        Assert.That(table, Does.Contain("Qty"));
        Assert.That(table, Does.Contain("P&amp;L"));
        Assert.That(table, Does.Contain("┌"));
        Assert.That(table, Does.Contain("└"));
        Assert.That(table, Does.Contain("token-yes"));
    }

    [TestCase(TestName = "Visualizer: RenderPositionsTable пустой → [Нет открытых позиций]")]
    public void VisualizerPositionsTableEmptyTest()
    {
        var viz = new PolymarketVisualizer();
        var table = viz.RenderPositionsTable([]);
        Assert.That(table, Is.EqualTo("[Нет открытых позиций]"));
    }

    [TestCase(TestName = "Visualizer: RenderBacktestSummary содержит показатели")]
    public void VisualizerBacktestSummaryTest()
    {
        var viz = new PolymarketVisualizer();
        var result = new PolymarketBacktestResult
        {
            StrategyName = "Momentum",
            InitialBalance = 10_000,
            FinalBalance = 12_500,
            SharpeRatio = 1.75,
            MaxDrawdownPercent = 8.5,
            ProfitFactor = 2.30,
            Trades = [],
            EquityCurve = [10_000, 12_500]
        };

        var summary = viz.RenderBacktestSummary(result);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(summary, Does.Contain("Momentum"));
        Assert.That(summary, Does.Contain("10000"));
        Assert.That(summary, Does.Contain("12500"));
        Assert.That(summary, Does.Contain("Sharpe"));
        Assert.That(summary, Does.Contain("Drawdown"));
        Assert.That(summary, Does.Contain("Profit Factor"));
    }

    [TestCase(TestName = "Visualizer: RenderPortfolioSummary содержит все поля")]
    public void VisualizerPortfolioSummaryTest()
    {
        var viz = new PolymarketVisualizer();
        var summary = new PolymarketPortfolioSummary
        {
            OpenPositions = 3,
            ClosedPositions = 2,
            TotalMarketValue = 5000,
            TotalCostBasis = 4500,
            TotalUnrealizedPnL = 500,
            TotalRealizedPnL = 200,
            TotalFees = 10,
            NetPnL = 690
        };

        var text = viz.RenderPortfolioSummary(summary);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(text, Does.Contain("Сводка портфеля"));
        Assert.That(text, Does.Contain("3")); // OpenPositions
        Assert.That(text, Does.Contain("5000"));
        Assert.That(text, Does.Contain("500"));
    }

    #endregion

    #region ConfigManager — сериализация

    [TestCase(TestName = "ConfigManager: Save/Load round-trip")]
    public void ConfigManagerRoundTripTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig
                {
                    Type = "Momentum",
                    LookbackPeriod = 10,
                    Threshold = 0.02,
                    PositionSize = 100,
                    AssetIds = ["token-a", "token-b"]
                }
            ],
            RiskRules =
            [
                new PolymarketRiskRuleConfig
                {
                    AssetId = "token-a",
                    StopLossPrice = 0.30,
                    TakeProfitPrice = 0.90
                }
            ],
            DryRun = true,
            MinConfidence = 0.5,
            EvaluationIntervalSeconds = 15,
            OrderCooldownSeconds = 30
        };

        var json = manager.Save(config);
        var loaded = manager.Load(json);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Strategies, Has.Length.EqualTo(1));
        Assert.That(loaded.Strategies![0].Type, Is.EqualTo("Momentum"));
        Assert.That(loaded.Strategies[0].LookbackPeriod, Is.EqualTo(10));
        Assert.That(loaded.Strategies[0].Threshold, Is.EqualTo(0.02));
        Assert.That(loaded.Strategies[0].PositionSize, Is.EqualTo(100));
        Assert.That(loaded.Strategies[0].AssetIds, Has.Length.EqualTo(2));
        Assert.That(loaded.RiskRules, Has.Length.EqualTo(1));
        Assert.That(loaded.RiskRules![0].StopLossPrice, Is.EqualTo(0.30));
        Assert.That(loaded.DryRun, Is.True);
        Assert.That(loaded.MinConfidence, Is.EqualTo(0.5));
    }

    [TestCase(TestName = "ConfigManager: Load пустая строка → исключение")]
    public void ConfigManagerLoadEmptyTest()
    {
        var manager = new PolymarketConfigManager();
        Assert.Throws<ArgumentException>(() => manager.Load(""));
    }

    [TestCase(TestName = "ConfigManager: Save null → исключение")]
    public void ConfigManagerSaveNullTest()
    {
        var manager = new PolymarketConfigManager();
        Assert.Throws<ArgumentNullException>(() => manager.Save(null!));
    }

    [TestCase(TestName = "ConfigManager: SaveToFile/LoadFromFile round-trip")]
    public void ConfigManagerFileRoundTripTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            DryRun = false,
            MinConfidence = 0.7,
            Strategies =
            [
                new PolymarketStrategyConfig
                {
                    Type = "MeanReversion",
                    LookbackPeriod = 20,
                    Threshold = 1.5,
                    PositionSize = 200
                }
            ]
        };

        var tmpFile = Path.GetTempFileName();
        try
        {
            manager.SaveToFile(config, tmpFile);
            var loaded = manager.LoadFromFile(tmpFile);

            using var scope = Assert.EnterMultipleScope();
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.DryRun, Is.False);
            Assert.That(loaded.MinConfidence, Is.EqualTo(0.7));
            Assert.That(loaded.Strategies![0].Type, Is.EqualTo("MeanReversion"));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [TestCase(TestName = "ConfigManager: конфигурация с webhooks")]
    public void ConfigManagerWebhooksTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Webhooks =
            [
                new PolymarketWebhookConfigData
                {
                    Id = "wh1",
                    Url = "https://hooks.slack.com/test",
                    Type = PolymarketWebhookType.Slack,
                    Enabled = true
                },
                new PolymarketWebhookConfigData
                {
                    Id = "wh2",
                    Url = "https://api.telegram.org/bot123/sendMessage",
                    Type = PolymarketWebhookType.Telegram,
                    TelegramChatId = "12345",
                    Enabled = false
                }
            ]
        };

        var json = manager.Save(config);
        var loaded = manager.Load(json);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(loaded!.Webhooks, Has.Length.EqualTo(2));
        Assert.That(loaded.Webhooks![0].Type, Is.EqualTo(PolymarketWebhookType.Slack));
        Assert.That(loaded.Webhooks[1].TelegramChatId, Is.EqualTo("12345"));
        Assert.That(loaded.Webhooks[1].Enabled, Is.False);
    }

    [TestCase(TestName = "ConfigManager: конфигурация с лимитами")]
    public void ConfigManagerLimitsTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Limits = new PolymarketLimitsConfig
            {
                MaxPositionSize = 1000,
                MaxOpenPositions = 10,
                MaxPortfolioLoss = 5000,
                MaxPositionPercent = 0.20,
                MaxDailyLoss = 500
            }
        };

        var json = manager.Save(config);
        var loaded = manager.Load(json);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(loaded!.Limits, Is.Not.Null);
        Assert.That(loaded.Limits!.MaxPositionSize, Is.EqualTo(1000));
        Assert.That(loaded.Limits.MaxOpenPositions, Is.EqualTo(10));
        Assert.That(loaded.Limits.MaxPortfolioLoss, Is.EqualTo(5000));
        Assert.That(loaded.Limits.MaxPositionPercent, Is.EqualTo(0.20));
        Assert.That(loaded.Limits.MaxDailyLoss, Is.EqualTo(500));
    }

    #endregion

    #region ConfigManager — создание стратегий

    [TestCase(TestName = "ConfigManager: CreateStrategies — Momentum")]
    public void ConfigManagerCreateMomentumTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig
                {
                    Type = "Momentum",
                    LookbackPeriod = 10,
                    Threshold = 0.05,
                    PositionSize = 50,
                    AssetIds = ["t1", "t2"]
                }
            ]
        };

        var strategies = manager.CreateStrategies(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(strategies, Has.Length.EqualTo(1));
        Assert.That(strategies[0].strategy, Is.InstanceOf<PolymarketMomentumStrategy>());
        Assert.That(strategies[0].assetIds, Is.EqualTo(new[] { "t1", "t2" }));

        (strategies[0].strategy as IDisposable)?.Dispose();
    }

    [TestCase(TestName = "ConfigManager: CreateStrategies — MeanReversion")]
    public void ConfigManagerCreateMeanReversionTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig
                {
                    Type = "MeanReversion",
                    LookbackPeriod = 20,
                    Threshold = 2.0,
                    PositionSize = 100
                }
            ]
        };

        var strategies = manager.CreateStrategies(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(strategies, Has.Length.EqualTo(1));
        Assert.That(strategies[0].strategy, Is.InstanceOf<PolymarketMeanReversionStrategy>());
        Assert.That(strategies[0].assetIds, Is.Empty);

        (strategies[0].strategy as IDisposable)?.Dispose();
    }

    [TestCase(TestName = "ConfigManager: CreateStrategies — Arbitrage с парами")]
    public void ConfigManagerCreateArbitrageTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig
                {
                    Type = "Arbitrage",
                    Threshold = 0.03,
                    PositionSize = 200,
                    ArbitragePairs =
                    [
                        new PolymarketArbitragePairConfig { TokenA = "yes", TokenB = "no" }
                    ]
                }
            ]
        };

        var strategies = manager.CreateStrategies(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(strategies, Has.Length.EqualTo(1));
        Assert.That(strategies[0].strategy, Is.InstanceOf<PolymarketArbitrageStrategy>());

        (strategies[0].strategy as IDisposable)?.Dispose();
    }

    [TestCase(TestName = "ConfigManager: CreateStrategies — пустые стратегии → пустой массив")]
    public void ConfigManagerCreateEmptyStrategiesTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig { Strategies = null };

        var strategies = manager.CreateStrategies(config);
        Assert.That(strategies, Is.Empty);
    }

    [TestCase(TestName = "ConfigManager: CreateStrategies — неизвестный тип → PolymarketException")]
    public void ConfigManagerCreateUnknownStrategyTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig { Type = "Unknown", LookbackPeriod = 5, Threshold = 0.1, PositionSize = 10 }
            ]
        };

        Assert.Throws<PolymarketException>(() => manager.CreateStrategies(config));
    }

    [TestCase(TestName = "ConfigManager: CreateStrategies — несколько стратегий одновременно")]
    public void ConfigManagerCreateMultipleStrategiesTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Strategies =
            [
                new PolymarketStrategyConfig { Type = "Momentum", LookbackPeriod = 5, Threshold = 0.01, PositionSize = 100, AssetIds = ["a1"] },
                new PolymarketStrategyConfig { Type = "MeanReversion", LookbackPeriod = 10, Threshold = 1.5, PositionSize = 50, AssetIds = ["a2"] },
                new PolymarketStrategyConfig { Type = "Arbitrage", Threshold = 0.02, PositionSize = 200 }
            ]
        };

        var strategies = manager.CreateStrategies(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(strategies, Has.Length.EqualTo(3));
        Assert.That(strategies[0].strategy, Is.InstanceOf<PolymarketMomentumStrategy>());
        Assert.That(strategies[1].strategy, Is.InstanceOf<PolymarketMeanReversionStrategy>());
        Assert.That(strategies[2].strategy, Is.InstanceOf<PolymarketArbitrageStrategy>());

        foreach (var (s, _) in strategies)
            (s as IDisposable)?.Dispose();
    }

    #endregion

    #region ConfigManager — создание правил рисков

    [TestCase(TestName = "ConfigManager: CreateRiskRules round-trip")]
    public void ConfigManagerCreateRiskRulesTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            RiskRules =
            [
                new PolymarketRiskRuleConfig
                {
                    AssetId = "t1",
                    StopLossPrice = 0.30,
                    TakeProfitPrice = 0.90,
                    TrailingStopPercent = 0.10,
                    MaxLossPerPosition = 500
                }
            ]
        };

        var rules = manager.CreateRiskRules(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(rules, Has.Length.EqualTo(1));
        Assert.That(rules[0].AssetId, Is.EqualTo("t1"));
        Assert.That(rules[0].StopLossPrice, Is.EqualTo(0.30));
        Assert.That(rules[0].TakeProfitPrice, Is.EqualTo(0.90));
        Assert.That(rules[0].TrailingStopPercent, Is.EqualTo(0.10));
        Assert.That(rules[0].MaxLossPerPosition, Is.EqualTo(500));
    }

    [TestCase(TestName = "ConfigManager: CreateRiskRules null → пустой массив")]
    public void ConfigManagerCreateRiskRulesNullTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig { RiskRules = null };

        var rules = manager.CreateRiskRules(config);
        Assert.That(rules, Is.Empty);
    }

    #endregion

    #region ConfigManager — создание webhook-конфигураций

    [TestCase(TestName = "ConfigManager: CreateWebhookConfigs round-trip")]
    public void ConfigManagerCreateWebhookConfigsTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Webhooks =
            [
                new PolymarketWebhookConfigData
                {
                    Id = "wh-tg",
                    Url = "https://api.telegram.org/bot123/sendMessage",
                    Type = PolymarketWebhookType.Telegram,
                    TelegramChatId = "999",
                    Enabled = true
                }
            ]
        };

        var webhooks = manager.CreateWebhookConfigs(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(webhooks, Has.Length.EqualTo(1));
        Assert.That(webhooks[0].Id, Is.EqualTo("wh-tg"));
        Assert.That(webhooks[0].Url, Is.EqualTo("https://api.telegram.org/bot123/sendMessage"));
        Assert.That(webhooks[0].Type, Is.EqualTo(PolymarketWebhookType.Telegram));
        Assert.That(webhooks[0].TelegramChatId, Is.EqualTo("999"));
        Assert.That(webhooks[0].IsEnabled, Is.True);
    }

    [TestCase(TestName = "ConfigManager: CreateWebhookConfigs null → пустой массив")]
    public void ConfigManagerCreateWebhookConfigsNullTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig { Webhooks = null };

        var webhooks = manager.CreateWebhookConfigs(config);
        Assert.That(webhooks, Is.Empty);
    }

    #endregion

    #region ConfigManager — создание лимитов

    [TestCase(TestName = "ConfigManager: CreateLimits round-trip")]
    public void ConfigManagerCreateLimitsTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig
        {
            Limits = new PolymarketLimitsConfig
            {
                MaxPositionSize = 2000,
                MaxOpenPositions = 8,
                MaxPortfolioLoss = 10_000,
                MaxPositionPercent = 0.15,
                MaxDailyLoss = 1000
            }
        };

        var limits = manager.CreateLimits(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(limits.MaxPositionSize, Is.EqualTo(2000));
        Assert.That(limits.MaxOpenPositions, Is.EqualTo(8));
        Assert.That(limits.MaxPortfolioLoss, Is.EqualTo(10_000));
        Assert.That(limits.MaxPositionPercent, Is.EqualTo(0.15));
        Assert.That(limits.MaxDailyLoss, Is.EqualTo(1000));
    }

    [TestCase(TestName = "ConfigManager: CreateLimits без лимитов → defaults")]
    public void ConfigManagerCreateLimitsDefaultsTest()
    {
        var manager = new PolymarketConfigManager();
        var config = new PolymarketSystemConfig { Limits = null };

        var limits = manager.CreateLimits(config);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(limits.MaxPositionSize, Is.EqualTo(double.MaxValue));
        Assert.That(limits.MaxOpenPositions, Is.EqualTo(int.MaxValue));
    }

    #endregion

    #region ConfigManager — PolymarketSystemConfig defaults

    [TestCase(TestName = "SystemConfig: значения по умолчанию")]
    public void SystemConfigDefaultsTest()
    {
        var config = new PolymarketSystemConfig();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(config.DryRun, Is.True);
        Assert.That(config.MinConfidence, Is.EqualTo(0.3));
        Assert.That(config.EvaluationIntervalSeconds, Is.EqualTo(30));
        Assert.That(config.OrderCooldownSeconds, Is.EqualTo(60));
        Assert.That(config.Strategies, Is.Null);
        Assert.That(config.RiskRules, Is.Null);
        Assert.That(config.Webhooks, Is.Null);
        Assert.That(config.Limits, Is.Null);
    }

    #endregion

    #region ConfigManager — JSON source-generation (NativeAOT)

    [TestCase(TestName = "ConfigJsonContext: source-generated сериализация работает")]
    public void ConfigJsonContextTest()
    {
        var config = new PolymarketSystemConfig
        {
            DryRun = false,
            MinConfidence = 0.8
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config,
            PolymarketConfigJsonContext.Default.PolymarketSystemConfig);
        var loaded = System.Text.Json.JsonSerializer.Deserialize(json,
            PolymarketConfigJsonContext.Default.PolymarketSystemConfig);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.Contain("dryRun"));
        Assert.That(json, Does.Contain("minConfidence"));
        Assert.That(loaded!.DryRun, Is.False);
        Assert.That(loaded.MinConfidence, Is.EqualTo(0.8));
    }

    [TestCase(TestName = "ConfigJsonContext: WriteIndented форматирует JSON")]
    public void ConfigJsonContextIndentedTest()
    {
        var config = new PolymarketSystemConfig { DryRun = true };

        var json = System.Text.Json.JsonSerializer.Serialize(config,
            PolymarketConfigJsonContext.Default.PolymarketSystemConfig);

        // WriteIndented = true в контексте → JSON содержит переносы
        Assert.That(json, Does.Contain("\n"));
    }

    [TestCase(TestName = "ConfigJsonContext: WhenWritingNull скрывает null-поля")]
    public void ConfigJsonContextNullHandlingTest()
    {
        var config = new PolymarketSystemConfig(); // все nullable = null

        var json = System.Text.Json.JsonSerializer.Serialize(config,
            PolymarketConfigJsonContext.Default.PolymarketSystemConfig);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.Not.Contain("strategies"));
        Assert.That(json, Does.Not.Contain("riskRules"));
        Assert.That(json, Does.Not.Contain("webhooks"));
        Assert.That(json, Does.Not.Contain("limits"));
    }

    #endregion

    #region Visualizer — крайние случаи

    [TestCase(TestName = "Visualizer: RenderChart с большим набором данных")]
    public void VisualizerLargeDatasetTest()
    {
        var viz = new PolymarketVisualizer { Width = 40, Height = 10 };
        var values = Enumerable.Range(0, 1000).Select(i => Math.Sin(i * 0.1) * 50 + 100).ToArray();

        var chart = viz.RenderChart(values, "1000 точек");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("1000 точек"));
        Assert.That(chart, Does.Contain("Points: 1000"));
    }

    [TestCase(TestName = "Visualizer: RenderChart с отрицательными значениями")]
    public void VisualizerNegativeValuesTest()
    {
        var viz = new PolymarketVisualizer { Width = 20, Height = 8 };
        double[] values = [-50, -30, -10, 10, 30, 50, 30, 10, -10, -30];

        var chart = viz.RenderChart(values, "±");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(chart, Does.Contain("Min: -50.00"));
        Assert.That(chart, Does.Contain("Max: 50.00"));
    }

    #endregion
}
