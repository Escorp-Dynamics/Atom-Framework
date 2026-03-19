# Atom.Web.Services.Markets

**Универсальные контракты для торговых платформ** — абстрактный слой интерфейсов и моделей для построения клиентов к произвольным торговым площадкам (криптобиржи, prediction markets, фондовые рынки).

## Архитектура

Отдельный runtime-дизайн для выравнивания биржевых WebSocket-клиентов описан в [ExchangeRuntimeTemplate.md](ExchangeRuntimeTemplate.md).
Отдельная заметка по следующему шагу для Binance stream-selection описана в [Binance/BinanceStreamSelectionDesign.md](Binance/BinanceStreamSelectionDesign.md).
Отдельная заметка по общему ack-tracking после миграций Binance/Coinbase/OKX описана в [AckTrackingDesign.md](AckTrackingDesign.md).

```
Markets/
├── MarketEnums.cs              — 9 универсальных перечислений
├── MarketModels.cs             — 12 интерфейсов моделей данных
├── MarketException.cs          — Базовое исключение
├── IMarketClient.cs            — WebSocket/стриминг клиент
├── IMarketRestClient.cs        — REST API клиент
├── IMarketPriceStream.cs       — Кеш цен в реальном времени
├── IMarketStrategy.cs          — Торговая стратегия (интерфейс)
├── MarketStrategies.cs         — 7 реализаций стратегий + CompositeStrategy
├── IMarketPortfolioTracker.cs  — Портфель + P&L история (интерфейс)
├── MarketPortfolioTracker.cs   — Трекер позиций + MarketPnLHistory
├── IMarketRiskManager.cs       — Stop-Loss, Take-Profit, лимиты (интерфейс)
├── MarketRiskManager.cs        — Реализация риск-менеджера
├── IMarketOrderExecutor.cs     — Авто-исполнение ордеров (интерфейс)
├── MarketOrderExecutor.cs      — Реализация авто-исполнителя
├── IMarketAlertSystem.cs       — Система алертов (интерфейс)
├── MarketAlertSystem.cs        — Реализация системы алертов
├── IMarketBacktester.cs        — Бэктестирование стратегий (интерфейс)
├── MarketBacktester.cs         — Реализация бэктестера с PnL-метриками
├── IMarketDataExporter.cs      — Экспорт данных (интерфейс)
├── MarketDataExporter.cs       — CSV/JSON экспорт позиций и P&L
├── IMarketVisualizer.cs        — ASCII-визуализация (интерфейс)
├── MarketVisualizer.cs         — ASCII-графики, таблицы, отчёты
├── IMarketAuthenticator.cs     — 5 аутентификаторов (HMAC + 4 специализированных)
├── MarketStreamingPipeline.cs  — Трёхстадийный стриминг-конвейер + 7 extension-методов
└── MarketPlatformBuilder.cs    — DI-регистрация платформ
```

## Перечисления (Enums)

| Enum | Описание | Значения |
|------|----------|----------|
| `TradeSide` | Сторона ордера | Buy, Sell |
| `TradeAction` | Действие стратегии | Hold, Buy, Sell |
| `MarketOrderStatus` | Статус ордера | Open, PartiallyFilled, Filled, Cancelled, Rejected |
| `PositionChangeReason` | Причина изменения позиции | Trade, PriceUpdate, MarketResolved, ManualSync |
| `MarketStatus` | Статус рынка | Active, Closed, Resolved, Voided, Unknown |
| `AlertCondition` | Условие алерта | PnLThreshold, PriceThreshold, PortfolioPnLThreshold, MarketClosed, MarketResolved |
| `AlertDirection` | Направление сравнения | Above, Below |
| `RiskOrderType` | Тип риск-ордера | StopLoss, TakeProfit, TrailingStop |
| `ExportFormat` | Формат экспорта | Csv, Json |

## Модели данных (Interfaces)

