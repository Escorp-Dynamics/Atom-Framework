# Atom.Web.Services.Markets

**Универсальные контракты для торговых платформ** — абстрактный слой интерфейсов и моделей для построения клиентов к произвольным торговым площадкам (криптобиржи, prediction markets, фондовые рынки).

## Архитектура

```
Markets/
├── MarketEnums.cs            — 9 универсальных перечислений
├── MarketModels.cs           — 12 интерфейсов моделей данных
├── MarketException.cs        — Базовое исключение
├── IMarketClient.cs          — WebSocket/стриминг клиент
├── IMarketRestClient.cs      — REST API клиент
├── IMarketPriceStream.cs     — Кеш цен в реальном времени
├── IMarketStrategy.cs        — Торговая стратегия
├── IMarketPortfolioTracker.cs — Портфель + P&L история
├── IMarketRiskManager.cs     — Stop-Loss, Take-Profit, лимиты
├── IMarketOrderExecutor.cs   — Авто-исполнение ордеров
├── IMarketAlertSystem.cs     — Система алертов
├── IMarketBacktester.cs      — Бэктестирование стратегий
├── IMarketDataExporter.cs      — Экспорт данных (CSV/JSON)
├── IMarketVisualizer.cs        — ASCII-визуализация
├── IMarketAuthenticator.cs     — Унифицированный HMAC-аутентификатор
├── MarketStreamingPipeline.cs  — Трёхстадийный стриминг-конвейер
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
| **Binance** | `Atom.Web.Services.Binance` | HMAC-SHA256 hex, query | 🔧 Скелет + IMarketAuthenticator |
| **Kraken** | `Atom.Web.Services.Kraken` | SHA256→HMAC-SHA512 2-step | 🔧 Скелет + IMarketAuthenticator |
| **Coinbase** | `Atom.Web.Services.Coinbase` | HMAC-SHA256 hex, header | 🔧 Скелет + IMarketAuthenticator |
| **Bybit** | `Atom.Web.Services.Bybit` | HMAC-SHA256 hex, header | 🔧 Скелет + IMarketAuthenticator |
| **OKX** | `Atom.Web.Services.Okx` | HMAC-SHA256 base64, header | 🔧 Скелет + IMarketAuthenticator |
| **Bitfinex** | `Atom.Web.Services.Bitfinex` | HMAC-SHA384 hex, header | 🔧 Скелет + IMarketAuthenticator |
| **Gate.io** | `Atom.Web.Services.GateIo` | HMAC-SHA512 hex, header | 🔧 Скелет + IMarketAuthenticator |
| **KuCoin** | `Atom.Web.Services.KuCoin` | HMAC-SHA256 base64 + passphrase | 🔧 Скелет + IMarketAuthenticator |
| **HTX** | `Atom.Web.Services.Htx` | HMAC-SHA256 base64, query | 🔧 Скелет + IMarketAuthenticator |
| **MEXC** | `Atom.Web.Services.Mexc` | HMAC-SHA256 hex (Binance-compat) | 🔧 Скелет + IMarketAuthenticator |
| **Deribit** | `Atom.Web.Services.Deribit` | OAuth client_credentials | 🔧 Скелет + JSON-RPC 2.0 WS |
| **Bitstamp** | `Atom.Web.Services.Bitstamp` | HMAC-SHA256 v2, header | 🔧 Скелет + IMarketAuthenticator |
| **Crypto.com** | `Atom.Web.Services.CryptoCom` | HMAC-SHA256 hex, sorted params | 🔧 Скелет + IMarketAuthenticator |

## Унифицированная аутентификация — `IMarketAuthenticator`

Единый интерфейс для HMAC-подписи запросов ко всем биржам:

```csharp
public interface IMarketAuthenticator : IDisposable
{
    string AlgorithmName { get; }
    void SignRequest(HttpRequestMessage request, string? body = null);
}
```

### HmacAuthenticator

Универсальная реализация, конфигурируемая через:

| Параметр | Описание |
|----------|----------|
| `HashAlgorithmName` | SHA256, SHA384, SHA512 |
| `HmacOutputFormat` | HexLower, Base64 |
| `SignaturePlacement` | Header, QueryParameter |
| `SignatureStringBuilder` | Делегат формирования строки подписи |

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
