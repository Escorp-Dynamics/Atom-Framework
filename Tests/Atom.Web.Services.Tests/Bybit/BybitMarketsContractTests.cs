using Atom.Web.Services.Bybit;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bybit.Tests;

/// <summary>
/// Контрактные тесты для Bybit-реализации Markets/ интерфейсов.
/// </summary>
public class BybitMarketsContractTests(ILogger logger) : BenchmarkTests<BybitMarketsContractTests>(logger)
{
    public BybitMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "Bybit IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new BybitPriceSnapshot
        {
            AssetId = "BTCUSDT",
            BestBid = 67000.5,
            BestAsk = 67001.0,
            LastTradePrice = 67000.75,
            LastUpdateTicks = 77777
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(isnap.BestBid, Is.EqualTo(67000.5));
        Assert.That(isnap.BestAsk, Is.EqualTo(67001.0));
        Assert.That(isnap.Midpoint, Is.EqualTo((67000.5 + 67001.0) / 2.0).Within(0.001));
        Assert.That(isnap.LastTradePrice, Is.EqualTo(67000.75));
    }

    [TestCase(TestName = "Bybit IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new BybitPriceSnapshot { AssetId = "ETHUSDT" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "Bybit IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new BybitPosition
        {
            AssetId = "BTCUSDT",
            Quantity = 0.1,
            AverageCostBasis = 64000,
            CurrentPrice = 67000,
            RealizedPnL = 200,
            TotalFees = 12,
            TradeCount = 5
        };

        IMarketPosition ipos = pos;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ipos.MarketValue, Is.EqualTo(0.1 * 67000));
        Assert.That(ipos.UnrealizedPnL, Is.EqualTo(0.1 * 67000 - 0.1 * 64000));
        Assert.That(ipos.IsClosed, Is.False);
    }

    [TestCase(TestName = "Bybit IMarketPosition: IsClosed при нулевом количестве")]
    public void PositionIsClosed()
    {
        var pos = new BybitPosition { AssetId = "ETHUSDT", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "Bybit IMarketPortfolioSummary: NetPnL")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new BybitPortfolioSummary
        {
            TotalUnrealizedPnL = 4000,
            TotalRealizedPnL = 300,
            TotalFees = 40
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(4000 + 300 - 40));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "Bybit IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new BybitOrderBookSnapshot
        {
            AssetId = "BTCUSDT",
            Timestamp = ts,
            Bids = [(67000, 1.5)],
            Asks = [(67001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "Bybit IMarketTradeSignal: все действия")]
    public void TradeSignalAllActions()
    {
        using var scope = Assert.EnterMultipleScope();

        var buy = new BybitTradeSignal { AssetId = "BTCUSDT", Action = TradeAction.Buy };
        Assert.That(((IMarketTradeSignal)buy).Action, Is.EqualTo(TradeAction.Buy));

        var sell = new BybitTradeSignal { AssetId = "BTCUSDT", Action = TradeAction.Sell };
        Assert.That(((IMarketTradeSignal)sell).Action, Is.EqualTo(TradeAction.Sell));

        var hold = new BybitTradeSignal { AssetId = "BTCUSDT", Action = TradeAction.Hold };
        Assert.That(((IMarketTradeSignal)hold).Action, Is.EqualTo(TradeAction.Hold));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "BybitException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new BybitException("bybit error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
        Assert.That(ex.Message, Is.EqualTo("bybit error"));
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "Bybit IMarketClient: PlatformName = 'Bybit'")]
    public void ClientPlatformName()
    {
        using var client = new BybitClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Bybit"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "Bybit IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new BybitRestClient();
        Assert.That(((IMarketRestClient)rest).BaseUrl, Is.EqualTo(BybitRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "Bybit IMarketRestClient: кастомный BaseUrl")]
    public void RestClientCustomBaseUrl()
    {
        using var rest = new BybitRestClient("https://api-testnet.bybit.com");
        Assert.That(((IMarketRestClient)rest).BaseUrl, Is.EqualTo("https://api-testnet.bybit.com"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "Bybit IMarketPriceStream: GetPrice/TokenCount/ClearCache")]
    public void PriceStreamOperations()
    {
        using var stream = new BybitPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));
        Assert.That(istream.GetPrice("BTCUSDT"), Is.Null);

        stream.UpdatePrice("BTCUSDT", 67000, 67001, 67000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        var price = istream.GetPrice("BTCUSDT");
        Assert.That(price, Is.Not.Null);
        Assert.That(price!.BestBid, Is.EqualTo(67000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