| Интерфейс | Описание |
|-----------|----------|
| `IMarketPriceSnapshot` | Снимок цены: bid, ask, midpoint, last trade |
| `IMarketPosition` | Позиция: количество, cost basis, P&L |
| `IMarketPortfolioSummary` | Сводка портфеля: market value, P&L, fees |
| `IMarketTradeSignal` | Торговый сигнал: действие, объём, уверенность |
| `IMarketPnLSnapshot` | Снимок P&L для истории |
| `IMarketAlertDefinition` | Настройки алерта |
| `IMarketRiskRule` | Правило стоп-лосса / тейк-профита |
| `IMarketPortfolioLimits` | Лимиты портфеля |
| `IMarketBacktestResult` | Результат бэктеста: ROI, Sharpe, drawdown |
| `IMarketPricePoint` | Историческая ценовая точка |
| `IMarketOrderBookSnapshot` | Снимок книги ордеров |

## Сервисные интерфейсы

### IMarketClient — Стриминг-клиент

```csharp
public interface IMarketClient : IAsyncDisposable
{
    string PlatformName { get; }
    bool IsConnected { get; }
    ValueTask SubscribeAsync(string[] marketIds, CancellationToken ct = default);
    ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken ct = default);
    ValueTask DisconnectAsync(CancellationToken ct = default);
}
```

### IMarketRestClient — REST API

```csharp
public interface IMarketRestClient : IDisposable
{
    string BaseUrl { get; }
    ValueTask<string?> CreateOrderAsync(string assetId, TradeSide side, double quantity, double? price = null, CancellationToken ct = default);
    ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);
    ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken ct = default);
    ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken ct = default);
}
```

### IMarketStrategy — Торговая стратегия

```csharp
public interface IMarketStrategy : IDisposable
{
    string Name { get; }
    IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId);
    void OnPriceUpdated(IMarketPriceSnapshot snapshot);
}
```

### IMarketRiskManager — Риск-менеджмент

```csharp
public interface IMarketRiskManager : IDisposable
{
    IMarketPortfolioLimits Limits { get; }
    bool AutoExecute { get; set; }
    double DailyLoss { get; }
    void AddRule(IMarketRiskRule rule);
    IMarketRiskRule? GetRule(string assetId);
    bool CanOpenPosition(string assetId, double quantity);
    ValueTask CheckAllRulesAsync();
}
```

## Пример реализации для новой платформы

```csharp
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Binance;

// 1. Модели данных
public sealed class BinancePriceSnapshot : IMarketPriceSnapshot
{
    public string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

// 2. WebSocket клиент
public sealed class BinanceClient : IMarketClient
{
    public string PlatformName => "Binance";
    public bool IsConnected { get; private set; }

    public async ValueTask SubscribeAsync(string[] symbols, CancellationToken ct = default)
    {
        // Подключение к wss://stream.binance.com:9443/ws
        // Подписка на bookTicker / trade streams
    }

    public ValueTask UnsubscribeAsync(string[] symbols, CancellationToken ct = default) { ... }
    public ValueTask DisconnectAsync(CancellationToken ct = default) { ... }
    public ValueTask DisposeAsync() => DisconnectAsync();
}

// 3. REST клиент
public sealed class BinanceRestClient : IMarketRestClient
{
    public string BaseUrl => "https://api.binance.com";

    public async ValueTask<string?> CreateOrderAsync(
        string symbol, TradeSide side, double quantity, double? price = null,
        CancellationToken ct = default)
    {
        // POST /api/v3/order с HMAC подписью
    }

    public async ValueTask<double?> GetPriceAsync(string symbol, TradeSide side, CancellationToken ct = default)
    {
        // GET /api/v3/ticker/price?symbol=...
    }
    // ...
}

// 4. Стратегия — переносится между площадками!
public sealed class CrossPlatformMomentum : IMarketStrategy
{
    public string Name => "CrossPlatformMomentum";

    public IMarketTradeSignal Evaluate(IMarketPriceStream stream, string assetId)
    {
        var price = stream.GetPrice(assetId);
        // Логика, работающая на ЛЮБОЙ площадке
    }

    public void OnPriceUpdated(IMarketPriceSnapshot snapshot) { ... }
    public void Dispose() { }
}
```

