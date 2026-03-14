using Atom.Web.Services.GateIo;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.GateIo.Tests;

/// <summary>
/// Контрактные тесты для Gate.io-реализации Markets/ интерфейсов.
/// </summary>
public class GateIoMarketsContractTests(ILogger logger) : BenchmarkTests<GateIoMarketsContractTests>(logger)
{
    public GateIoMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "GateIo IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new GateIoPriceSnapshot
        {
            AssetId = "BTC_USDT",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("BTC_USDT"));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
    }

    [TestCase(TestName = "GateIo IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new GateIoPriceSnapshot { AssetId = "ETH_USDT" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "GateIo IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new GateIoPosition
        {
            AssetId = "BTC_USDT",
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

    [TestCase(TestName = "GateIo IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new GateIoPosition { AssetId = "ETH_USDT", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "GateIo IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new GateIoPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "GateIo IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new GateIoOrderBookSnapshot
        {
            AssetId = "BTC_USDT",
            Timestamp = ts,
            Bids = [(65000, 1.5)],
            Asks = [(65001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("BTC_USDT"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "GateIo IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new GateIoTradeSignal
        {
            AssetId = "BTC_USDT",
            Action = TradeAction.Buy,
            Quantity = 0.1,
            Price = "65000.00",
            Confidence = 0.92,
            Reason = "Поддержка на уровне"
        };

        Assert.That(((IMarketTradeSignal)signal).Action, Is.EqualTo(TradeAction.Buy));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "GateIoException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new GateIoException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "GateIo IMarketClient: PlatformName = 'Gate.io'")]
    public void ClientPlatformName()
    {
        using var client = new GateIoClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Gate.io"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "GateIo IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new GateIoRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(GateIoRestClient.DefaultApiUrl));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "GateIo IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new GateIoPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));

        stream.UpdatePrice("BTC_USDT", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        Assert.That(istream.GetPrice("BTC_USDT")!.BestBid, Is.EqualTo(65000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
