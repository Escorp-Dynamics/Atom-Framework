using Atom.Web.Services.Bitfinex;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitfinex.Tests;

/// <summary>
/// Контрактные тесты для Bitfinex-реализации Markets/ интерфейсов.
/// </summary>
public class BitfinexMarketsContractTests(ILogger logger) : BenchmarkTests<BitfinexMarketsContractTests>(logger)
{
    public BitfinexMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "Bitfinex IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new BitfinexPriceSnapshot
        {
            AssetId = "tBTCUSD",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("tBTCUSD"));
        Assert.That(isnap.BestBid, Is.EqualTo(65000.5));
        Assert.That(isnap.BestAsk, Is.EqualTo(65001.0));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
        Assert.That(isnap.LastTradePrice, Is.EqualTo(65000.75));
    }

    [TestCase(TestName = "Bitfinex IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new BitfinexPriceSnapshot { AssetId = "tETHUSD" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "Bitfinex IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new BitfinexPosition
        {
            AssetId = "tBTCUSD",
            Quantity = 0.5,
            AverageCostBasis = 60000,
            CurrentPrice = 65000,
            RealizedPnL = 100,
            TotalFees = 15,
            TradeCount = 7
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.AssetId, Is.EqualTo("tBTCUSD"));
        Assert.That(ipos.MarketValue, Is.EqualTo(0.5 * 65000));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.5 * 65000 - 0.5 * 60000));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "Bitfinex IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new BitfinexPosition { AssetId = "tETHUSD", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "Bitfinex IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new BitfinexPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "Bitfinex IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new BitfinexOrderBookSnapshot
        {
            AssetId = "tBTCUSD",
            Timestamp = ts,
            Bids = [(65000, 1.5)],
            Asks = [(65001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("tBTCUSD"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "Bitfinex IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new BitfinexTradeSignal
        {
            AssetId = "tBTCUSD",
            Action = TradeAction.Sell,
            Quantity = 0.3,
            Price = "65000.00",
            Confidence = 0.88,
            Reason = "Медвежий дивергенция"
        };

        IMarketTradeSignal isig = signal;
        Assert.That(isig.Action, Is.EqualTo(TradeAction.Sell));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "BitfinexException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new BitfinexException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "Bitfinex IMarketClient: PlatformName = 'Bitfinex'")]
    public void ClientPlatformName()
    {
        using var client = new BitfinexClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Bitfinex"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "Bitfinex IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new BitfinexRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(BitfinexRestClient.DefaultApiUrl));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "Bitfinex IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new BitfinexPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));
        Assert.That(istream.GetPrice("tBTCUSD"), Is.Null);

        stream.UpdatePrice("tBTCUSD", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        Assert.That(istream.GetPrice("tBTCUSD"), Is.Not.Null);
        Assert.That(istream.GetPrice("tBTCUSD")!.BestBid, Is.EqualTo(65000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