## DI-регистрация — `MarketPlatformBuilder`

Встроенный builder-паттерн для регистрации и создания клиентов без внешних зависимостей:

```csharp
using Atom.Web.Services.Markets;
using Atom.Web.Services.Polymarket;
using Atom.Web.Services.Binance;
using Atom.Web.Services.Kraken;
using Atom.Web.Services.Coinbase;

// 1. Регистрация через обобщённые типы (конструктор без параметров)
var registry = new MarketPlatformBuilder()
    .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
    .AddPlatform<CoinbaseClient, CoinbaseRestClient, CoinbasePriceStream>("Coinbase")
    .Build();

// 2. Регистрация через фабрики (с параметрами)
var registry2 = new MarketPlatformBuilder()
    .AddPlatform("Polymarket", new MarketPlatformRegistration
    {
        Name = "Polymarket",
        ClientFactory = () => new PolymarketClient(apiKey, secret, passphrase),
        RestClientFactory = () => new PolymarketRestClient(),
        PriceStreamFactory = () => new PolymarketPriceStream()
    })
    .AddPlatform("Binance", new MarketPlatformRegistration
    {
        Name = "Binance",
        RestClientFactory = () => new BinanceRestClient("https://api.binance.com", httpClient)
    })
    .Build();

// 3. Использование реестра
IMarketClient client = registry.CreateClient("Kraken");
IMarketRestClient rest = registry.CreateRestClient("Coinbase");
IMarketPriceStream stream = registry.CreatePriceStream("Kraken");

// 4. Безопасный доступ
if (registry.TryCreateClient("Unknown", out var noClient))
    Console.WriteLine(noClient.PlatformName);

// 5. Перечисление платформ
foreach (var name in registry.PlatformNames)
    Console.WriteLine($"Зарегистрирована: {name}");
```

Ключевые особенности:

- **FrozenDictionary** — thread-safe, иммутабельный реестр после `Build()`
- **Case-insensitive** — `"kraken"`, `"Kraken"`, `"KRAKEN"` — одно и то же
- **Ленивое создание** — фабрики вызываются только при `Create*()` / `TryCreate*()`
- **NativeAOT** — никакой рефлексии, все типы известны на этапе компиляции

## Существующие реализации

| Платформа | Неймспейс | Аутентификация | Статус |
|-----------|-----------|---------------|--------|
| **Polymarket** | `Atom.Web.Services.Polymarket` | EIP-712 | ✅ Полная реализация (20+ компонентов) |
| **Binance** | `Atom.Web.Services.Binance` | HmacAuthenticator (SHA256, query) | ✅ Полная миграция |
| **Kraken** | `Atom.Web.Services.Kraken` | KrakenAuthenticator (SHA256→SHA512) | ✅ Полная миграция |
| **Coinbase** | `Atom.Web.Services.Coinbase` | HmacAuthenticator (SHA256, header) | ✅ Полная миграция |
| **Bybit** | `Atom.Web.Services.Bybit` | HmacAuthenticator (SHA256, header) | ✅ Полная миграция |
| **OKX** | `Atom.Web.Services.Okx` | HmacAuthenticator (SHA256 base64, header) | ✅ Полная миграция |
| **Bitfinex** | `Atom.Web.Services.Bitfinex` | HmacAuthenticator (SHA384, header) | ✅ Полная миграция |
| **Gate.io** | `Atom.Web.Services.GateIo` | HmacAuthenticator (SHA512, header) | ✅ Полная миграция |
| **KuCoin** | `Atom.Web.Services.KuCoin` | HmacAuthenticator (SHA256 base64 + passphrase) | ✅ Полная миграция |
| **HTX** | `Atom.Web.Services.Htx` | HtxAuthenticator (SHA256, URL params) | ✅ Полная миграция |
| **MEXC** | `Atom.Web.Services.Mexc` | HmacAuthenticator (SHA256, query) | ✅ Полная миграция |
| **Deribit** | `Atom.Web.Services.Deribit` | OAuth client_credentials | ✅ JSON-RPC 2.0 WS |
| **Bitstamp** | `Atom.Web.Services.Bitstamp` | BitstampAuthenticator (SHA256, 5 headers) | ✅ Полная миграция |
| **Crypto.com** | `Atom.Web.Services.CryptoCom` | CryptoComAuthenticator (SHA256, JSON body) | ✅ Полная миграция |

