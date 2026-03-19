using System.Text.Json;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты сериализации и десериализации JSON для всех моделей данных Polymarket.
/// Проверяет совместимость с NativeAOT через source-generated контекст.
/// </summary>
public class PolymarketSerializationTests(ILogger logger) : BenchmarkTests<PolymarketSerializationTests>(logger)
{
    public PolymarketSerializationTests() : this(ConsoleLogger.Unicode) { }

    #region Перечисления

    [TestCase(TestName = "Сериализация PolymarketChannel")]
    public void ChannelSerializationTest()
    {
        // Market
        var json = JsonSerializer.Serialize(PolymarketChannel.Market, PolymarketJsonContext.Default.PolymarketChannel);
        Assert.That(json, Is.EqualTo("\"market\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketChannel);
        Assert.That(deserialized, Is.EqualTo(PolymarketChannel.Market));

        // User
        json = JsonSerializer.Serialize(PolymarketChannel.User, PolymarketJsonContext.Default.PolymarketChannel);
        Assert.That(json, Is.EqualTo("\"user\""));

        deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketChannel);
        Assert.That(deserialized, Is.EqualTo(PolymarketChannel.User));
    }

    [TestCase(TestName = "Сериализация PolymarketEventType")]
    public void EventTypeSerializationTest()
    {
        var cases = new (PolymarketEventType Value, string Expected)[]
        {
            (PolymarketEventType.Book, "\"book\""),
            (PolymarketEventType.PriceChange, "\"price_change\""),
            (PolymarketEventType.LastTradePrice, "\"last_trade_price\""),
            (PolymarketEventType.TickSizeChange, "\"tick_size_change\""),
            (PolymarketEventType.Order, "\"order\""),
            (PolymarketEventType.Trade, "\"trade\"")
        };

        foreach (var (value, expected) in cases)
        {
            var json = JsonSerializer.Serialize(value, PolymarketJsonContext.Default.PolymarketEventType);
            Assert.That(json, Is.EqualTo(expected), $"Сериализация {value}");

            var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketEventType);
            Assert.That(deserialized, Is.EqualTo(value), $"Десериализация {value}");
        }
    }

    [TestCase(TestName = "Сериализация PolymarketSide")]
    public void SideSerializationTest()
    {
        var json = JsonSerializer.Serialize(PolymarketSide.Buy, PolymarketJsonContext.Default.PolymarketSide);
        Assert.That(json, Is.EqualTo("\"BUY\""));

        json = JsonSerializer.Serialize(PolymarketSide.Sell, PolymarketJsonContext.Default.PolymarketSide);
        Assert.That(json, Is.EqualTo("\"SELL\""));

        var buy = JsonSerializer.Deserialize("\"BUY\"", PolymarketJsonContext.Default.PolymarketSide);
        Assert.That(buy, Is.EqualTo(PolymarketSide.Buy));

        var sell = JsonSerializer.Deserialize("\"SELL\"", PolymarketJsonContext.Default.PolymarketSide);
        Assert.That(sell, Is.EqualTo(PolymarketSide.Sell));
    }

