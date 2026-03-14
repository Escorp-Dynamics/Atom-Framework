using System.Globalization;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для Portfolio Tracker и Event Resolver Polymarket.
/// </summary>
public class PolymarketPortfolioResolverTests(ILogger logger) : BenchmarkTests<PolymarketPortfolioResolverTests>(logger)
{
    public PolymarketPortfolioResolverTests() : this(ConsoleLogger.Unicode) { }

    #region PolymarketPosition — модель

    [TestCase(TestName = "Position: начальные значения")]
    public void PositionInitialValuesTest()
    {
        var pos = new PolymarketPosition
        {
            AssetId = "token-1",
            Market = "market-1",
            Outcome = "Yes"
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos.AssetId, Is.EqualTo("token-1"));
            Assert.That(pos.Quantity, Is.EqualTo(0));
            Assert.That(pos.AverageCostBasis, Is.EqualTo(0));
            Assert.That(pos.TotalCost, Is.EqualTo(0));
            Assert.That(pos.MarketValue, Is.EqualTo(0));
            Assert.That(pos.UnrealizedPnL, Is.EqualTo(0));
            Assert.That(pos.RealizedPnL, Is.EqualTo(0));
            Assert.That(pos.IsClosed, Is.True);
            Assert.That(pos.TradeCount, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "Position: TotalCost = Quantity × AverageCostBasis")]
    public void PositionTotalCostTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        pos.Quantity = 100;
        pos.AverageCostBasis = 0.55;