## Унифицированная аутентификация — `IMarketAuthenticator`

Единый интерфейс для HMAC-подписи запросов ко всем биржам:

```csharp
public interface IMarketAuthenticator : IDisposable
{
    string AlgorithmName { get; }
    void SignRequest(HttpRequestMessage request, string? body = null);
}
```

### 5 реализаций аутентификаторов

| Класс | Биржи | Алгоритм |
|-------|-------|----------|
| `HmacAuthenticator` | Binance, MEXC, Coinbase, Bybit, OKX, Bitfinex, GateIo, KuCoin | Конфигурируемый HMAC (SHA256/384/512, hex/base64, header/query) |
| `KrakenAuthenticator` | Kraken | SHA256(nonce+body) → HMAC-SHA512(path+hash), auto-nonce |
| `HtxAuthenticator` | HTX | Auth params в URL, signs METHOD\nHOST\nPATH\nQuery |
| `BitstampAuthenticator` | Bitstamp | UUID nonce, 5 headers (X-Auth, X-Auth-Signature, etc.) |
| `CryptoComAuthenticator` | Crypto.com | JsonNode-parsing body, sorted params, sig injection |

### HmacAuthenticator — универсальный

Конфигурируемая реализация, покрывает 8 из 12 бирж:

| Параметр | Описание |
|----------|----------|
| `HashAlgorithmName` | SHA256, SHA384, SHA512 |
| `HmacOutputFormat` | HexLower, Base64 |
| `SignaturePlacement` | Header, QueryParameter |
| `SignatureStringBuilder` | Делегат формирования строки подписи |
| `TimestampGenerator` | Делегат генерации timestamp |

### Специализированные аутентификаторы

**KrakenAuthenticator** — двухшаговое хеширование:

```
SHA256(nonce + body) → HMAC-SHA512(path + sha256hash)
```

Генерирует nonce (микросекунды), подставляет `nonce=` в body, устанавливает `API-Key` + `API-Sign` headers.

**HtxAuthenticator** — подпись в URL:

```
METHOD\nhost\npath\nAccessKeyId=...&SignatureMethod=HmacSHA256&SignatureVersion=2&Timestamp=...
```

Добавляет auth-параметры + `Signature` прямо в URL запроса.

**BitstampAuthenticator** — 5 заголовков:

```
BITSTAMP {apiKey}{timestamp}{nonce}{contentType}{path}{query}{body}
```

Генерирует UUID nonce, устанавливает X-Auth, X-Auth-Signature, X-Auth-Nonce, X-Auth-Timestamp, X-Auth-Version.

**CryptoComAuthenticator** — подпись JSON body:

```
{method}{id}{apiKey}{sortedParams}{nonce} → HMAC-SHA256
```

Парсит JSON через `JsonNode`, вычисляет подпись, инжектирует `api_key` + `sig` в body.

### MarketAuthenticators — Фабрики

```csharp
// Создание аутентификатора для конкретной биржи:
var auth = MarketAuthenticators.Binance(apiKey, apiSecret);
var auth = MarketAuthenticators.Kraken(apiKey, apiSecret);
var auth = MarketAuthenticators.Coinbase(apiKey, apiSecret);
var auth = MarketAuthenticators.Bybit(apiKey, apiSecret);
var auth = MarketAuthenticators.Okx(apiKey, apiSecret, passphrase);
var auth = MarketAuthenticators.Bitfinex(apiKey, apiSecret);
var auth = MarketAuthenticators.GateIo(apiKey, apiSecret);
var auth = MarketAuthenticators.KuCoin(apiKey, apiSecret, passphrase);
var auth = MarketAuthenticators.Htx(apiKey, apiSecret);
var auth = MarketAuthenticators.Mexc(apiKey, apiSecret);
var auth = MarketAuthenticators.Bitstamp(apiKey, apiSecret);
var auth = MarketAuthenticators.CryptoCom(apiKey, apiSecret);

// Инъекция в RestClient:
var rest = new BinanceRestClient(authenticator: auth);
```