    [TestCase(TestName = "Сериализация PolymarketOrderStatus")]
    public void OrderStatusSerializationTest()
    {
        var cases = new (PolymarketOrderStatus Value, string Expected)[]
        {
            (PolymarketOrderStatus.Live, "\"LIVE\""),
            (PolymarketOrderStatus.Cancelled, "\"CANCELLED\""),
            (PolymarketOrderStatus.Matched, "\"MATCHED\""),
            (PolymarketOrderStatus.Delayed, "\"DELAYED\"")
        };

        foreach (var (value, expected) in cases)
        {
            var json = JsonSerializer.Serialize(value, PolymarketJsonContext.Default.PolymarketOrderStatus);
            Assert.That(json, Is.EqualTo(expected));

            var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderStatus);
            Assert.That(deserialized, Is.EqualTo(value));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketOrderType")]
    public void OrderTypeSerializationTest()
    {
        var cases = new (PolymarketOrderType Value, string Expected)[]
        {
            (PolymarketOrderType.GoodTilCancelled, "\"GTC\""),
            (PolymarketOrderType.GoodTilDate, "\"GTD\""),
            (PolymarketOrderType.FillOrKill, "\"FOK\"")
        };

        foreach (var (value, expected) in cases)
        {
            var json = JsonSerializer.Serialize(value, PolymarketJsonContext.Default.PolymarketOrderType);
            Assert.That(json, Is.EqualTo(expected));

            var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrderType);
            Assert.That(deserialized, Is.EqualTo(value));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketTradeStatus")]
    public void TradeStatusSerializationTest()
    {
        var cases = new (PolymarketTradeStatus Value, string Expected)[]
        {
            (PolymarketTradeStatus.Matched, "\"MATCHED\""),
            (PolymarketTradeStatus.Confirmed, "\"CONFIRMED\""),
            (PolymarketTradeStatus.Failed, "\"FAILED\""),
            (PolymarketTradeStatus.Retracted, "\"RETRACTED\"")
        };

        foreach (var (value, expected) in cases)
        {
            var json = JsonSerializer.Serialize(value, PolymarketJsonContext.Default.PolymarketTradeStatus);
            Assert.That(json, Is.EqualTo(expected));

            var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketTradeStatus);
            Assert.That(deserialized, Is.EqualTo(value));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketTraderSide")]
    public void TraderSideSerializationTest()
    {
        var json = JsonSerializer.Serialize(PolymarketTraderSide.Maker, PolymarketJsonContext.Default.PolymarketTraderSide);
        Assert.That(json, Is.EqualTo("\"MAKER\""));

        json = JsonSerializer.Serialize(PolymarketTraderSide.Taker, PolymarketJsonContext.Default.PolymarketTraderSide);
        Assert.That(json, Is.EqualTo("\"TAKER\""));
    }

    #endregion

    #region Модели данных

    [TestCase(TestName = "Сериализация PolymarketAuth")]
    public void AuthSerializationTest()
    {
        var auth = new PolymarketAuth
        {
            ApiKey = "test-api-key",
            Secret = "test-secret",
            Passphrase = "test-passphrase"
        };

        var json = JsonSerializer.Serialize(auth, PolymarketJsonContext.Default.PolymarketAuth);
        Assert.That(json, Does.Contain("\"apiKey\":\"test-api-key\""));
        Assert.That(json, Does.Contain("\"secret\":\"test-secret\""));
        Assert.That(json, Does.Contain("\"passphrase\":\"test-passphrase\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketAuth);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.ApiKey, Is.EqualTo("test-api-key"));
            Assert.That(deserialized.Secret, Is.EqualTo("test-secret"));
            Assert.That(deserialized.Passphrase, Is.EqualTo("test-passphrase"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketBookEntry")]
    public void BookEntrySerializationTest()
    {
        var entry = new PolymarketBookEntry { Price = "0.50", Size = "100.00" };

        var json = JsonSerializer.Serialize(entry, PolymarketJsonContext.Default.PolymarketBookEntry);
        Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"price\":\"0.50\",\"size\":\"100.00\"}"));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketBookEntry);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Price, Is.EqualTo("0.50"));
            Assert.That(deserialized.Size, Is.EqualTo("100.00"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketPriceChangeEntry")]
    public void PriceChangeEntrySerializationTest()
    {
        var entry = new PolymarketPriceChangeEntry
        {
            Price = "0.55",
            Size = "200.00",
            Side = PolymarketSide.Buy
        };

        var json = JsonSerializer.Serialize(entry, PolymarketJsonContext.Default.PolymarketPriceChangeEntry);
        Assert.That(json, Does.Contain("\"price\":\"0.55\""));
        Assert.That(json, Does.Contain("\"size\":\"200.00\""));
        Assert.That(json, Does.Contain("\"side\":\"BUY\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceChangeEntry);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Price, Is.EqualTo("0.55"));
            Assert.That(deserialized.Size, Is.EqualTo("200.00"));
            Assert.That(deserialized.Side, Is.EqualTo(PolymarketSide.Buy));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketBookSnapshot")]
    public void BookSnapshotSerializationTest()
    {
        var snapshot = new PolymarketBookSnapshot
        {
            EventType = PolymarketEventType.Book,
            AssetId = "asset-123",
            Market = "0xCondition456",
            Timestamp = "1700000000",
            Hash = "0xHash789",
            Buys = [new() { Price = "0.50", Size = "100" }, new() { Price = "0.49", Size = "200" }],
            Sells = [new() { Price = "0.51", Size = "150" }]
        };

        var json = JsonSerializer.Serialize(snapshot, PolymarketJsonContext.Default.PolymarketBookSnapshot);
        Assert.That(json, Does.Contain("\"event_type\":\"book\""));
        Assert.That(json, Does.Contain("\"asset_id\":\"asset-123\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketBookSnapshot);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.EventType, Is.EqualTo(PolymarketEventType.Book));
            Assert.That(deserialized.AssetId, Is.EqualTo("asset-123"));
            Assert.That(deserialized.Market, Is.EqualTo("0xCondition456"));
            Assert.That(deserialized.Timestamp, Is.EqualTo("1700000000"));
            Assert.That(deserialized.Hash, Is.EqualTo("0xHash789"));
            Assert.That(deserialized.Buys, Has.Length.EqualTo(2));
            Assert.That(deserialized.Sells, Has.Length.EqualTo(1));
            Assert.That(deserialized.Buys![0].Price, Is.EqualTo("0.50"));
            Assert.That(deserialized.Buys![1].Size, Is.EqualTo("200"));
            Assert.That(deserialized.Sells![0].Price, Is.EqualTo("0.51"));
        }
    }

    [TestCase(TestName = "Десериализация пустого стакана")]
    public void EmptyBookSnapshotDeserializationTest()
    {
        const string json = /*lang=json,strict*/ """{"event_type":"book","asset_id":"a1","market":"m1","buys":[],"sells":[]}""";

        var snapshot = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketBookSnapshot);
        Assert.That(snapshot, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot!.Buys, Is.Empty);
            Assert.That(snapshot.Sells, Is.Empty);
        }
    }

    [TestCase(TestName = "Сериализация PolymarketPriceChange")]
    public void PriceChangeSerializationTest()
    {
        var priceChange = new PolymarketPriceChange
        {
            EventType = PolymarketEventType.PriceChange,
            AssetId = "asset-abc",
            Market = "0xMarket",
            Changes =
            [
                new() { Price = "0.50", Size = "150", Side = PolymarketSide.Buy },
                new() { Price = "0.51", Size = "0", Side = PolymarketSide.Sell }
            ]
        };

        var json = JsonSerializer.Serialize(priceChange, PolymarketJsonContext.Default.PolymarketPriceChange);
        Assert.That(json, Does.Contain("\"event_type\":\"price_change\""));
        Assert.That(json, Does.Contain("\"changes\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketPriceChange);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Changes, Has.Length.EqualTo(2));
            Assert.That(deserialized.Changes![0].Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(deserialized.Changes![1].Size, Is.EqualTo("0"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketLastTradePrice")]
    public void LastTradePriceSerializationTest()
    {
        var ltp = new PolymarketLastTradePrice
        {
            EventType = PolymarketEventType.LastTradePrice,
            AssetId = "asset-ltp",
            Market = "0xMarketLtp",
            Price = "0.6789"
        };

        var json = JsonSerializer.Serialize(ltp, PolymarketJsonContext.Default.PolymarketLastTradePrice);
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketLastTradePrice);

        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.EventType, Is.EqualTo(PolymarketEventType.LastTradePrice));
            Assert.That(deserialized.Price, Is.EqualTo("0.6789"));
            Assert.That(deserialized.AssetId, Is.EqualTo("asset-ltp"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketTickSizeChange")]
    public void TickSizeChangeSerializationTest()
    {
        var change = new PolymarketTickSizeChange
        {
            EventType = PolymarketEventType.TickSizeChange,
            AssetId = "asset-tick",
            Market = "0xMarketTick",
            OldTickSize = "0.01",
            NewTickSize = "0.001"
        };

        var json = JsonSerializer.Serialize(change, PolymarketJsonContext.Default.PolymarketTickSizeChange);
        Assert.That(json, Does.Contain("\"old_tick_size\":\"0.01\""));
        Assert.That(json, Does.Contain("\"new_tick_size\":\"0.001\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketTickSizeChange);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.OldTickSize, Is.EqualTo("0.01"));
            Assert.That(deserialized.NewTickSize, Is.EqualTo("0.001"));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketOrder")]
    public void OrderSerializationTest()
    {
        var order = new PolymarketOrder
        {
            Id = "order-001",
            Market = "0xCondition",
            AssetId = "asset-001",
            Side = PolymarketSide.Buy,
            Type = PolymarketOrderType.GoodTilCancelled,
            OriginalSize = "100",
            SizeMatched = "50",
            Price = "0.55",
            Status = PolymarketOrderStatus.Live,
            Owner = "0xOwner",
            Timestamp = "1700000000",
            Expiration = "0",
            AssociateTrades = ["trade-001", "trade-002"]
        };

        var json = JsonSerializer.Serialize(order, PolymarketJsonContext.Default.PolymarketOrder);
        Assert.That(json, Does.Contain("\"id\":\"order-001\""));
        Assert.That(json, Does.Contain("\"side\":\"BUY\""));
        Assert.That(json, Does.Contain("\"type\":\"GTC\""));
        Assert.That(json, Does.Contain("\"status\":\"LIVE\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrder);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Id, Is.EqualTo("order-001"));
            Assert.That(deserialized.Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(deserialized.Type, Is.EqualTo(PolymarketOrderType.GoodTilCancelled));
            Assert.That(deserialized.OriginalSize, Is.EqualTo("100"));
            Assert.That(deserialized.SizeMatched, Is.EqualTo("50"));
            Assert.That(deserialized.AssociateTrades, Has.Length.EqualTo(2));
        }
    }

    [TestCase(TestName = "Сериализация PolymarketTrade")]
    public void TradeSerializationTest()
    {
        var trade = new PolymarketTrade
        {
            Id = "trade-001",
            TakerOrderId = "order-999",
            Market = "0xCondition",
            AssetId = "asset-001",
            Side = PolymarketSide.Sell,
            Size = "50",
            FeeRateBps = "200",
            Price = "0.55",
            Status = PolymarketTradeStatus.Matched,
            MatchTime = "1700000000",
            LastUpdate = "1700000001",
            Outcome = "Yes",
            BucketIndex = "0",
            Owner = "0xOwner",
            TraderSide = PolymarketTraderSide.Taker,
            TransactionHash = "0xTxHash"
        };

        var json = JsonSerializer.Serialize(trade, PolymarketJsonContext.Default.PolymarketTrade);
        Assert.That(json, Does.Contain("\"taker_order_id\":\"order-999\""));
        Assert.That(json, Does.Contain("\"trader_side\":\"TAKER\""));
        Assert.That(json, Does.Contain("\"fee_rate_bps\":\"200\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketTrade);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Id, Is.EqualTo("trade-001"));
            Assert.That(deserialized.TakerOrderId, Is.EqualTo("order-999"));
            Assert.That(deserialized.Side, Is.EqualTo(PolymarketSide.Sell));
            Assert.That(deserialized.Status, Is.EqualTo(PolymarketTradeStatus.Matched));
            Assert.That(deserialized.TraderSide, Is.EqualTo(PolymarketTraderSide.Taker));
            Assert.That(deserialized.TransactionHash, Is.EqualTo("0xTxHash"));
        }
    }

    #endregion

    #region Сообщения подписки

    [TestCase(TestName = "Сериализация подписки на рыночный канал")]
    public void MarketSubscriptionSerializationTest()
    {
        var sub = new PolymarketSubscription
        {
            Type = "subscribe",
            Channel = PolymarketChannel.Market,
            Markets = ["0xCondition1", "0xCondition2"],
            AssetsIds = ["asset-1"]
        };

        var json = JsonSerializer.Serialize(sub, PolymarketJsonContext.Default.PolymarketSubscription);
        Assert.That(json, Does.Contain("\"type\":\"subscribe\""));
        Assert.That(json, Does.Contain("\"channel\":\"market\""));
        Assert.That(json, Does.Contain("\"markets\""));
        Assert.That(json, Does.Not.Contain("\"auth\""));

        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketSubscription);
        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Type, Is.EqualTo("subscribe"));
            Assert.That(deserialized.Channel, Is.EqualTo(PolymarketChannel.Market));
            Assert.That(deserialized.Markets, Has.Length.EqualTo(2));
        }
    }

    [TestCase(TestName = "Сериализация подписки на пользовательский канал с auth")]
    public void UserSubscriptionWithAuthSerializationTest()
    {
        var sub = new PolymarketSubscription
        {
            Type = "subscribe",
            Channel = PolymarketChannel.User,
            Markets = [],
            AssetsIds = [],
            Auth = new PolymarketAuth
            {
                ApiKey = "key-123",
                Secret = "secret-456",
                Passphrase = "pass-789"
            }
        };

        var json = JsonSerializer.Serialize(sub, PolymarketJsonContext.Default.PolymarketSubscription);
        Assert.That(json, Does.Contain("\"auth\""));
        Assert.That(json, Does.Contain("\"apiKey\":\"key-123\""));
        Assert.That(json, Does.Contain("\"channel\":\"user\""));
    }

    [TestCase(TestName = "Сериализация отписки")]
    public void UnsubscribeSerializationTest()
    {
        var sub = new PolymarketSubscription
        {
            Type = "unsubscribe",
            Channel = PolymarketChannel.Market,
            Markets = ["0xCondition1"]
        };

        var json = JsonSerializer.Serialize(sub, PolymarketJsonContext.Default.PolymarketSubscription);
        Assert.That(json, Does.Contain("\"type\":\"unsubscribe\""));
    }

    #endregion

    #region Полные WebSocket-сообщения (PolymarketMessage)

    [TestCase(TestName = "Десериализация book-события")]
    public void BookMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "event_type": "book",
                "asset_id": "asset-123",
                "market": "0xCondition",
                "timestamp": "1700000000",
                "hash": "0xHash",
                "buys": [{"price": "0.50", "size": "100"}],
                "sells": [{"price": "0.51", "size": "200"}, {"price": "0.52", "size": "300"}]
            }
            """;

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg!.EventType, Is.EqualTo(PolymarketEventType.Book));
            Assert.That(msg.AssetId, Is.EqualTo("asset-123"));
            Assert.That(msg.Buys, Has.Length.EqualTo(1));
            Assert.That(msg.Sells, Has.Length.EqualTo(2));
            Assert.That(msg.Timestamp, Is.EqualTo("1700000000"));
            Assert.That(msg.Hash, Is.EqualTo("0xHash"));
        }
    }

    [TestCase(TestName = "Десериализация price_change-события")]
    public void PriceChangeMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "event_type": "price_change",
                "asset_id": "asset-456",
                "market": "0xMarket",
                "changes": [
                    {"price": "0.50", "size": "150", "side": "BUY"},
                    {"price": "0.51", "size": "0", "side": "SELL"}
                ]
            }
            """;

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg!.EventType, Is.EqualTo(PolymarketEventType.PriceChange));
            Assert.That(msg.Changes, Has.Length.EqualTo(2));
            Assert.That(msg.Changes![0].Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(msg.Changes![1].Size, Is.EqualTo("0"));
        }
    }

    [TestCase(TestName = "Десериализация last_trade_price-события")]
    public void LastTradePriceMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """{"event_type": "last_trade_price", "asset_id": "a1", "market": "m1", "price": "0.75"}""";

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg!.EventType, Is.EqualTo(PolymarketEventType.LastTradePrice));
            Assert.That(msg.Price, Is.EqualTo("0.75"));
        }
    }

    [TestCase(TestName = "Десериализация tick_size_change-события")]
    public void TickSizeChangeMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "event_type": "tick_size_change",
                "asset_id": "a1",
                "market": "m1",
                "old_tick_size": "0.01",
                "new_tick_size": "0.001"
            }
            """;

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg!.EventType, Is.EqualTo(PolymarketEventType.TickSizeChange));
            Assert.That(msg.OldTickSize, Is.EqualTo("0.01"));
            Assert.That(msg.NewTickSize, Is.EqualTo("0.001"));
        }
    }

    [TestCase(TestName = "Десериализация order-события")]
    public void OrderMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "event_type": "order",
                "order": {
                    "id": "ord-1",
                    "market": "0xC",
                    "asset_id": "a1",
                    "side": "BUY",
                    "type": "GTC",
                    "original_size": "100",
                    "size_matched": "0",
                    "price": "0.55",
                    "status": "LIVE",
                    "owner": "0xOwner",
                    "timestamp": "1700000000",
                    "expiration": "0",
                    "associate_trades": []
                }
            }
            """;

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Order, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg.EventType, Is.EqualTo(PolymarketEventType.Order));
            Assert.That(msg.Order!.Id, Is.EqualTo("ord-1"));
            Assert.That(msg.Order.Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(msg.Order.Type, Is.EqualTo(PolymarketOrderType.GoodTilCancelled));
            Assert.That(msg.Order.Status, Is.EqualTo(PolymarketOrderStatus.Live));
            Assert.That(msg.Order.AssociateTrades, Is.Empty);
        }
    }

    [TestCase(TestName = "Десериализация trade-события")]
    public void TradeMessageDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "event_type": "trade",
                "trade": {
                    "id": "t1",
                    "taker_order_id": "o1",
                    "market": "0xC",
                    "asset_id": "a1",
                    "side": "SELL",
                    "size": "50",
                    "fee_rate_bps": "200",
                    "price": "0.55",
                    "status": "MATCHED",
                    "match_time": "1700000000",
                    "last_update": "1700000001",
                    "outcome": "Yes",
                    "bucket_index": "0",
                    "owner": "0xOwner",
                    "trader_side": "TAKER",
                    "transaction_hash": "0xTx"
                }
            }
            """;

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Trade, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg.EventType, Is.EqualTo(PolymarketEventType.Trade));
            Assert.That(msg.Trade!.Id, Is.EqualTo("t1"));
            Assert.That(msg.Trade.Side, Is.EqualTo(PolymarketSide.Sell));
            Assert.That(msg.Trade.Status, Is.EqualTo(PolymarketTradeStatus.Matched));
            Assert.That(msg.Trade.TraderSide, Is.EqualTo(PolymarketTraderSide.Taker));
        }
    }

    #endregion

    #region Крайние случаи

    [TestCase(TestName = "Десериализация сообщения с неизвестными полями (ExtensionData)")]
    public void UnknownFieldsDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """{"event_type":"book","asset_id":"a1","market":"m1","unknown_field":"value","buys":[],"sells":[]}""";

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.ExtensionData, Does.ContainKey("unknown_field"));
    }

    [TestCase(TestName = "Десериализация сообщения с null-полями")]
    public void NullFieldsDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """{"event_type":"last_trade_price","asset_id":null,"market":null,"price":"0.5"}""";

        var msg = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketMessage);
        Assert.That(msg, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(msg!.AssetId, Is.Null);
            Assert.That(msg.Market, Is.Null);
            Assert.That(msg.Price, Is.EqualTo("0.5"));
        }
    }

    [TestCase(TestName = "Десериализация ордера с GTD типом")]
    public void GtdOrderDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "id": "o1",
                "market": "m1",
                "asset_id": "a1",
                "side": "SELL",
                "type": "GTD",
                "original_size": "500",
                "size_matched": "250",
                "price": "0.30",
                "status": "MATCHED",
                "owner": "0xAddr",
                "timestamp": "1700000000",
                "expiration": "1700090000"
            }
            """;

        var order = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketOrder);
        Assert.That(order, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(order!.Type, Is.EqualTo(PolymarketOrderType.GoodTilDate));
            Assert.That(order.Status, Is.EqualTo(PolymarketOrderStatus.Matched));
            Assert.That(order.Expiration, Is.EqualTo("1700090000"));
        }
    }

    [TestCase(TestName = "Десериализация сделки с FOK типом ордера и статусом Confirmed")]
    public void ConfirmedTradeDeserializationTest()
    {
        const string json = /*lang=json,strict*/
            """
            {
                "id": "t1",
                "taker_order_id": "o1",
                "market": "m1",
                "asset_id": "a1",
                "side": "BUY",
                "size": "1000",
                "fee_rate_bps": "0",
                "price": "0.99",
                "status": "CONFIRMED",
                "match_time": "1700000000",
                "last_update": "1700000005",
                "outcome": "No",
                "bucket_index": "1",
                "owner": "0xAddr",
                "trader_side": "MAKER",
                "transaction_hash": "0xLongHash"
            }
            """;

        var trade = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketTrade);
        Assert.That(trade, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trade!.Status, Is.EqualTo(PolymarketTradeStatus.Confirmed));
            Assert.That(trade.TraderSide, Is.EqualTo(PolymarketTraderSide.Maker));
            Assert.That(trade.Outcome, Is.EqualTo("No"));
            Assert.That(trade.Size, Is.EqualTo("1000"));
        }
    }

    [TestCase(TestName = "Сериализация подписки без markets и assets")]
    public void EmptySubscriptionSerializationTest()
    {
        var sub = new PolymarketSubscription
        {
            Type = "subscribe",
            Channel = PolymarketChannel.Market
        };

        var json = JsonSerializer.Serialize(sub, PolymarketJsonContext.Default.PolymarketSubscription);

        // markets и assets_ids могут быть null → не включены в JSON
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketSubscription);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Markets, Is.Null);
        Assert.That(deserialized.AssetsIds, Is.Null);
    }

    [TestCase(TestName = "Десериализация стакана с большим количеством уровней")]
    public void LargeBookSnapshotDeserializationTest()
    {
        // Генерация стакана со 100 уровнями с каждой стороны
        var buys = new PolymarketBookEntry[100];
        var sells = new PolymarketBookEntry[100];

        for (var i = 0; i < 100; i++)
        {
            buys[i] = new PolymarketBookEntry { Price = $"0.{50 - i:D2}", Size = $"{(i + 1) * 10}" };
            sells[i] = new PolymarketBookEntry { Price = $"0.{51 + i:D2}", Size = $"{(i + 1) * 10}" };
        }

        var snapshot = new PolymarketBookSnapshot
        {
            EventType = PolymarketEventType.Book,
            AssetId = "large-asset",
            Market = "large-market",
            Timestamp = "1700000000",
            Buys = buys,
            Sells = sells
        };

        var json = JsonSerializer.Serialize(snapshot, PolymarketJsonContext.Default.PolymarketBookSnapshot);
        var deserialized = JsonSerializer.Deserialize(json, PolymarketJsonContext.Default.PolymarketBookSnapshot);

        Assert.That(deserialized, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Buys, Has.Length.EqualTo(100));
            Assert.That(deserialized.Sells, Has.Length.EqualTo(100));
        }
    }

    [TestCase(TestName = "Roundtrip сериализации всех моделей")]
    public void FullRoundtripTest()
    {
        // PolymarketAuth
        var auth = new PolymarketAuth { ApiKey = "k", Secret = "s", Passphrase = "p" };
        var authJson = JsonSerializer.Serialize(auth, PolymarketJsonContext.Default.PolymarketAuth);
        var authBack = JsonSerializer.Deserialize(authJson, PolymarketJsonContext.Default.PolymarketAuth)!;
        Assert.That(authBack.ApiKey, Is.EqualTo("k"));

        // PolymarketSubscription
        var sub = new PolymarketSubscription
        {
            Type = "subscribe",
            Channel = PolymarketChannel.User,
            Markets = ["m1"],
            AssetsIds = ["a1"],
            Auth = auth
        };
        var subJson = JsonSerializer.Serialize(sub, PolymarketJsonContext.Default.PolymarketSubscription);
        var subBack = JsonSerializer.Deserialize(subJson, PolymarketJsonContext.Default.PolymarketSubscription)!;
        Assert.That(subBack.Auth, Is.Not.Null);
        Assert.That(subBack.Auth!.ApiKey, Is.EqualTo("k"));
    }

    #endregion
}
