# Coinbase and OKX Migration Plan

## Цель

Подготовить CoinbaseClient и OkxClient к миграции на ExchangeWebSocketClientBase без повторного исследования payload-форматов и runtime-gaps.

Этот документ фиксирует:

- что у клиентов уже есть
- чего им не хватает относительно нового базового runtime
- какие методы/адаптеры нужно реализовать при миграции
- какие тесты должны появиться сразу после переноса

## Общее состояние

Оба клиента сейчас находятся в одинаковом transitional-state:

- уже есть отдельный WebSocket-клиент
- уже есть subscribe/unsubscribe payload generation
- уже есть REST-клиент
- уже есть локальный price stream с ручным UpdatePrice
- ещё нет receive loop, reconnect и platform parser-path, сопоставимого с Polymarket

Это делает их хорошими кандидатами для миграции сразу после первой опорной реализации.

## Coinbase

### Что уже реализовано

Файл: [Coinbase/CoinbaseMarketClient.cs](Coinbase/CoinbaseMarketClient.cs)

- endpoint: wss://advanced-trade-ws.coinbase.com
- public channel: ticker
- subscribe payload:

```json
{ "type": "subscribe", "product_ids": ["BTC-USD"], "channel": "ticker" }
```

- unsubscribe payload:

```json
{ "type": "unsubscribe", "product_ids": ["BTC-USD"], "channel": "ticker" }
```

- price stream already stores:
  - best bid
  - best ask
  - last trade price
  - last update ticks

### Что нужно адаптировать к ExchangeWebSocketClientBase

#### 1. Наследование

CoinbaseClient должен перейти с прямой реализации IMarketClient на наследование от ExchangeWebSocketClientBase.

Минимальные переопределения:

- PlatformName
- EndpointUri
- BuildSubscribeMessage
- BuildUnsubscribeMessage
- OnMessageReceivedAsync

#### 2. Parser-path

Нужно ввести platform-specific parser для Coinbase ticker messages.

Минимальный результат parser-а:

- распознать service-level messages subscribe/unsubscribe/error
- извлечь product_id
- извлечь bid/ask/last trade price
- публиковать MarketRealtimeUpdateKind.Ticker или Trade

#### 3. Price stream wiring

После миграции клиент не должен требовать ручного вызова CoinbasePriceStream.UpdatePrice извне.

Нужно подключить:

- либо MarketRuntimePriceStreamBridge
- либо внутреннюю обвязку CoinbaseClient -> CoinbasePriceStream

#### 4. Runtime gaps

Нужно проверить и зафиксировать:

- нужен ли отдельный ping payload у Coinbase public WS
- как выглядит реальное subscription ack-сообщение
- как платформа сигнализирует heartbeat или idle timeout
- как выглядят ошибки формата и channel-level rejects

### Контракт миграции для Coinbase

После переноса Coinbase должен поддерживать:

- connect on first subscribe
- reconnect + resubscribe
- parser-path из raw WS payload в MarketRealtimeUpdate
- заполнение CoinbasePriceStream через bridge
- те же contract tests, что уже есть, плюс новые runtime tests

### Тесты, которых сейчас не хватает

Текущие тесты в [Tests/Atom.Web.Services.Markets.Tests/Coinbase/CoinbaseMarketsContractTests.cs](../../../Tests/Atom.Web.Services.Markets.Tests/Coinbase/CoinbaseMarketsContractTests.cs) проверяют только:

- модели
- интерфейсные свойства
- ручной UpdatePrice

После миграции добавить:

- subscribe payload generation
- unsubscribe payload generation
- parser test на ticker payload
- reconnect/resubscribe test
- bridge test client -> CoinbasePriceStream

## OKX

### Что уже реализовано

Файл: [Okx/OkxMarketClient.cs](Okx/OkxMarketClient.cs)

- endpoint: wss://ws.okx.com:8443/ws/v5/public
- public channel: tickers
- subscribe payload:

```json
{ "op": "subscribe", "args": [{ "channel": "tickers", "instId": "BTC-USDT" }] }
```

- unsubscribe payload:

```json
{ "op": "unsubscribe", "args": [{ "channel": "tickers", "instId": "BTC-USDT" }] }
```

- price stream already stores:
  - best bid
  - best ask
  - last trade price
  - last update ticks

### Что нужно адаптировать к ExchangeWebSocketClientBase

#### 1. Наследование

OkxClient должен перейти на ExchangeWebSocketClientBase.

Минимальные переопределения:

- PlatformName
- EndpointUri
- BuildSubscribeMessage
- BuildUnsubscribeMessage
- OnMessageReceivedAsync

#### 2. Parser-path

Нужно обработать OKX v5 payload с data-array и arg metadata.

Минимум для первой миграции:

- распознать op/event response
- извлечь instId
- извлечь bidPx/askPx/last
- преобразовать в MarketRealtimeUpdate

#### 3. Runtime gaps

Нужно уточнить до фактической миграции:

- есть ли у OKX обязательный ping/pong на public channel
- как выглядит ack на op=subscribe
- есть ли разница между reconnect на public tickers и других каналах
- требует ли парсинг decimal-string значений с явной invariant conversion

#### 4. Price stream wiring

Как и у Coinbase, OkxPriceStream должен обновляться не ручным вызовом UpdatePrice, а через runtime events/bridge.

### Контракт миграции для OKX

После переноса OkxClient должен поддерживать:

- connect on first subscribe
- reconnect + resubscribe
- parser-path из raw OKX WS payload
- bridge в OkxPriceStream
- сохранение существующих REST и contract semantics

### Тесты, которых сейчас не хватает

Текущие тесты в [Tests/Atom.Web.Services.Markets.Tests/Okx/OkxMarketsContractTests.cs](../../../Tests/Atom.Web.Services.Markets.Tests/Okx/OkxMarketsContractTests.cs) покрывают:

- модели
- интерфейсные свойства
- ручной UpdatePrice

После миграции добавить:

- subscribe payload generation
- unsubscribe payload generation
- parser test на ticker payload
- reconnect/resubscribe test
- bridge test client -> OkxPriceStream

## Отличия Coinbase и OKX, важные для адаптера

### Coinbase

- поле идентификатора: product_ids
- форма сообщения: type/channel/product_ids
- naming convention: BTC-USD
- вероятный фокус первой версии parser-а: object-shaped JSON

### OKX

- поле идентификатора: instId
- форма сообщения: op/args/data
- naming convention: BTC-USDT
- вероятный фокус parser-а: args metadata + array data payload

## Что можно вынести в общий код до миграции

В базовом runtime уже достаточно устойчиво выделяются:

- lifecycle socket
- receive loop
- ping loop
- reconnect/resubscribe
- runtime events
- bridge в writable price stream

Дополнительно можно вынести только лёгкие helper-методы:

- culture-invariant parsing строковых decimal-значений
- safe extraction utilities для JSON payload

Но platform-specific shape parsing лучше оставить в адаптерах Coinbase и OKX.

## Рекомендуемый порядок

1. Binance как первый эталонный перенос.
2. Coinbase сразу после Binance как object-shaped adapter.
3. OKX следующим как adapter с args/data моделью.

Такой порядок даст:

- один простой перенос
- один близкий по JSON-object style перенос
- один перенос с более сложной envelope-структурой

После этого можно почти механически переносить остальные CEX-клиенты.