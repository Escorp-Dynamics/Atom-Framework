using Atom.Web.Services.CryptoCom;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.CryptoCom.Tests;

/// <summary>
/// Контрактные тесты для Crypto.com-реализации Markets/ интерфейсов.
/// </summary>
public class CryptoComMarketsContractTests(ILogger logger) : BenchmarkTests<CryptoComMarketsContractTests>(logger)
{
    public CryptoComMarketsContractTests() : this(ConsoleLogger.Unicode) { }

    #region Модели — IMarketPriceSnapshot

    [TestCase(TestName = "CryptoCom IMarketPriceSnapshot: все свойства доступны")]
    public void PriceSnapshotImplementsInterface()
    {
        var snap = new CryptoComPriceSnapshot
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

    [TestCase(TestName = "CryptoCom IMarketPriceSnapshot: null значения")]
    public void PriceSnapshotNullValues()
    {
        var snap = new CryptoComPriceSnapshot { AssetId = "ETH_USDT" };
        IMarketPriceSnapshot isnap = snap;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(isnap.BestBid, Is.Null);
        Assert.That(isnap.BestAsk, Is.Null);
        Assert.That(isnap.LastTradePrice, Is.Null);
    }

    #endregion

    #region Модели — IMarketPosition

    [TestCase(TestName = "CryptoCom IMarketPosition: все свойства и вычисления")]
    public void PositionImplementsInterface()
    {
        var pos = new CryptoComPosition
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

    [TestCase(TestName = "CryptoCom IMarketPosition: IsClosed = true при нулевом количестве")]
    public void PositionIsClosedTest()
    {
        var pos = new CryptoComPosition { AssetId = "ETH_USDT", Quantity = 0 };
        Assert.That(((IMarketPosition)pos).IsClosed, Is.True);
    }

    #endregion

    #region Модели — IMarketPortfolioSummary

    [TestCase(TestName = "CryptoCom IMarketPortfolioSummary: NetPnL = unrealized + realized - fees")]
    public void PortfolioSummaryNetPnL()
    {
        var summary = new CryptoComPortfolioSummary
        {
            TotalUnrealizedPnL = 5000,
            TotalRealizedPnL = 200,
            TotalFees = 50
        };

        Assert.That(((IMarketPortfolioSummary)summary).NetPnL, Is.EqualTo(5000 + 200 - 50));
    }

    #endregion

    #region Модели — IMarketOrderBookSnapshot

    [TestCase(TestName = "CryptoCom IMarketOrderBookSnapshot: AssetId и Timestamp")]
    public void OrderBookSnapshotImplementsInterface()
    {
        var ts = DateTimeOffset.UtcNow;
        var book = new CryptoComOrderBookSnapshot
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

    [TestCase(TestName = "CryptoCom IMarketTradeSignal: все свойства доступны")]
    public void TradeSignalImplementsInterface()
    {
        var signal = new CryptoComTradeSignal
        {
            AssetId = "BTC_USDT",
            Action = TradeAction.Buy,
            Quantity = 0.1,
            Confidence = 0.9,
            Reason = "Объёмный всплеск"
        };

        Assert.That(((IMarketTradeSignal)signal).Action, Is.EqualTo(TradeAction.Buy));
    }

    #endregion

    #region Исключение

    [TestCase(TestName = "CryptoComException наследует MarketException")]
    public void ExceptionInheritance()
    {
        var ex = new CryptoComException("test error");
        Assert.That(ex, Is.InstanceOf<MarketException>());
    }

    #endregion

    #region Сервисы — IMarketClient

    [TestCase(TestName = "CryptoCom IMarketClient: PlatformName = 'Crypto.com'")]
    public void ClientPlatformName()
    {
        using var client = new CryptoComClient();
        IMarketClient iclient = client;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(iclient.PlatformName, Is.EqualTo("Crypto.com"));
        Assert.That(iclient.IsConnected, Is.False);
    }

    #endregion

    #region Сервисы — IMarketRestClient

    [TestCase(TestName = "CryptoCom IMarketRestClient: BaseUrl корректный")]
    public void RestClientBaseUrl()
    {
        using var rest = new CryptoComRestClient();
        IMarketRestClient irest = rest;
        Assert.That(irest.BaseUrl, Is.EqualTo(CryptoComRestClient.DefaultApiUrl));
    }

    [TestCase(TestName = "CryptoCom IMarketRestClient: HMAC-SHA256 auth")]
    public void RestClientHmacAuth()
    {
        using var rest = new CryptoComRestClient();
        Assert.That(rest.BaseUrl, Does.Contain("crypto.com"));
    }

    #endregion

    #region Сервисы — IMarketPriceStream

    [TestCase(TestName = "CryptoCom IMarketPriceStream: кеш цен работает")]
    public void PriceStreamCacheWorks()
    {
        using var stream = new CryptoComPriceStream();
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
