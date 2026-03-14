using System.Text;
using System.Text.Json;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты клиента PolymarketClient: обработка сообщений, диспетчеризация событий,
/// управление жизненным циклом и крайние случаи.
/// </summary>
public class PolymarketClientTests(ILogger logger) : BenchmarkTests<PolymarketClientTests>(logger)
{
    public PolymarketClientTests() : this(ConsoleLogger.Unicode) { }

    #region Создание и утилизация

    [TestCase(TestName = "Создание клиента с параметрами по умолчанию")]
    public void DefaultConstructorTest()
    {
        using var client = new PolymarketClient();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.IsMarketConnected, Is.False);
            Assert.That(client.IsUserConnected, Is.False);
        }
    }

    [TestCase(TestName = "Создание клиента с пользовательским URL")]
    public void CustomUrlConstructorTest()
    {
        using var client = new PolymarketClient("wss://custom.example.com/ws");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.IsMarketConnected, Is.False);
            Assert.That(client.IsUserConnected, Is.False);
        }
    }

    [TestCase(TestName = "Конструктор отклоняет пустой URL")]
    public void EmptyUrlThrowsTest()
    {
        Assert.Throws<ArgumentException>(() => new PolymarketClient(""));
        Assert.Throws<ArgumentException>(() => new PolymarketClient("   "));
    }

    [TestCase(TestName = "Конструктор отклоняет null URL")]
    public void NullUrlThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() => new PolymarketClient(null!));
    }

    [TestCase(TestName = "Конструктор отклоняет отрицательный размер сообщения")]
    public void NegativeMaxMessageSizeThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketClient("wss://test.com", maxMessageSize: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketClient("wss://test.com", maxMessageSize: 0));
    }

    [TestCase(TestName = "Конструктор отклоняет отрицательное количество попыток reconnect")]
    public void NegativeMaxReconnectAttemptsThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketClient("wss://test.com", maxReconnectAttempts: -1));
    }

    [TestCase(TestName = "Конструктор принимает нулевое количество попыток reconnect (безлимитно)")]
    public void ZeroMaxReconnectAttemptsIsValidTest()
    {
        using var client = new PolymarketClient("wss://test.com", maxReconnectAttempts: 0);
        Assert.That(client.AutoReconnectEnabled, Is.True);
    }

    [TestCase(TestName = "AutoReconnectEnabled по умолчанию true")]
    public void AutoReconnectEnabledByDefaultTest()
    {
        using var client = new PolymarketClient();
        Assert.That(client.AutoReconnectEnabled, Is.True);
    }

    [TestCase(TestName = "AutoReconnectEnabled можно отключить")]
    public void DisableAutoReconnectTest()
    {
        using var client = new PolymarketClient();
        client.AutoReconnectEnabled = false;
        Assert.That(client.AutoReconnectEnabled, Is.False);
    }

    [TestCase(TestName = "Двойная утилизация не вызывает ошибку")]
    public void DoubleDisposeTest()
    {
        var client = new PolymarketClient();
        client.Dispose();

        Assert.DoesNotThrow(() => client.Dispose());
    }

    [TestCase(TestName = "Двойная асинхронная утилизация не вызывает ошибку")]
    public async Task DoubleAsyncDisposeTest()
    {
        var client = new PolymarketClient();
        await client.DisposeAsync();

        Assert.DoesNotThrowAsync(async () => await client.DisposeAsync());
    }

    [TestCase(TestName = "Подписка после утилизации вызывает ObjectDisposedException")]
    public void SubscribeAfterDisposeThrowsTest()
    {
        var client = new PolymarketClient();
        client.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await client.SubscribeMarketAsync(["m1"]));
    }

    [TestCase(TestName = "Пользовательская подписка после утилизации вызывает ObjectDisposedException")]
    public void UserSubscribeAfterDisposeThrowsTest()
    {
        var client = new PolymarketClient();
        client.Dispose();

        var auth = new PolymarketAuth { ApiKey = "k", Secret = "s", Passphrase = "p" };

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await client.SubscribeUserAsync(auth, ["m1"]));
    }

    [TestCase(TestName = "Отписка без подключения вызывает PolymarketException")]
    public void UnsubscribeWithoutConnectionThrowsTest()
    {
        using var client = new PolymarketClient();

        Assert.ThrowsAsync<PolymarketException>(async () =>
            await client.UnsubscribeMarketAsync(["m1"]));
    }

    [TestCase(TestName = "Пользовательская отписка без подключения вызывает PolymarketException")]
    public void UserUnsubscribeWithoutConnectionThrowsTest()
    {
        using var client = new PolymarketClient();

        Assert.ThrowsAsync<PolymarketException>(async () =>
            await client.UnsubscribeUserAsync(["m1"]));
    }

    [TestCase(TestName = "Пользовательская подписка с null credentials вызывает ArgumentNullException")]
    public void UserSubscribeNullCredentialsThrowsTest()
    {
        using var client = new PolymarketClient();

        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.SubscribeUserAsync(null!, ["m1"]));
    }

    #endregion

    #region Обработка сообщений (ProcessMessageAsync)

    [TestCase(TestName = "Обработка book-события вызывает BookSnapshotReceived")]
    public async Task ProcessBookMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketBookSnapshot? received = null;

        client.BookSnapshotReceived += (_, e) =>
        {
            received = e.Snapshot;
            return ValueTask.CompletedTask;
        };

        var json = /*lang=json,strict*/
            """{"event_type":"book","asset_id":"a1","market":"m1","timestamp":"100","hash":"h","buys":[{"price":"0.5","size":"10"}],"sells":[]}"""u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(received, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.AssetId, Is.EqualTo("a1"));
            Assert.That(received.Market, Is.EqualTo("m1"));
            Assert.That(received.Buys, Has.Length.EqualTo(1));
            Assert.That(received.Sells, Is.Empty);
            Assert.That(received.Timestamp, Is.EqualTo("100"));
        }
    }

    [TestCase(TestName = "Обработка price_change-события вызывает PriceChanged")]
    public async Task ProcessPriceChangeMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketPriceChange? received = null;

        client.PriceChanged += (_, e) =>
        {
            received = e.PriceChange;
            return ValueTask.CompletedTask;
        };

        var json = /*lang=json,strict*/
            """{"event_type":"price_change","asset_id":"a1","market":"m1","changes":[{"price":"0.5","size":"100","side":"BUY"}]}"""u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(received, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Changes, Has.Length.EqualTo(1));
            Assert.That(received.Changes![0].Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(received.Changes![0].Price, Is.EqualTo("0.5"));
        }
    }

    [TestCase(TestName = "Обработка last_trade_price-события вызывает LastTradePriceReceived")]
    public async Task ProcessLastTradePriceMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketLastTradePrice? received = null;

        client.LastTradePriceReceived += (_, e) =>
        {
            received = e.LastTradePrice;
            return ValueTask.CompletedTask;
        };

        var json = """{"event_type":"last_trade_price","asset_id":"a1","market":"m1","price":"0.75"}"""u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Price, Is.EqualTo("0.75"));
    }

    [TestCase(TestName = "Обработка tick_size_change-события вызывает TickSizeChanged")]
    public async Task ProcessTickSizeChangeMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketTickSizeChange? received = null;

        client.TickSizeChanged += (_, e) =>
        {
            received = e.TickSizeChange;
            return ValueTask.CompletedTask;
        };

        var json = """{"event_type":"tick_size_change","asset_id":"a1","market":"m1","old_tick_size":"0.01","new_tick_size":"0.001"}"""u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(received, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.OldTickSize, Is.EqualTo("0.01"));
            Assert.That(received.NewTickSize, Is.EqualTo("0.001"));
        }
    }

    [TestCase(TestName = "Обработка order-события вызывает OrderUpdated")]
    public async Task ProcessOrderMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketOrder? received = null;

        client.OrderUpdated += (_, e) =>
        {
            received = e.Order;
            return ValueTask.CompletedTask;
        };

        var json = /*lang=json,strict*/
            """
            {
                "event_type":"order",
                "order":{
                    "id":"o1","market":"m1","asset_id":"a1","side":"BUY","type":"GTC",
                    "original_size":"100","size_matched":"0","price":"0.55",
                    "status":"LIVE","owner":"0xO","timestamp":"100","expiration":"0",
                    "associate_trades":[]
                }
            }
            """u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.User);

        Assert.That(received, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Id, Is.EqualTo("o1"));
            Assert.That(received.Side, Is.EqualTo(PolymarketSide.Buy));
            Assert.That(received.Status, Is.EqualTo(PolymarketOrderStatus.Live));
        }
    }

    [TestCase(TestName = "Обработка trade-события вызывает TradeReceived")]
    public async Task ProcessTradeMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketTrade? received = null;

        client.TradeReceived += (_, e) =>
        {
            received = e.Trade;
            return ValueTask.CompletedTask;
        };

        var json = /*lang=json,strict*/
            """
            {
                "event_type":"trade",
                "trade":{
                    "id":"t1","taker_order_id":"o1","market":"m1","asset_id":"a1",
                    "side":"SELL","size":"50","fee_rate_bps":"0","price":"0.55",
                    "status":"MATCHED","match_time":"100","last_update":"101",
                    "outcome":"Yes","bucket_index":"0","owner":"0xO",
                    "trader_side":"TAKER","transaction_hash":"0xTx"
                }
            }
            """u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.User);

        Assert.That(received, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Id, Is.EqualTo("t1"));
            Assert.That(received.Side, Is.EqualTo(PolymarketSide.Sell));
            Assert.That(received.TraderSide, Is.EqualTo(PolymarketTraderSide.Taker));
        }
    }

    #endregion

    #region Крайние случаи обработки сообщений

    [TestCase(TestName = "Невалидный JSON вызывает ErrorOccurred")]
    public async Task InvalidJsonTriggersErrorTest()
    {
        using var client = new PolymarketClient();
        Exception? receivedError = null;
        PolymarketChannel? errorChannel = null;

        client.ErrorOccurred += (_, e) =>
        {
            receivedError = e.Exception;
            errorChannel = e.Channel;
            return ValueTask.CompletedTask;
        };

        var invalidJson = "{ invalid json }"u8;
        await client.ProcessMessageAsync(invalidJson.ToArray(), PolymarketChannel.Market);

        Assert.That(receivedError, Is.Not.Null);
        Assert.That(receivedError, Is.TypeOf<JsonException>());
        Assert.That(errorChannel, Is.EqualTo(PolymarketChannel.Market));
    }

    [TestCase(TestName = "Пустое JSON-сообщение не вызывает событий")]
    public async Task EmptyJsonNoEventsTest()
    {
        using var client = new PolymarketClient();
        var eventFired = false;

        client.BookSnapshotReceived += (_, _) => { eventFired = true; return ValueTask.CompletedTask; };
        client.PriceChanged += (_, _) => { eventFired = true; return ValueTask.CompletedTask; };
        client.LastTradePriceReceived += (_, _) => { eventFired = true; return ValueTask.CompletedTask; };

        // null десериализуется из "null"
        var json = "null"u8;
        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(eventFired, Is.False);
    }

    [TestCase(TestName = "Событие без подписчиков не вызывает ошибок")]
    public async Task NoSubscribersNoErrorTest()
    {
        using var client = new PolymarketClient();

        // Нет подписчиков ни на одно событие — обработка не должна упасть
        var json = """{"event_type":"book","asset_id":"a1","market":"m1","buys":[],"sells":[]}"""u8;

        Assert.DoesNotThrowAsync(async () =>
            await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market));
    }

    [TestCase(TestName = "Обработка order-события с null order не вызывает OrderUpdated")]
    public async Task OrderWithNullDataNoEventTest()
    {
        using var client = new PolymarketClient();
        var eventFired = false;

        client.OrderUpdated += (_, _) => { eventFired = true; return ValueTask.CompletedTask; };

        // event_type = order, но поле order отсутствует
        var json = """{"event_type":"order"}"""u8;
        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.User);

        Assert.That(eventFired, Is.False);
    }

    [TestCase(TestName = "Обработка trade-события с null trade не вызывает TradeReceived")]
    public async Task TradeWithNullDataNoEventTest()
    {
        using var client = new PolymarketClient();
        var eventFired = false;

        client.TradeReceived += (_, _) => { eventFired = true; return ValueTask.CompletedTask; };

        // event_type = trade, но поле trade отсутствует
        var json = """{"event_type":"trade"}"""u8;
        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.User);

        Assert.That(eventFired, Is.False);
    }

    [TestCase(TestName = "Последовательная обработка нескольких сообщений")]
    public async Task MultipleSequentialMessagesTest()
    {
        using var client = new PolymarketClient();
        var bookCount = 0;
        var priceChangeCount = 0;

        client.BookSnapshotReceived += (_, _) => { bookCount++; return ValueTask.CompletedTask; };
        client.PriceChanged += (_, _) => { priceChangeCount++; return ValueTask.CompletedTask; };

        var bookJson = """{"event_type":"book","asset_id":"a1","market":"m1","buys":[],"sells":[]}"""u8;
        var priceJson = """{"event_type":"price_change","asset_id":"a1","market":"m1","changes":[]}"""u8;

        await client.ProcessMessageAsync(bookJson.ToArray(), PolymarketChannel.Market);
        await client.ProcessMessageAsync(priceJson.ToArray(), PolymarketChannel.Market);
        await client.ProcessMessageAsync(bookJson.ToArray(), PolymarketChannel.Market);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bookCount, Is.EqualTo(2));
            Assert.That(priceChangeCount, Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "Данные с дополнительными полями не нарушают десериализацию")]
    public async Task ExtraFieldsInMessageTest()
    {
        using var client = new PolymarketClient();
        PolymarketBookSnapshot? received = null;

        client.BookSnapshotReceived += (_, e) =>
        {
            received = e.Snapshot;
            return ValueTask.CompletedTask;
        };

        // Дополнительные поля future_field и extra_data
        var json = """{"event_type":"book","asset_id":"a1","market":"m1","buys":[],"sells":[],"future_field":"v","extra_data":42}"""u8;

        await client.ProcessMessageAsync(json.ToArray(), PolymarketChannel.Market);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.AssetId, Is.EqualTo("a1"));
    }

    #endregion

    #region EventArgs

    [TestCase(TestName = "PolymarketBookEventArgs хранит снимок")]
    public void BookEventArgsTest()
    {
        var snapshot = new PolymarketBookSnapshot { AssetId = "a1" };
        var args = new PolymarketBookEventArgs(snapshot);
        Assert.That(args.Snapshot, Is.SameAs(snapshot));
    }

    [TestCase(TestName = "PolymarketPriceChangeEventArgs хранит данные изменения")]
    public void PriceChangeEventArgsTest()
    {
        var pc = new PolymarketPriceChange { Market = "m1" };
        var args = new PolymarketPriceChangeEventArgs(pc);
        Assert.That(args.PriceChange, Is.SameAs(pc));
    }

    [TestCase(TestName = "PolymarketLastTradePriceEventArgs хранит данные")]
    public void LastTradePriceEventArgsTest()
    {
        var ltp = new PolymarketLastTradePrice { Price = "0.5" };
        var args = new PolymarketLastTradePriceEventArgs(ltp);
        Assert.That(args.LastTradePrice, Is.SameAs(ltp));
    }

    [TestCase(TestName = "PolymarketTickSizeChangeEventArgs хранит данные")]
    public void TickSizeChangeEventArgsTest()
    {
        var tsc = new PolymarketTickSizeChange { OldTickSize = "0.01", NewTickSize = "0.001" };
        var args = new PolymarketTickSizeChangeEventArgs(tsc);
        Assert.That(args.TickSizeChange, Is.SameAs(tsc));
    }

    [TestCase(TestName = "PolymarketOrderEventArgs хранит ордер")]
    public void OrderEventArgsTest()
    {
        var order = new PolymarketOrder { Id = "o1" };
        var args = new PolymarketOrderEventArgs(order);
        Assert.That(args.Order, Is.SameAs(order));
    }

    [TestCase(TestName = "PolymarketTradeEventArgs хранит сделку")]
    public void TradeEventArgsTest()
    {
        var trade = new PolymarketTrade { Id = "t1" };
        var args = new PolymarketTradeEventArgs(trade);
        Assert.That(args.Trade, Is.SameAs(trade));
    }

    [TestCase(TestName = "PolymarketDisconnectedEventArgs хранит канал")]
    public void DisconnectedEventArgsTest()
    {
        var args = new PolymarketDisconnectedEventArgs(PolymarketChannel.Market);
        Assert.That(args.Channel, Is.EqualTo(PolymarketChannel.Market));

        args = new PolymarketDisconnectedEventArgs(PolymarketChannel.User);
        Assert.That(args.Channel, Is.EqualTo(PolymarketChannel.User));
    }

    [TestCase(TestName = "PolymarketErrorEventArgs хранит исключение и канал")]
    public void ErrorEventArgsTest()
    {
        var ex = new InvalidOperationException("тест");
        var args = new PolymarketErrorEventArgs(ex, PolymarketChannel.User);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Exception, Is.SameAs(ex));
            Assert.That(args.Channel, Is.EqualTo(PolymarketChannel.User));
        }
    }

    [TestCase(TestName = "PolymarketReconnectedEventArgs хранит канал и попытку")]
    public void ReconnectedEventArgsTest()
    {
        var args = new PolymarketReconnectedEventArgs(PolymarketChannel.Market, 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Channel, Is.EqualTo(PolymarketChannel.Market));
            Assert.That(args.Attempt, Is.EqualTo(3));
        }
    }

    [TestCase(TestName = "PolymarketReconnectedEventArgs — первая попытка")]
    public void ReconnectedFirstAttemptEventArgsTest()
    {
        var args = new PolymarketReconnectedEventArgs(PolymarketChannel.User, 1);
        Assert.That(args.Attempt, Is.EqualTo(1));
    }

    #endregion

    #region PolymarketException

    [TestCase(TestName = "PolymarketException — конструктор без параметров")]
    public void ExceptionDefaultConstructorTest()
    {
        var ex = new PolymarketException();
        Assert.That(ex.Message, Is.Not.Null);
    }

    [TestCase(TestName = "PolymarketException — конструктор с сообщением")]
    public void ExceptionMessageConstructorTest()
    {
        var ex = new PolymarketException("Тестовая ошибка");
        Assert.That(ex.Message, Is.EqualTo("Тестовая ошибка"));
    }

    [TestCase(TestName = "PolymarketException — конструктор с внутренним исключением")]
    public void ExceptionInnerExceptionConstructorTest()
    {
        var inner = new InvalidOperationException("внутренняя");
        var ex = new PolymarketException("внешняя", inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Is.EqualTo("внешняя"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }

    #endregion

    #region Состояние свойств IsConnected

    [TestCase(TestName = "IsMarketConnected и IsUserConnected false после утилизации")]
    public void IsConnectedAfterDisposeTest()
    {
        var client = new PolymarketClient();
        client.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.IsMarketConnected, Is.False);
            Assert.That(client.IsUserConnected, Is.False);
        }
    }

    [TestCase(TestName = "DisconnectMarketAsync без подключения не вызывает ошибок")]
    public async Task DisconnectMarketWithoutConnectionTest()
    {
        using var client = new PolymarketClient();
        Assert.DoesNotThrowAsync(async () => await client.DisconnectMarketAsync());
    }

    [TestCase(TestName = "DisconnectUserAsync без подключения не вызывает ошибок")]
    public async Task DisconnectUserWithoutConnectionTest()
    {
        using var client = new PolymarketClient();
        Assert.DoesNotThrowAsync(async () => await client.DisconnectUserAsync());
    }

    #endregion

    #region Константы клиента

    [TestCase(TestName = "Проверка значения DefaultBaseUrl")]
    public void DefaultBaseUrlTest()
    {
        Assert.That(PolymarketClient.DefaultBaseUrl, Is.EqualTo("wss://ws-subscriptions-clob.polymarket.com/ws"));
    }

    [TestCase(TestName = "Создание клиента с пользовательскими параметрами reconnect и ping")]
    public void CustomReconnectAndPingParametersTest()
    {
        using var client = new PolymarketClient(
            "wss://test.com",
            maxMessageSize: 512,
            reconnectDelay: TimeSpan.FromSeconds(10),
            maxReconnectAttempts: 5,
            pingInterval: TimeSpan.FromSeconds(15));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.IsMarketConnected, Is.False);
            Assert.That(client.IsUserConnected, Is.False);
            Assert.That(client.AutoReconnectEnabled, Is.True);
        }
    }

    [TestCase(TestName = "AutoReconnectEnabled false после Dispose")]
    public void AutoReconnectDisabledAfterDisposeTest()
    {
        var client = new PolymarketClient();
        client.Dispose();
        Assert.That(client.AutoReconnectEnabled, Is.False);
    }

    [TestCase(TestName = "AutoReconnectEnabled false после async Dispose")]
    public async Task AutoReconnectDisabledAfterAsyncDisposeTest()
    {
        var client = new PolymarketClient();
        await client.DisposeAsync();
        Assert.That(client.AutoReconnectEnabled, Is.False);
    }

    #endregion
}
