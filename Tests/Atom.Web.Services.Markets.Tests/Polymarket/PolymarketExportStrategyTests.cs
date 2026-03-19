namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для PolymarketDataExporter (CSV/JSON экспорт) и торговых стратегий.
/// </summary>
public class PolymarketExportStrategyTests(ILogger logger) : BenchmarkTests<PolymarketExportStrategyTests>(logger)
{
    public PolymarketExportStrategyTests() : this(ConsoleLogger.Unicode) { }

    #region DataExporter — Позиции CSV

    [TestCase(TestName = "ExportPositionsCsv: корректный заголовок и строки")]
    public void ExportPositionsCsvBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var positions = new[]
        {
            new PolymarketPosition
            {
                AssetId = "token-1", Market = "m1", Outcome = "Yes",
                Quantity = 100, AverageCostBasis = 0.6, CurrentPrice = 0.75,
                RealizedPnL = 5.0, TotalFees = 1.2, TradeCount = 3
            }
        };

        var csv = exporter.ExportPositionsCsvString(positions);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Does.Contain("AssetId,Market,Outcome,Quantity"));
        Assert.That(csv, Does.Contain("token-1"));
        Assert.That(csv, Does.Contain("m1"));
        Assert.That(csv, Does.Contain("Yes"));
    }

    [TestCase(TestName = "ExportPositionsCsv: пустой список — только заголовок")]
    public void ExportPositionsCsvEmptyTest()
    {
        var exporter = new PolymarketDataExporter();
        var csv = exporter.ExportPositionsCsvString([]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Does.Contain("AssetId,Market,Outcome"));
        // Только одна строка — заголовок
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(1));
    }

    [TestCase(TestName = "ExportPositionsCsv: спецсимволы экранируются (запятая, кавычки)")]
    public void ExportPositionsCsvEscapeTest()
    {
        var exporter = new PolymarketDataExporter();
        var positions = new[]
        {
            new PolymarketPosition
            {
                AssetId = "token,with,commas", Market = "market\"quoted\"",
                Outcome = "Yes\nNewline"
            }
        };

        var csv = exporter.ExportPositionsCsvString(positions);

        using var scope = Assert.EnterMultipleScope();
        // Запятые и кавычки должны быть экранированы
        Assert.That(csv, Does.Contain("\"token,with,commas\""));
        Assert.That(csv, Does.Contain("\"market\"\"quoted\"\"\""));
    }

    [TestCase(TestName = "ExportPositionsCsv: числа в InvariantCulture")]
    public void ExportPositionsCsvNumberFormatTest()
    {
        var exporter = new PolymarketDataExporter();
        var positions = new[]
        {
            new PolymarketPosition
            {
                AssetId = "t1",
                Quantity = 123.456, AverageCostBasis = 0.789,
                CurrentPrice = 0.95
            }
        };

        var csv = exporter.ExportPositionsCsvString(positions);

        // Должны использоваться точки, а не запятые для десятичных
        Assert.That(csv, Does.Contain("123.456"));
    }

    #endregion

    #region DataExporter — Позиции JSON

    [TestCase(TestName = "ExportPositionsJson: корректная JSON сериализация")]
    public void ExportPositionsJsonBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var positions = new[]
        {
            new PolymarketPosition
            {
                AssetId = "token-json", Market = "m1", Outcome = "No",
                Quantity = 50, AverageCostBasis = 0.4, CurrentPrice = 0.3
            }
        };

        var json = exporter.ExportPositionsJsonString(positions);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.StartWith("["));
        Assert.That(json, Does.Contain("token-json"));
        Assert.That(json, Does.Contain("\"Outcome\":"));
    }

    [TestCase(TestName = "ExportPositionsJson: пустой массив — '[]'")]
    public void ExportPositionsJsonEmptyTest()
    {
        var exporter = new PolymarketDataExporter();
        var json = exporter.ExportPositionsJsonString([]);
        Assert.That(json, Is.EqualTo("[]"));
    }

    #endregion

    #region DataExporter — P&L история CSV

    [TestCase(TestName = "ExportPnLHistoryCsv: заголовок и данные")]
    public void ExportPnLHistoryCsvBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var snapshots = new[]
        {
            new PolymarketPnLSnapshot
            {
                TimestampTicks = 1000,
                Timestamp = DateTimeOffset.UtcNow,
                TotalMarketValue = 500, TotalCostBasis = 400,
                UnrealizedPnL = 100, RealizedPnL = 20,
                TotalFees = 5, OpenPositions = 3
            }
        };

        var csv = exporter.ExportPnLHistoryCsvString(snapshots);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Does.Contain("Timestamp,TotalMarketValue"));
        Assert.That(csv, Does.Contain("500"));
        Assert.That(csv, Does.Contain("400"));
    }

    [TestCase(TestName = "ExportPnLHistoryCsv: множество снимков — множество строк")]
    public void ExportPnLHistoryCsvMultipleTest()
    {
        var exporter = new PolymarketDataExporter();
        var snapshots = Enumerable.Range(0, 10).Select(i => new PolymarketPnLSnapshot
        {
            TimestampTicks = i * 1000,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(i * 5),
            TotalMarketValue = 100 + i * 10,
            OpenPositions = i
        }).ToArray();

        var csv = exporter.ExportPnLHistoryCsvString(snapshots);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(11)); // 1 header + 10 data
    }

    #endregion

    #region DataExporter — P&L история JSON

    [TestCase(TestName = "ExportPnLHistoryJson: корректная JSON структура")]
    public void ExportPnLHistoryJsonBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var snapshots = new[]
        {
            new PolymarketPnLSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                TotalMarketValue = 1000, RealizedPnL = 50
            }
        };

        var json = exporter.ExportPnLHistoryJsonString(snapshots);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.StartWith("["));
        Assert.That(json, Does.Contain("TotalMarketValue"));
    }

    #endregion

    #region DataExporter — Сделки CSV

    [TestCase(TestName = "ExportTradesCsv: корректный вывод сделок")]
    public void ExportTradesCsvBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var trades = new[]
        {
            new PolymarketTrade
            {
                Id = "trade-1", Market = "m1", AssetId = "t1",
                Side = PolymarketSide.Buy, Size = "100", Price = "0.6",
                FeeRateBps = "200", Status = PolymarketTradeStatus.Confirmed,
                MatchTime = "2024-01-01T00:00:00Z", Outcome = "Yes"
            }
        };

        var csv = exporter.ExportTradesCsvString(trades);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(csv, Does.Contain("Id,Market,AssetId,Side"));
        Assert.That(csv, Does.Contain("trade-1"));
        Assert.That(csv, Does.Contain("Buy"));
    }

    [TestCase(TestName = "ExportTradesCsv: пустой список — только заголовок")]
    public void ExportTradesCsvEmptyTest()
    {
        var exporter = new PolymarketDataExporter();
        var csv = exporter.ExportTradesCsvString([]);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(1));
    }

    #endregion

    #region DataExporter — Сделки JSON

    [TestCase(TestName = "ExportTradesJson: корректная сериализация")]
    public void ExportTradesJsonBasicTest()
    {
        var exporter = new PolymarketDataExporter();
        var trades = new[]
        {
            new PolymarketTrade
            {
                Id = "trade-json", AssetId = "token-1",
                Side = PolymarketSide.Sell, Price = "0.45"
            }
        };

        var json = exporter.ExportTradesJsonString(trades);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.Contain("trade-json"));
        Assert.That(json, Does.Contain("token-1"));
    }

    #endregion

    #region DataExporter — Полный отчёт портфеля

    [TestCase(TestName = "ExportPortfolioReport: полный JSON отчёт")]
    public void ExportPortfolioReportJsonTest()
    {
        var exporter = new PolymarketDataExporter();
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1",
                Size = "100", Price = "0.60",
                FeeRateBps = "100", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var json = exporter.ExportPortfolioReportJsonString(tracker);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(json, Does.Contain("GeneratedAt"));
        Assert.That(json, Does.Contain("Summary"));
        Assert.That(json, Does.Contain("Positions"));
        Assert.That(json, Does.Contain("PnLHistory"));
        Assert.That(json, Does.Contain("t1"));
    }

    [TestCase(TestName = "ExportPortfolioReport: с P&L историей")]
    public void ExportPortfolioReportWithHistoryTest()
    {
        var exporter = new PolymarketDataExporter();
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker, TimeSpan.FromMinutes(5));

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1",
                Size = "50", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        history.TakeSnapshot();
        history.TakeSnapshot();

        var json = exporter.ExportPortfolioReportJsonString(tracker, history);
        Assert.That(json, Does.Contain("PnLHistory"));
    }

    [TestCase(TestName = "ExportPortfolioReport: null tracker → ArgumentNullException")]
    public void ExportPortfolioReportNullTrackerTest()
    {
        var exporter = new PolymarketDataExporter();
        Assert.Throws<ArgumentNullException>(() =>
            exporter.ExportPortfolioReportJsonString(null!));
    }

    #endregion

    #region DataExporter — Null аргументы

    [TestCase(TestName = "ExportPositionsCsv: null → ArgumentNullException")]
    public void ExportPositionsCsvNullTest()
    {
        var exporter = new PolymarketDataExporter();
        Assert.Throws<ArgumentNullException>(() =>
            exporter.ExportPositionsCsvString(null!));
    }

    [TestCase(TestName = "ExportTradesCsv: null → ArgumentNullException")]
    public void ExportTradesCsvNullTest()
    {
        var exporter = new PolymarketDataExporter();
        Assert.Throws<ArgumentNullException>(() =>
            exporter.ExportTradesCsvString(null!));
    }

    [TestCase(TestName = "ExportPnLHistoryJson: null → ArgumentNullException")]
    public void ExportPnLHistoryJsonNullTest()
    {
        var exporter = new PolymarketDataExporter();
        Assert.Throws<ArgumentNullException>(() =>
            exporter.ExportPnLHistoryJsonString(null!));
    }

    #endregion

    #region Momentum Strategy — базовые тесты

    [TestCase(TestName = "MomentumStrategy: Name = 'Momentum'")]
    public void MomentumNameTest()
    {
        using var strategy = new PolymarketMomentumStrategy();
        Assert.That(strategy.Name, Is.EqualTo("Momentum"));
    }

    [TestCase(TestName = "MomentumStrategy: недостаточно данных → Hold")]
    public void MomentumInsufficientDataTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMomentumStrategy(lookbackPeriod: 10);

        var signal = strategy.Evaluate(stream, "token-1");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
        Assert.That(signal.AssetId, Is.EqualTo("token-1"));
        Assert.That(signal.Reason, Does.Contain("Недостаточно"));
    }

    [TestCase(TestName = "MomentumStrategy: восходящий тренд → Buy")]
    public void MomentumBuySignalTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 5, momentumThreshold: 0.01);

        // Подаём восходящий тренд
        for (int i = 0; i < 5; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "up-token",
                Midpoint = (0.40 + i * 0.05).ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        var signal = strategy.Evaluate(stream, "up-token");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Buy));
    }

    [TestCase(TestName = "MomentumStrategy: нисходящий тренд → Sell")]
    public void MomentumSellSignalTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 5, momentumThreshold: 0.01);

        // Подаём нисходящий тренд
        for (int i = 0; i < 5; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "down-token",
                Midpoint = (0.80 - i * 0.05).ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        var signal = strategy.Evaluate(stream, "down-token");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Sell));
    }

    [TestCase(TestName = "MomentumStrategy: стабильная цена → Hold")]
    public void MomentumStableHoldTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 5, momentumThreshold: 0.05);

        // Подаём стабильную цену
        for (int i = 0; i < 5; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "stable-token",
                Midpoint = "0.50"
            });
        }

        var signal = strategy.Evaluate(stream, "stable-token");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    [TestCase(TestName = "MomentumStrategy: confidence в диапазоне [0, 1]")]
    public void MomentumConfidenceRangeTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMomentumStrategy(
            lookbackPeriod: 5, momentumThreshold: 0.01);

        for (int i = 0; i < 5; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "conf-token",
                Midpoint = (0.10 + i * 0.20).ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        var signal = strategy.Evaluate(stream, "conf-token");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Confidence, Is.GreaterThanOrEqualTo(0));
        Assert.That(signal.Confidence, Is.LessThanOrEqualTo(1.0));
    }

    #endregion

    #region MeanReversion Strategy — базовые тесты

    [TestCase(TestName = "MeanReversionStrategy: Name = 'MeanReversion'")]
    public void MeanReversionNameTest()
    {
        using var strategy = new PolymarketMeanReversionStrategy();
        Assert.That(strategy.Name, Is.EqualTo("MeanReversion"));
    }

    [TestCase(TestName = "MeanReversionStrategy: недостаточно данных → Hold")]
    public void MeanReversionInsufficientDataTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMeanReversionStrategy(lookbackPeriod: 20);

        var signal = strategy.Evaluate(stream, "token-mr");

        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    [TestCase(TestName = "MeanReversionStrategy: цена ниже среднего → Buy")]
    public void MeanReversionBuySignalTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMeanReversionStrategy(
            lookbackPeriod: 5, deviationThreshold: 1.0);

        // Стабильная цена 0.50, потом резкое падение
        for (int i = 0; i < 4; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "mr-buy",
                Midpoint = "0.50"
            });
        }
        // Резкое падение
        strategy.OnPriceUpdated(new PolymarketPriceSnapshot
        {
            AssetId = "mr-buy",
            Midpoint = "0.10"
        });

        var signal = strategy.Evaluate(stream, "mr-buy");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Buy));
    }

    [TestCase(TestName = "MeanReversionStrategy: цена выше среднего → Sell")]
    public void MeanReversionSellSignalTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMeanReversionStrategy(
            lookbackPeriod: 5, deviationThreshold: 1.0);

        // Стабильная цена 0.50, потом резкий рост
        for (int i = 0; i < 4; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "mr-sell",
                Midpoint = "0.50"
            });
        }
        strategy.OnPriceUpdated(new PolymarketPriceSnapshot
        {
            AssetId = "mr-sell",
            Midpoint = "0.90"
        });

        var signal = strategy.Evaluate(stream, "mr-sell");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Sell));
    }

    [TestCase(TestName = "MeanReversionStrategy: нулевая волатильность → Hold")]
    public void MeanReversionZeroVolatilityTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketMeanReversionStrategy(lookbackPeriod: 5);

        for (int i = 0; i < 5; i++)
        {
            strategy.OnPriceUpdated(new PolymarketPriceSnapshot
            {
                AssetId = "zero-vol",
                Midpoint = "0.50"
            });
        }

        var signal = strategy.Evaluate(stream, "zero-vol");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
        Assert.That(signal.Reason, Does.Contain("волатильность"));
    }

    #endregion

    #region Arbitrage Strategy — базовые тесты

    [TestCase(TestName = "ArbitrageStrategy: Name = 'Arbitrage'")]
    public void ArbitrageNameTest()
    {
        using var strategy = new PolymarketArbitrageStrategy();
        Assert.That(strategy.Name, Is.EqualTo("Arbitrage"));
    }

    [TestCase(TestName = "ArbitrageStrategy: пара не зарегистрирована → Hold")]
    public void ArbitrageNoPairTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketArbitrageStrategy();

        var signal = strategy.Evaluate(stream, "orphan-token");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
        Assert.That(signal.Reason, Does.Contain("не зарегистрирована"));
    }

    [TestCase(TestName = "ArbitrageStrategy: RegisterPair и UnregisterPair")]
    public void ArbitrageRegisterUnregisterTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketArbitrageStrategy();

        strategy.RegisterPair("yes-token", "no-token");
        // После регистрации — не Hold с "не зарегистрирована"
        var signal = strategy.Evaluate(stream, "yes-token");
        Assert.That(signal.Reason, Does.Not.Contain("не зарегистрирована"));

        strategy.UnregisterPair("yes-token", "no-token");
        signal = strategy.Evaluate(stream, "yes-token");
        Assert.That(signal.Reason, Does.Contain("не зарегистрирована"));
    }

    [TestCase(TestName = "ArbitrageStrategy: нет ценовых данных → Hold")]
    public void ArbitrageNoPriceDataTest()
    {
        using var stream = new PolymarketPriceStream();
        using var strategy = new PolymarketArbitrageStrategy();

        strategy.RegisterPair("a", "b");
        var signal = strategy.Evaluate(stream, "a");

        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    #endregion

    #region Стратегии — аргументы конструктора

    [TestCase(TestName = "MomentumStrategy: lookbackPeriod=0 → ArgumentOutOfRangeException")]
    public void MomentumInvalidLookbackTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PolymarketMomentumStrategy(lookbackPeriod: 0));
    }

    [TestCase(TestName = "MomentumStrategy: momentumThreshold=0 → ArgumentOutOfRangeException")]
    public void MomentumInvalidThresholdTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PolymarketMomentumStrategy(momentumThreshold: 0));
    }

    [TestCase(TestName = "MeanReversionStrategy: deviationThreshold=-1 → ArgumentOutOfRangeException")]
    public void MeanReversionInvalidThresholdTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PolymarketMeanReversionStrategy(deviationThreshold: -1));
    }

    [TestCase(TestName = "ArbitrageStrategy: spreadThreshold=0 → ArgumentOutOfRangeException")]
    public void ArbitrageInvalidThresholdTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PolymarketArbitrageStrategy(spreadThreshold: 0));
    }

    #endregion

    #region Стратегии — Dispose

    [TestCase(TestName = "Strategy: Dispose очищает PriceHistories")]
    public void StrategyDisposeTest()
    {
        var strategy = new PolymarketMomentumStrategy(lookbackPeriod: 3);

        strategy.OnPriceUpdated(new PolymarketPriceSnapshot
        {
            AssetId = "token-x",
            Midpoint = "0.50"
        });

        strategy.Dispose();

        // После Dispose Evaluate всё ещё работает — просто нет данных
        using var stream = new PolymarketPriceStream();
        var signal = strategy.Evaluate(stream, "token-x");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    #endregion

    #region PriceHistory — кольцевой буфер

    [TestCase(TestName = "MomentumStrategy: OnPriceUpdated с null midpoint → игнорируется")]
    public void OnPriceUpdatedNullMidpointTest()
    {
        using var strategy = new PolymarketMomentumStrategy(lookbackPeriod: 5);

        strategy.OnPriceUpdated(new PolymarketPriceSnapshot
        {
            AssetId = "null-mid",
            Midpoint = null
        });

        using var stream = new PolymarketPriceStream();
        var signal = strategy.Evaluate(stream, "null-mid");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    [TestCase(TestName = "MomentumStrategy: OnPriceUpdated с невалидным midpoint → игнорируется")]
    public void OnPriceUpdatedInvalidMidpointTest()
    {
        using var strategy = new PolymarketMomentumStrategy(lookbackPeriod: 5);

        strategy.OnPriceUpdated(new PolymarketPriceSnapshot
        {
            AssetId = "bad-mid",
            Midpoint = "not-a-number"
        });

        using var stream = new PolymarketPriceStream();
        var signal = strategy.Evaluate(stream, "bad-mid");
        Assert.That(signal.Action, Is.EqualTo(PolymarketTradeAction.Hold));
    }

    #endregion

    #region TradeSignal — свойства

    [TestCase(TestName = "TradeSignal: TimestampTicks автоматически заполняется")]
    public void TradeSignalTimestampTest()
    {
        var signal = new PolymarketTradeSignal
        {
            AssetId = "ts-test",
            Action = PolymarketTradeAction.Hold
        };

        Assert.That(signal.TimestampTicks, Is.GreaterThan(0));
    }

    [TestCase(TestName = "TradeAction: все значения определены")]
    public void TradeActionEnumTest()
    {
        var values = Enum.GetValues<PolymarketTradeAction>();
        Assert.That(values, Has.Length.EqualTo(3)); // Hold, Buy, Sell
    }

    #endregion

    #region ExportFormat Enum

    [TestCase(TestName = "ExportFormat: Csv и Json определены")]
    public void ExportFormatEnumTest()
    {
        var values = Enum.GetValues<PolymarketExportFormat>();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(values, Has.Length.EqualTo(2));
        Assert.That(values, Does.Contain(PolymarketExportFormat.Csv));
        Assert.That(values, Does.Contain(PolymarketExportFormat.Json));
    }

    #endregion
}
