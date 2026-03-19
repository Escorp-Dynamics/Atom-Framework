# Atom.Web.Services.Polymarket

Полная реализация клиента [Polymarket CLOB API](https://docs.polymarket.com/) для .NET 10+ с поддержкой NativeAOT.

## Оглавление

- [Архитектура](#архитектура)
- [Быстрый старт](#быстрый-старт)
- [WebSocket клиент](#websocket-клиент)
- [REST клиент](#rest-клиент)
- [Аутентификация](#аутентификация)
- [Подписка ордеров (EIP-712)](#подписка-ордеров-eip-712)
- [Стриминг цен](#стриминг-цен)
- [Портфель и позиции](#портфель-и-позиции)
- [Резолвер событий](#резолвер-событий)
- [История P&L](#история-pnl)
- [Система алертов](#система-алертов)
- [Мульти-портфель менеджер](#мульти-портфель-менеджер)
- [Экспорт данных](#экспорт-данных)
- [Авто-трейдинг стратегии](#авто-трейдинг-стратегии)
- [Бэктестирование](#бэктестирование)
- [Авто-исполнение ордеров](#авто-исполнение-ордеров)
- [Webhook-уведомления](#webhook-уведомления)
- [Риск-менеджмент](#риск-менеджмент)
- [Визуализация](#визуализация)
- [Конфигурация](#конфигурация)
- [Middleware](#middleware)
- [Rate Limiter и Retry Policy](#rate-limiter-и-retry-policy)
- [NativeAOT совместимость](#nativeaot-совместимость)
- [Модели данных](#модели-данных)

---

## Архитектура

```
PolymarketPortfolioManager          ← центральный оркестратор
├── PolymarketClient                ← WebSocket (market + user каналы)
├── PolymarketPriceStream           ← кеш цен реального времени
├── PolymarketEventResolver         ← мониторинг резолюций (REST-поллинг)
├── PolymarketAlertSystem           ← алерты по P&L, ценам, событиям
├── PolymarketPortfolioTracker[]    ← трекеры позиций (по одному на профиль)
│   └── PolymarketPnLHistory        ← снимки P&L (опционально)
├── PolymarketRestClient            ← REST API (публичные + авторизованные)
│   ├── PolymarketRateLimiter       ← Token Bucket (100 req / 10s)
│   ├── PolymarketRetryPolicy       ← экспоненциальный backoff
│   └── IPolymarketMiddleware[]     ← пайплайн middleware
├── PolymarketDataExporter          ← экспорт CSV/JSON
├── PolymarketBacktester            ← бэктестирование стратегий (Sharpe, drawdown)
├── PolymarketOrderExecutor         ← авто-исполнение ордеров по сигналам
├── PolymarketWebhookNotifier       ← уведомления (Telegram, Discord, Slack)
├── PolymarketRiskManager           ← Stop-Loss, Take-Profit, Trailing Stop
├── PolymarketVisualizer            ← ASCII-графики, таблицы, отчёты
├── PolymarketConfigManager         ← JSON-конфигурация торговой системы
└── IPolymarketStrategy             ← авто-трейдинг стратегии
    ├── PolymarketMomentumStrategy
    ├── PolymarketMeanReversionStrategy
    └── PolymarketArbitrageStrategy
```

---

## Быстрый старт

### WebSocket — подписка на рыночные данные

```csharp
await using var client = new PolymarketClient();

client.BookSnapshotReceived += async (sender, e) =>
{
    Console.WriteLine($"Книга ордеров: {e.Snapshot.AssetId} — {e.Snapshot.Buys?.Length} bid / {e.Snapshot.Sells?.Length} ask");
};

client.PriceChanged += async (sender, e) =>
{
    Console.WriteLine($"Цена изменилась: {e.PriceChange.AssetId}");
};

await client.SubscribeMarketAsync(markets: ["condition_id_1", "condition_id_2"]);

// Ожидание событий...
await Task.Delay(TimeSpan.FromMinutes(5));
```

### REST — получение данных рынка

```csharp
using var rest = new PolymarketRestClient();

var markets = await rest.GetMarketsAsync();
var orderBook = await rest.GetOrderBookAsync("token_id_123");
var price = await rest.GetPriceAsync("token_id_123", PolymarketSide.Buy);
```

### Полный пайплайн: портфель + алерты + P&L

```csharp
await using var manager = new PolymarketPortfolioManager();

var profile = manager.CreatePortfolio("main", "Основной портфель",
    strategy: "momentum", enablePnLHistory: true);

var auth = new PolymarketAuth
{
    ApiKey = "...", Secret = "...", Passphrase = "..."
};

await profile.Tracker.SubscribeAsync(auth,
    conditionIds: ["cond_1", "cond_2"]);

manager.AlertSystem.AddAlert(new PolymarketAlertDefinition
{
    Id = "pnl-drop",
    Condition = PolymarketAlertCondition.PortfolioPnLThreshold,
    Direction = PolymarketAlertDirection.Below,
    Threshold = -100.0,
    Description = "P&L портфеля упал ниже -$100"
});

manager.AlertSystem.AlertTriggered += async (sender, e) =>
{
    Console.WriteLine($"⚠ Алерт: {e.Alert.Description} (значение: {e.CurrentValue:F2})");
};

manager.StartResolverPolling();
```

### Полный пайплайн: конфиг → стратегии → риски → webhook → визуализация

```csharp
// 1. Загружаем конфигурацию
var configManager = new PolymarketConfigManager();
var config = configManager.LoadFromFile("polymarket-config.json");

// 2. Создаём объекты из конфигурации
var strategiesWithAssets = configManager.CreateStrategies(config!);
var riskRules = configManager.CreateRiskRules(config!);
var webhookConfigs = configManager.CreateWebhookConfigs(config!);
var limits = configManager.CreateLimits(config!);

// 3. Инициализируем инфраструктуру
await using var client = new PolymarketClient();
using var priceStream = new PolymarketPriceStream();
priceStream.ConnectClient(client);

using var restClient = new PolymarketRestClient(apiKey: "...", secret: "...", passphrase: "...");
var tracker = new PolymarketPortfolioTracker(client, priceStream);

// 4. Настраиваем риск-менеджмент
using var risk = new PolymarketRiskManager { Limits = limits };
foreach (var rule in riskRules)
    risk.AddRule(rule);
risk.ConnectPriceStream(priceStream);
risk.ConnectTracker(tracker);

risk.RiskTriggered += async (sender, e) =>
{
    Console.WriteLine($"РИСК: {e.OrderType} на {e.AssetId} при цене {e.CurrentPrice}");
};

// 5. Настраиваем авто-исполнение
var executor = new PolymarketOrderExecutor(restClient, priceStream);
executor.DryRun = config.DryRun;
executor.MinConfidence = config.MinConfidence;
executor.EvaluationInterval = TimeSpan.FromSeconds(config.EvaluationIntervalSeconds);
executor.OrderCooldown = TimeSpan.FromSeconds(config.OrderCooldownSeconds);

foreach (var (strategy, assetIds) in strategiesWithAssets)
    executor.AddStrategy(strategy, assetIds);

// 6. Настраиваем webhook-уведомления
using var notifier = new PolymarketWebhookNotifier();
foreach (var wc in webhookConfigs)
    notifier.AddWebhook(wc);
notifier.ConnectOrderExecutor(executor);

// 7. Запускаем всё
var auth = new PolymarketAuth { ApiKey = "...", Secret = "...", Passphrase = "..." };
await tracker.SubscribeAsync(auth, conditionIds: ["cond_1"]);
executor.Start();

// 8. Визуализация текущего состояния
var viz = new PolymarketVisualizer { Width = 60, Height = 15 };
Console.WriteLine(viz.RenderPositionsTable(tracker.Positions.Values));
Console.WriteLine(viz.RenderPortfolioSummary(tracker.GetSummary()));

// 9. Бэктест стратегии
var backtester = new PolymarketBacktester { InitialBalance = 10_000, FeeRateBps = 10 };
using var momentum = new PolymarketMomentumStrategy(lookbackPeriod: 10, momentumThreshold: 0.02, positionSize: 200);
var result = backtester.Run(momentum, "token-yes", historicalPrices);

Console.WriteLine(viz.RenderBacktestSummary(result));
Console.WriteLine(viz.RenderEquityCurve(result));

// 10. Сохраняем обновлённую конфигурацию
configManager.SaveToFile(config, "polymarket-config.json");
```

---

## WebSocket клиент

`PolymarketClient` подключается к Polymarket WebSocket API с поддержкой двух независимых каналов: **Market** (рыночные данные) и **User** (пользовательские ордера/сделки).

### Конструкторы

```csharp
// По умолчанию
new PolymarketClient();

// С параметрами
new PolymarketClient(
    baseUrl: "wss://ws-subscriptions-clob.polymarket.com/ws",
    maxMessageSize: 1_048_576,           // 1 МБ
    reconnectDelay: TimeSpan.FromSeconds(5),
    maxReconnectAttempts: 0,             // 0 = бесконечно
    pingInterval: TimeSpan.FromSeconds(30));
```

### События

| Событие | Тип аргумента | Описание |
|---------|---------------|----------|
| `BookSnapshotReceived` | `PolymarketBookEventArgs` | Полный снимок книги ордеров |
| `PriceChanged` | `PolymarketPriceChangeEventArgs` | Инкрементальные изменения цен |
| `LastTradePriceReceived` | `PolymarketLastTradePriceEventArgs` | Цена последней сделки |
| `TickSizeChanged` | `PolymarketTickSizeChangeEventArgs` | Изменение минимального шага цены |
| `OrderUpdated` | `PolymarketOrderEventArgs` | Обновление ордера (user-канал) |
| `TradeReceived` | `PolymarketTradeEventArgs` | Новая сделка (user-канал) |
| `MarketDisconnected` | `PolymarketDisconnectedEventArgs` | Разрыв market-соединения |
| `UserDisconnected` | `PolymarketDisconnectedEventArgs` | Разрыв user-соединения |
| `ErrorOccurred` | `PolymarketErrorEventArgs` | Ошибка обработки |
| `Reconnected` | `PolymarketReconnectedEventArgs` | Успешное переподключение |

### Авто-реконнект

```csharp
client.AutoReconnectEnabled = true; // по умолчанию true

// Экспоненциальный backoff: 5s → 10s → 20s → ... (макс. 5 минут)
// Ping/Pong keepalive каждые 30 секунд
```

### Методы

```csharp
// Market-канал
await client.SubscribeMarketAsync(markets: ["cond_1"], assetsIds: ["token_1"]);
await client.UnsubscribeMarketAsync(markets: ["cond_1"]);
await client.DisconnectMarketAsync();

// User-канал (требуется аутентификация)
await client.SubscribeUserAsync(auth, markets: ["cond_1"]);
await client.UnsubscribeUserAsync(markets: ["cond_1"]);
await client.DisconnectUserAsync();
```

---

## REST клиент

`PolymarketRestClient` реализует все эндпоинты Polymarket CLOB REST API.

### Публичные эндпоинты

```csharp
using var rest = new PolymarketRestClient();

// Рынки
var markets = await rest.GetMarketsAsync(nextCursor: null);
var market = await rest.GetMarketAsync("condition_id");

// Книги ордеров
var book = await rest.GetOrderBookAsync("token_id");
var books = await rest.GetOrderBooksAsync(["token_1", "token_2"]);

// Цены
var price = await rest.GetPriceAsync("token_id", PolymarketSide.Buy);
var prices = await rest.GetPricesAsync(["t1", "t2"], PolymarketSide.Sell);
var mid = await rest.GetMidpointAsync("token_id");
var spread = await rest.GetSpreadAsync("token_id");
var lastTrade = await rest.GetLastTradePriceAsync("token_id");
var tickSize = await rest.GetTickSizeAsync("token_id");
var negRisk = await rest.IsNegRiskAsync("token_id");
```

### Авторизованные эндпоинты

```csharp
rest.SetAuth(new PolymarketAuth
{
    ApiKey = "...", Secret = "...", Passphrase = "..."
});

// Ордера
var result = await rest.CreateOrderAsync(new PolymarketCreateOrderRequest { ... });
var cancel = await rest.CancelOrderAsync("order_id");
var cancelAll = await rest.CancelAllOrdersAsync();
var openOrders = await rest.GetOpenOrdersAsync(market: "cond_id");

// Сделки и баланс
var trades = await rest.GetTradesAsync(market: "cond_id");
var balance = await rest.GetBalanceAllowanceAsync();
```

### Middleware пайплайн

```csharp
rest.Middleware.Add(new PolymarketLoggingMiddleware(Console.WriteLine));
rest.Middleware.Add(new PolymarketMetricsMiddleware());
rest.Middleware.Add(new PolymarketHeadersMiddleware()
    .AddHeader("X-Custom", "value"));
```

---

## Аутентификация

### HMAC-SHA256 (REST API)

```csharp
var timestamp = PolymarketApiSigner.GetTimestamp();
var nonce = PolymarketApiSigner.GenerateNonce();
var signature = PolymarketApiSigner.Sign(
    apiSecret: "secret",
    timestamp: timestamp,
    nonce: nonce,
    method: "GET",
    requestPath: "/markets",
    body: null);
```

Заголовки автоматически добавляются `PolymarketRestClient` при вызове `SetAuth()`:

- `POLY-ADDRESS` — API ключ
- `POLY-SIGNATURE` — HMAC-SHA256 подпись
- `POLY-TIMESTAMP` — Unix timestamp
- `POLY-NONCE` — Уникальный nonce
- `POLY-API-KEY` — API ключ
- `POLY-PASSPHRASE` — Passphrase

---

## Подписка ордеров (EIP-712)

Для размещения ордеров на Polymarket требуется подпись EIP-712 (Ethereum typed structured data).

```csharp
var order = new PolymarketSignedOrder
{
    Salt = "12345",
    Maker = "0xYourAddress",
    Signer = "0xYourAddress",
    Taker = "0x0000000000000000000000000000000000000000",
    TokenId = "token_id",
    MakerAmount = "1000000",    // в USDC (6 decimals)
    TakerAmount = "2000000",
    Expiration = "0",
    Nonce = "0",
    FeeRateBps = "0"
};

var signed = PolymarketOrderSigner.SignOrder(order, "private_key_hex", negRisk: false);
// signed содержит Signature — готов к отправке через CreateOrderAsync
```

**Крипто-примитивы:**

- `Keccak256.Hash(data)` — чистая managed реализация Keccak-256 (pre-NIST, Ethereum-вариант)
- `PolymarketOrderSigner.ComputeOrderDigest(order, negRisk)` — EIP-712 дайджест
- Поддержка Polygon (chainId=137), отдельные контракты для обычных и neg-risk рынков

---

## Стриминг цен

`PolymarketPriceStream` поддерживает кеш цен в реальном времени через WebSocket.

```csharp
await using var stream = new PolymarketPriceStream(client);

stream.PriceUpdated += async (sender, e) =>
{
    var snap = e.Snapshot;
    Console.WriteLine($"{snap.AssetId}: bid={snap.BestBid} ask={snap.BestAsk} mid={snap.Midpoint}");
};

await stream.SubscribeAsync(conditionIds: ["cond_1", "cond_2"]);

// Прямой доступ к кешу
var price = stream.GetPrice("token_id");
var allPrices = stream.Prices; // IReadOnlyDictionary
```

### Поля `PolymarketPriceSnapshot`

| Поле | Тип | Описание |
|------|-----|----------|
| `AssetId` | `string` | Идентификатор токена |
| `Market` | `string?` | Идентификатор рынка |
| `BestBid` | `string?` | Лучшая цена покупки |
| `BestAsk` | `string?` | Лучшая цена продажи |
| `LastTradePrice` | `string?` | Цена последней сделки |
| `Midpoint` | `string?` | Средняя цена (bid+ask)/2 |
| `TickSize` | `string?` | Минимальный шаг цены |
| `LastUpdateTicks` | `long` | Тики последнего обновления |

---

## Портфель и позиции

`PolymarketPortfolioTracker` отслеживает позиции с VWAP-расчётом стоимости.

```csharp
await using var tracker = new PolymarketPortfolioTracker(client, priceStream);

tracker.PositionChanged += async (sender, e) =>
{
    var pos = e.Position;
    Console.WriteLine($"{pos.AssetId}: qty={pos.Quantity} P&L={pos.UnrealizedPnL:F2}");
};

// Подписка через WebSocket
await tracker.SubscribeAsync(auth, conditionIds: ["cond_1"]);

// Или синхронизация из REST
await tracker.SyncFromRestAsync(restClient, market: "cond_1");

// Или из массива сделок
tracker.SyncFromTrades(trades);

// Получение позиции и сводки
var position = tracker.GetPosition("token_id");
var summary = tracker.GetSummary();
```

### `PolymarketPortfolioSummary`

| Поле | Описание |
|------|----------|
| `OpenPositions` | Количество открытых позиций |
| `ClosedPositions` | Количество закрытых позиций |
| `TotalMarketValue` | Рыночная стоимость |
| `TotalCostBasis` | Базис стоимости |
| `TotalUnrealizedPnL` | Нереализованный P&L |
| `TotalRealizedPnL` | Реализованный P&L |
| `TotalFees` | Суммарные комиссии |
| `NetPnL` | Чистый P&L (realized + unrealized - fees) |

---

## Резолвер событий

`PolymarketEventResolver` мониторит рынки на закрытие и резолюцию через REST-поллинг.

```csharp
await using var resolver = new PolymarketEventResolver(
    restClient,
    pollInterval: TimeSpan.FromSeconds(60));

resolver.MarketResolved += async (sender, e) =>
{
    Console.WriteLine($"Рынок {e.Resolution.ConditionId} разрешён: {e.Resolution.WinningOutcome}");
};

resolver.MarketClosed += async (sender, e) =>
{
    Console.WriteLine($"Рынок {e.Market.ConditionId} закрыт");
};

resolver.Track("condition_id_1");
resolver.TrackMany(["cond_2", "cond_3"]);
resolver.StartPolling();
```

### Интеграция с портфелем

```csharp
tracker.ConnectResolver(resolver);
// Теперь при резолюции рынка позиции автоматически обновляются (ApplyResolution)
```

---

## История P&L

`PolymarketPnLHistory` записывает периодические снимки P&L.

```csharp
var history = new PolymarketPnLHistory(
    tracker,
    snapshotInterval: TimeSpan.FromMinutes(5),
    maxSnapshots: 288);  // 24 часа × 5 минут

history.SnapshotRecorded += async (sender, e) =>
{
    Console.WriteLine($"Снимок: {e.Snapshot.NetPnL:F2} ({e.Snapshot.OpenPositions} позиций)");
};

history.Start();

// Ручной снимок
var snap = history.TakeSnapshot();

// Массив всех снимков (для графиков)
var all = history.ToArray();

await history.StopAsync();
```

---

## Система алертов

`PolymarketAlertSystem` реагирует на пороговые значения P&L, цен и события рынков.

```csharp
var alerts = new PolymarketAlertSystem();
alerts.ConnectTracker(tracker);
alerts.ConnectResolver(resolver);

alerts.AlertTriggered += async (sender, e) =>
{
    Console.WriteLine($"Алерт '{e.Alert.Description}' — значение {e.CurrentValue:F2}");
};

// P&L позиции
alerts.AddAlert(new PolymarketAlertDefinition
{
    Id = "token-pnl",
    Condition = PolymarketAlertCondition.PnLThreshold,
    Direction = PolymarketAlertDirection.Below,
    Threshold = -50.0,
    AssetId = "token_id"
});

// Цена токена
alerts.AddAlert(new PolymarketAlertDefinition
{
    Id = "price-alert",
    Condition = PolymarketAlertCondition.PriceThreshold,
    Direction = PolymarketAlertDirection.Above,
    Threshold = 0.75,
    AssetId = "token_id"
});

// Портфель (суммарный P&L)
alerts.AddAlert(new PolymarketAlertDefinition
{
    Id = "portfolio-pnl",
    Condition = PolymarketAlertCondition.PortfolioPnLThreshold,
    Direction = PolymarketAlertDirection.Below,
    Threshold = -200.0
});

// Закрытие/резолюция рынка
alerts.AddAlert(new PolymarketAlertDefinition
{
    Id = "market-close",
    Condition = PolymarketAlertCondition.MarketClosed,
    ConditionId = "condition_id"
});
```

### Параметры `PolymarketAlertDefinition`

| Параметр | Тип | Описание |
|----------|-----|----------|
| `Id` | `string` | Уникальный идентификатор |
| `Condition` | `PolymarketAlertCondition` | Тип условия |
| `Direction` | `PolymarketAlertDirection` | `Above` / `Below` |
| `Threshold` | `double` | Пороговое значение |
| `AssetId` | `string?` | ID токена (для PnL/Price) |
| `ConditionId` | `string?` | ID рынка (для MarketClosed/Resolved) |
| `Description` | `string?` | Описание алерта |
| `OneShot` | `bool` | Одноразовый (по умолчанию `true`) |
| `IsEnabled` | `bool` | Активен (по умолчанию `true`) |

---

## Мульти-портфель менеджер

`PolymarketPortfolioManager` — центральный оркестратор для управления несколькими портфелями с общей инфраструктурой.

```csharp
await using var manager = new PolymarketPortfolioManager();

// Создание портфелей с тегами и стратегиями
var main = manager.CreatePortfolio("main", "Основной",
    strategy: "momentum", tags: ["active"], enablePnLHistory: true);

var hedge = manager.CreatePortfolio("hedge", "Хеджирование",
    strategy: "mean-reversion", tags: ["hedge", "active"]);

// Фильтрация
var byStrategy = manager.GetPortfoliosByStrategy("momentum");
var byTag = manager.GetPortfoliosByTag("active");

// Агрегированная сводка
var summary = manager.GetAggregatedSummary();
Console.WriteLine($"Всего: {summary.PortfolioCount} портфелей, " +
    $"Net P&L: {summary.NetPnL:F2}");

// Синхронизация из REST
await manager.SyncPortfolioAsync("main", restClient, market: "cond_1");

// Мониторинг резолюций
manager.Resolver.Track("cond_1");
manager.StartResolverPolling();
```

---

## Экспорт данных

`PolymarketDataExporter` экспортирует позиции, историю P&L и сделки в CSV и JSON.

```csharp
var exporter = new PolymarketDataExporter();

// Экспорт позиций в CSV
exporter.ExportPositionsCsv(tracker.Positions.Values, "positions.csv");

// Экспорт истории P&L в JSON
exporter.ExportPnLHistoryJson(history.ToArray(), "pnl_history.json");

// Экспорт сделок
exporter.ExportTradesCsv(trades, "trades.csv");

// Экспорт в строку (для отправки по сети и т.п.)
string csv = exporter.ExportPositionsCsvString(tracker.Positions.Values);
string json = exporter.ExportPnLHistoryJsonString(history.ToArray());

// Полный отчёт портфеля (JSON)
exporter.ExportPortfolioReportJson(tracker, history, "report.json");
```

---

## Авто-трейдинг стратегии

Интерфейс `IPolymarketStrategy` и встроенные стратегии для автоматической торговли.

```csharp
// Momentum — покупка при движении цены вверх
var momentum = new PolymarketMomentumStrategy(
    lookbackPeriod: 10,
    momentumThreshold: 0.05,
    positionSize: 100.0);

// Mean Reversion — возврат к среднему
var meanRev = new PolymarketMeanReversionStrategy(
    lookbackPeriod: 20,
    deviationThreshold: 2.0,
    positionSize: 50.0);

// Арбитраж — поиск расхождений между рынками
var arb = new PolymarketArbitrageStrategy(
    spreadThreshold: 0.02,
    positionSize: 200.0);

// Получение сигналов
var signal = momentum.Evaluate(priceStream, "token_id");
if (signal.Action != PolymarketTradeAction.Hold)
{
    Console.WriteLine($"Сигнал: {signal.Action} {signal.AssetId} " +
        $"qty={signal.Quantity:F2} @ {signal.Price}");
}
```

---

## Бэктестирование

`PolymarketBacktester` прогоняет стратегию по историческим ценовым данным и рассчитывает торговые метрики.

```csharp
var bt = new PolymarketBacktester
{
    InitialBalance = 10_000,
    FeeRateBps = 100  // 1% комиссия
};

using var strategy = new PolymarketMomentumStrategy(
    lookbackPeriod: 10, momentumThreshold: 0.03, positionSize: 500);

// Исторические данные (из файла, REST API и т.п.)
var priceData = new PolymarketPricePoint[]
{
    new() { Midpoint = 0.40, BestBid = 0.39, BestAsk = 0.41 },
    new() { Midpoint = 0.42, BestBid = 0.41, BestAsk = 0.43 },
    // ...
};

var result = bt.Run(strategy, "token_id", priceData);

Console.WriteLine($"Стратегия: {result.StrategyName}");
Console.WriteLine($"P&L: {result.NetPnL:F2} ({result.ReturnPercent:F1}%)");
Console.WriteLine($"Сделок: {result.TotalTrades}, Win rate: {result.WinRate:F1}%");
Console.WriteLine($"Sharpe: {result.SharpeRatio:F2}, Max drawdown: {result.MaxDrawdownPercent:F1}%");
Console.WriteLine($"Profit factor: {result.ProfitFactor:F2}");
```

### Метрики `PolymarketBacktestResult`

| Метрика | Описание |
|---------|----------|
| `NetPnL` | Чистая прибыль/убыток |
| `ReturnPercent` | Доходность (%) |
| `WinRate` | Процент прибыльных сделок |
| `SharpeRatio` | Коэффициент Шарпа (annualized) |
| `MaxDrawdownPercent` | Максимальная просадка (%) |
| `ProfitFactor` | Сумма прибылей / сумма убытков |
| `AveragePnLPerTrade` | Средний P&L на сделку |
| `EquityCurve` | Кривая баланса (для графиков) |

---

## Авто-исполнение ордеров

`PolymarketOrderExecutor` связывает стратегии с REST-клиентом для автоматического размещения ордеров.

```csharp
using var rest = new PolymarketRestClient();
rest.SetAuth(auth);

await using var executor = new PolymarketOrderExecutor(rest, priceStream);

// DryRun = true по умолчанию (безопасный режим)
executor.DryRun = true;
executor.MinConfidence = 0.5;
executor.OrderCooldown = TimeSpan.FromSeconds(60);
executor.EvaluationInterval = TimeSpan.FromSeconds(30);

// Регистрируем стратегии
executor.AddStrategy(momentum, ["yes_token", "no_token"]);
executor.AddStrategy(meanRev, ["other_token"]);

// Подписываемся на события
executor.SignalGenerated += async (s, e) =>
    Console.WriteLine($"Сигнал: {e.StrategyName} → {e.Signal.Action} {e.Signal.AssetId}");

executor.OrderExecuted += async (s, e) =>
    Console.WriteLine($"{(e.Success ? "✅" : "❌")} Ордер: {e.Signal.AssetId}");

// Запуск автоматического цикла
executor.Start();

// Или единичная оценка
await executor.EvaluateOnceAsync();
```

---

## Webhook-уведомления

`PolymarketWebhookNotifier` отправляет алерты через HTTP webhooks в Telegram, Discord, Slack и произвольные URL.

```csharp
using var notifier = new PolymarketWebhookNotifier();

// Telegram
notifier.AddWebhook(new PolymarketWebhookConfig
{
    Id = "tg",
    Url = "https://api.telegram.org/bot<TOKEN>/sendMessage",
    Type = PolymarketWebhookType.Telegram,
    TelegramChatId = "-123456789"
});

// Discord
notifier.AddWebhook(new PolymarketWebhookConfig
{
    Id = "discord",
    Url = "https://discord.com/api/webhooks/123/abc",
    Type = PolymarketWebhookType.Discord
});

// Slack
notifier.AddWebhook(new PolymarketWebhookConfig
{
    Id = "slack",
    Url = "https://hooks.slack.com/services/T00/B00/XXX",
    Type = PolymarketWebhookType.Slack
});

// Подключение к алертам и ордерам
notifier.ConnectAlertSystem(alertSystem);
notifier.ConnectOrderExecutor(executor);

// Или произвольный текст
await notifier.SendMessageAsync("Портфель обновлён: +$150");
```

Поддерживаемые платформы:

| Тип | Формат payload |
|-----|----------------|
| `Generic` | `{"message": "...", "source": "Polymarket", "timestamp": "..."}` |
| `Telegram` | `{"chat_id": "...", "text": "...", "parse_mode": "HTML"}` |
| `Discord` | `{"content": "...", "username": "Polymarket Bot"}` |
| `Slack` | `{"text": "..."}` |

---

## Middleware

### Логирование

```csharp
rest.Middleware.Add(new PolymarketLoggingMiddleware(msg => logger.Info(msg)));
```

### Метрики

```csharp
var metrics = new PolymarketMetricsMiddleware();
rest.Middleware.Add(metrics);

// После нескольких запросов:
Console.WriteLine($"Запросов: {metrics.TotalRequests}, Ошибок: {metrics.FailedRequests}");
Console.WriteLine($"Среднее время: {metrics.AverageResponseTimeMs:F1}ms");
```

### Кастомные заголовки

```csharp
rest.Middleware.Add(new PolymarketHeadersMiddleware()
    .AddHeader("X-Request-Source", "atom-client")
    .AddHeader("X-Version", "1.0"));
```

### Собственный middleware

```csharp
public sealed class MyMiddleware : IPolymarketMiddleware
{
    public ValueTask<bool> OnRequestAsync(PolymarketRequestContext ctx, CancellationToken ct)
    {
        // true = продолжить, false = отменить запрос
        return ValueTask.FromResult(true);
    }

    public ValueTask OnResponseAsync(PolymarketResponseContext ctx, CancellationToken ct)
    {
        if (!ctx.IsSuccess)
            Console.WriteLine($"Ошибка {ctx.StatusCode}: {ctx.Request.Path}");
        return ValueTask.CompletedTask;
    }
}
```

---

## Rate Limiter и Retry Policy

### Token Bucket Rate Limiter

```csharp
rest.RateLimiter = new PolymarketRateLimiter(
    maxTokens: 100,
    refillPeriodSeconds: 10);

// Ручное использование
if (limiter.TryAcquire())
    Console.WriteLine("Токен получен");

await limiter.WaitAsync(cancellationToken); // блокирует до получения токена
```

### Retry Policy с экспоненциальным backoff

```csharp
rest.RetryPolicy = new PolymarketRetryPolicy(
    maxRetries: 3,
    initialDelay: TimeSpan.FromMilliseconds(500),
    maxDelay: TimeSpan.FromSeconds(30));

// Повторяются: HTTP 429, 5xx, сетевые ошибки
// Jitter: ±25% для предотвращения thundering herd
```

---

## NativeAOT совместимость

Все типы используют source-generated JSON через `PolymarketJsonContext`:

```csharp
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PolymarketMessage))]
[JsonSerializable(typeof(PolymarketMarket))]
// ... все типы зарегистрированы
internal sealed partial class PolymarketJsonContext : JsonSerializerContext;
```

Нет рефлексии. Нет `dynamic`. Нет `System.Reflection.Emit`. Полная совместимость с `PublishAot=true`.

---

## Риск-менеджмент

`PolymarketRiskManager` — менеджер рисков с Stop-Loss, Take-Profit, Trailing Stop и позиционными лимитами.

```csharp
using var risk = new PolymarketRiskManager();

// Глобальные лимиты
risk.Limits = new PolymarketPortfolioLimits
{
    MaxPositionSize = 1000,
    MaxOpenPositions = 10,
    MaxPortfolioLoss = 5000,
    MaxPositionPercent = 0.25,
    MaxDailyLoss = 500
};

// Правило для конкретного актива
risk.AddRule(new PolymarketRiskRule
{
    AssetId = "token-yes",
    StopLossPrice = 0.30,
    TakeProfitPrice = 0.85,
    TrailingStopPercent = 0.10,
    MaxLossPerPosition = 200
});

// Подключение к стриму цен и трекеру
risk.ConnectPriceStream(priceStream);
risk.ConnectTracker(tracker);

// Событие при срабатывании стопа
risk.RiskTriggered += async (sender, e) =>
{
    Console.WriteLine($"РИСК: {e.OrderType} на {e.AssetId} при цене {e.CurrentPrice}");
};

// Проверка перед открытием позиции
if (risk.CanOpenPosition("token-yes", 500))
    Console.WriteLine("Позиция разрешена");

// Сброс дневного лимита
risk.ResetDailyLoss();
```

| Тип стопа | Логика |
|-----------|--------|
| `StopLoss` | Закрытие при цене ≤ порога |
| `TakeProfit` | Фиксация прибыли при цене ≥ порога |
| `TrailingStop` | Плавающий стоп от HighWaterMark (%) |

---

## Визуализация

`PolymarketVisualizer` — ASCII-графики и текстовые отчёты для консоли, логов и Markdown.

```csharp
var viz = new PolymarketVisualizer { Width = 60, Height = 15 };

// ASCII-график значений
var chart = viz.RenderChart(equityCurve, "Equity Curve");
Console.WriteLine(chart);

// Equity curve из бэктеста
Console.WriteLine(viz.RenderEquityCurve(backtestResult));

// История P&L
Console.WriteLine(viz.RenderPnLHistory(pnlSnapshots));

// Таблица позиций
Console.WriteLine(viz.RenderPositionsTable(positions));

// Сводка бэктеста
Console.WriteLine(viz.RenderBacktestSummary(backtestResult));

// Сводка портфеля
Console.WriteLine(viz.RenderPortfolioSummary(portfolioSummary));
```

---

## Конфигурация

`PolymarketConfigManager` — JSON-конфигурация всей торговой системы. NativeAOT-совместима (`PolymarketConfigJsonContext`).

```csharp
var manager = new PolymarketConfigManager();

// Создание конфигурации
var config = new PolymarketSystemConfig
{
    DryRun = true,
    MinConfidence = 0.5,
    EvaluationIntervalSeconds = 15,
    OrderCooldownSeconds = 30,
    Strategies =
    [
        new PolymarketStrategyConfig
        {
            Type = "Momentum",
            LookbackPeriod = 10,
            Threshold = 0.02,
            PositionSize = 100,
            AssetIds = ["token-a", "token-b"]
        }
    ],
    RiskRules =
    [
        new PolymarketRiskRuleConfig
        {
            AssetId = "token-a",
            StopLossPrice = 0.30,
            TakeProfitPrice = 0.90
        }
    ],
    Limits = new PolymarketLimitsConfig
    {
        MaxPositionSize = 1000,
        MaxOpenPositions = 10
    },
    Webhooks =
    [
        new PolymarketWebhookConfigData
        {
            Id = "telegram",
            Url = "https://api.telegram.org/bot.../sendMessage",
            Type = PolymarketWebhookType.Telegram,
            TelegramChatId = "12345"
        }
    ]
};

// Сохранение/загрузка
manager.SaveToFile(config, "config.json");
var loaded = manager.LoadFromFile("config.json");

// Создание объектов из конфигурации
var strategies = manager.CreateStrategies(loaded!);
var riskRules  = manager.CreateRiskRules(loaded!);
var webhooks   = manager.CreateWebhookConfigs(loaded!);
var limits     = manager.CreateLimits(loaded!);
```

---

## Модели данных

### Перечисления

| Enum | Значения |
|------|----------|
| `PolymarketChannel` | `Market`, `User` |
| `PolymarketEventType` | `Book`, `PriceChange`, `LastTradePrice`, `TickSizeChange`, `Order`, `Trade` |
| `PolymarketSide` | `Buy`, `Sell` |
| `PolymarketOrderStatus` | `Live`, `Cancelled`, `Matched`, `Delayed` |
| `PolymarketOrderType` | `GoodTilCancelled`, `GoodTilDate`, `FillOrKill` |
| `PolymarketTradeStatus` | `Matched`, `Confirmed`, `Failed`, `Retracted` |
| `PolymarketTraderSide` | *(maker/taker)* |
| `PolymarketPositionChangeReason` | `Trade`, `PriceUpdate`, `MarketResolved`, `ManualSync` |
| `PolymarketMarketStatus` | `Active`, `Closed`, `Resolved`, `Voided`, `Unknown` |
| `PolymarketAlertCondition` | `PnLThreshold`, `PriceThreshold`, `MarketClosed`, `MarketResolved`, `PortfolioPnLThreshold` |
| `PolymarketAlertDirection` | `Above`, `Below` |

### WebSocket модели

| Тип | Описание |
|-----|----------|
| `PolymarketBookSnapshot` | Снимок книги ордеров (buys/sells) |
| `PolymarketPriceChange` | Инкрементальные изменения (changes) |
| `PolymarketLastTradePrice` | Цена последней сделки |
| `PolymarketTickSizeChange` | Изменение тик-сайза |
| `PolymarketOrder` | Ордер пользователя |
| `PolymarketTrade` | Сделка пользователя |
| `PolymarketMessage` | Универсальное сообщение (union-тип) |
| `PolymarketSubscription` | Запрос подписки/отписки |

### REST модели

| Тип | Описание |
|-----|----------|
| `PolymarketMarket` | Рынок (conditionId, tokens, status) |
| `PolymarketToken` | Токен рынка (tokenId, outcome, price, winner) |
| `PolymarketOrderBook` | Книга ордеров (bids/asks) |
| `PolymarketPriceResponse` | Цена (price, mid, spread, tickSize) |
| `PolymarketCreateOrderRequest` | Запрос создания ордера |
| `PolymarketSignedOrder` | Подписанный ордер (EIP-712) |
| `PolymarketOrderResponse` | Ответ на создание ордера |
| `PolymarketCancelResponse` | Ответ на отмену ордера |
| `PolymarketBalanceAllowance` | Баланс и allowance |

### Портфель и трекинг

| Тип | Описание |
|-----|----------|
| `PolymarketPosition` | Позиция (qty, VWAP-cost, P&L) |
| `PolymarketPortfolioSummary` | Сводка портфеля |
| `PolymarketResolution` | Результат резолюции рынка |
| `PolymarketTrackedMarket` | Отслеживаемый рынок |
| `PolymarketPnLSnapshot` | Снимок P&L |
| `PolymarketAlertDefinition` | Определение алерта |
| `PolymarketPriceSnapshot` | Снимок цены (кеш) |
| `PolymarketPortfolioProfile` | Профиль портфеля (менеджер) |
| `PolymarketAggregatedSummary` | Агрегированная сводка (менеджер) |

---

## Универсальные контракты Markets/

Все основные компоненты Polymarket реализуют универсальные интерфейсы из `Atom.Web.Services.Markets`, что позволяет:

- Писать стратегии, работающие на любой площадке
- Использовать единый API для risk-менеджмента, бэктестирования и визуализации
- Легко переносить код между Polymarket, Binance, Kraken и другими биржами

### Таблица соответствий

| Polymarket класс | Markets/ интерфейс | Особенности |
|---|---|---|
| `PolymarketClient` | `IMarketClient` | PlatformName = "Polymarket", маппинг Subscribe → SubscribeMarket |
| `PolymarketRestClient` | `IMarketRestClient` | CreateOrder → MarketException (требует EIP-712 подпись) |
| `PolymarketPriceStream` | `IMarketPriceStream` | GetPrice возвращает `IMarketPriceSnapshot` |
| `PolymarketPriceSnapshot` | `IMarketPriceSnapshot` | Явная реализация: string → double парсинг |
| `PolymarketPosition` | `IMarketPosition` | Полное соответствие свойств |
| `PolymarketPortfolioSummary` | `IMarketPortfolioSummary` | Полное соответствие свойств |
| `PolymarketPortfolioTracker` | `IMarketPortfolioTracker` | Явная реализация GetPosition/GetSummary |
| `PolymarketPnLHistory` | `IMarketPnLHistory` | Явная реализация Latest/TakeSnapshot |
| `PolymarketRiskManager` | `IMarketRiskManager` | Явная реализация Limits/AddRule/GetRule |
| `PolymarketAlertSystem` | `IMarketAlertSystem` | Явная реализация AddAlert/GetAlert |
| `PolymarketAlertDefinition` | `IMarketAlertDefinition` | Конверсия enum: PolymarketAlertCondition → AlertCondition |
| `PolymarketOrderExecutor` | `IMarketOrderExecutor` | Явная реализация AddStrategy |
| `PolymarketBacktester` | `IMarketBacktester` | Явная реализация Run |
| `PolymarketDataExporter` | `IMarketDataExporter` | Маршрутизация по ExportFormat (Csv/Json) |
| `PolymarketVisualizer` | `IMarketVisualizer` | Явная реализация всех Render-методов |
| `PolymarketTradeSignal` | `IMarketTradeSignal` | Конверсия: PolymarketTradeAction → TradeAction |
| `IPolymarketStrategy` | `IMarketStrategy` | Наследует IMarketStrategy, добавляет Polymarket-специфику |
| `PolymarketException` | `MarketException` | Базовый класс — MarketException |
| `PolymarketOrderBook` | `IMarketOrderBookSnapshot` | Парсинг string timestamp → DateTimeOffset |

### Использование через универсальный интерфейс

```csharp
using Atom.Web.Services.Markets;
using Atom.Web.Services.Polymarket;

// Работа через универсальный контракт — код не зависит от площадки
IMarketClient client = new PolymarketClient();
await client.SubscribeAsync(["market-id-1", "market-id-2"]);
Console.WriteLine($"Площадка: {client.PlatformName}"); // "Polymarket"

IMarketPriceStream stream = new PolymarketPriceStream();
IMarketPriceSnapshot? price = stream.GetPrice("token-abc");

IMarketRiskManager risk = new PolymarketRiskManager();
risk.AddRule(new PolymarketRiskRule { AssetId = "t1", StopLossPrice = 0.20 });
Console.WriteLine($"Лимит позиций: {risk.Limits.MaxOpenPositions}");
```
