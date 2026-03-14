using System.Security.Cryptography;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты унифицированной HMAC-аутентификации и streaming pipeline.
/// </summary>
public class MarketAuthenticatorTests(ILogger logger) : BenchmarkTests<MarketAuthenticatorTests>(logger)
{
    public MarketAuthenticatorTests() : this(ConsoleLogger.Unicode) { }

    #region IMarketAuthenticator — Базовые контракты

    [TestCase(TestName = "HmacAuthenticator SHA256: AlgorithmName корректный")]
    public void Sha256AlgorithmName()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256);
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));
    }

    [TestCase(TestName = "HmacAuthenticator SHA384: AlgorithmName корректный")]
    public void Sha384AlgorithmName()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA384);
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA384"));
    }

    [TestCase(TestName = "HmacAuthenticator SHA512: AlgorithmName корректный")]
    public void Sha512AlgorithmName()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA512);
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA512"));
    }

    [TestCase(TestName = "HmacAuthenticator: SignRequest добавляет заголовок API-ключа")]
    public void SignRequestAddsApiKeyHeader()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256, apiKeyHeader: "X-TEST-KEY");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/v1/ticker");

        auth.SignRequest(request);

        Assert.That(request.Headers.Contains("X-TEST-KEY"), Is.True);
    }

    [TestCase(TestName = "HmacAuthenticator: SignRequest добавляет заголовок подписи")]
    public void SignRequestAddsSignatureHeader()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256, signatureHeader: "X-TEST-SIGN");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/v1/ticker");

        auth.SignRequest(request);

        Assert.That(request.Headers.Contains("X-TEST-SIGN"), Is.True);
    }

    [TestCase(TestName = "HmacAuthenticator: SignRequest добавляет timestamp")]
    public void SignRequestAddsTimestamp()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256, timestampHeader: "X-TEST-TS");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/v1/ticker");

        auth.SignRequest(request);

        Assert.That(request.Headers.Contains("X-TEST-TS"), Is.True);
    }

    [TestCase(TestName = "HmacAuthenticator: HexLower формат подписи")]
    public void HexLowerOutputFormat()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256,
            outputFormat: HmacOutputFormat.HexLower, signatureHeader: "Sig");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/");

        auth.SignRequest(request);

        var sig = request.Headers.GetValues("Sig").First();
        // Hex string должен содержать только 0-9a-f
        Assert.That(sig, Does.Match("^[0-9a-f]+$"));
    }

    [TestCase(TestName = "HmacAuthenticator: Base64 формат подписи")]
    public void Base64OutputFormat()
    {
        using var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256,
            outputFormat: HmacOutputFormat.Base64, signatureHeader: "Sig");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/");

        auth.SignRequest(request);

        var sig = request.Headers.GetValues("Sig").First();
        // Base64 должен декодироваться без ошибки
        Assert.DoesNotThrow(() => Convert.FromBase64String(sig));
    }

    [TestCase(TestName = "HmacAuthenticator: QueryParameter placement добавляет signature в URL")]
    public void QueryParameterPlacement()
    {
        using var auth = new HmacAuthenticator(
            HashAlgorithmName.SHA256,
            new HmacAuthenticatorConfig
            {
                ApiKey = "key", ApiSecret = "secret",
                OutputFormat = HmacOutputFormat.HexLower,
                Placement = SignaturePlacement.QueryParameter,
                SignatureQueryParam = "sig"
            },
            static (in SignatureContext ctx) => $"{ctx.Timestamp}{ctx.Body}");

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/v1?symbol=BTC");
        auth.SignRequest(request);

        Assert.That(request.RequestUri!.ToString(), Does.Contain("sig="));
    }

    [TestCase(TestName = "HmacAuthenticator: Dispose обнуляет секрет")]
    public void DisposeZerosSecret()
    {
        var auth = CreateTestAuthenticator(HashAlgorithmName.SHA256);
        auth.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com/");
        Assert.Throws<ObjectDisposedException>(() => auth.SignRequest(request));
    }

    #endregion

    #region Фабрики MarketAuthenticators

    [TestCase(TestName = "MarketAuthenticators.Binance: создаёт валидный аутентификатор")]
    public void BinanceAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Binance("apikey123", "secret456");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.binance.com/api/v3/order?symbol=BTCUSDT&timestamp=1234");
        auth.SignRequest(request, "");
        Assert.That(request.Headers.Contains("X-MBX-APIKEY"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.Coinbase: создаёт валидный аутентификатор")]
    public void CoinbaseAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Coinbase("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.coinbase.com/api/v3/brokerage/orders");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("CB-ACCESS-KEY"), Is.True);
        Assert.That(request.Headers.Contains("CB-ACCESS-SIGN"), Is.True);
        Assert.That(request.Headers.Contains("CB-ACCESS-TIMESTAMP"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.Bybit: создаёт валидный аутентификатор")]
    public void BybitAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Bybit("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.bybit.com/v5/order/create");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("X-BAPI-API-KEY"), Is.True);
        Assert.That(request.Headers.Contains("X-BAPI-SIGN"), Is.True);
        Assert.That(request.Headers.Contains("X-BAPI-RECV-WINDOW"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.Okx: создаёт валидный аутентификатор с passphrase")]
    public void OkxAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Okx("apikey", "secret", "mypassphrase");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.okx.com/api/v5/trade/order");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("OK-ACCESS-KEY"), Is.True);
        Assert.That(request.Headers.Contains("OK-ACCESS-SIGN"), Is.True);
        Assert.That(request.Headers.Contains("OK-ACCESS-PASSPHRASE"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.Bitfinex: создаёт HMAC-SHA384 аутентификатор")]
    public void BitfinexAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Bitfinex("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA384"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.bitfinex.com/v2/auth/w/order/submit");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("bfx-apikey"), Is.True);
        Assert.That(request.Headers.Contains("bfx-signature"), Is.True);
        Assert.That(request.Headers.Contains("bfx-nonce"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.GateIo: создаёт HMAC-SHA512 аутентификатор")]
    public void GateIoAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.GateIo("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA512"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.gateio.ws/api/v4/spot/orders");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("KEY"), Is.True);
        Assert.That(request.Headers.Contains("SIGN"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.KuCoin: создаёт аутентификатор с подписанным passphrase")]
    public void KuCoinAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.KuCoin("apikey", "secret", "pass123");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kucoin.com/api/v1/orders");
        auth.SignRequest(request, "{}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("KC-API-KEY"), Is.True);
        Assert.That(request.Headers.Contains("KC-API-SIGN"), Is.True);
        Assert.That(request.Headers.Contains("KC-API-PASSPHRASE"), Is.True);
        Assert.That(request.Headers.Contains("KC-API-KEY-VERSION"), Is.True);
    }

    [TestCase(TestName = "MarketAuthenticators.Htx: создаёт аутентификатор с query placement")]
    public void HtxAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Htx("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.huobi.pro/v1/order/orders/place?AccessKeyId=apikey&SignatureMethod=HmacSHA256");
        auth.SignRequest(request, "{}");

        // HTX использует query parameter для подписи
        Assert.That(request.RequestUri!.ToString(), Does.Contain("Signature="));
    }

    [TestCase(TestName = "MarketAuthenticators.Mexc: создаёт Binance-совместимый аутентификатор")]
    public void MexcAuthenticatorFactory()
    {
        using var auth = MarketAuthenticators.Mexc("apikey", "secret");
        Assert.That(auth.AlgorithmName, Is.EqualTo("HMAC-SHA256"));

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.mexc.com/api/v3/order?symbol=BTCUSDT&timestamp=1234");
        auth.SignRequest(request, "");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(request.Headers.Contains("X-MEXC-APIKEY"), Is.True);
        Assert.That(request.RequestUri!.ToString(), Does.Contain("signature="));
    }

    #endregion

    #region StreamingPipelineConfig

    [TestCase(TestName = "StreamingPipelineConfig: значения по умолчанию")]
    public void PipelineConfigDefaults()
    {
        var cfg = new StreamingPipelineConfig();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(cfg.EvaluationInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(cfg.OrderCooldown, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(cfg.MinConfidence, Is.EqualTo(0.7));
        Assert.That(cfg.DryRun, Is.True);
        Assert.That(cfg.PriceChannelCapacity, Is.EqualTo(10_000));
        Assert.That(cfg.AutoReconnect, Is.True);
    }

    #endregion

    #region MarketStreamingPipeline — конструктор и свойства

    [TestCase(TestName = "MarketStreamingPipeline: PlatformName из wsClient")]
    public void PipelinePlatformName()
    {
        using var client = new StubMarketClient("TestExchange");
        using var priceStream = new StubPriceStream();
        var pipeline = new MarketStreamingPipeline(client, priceStream);

        Assert.That(pipeline.PlatformName, Is.EqualTo("TestExchange"));
    }

    [TestCase(TestName = "MarketStreamingPipeline: не запущен после создания")]
    public void PipelineNotRunningInitially()
    {
        using var client = new StubMarketClient("Test");
        using var priceStream = new StubPriceStream();
        var pipeline = new MarketStreamingPipeline(client, priceStream);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(pipeline.IsRunning, Is.False);
        Assert.That(pipeline.ProcessedUpdates, Is.EqualTo(0));
        Assert.That(pipeline.GeneratedSignals, Is.EqualTo(0));
        Assert.That(pipeline.ExecutedOrders, Is.EqualTo(0));
    }

    [TestCase(TestName = "MarketStreamingPipeline: PublishPriceUpdate false когда не запущен")]
    public void PublishPriceUpdateWhenNotRunning()
    {
        using var client = new StubMarketClient("Test");
        using var priceStream = new StubPriceStream();
        var pipeline = new MarketStreamingPipeline(client, priceStream);

        var result = pipeline.PublishPriceUpdate(new PriceUpdate("BTC", 65000, 65001, 65000.5, 0));
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "MarketStreamingPipeline: AddStrategy/RemoveStrategy")]
    public void PipelineAddRemoveStrategy()
    {
        using var client = new StubMarketClient("Test");
        using var priceStream = new StubPriceStream();
        var pipeline = new MarketStreamingPipeline(client, priceStream);

        var strategy = new StubStrategy("TestStrategy");
        pipeline.AddStrategy(strategy, ["BTC"]);

        // Не бросает исключение при удалении
        Assert.DoesNotThrow(() => pipeline.RemoveStrategy("TestStrategy"));
    }

    #endregion

    #region Helpers

    private static HmacAuthenticator CreateTestAuthenticator(
        HashAlgorithmName algo,
        string apiKeyHeader = "X-API-KEY",
        string signatureHeader = "X-API-SIGN",
        string timestampHeader = "X-API-TS",
        HmacOutputFormat outputFormat = HmacOutputFormat.HexLower)
    {
        return new HmacAuthenticator(
            algo,
            new HmacAuthenticatorConfig
            {
                ApiKey = "test-key",
                ApiSecret = "test-secret-key-for-hmac-testing",
                OutputFormat = outputFormat,
                Placement = SignaturePlacement.Header,
                ApiKeyHeader = apiKeyHeader,
                SignatureHeader = signatureHeader,
                TimestampHeader = timestampHeader
            },
            static (in SignatureContext ctx) => $"{ctx.Timestamp}{ctx.Method}{ctx.Path}{ctx.Body}");
    }

    /// <summary>Стаб для IMarketClient.</summary>
    private sealed class StubMarketClient(string platformName) : IMarketClient, IDisposable
    {
        public string PlatformName => platformName;
        public bool IsConnected => false;
        public ValueTask SubscribeAsync(string[] marketIds, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DisconnectAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>Стаб для IMarketPriceStream.</summary>
    private sealed class StubPriceStream : IMarketPriceStream
    {
        public int TokenCount => 0;
        public IMarketPriceSnapshot? GetPrice(string assetId) => null;
        public void ClearCache() { }
        public void Dispose() { }
    }

    /// <summary>Стаб для IMarketStrategy.</summary>
    private sealed class StubStrategy(string name) : IMarketStrategy
    {
        public string Name => name;
        public IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId) => new StubSignal(assetId);
        public void OnPriceUpdated(IMarketPriceSnapshot snapshot) { }
        public void Dispose() { }
    }

    private sealed class StubSignal(string assetId) : IMarketTradeSignal
    {
        public string AssetId => assetId;
        public TradeAction Action => TradeAction.Hold;
        public double Quantity => 0;
        public string? Price => null;
        public double Confidence => 0;
        public string? Reason => null;
    }

    #endregion
}
