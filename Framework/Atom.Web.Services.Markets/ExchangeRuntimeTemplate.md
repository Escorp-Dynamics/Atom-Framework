# Exchange Runtime Template

## Зачем это нужно

Сейчас биржевые адаптеры в Markets уже выровнены по базовым контрактам, REST-методам и price stream-кешу, но не выровнены по runtime-поведению.

Типовой текущий паттерн в клиентах Binance, Coinbase, OKX, Bybit, Kraken и других выглядит так:

- создать ClientWebSocket
- выполнить ConnectAsync
- отправить subscribe/unsubscribe payload
- закрыть сокет при DisconnectAsync

При этом в большинстве клиентов отсутствуют:

- полноценный receive loop
- разбор входящих сообщений в общий runtime-формат
- ping/keepalive
- reconnect/resubscribe
- surface событий уровня цены, стакана, ошибок и reconnect
- bridge в общий streaming pipeline

Polymarket уже реализует этот runtime-уровень и фактически является эталоном для дальнейшего выравнивания.

## Цель

Ввести единый runtime-шаблон для биржевых WebSocket-клиентов без привязки к system-cursor, foreground-only или иным сериализующим ограничениям. Дизайн должен оставаться tab-local, headless-safe и параллельно масштабируемым.

Шаблон должен решать две задачи:

1. убрать дублирование жизненного цикла WebSocket-клиентов
2. отделить transport/runtime-логику от platform-specific message parsing

## Что не нужно делать

Не нужно превращать все клиенты в тяжёлую универсальную иерархию с большим количеством generic-параметров и виртуальных хуков.

Не нужно смешивать в одном абстрактном типе:

- WebSocket lifecycle
- JSON parsing
- обновление IMarketPriceStream
- бизнес-события домена

Не нужно встраивать стратегии, risk manager и execution в сам клиент. Для этого уже существует общий pipeline.

## Предлагаемый шаблон

### Слой 1. ExchangeWebSocketClientBase

Базовый класс отвечает только за transport/runtime:

- создание и переиспользование ClientWebSocket
- connect/disconnect
- send subscribe/unsubscribe
- receive loop
- сборку фрагментированных сообщений
- ping loop
- reconnect с backoff
- автоматический resubscribe после reconnect
- маршрутизацию raw text/binary payload в adapter

Базовый класс не знает ничего о Binance, Coinbase или OKX payload-структурах.

Пример целевой формы:

```csharp
public abstract class ExchangeWebSocketClientBase : IMarketClient, IAsyncDisposable, IDisposable
{
    public abstract string PlatformName { get; }
    public bool IsConnected { get; }

    public ValueTask SubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default);
    public ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default);
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    protected abstract Uri EndpointUri { get; }
    protected abstract ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds);
    protected abstract ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds);
    protected abstract ValueTask HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    protected virtual ValueTask OnConnectedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask OnDisconnectedAsync(Exception? error, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
```

### Слой 2. Exchange message adapter

Platform-specific adapter отвечает только за интерпретацию входящих сообщений.

Его обязанности:

- распознать ping/pong/ack/data/error payload
- извлечь обновления best bid/ask/last trade
- извлечь обновления стакана, если биржа их поддерживает
- преобразовать данные в общий внутренний runtime-формат

Рекомендуемый минимальный runtime-формат:

```csharp
public readonly record struct MarketRealtimeUpdate(
    string AssetId,
    double? BestBid,
    double? BestAsk,
    double? LastTradePrice,
    long LastUpdateTicks,
    MarketRealtimeUpdateKind Kind);
```

Где MarketRealtimeUpdateKind различает хотя бы:

- Ticker
- Trade
- OrderBook
- Heartbeat
- SubscriptionAck
- Error

### Слой 3. Runtime events / bridge

После разбора payload базовый клиент публикует нормализованные события.

Минимальный surface:

- MarketUpdateReceived
- SubscriptionAcknowledged
- RuntimeError
- Reconnected

Дальше возможны два потребителя:

1. конкретный PriceStream адаптера
2. общий MarketStreamingPipeline

