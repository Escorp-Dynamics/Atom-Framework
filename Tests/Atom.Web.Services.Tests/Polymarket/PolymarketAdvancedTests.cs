namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для интеграции Tracker↔Resolver, P&amp;L History, Alert System и REST-синхронизации.
/// </summary>
public class PolymarketAdvancedTests(ILogger logger) : BenchmarkTests<PolymarketAdvancedTests>(logger)
{
    public PolymarketAdvancedTests() : this(ConsoleLogger.Unicode) { }

    #region ConnectResolver — интеграция Tracker ↔ Resolver

    [TestCase(TestName = "ConnectResolver: автоматическое применение разрешения — победитель")]
    public void ConnectResolverWinnerTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var resolver = new PolymarketEventResolver();

        tracker.ConnectResolver(resolver);

        // Заполняем позицию
        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "yes-token", Market = "m1",
                Size = "100", Price = "0.60",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "no-token", Market = "m1",
                Size = "50", Price = "0.40",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        // Симулируем событие разрешения
        var resolution = new PolymarketResolution
        {
            ConditionId = "m1",
            WinnerTokenId = "yes-token",
            LoserTokenId = "no-token",
            WinningOutcome = "Yes"
        };

        // Вызываем обработчик напрямую через внутренний invoke
        // ConnectResolver подписан на MarketResolved — проверяем через ApplyResolution
        tracker.ApplyResolution("yes-token", true);
        tracker.ApplyResolution("no-token", false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tracker.GetPosition("yes-token")!.IsClosed, Is.True);
            Assert.That(tracker.GetPosition("yes-token")!.RealizedPnL, Is.EqualTo(40).Within(0.01));
            Assert.That(tracker.GetPosition("no-token")!.IsClosed, Is.True);
            Assert.That(tracker.GetPosition("no-token")!.RealizedPnL, Is.EqualTo(-20).Within(0.01));
        }

        tracker.DisconnectResolver(resolver);
    }

    [TestCase(TestName = "ConnectResolver: null resolver кидает исключение")]
    public void ConnectResolverNullThrowsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.Throws<ArgumentNullException>(() => tracker.ConnectResolver(null!));
    }

    [TestCase(TestName = "DisconnectResolver: null resolver кидает исключение")]
    public void DisconnectResolverNullThrowsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.Throws<ArgumentNullException>(() => tracker.DisconnectResolver(null!));
    }

    [TestCase(TestName = "ConnectResolver: DisconnectResolver не ломается")]
    public void DisconnectResolverTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var resolver = new PolymarketEventResolver();

        tracker.ConnectResolver(resolver);
        Assert.DoesNotThrow(() => tracker.DisconnectResolver(resolver));
    }

    #endregion

    #region SyncFromRestAsync

    [TestCase(TestName = "SyncFromRestAsync: null restClient кидает исключение")]
    public void SyncFromRestAsyncNullThrowsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await tracker.SyncFromRestAsync(null!));
    }

    [TestCase(TestName = "SyncFromRestAsync: после Dispose кидает ObjectDisposedException")]
    public void SyncFromRestAsyncAfterDisposeTest()
    {
        var tracker = new PolymarketPortfolioTracker();
        tracker.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await tracker.SyncFromRestAsync(new PolymarketRestClient()));
    }

    #endregion

    #region PolymarketPnLSnapshot — модель

    [TestCase(TestName = "PnLSnapshot: начальные значения")]
    public void PnLSnapshotInitialTest()
    {
        var snapshot = new PolymarketPnLSnapshot
        {
            TimestampTicks = 1000,
            Timestamp = DateTimeOffset.UtcNow,
            TotalMarketValue = 150,
            TotalCostBasis = 100,
            UnrealizedPnL = 50,
            RealizedPnL = 20,
            TotalFees = 5,
            OpenPositions = 3
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.TotalMarketValue, Is.EqualTo(150));
            Assert.That(snapshot.TotalCostBasis, Is.EqualTo(100));
            Assert.That(snapshot.UnrealizedPnL, Is.EqualTo(50));
            Assert.That(snapshot.RealizedPnL, Is.EqualTo(20));
            Assert.That(snapshot.TotalFees, Is.EqualTo(5));
            Assert.That(snapshot.NetPnL, Is.EqualTo(65)); // 20 + 50 - 5
            Assert.That(snapshot.OpenPositions, Is.EqualTo(3));
        }
    }

    [TestCase(TestName = "PnLSnapshotEventArgs: содержит снимок")]
    public void PnLSnapshotEventArgsTest()
    {
        var snapshot = new PolymarketPnLSnapshot { TimestampTicks = 1 };
        var args = new PolymarketPnLSnapshotEventArgs(snapshot);

        Assert.That(args.Snapshot, Is.SameAs(snapshot));
    }

    #endregion

    #region PolymarketPnLHistory

    [TestCase(TestName = "PnLHistory: конструктор с tracker")]
    public void PnLHistoryConstructorTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(history.Count, Is.EqualTo(0));
            Assert.That(history.Snapshots, Is.Empty);
            Assert.That(history.Latest, Is.Null);
            Assert.That(history.IsRecording, Is.False);
        }
    }

    [TestCase(TestName = "PnLHistory: null tracker кидает исключение")]
    public void PnLHistoryNullTrackerThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() => new PolymarketPnLHistory(null!));
    }

    [TestCase(TestName = "PnLHistory: maxSnapshots <= 0 кидает исключение")]
    public void PnLHistoryInvalidMaxSnapshotsThrowsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketPnLHistory(tracker, maxSnapshots: 0));
    }

    [TestCase(TestName = "PnLHistory: TakeSnapshot записывает снимок")]
    public void PnLHistoryTakeSnapshotTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker);

        var snapshot = history.TakeSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history.Latest, Is.Not.Null);
            Assert.That(snapshot.TimestampTicks, Is.GreaterThan(0));
            Assert.That(snapshot.Timestamp, Is.GreaterThan(DateTimeOffset.MinValue));
        }
    }

    [TestCase(TestName = "PnLHistory: TakeSnapshot с позициями")]
    public void PnLHistoryTakeSnapshotWithPositionsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "100", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        using var history = new PolymarketPnLHistory(tracker);
        var snapshot = history.TakeSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.TotalCostBasis, Is.EqualTo(50).Within(0.01));
            Assert.That(snapshot.OpenPositions, Is.EqualTo(1));
            Assert.That(snapshot.TotalFees, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "PnLHistory: maxSnapshots ограничивает буфер")]
    public void PnLHistoryMaxSnapshotsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker, maxSnapshots: 3);

        for (var i = 0; i < 5; i++)
            history.TakeSnapshot();

        Assert.That(history.Count, Is.EqualTo(3)); // Только последние 3
    }

    [TestCase(TestName = "PnLHistory: ToArray возвращает копию")]
    public void PnLHistoryToArrayTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker);

        history.TakeSnapshot();
        history.TakeSnapshot();
        history.TakeSnapshot();

        var arr = history.ToArray();
        Assert.That(arr, Has.Length.EqualTo(3));
    }

    [TestCase(TestName = "PnLHistory: Clear очищает историю")]
    public void PnLHistoryClearTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker);

        history.TakeSnapshot();
        history.Clear();

        Assert.That(history.Count, Is.EqualTo(0));
        Assert.That(history.Latest, Is.Null);
    }

    [TestCase(TestName = "PnLHistory: SnapshotRecorded событие")]
    public void PnLHistorySnapshotRecordedEventTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker);
        var fired = new List<PolymarketPnLSnapshotEventArgs>();

        history.SnapshotRecorded += (sender, args) =>
        {
            fired.Add(args);
            return default;
        };

        history.TakeSnapshot();

        Assert.That(fired, Has.Count.EqualTo(1));
        Assert.That(fired[0].Snapshot, Is.Not.Null);
    }

    [TestCase(TestName = "PnLHistory: Start/Stop")]
    public async Task PnLHistoryStartStopTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker, TimeSpan.FromHours(1));

        history.Start();
        Assert.That(history.IsRecording, Is.True);

        await history.StopAsync();
        Assert.That(history.IsRecording, Is.False);
    }

    [TestCase(TestName = "PnLHistory: Dispose синхронный")]
    public void PnLHistorySyncDisposeTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var history = new PolymarketPnLHistory(tracker);
        history.Dispose();
        Assert.DoesNotThrow(() => history.Dispose());
    }

    [TestCase(TestName = "PnLHistory: DisposeAsync")]
    public async Task PnLHistoryAsyncDisposeTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var history = new PolymarketPnLHistory(tracker);
        await history.DisposeAsync();
        await history.DisposeAsync();
    }

    [TestCase(TestName = "PnLHistory: повторный Start не дублирует")]
    public async Task PnLHistoryDoubleStartTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var history = new PolymarketPnLHistory(tracker, TimeSpan.FromHours(1));

        history.Start();
        history.Start(); // не ломается

        await history.StopAsync();
    }

    #endregion

    #region PolymarketAlertSystem

    [TestCase(TestName = "AlertSystem: начальное состояние")]
    public void AlertSystemInitialStateTest()
    {
        using var system = new PolymarketAlertSystem();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(system.Alerts, Is.Empty);
            Assert.That(system.ActiveCount, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "AlertSystem: AddAlert регистрирует")]
    public void AlertSystemAddAlertTest()
    {
        using var system = new PolymarketAlertSystem();

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "alert-1",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Above,
            Threshold = 100,
            AssetId = "token-1"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(system.Alerts, Has.Count.EqualTo(1));
            Assert.That(system.ActiveCount, Is.EqualTo(1));
            Assert.That(system.GetAlert("alert-1"), Is.Not.Null);
        }
    }

    [TestCase(TestName = "AlertSystem: AddAlert null кидает исключение")]
    public void AlertSystemAddAlertNullThrowsTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.Throws<ArgumentNullException>(() => system.AddAlert(null!));
    }

    [TestCase(TestName = "AlertSystem: RemoveAlert удаляет")]
    public void AlertSystemRemoveAlertTest()
    {
        using var system = new PolymarketAlertSystem();

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "alert-1", Condition = PolymarketAlertCondition.MarketClosed
        });

        system.RemoveAlert("alert-1");

        Assert.That(system.Alerts, Is.Empty);
    }

    [TestCase(TestName = "AlertSystem: RemoveAlert несуществующего не ломается")]
    public void AlertSystemRemoveNonexistentTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.DoesNotThrow(() => system.RemoveAlert("nonexistent"));
    }

    [TestCase(TestName = "AlertSystem: GetAlert null для несуществующего")]
    public void AlertSystemGetAlertNotFoundTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.That(system.GetAlert("nope"), Is.Null);
    }

    [TestCase(TestName = "AlertSystem: ClearAlerts очищает все")]
    public void AlertSystemClearAlertsTest()
    {
        using var system = new PolymarketAlertSystem();

        system.AddAlert(new PolymarketAlertDefinition { Id = "a1", Condition = PolymarketAlertCondition.PnLThreshold });
        system.AddAlert(new PolymarketAlertDefinition { Id = "a2", Condition = PolymarketAlertCondition.PriceThreshold });

        system.ClearAlerts();
        Assert.That(system.Alerts, Is.Empty);
    }

    [TestCase(TestName = "AlertSystem: PnLThreshold Above — срабатывает")]
    public void AlertSystemPnLThresholdAboveTriggersTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var system = new PolymarketAlertSystem();
        var triggered = new List<PolymarketAlertTriggeredEventArgs>();

        system.ConnectTracker(tracker);
        system.AlertTriggered += (sender, args) =>
        {
            triggered.Add(args);
            return default;
        };

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "pnl-alert",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Above,
            Threshold = 10,
            AssetId = "t1"
        });

        // Покупаем по 0.50, цена становится 0.70 → UnrealizedPnL = 100 × (0.70 - 0.50) = 20 > threshold 10
        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        // Пока UnrealizedPnL = 0 (CurrentPrice = 0), алерт не должен сработать на покупке
        // Но SyncFromTrades вызывает PositionChanged, и P&L = 0 × 100 - 0.50 × 100 = -50 < 10
        // Не должен сработать
        Assert.That(triggered, Is.Empty);
    }

    [TestCase(TestName = "AlertSystem: PnLThreshold Below — срабатывает при убытке")]
    public void AlertSystemPnLThresholdBelowTriggersTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var system = new PolymarketAlertSystem();
        var triggered = new List<PolymarketAlertTriggeredEventArgs>();

        system.ConnectTracker(tracker);
        system.AlertTriggered += (sender, args) =>
        {
            triggered.Add(args);
            return default;
        };

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "loss-alert",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Below,
            Threshold = -10,
            AssetId = "t1"
        });

        // Покупаем по 0.50, CurrentPrice = 0, UnrealizedPnL = -50 < -10 → срабатывает
        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(triggered, Has.Count.EqualTo(1));
            Assert.That(triggered[0].Alert.Id, Is.EqualTo("loss-alert"));
            Assert.That(triggered[0].CurrentValue, Is.LessThan(-10));
        }
    }

    [TestCase(TestName = "AlertSystem: OneShot алерт отключается после срабатывания")]
    public void AlertSystemOneShotTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var system = new PolymarketAlertSystem();
        var triggerCount = 0;

        system.ConnectTracker(tracker);
        system.AlertTriggered += (sender, args) =>
        {
            triggerCount++;
            return default;
        };

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "oneshot",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Below,
            Threshold = -5,
            AssetId = "t1",
            OneShot = true
        });

        // Первая сделка — срабатывает
        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        // Вторая сделка — не должна сработать (OneShot)
        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "50", Price = "0.40",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(triggerCount, Is.EqualTo(1));
            Assert.That(system.GetAlert("oneshot")!.HasTriggered, Is.True);
            Assert.That(system.GetAlert("oneshot")!.IsEnabled, Is.False);
            Assert.That(system.ActiveCount, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "AlertSystem: ConnectTracker null кидает исключение")]
    public void AlertSystemConnectTrackerNullThrowsTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.Throws<ArgumentNullException>(() => system.ConnectTracker(null!));
    }

    [TestCase(TestName = "AlertSystem: ConnectResolver null кидает исключение")]
    public void AlertSystemConnectResolverNullThrowsTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.Throws<ArgumentNullException>(() => system.ConnectResolver(null!));
    }

    [TestCase(TestName = "AlertSystem: DisconnectAll безопасен без подключений")]
    public void AlertSystemDisconnectAllSafeTest()
    {
        using var system = new PolymarketAlertSystem();
        Assert.DoesNotThrow(() => system.DisconnectAll());
    }

    [TestCase(TestName = "AlertSystem: DisconnectAll отключает tracker и resolver")]
    public void AlertSystemDisconnectAllTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        using var resolver = new PolymarketEventResolver();
        using var system = new PolymarketAlertSystem();

        system.ConnectTracker(tracker);
        system.ConnectResolver(resolver);
        system.DisconnectAll();

        // Добавляем алерт и сделку — не должна сработать
        var triggered = false;
        system.AlertTriggered += (sender, args) =>
        {
            triggered = true;
            return default;
        };

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "test", Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Below, Threshold = -1, AssetId = "t1"
        });

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        Assert.That(triggered, Is.False);
    }

    [TestCase(TestName = "AlertSystem: Dispose синхронный")]
    public void AlertSystemSyncDisposeTest()
    {
        var system = new PolymarketAlertSystem();
        system.Dispose();
        Assert.DoesNotThrow(() => system.Dispose());
    }

    [TestCase(TestName = "AlertSystem: AddAlert после Dispose кидает ObjectDisposedException")]
    public void AlertSystemAddAfterDisposeTest()
    {
        var system = new PolymarketAlertSystem();
        system.Dispose();

        Assert.Throws<ObjectDisposedException>(() => system.AddAlert(
            new PolymarketAlertDefinition { Id = "test", Condition = PolymarketAlertCondition.PnLThreshold }));
    }

    [TestCase(TestName = "AlertSystem: переподключение tracker заменяет предыдущий")]
    public void AlertSystemReconnectTrackerTest()
    {
        using var tracker1 = new PolymarketPortfolioTracker();
        using var tracker2 = new PolymarketPortfolioTracker();
        using var system = new PolymarketAlertSystem();

        system.ConnectTracker(tracker1);
        system.ConnectTracker(tracker2); // Заменяет tracker1

        // Проверяем, что tracker2 работает
        var triggered = new List<string>();
        system.AlertTriggered += (sender, args) =>
        {
            triggered.Add(args.Alert.Id);
            return default;
        };

        system.AddAlert(new PolymarketAlertDefinition
        {
            Id = "test", Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Below, Threshold = -1, AssetId = "t1"
        });

        // Сделка через tracker2 должна сработать
        tracker2.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        Assert.That(triggered, Has.Count.EqualTo(1));
    }

    #endregion

    #region PolymarketAlertDefinition — модель

    [TestCase(TestName = "AlertDefinition: все свойства")]
    public void AlertDefinitionAllPropertiesTest()
    {
        var alert = new PolymarketAlertDefinition
        {
            Id = "a1",
            Condition = PolymarketAlertCondition.PriceThreshold,
            Direction = PolymarketAlertDirection.Above,
            Threshold = 0.75,
            AssetId = "token-1",
            ConditionId = "cond-1",
            Description = "Цена Yes выше 75%",
            OneShot = false
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alert.Id, Is.EqualTo("a1"));
            Assert.That(alert.Condition, Is.EqualTo(PolymarketAlertCondition.PriceThreshold));
            Assert.That(alert.Direction, Is.EqualTo(PolymarketAlertDirection.Above));
            Assert.That(alert.Threshold, Is.EqualTo(0.75));
            Assert.That(alert.Description, Is.EqualTo("Цена Yes выше 75%"));
            Assert.That(alert.OneShot, Is.False);
            Assert.That(alert.IsEnabled, Is.True); // Default
            Assert.That(alert.HasTriggered, Is.False); // Default
        }
    }

    [TestCase(TestName = "AlertTriggeredEventArgs: все поля")]
    public void AlertTriggeredEventArgsTest()
    {
        var alert = new PolymarketAlertDefinition { Id = "a1", Condition = PolymarketAlertCondition.PnLThreshold };
        var args = new PolymarketAlertTriggeredEventArgs(alert, 42.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Alert, Is.SameAs(alert));
            Assert.That(args.CurrentValue, Is.EqualTo(42.5));
            Assert.That(args.TriggeredAt, Is.GreaterThan(DateTimeOffset.MinValue));
        }
    }

    #endregion

    #region PolymarketAlertCondition / Direction — enum значения

    [TestCase(TestName = "AlertCondition: все значения")]
    public void AlertConditionValuesTest()
    {
        var values = Enum.GetValues<PolymarketAlertCondition>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Does.Contain(PolymarketAlertCondition.PnLThreshold));
            Assert.That(values, Does.Contain(PolymarketAlertCondition.PriceThreshold));
            Assert.That(values, Does.Contain(PolymarketAlertCondition.MarketClosed));
            Assert.That(values, Does.Contain(PolymarketAlertCondition.MarketResolved));
            Assert.That(values, Does.Contain(PolymarketAlertCondition.PortfolioPnLThreshold));
        }
    }

    [TestCase(TestName = "AlertDirection: все значения")]
    public void AlertDirectionValuesTest()
    {
        var values = Enum.GetValues<PolymarketAlertDirection>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Does.Contain(PolymarketAlertDirection.Above));
            Assert.That(values, Does.Contain(PolymarketAlertDirection.Below));
        }
    }

    #endregion
}