        Assert.That(pos.TotalCost, Is.EqualTo(55).Within(0.001));
    }

    [TestCase(TestName = "Position: MarketValue = Quantity × CurrentPrice")]
    public void PositionMarketValueTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        pos.Quantity = 100;
        pos.CurrentPrice = 0.65;

        Assert.That(pos.MarketValue, Is.EqualTo(65).Within(0.001));
    }

    [TestCase(TestName = "Position: UnrealizedPnL = MarketValue - TotalCost")]
    public void PositionUnrealizedPnLTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        pos.Quantity = 100;
        pos.AverageCostBasis = 0.50;
        pos.CurrentPrice = 0.70;

        Assert.That(pos.UnrealizedPnL, Is.EqualTo(20).Within(0.001));
    }

    [TestCase(TestName = "Position: UnrealizedPnLPercent при отрицательном P&L")]
    public void PositionUnrealizedPnLPercentNegativeTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        pos.Quantity = 100;
        pos.AverageCostBasis = 0.80;
        pos.CurrentPrice = 0.60;

        // P&L = 60 - 80 = -20, Percent = -20/80 × 100 = -25%
        Assert.That(pos.UnrealizedPnLPercent, Is.EqualTo(-25).Within(0.1));
    }

    [TestCase(TestName = "Position: UnrealizedPnLPercent = 0 когда TotalCost = 0")]
    public void PositionUnrealizedPnLPercentZeroCostTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        Assert.That(pos.UnrealizedPnLPercent, Is.EqualTo(0));
    }

    [TestCase(TestName = "Position: IsClosed = true когда Quantity = 0")]
    public void PositionIsClosedTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        pos.Quantity = 0;
        Assert.That(pos.IsClosed, Is.True);

        pos.Quantity = 50;
        Assert.That(pos.IsClosed, Is.False);
    }

    #endregion

    #region PolymarketPortfolioSummary

    [TestCase(TestName = "PortfolioSummary: NetPnL = Realized + Unrealized - Fees")]
    public void PortfolioSummaryNetPnLTest()
    {
        var summary = new PolymarketPortfolioSummary
        {
            TotalRealizedPnL = 50,
            TotalUnrealizedPnL = 30,
            TotalFees = 5
        };

        Assert.That(summary.NetPnL, Is.EqualTo(75).Within(0.001));
    }

    [TestCase(TestName = "PortfolioSummary: отрицательный NetPnL")]
    public void PortfolioSummaryNegativeNetPnLTest()
    {
        var summary = new PolymarketPortfolioSummary
        {
            TotalRealizedPnL = -20,
            TotalUnrealizedPnL = -10,
            TotalFees = 3
        };

        Assert.That(summary.NetPnL, Is.EqualTo(-33).Within(0.001));
    }

    #endregion

    #region PolymarketPortfolioTracker — конструктор / dispose

    [TestCase(TestName = "PortfolioTracker: конструктор по умолчанию")]
    public void PortfolioTrackerDefaultConstructorTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tracker.Positions, Is.Empty);
            Assert.That(tracker.OpenPositionCount, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "PortfolioTracker: конструктор с существующими клиентами")]
    public void PortfolioTrackerExternalClientsTest()
    {
        using var client = new PolymarketClient();
        using var stream = new PolymarketPriceStream(client);
        using var tracker = new PolymarketPortfolioTracker(client, stream);

        Assert.That(tracker, Is.Not.Null);
    }

    [TestCase(TestName = "PortfolioTracker: null client кидает исключение")]
    public void PortfolioTrackerNullClientThrowsTest()
    {
        using var stream = new PolymarketPriceStream();
        Assert.Throws<ArgumentNullException>(() => new PolymarketPortfolioTracker(null!, stream));
    }

    [TestCase(TestName = "PortfolioTracker: null priceStream кидает исключение")]
    public void PortfolioTrackerNullStreamThrowsTest()
    {
        using var client = new PolymarketClient();
        Assert.Throws<ArgumentNullException>(() => new PolymarketPortfolioTracker(client, null!));
    }

    [TestCase(TestName = "PortfolioTracker: Dispose синхронный")]
    public void PortfolioTrackerSyncDisposeTest()
    {
        var tracker = new PolymarketPortfolioTracker();
        tracker.Dispose();
        Assert.DoesNotThrow(() => tracker.Dispose());
    }

    [TestCase(TestName = "PortfolioTracker: DisposeAsync")]
    public async Task PortfolioTrackerAsyncDisposeTest()
    {
        var tracker = new PolymarketPortfolioTracker();
        await tracker.DisposeAsync();
        await tracker.DisposeAsync(); // double dispose
    }

    #endregion

    #region PortfolioTracker — SyncFromTrades

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades с одной покупкой")]
    public void SyncFromTradesSingleBuyTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1",
                Market = "market-1",
                Size = "100",
                Price = "0.50",
                FeeRateBps = "200", // 2%
                Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var pos = tracker.GetPosition("token-1");
        Assert.That(pos, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos!.Quantity, Is.EqualTo(100));
            Assert.That(pos.AverageCostBasis, Is.EqualTo(0.50).Within(0.001));
            Assert.That(pos.TotalFees, Is.EqualTo(1.0).Within(0.01)); // 100 × 0.50 × 0.02
            Assert.That(pos.TradeCount, Is.EqualTo(1));
            Assert.That(pos.IsClosed, Is.False);
        }
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — несколько покупок с разной ценой")]
    public void SyncFromTradesMultipleBuysTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.40",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.60",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var pos = tracker.GetPosition("token-1");
        Assert.That(pos, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos!.Quantity, Is.EqualTo(200));
            // Средняя: (100×0.40 + 100×0.60) / 200 = 0.50
            Assert.That(pos.AverageCostBasis, Is.EqualTo(0.50).Within(0.001));
        }
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — покупка + продажа")]
    public void SyncFromTradesBuyAndSellTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "50", Price = "0.70",
                FeeRateBps = "0", Side = PolymarketSide.Sell,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var pos = tracker.GetPosition("token-1");
        Assert.That(pos, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos!.Quantity, Is.EqualTo(50));
            Assert.That(pos.AverageCostBasis, Is.EqualTo(0.50).Within(0.001));
            // Realized P&L: 50 × (0.70 - 0.50) = 10
            Assert.That(pos.RealizedPnL, Is.EqualTo(10).Within(0.01));
        }
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — продажа больше чем есть (клампит)")]
    public void SyncFromTradesSellMoreThanOwnedTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "200", Price = "0.80",
                FeeRateBps = "0", Side = PolymarketSide.Sell,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var pos = tracker.GetPosition("token-1");
        Assert.That(pos!.Quantity, Is.EqualTo(0));
        Assert.That(pos.IsClosed, Is.True);
        // Realized: 100 × (0.80 - 0.50) = 30 (продано только 100 из запрошенных 200)
        Assert.That(pos.RealizedPnL, Is.EqualTo(30).Within(0.01));
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — пропускает неподтверждённые сделки")]
    public void SyncFromTradesSkipsUnconfirmedTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Matched // не Confirmed
            }
        ]);

        Assert.That(tracker.GetPosition("token-1"), Is.Null);
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — пропускает сделки без AssetId")]
    public void SyncFromTradesSkipsNullAssetIdTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = null, Market = "m1",
                Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        Assert.That(tracker.Positions, Is.Empty);
    }

    [TestCase(TestName = "PortfolioTracker: SyncFromTrades — null кидает исключение")]
    public void SyncFromTradesNullThrowsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.Throws<ArgumentNullException>(() => tracker.SyncFromTrades(null!));
    }

    #endregion

    #region PortfolioTracker — ApplyResolution

    [TestCase(TestName = "PortfolioTracker: ApplyResolution — победитель получает выплату")]
    public void ApplyResolutionWinnerTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "yes-token", Market = "m1",
                Size = "100", Price = "0.60",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        tracker.ApplyResolution("yes-token", isWinner: true);

        var pos = tracker.GetPosition("yes-token");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos!.IsClosed, Is.True);
            Assert.That(pos.CurrentPrice, Is.EqualTo(1.0));
            // Realized: payout(100) - cost(60) = 40
            Assert.That(pos.RealizedPnL, Is.EqualTo(40).Within(0.01));
        }
    }

    [TestCase(TestName = "PortfolioTracker: ApplyResolution — проигравший теряет всё")]
    public void ApplyResolutionLoserTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "no-token", Market = "m1",
                Size = "100", Price = "0.40",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        tracker.ApplyResolution("no-token", isWinner: false);

        var pos = tracker.GetPosition("no-token");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pos!.IsClosed, Is.True);
            Assert.That(pos.CurrentPrice, Is.EqualTo(0));
            // Realized: payout(0) - cost(40) = -40
            Assert.That(pos.RealizedPnL, Is.EqualTo(-40).Within(0.01));
        }
    }

    [TestCase(TestName = "PortfolioTracker: ApplyResolution — несуществующий токен игнорируется")]
    public void ApplyResolutionUnknownTokenTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        Assert.DoesNotThrow(() => tracker.ApplyResolution("nonexistent", true));
    }

    [TestCase(TestName = "PortfolioTracker: ApplyResolution — закрытая позиция игнорируется")]
    public void ApplyResolutionClosedPositionTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy,
                Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "token-1", Market = "m1",
                Size = "100", Price = "0.70",
                FeeRateBps = "0", Side = PolymarketSide.Sell,
                Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        // Позиция уже закрыта
        var pnlBefore = tracker.GetPosition("token-1")!.RealizedPnL;
        tracker.ApplyResolution("token-1", true);
        Assert.That(tracker.GetPosition("token-1")!.RealizedPnL, Is.EqualTo(pnlBefore));
    }

    #endregion

    #region PortfolioTracker — GetSummary

    [TestCase(TestName = "PortfolioTracker: GetSummary — пустой портфель")]
    public void GetSummaryEmptyTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var summary = tracker.GetSummary();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.OpenPositions, Is.EqualTo(0));
            Assert.That(summary.ClosedPositions, Is.EqualTo(0));
            Assert.That(summary.NetPnL, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "PortfolioTracker: GetSummary — аккумулирует позиции")]
    public void GetSummaryWithPositionsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "100", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            },
            new PolymarketTrade
            {
                AssetId = "t2", Market = "m2", Size = "200", Price = "0.30",
                FeeRateBps = "100", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var summary = tracker.GetSummary();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.OpenPositions, Is.EqualTo(2));
            Assert.That(summary.TotalCostBasis, Is.EqualTo(110).Within(0.01)); // 50 + 60
            Assert.That(summary.TotalFees, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "PortfolioTracker: ClearPositions очищает портфель")]
    public void ClearPositionsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        tracker.ClearPositions();
        Assert.That(tracker.Positions, Is.Empty);
    }

    #endregion

    #region PolymarketPositionChangedEventArgs

    [TestCase(TestName = "PositionChangedEventArgs: содержит позицию и причину")]
    public void PositionChangedEventArgsTest()
    {
        var pos = new PolymarketPosition { AssetId = "a1" };
        var args = new PolymarketPositionChangedEventArgs(pos, PolymarketPositionChangeReason.Trade);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Position, Is.SameAs(pos));
            Assert.That(args.Reason, Is.EqualTo(PolymarketPositionChangeReason.Trade));
        }
    }

    #endregion

    #region PolymarketEventResolver — конструктор / dispose

    [TestCase(TestName = "EventResolver: конструктор по умолчанию")]
    public void EventResolverDefaultConstructorTest()
    {
        using var resolver = new PolymarketEventResolver();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver.TrackedMarkets, Is.Empty);
            Assert.That(resolver.TrackedCount, Is.EqualTo(0));
            Assert.That(resolver.IsPolling, Is.False);
        }
    }

    [TestCase(TestName = "EventResolver: конструктор с REST клиентом")]
    public void EventResolverExternalClientTest()
    {
        using var client = new PolymarketRestClient();
        using var resolver = new PolymarketEventResolver(client, TimeSpan.FromSeconds(30));

        Assert.That(resolver, Is.Not.Null);
    }

    [TestCase(TestName = "EventResolver: null client кидает исключение")]
    public void EventResolverNullClientThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() => new PolymarketEventResolver(null!));
    }

    [TestCase(TestName = "EventResolver: Dispose синхронный")]
    public void EventResolverSyncDisposeTest()
    {
        var resolver = new PolymarketEventResolver();
        resolver.Dispose();
        Assert.DoesNotThrow(() => resolver.Dispose());
    }

    [TestCase(TestName = "EventResolver: DisposeAsync")]
    public async Task EventResolverAsyncDisposeTest()
    {
        var resolver = new PolymarketEventResolver();
        await resolver.DisposeAsync();
        await resolver.DisposeAsync(); // double dispose
    }

    #endregion

    #region EventResolver — Track / Untrack

    [TestCase(TestName = "EventResolver: Track добавляет рынок")]
    public void EventResolverTrackTest()
    {
        using var resolver = new PolymarketEventResolver();

        resolver.Track("condition-1");
        resolver.Track("condition-2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver.TrackedCount, Is.EqualTo(2));
            Assert.That(resolver.TrackedMarkets.ContainsKey("condition-1"), Is.True);
            Assert.That(resolver.TrackedMarkets.ContainsKey("condition-2"), Is.True);
        }
    }

    [TestCase(TestName = "EventResolver: Track дубликат не добавляется")]
    public void EventResolverTrackDuplicateTest()
    {
        using var resolver = new PolymarketEventResolver();

        resolver.Track("condition-1");
        resolver.Track("condition-1");

        Assert.That(resolver.TrackedCount, Is.EqualTo(1));
    }

    [TestCase(TestName = "EventResolver: Track пустого ID кидает исключение")]
    public void EventResolverTrackEmptyThrowsTest()
    {
        using var resolver = new PolymarketEventResolver();
        Assert.Throws<ArgumentException>(() => resolver.Track(""));
    }

    [TestCase(TestName = "EventResolver: TrackMany добавляет несколько")]
    public void EventResolverTrackManyTest()
    {
        using var resolver = new PolymarketEventResolver();

        resolver.TrackMany(["c1", "c2", "c3"]);

        Assert.That(resolver.TrackedCount, Is.EqualTo(3));
    }

    [TestCase(TestName = "EventResolver: Untrack удаляет рынок")]
    public void EventResolverUntrackTest()
    {
        using var resolver = new PolymarketEventResolver();

        resolver.Track("condition-1");
        resolver.Untrack("condition-1");

        Assert.That(resolver.TrackedCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "EventResolver: Untrack несуществующего не ломается")]
    public void EventResolverUntrackNonexistentTest()
    {
        using var resolver = new PolymarketEventResolver();
        Assert.DoesNotThrow(() => resolver.Untrack("nonexistent"));
    }

    [TestCase(TestName = "EventResolver: ClearTracked очищает все")]
    public void EventResolverClearTrackedTest()
    {
        using var resolver = new PolymarketEventResolver();
        resolver.TrackMany(["c1", "c2", "c3"]);

        resolver.ClearTracked();

        Assert.That(resolver.TrackedCount, Is.EqualTo(0));
    }

    #endregion

    #region EventResolver — GetMarketStatus

    [TestCase(TestName = "EventResolver: GetMarketStatus — неизвестный рынок")]
    public void GetMarketStatusUnknownTest()
    {
        using var resolver = new PolymarketEventResolver();
        Assert.That(resolver.GetMarketStatus("unknown"), Is.EqualTo(PolymarketMarketStatus.Unknown));
    }

    [TestCase(TestName = "EventResolver: GetMarketStatus — Active")]
    public void GetMarketStatusActiveTest()
    {
        using var resolver = new PolymarketEventResolver();
        resolver.Track("c1");
        Assert.That(resolver.GetMarketStatus("c1"), Is.EqualTo(PolymarketMarketStatus.Active));
    }

    [TestCase(TestName = "EventResolver: GetMarketStatus — Closed")]
    public void GetMarketStatusClosedTest()
    {
        using var resolver = new PolymarketEventResolver();
        resolver.Track("c1");
        resolver.TrackedMarkets["c1"].IsClosed = true;

        Assert.That(resolver.GetMarketStatus("c1"), Is.EqualTo(PolymarketMarketStatus.Closed));
    }

    [TestCase(TestName = "EventResolver: GetMarketStatus — Resolved")]
    public void GetMarketStatusResolvedTest()
    {
        using var resolver = new PolymarketEventResolver();
        resolver.Track("c1");
        resolver.TrackedMarkets["c1"].IsResolved = true;

        Assert.That(resolver.GetMarketStatus("c1"), Is.EqualTo(PolymarketMarketStatus.Resolved));
    }

    #endregion

    #region EventResolver — StartPolling / StopPolling

    [TestCase(TestName = "EventResolver: StartPolling → IsPolling = true")]
    public async Task EventResolverStartPollingTest()
    {
        using var resolver = new PolymarketEventResolver(TimeSpan.FromHours(1)); // большой интервал

        resolver.StartPolling();
        Assert.That(resolver.IsPolling, Is.True);

        await resolver.StopPollingAsync();
        // После остановки может занять мгновение
        Assert.That(resolver.IsPolling, Is.False);
    }

    [TestCase(TestName = "EventResolver: повторный StartPolling не дублирует")]
    public async Task EventResolverDoubleStartTest()
    {
        using var resolver = new PolymarketEventResolver(TimeSpan.FromHours(1));

        resolver.StartPolling();
        resolver.StartPolling(); // не должен создать второй цикл

        await resolver.StopPollingAsync();
    }

    [TestCase(TestName = "EventResolver: StopPolling без Start не ломается")]
    public async Task EventResolverStopWithoutStartTest()
    {
        using var resolver = new PolymarketEventResolver();
        await resolver.StopPollingAsync();
    }

    [TestCase(TestName = "EventResolver: Track после Dispose кидает ObjectDisposedException")]
    public void EventResolverTrackAfterDisposeTest()
    {
        var resolver = new PolymarketEventResolver();
        resolver.Dispose();

        Assert.Throws<ObjectDisposedException>(() => resolver.Track("c1"));
    }

    #endregion

    #region PolymarketResolution — модель

    [TestCase(TestName = "Resolution: все свойства")]
    public void ResolutionAllPropertiesTest()
    {
        var resolution = new PolymarketResolution
        {
            ConditionId = "cond-1",
            Question = "Will X happen?",
            WinningOutcome = "Yes",
            WinnerTokenId = "token-yes",
            LoserTokenId = "token-no",
            NegRisk = true,
            ResolvedAtTicks = 12345,
            IsVoided = false
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.ConditionId, Is.EqualTo("cond-1"));
            Assert.That(resolution.Question, Is.EqualTo("Will X happen?"));
            Assert.That(resolution.WinningOutcome, Is.EqualTo("Yes"));
            Assert.That(resolution.WinnerTokenId, Is.EqualTo("token-yes"));
            Assert.That(resolution.LoserTokenId, Is.EqualTo("token-no"));
            Assert.That(resolution.NegRisk, Is.True);
            Assert.That(resolution.IsVoided, Is.False);
        }
    }

    [TestCase(TestName = "MarketResolvedEventArgs: содержит Resolution")]
    public void MarketResolvedEventArgsTest()
    {
        var resolution = new PolymarketResolution { ConditionId = "c1" };
        var args = new PolymarketMarketResolvedEventArgs(resolution);

        Assert.That(args.Resolution, Is.SameAs(resolution));
    }

    [TestCase(TestName = "MarketClosedEventArgs: содержит Market")]
    public void MarketClosedEventArgsTest()
    {
        var market = new PolymarketMarket { ConditionId = "c1" };
        var args = new PolymarketMarketClosedEventArgs(market);

        Assert.That(args.Market, Is.SameAs(market));
    }

    [TestCase(TestName = "TrackedMarket: начальные значения")]
    public void TrackedMarketInitialValuesTest()
    {
        var tracked = new PolymarketTrackedMarket { ConditionId = "c1" };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tracked.ConditionId, Is.EqualTo("c1"));
            Assert.That(tracked.IsClosed, Is.False);
            Assert.That(tracked.IsResolved, Is.False);
            Assert.That(tracked.NegRisk, Is.False);
        }
    }

    #endregion

    #region PolymarketPositionChangeReason / PolymarketMarketStatus — enum значения

    [TestCase(TestName = "PositionChangeReason: все значения")]
    public void PositionChangeReasonValuesTest()
    {
        var values = Enum.GetValues<PolymarketPositionChangeReason>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Does.Contain(PolymarketPositionChangeReason.Trade));
            Assert.That(values, Does.Contain(PolymarketPositionChangeReason.PriceUpdate));
            Assert.That(values, Does.Contain(PolymarketPositionChangeReason.MarketResolved));
            Assert.That(values, Does.Contain(PolymarketPositionChangeReason.ManualSync));
        }
    }

    [TestCase(TestName = "MarketStatus: все значения")]
    public void MarketStatusValuesTest()
    {
        var values = Enum.GetValues<PolymarketMarketStatus>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Does.Contain(PolymarketMarketStatus.Active));
            Assert.That(values, Does.Contain(PolymarketMarketStatus.Closed));
            Assert.That(values, Does.Contain(PolymarketMarketStatus.Resolved));
            Assert.That(values, Does.Contain(PolymarketMarketStatus.Voided));
            Assert.That(values, Does.Contain(PolymarketMarketStatus.Unknown));
        }
    }

    #endregion

    #region PortfolioTracker — PositionChanged event через SyncFromTrades

    [TestCase(TestName = "PortfolioTracker: PositionChanged срабатывает при SyncFromTrades")]
    public void PositionChangedEventFiresTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var changes = new List<PolymarketPositionChangedEventArgs>();

        tracker.PositionChanged += (sender, args) =>
        {
            changes.Add(args);
            return default;
        };

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
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Reason, Is.EqualTo(PolymarketPositionChangeReason.ManualSync));
            Assert.That(changes[0].Position.AssetId, Is.EqualTo("t1"));
        }
    }

    [TestCase(TestName = "PortfolioTracker: PositionChanged при ApplyResolution")]
    public void PositionChangedEventOnResolutionTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var changes = new List<PolymarketPositionChangedEventArgs>();

        tracker.PositionChanged += (sender, args) =>
        {
            changes.Add(args);
            return default;
        };

        tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        changes.Clear();
        tracker.ApplyResolution("t1", true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Reason, Is.EqualTo(PolymarketPositionChangeReason.MarketResolved));
        }
    }

    #endregion
}