Это позволяет не зашивать стратегии и исполнение ордеров внутрь конкретного клиента.

### Слой 4. PriceStream integration

PriceStream конкретной биржи должен быть тонким адаптером вокруг общего runtime update.

То есть вместо схемы:

- внешний код сам вызывает UpdatePrice

должна быть схема:

- клиент публикует MarketRealtimeUpdate
- price stream подписывается на него
- price stream сам обновляет кеш

Это делает live-path реальным, а не ручным тестовым сценарием.

## Минимальный контракт базового runtime

Во всех биржевых клиентах должно появиться одинаковое поведение:

1. SubscribeAsync гарантирует connect и отправку subscribe payload
2. первый subscribe запускает receive loop
3. DisconnectAsync останавливает receive loop и ping loop
4. reconnect восстанавливает последнее множество подписок
5. transport-ошибки публикуются наружу, а не теряются
6. входные сообщения проходят через единый HandleMessageAsync path

## Минимальные события

Для биржевых клиентов достаточно следующего общего набора:

```csharp
public event Action<MarketRealtimeUpdate>? MarketUpdateReceived;
public event Action<string[]>? SubscriptionAcknowledged;
public event Action<Exception>? RuntimeError;
public event Action<int>? Reconnected;
```

Если для конкретной площадки нужны специальные события, они добавляются поверх этого минимального набора, но не вместо него.

## Как использовать с MarketStreamingPipeline

Текущий MarketStreamingPipeline уже умеет обрабатывать PriceUpdate, но не получает их автоматически из большинства биржевых клиентов.

Чтобы соединить runtime и pipeline, нужен тонкий bridge:

```csharp
public sealed class MarketRuntimePipelineBridge : IDisposable
{
    public MarketRuntimePipelineBridge(
        ExchangeWebSocketClientBase client,
        MarketStreamingPipeline pipeline);
}
```

Bridge подписывается на MarketUpdateReceived и передаёт обновления в PublishPriceUpdate.

Это оставляет pipeline общим и не заставляет каждый клиент знать о нём напрямую.

## Порядок миграции

### Этап 1. Вынести общий runtime-каркас

Сначала вводится ExchangeWebSocketClientBase без массовой миграции всех адаптеров.

Первая цель этапа:

- доказать, что базовый класс покрывает connect, receive, ping и reconnect
- не менять публичные REST-контракты
- не ломать текущие contract tests

### Этап 2. Протянуть 3 опорные биржи

Первыми мигрировать:

- Binance
- Coinbase
- OKX

Причина:

- разные subscribe payload-форматы
- разные naming conventions symbol/instId/product_id
- хорошая репрезентативность для остальных CEX-адаптеров

### Этап 3. Подключить runtime tests

Для каждой опорной биржи должны появиться тесты уровня:

- subscribe payload generation
- unsubscribe payload generation
- message parsing в MarketRealtimeUpdate
- reconnect/resubscribe semantics
- integration price stream update after parsed message

### Этап 4. Массовая миграция остальных CEX-клиентов

После стабилизации на трёх платформах по тому же шаблону переносятся остальные:

- Kraken
- Bybit
- Bitfinex
- GateIo
- KuCoin
- HTX
- Bitstamp
- MEXC
- Crypto.com
- Deribit

## Критерии готовности

Биржевой адаптер считается runtime-ready, если:

- contract tests проходят
- есть runtime tests на parse/reconnect
- PriceStream обновляется из реальных входящих сообщений, а не только ручным вызовом UpdatePrice
- reconnect восстанавливает подписки
- ошибки не скрываются и доступны вызывающему коду

## Что делать следующим шагом

Следующий практический шаг после этого документа:

1. ввести ExchangeWebSocketClientBase
2. мигрировать BinanceClient на новый runtime-шаблон
3. добавить для Binance отдельный контрактный и runtime test baseline

Это даст первый проверяемый образец для дальнейшей миграции остальных клиентов.

Подготовительные заметки по следующим кандидатам миграции см. в [CoinbaseOkxMigrationPlan.md](CoinbaseOkxMigrationPlan.md).
