# 📄 Техническое задание: `HttpsClientHandler`

## 🧭 Цель проекта

Реализация полностью кастомного, **антидетектного**, высокопроизводительного HTTP-стека на .NET 9, предназначенного для эмуляции поведения реальных браузеров (Chrome, Firefox, Safari и др.) при выполнении HTTP/1.1, HTTP/2, HTTP/3 запросов с точной симуляцией всех сетевых, TLS и HTTP параметров.

## 🎯 Основные задачи

- ✅ **Полная эмуляция** сетевого поведения реальных браузеров:
  - TLS fingerprint (JA3, JA4)
  - HTTP/2, HTTP/3 frame-поведение
  - Заголовки, cookie, redirect, fallback
  - Алгоритмы сжатия (gzip, deflate, br, zstd)
  - Псевдозаголовки и сжатие (HPACK, QPACK)
- ✅ **Полноценная замена HttpClientHandler**
  - Совместимость со всеми его свойствами
  - Расширение функционала
- ✅ **Работа как самостоятельный low-level стек**
  - TCP
  - TLS
  - HTTP/1.1, 2, 3
- ✅ **Интеграция с браузерным эмулятором**
  - Поддержка `Navigation`, `Fetch`, `Preload`, `ServiceWorker`

## 🚫 Ограничения

- ❌ Никаких сторонних библиотек
- ❌ Никакого P/Invoke
- ❌ Никаких прокси или внешних процессов
- ❌ Никакой рефлексии
- ✅ Полная совместимость с NativeAOT и trimming
- ✅ Без аллокаций там, где можно обойтись `stackalloc`, `Span`, аренда
- ✅ Отсутствие блокировок в многопоточности (`lock-free`)

## 🧩 Архитектура

```
HttpsClientHandler
│
├─ TcpEngine        // Lock-free соединения с ручной настройкой TCP
├─ TlsEngine        // TLS 1.1 / 1.2 / 1.3 с полной эмуляцией
├─ Http1Engine      // Реализация HTTP/1.1
├─ Http2Engine      // HPACK, frame control
├─ Http3Engine      // QUIC, QPACK
├─ HeaderPolicy     // Полный контроль заголовков и псевдохедеров
├─ Fingerprint      // Профили браузеров и платформ
├─ CookieContainer  // Полностью кастомная реализация куков
├─ RedirectPolicy   // Поведение как в браузерах (Origin, метод, тело)
└─ TrafficControl   // Учёт байт по всем соединениям (вход/выход)
```

## 🧱 Базовые компоненты

### `HttpsClientHandler`

- Полная замена `SocketsHttpHandler`
- Поддержка всех свойств:
  - Proxy, CookieContainer, MaxConnectionsPerServer, Timeout и др.
- Расширения:
  - `FingerprintProfile`
  - `TlsPolicy`
  - `HeaderPolicy`
  - `VersionPolicy`

### `HttpsRequestMessage : HttpRequestMessage`

- Добавляет:
  - `RequestKind: HttpsRequestKind` (Navigation, Fetch и др.)
  - `Context: object?` (например, для интеграции с V8)

### `HttpsResponseMessage : HttpResponseMessage`

- Безопасный:
  - `Exception: Exception?` — не выбрасывает исключения
  - `Traffic: Traffic` — статистика по Input/Output

### `HttpsClient`

- Замена `HttpClient`
- Безопасный API
- Суммарный `Traffic`
- Методы `SendAsync`, `GetAsync`, `PostAsync` и т.д.

### `Traffic`

```csharp
public readonly struct Traffic
{
    public ulong Input { get; init; }
    public ulong Output { get; init; }
}
```

## 🎛 Контроль заголовков

- Модуль `HeaderPolicyEngine`
- Интерфейс `IHeaderPolicy`
- Учитывает:
  - Профиль браузера
  - Тип запроса (`Navigation`, `Fetch`, ...)
  - Цель, метод, кроссдоменность
  - Псевдозаголовки
- Поддержка:
  - Порядка
  - casing
  - пробелов
  - стратегий переопределения

## 🔐 TLS и JA3/JA4

- Реализация TLS 1.1, 1.2, 1.3
- Поддержка:
  - Cipher Suite Order
  - Extensions (ALPN, SNI, GREASE, padding)
  - Key Share
  - Session resumption / ticket
- Совместимость с: tls.peet.ws, ja3er.com

## 🌐 HTTP/2 и HTTP/3

- HPACK, QPACK реализация вручную
- Учитываются:
  - Huffman кодирование
  - Frame priority
  - Порядок фреймов
  - Window size и flow control
  - Поддержка padding и weight
- HTTP/3: реализация QUIC поверх `Socket`, либо `System.Net.Quic` (если возможно)

## 📦 Content-Encoding

- Поддержка:
  - `gzip` (RFC 1952)
  - `deflate` (RFC 1951)
  - `br` (Brotli)
  - `zstd` (RFC 8878)
- Автоматическая декомпрессия
- Выбор на основе профиля браузера
- Эмуляция Accept-Encoding заголовка

## 📌 Ключевые принципы

- Безопасный API (не кидает исключения)
- Производительность: каждая микросекунда важна
- Без компромиссов
- Полная модульность и масштабируемость
- Единый API для использования как внутри браузерного эмулятора, так и отдельно
