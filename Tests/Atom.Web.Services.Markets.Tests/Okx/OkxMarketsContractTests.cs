using Atom.Web.Services.Okx;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Okx.Tests;

/// <summary>
/// Контрактные тесты для OKX-реализации Markets/ интерфейсов.
/// </summary>
public class OkxMarketsContractTests(ILogger logger) : BenchmarkTests<OkxMarketsContractTests>(logger)
{
    public OkxMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "OKX IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new OkxPriceSnapshot
        {
            AssetId = "BTC-USDT",
            BestBid = 66500.0,
            BestAsk = 66501.5,
            LastTradePrice = 66500.75,
            LastUpdateTicks = 55555
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("BTC-USDT"));
        Assert.That(isnap.BestBid, Is.EqualTo(66500.0));
        Assert.That(isnap.BestAsk, Is.EqualTo(66501.5));
        Assert.That(isnap.Midpoint, Is.EqualTo((66500.0 + 66501.5) / 2.0).Within(0.001));
    }

    [TestCase(TestName = "OKX IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new OkxPriceSnapshot { AssetId = "ETH-USDT" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "OKX IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new OkxPosition
        {
            AssetId = "BTC-USDT",
            Quantity = 0.2,
            AverageCostBasis = 63000,
            CurrentPrice = 66500,
            RealizedPnL = 150,
            TotalFees = 20,
            TradeCount = 6
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.MarketValue, Is.EqualTo(0.2 * 66500));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.2 * 66500 - 0.2 * 63000));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "OKX IMarketPosition: IsClosed при нулевом количестве")]
    public void PositionIsClosed()
    {
        var pos = new OkxPosition { AssetId = "ETH-USDT", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "OKX IMarketPortfolioSummary: NetPnL")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new OkxPortfolioSummary
        {
            TotalUnrealizedPnL = 2500,
            TotalRealizedPnL = 180,
            TotalFees = 25
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(2500 + 180 - 25));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "OKX IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new OkxOrderBookSnapshot
        {
            AssetId = "BTC-USDT",
            Timestamp = ts,
            Bids = [(66500, 2.0)],
            Asks = [(66501, 1.5)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("BTC-USDT"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "OKX IMarketTradeSignal: все действия")]
    public void TradeSignalAllActions()
    {
        using var scope = Assert.EnterMultipleScope();

        var buy = new OkxTradeSignal { AssetId = "BTC-USDT", Action = TradeAction.Buy };
        Assert.That(((IMarketTradeSignal)buy).Action, Is.EqualTo(TradeAction.Buy));

        var sell = new OkxTradeSignal { AssetId = "BTC-USDT", Action = TradeAction.Sell };
        Assert.That(((IMarketTradeSignal)sell).Action, Is.EqualTo(TradeAction.Sell));

        var hold = new OkxTradeSignal { AssetId = "BTC-USDT", Action = TradeAction.Hold };
        Assert.That(((IMarketTradeSignal)hold).Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "OkxException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new OkxException("okx error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex.Message, Is.EqualTo("okx error"));
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "OKX IMarketClient: PlatformName = 'OKX'")]
    public void ClientPlatformName()
    {
        using var client = new OkxClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("OKX"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "OKX IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new OkxRestClient();
        Assert.That(((IMarketRestClient)rest).BaseUrl, Is.EqualTo(OkxRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "OKX IMarketRestClient: кастомный BaseUrl (демо)")]
    public void RestClientCustomBaseUrl()
    {
        using var rest = new OkxRestClient("https://www.okx.com/api/v5");
        Assert.That(((IMarketRestClient)rest).BaseUrl, Is.EqualTo("https://www.okx.com/api/v5"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "OKX IMarketPriceStream: GetPrice/TokenCount/ClearCache")]
    public void PriceStreamOperations()
    {
        using var stream = new OkxPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));
        Assert.That(istream.GetPrice("BTC-USDT"), Is.Null);

        stream.UpdatePrice("BTC-USDT", 66500, 66501, 66500.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        var price = istream.GetPrice("BTC-USDT");
        Assert.That(price, Is.Not.Null);
        Assert.That(price!.BestBid, Is.EqualTo(66500));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "OKX IMarketPriceStream: обновление перезаписывает кеш")]
    public void PriceStreamUpdateOverwrites()
    {
        using var stream = new OkxPriceStream();
        stream.UpdatePrice("BTC-USDT", 60000, 60001, 60000.5);
        stream.UpdatePrice("BTC-USDT", 66500, 66501, 66500.5);

        var price = ((IMarketPriceStream)stream).GetPrice("BTC-USDT");
        Assert.That(price!.BestBid, Is.EqualTo(66500));
    }

    #endregion
}
