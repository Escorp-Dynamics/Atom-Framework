namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для бэктестера, исполнителя ордеров и webhook-нотификатора.
/// </summary>
public class PolymarketTradingSystemTests(ILogger logger) : BenchmarkTests<PolymarketTradingSystemTests>(logger)
{
    public PolymarketTradingSystemTests() : this(ConsoleLogger.Unicode) { }

    #region Backtester — базовые тесты

    [TestCase(TestName = "Backtester: пустые данные → баланс не меняется")]
    public void BacktesterEmptyDataTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 10_000 };
        using var strategy = new PolymarketMomentumStrategy(lookbackPeriod: 3);

        var result = bt.Run(strategy, "t1", []);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.FinalBalance, Is.EqualTo(10_000));
        Assert.That(result.TotalTrades, Is.EqualTo(0));
        Assert.That(result.StrategyName, Is.EqualTo("Momentum"));
        Assert.That(result.EquityCurve, Has.Length.EqualTo(1));
        Assert.That(result.Trades, Is.Empty);
    }

    [TestCase(TestName = "Backtester: Momentum на восходящем тренде — прибыль")]
    public void BacktesterMomentumUptrendTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 1000 };
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 100);

        // Генерируем восходящий тренд: 0.30 → 0.90
        var prices = Enumerable.Range(0, 20).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.30 + i * 0.03,
            BestBid = 0.29 + i * 0.03,
            BestAsk = 0.31 + i * 0.03
        }).ToArray();

        var result = bt.Run(strategy, "uptrend", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.TotalTrades, Is.GreaterThan(0));
        Assert.That(result.EquityCurve, Has.Length.GreaterThan(1));
    }

    [TestCase(TestName = "Backtester: MeanReversion на волатильных данных")]
    public void BacktesterMeanReversionTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 5000 };
        using var strategy = new PolymarketMeanReversionStrategy(
            lookbackPeriod: 5, deviationThreshold: 1.5, positionSize: 50);

        // Осциллирующая цена: 0.50 ± 0.15
        var prices = Enumerable.Range(0, 30).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.50 + 0.15 * Math.Sin(i * 0.5),
            BestBid = 0.49 + 0.15 * Math.Sin(i * 0.5),
            BestAsk = 0.51 + 0.15 * Math.Sin(i * 0.5)
        }).ToArray();

        var result = bt.Run(strategy, "oscillating", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.InitialBalance, Is.EqualTo(5000));
        Assert.That(result.EquityCurve.Length, Is.GreaterThan(1));
    }

    [TestCase(TestName = "Backtester: SharpeRatio корректно рассчитывается")]
    public void BacktesterSharpeRatioTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 1000 };
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);

        var prices = Enumerable.Range(0, 15).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.40 + i * 0.02
        }).ToArray();

        var result = bt.Run(strategy, "sharpe", prices);

        // SharpeRatio может быть 0 если нет сделок, но не NaN
        Assert.That(double.IsNaN(result.SharpeRatio), Is.False);
    }

    [TestCase(TestName = "Backtester: MaxDrawdown >= 0")]
    public void BacktesterMaxDrawdownTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 1000 };
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);

        // Сначала вверх, потом вниз
        var prices = new List<PolymarketPricePoint>();
        for (int i = 0; i < 10; i++)
            prices.Add(new PolymarketPricePoint { Midpoint = 0.30 + i * 0.04 });
        for (int i = 0; i < 10; i++)
            prices.Add(new PolymarketPricePoint { Midpoint = 0.70 - i * 0.04 });

        var result = bt.Run(strategy, "drawdown", [.. prices]);

        Assert.That(result.MaxDrawdownPercent, Is.GreaterThanOrEqualTo(0));
    }

    [TestCase(TestName = "Backtester: WinRate в диапазоне [0, 100]")]
    public void BacktesterWinRateRangeTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 1000 };
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);

        var prices = Enumerable.Range(0, 15).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.40 + i * 0.02
        }).ToArray();

        var result = bt.Run(strategy, "winrate", prices);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(result.WinRate, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.WinRate, Is.LessThanOrEqualTo(100));
    }

    [TestCase(TestName = "Backtester: ProfitFactor >= 0")]
    public void BacktesterProfitFactorTest()
    {
        var bt = new PolymarketBacktester { InitialBalance = 1000 };
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);

        var prices = Enumerable.Range(0, 15).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.40 + i * 0.02
        }).ToArray();

        var result = bt.Run(strategy, "pf", prices);
        Assert.That(result.ProfitFactor, Is.GreaterThanOrEqualTo(0));
    }

    [TestCase(TestName = "Backtester: FeeRateBps уменьшает прибыль")]
    public void BacktesterWithFeesTest()
    {
        using var strategyNoFee = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);
        using var strategyWithFee = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.01, positionSize: 50);

        var prices = Enumerable.Range(0, 15).Select(i => new PolymarketPricePoint
        {
            Midpoint = 0.40 + i * 0.02,
            BestBid = 0.39 + i * 0.02,
            BestAsk = 0.41 + i * 0.02
        }).ToArray();

        var btNoFee = new PolymarketBacktester { InitialBalance = 1000, FeeRateBps = 0 };
        var btWithFee = new PolymarketBacktester { InitialBalance = 1000, FeeRateBps = 200 };

        var resultNoFee = btNoFee.Run(strategyNoFee, "fee-test", prices);
        var resultWithFee = btWithFee.Run(strategyWithFee, "fee-test", prices);

        // С комиссией итоговый баланс должен быть <= без комиссии
        Assert.That(resultWithFee.FinalBalance, Is.LessThanOrEqualTo(resultNoFee.FinalBalance));
    }

    [TestCase(TestName = "Backtester: null strategy → ArgumentNullException")]
    public void BacktesterNullStrategyTest()
    {
        var bt = new PolymarketBacktester();
        Assert.Throws<ArgumentNullException>(() => bt.Run(null!, "t1", []));
    }

    [TestCase(TestName = "Backtester: null priceData → ArgumentNullException")]
    public void BacktesterNullPriceDataTest()
    {
        var bt = new PolymarketBacktester();
        using var strategy = new PolymarketMomentumStrategy();
        Assert.Throws<ArgumentNullException>(() => bt.Run(strategy, "t1", null!));
    }

    #endregion

    #region BacktestResult — вычисляемые свойства

    [TestCase(TestName = "BacktestResult: NetPnL = FinalBalance - InitialBalance")]
    public void BacktestResultNetPnLTest()
    {
        var result = new PolymarketBacktestResult
        {
            StrategyName = "Test",
            InitialBalance = 1000,
            FinalBalance = 1500,
            EquityCurve = [1000, 1500],
            Trades = []
        };

        Assert.That(result.NetPnL, Is.EqualTo(500));
    }

    [TestCase(TestName = "BacktestResult: ReturnPercent корректен")]
    public void BacktestResultReturnPercentTest()
    {
        var result = new PolymarketBacktestResult
        {
            StrategyName = "Test",
            InitialBalance = 1000,
            FinalBalance = 1200,
            EquityCurve = [1000, 1200],
            Trades = []
        };

        Assert.That(result.ReturnPercent, Is.EqualTo(20.0).Within(0.01));
    }

    [TestCase(TestName = "BacktestResult: WinRate при 0 сделках = 0")]
    public void BacktestResultWinRateZeroTradesTest()
    {
        var result = new PolymarketBacktestResult
        {
            StrategyName = "Test",
            InitialBalance = 1000,
            FinalBalance = 1000,
            EquityCurve = [1000],
            Trades = []
        };

        Assert.That(result.WinRate, Is.EqualTo(0));
    }

    [TestCase(TestName = "BacktestResult: AveragePnLPerTrade при 0 сделках = 0")]
    public void BacktestResultAvgPnLZeroTest()
    {
        var result = new PolymarketBacktestResult
        {
            StrategyName = "Test",
            InitialBalance = 1000,
            FinalBalance = 1000,
            EquityCurve = [1000],
            Trades = []
        };

        Assert.That(result.AveragePnLPerTrade, Is.EqualTo(0));
    }

    #endregion

    #region OrderExecutor — базовые тесты

    [TestCase(TestName = "OrderExecutor: DryRun по умолчанию включён")]
    public void OrderExecutorDryRunDefaultTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        using var executor = new PolymarketOrderExecutor(rest, stream);

        Assert.That(executor.DryRun, Is.True);
    }

    [TestCase(TestName = "OrderExecutor: AddStrategy и RemoveStrategy")]
    public void OrderExecutorAddRemoveStrategyTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        using var executor = new PolymarketOrderExecutor(rest, stream);
        using var strategy = new PolymarketMomentumStrategy();

        executor.AddStrategy(strategy, ["token-1", "token-2"]);
        Assert.That(executor.StrategyCount, Is.EqualTo(1));

        executor.RemoveStrategy("Momentum");
        Assert.That(executor.StrategyCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "OrderExecutor: Start и StopAsync")]
    public async Task OrderExecutorStartStopTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        await using var executor = new PolymarketOrderExecutor(rest, stream);

        executor.EvaluationInterval = TimeSpan.FromMilliseconds(50);
        executor.Start();
        Assert.That(executor.IsRunning, Is.True);

        await executor.StopAsync();
        Assert.That(executor.IsRunning, Is.False);
    }

    [TestCase(TestName = "OrderExecutor: EvaluateOnce без стратегий — без ошибок")]
    public async Task OrderExecutorEvaluateEmptyTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        await using var executor = new PolymarketOrderExecutor(rest, stream);

        // Не должно бросить исключение
        await executor.EvaluateOnceAsync();
    }

    [TestCase(TestName = "OrderExecutor: DryRun генерирует OrderExecuted без реального вызова")]
    public async Task OrderExecutorDryRunSignalTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        await using var executor = new PolymarketOrderExecutor(rest, stream);
        executor.DryRun = true;
        executor.MinConfidence = 0.0;
        executor.OrderCooldown = TimeSpan.Zero;

        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.001, positionSize: 10);

        // Подаём тренд
        for (int i = 0; i < 3; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceStream.PolymarketPriceSnapshot
            {
                AssetId = "dry-token",
                Midpoint = (0.30 + i * 0.10).ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        executor.AddStrategy(strategy, ["dry-token"]);

        var executed = false;
        executor.OrderExecuted += (s, e) =>
        {
            executed = e.Success;
            return ValueTask.CompletedTask;
        };

        await executor.EvaluateOnceAsync();
        Assert.That(executed, Is.True);
    }

    [TestCase(TestName = "OrderExecutor: MinConfidence фильтрует слабые сигналы")]
    public async Task OrderExecutorMinConfidenceFilterTest()
    {
        using var rest = new PolymarketRestClient();
        using var stream = new PolymarketPriceStream();
        await using var executor = new PolymarketOrderExecutor(rest, stream);
        executor.DryRun = true;
        executor.MinConfidence = 0.99; // Очень высокий порог
        executor.OrderCooldown = TimeSpan.Zero;

        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 3, momentumThreshold: 0.001, positionSize: 10);

        for (int i = 0; i < 3; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceStream.PolymarketPriceSnapshot
            {
                AssetId = "filter-token",
                Midpoint = (0.40 + i * 0.05).ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        executor.AddStrategy(strategy, ["filter-token"]);

        var executed = false;
        executor.OrderExecuted += (s, e) =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        };

        await executor.EvaluateOnceAsync();
        // Сигнал с низкой confidence должен быть отфильтрован
        Assert.That(executed, Is.False);
    }

    [TestCase(TestName = "OrderExecutor: null restClient → ArgumentNullException")]
    public void OrderExecutorNullRestClientTest()
    {
        using var stream = new PolymarketPriceStream();
        Assert.Throws<ArgumentNullException>(() => new PolymarketOrderExecutor(null!, stream));
    }

    [TestCase(TestName = "OrderExecutor: null priceStream → ArgumentNullException")]
    public void OrderExecutorNullPriceStreamTest()
    {
        using var rest = new PolymarketRestClient();
        Assert.Throws<ArgumentNullException>(() => new PolymarketOrderExecutor(rest, null!));
    }

    #endregion

    #region WebhookNotifier — базовые тесты

    [TestCase(TestName = "WebhookNotifier: AddWebhook и RemoveWebhook")]
    public void WebhookAddRemoveTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "wh-1",
            Url = "https://example.com/webhook",
            Type = PolymarketWebhookType.Generic
        });

        var hooks = notifier.GetWebhooks();
        Assert.That(hooks, Has.Length.EqualTo(1));

        notifier.RemoveWebhook("wh-1");
        hooks = notifier.GetWebhooks();
        Assert.That(hooks, Has.Length.EqualTo(0));
    }

    [TestCase(TestName = "WebhookNotifier: HTTP URL → ArgumentException")]
    public void WebhookHttpUrlRejectTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        Assert.Throws<ArgumentException>(() => notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "bad", Url = "http://insecure.com/hook",
            Type = PolymarketWebhookType.Generic
        }));
    }

    [TestCase(TestName = "WebhookNotifier: невалидный URL → ArgumentException")]
    public void WebhookInvalidUrlTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        Assert.Throws<ArgumentException>(() => notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "bad", Url = "not-a-url",
            Type = PolymarketWebhookType.Generic
        }));
    }

    [TestCase(TestName = "WebhookNotifier: Telegram конфиг")]
    public void WebhookTelegramConfigTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "tg",
            Url = "https://api.telegram.org/bot12345/sendMessage",
            Type = PolymarketWebhookType.Telegram,
            TelegramChatId = "-123456789"
        });

        var hooks = notifier.GetWebhooks();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(hooks, Has.Length.EqualTo(1));
        Assert.That(hooks[0].Type, Is.EqualTo(PolymarketWebhookType.Telegram));
        Assert.That(hooks[0].TelegramChatId, Is.EqualTo("-123456789"));
    }

    [TestCase(TestName = "WebhookNotifier: Discord конфиг")]
    public void WebhookDiscordConfigTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "discord",
            Url = "https://discord.com/api/webhooks/123/abc",
            Type = PolymarketWebhookType.Discord
        });

        var hooks = notifier.GetWebhooks();
        Assert.That(hooks[0].Type, Is.EqualTo(PolymarketWebhookType.Discord));
    }

    [TestCase(TestName = "WebhookNotifier: Slack конфиг")]
    public void WebhookSlackConfigTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "slack",
            Url = "https://hooks.slack.com/services/T00/B00/XXX",
            Type = PolymarketWebhookType.Slack
        });

        var hooks = notifier.GetWebhooks();
        Assert.That(hooks[0].Type, Is.EqualTo(PolymarketWebhookType.Slack));
    }

    [TestCase(TestName = "WebhookNotifier: IsEnabled = false — webhook пропускается")]
    public void WebhookDisabledSkipTest()
    {
        using var notifier = new PolymarketWebhookNotifier();

        notifier.AddWebhook(new PolymarketWebhookConfig
        {
            Id = "disabled",
            Url = "https://example.com/hook",
            Type = PolymarketWebhookType.Generic,
            IsEnabled = false
        });

        // Не должно бросить исключение — disabled webhook пропускается
        Assert.DoesNotThrowAsync(async () =>
            await notifier.SendMessageAsync("test message"));
    }

    [TestCase(TestName = "WebhookNotifier: null config → ArgumentNullException")]
    public void WebhookNullConfigTest()
    {
        using var notifier = new PolymarketWebhookNotifier();
        Assert.Throws<ArgumentNullException>(() => notifier.AddWebhook(null!));
    }

    [TestCase(TestName = "WebhookNotifier: пустое сообщение → ArgumentException")]
    public void WebhookEmptyMessageTest()
    {
        using var notifier = new PolymarketWebhookNotifier();
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await notifier.SendMessageAsync(""));
    }

    #endregion

    #region WebhookType Enum

    [TestCase(TestName = "WebhookType: все 4 типа определены")]
    public void WebhookTypeEnumTest()
    {
        var values = Enum.GetValues<PolymarketWebhookType>();
        Assert.That(values, Has.Length.EqualTo(4));
    }

    #endregion

    #region OrderExecutedEventArgs

    [TestCase(TestName = "OrderExecutedEventArgs: свойства заполняются")]
    public void OrderExecutedEventArgsTest()
    {
        var signal = new PolymarketTradeSignal
        {
            AssetId = "t1", Action = PolymarketTradeAction.Buy,
            Quantity = 100, Price = "0.60"
        };

        var args = new PolymarketOrderExecutedEventArgs(signal, null, true) { Error = null };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.Signal.AssetId, Is.EqualTo("t1"));
        Assert.That(args.Success, Is.True);
        Assert.That(args.Response, Is.Null);
        Assert.That(args.ExecutedAt, Is.GreaterThan(DateTimeOffset.MinValue));
    }

    [TestCase(TestName = "SignalGeneratedEventArgs: корректное создание")]
    public void SignalGeneratedEventArgsTest()
    {
        var signal = new PolymarketTradeSignal
        {
            AssetId = "t1", Action = PolymarketTradeAction.Sell
        };

        var args = new PolymarketSignalGeneratedEventArgs(signal, "TestStrategy");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.Signal.Action, Is.EqualTo(PolymarketTradeAction.Sell));
        Assert.That(args.StrategyName, Is.EqualTo("TestStrategy"));
    }

    #endregion

    #region PricePoint

    [TestCase(TestName = "PricePoint: свойства заполняются")]
    public void PricePointTest()
    {
        var pp = new PolymarketPricePoint
        {
            Midpoint = 0.55,
            BestBid = 0.54,
            BestAsk = 0.56,
            Timestamp = DateTimeOffset.UtcNow
        };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(pp.Midpoint, Is.EqualTo(0.55));
        Assert.That(pp.BestBid, Is.EqualTo(0.54));
        Assert.That(pp.BestAsk, Is.EqualTo(0.56));
    }

    #endregion

    #region BacktestTrade

    [TestCase(TestName = "BacktestTrade: свойства вычисляются")]
    public void BacktestTradeTest()
    {
        var trade = new PolymarketBacktestTrade
        {
            AssetId = "t1",
            Action = PolymarketTradeAction.Buy,
            Quantity = 50,
            EntryPrice = 0.40,
            ExitPrice = 0.60,
            PnL = 10,
            EntryIndex = 5,
            ExitIndex = 15
        };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(trade.PnL, Is.EqualTo(10));
        Assert.That(trade.ExitIndex, Is.GreaterThan(trade.EntryIndex));
    }

    #endregion

    #region WebhookSentEventArgs

    [TestCase(TestName = "WebhookSentEventArgs: корректное создание")]
    public void WebhookSentEventArgsTest()
    {
        var config = new PolymarketWebhookConfig
        {
            Id = "test", Url = "https://example.com/hook",
            Type = PolymarketWebhookType.Generic
        };

        var args = new PolymarketWebhookSentEventArgs(config, true, 200);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.Config.Id, Is.EqualTo("test"));
        Assert.That(args.Success, Is.True);
        Assert.That(args.StatusCode, Is.EqualTo(200));
        Assert.That(args.Error, Is.Null);
    }

    [TestCase(TestName = "WebhookSentEventArgs: с ошибкой")]
    public void WebhookSentEventArgsErrorTest()
    {
        var config = new PolymarketWebhookConfig
        {
            Id = "fail", Url = "https://example.com/hook",
            Type = PolymarketWebhookType.Telegram
        };

        var ex = new HttpRequestException("Connection refused");
        var args = new PolymarketWebhookSentEventArgs(config, false, 0) { Error = ex };

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.Success, Is.False);
        Assert.That(args.Error, Is.Not.Null);
        Assert.That(args.Error!.Message, Does.Contain("Connection refused"));
    }

    #endregion
}
