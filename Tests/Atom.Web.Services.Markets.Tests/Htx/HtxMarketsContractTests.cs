using Atom.Web.Services.Htx;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Htx.Tests;

/// <summary>
/// Контрактные тесты для HTX (Huobi)-реализации Markets/ интерфейсов.
/// </summary>
public class HtxMarketsContractTests(ILogger logger) : BenchmarkTests<HtxMarketsContractTests>(logger)
{
    public HtxMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "HTX IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new HtxPriceSnapshot
        {
            AssetId = "btcusdt",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("btcusdt"));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
    }

    [TestCase(TestName = "HTX IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new HtxPriceSnapshot { AssetId = "ethusdt" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "HTX IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new HtxPosition
        {
            AssetId = "btcusdt",
            Quantity = 0.5,
            AverageCostBasis = 60000,
            CurrentPrice = 65000,
            RealizedPnL = 100,
            TotalFees = 15,
            TradeCount = 7
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.MarketValue, Is.EqualTo(0.5 * 65000));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.5 * 65000 - 0.5 * 60000));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "HTX IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new HtxPosition { AssetId = "ethusdt", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "HTX IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new HtxPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "HTX IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new HtxOrderBookSnapshot
        {
            AssetId = "btcusdt",
            Timestamp = ts,
            Bids = [(65000, 1.5)],
            Asks = [(65001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("btcusdt"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "HTX IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new HtxTradeSignal
        {
            AssetId = "btcusdt",
            Action = TradeAction.Sell,
            Quantity = 0.2,
            Confidence = 0.85,
            Reason = "RSI перекуплен"
        };

        Assert.That(((IMarketTradeSignal)signal).Action, Is.EqualTo(TradeAction.Sell));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "HtxException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new HtxException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "HTX IMarketClient: PlatformName = 'HTX'")]
    public void ClientPlatformName()
    {
        using var client = new HtxClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("HTX"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "HTX IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new HtxRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(HtxRestClient.DefaultApiUrl));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "HTX IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new HtxPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));

        stream.UpdatePrice("btcusdt", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        Assert.That(istream.GetPrice("btcusdt")!.BestBid, Is.EqualTo(65000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
