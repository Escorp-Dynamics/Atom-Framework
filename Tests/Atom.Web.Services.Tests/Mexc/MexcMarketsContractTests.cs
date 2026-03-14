using Atom.Web.Services.Mexc;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Mexc.Tests;

/// <summary>
/// Контрактные тесты для MEXC-реализации Markets/ интерфейсов.
/// </summary>
public class MexcMarketsContractTests(ILogger logger) : BenchmarkTests<MexcMarketsContractTests>(logger)
{
    public MexcMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "MEXC IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new MexcPriceSnapshot
        {
            AssetId = "BTCUSDT",
            BestBid = 65000.5,
            BestAsk = 65001.0,
            LastTradePrice = 65000.75,
            LastUpdateTicks = 99999
        };

        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(isnap.Midpoint, Is.EqualTo((65000.5 + 65001.0) / 2.0).Within(0.001));
    }

    [TestCase(TestName = "MEXC IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new MexcPriceSnapshot { AssetId = "ETHUSDT" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "MEXC IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new MexcPosition
        {
            AssetId = "BTCUSDT",
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

    [TestCase(TestName = "MEXC IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new MexcPosition { AssetId = "ETHUSDT", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "MEXC IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new MexcPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "MEXC IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new MexcOrderBookSnapshot
        {
            AssetId = "BTCUSDT",
            Timestamp = ts,
            Bids = [(65000, 1.5)],
            Asks = [(65001, 0.8)]
        };

        IMarketOrderBookSnapshot ibook = book;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ibook.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(ibook.Timestamp, Is.EqualTo(ts));
    }

    #endregion

    #region Модели — IMarketTradeSignal

    [TestCase(TestName = "MEXC IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new MexcTradeSignal
        {
            AssetId = "BTCUSDT",
            Action = TradeAction.Buy,
            Quantity = 0.1,
            Confidence = 0.9,
            Reason = "MACD пересечение"
        };

        Assert.That(((IMarketTradeSignal)signal).Action, Is.EqualTo(TradeAction.Buy));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "MexcException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new MexcException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "MEXC IMarketClient: PlatformName = 'MEXC'")]
    public void ClientPlatformName()
    {
        using var client = new MexcClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("MEXC"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "MEXC IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new MexcRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(MexcRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "MEXC IMarketRestClient: Binance-совместимый API")]
    public void RestClientBinanceCompatibleApi()
    {
        // MEXC использует Binance-совместимый API v3
        using var rest = new MexcRestClient();
        Assert.That(rest.BaseUrl, Does.Contain("mexc.com"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "MEXC IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new MexcPriceStream();
        IMarketPriceStream istream = stream;

        Assert.That(istream.TokenCount, Is.EqualTo(0));

        stream.UpdatePrice("BTCUSDT", 65000, 65001, 65000.5);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(istream.TokenCount, Is.EqualTo(1));
        Assert.That(istream.GetPrice("BTCUSDT")!.BestBid, Is.EqualTo(65000));

        istream.ClearCache();
        Assert.That(istream.TokenCount, Is.EqualTo(0));
    }

    #endregion
}
