namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для мульти-портфельного менеджера Polymarket.
/// </summary>
public class PolymarketPortfolioManagerTests(ILogger logger) : BenchmarkTests<PolymarketPortfolioManagerTests>(logger)
{
    public PolymarketPortfolioManagerTests() : this(ConsoleLogger.Unicode) { }

    #region PolymarketPortfolioManager — конструктор / dispose

    [TestCase(TestName = "PortfolioManager: конструктор по умолчанию")]
    public void ManagerDefaultConstructorTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mgr.Portfolios, Is.Empty);
            Assert.That(mgr.Count, Is.EqualTo(0));
            Assert.That(mgr.Client, Is.Not.Null);
            Assert.That(mgr.PriceStream, Is.Not.Null);
            Assert.That(mgr.Resolver, Is.Not.Null);
            Assert.That(mgr.AlertSystem, Is.Not.Null);
        }
    }

    [TestCase(TestName = "PortfolioManager: конструктор с внешней инфраструктурой")]
    public void ManagerExternalInfraTest()
    {
        using var client = new PolymarketClient();
        using var stream = new PolymarketPriceStream(client);
        using var resolver = new PolymarketEventResolver();
        using var alerts = new PolymarketAlertSystem();
        using var mgr = new PolymarketPortfolioManager(client, stream, resolver, alerts);

        Assert.That(mgr.Client, Is.SameAs(client));
        Assert.That(mgr.PriceStream, Is.SameAs(stream));
    }

    [TestCase(TestName = "PortfolioManager: null client кидает исключение")]
    public void ManagerNullClientThrowsTest()
    {
        using var stream = new PolymarketPriceStream();
        using var resolver = new PolymarketEventResolver();
        using var alerts = new PolymarketAlertSystem();

        Assert.Throws<ArgumentNullException>(() =>
            new PolymarketPortfolioManager(null!, stream, resolver, alerts));
    }

    [TestCase(TestName = "PortfolioManager: null priceStream кидает исключение")]
    public void ManagerNullStreamThrowsTest()
    {
        using var client = new PolymarketClient();
        using var resolver = new PolymarketEventResolver();
        using var alerts = new PolymarketAlertSystem();

        Assert.Throws<ArgumentNullException>(() =>
            new PolymarketPortfolioManager(client, null!, resolver, alerts));
    }

    [TestCase(TestName = "PortfolioManager: Dispose синхронный")]
    public void ManagerSyncDisposeTest()
    {
        var mgr = new PolymarketPortfolioManager();
        mgr.Dispose();
        Assert.DoesNotThrow(() => mgr.Dispose());
    }

    [TestCase(TestName = "PortfolioManager: DisposeAsync")]
    public async Task ManagerAsyncDisposeTest()
    {
        var mgr = new PolymarketPortfolioManager();
        await mgr.DisposeAsync();
        await mgr.DisposeAsync();
    }

    #endregion

    #region CreatePortfolio

    [TestCase(TestName = "CreatePortfolio: создаёт портфель")]
    public void CreatePortfolioTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        var profile = mgr.CreatePortfolio("p1", "Основной", strategy: "Momentum");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mgr.Count, Is.EqualTo(1));
            Assert.That(profile.Id, Is.EqualTo("p1"));
            Assert.That(profile.Name, Is.EqualTo("Основной"));
            Assert.That(profile.Strategy, Is.EqualTo("Momentum"));
            Assert.That(profile.Tracker, Is.Not.Null);
            Assert.That(profile.CreatedAt, Is.GreaterThan(DateTimeOffset.MinValue));
        }
    }

    [TestCase(TestName = "CreatePortfolio: с тегами")]
    public void CreatePortfolioWithTagsTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        var profile = mgr.CreatePortfolio("p1", "Тест", tags: ["crypto", "politics"]);

        Assert.That(profile.Tags, Has.Length.EqualTo(2));
    }

    [TestCase(TestName = "CreatePortfolio: с P&L историей")]
    public void CreatePortfolioWithPnLHistoryTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        var profile = mgr.CreatePortfolio("p1", "Тест", enablePnLHistory: true);

        Assert.That(profile.PnLHistory, Is.Not.Null);
        Assert.That(profile.PnLHistory!.IsRecording, Is.True);
    }

    [TestCase(TestName = "CreatePortfolio: дубликат ID кидает исключение")]
    public void CreatePortfolioDuplicateIdThrowsTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        mgr.CreatePortfolio("p1", "Первый");

        Assert.Throws<InvalidOperationException>(() =>
            mgr.CreatePortfolio("p1", "Второй"));
    }

    [TestCase(TestName = "CreatePortfolio: пустой id кидает исключение")]
    public void CreatePortfolioEmptyIdThrowsTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        Assert.Throws<ArgumentException>(() => mgr.CreatePortfolio("", "Имя"));
    }

    [TestCase(TestName = "CreatePortfolio: пустое имя кидает исключение")]
    public void CreatePortfolioEmptyNameThrowsTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        Assert.Throws<ArgumentException>(() => mgr.CreatePortfolio("p1", ""));
    }

    [TestCase(TestName = "CreatePortfolio: после Dispose кидает ObjectDisposedException")]
    public void CreatePortfolioAfterDisposeTest()
    {
        var mgr = new PolymarketPortfolioManager();
        mgr.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mgr.CreatePortfolio("p1", "Тест"));
    }

    [TestCase(TestName = "CreatePortfolio: событие PortfolioAdded")]
    public void CreatePortfolioEventTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        var fired = new List<string>();

        mgr.PortfolioAdded += (sender, args) =>
        {
            fired.Add(args.Profile.Id);
            return default;
        };

        mgr.CreatePortfolio("p1", "Тест");

        Assert.That(fired, Has.Count.EqualTo(1));
        Assert.That(fired[0], Is.EqualTo("p1"));
    }

    #endregion

    #region GetPortfolio / GetPortfoliosByStrategy / GetPortfoliosByTag

    [TestCase(TestName = "GetPortfolio: возвращает профиль")]
    public void GetPortfolioTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест");

        var profile = mgr.GetPortfolio("p1");
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.Name, Is.EqualTo("Тест"));
    }

    [TestCase(TestName = "GetPortfolio: null для несуществующего")]
    public void GetPortfolioNotFoundTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        Assert.That(mgr.GetPortfolio("nonexistent"), Is.Null);
    }

    [TestCase(TestName = "GetPortfoliosByStrategy: фильтрация")]
    public void GetPortfoliosByStrategyTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Первый", strategy: "Momentum");
        mgr.CreatePortfolio("p2", "Второй", strategy: "Hedging");
        mgr.CreatePortfolio("p3", "Третий", strategy: "Momentum");

        var momentum = mgr.GetPortfoliosByStrategy("Momentum");

        Assert.That(momentum, Has.Length.EqualTo(2));
    }

    [TestCase(TestName = "GetPortfoliosByTag: фильтрация")]
    public void GetPortfoliosByTagTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Первый", tags: ["crypto", "high-risk"]);
        mgr.CreatePortfolio("p2", "Второй", tags: ["politics"]);
        mgr.CreatePortfolio("p3", "Третий", tags: ["crypto"]);

        var crypto = mgr.GetPortfoliosByTag("crypto");

        Assert.That(crypto, Has.Length.EqualTo(2));
    }

    [TestCase(TestName = "GetPortfoliosByTag: пустой результат")]
    public void GetPortfoliosByTagEmptyTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест");

        Assert.That(mgr.GetPortfoliosByTag("nonexistent"), Is.Empty);
    }

    #endregion

    #region RemovePortfolioAsync

    [TestCase(TestName = "RemovePortfolio: удаляет портфель")]
    public async Task RemovePortfolioTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест");

        await mgr.RemovePortfolioAsync("p1");

        Assert.That(mgr.Count, Is.EqualTo(0));
        Assert.That(mgr.GetPortfolio("p1"), Is.Null);
    }

    [TestCase(TestName = "RemovePortfolio: удаление несуществующего не ломается")]
    public async Task RemovePortfolioNonexistentTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        await mgr.RemovePortfolioAsync("nonexistent"); // no throw
    }

    [TestCase(TestName = "RemovePortfolio: событие PortfolioRemoved")]
    public async Task RemovePortfolioEventTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест");

        var fired = new List<string>();
        mgr.PortfolioRemoved += (sender, args) =>
        {
            fired.Add(args.Profile.Id);
            return default;
        };

        await mgr.RemovePortfolioAsync("p1");

        Assert.That(fired, Has.Count.EqualTo(1));
        Assert.That(fired[0], Is.EqualTo("p1"));
    }

    [TestCase(TestName = "RemovePortfolio: с P&L историей")]
    public async Task RemovePortfolioWithHistoryTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест", enablePnLHistory: true);

        await mgr.RemovePortfolioAsync("p1");
        Assert.That(mgr.Count, Is.EqualTo(0));
    }

    #endregion

    #region GetAggregatedSummary

    [TestCase(TestName = "GetAggregatedSummary: пустой менеджер")]
    public void AggregatedSummaryEmptyTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        var summary = mgr.GetAggregatedSummary();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.PortfolioCount, Is.EqualTo(0));
            Assert.That(summary.TotalOpenPositions, Is.EqualTo(0));
            Assert.That(summary.NetPnL, Is.EqualTo(0));
            Assert.That(summary.PerPortfolio, Is.Empty);
        }
    }

    [TestCase(TestName = "GetAggregatedSummary: несколько портфелей")]
    public void AggregatedSummaryMultipleTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        var p1 = mgr.CreatePortfolio("p1", "First", strategy: "Momentum");
        var p2 = mgr.CreatePortfolio("p2", "Second", strategy: "Hedging");

        p1.Tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t1", Market = "m1", Size = "100", Price = "0.50",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        p2.Tracker.SyncFromTrades(
        [
            new PolymarketTrade
            {
                AssetId = "t2", Market = "m2", Size = "200", Price = "0.30",
                FeeRateBps = "0", Side = PolymarketSide.Buy, Status = PolymarketTradeStatus.Confirmed
            }
        ]);

        var summary = mgr.GetAggregatedSummary();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.PortfolioCount, Is.EqualTo(2));
            Assert.That(summary.TotalOpenPositions, Is.EqualTo(2));
            Assert.That(summary.TotalCostBasis, Is.EqualTo(110).Within(0.01)); // 50 + 60
            Assert.That(summary.PerPortfolio, Has.Count.EqualTo(2));
            Assert.That(summary.PerPortfolio.ContainsKey("p1"), Is.True);
            Assert.That(summary.PerPortfolio.ContainsKey("p2"), Is.True);
        }
    }

    #endregion

    #region AddPortfolioAlert

    [TestCase(TestName = "AddPortfolioAlert: добавляет алерт")]
    public void AddPortfolioAlertTest()
    {
        using var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "Тест");

        mgr.AddPortfolioAlert("p1", new PolymarketAlertDefinition
        {
            Id = "alert-1",
            Condition = PolymarketAlertCondition.PnLThreshold,
            Direction = PolymarketAlertDirection.Below,
            Threshold = -50,
            AssetId = "t1"
        });

        Assert.That(mgr.AlertSystem.GetAlert("alert-1"), Is.Not.Null);
    }

    [TestCase(TestName = "AddPortfolioAlert: несуществующий портфель кидает исключение")]
    public void AddPortfolioAlertNotFoundThrowsTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        Assert.Throws<InvalidOperationException>(() => mgr.AddPortfolioAlert("nope",
            new PolymarketAlertDefinition { Id = "a1", Condition = PolymarketAlertCondition.PnLThreshold }));
    }

    #endregion

    #region StartResolverPolling / StopResolverPolling

    [TestCase(TestName = "StartResolverPolling: запускается")]
    public async Task StartResolverPollingTest()
    {
        using var mgr = new PolymarketPortfolioManager();

        mgr.StartResolverPolling();
        Assert.That(mgr.Resolver.IsPolling, Is.True);

        await mgr.StopResolverPollingAsync();
        Assert.That(mgr.Resolver.IsPolling, Is.False);
    }

    #endregion

    #region PolymarketPortfolioProfile — модель

    [TestCase(TestName = "PortfolioProfile: все свойства")]
    public void PortfolioProfileAllPropertiesTest()
    {
        using var tracker = new PolymarketPortfolioTracker();

        var profile = new PolymarketPortfolioProfile
        {
            Id = "p1",
            Name = "Тестовый",
            Strategy = "Scalping",
            Tags = ["crypto", "fast"],
            Tracker = tracker
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Id, Is.EqualTo("p1"));
            Assert.That(profile.Name, Is.EqualTo("Тестовый"));
            Assert.That(profile.Strategy, Is.EqualTo("Scalping"));
            Assert.That(profile.Tags, Has.Length.EqualTo(2));
            Assert.That(profile.PnLHistory, Is.Null);
            Assert.That(profile.CreatedAt, Is.GreaterThan(DateTimeOffset.MinValue));
        }
    }

    [TestCase(TestName = "PortfolioEventArgs: содержит профиль")]
    public void PortfolioEventArgsTest()
    {
        using var tracker = new PolymarketPortfolioTracker();
        var profile = new PolymarketPortfolioProfile { Id = "p1", Name = "Тест", Tracker = tracker };
        var args = new PolymarketPortfolioEventArgs(profile);

        Assert.That(args.Profile, Is.SameAs(profile));
    }

    #endregion

    #region PolymarketAggregatedSummary — модель

    [TestCase(TestName = "AggregatedSummary: NetPnL = Realized + Unrealized - Fees")]
    public void AggregatedSummaryNetPnLTest()
    {
        var summary = new PolymarketAggregatedSummary
        {
            TotalRealizedPnL = 100,
            TotalUnrealizedPnL = 50,
            TotalFees = 10,
            PerPortfolio = new Dictionary<string, PolymarketPortfolioSummary>()
        };

        Assert.That(summary.NetPnL, Is.EqualTo(140));
    }

    #endregion

    #region Dispose с портфелями

    [TestCase(TestName = "Dispose: очищает все портфели")]
    public void DisposeWithPortfoliosTest()
    {
        var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "First");
        mgr.CreatePortfolio("p2", "Second", enablePnLHistory: true);

        mgr.Dispose();

        Assert.That(mgr.Portfolios, Is.Empty);
    }

    [TestCase(TestName = "DisposeAsync: очищает все портфели")]
    public async Task DisposeAsyncWithPortfoliosTest()
    {
        var mgr = new PolymarketPortfolioManager();
        mgr.CreatePortfolio("p1", "First");
        mgr.CreatePortfolio("p2", "Second", enablePnLHistory: true);

        await mgr.DisposeAsync();

        Assert.That(mgr.Portfolios, Is.Empty);
    }

    #endregion
}
