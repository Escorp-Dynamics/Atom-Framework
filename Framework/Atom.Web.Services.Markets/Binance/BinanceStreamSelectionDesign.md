# Binance Stream Selection Design

## Контекст

Текущий Binance runtime уже стабилизирован на общем ExchangeWebSocketClientBase и покрыт runtime-тестами для:

- subscribe/unsubscribe payload generation
- request-id ack tracking
- reconnect/resubscribe
- MarketRuntimePriceStreamBridge
- bookTicker
- trade
- aggTrade
- kline
- protocol error payload
- combined stream envelope

При этом subscribe path пока жёстко пришит к `@bookTicker`:

- `BuildSubscribeMessage` всегда строит `<symbol>@bookTicker`
- `BuildUnsubscribeMessage` делает то же самое
- parser уже умеет больше, чем реально может запросить текущий клиент

Это означает, что runtime и parser готовы к расширению, но API выбора stream-типа отсутствует.

## Ограничения

Нельзя ломать:

- существующий IMarketClient контракт
- tab-local и параллельно масштабируемую архитектуру
- текущий green baseline тестов

Нежелательно:

- вводить global mutable mode на весь процесс
- делать design, где активный stream-type зависит от единственного machine-wide состояния
- смешивать transport/runtime и product-specific parser rules в одном публичном API

## Что именно нужно выбрать

Для Binance практически интересны как минимум 4 stream-типа:

- `bookTicker` — лучший bid/ask, текущий mainline
- `trade` — отдельные сделки
- `aggTrade` — агрегированные сделки
- `kline` — свечи, обычно как snapshot/close-price источник

Дополнительно возможен:

- `ticker` / `24hrTicker` — суточная статистика и last-price oriented payload

## Проблема наивного решения

Наивный вариант — добавить в BinanceClient одно глобальное свойство вроде `SelectedStreamType`.

Почему это плохая идея:

- один экземпляр клиента перестаёт быть очевидно deterministic
- подписки, сделанные в разное время, начинают зависеть от внешнего mutable state
- reconnect может восстановить уже не тот stream-type, который был активен на момент подписки
- тяжело безопасно поддержать mixed subscriptions для разных marketIds

## Рекомендуемое направление

Рекомендуемый путь — не менять IMarketClient, а добавить Binance-specific конфигурационный слой поверх него.

### Вариант A. Отдельный Binance runtime profile

Добавить value-object, например:

```csharp
public enum BinanceStreamType
{
    BookTicker,
    Trade,
    AggregateTrade,
    Kline,
    TwentyFourHourTicker
}

public sealed record BinanceStreamSelection(
    BinanceStreamType StreamType,
    string? Interval = null);
```

И затем передавать его в BinanceClient через конструктор:

```csharp
public BinanceClient(BinanceStreamSelection? streamSelection = null, ...)
```

Плюсы:

- не ломает IMarketClient
- configuration immutable на lifetime клиента
- reconnect воспроизводит тот же stream profile
- хорошо тестируется

Минусы:

- один клиент = один stream profile
- mixed-stream usage потребует нескольких клиентов

### Вариант B. Binance-specific overloads поверх IMarketClient

Добавить отдельный публичный Binance API:

```csharp
public ValueTask SubscribeAsync(string[] marketIds, BinanceStreamSelection selection, CancellationToken cancellationToken = default)
```

Плюсы:

- можно выражать stream-type на уровне вызова

Минусы:

- сложнее reconnect semantics
- придётся хранить точную карту `marketId -> selection`
- появляется дублирование между generic и Binance-specific subscribe path

## Рекомендация

Для следующего этапа выбрать Вариант A.

Причины:

- минимальный риск
- совместим с текущим base runtime
- не требует перекраивать generic contract
- хорошо ложится на уже существующий request-id tracking
- проще переносится на Coinbase/OKX как модель immutable runtime profile

## Рекомендованная форма первой итерации

Первая итерация должна быть узкой:

1. Ввести `BinanceStreamType`.
2. Ввести immutable `BinanceStreamSelection`.
3. Научить `BuildSubscribeMessage` и `BuildUnsubscribeMessage` строить stream name из selection.
4. Оставить default = `BookTicker`, чтобы существующий код не менялся.
5. Добавить runtime-тесты на subscribe payload generation для `trade`, `aggTrade` и `kline`.

## Что пока не делать

Пока не стоит делать:

- mixed stream-types внутри одного generic SubscribeAsync
- глобальные переключатели stream-mode после создания клиента
- авто-merge нескольких stream categories в один subscribe call без явной модели
- расширение IMarketClient новым обязательным параметром

## Признак готовности

Дизайн можно считать готовым к реализации, когда:

- default path сохраняет текущий `bookTicker` behavior
- можно поднять отдельный BinanceClient для `trade` или `kline`
- reconnect/resubscribe сохраняет тот же stream selection
- parser tests и payload generation tests покрывают хотя бы `bookTicker`, `trade`, `aggTrade`, `kline`