Каждый `RestClient` принимает опциональный `IMarketAuthenticator? authenticator`:

- Если передан — используется напрямую
- Если не передан, но есть apiKey/apiSecret — создаётся из `MarketAuthenticators.Xxx()`
- Безопасная очистка ключей через `CryptographicOperations.ZeroMemory()` при `Dispose()`

## Стриминг-конвейер — `MarketStreamingPipeline`

Трёхстадийный конвейер обработки рыночных данных на базе `Channel<T>`:

```
┌──────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  WS Reader   │────▶│ Strategy Evaluator│────▶│ Order Executor  │
│ (PriceUpdate)│     │  (PipelineSignal) │     │  (IMarketRest)  │
└──────────────┘     └──────────────────┘     └─────────────────┘
```

### Конфигурация

```csharp
var config = new StreamingPipelineConfig
{
    EvaluationInterval = TimeSpan.FromMilliseconds(100),
    OrderCooldown = TimeSpan.FromSeconds(5),
    MinConfidence = 0.7,
    DryRun = true,        // без реального исполнения ордеров
    AutoReconnect = true
};
```

### Использование

```csharp
var pipeline = new MarketStreamingPipeline(
    client: binanceWs,
    restClient: binanceRest,
    config: config);

pipeline.AddStrategy(new MomentumStrategy());
pipeline.OnSignalGenerated += (s, signal) => Console.WriteLine($"Signal: {signal}");
pipeline.OnOrderExecuted += (s, orderId) => Console.WriteLine($"Order: {orderId}");

await pipeline.StartAsync(["BTCUSDT", "ETHUSDT"]);
// ... работает в фоне ...
await pipeline.StopAsync();
```

| Свойство | Описание |
|----------|----------|
| `IsRunning` | Конвейер запущен |
| `PricesProcessed` | Кол-во обработанных обновлений цен |
| `SignalsGenerated` | Кол-во сгенерированных сигналов |
| `OrdersExecuted` | Кол-во исполненных ордеров |

## Совместимость

- **.NET 10.0+** с `LangVersion preview`
- **NativeAOT** — все интерфейсы совместимы
- Все enum'ы — `: byte` для минимального размера
- Нет рефлексии, нет dynamic

## Реализации стратегий — `MarketStrategies.cs`

7 готовых стратегий, работающих на **любой** площадке через `IMarketPriceStream`:

| Стратегия | Описание | Ключевые параметры |
|-----------|----------|--------------------|
| `MomentumStrategy` | SMA + пороговый % | WindowSize, BuyThresholdPercent, SellThresholdPercent |
| `MeanReversionStrategy` | Bollinger Bands | WindowSize, BollingerMultiplier |
| `ArbitrageStrategy` | Межбиржевой арбитраж | MinSpreadPercent (два IMarketPriceStream) |
| `VwapStrategy` | VWAP-отклонение | ThresholdPercent |
| `RsiStrategy` | Wilder's RSI | Period, OversoldLevel, OverboughtLevel |
| `MacdCrossoverStrategy` | EMA crossover | FastPeriod, SlowPeriod, SignalPeriod |
| `CompositeStrategy` | Голосование N стратегий | Quorum (по умолчанию = большинство) |

### Пример использования

```csharp
// Простая стратегия
var momentum = new MomentumStrategy { WindowSize = 20, BuyThresholdPercent = 2.0 };
var signal = momentum.Evaluate(priceStream, "BTCUSDT");

// Композитная стратегия (голосование)
var composite = new CompositeStrategy(
    new MomentumStrategy { WindowSize = 10 },
    new RsiStrategy(period: 14),
    new MacdCrossoverStrategy());
// Сигнал Buy/Sell только если большинство стратегий согласны
var signal = composite.Evaluate(priceStream, "BTCUSDT");
```

