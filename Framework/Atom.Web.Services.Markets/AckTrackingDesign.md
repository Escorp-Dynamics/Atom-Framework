# Ack Tracking Design

## Контекст

После миграций Binance, Coinbase и OKX стало видно, что подтверждение подписок бывает двух принципиально разных типов:

- request-correlated ack:
  Binance отвечает `{ result: null, id: <requestId> }`, поэтому клиенту нужно хранить pending subscribe/unsubscribe requests и связывать ack с исходной операцией.
- state-derived ack:
  Coinbase и OKX не подтверждают исходный request id, а возвращают состояние подписки (`type = subscriptions`, `event = subscribe`) и набор market ids приходится извлекать из самого payload.

Текущий runtime base уже нормализует transport/reconnect/resubscribe, но не нормализует стратегию распознавания ack. Это правильно: формат ack exchange-specific. Проблема не в том, что ack должен разбираться в base, а в том, что у клиентов нет общего контракта для хранения и публикации acknowledgement semantics.

## Цели

- не переносить platform-specific payload parsing в `ExchangeWebSocketClientBase`
- дать единый контракт для tracking pending requests там, где ack коррелируется по request id
- сохранить поддержку stateless/state-derived ack для Coinbase/OKX-like платформ
- отделить `subscribe ack`, `unsubscribe ack` и `resubscribe ack`
- не ломать текущий event contract `SubscriptionAcknowledged`

## Не-цели

- не пытаться унифицировать wire format subscribe/unsubscribe payloads
- не вводить обязательный request id для платформ, у которых его нет
- не переносить error normalization и ack parsing в один общий giant parser

## Наблюдения по текущим клиентам

### Binance

- request id генерируется при `BuildSubscribeMessage` / `BuildUnsubscribeMessage`
- `pendingAckRequests` хранит `requestId -> (IsSubscribe, MarketIds)`
- только subscribe ack приводит к `SubscriptionAcknowledged`
- unsubscribe ack должен только завершать внутренний pending state

### Coinbase

- pending request state сейчас не нужен
- ack выводится из payload `type = subscriptions`
- market ids извлекаются из `channels[].product_ids`

### OKX

- pending request state сейчас не нужен
- ack выводится из `event = subscribe`
- market ids извлекаются из `arg.instId`

## Предлагаемый дизайн

Вместо попытки тащить request tracking в `ExchangeWebSocketClientBase`, вводится отдельный internal abstraction layer для клиентов, которым это действительно нужно.

### 1. Internal pending request model

```csharp
internal enum MarketSubscriptionRequestKind : byte
{
    Subscribe,
    Unsubscribe
}

internal sealed record MarketPendingSubscriptionRequest(
    MarketSubscriptionRequestKind Kind,
    string[] MarketIds);
```

### 2. Internal tracker contract

```csharp
internal interface IMarketSubscriptionAckTracker<TKey>
    where TKey : notnull
{
    void Track(TKey key, MarketPendingSubscriptionRequest request);
    bool TryResolve(TKey key, out MarketPendingSubscriptionRequest request);
}
```

Базовая реализация для request-id платформ:

```csharp
internal sealed class RequestIdSubscriptionAckTracker
    : IMarketSubscriptionAckTracker<int>
{
    // ConcurrentDictionary<int, MarketPendingSubscriptionRequest>
}
```

### 3. Exchange-specific ack strategies

Для следующего слоя не нужен polymorphic parser в base. Нужна минимальная стратегия, которую использует сам клиент внутри `OnMessageReceivedAsync`.

```csharp
internal interface IMarketAckStrategy
{
    bool TryHandleAck(
        JsonElement root,
        Func<string[], bool, ValueTask> publishAcknowledgedAsync);
}
```

Но этот слой стоит вводить только если после GateIo/других миграций появится хотя бы 3+ платформы одного ack-family. До этого достаточно tracker + локальный parser per client.

## Практический rollout

### Фаза 1

- вынести бинансовский pending request model в shared internal types
- добавить shared tracker implementation для request-id platforms
- переподключить Binance на shared tracker без поведенческих изменений

### Фаза 2

- при миграции следующей request-id платформы проверить, переиспользуется ли tracker как есть
- если да, зафиксировать family `request-id ack`
- если нет, не расширять base раньше времени

### Фаза 3

- только после появления нескольких exchange families решить, нужен ли отдельный `IMarketAckStrategy`

## Почему не встраивать ack-tracking в ExchangeWebSocketClientBase

- `ExchangeWebSocketClientBase` отвечает за transport lifecycle, не за wire-protocol semantics
- часть платформ вообще не имеет request-correlated ack
- попытка встроить request id/ack state в base приведет к пустым hooks и optional branches почти у каждого клиента
- exchange-specific `OnMessageReceivedAsync` уже является корректной точкой для парсинга ack payload

## Рекомендуемый следующий кодовый шаг

1. Вынести shared internal pending request types и tracker implementation.
2. Переключить Binance на shared tracker.
3. Не трогать Coinbase и OKX, пока у stateless ack-family не появится повторяемый abstraction pressure.

## Критерий успеха

- бинансовский exact ack tracking остается неизменным по поведению
- shared tracker переиспользуем для следующей request-id платформы
- `ExchangeWebSocketClientBase` не получает exchange-specific ack branches
- Coinbase/OKX продолжают жить на stateless ack parsing без лишней абстракции
