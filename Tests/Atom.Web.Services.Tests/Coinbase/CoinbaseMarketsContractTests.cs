using Atom.Web.Services.Coinbase;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Coinbase.Tests;

/// <summary>
/// Контрактные тесты для Coinbase-реализации Markets/ интерфейсов.
/// </summary>
public class CoinbaseMarketsContractTests(ILogger logger) : BenchmarkTests<CoinbaseMarketsContractTests>(logger)
{
    public CoinbaseMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "Coinbase IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new CoinbasePriceSnapshot
        {
            AssetId = "BTC-USD",
            BestBid = 64500.0,
            BestAsk = 64501.5,
            LastTradePrice = 64500.75,
            LastUpdateTicks = 88888
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("BTC-USD"));
        Assert.That(isnap.BestBid, Is.EqualTo(64500.0));
        Assert.That(isnap.BestAsk, Is.EqualTo(64501.5));
        Assert.That(isnap.Midpoint, Is.EqualTo((64500.0 + 64501.5) / 2.0).Within(0.001));
        Assert.That(isnap.LastTradePrice, Is.EqualTo(64500.75));
    }

    [TestCase(TestName = "Coinbase IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new CoinbasePriceSnapshot { AssetId = "ETH-USD" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "Coinbase IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new CoinbasePosition
        {
            AssetId = "BTC-USD",
            Quantity = 0.25,
            AverageCostBasis = 62000,
            CurrentPrice = 65000,
            RealizedPnL = 50,
            TotalFees = 8,
            TradeCount = 4
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.AssetId, Is.EqualTo("BTC-USD"));
        Assert.That(ipos.MarketValue, Is.EqualTo(0.25 * 65000));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.25 * 65000 - 0.25 * 62000));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "Coinbase IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new CoinbasePosition { AssetId = "ETH-USD", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "Coinbase IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new CoinbasePortfolioSummary
        {
            TotalUnrealizedPnL = 3000,
            TotalRealizedPnL = 150,
            TotalFees = 30
        };

        IMarketPortfolioSummary isum = summary;
        Assert.That(isum.NetPnL, Is.EqualTo(3000 + 150 - 30));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "Coinbase IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new CoinbaseOrderBookSnapshot
        {
            AssetId = "BTC-USD",
            Timestamp = ts,
            Bids = [(64500, 0.5)],
            Asks = [(64501, 1.0)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("BTC-USD"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "Coinbase IMarketTradeSignal: все действия")]
    public void TradeSignalAllActions()
    {
        using var scope = Assert.EnterMultipleScope();

        var buy = new CoinbaseTradeSignal { AssetId = "BTC-USD", Action = TradeAction.Buy };
        Assert.That(((IMarketTradeSignal)buy).Action, Is.EqualTo(TradeAction.Buy));

        var sell = new CoinbaseTradeSignal { AssetId = "BTC-USD", Action = TradeAction.Sell };
        Assert.That(((IMarketTradeSignal)sell).Action, Is.EqualTo(TradeAction.Sell));

        var hold = new CoinbaseTradeSignal { AssetId = "BTC-USD", Action = TradeAction.Hold };
        Assert.That(((IMarketTradeSignal)hold).Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "CoinbaseException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new CoinbaseException("coinbase error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex.Message, Is.EqualTo("coinbase error"));
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "Coinbase IMarketClient: PlatformName = 'Coinbase'")]
    public void ClientPlatformName()
    {
        using var client = new CoinbaseClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Coinbase"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "Coinbase IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new CoinbaseRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(CoinbaseRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "Coinbase IMarketRestClient: кастомный BaseUrl")]
    public void RestClientCustomBaseUrl()
    {
        using var rest = new CoinbaseRestClient("https://sandbox.coinbase.com");
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo("https://sandbox.coinbase.com"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "Coinbase IMarketPriceStream: GetPrice/TokenCount/ClearCache")]
    public void PriceStreamOperations()
    {
        using var stream = new CoinbasePriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));
        Assert.That(istream.GetPrice("BTC-USD"), Is.Null);

        stream.UpdatePrice("BTC-USD", 64500, 64501, 64500.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        var price = istream.GetPrice("BTC-USD");
        Assert.That(price, Is.Not.Null);
        Assert.That(price!.BestBid, Is.EqualTo(64500));
        Assert.That(price.BestAsk, Is.EqualTo(64501));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