### Pipeline Extensions — быстрое подключение к конвейеру

```csharp
var pipeline = new MarketStreamingPipeline(client, restClient, config);

// Добавить конкретные стратегии:
pipeline.AddMomentum(windowSize: 20, buyThreshold: 2.0);
pipeline.AddRsi(period: 14);
pipeline.AddMacd();

// Добавить сразу все индикаторы:
pipeline.AddStandardIndicators();

// Композитная стратегия из массива:
pipeline.AddComposite(new MomentumStrategy(), new RsiStrategy(), new VwapStrategy());
```

## Бэктестер — `MarketBacktester`

Прогоняет стратегию по историческим данным и вычисляет торговые метрики:

```csharp
var backtester = new MarketBacktester
{
    InitialBalance = 10_000,
    FeeRateBps = 10     // 0.1% комиссия
};

var result = backtester.Run(strategy, "BTCUSDT", historicalPrices);
```

### Метрики результата (`IMarketBacktestResult`)

| Метрика | Описание |
|---------|----------|
| `NetPnL` | Чистая прибыль/убыток |
| `ReturnPercent` | Доходность в % |
| `TotalTrades` | Количество сделок |
| `WinRate` | Процент прибыльных сделок |
| `SharpeRatio` | Annualized Sharpe Ratio (√252) |
| `MaxDrawdownPercent` | Максимальная просадка от пика |
| `ProfitFactor` | grossProfit / grossLoss |
| `EquityCurve` | Кривая капитала (double[]) |

Движок поддерживает long/short позиции, учитывает комиссию, автоматически закрывает позицию в конце.

## Риск-менеджер — `MarketRiskManager`

Stop-Loss, Take-Profit, Trailing Stop и портфельные лимиты:

```csharp
var riskManager = new MarketRiskManager(priceStream, restClient, limits);

// Добавить правило для актива
riskManager.AddRule(new RiskRule
{
    AssetId = "BTCUSDT",
    StopLossPrice = 50_000,
    TakeProfitPrice = 70_000,
    TrailingStopPercent = 5.0
});

// Автоматическое исполнение через REST-клиент
riskManager.AutoExecute = true;

// Проверка правил (вызывается периодически)
await riskManager.CheckAllRulesAsync();

// Проверка перед открытием позиции
if (riskManager.CanOpenPosition("BTCUSDT", quantity: 0.5))
    await restClient.CreateOrderAsync("BTCUSDT", TradeSide.Buy, 0.5);
```

### Правила (`RiskRule`)

| Параметр | Описание |
|----------|----------|
| `StopLossPrice` | Цена Stop-Loss |
| `TakeProfitPrice` | Цена Take-Profit |
| `TrailingStopPercent` | Trailing Stop % (High Water Mark) |
| `MaxLossPerPosition` | Макс. убыток на позицию |

### Портфельные лимиты (`PortfolioLimits`)

| Параметр | Описание |
|----------|----------|
| `MaxPositionSize` | Макс. размер позиции |
| `MaxOpenPositions` | Макс. кол-во открытых позиций |
| `MaxPortfolioLoss` | Макс. убыток портфеля |
| `MaxDailyLoss` | Дневной лимит убытков |
| `MaxPositionPercent` | Макс. % портфеля на позицию |

## Трекер портфеля — `MarketPortfolioTracker`

Отслеживание позиций, P&L, сводка портфеля:

```csharp
var tracker = new MarketPortfolioTracker(priceStream);

// Записываем сделки
tracker.RecordTrade("BTCUSDT", TradeSide.Buy, 1.0, 50_000, fee: 5);
tracker.RecordTrade("ETHUSDT", TradeSide.Buy, 10.0, 3_000);

// Сводка (автоматически обновляет цены из PriceStream)
var summary = tracker.GetSummary();
// summary.TotalMarketValue, .TotalUnrealizedPnL, .NetPnL

// Позиция
var pos = tracker.GetPosition("BTCUSDT");
// pos.UnrealizedPnL, .UnrealizedPnLPercent, .RealizedPnL, .MarketValue
```

