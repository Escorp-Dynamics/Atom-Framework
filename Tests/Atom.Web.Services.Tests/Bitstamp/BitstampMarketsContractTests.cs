using Atom.Web.Services.Bitstamp;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitstamp.Tests;

/// <summary>
/// Контрактные тесты для Bitstamp-реализации Markets/ интерфейсов.
/// </summary>
public class BitstampMarketsContractTests(ILogger logger) : BenchmarkTests<BitstampMarketsContractTests>(logger)
{
    public BitstampMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "Bitstamp IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new BitstampPriceSnapshot
        {
            AssetId = "btcusd",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("btcusd"));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
    }

    [TestCase(TestName = "Bitstamp IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new BitstampPriceSnapshot { AssetId = "ethusd" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "Bitstamp IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new BitstampPosition
        {
            AssetId = "btcusd",
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

    [TestCase(TestName = "Bitstamp IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new BitstampPosition { AssetId = "ethusd", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "Bitstamp IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new BitstampPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "Bitstamp IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new BitstampOrderBookSnapshot
        {
            AssetId = "btcusd",
            Timestamp = ts,
            Bids = [(65000, 1.5)],
            Asks = [(65001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("btcusd"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "Bitstamp IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new BitstampTradeSignal
        {
            AssetId = "btcusd",
            Action = TradeAction.Buy,
            Quantity = 0.1,
            Confidence = 0.9,
            Reason = "Ликвидностный сигнал"
        };

        Assert.That(((IMarketTradeSignal)signal).Action, Is.EqualTo(TradeAction.Buy));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "BitstampException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new BitstampException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "Bitstamp IMarketClient: PlatformName = 'Bitstamp'")]
    public void ClientPlatformName()
    {
        using var client = new BitstampClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Bitstamp"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "Bitstamp IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new BitstampRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(BitstampRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "Bitstamp IMarketRestClient: HMAC-SHA256 v2 auth")]
    public void RestClientHmacV2Auth()
    {
        using var rest = new BitstampRestClient();
        Assert.That(rest.BaseUrl, Does.Contain("bitstamp.net"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "Bitstamp IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new BitstampPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));

        stream.UpdatePrice("btcusd", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        Assert.That(istream.GetPrice("btcusd")!.BestBid, Is.EqualTo(65000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
