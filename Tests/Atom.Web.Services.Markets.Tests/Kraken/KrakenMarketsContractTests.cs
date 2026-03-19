using Atom.Web.Services.Kraken;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Kraken.Tests;

/// <summary>
/// Контрактные тесты для Kraken-реализации Markets/ интерфейсов.
/// </summary>
public class KrakenMarketsContractTests(ILogger logger) : BenchmarkTests<KrakenMarketsContractTests>(logger)
{
    public KrakenMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "Kraken IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new KrakenPriceSnapshot
        {
            AssetId = "XBTUSD",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("XBTUSD"));
        Assert.That(isnap.BestBid, Is.EqualTo(65000.5));
        Assert.That(isnap.BestAsk, Is.EqualTo(65001.0));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
        Assert.That(isnap.LastTradePrice, Is.EqualTo(65000.75));
        Assert.That(isnap.LastUpdateTicks, Is.EqualTo(99999));
    }

    [TestCase(TestName = "Kraken IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new KrakenPriceSnapshot { AssetId = "ETHUSD" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "Kraken IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new KrakenPosition
        {
            AssetId = "XBTUSD",
            Quantity = 0.5,
            AverageCostBasis = 60000,
            CurrentPrice = 65000,
            RealizedPnL = 100,
            TotalFees = 15,
            TradeCount = 7
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.AssetId, Is.EqualTo("XBTUSD"));
        Assert.That(ipos.Quantity, Is.EqualTo(0.5));
        Assert.That(ipos.MarketValue, Is.EqualTo(0.5 * 65000));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.5 * 65000 - 0.5 * 60000));
        Assert.That(ipos.RealizedPnL, Is.EqualTo(100));
        Assert.That(ipos.TotalFees, Is.EqualTo(15));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "Kraken IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new KrakenPosition { AssetId = "ETHUSD", Quantity = 0 };
        IMarketPosition ipos = pos;
        Assert.That(ipos.IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "Kraken IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new KrakenPortfolioSummary
        {
            OpenPositions = 3,
            ClosedPositions = 1,
            TotalMarketValue = 50000,
            TotalCostBasis = 45000,
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        IMarketPortfolioSummary isum = summary;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isum.NetPnL, Is.EqualTo(5000 + 200 - 50));
        Assert.That(isum.OpenPositions, Is.EqualTo(3));
        Assert.That(isum.ClosedPositions, Is.EqualTo(1));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "Kraken IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new KrakenOrderBookSnapshot
        {
            AssetId = "XBTUSD",
            Timestamp = ts,
            Bids = [(65000, 1.5), (64999, 2.0)],
            Asks = [(65001, 0.8), (65002, 1.2)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("XBTUSD"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "Kraken IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new KrakenTradeSignal
        {
            AssetId = "XBTUSD",
            Action = TradeAction.Buy,
            Quantity = 0.1,
            Price = "65000.00",
            Confidence = 0.92,
            Reason = "Бычий тренд"
        };

        IMarketTradeSignal isig = signal;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isig.Action, Is.EqualTo(TradeAction.Buy));
        Assert.That(isig.Quantity, Is.EqualTo(0.1));
        Assert.That(isig.Confidence, Is.EqualTo(0.92));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "KrakenException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new KrakenException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex.Message, Is.EqualTo("test error"));
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "Kraken IMarketClient: PlatformName = 'Kraken'")]
    public void ClientPlatformName()
    {
        using var client = new KrakenClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Kraken"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "Kraken IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new KrakenRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(KrakenRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "Kraken IMarketRestClient: кастомный BaseUrl")]
    public void RestClientCustomBaseUrl()
    {
        using var rest = new KrakenRestClient("https://custom.kraken.api");
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo("https://custom.kraken.api"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "Kraken IMarketPriceStream: GetPrice/TokenCount/ClearCache")]
    public void PriceStreamOperations()
    {
        using var stream = new KrakenPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));
        Assert.That(istream.GetPrice("XBTUSD"), Is.Null);

        stream.UpdatePrice("XBTUSD", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        var price = istream.GetPrice("XBTUSD");
        Assert.That(price, Is.Not.Null);
        Assert.That(price!.BestBid, Is.EqualTo(65000));
        Assert.That(price.BestAsk, Is.EqualTo(65001));
        Assert.That(price.LastTradePrice, Is.EqualTo(65000.5));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "Kraken IMarketPriceStream: обновление перезаписывает кеш")]
    public void PriceStreamUpdateOverwrites()
    {
        using var stream = new KrakenPriceStream();
        stream.UpdatePrice("XBTUSD", 60000, 60001, 60000.5);
        stream.UpdatePrice("XBTUSD", 65000, 65001, 65000.5);

        var price = ((IMarketPriceStream)stream).GetPrice("XBTUSD");
        Assert.That(price!.BestBid, Is.EqualTo(65000));
    }

    #endregion
}