Поддерживает усреднение позиций, частичное закрытие, переворот (long→short), учёт комиссий.

### История P&L — `MarketPnLHistory`

```csharp
var history = new MarketPnLHistory(tracker) { Interval = TimeSpan.FromMinutes(5) };
history.Start();      // периодическая запись снимков
// ...
await history.StopAsync();

var snapshots = history.GetSnapshots();  // все снимки
var latest = history.Latest;              // последний
```

## Система алертов — `MarketAlertSystem`

Мониторинг ценовых условий и P&L-порогов:

```csharp
var alerts = new MarketAlertSystem(priceStream, portfolioTracker);

alerts.AddAlert(new AlertDefinition
{
    Id = "btc-breakout",
    Condition = AlertCondition.PriceThreshold,
    Direction = AlertDirection.Above,
    Threshold = 70_000,
    AssetId = "BTCUSDT",
    OneShot = true      // сработает один раз
});

alerts.OnAlertTriggered += (alert, value) =>
    Console.WriteLine($"Alert {alert.Id}: {value:F2}");

alerts.CheckAlerts();   // вызывать периодически
```

| Условие | Описание |
|---------|----------|
| `PriceThreshold` | Цена актива выше/ниже порога |
| `PnLThreshold` | P&L позиции выше/ниже порога |
| `PortfolioPnLThreshold` | P&L всего портфеля |

## Экспорт данных — `MarketDataExporter`

CSV и JSON экспорт через `Utf8JsonWriter` (NativeAOT-compatible):

```csharp
var exporter = new MarketDataExporter();

// Экспорт позиций
var csv = exporter.ExportPositions(positions, ExportFormat.Csv);
var json = exporter.ExportPositions(positions, ExportFormat.Json);
exporter.ExportPositionsToFile(positions, ExportFormat.Csv, "positions.csv");

// Экспорт P&L истории
var pnlCsv = exporter.ExportPnLHistory(snapshots, ExportFormat.Csv);
exporter.ExportPnLHistoryToFile(snapshots, ExportFormat.Json, "pnl.json");
```

## ASCII-визуализация — `MarketVisualizer`

Графики, таблицы, отчёты для консольного вывода:

```csharp
var viz = new MarketVisualizer { Width = 60, Height = 15 };

// ASCII-график equity curve
Console.Write(viz.RenderEquityCurve(backtestResult));

// Таблица позиций
Console.Write(viz.RenderPositionsTable(positions));

// Сводка бэктеста
Console.Write(viz.RenderBacktestSummary(backtestResult));

// Сводка портфеля
Console.Write(viz.RenderPortfolioSummary(summary));

// Произвольный график
Console.Write(viz.RenderChart(values, "My Chart"));
```

## Авто-исполнитель — `MarketOrderExecutor`

Связывает стратегии с REST-клиентом для автоматического исполнения:

```csharp
var executor = new MarketOrderExecutor(priceStream, restClient)
{
    MinConfidence = 0.7,
    EvaluationInterval = TimeSpan.FromSeconds(10),
    OrderCooldown = TimeSpan.FromSeconds(30),
    DryRun = true   // тестовый режим
};

executor.AddStrategy(new MomentumStrategy(), ["BTCUSDT", "ETHUSDT"]);
executor.AddStrategy(new RsiStrategy(), ["BTCUSDT"]);

executor.OnSignalGenerated += signal => Console.WriteLine($"Signal: {signal.Action}");
executor.OnOrderExecuted += (asset, orderId) => Console.WriteLine($"Order: {orderId}");

executor.Start();               // фоновый цикл
// или
await executor.EvaluateOnceAsync(); // одиночная оценка
await executor.StopAsync();
```

| Параметр | Описание |
|----------|----------|
| `DryRun` | Тестовый режим (без реальных ордеров) |
| `MinConfidence` | Мин. уверенность сигнала (0.0–1.0) |
| `EvaluationInterval` | Интервал оценки стратегий |
| `OrderCooldown` | Cooldown между ордерами по одному активу |
