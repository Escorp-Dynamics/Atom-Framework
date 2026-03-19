using System.Text.Json;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты HMAC-подписи (PolymarketApiSigner) и сериализации REST-моделей Polymarket.
/// </summary>
public class PolymarketRestApiTests(ILogger logger) : BenchmarkTests<PolymarketRestApiTests>(logger)
{
    public PolymarketRestApiTests() : this(ConsoleLogger.Unicode) { }

    #region PolymarketApiSigner — HMAC-SHA256

    [TestCase(TestName = "Подпись GET-запроса без тела")]
    public void SignGetRequestTest()
    {
        // Известный Base64-секрет (16 байт нулей → AAAAAAAAAAAAAAAAAAAAxQ==)
        var secret = Convert.ToBase64String(new byte[16]);

        var signature = PolymarketApiSigner.Sign(secret, "1700000000", "abc123", "GET", "/markets");

        Assert.That(signature, Is.Not.Null.And.Not.Empty);
        // Подпись — валидный Base64
        Assert.DoesNotThrow(() => Convert.FromBase64String(signature));
    }

    [TestCase(TestName = "Подпись POST-запроса с телом")]
    public void SignPostRequestWithBodyTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);
        var body = """{"order":"test"}""";

        var signature = PolymarketApiSigner.Sign(secret, "1700000000", "nonce1", "POST", "/order", body);

        Assert.That(signature, Is.Not.Null.And.Not.Empty);
        Assert.DoesNotThrow(() => Convert.FromBase64String(signature));
    }

    [TestCase(TestName = "Детерменированность подписи — одинаковые параметры дают одинаковый результат")]
    public void SignDeterministicTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sig1 = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "GET", "/book");
        var sig2 = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "GET", "/book");

        Assert.That(sig1, Is.EqualTo(sig2));
    }

    [TestCase(TestName = "Разные методы дают разные подписи")]
    public void SignDifferentMethodsTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sigGet = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "GET", "/order");
        var sigPost = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "POST", "/order");
        var sigDelete = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "DELETE", "/order");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sigGet, Is.Not.EqualTo(sigPost));
            Assert.That(sigGet, Is.Not.EqualTo(sigDelete));
            Assert.That(sigPost, Is.Not.EqualTo(sigDelete));
        }
    }

    [TestCase(TestName = "Метод приводится к верхнему регистру")]
    public void SignMethodCaseInsensitiveTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sigLower = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "get", "/book");
        var sigUpper = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "GET", "/book");

        Assert.That(sigLower, Is.EqualTo(sigUpper));
    }

    [TestCase(TestName = "Разные nonce дают разные подписи")]
    public void SignDifferentNoncesTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sig1 = PolymarketApiSigner.Sign(secret, "1700000000", "nonce1", "GET", "/book");
        var sig2 = PolymarketApiSigner.Sign(secret, "1700000000", "nonce2", "GET", "/book");

        Assert.That(sig1, Is.Not.EqualTo(sig2));
    }

    [TestCase(TestName = "Разные timestamp дают разные подписи")]
    public void SignDifferentTimestampsTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sig1 = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "GET", "/book");
        var sig2 = PolymarketApiSigner.Sign(secret, "1700000001", "nonce", "GET", "/book");

        Assert.That(sig1, Is.Not.EqualTo(sig2));
    }

    [TestCase(TestName = "Пустые параметры вызывают исключение")]
    public void SignNullParametersTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => PolymarketApiSigner.Sign("", "ts", "n", "GET", "/p"));
            Assert.Throws<ArgumentException>(() => PolymarketApiSigner.Sign(secret, "", "n", "GET", "/p"));
            Assert.Throws<ArgumentException>(() => PolymarketApiSigner.Sign(secret, "ts", "", "GET", "/p"));
            Assert.Throws<ArgumentException>(() => PolymarketApiSigner.Sign(secret, "ts", "n", "", "/p"));
            Assert.Throws<ArgumentException>(() => PolymarketApiSigner.Sign(secret, "ts", "n", "GET", ""));
        }
    }

    [TestCase(TestName = "Невалидный Base64 секрет вызывает FormatException")]
    public void SignInvalidBase64SecretTest()
    {
        Assert.Throws<FormatException>(() =>
            PolymarketApiSigner.Sign("not-valid-base64!!!", "1700000000", "nonce", "GET", "/book"));
    }

    [TestCase(TestName = "GetTimestamp возвращает числовую строку")]
    public void GetTimestampTest()
    {
        var ts = PolymarketApiSigner.GetTimestamp();

        Assert.That(ts, Is.Not.Null.And.Not.Empty);
        Assert.That(long.TryParse(ts, out var value), Is.True);
        // Timestamp должен быть разумным (после 2024-01-01)
        Assert.That(value, Is.GreaterThan(1704067200L));
    }

    [TestCase(TestName = "GenerateNonce возвращает 32-символьную hex-строку")]
    public void GenerateNonceTest()
    {
        var nonce = PolymarketApiSigner.GenerateNonce();

        Assert.That(nonce, Has.Length.EqualTo(32)); // 16 байт → 32 hex символа
        Assert.That(nonce, Does.Match("^[0-9a-f]{32}$"));
    }

    [TestCase(TestName = "GenerateNonce уникален при повторных вызовах")]
    public void GenerateNonceUniqueTest()
    {
        var nonces = new HashSet<string>();

        for (var i = 0; i < 100; i++)
            nonces.Add(PolymarketApiSigner.GenerateNonce());

        Assert.That(nonces, Has.Count.EqualTo(100));
    }

    [TestCase(TestName = "Подпись с телом и без тела различается")]
    public void SignWithAndWithoutBodyTest()
    {
        var secret = Convert.ToBase64String(new byte[16]);

        var sigWithout = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "POST", "/order");
        var sigWith = PolymarketApiSigner.Sign(secret, "1700000000", "nonce", "POST", "/order", """{"test":true}""");

        Assert.That(sigWithout, Is.Not.EqualTo(sigWith));
    }

    #endregion

    #region REST-модели — сериализация

    [TestCase(TestName = "Сериализация PolymarketMarket")]
    public void MarketSerializationTest()
    {
        var market = new PolymarketMarket
        {
            ConditionId = "0xCondition123",
            QuestionId = "0xQuestion456",
            Tokens =
            [
                new PolymarketToken { TokenId = "tok1", Outcome = "Yes", Price = "0.65" },
                new PolymarketToken { TokenId = "tok2", Outcome = "No", Price = "0.35" }
            ],
            MinimumOrderSize = 5.0,
            MinimumTickSize = 0.01,
            Description = "Test market",
            Question = "Will it happen?",
            Active = true,
            Closed = false,
            NegRisk = false,
            AcceptingOrders = true
        };

        var json = JsonSerializer.Serialize(market, PolymarketJsonContext.Default.PolymarketMarket);

        Assert.That(json, Does.Contain("\"condition_id\":\"0xCondition123\""));
        Assert.That(json, Does.Contain("\"tokens\""));
        Assert.That(json, Does.Contain("\"active\":true"));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMarket);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.ConditionId, Is.EqualTo("0xCondition123"));
            Assert.That(deserialized.Tokens, Has.Length.EqualTo(2));
            Assert.That(deserialized.Tokens![0].Outcome, Is.EqualTo("Yes"));
            Assert.That(deserialized.MinimumTickSize, Is.EqualTo(0.01));
            Assert.That(deserialized.AcceptingOrders, Is.True);
        }
    }

    [TestCase(TestName = "Десериализация PolymarketMarket из JSON")]
    public void MarketDeserializationFromJsonTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "condition_id": "0xABC",
                "question_id": "0xDEF",
                "tokens": [{"token_id": "t1", "outcome": "Yes", "price": "0.50"}],
                "minimum_order_size": 1,
                "minimum_tick_size": 0.01,
                "description": "Test",
                "category": "Politics",
                "end_date_iso": "2025-12-31",
                "question": "Who wins?",
                "market_slug": "who-wins",
                "active": true,
                "closed": false,
                "seconds_delay": 5,
                "neg_risk": true,
                "neg_risk_market_id": "0xNegRisk",
                "accepting_orders": false,
                "icon": "https://example.com/icon.png",
                "fpmm": "0xFPMM"
            }
            """;

        var market = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMarket);
        Assert.That(market, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(market!.ConditionId, Is.EqualTo("0xABC"));
            Assert.That(market.Category, Is.EqualTo("Politics"));
            Assert.That(market.NegRisk, Is.True);
            Assert.That(market.NegRiskMarketId, Is.EqualTo("0xNegRisk"));
            Assert.That(market.AcceptingOrders, Is.False);
            Assert.That(market.SecondsDelay, Is.EqualTo(5));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketToken")]
    public void TokenSerializationTest()
    {
        var token = new PolymarketToken
        {
            TokenId = "token-abc",
            Outcome = "Yes",
            Price = "0.75",
            Winner = "Yes"
        };

        var json = JsonSerializer.Serialize(token, PolymarketJsonContext.Default.PolymarketToken);
        Assert.That(json, Does.Contain("\"token_id\":\"token-abc\""));
        Assert.That(json, Does.Contain("\"winner\":\"Yes\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketToken);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.TokenId, Is.EqualTo("token-abc"));
            Assert.That(deserialized.Winner, Is.EqualTo("Yes"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketOrderBook (REST)")]
    public void OrderBookSerializationTest()
    {
        var book = new PolymarketOrderBook
        {
            Market = "0xCondition",
            AssetId = "asset-1",
            Hash = "0xHash",
            Timestamp = "1700000000",
            Bids = [new PolymarketBookEntry { Price = "0.50", Size = "100" }],
            Asks = [new PolymarketBookEntry { Price = "0.51", Size = "200" }]
        };

        var json = JsonSerializer.Serialize(book, PolymarketJsonContext.Default.PolymarketOrderBook);
        Assert.That(json, Does.Contain("\"bids\""));
        Assert.That(json, Does.Contain("\"asks\""));
        Assert.That(json, Does.Contain("\"hash\":\"0xHash\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderBook);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Market, Is.EqualTo("0xCondition"));
            Assert.That(deserialized.Bids, Has.Length.EqualTo(1));
            Assert.That(deserialized.Asks, Has.Length.EqualTo(1));
            Assert.That(deserialized.Bids![0].Price, Is.EqualTo("0.50"));
        }
    }

    [TestCase(TestName = "Десериализация PolymarketOrderBook с пустым стаканом")]
    public void EmptyOrderBookDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """{"market":"m1","asset_id":"a1","hash":"0x","timestamp":"0","bids":[],"asks":[]}""";

        var book = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderBook);
        Assert.That(book, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(book!.Bids, Is.Empty);
            Assert.That(book.Asks, Is.Empty);
        }
    }

    [TestCase(TestName = "Сериализация PolymarketPriceResponse (price)")]
    public void PriceResponseSerializationTest()
    {
        var response = new PolymarketPriceResponse { Price = "0.55" };
        var json = JsonSerializer.Serialize(response, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(json, Does.Contain("\"price\":\"0.55\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(deserialized!.Price, Is.EqualTo("0.55"));
    }

    [TestCase(TestName = "Десериализация PolymarketPriceResponse (midpoint)")]
    public void MidpointResponseDeserializationTest()
    {
        const string json = /*lang=json,strict*/ """{"mid":"0.505"}""";
        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(response!.Mid, Is.EqualTo("0.505"));
    }

    [TestCase(TestName = "Десериализация PolymarketPriceResponse (spread)")]
    public void SpreadResponseDeserializationTest()
    {
        const string json = /*lang=json,strict*/ """{"spread":"0.02"}""";
        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(response!.Spread, Is.EqualTo("0.02"));
    }

    [TestCase(TestName = "Десериализация PolymarketPriceResponse (tick_size)")]
    public void TickSizeResponseDeserializationTest()
    {
        const string json = /*lang=json,strict*/ """{"minimum_tick_size":"0.001"}""";
        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(response!.MinimumTickSize, Is.EqualTo("0.001"));
    }

    [TestCase(TestName = "Сериализация PolymarketCreateOrderRequest")]
    public void CreateOrderRequestSerializationTest()
    {
        var request = new PolymarketCreateOrderRequest
        {
            Order = new PolymarketSignedOrder
            {
                Salt = "12345",
                Maker = "0xMaker",
                Signer = "0xSigner",
                Taker = "0x0000000000000000000000000000000000000000",
                TokenId = "token-1",
                MakerAmount = "100000000",
                TakerAmount = "50000000",
                Expiration = "0",
                Nonce = "0",
                FeeRateBps = "0",
                Side = PolymarketSide.Buy,
                SignatureType = 2,
                Signature = "0xSignature"
            },
            Owner = "0xOwner",
            OrderType = PolymarketOrderType.GoodTilCancelled
        };

        var json = JsonSerializer.Serialize(request, PolymarketJsonContext.Default.PolymarketCreateOrderRequest);
        Assert.That(json, Does.Contain("\"order\""));
        Assert.That(json, Does.Contain("\"maker\":\"0xMaker\""));
        Assert.That(json, Does.Contain("\"signatureType\":2"));
        Assert.That(json, Does.Contain("\"orderType\":\"GTC\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketCreateOrderRequest);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Owner, Is.EqualTo("0xOwner"));
            Assert.That(deserialized.OrderType, Is.EqualTo(PolymarketOrderType.GoodTilCancelled));
            Assert.That(deserialized.Order.Maker, Is.EqualTo("0xMaker"));
            Assert.That(deserialized.Order.MakerAmount, Is.EqualTo("100000000"));
            Assert.That(deserialized.Order.Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(deserialized.Order.SignatureType, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketSignedOrder с GTD-типом")]
    public void SignedOrderGtdSerializationTest()
    {
        var order = new PolymarketSignedOrder
        {
            Salt = "999",
            Maker = "0xM",
            Signer = "0xS",
            Taker = "0x0",
            TokenId = "t1",
            MakerAmount = "1000",
            TakerAmount = "500",
            Expiration = "1700090000",
            Nonce = "42",
            FeeRateBps = "100",
            Side = PolymarketSide.Sell,
            SignatureType = 0,
            Signature = "0xSig"
        };

        var json = JsonSerializer.Serialize(order, PolymarketJsonContext.Default.PolymarketSignedOrder);
        Assert.That(json, Does.Contain("\"expiration\":\"1700090000\""));
        Assert.That(json, Does.Contain("\"side\":\"SELL\""));
        Assert.That(json, Does.Contain("\"signatureType\":0"));
    }

    [TestCase(TestName = "Сериализация PolymarketOrderResponse")]
    public void OrderResponseSerializationTest()
    {
        var response = new PolymarketOrderResponse
        {
            OrderId = "ord-abc",
            Success = true,
            Status = "LIVE",
            TransactionHashes = ["0xTx1", "0xTx2"]
        };

        var json = JsonSerializer.Serialize(response, PolymarketJsonContext.Default.PolymarketOrderResponse);
        Assert.That(json, Does.Contain("\"orderID\":\"ord-abc\""));
        Assert.That(json, Does.Contain("\"success\":true"));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderResponse);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.OrderId, Is.EqualTo("ord-abc"));
            Assert.That(deserialized.Success, Is.True);
            Assert.That(deserialized.TransactionHashes, Has.Length.EqualTo(2));
        }
    }

    [TestCase(TestName = "Десериализация PolymarketOrderResponse с ошибкой")]
    public void OrderResponseErrorDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """{"orderID":"","success":false,"errorMsg":"Insufficient balance","status":"REJECTED"}""";

        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderResponse);
        Assert.That(response, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo("Insufficient balance"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketCancelResponse")]
    public void CancelResponseSerializationTest()
    {
        var response = new PolymarketCancelResponse
        {
            Canceled = ["ord-1", "ord-2"],
            NotCanceled = ["ord-3"]
        };

        var json = JsonSerializer.Serialize(response, PolymarketJsonContext.Default.PolymarketCancelResponse);
        Assert.That(json, Does.Contain("\"canceled\""));
        Assert.That(json, Does.Contain("\"not_canceled\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketCancelResponse);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Canceled, Has.Length.EqualTo(2));
            Assert.That(deserialized.NotCanceled, Has.Length.EqualTo(1));
        }
    }

    [TestCase(TestName = "Десериализация PolymarketCancelResponse — всё отменено")]
    public void CancelResponseAllCanceledTest()
    {
        const string json = /*lang=json,strict*/
            """{"canceled":["o1","o2","o3"],"not_canceled":[]}""";

        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketCancelResponse);
        Assert.That(response, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Canceled, Has.Length.EqualTo(3));
            Assert.That(response.NotCanceled, Is.Empty);
        }
    }

    [TestCase(TestName = "Сериализация PolymarketBalanceAllowance")]
    public void BalanceAllowanceSerializationTest()
    {
        var balance = new PolymarketBalanceAllowance
        {
            Balance = "1000000000",
            Allowance = "999999999"
        };

        var json = JsonSerializer.Serialize(balance, PolymarketJsonContext.Default.PolymarketBalanceAllowance);
        Assert.That(json, Does.Contain("\"balance\":\"1000000000\""));
        Assert.That(json, Does.Contain("\"allowance\":\"999999999\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketBalanceAllowance);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Balance, Is.EqualTo("1000000000"));
            Assert.That(deserialized.Allowance, Is.EqualTo("999999999"));
        }
    }

    #endregion

    #region PolymarketRestClient — конструкторы и Dispose

    [TestCase(TestName = "Конструктор по умолчанию")]
    public void DefaultConstructorTest()
    {
        using var client = new PolymarketRestClient();
        Assert.That(client, Is.Not.Null);
    }

    [TestCase(TestName = "Конструктор с кастомным URL")]
    public void CustomUrlConstructorTest()
    {
        using var client = new PolymarketRestClient("https://custom.api.example.com");
        Assert.That(client, Is.Not.Null);
    }

    [TestCase(TestName = "Конструктор с внешним HttpClient")]
    public void ExternalHttpClientConstructorTest()
    {
        using var httpClient = new HttpClient();
        using var client = new PolymarketRestClient(httpClient);
        Assert.That(client, Is.Not.Null);
    }

    [TestCase(TestName = "Конструктор с пустым URL вызывает исключение")]
    public void EmptyUrlConstructorThrowsTest()
    {
        Assert.Throws<ArgumentException>(() => new PolymarketRestClient(""));
    }

    [TestCase(TestName = "Конструктор с null HttpClient вызывает исключение")]
    public void NullHttpClientConstructorThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() => new PolymarketRestClient(null!, "https://example.com"));
    }

    [TestCase(TestName = "Dispose можно вызвать дважды безопасно")]
    public void DoubleDisposeTest()
    {
        var client = new PolymarketRestClient();
        client.Dispose();
        Assert.DoesNotThrow(() => client.Dispose());
    }

    [TestCase(TestName = "SetAuth с null вызывает исключение")]
    public void SetAuthNullThrowsTest()
    {
        using var client = new PolymarketRestClient();
        Assert.Throws<ArgumentNullException>(() => client.SetAuth(null!));
    }

    [TestCase(TestName = "SetAuth устанавливает учётные данные")]
    public void SetAuthTest()
    {
        using var client = new PolymarketRestClient();
        var auth = new PolymarketAuth
        {
            ApiKey = "key",
            Secret = Convert.ToBase64String(new byte[16]),
            Passphrase = "pass"
        };

        Assert.DoesNotThrow(() => client.SetAuth(auth));
    }

    [TestCase(TestName = "Вызов после Dispose вызывает ObjectDisposedException")]
    public async Task MethodsAfterDisposeThrowTest()
    {
        var client = new PolymarketRestClient();
        client.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await client.GetMarketsAsync());
    }

    #endregion

    #region Крайние случаи REST-моделей

    [TestCase(TestName = "PolymarketMarket с null-полями")]
    public void MarketNullFieldsTest()
    {
        var market = new PolymarketMarket();

        var json = JsonSerializer.Serialize(market, PolymarketJsonContext.Default.PolymarketMarket);
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMarket);

        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.ConditionId, Is.Null);
            Assert.That(deserialized.Tokens, Is.Null);
            Assert.That(deserialized.Description, Is.Null);
        }
    }

    [TestCase(TestName = "PolymarketToken с null winner")]
    public void TokenNullWinnerTest()
    {
        const string json = /*lang=json,strict*/
            """{"token_id":"t1","outcome":"Yes","price":"0.50"}""";

        var token = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketToken);
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Winner, Is.Null);
    }

    [TestCase(TestName = "PolymarketOrderBook с null bids/asks")]
    public void OrderBookNullBidsAsksTest()
    {
        const string json = /*lang=json,strict*/
            """{"market":"m1","asset_id":"a1"}""";

        var book = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderBook);
        Assert.That(book, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(book!.Bids, Is.Null);
            Assert.That(book.Asks, Is.Null);
        }
    }

    [TestCase(TestName = "PolymarketPriceResponse со всеми полями одновременно")]
    public void PriceResponseAllFieldsTest()
    {
        const string json = /*lang=json,strict*/
            """{"price":"0.55","mid":"0.505","spread":"0.01","minimum_tick_size":"0.001"}""";

        var response = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceResponse);
        Assert.That(response, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Price, Is.EqualTo("0.55"));
            Assert.That(response.Mid, Is.EqualTo("0.505"));
            Assert.That(response.Spread, Is.EqualTo("0.01"));
            Assert.That(response.MinimumTickSize, Is.EqualTo("0.001"));
        }
    }

    [TestCase(TestName = "Массив PolymarketMarket — roundtrip")]
    public void MarketArrayRoundtripTest()
    {
        var markets = new[]
        {
            new PolymarketMarket { ConditionId = "c1", Active = true },
            new PolymarketMarket { ConditionId = "c2", Active = false }
        };

        var json = JsonSerializer.Serialize(markets, PolymarketJsonContext.Default.PolymarketMarketArray);
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMarketArray);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized, Has.Length.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized![0].ConditionId, Is.EqualTo("c1"));
            Assert.That(deserialized[1].Active, Is.False);
        }
    }

    [TestCase(TestName = "Массив PolymarketOrderBook — roundtrip")]
    public void OrderBookArrayRoundtripTest()
    {
        var books = new[]
        {
            new PolymarketOrderBook { Market = "m1", Bids = [], Asks = [] },
            new PolymarketOrderBook { Market = "m2" }
        };

        var json = JsonSerializer.Serialize(books, PolymarketJsonContext.Default.PolymarketOrderBookArray);
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderBookArray);

        Assert.That(deserialized, Has.Length.EqualTo(2));
    }

    #endregion
}
