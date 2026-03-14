using System.Security.Cryptography;
using System.Text;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Унифицированная аутентификация для Markets/ REST-клиентов.
// Каждая биржа использует свою HMAC-схему — адаптеры инкапсулируют
// различия (алгоритм, формат, расположение подписи).
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Аутентификатор запросов — формирует подпись и заголовки для авторизованных REST-вызовов.
/// </summary>
public interface IMarketAuthenticator : IDisposable
{
    /// <summary>Имя алгоритма (для логирования/диагностики).</summary>
    string AlgorithmName { get; }

    /// <summary>
    /// Подписывает HTTP-запрос: добавляет заголовки аутентификации и/или query-параметры.
    /// </summary>
    /// <param name="request">HTTP-запрос для подписи.</param>
    /// <param name="body">Тело запроса (пустая строка для GET/DELETE без тела).</param>
    void SignRequest(HttpRequestMessage request, string body = "");
}

/// <summary>
/// Формат вывода HMAC-подписи.
/// </summary>
public enum HmacOutputFormat : byte
{
    /// <summary>Нижний hex (0-9a-f).</summary>
    HexLower,

    /// <summary>Base64.</summary>
    Base64
}

/// <summary>
/// Расположение подписи в запросе.
/// </summary>
public enum SignaturePlacement : byte
{
    /// <summary>В HTTP-заголовке.</summary>
    Header,

    /// <summary>В query-параметре.</summary>
    QueryParameter
}

/// <summary>
/// Конфигурация HMAC-аутентификатора — описывает как формировать подпись.
/// </summary>
public sealed class HmacAuthenticatorConfig
{
    /// <summary>API-ключ.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Секрет (для HMAC).</summary>
    public required string ApiSecret { get; init; }

    /// <summary>Passphrase (KuCoin, OKX).</summary>
    public string? Passphrase { get; init; }

    /// <summary>Формат вывода подписи.</summary>
    public HmacOutputFormat OutputFormat { get; init; } = HmacOutputFormat.HexLower;

    /// <summary>Куда помещать подпись.</summary>
    public SignaturePlacement Placement { get; init; } = SignaturePlacement.Header;

    /// <summary>
    /// Имя заголовка для API-ключа.
    /// </summary>
    public string ApiKeyHeader { get; init; } = "X-API-KEY";

    /// <summary>
    /// Имя заголовка для подписи.
    /// </summary>
    public string SignatureHeader { get; init; } = "X-API-SIGN";

    /// <summary>
    /// Имя заголовка для timestamp.
    /// </summary>
    public string TimestampHeader { get; init; } = "X-API-TIMESTAMP";

    /// <summary>
    /// Имя query-параметра для подписи (если <see cref="Placement"/> = QueryParameter).
    /// </summary>
    public string SignatureQueryParam { get; init; } = "signature";

    /// <summary>
    /// Генератор timestamp. По умолчанию: Unix milliseconds.
    /// Примеры: Unix seconds (Coinbase, Gate.io), ISO 8601 (OKX).
    /// </summary>
    public Func<string>? TimestampGenerator { get; init; }
}

/// <summary>
/// Делегат для построения строки подписи из компонентов запроса.
/// </summary>
/// <param name="context">Контекст запроса с разобранными компонентами.</param>
/// <returns>Строка для подписи HMAC.</returns>
public delegate string SignatureStringBuilder(in SignatureContext context);

/// <summary>
/// Контекст запроса для построения строки подписи.
/// </summary>
public readonly ref struct SignatureContext
{
    /// <summary>HTTP-метод (GET, POST, DELETE и т.д.).</summary>
    public required string Method { get; init; }

    /// <summary>Путь запроса (без базового URL).</summary>
    public required string Path { get; init; }

    /// <summary>Query-строка (без '?').</summary>
    public required string Query { get; init; }

    /// <summary>Тело запроса.</summary>
    public required string Body { get; init; }

    /// <summary>Timestamp (зависит от биржи — ms/s/ISO).</summary>
    public required string Timestamp { get; init; }

    /// <summary>Nonce (если требуется).</summary>
    public required string Nonce { get; init; }

    /// <summary>API-ключ.</summary>
    public required string ApiKey { get; init; }
}

/// <summary>
/// Универсальный HMAC-аутентификатор с настраиваемой стратегией формирования подписи.
/// </summary>
/// <remarks>
/// Поддерживает SHA256/SHA384/SHA512, hex/base64 вывод, заголовки/query placement.
/// Стратегия формирования строки подписи задаётся делегатом <see cref="SignatureStringBuilder"/>.
/// </remarks>
public sealed class HmacAuthenticator : IMarketAuthenticator
{
    private readonly byte[] secretBytes;
    private readonly HmacAuthenticatorConfig config;
    private readonly HashAlgorithmName hashAlgorithm;
    private readonly SignatureStringBuilder buildSignatureString;
    private readonly Action<HttpRequestMessage, HmacAuthenticatorConfig, string, string>? addExtraHeaders;
    private bool isDisposed;

    /// <summary>
    /// Создаёт HMAC-аутентификатор.
    /// </summary>
    /// <param name="algorithm">Алгоритм хеширования (SHA256, SHA384, SHA512).</param>
    /// <param name="config">Конфигурация ключей и форматов.</param>
    /// <param name="buildSignatureString">Стратегия построения строки подписи.</param>
    /// <param name="addExtraHeaders">Опциональные дополнительные заголовки (passphrase, version и т.д.).</param>
    public HmacAuthenticator(
        HashAlgorithmName algorithm,
        HmacAuthenticatorConfig config,
        SignatureStringBuilder buildSignatureString,
        Action<HttpRequestMessage, HmacAuthenticatorConfig, string, string>? addExtraHeaders = null)
    {
        hashAlgorithm = algorithm;
        this.config = config;
        this.buildSignatureString = buildSignatureString;
        this.addExtraHeaders = addExtraHeaders;
        secretBytes = Encoding.UTF8.GetBytes(config.ApiSecret);
    }

    /// <inheritdoc />
    public string AlgorithmName => $"HMAC-{hashAlgorithm.Name}";

    /// <inheritdoc />
    public void SignRequest(HttpRequestMessage request, string body = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var uri = request.RequestUri ?? throw new MarketException("RequestUri is null");

        var timestamp = config.TimestampGenerator?.Invoke()
            ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = timestamp; // Большинство бирж используют timestamp как nonce

        var context = new SignatureContext
        {
            Method = request.Method.Method,
            Path = uri.AbsolutePath,
            Query = uri.Query.TrimStart('?'),
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            ApiKey = config.ApiKey
        };

        var signatureString = buildSignatureString(in context);

        // Вычисляем HMAC
        var signatureBytes = ComputeHmac(signatureString);
        var signature = FormatSignature(signatureBytes);

        // Размещаем подпись
        if (config.Placement == SignaturePlacement.Header)
        {
            request.Headers.TryAddWithoutValidation(config.ApiKeyHeader, config.ApiKey);
            request.Headers.TryAddWithoutValidation(config.SignatureHeader, signature);
            request.Headers.TryAddWithoutValidation(config.TimestampHeader, timestamp);
        }
        else
        {
            // Query parameter: дописываем &signature=...
            var uriStr = uri.ToString();
            var separator = uriStr.Contains('?') ? "&" : "?";
            request.RequestUri = new Uri($"{uriStr}{separator}{config.SignatureQueryParam}={Uri.EscapeDataString(signature)}");
        }

        // Дополнительные заголовки (passphrase, version и т.д.)
        addExtraHeaders?.Invoke(request, config, timestamp, signature);
    }

    private byte[] ComputeHmac(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);

        if (hashAlgorithm == HashAlgorithmName.SHA256)
            return HMACSHA256.HashData(secretBytes, messageBytes);
        if (hashAlgorithm == HashAlgorithmName.SHA384)
            return HMACSHA384.HashData(secretBytes, messageBytes);
        if (hashAlgorithm == HashAlgorithmName.SHA512)
            return HMACSHA512.HashData(secretBytes, messageBytes);

        throw new MarketException($"Неподдерживаемый алгоритм: {hashAlgorithm.Name}");
    }

    private string FormatSignature(byte[] hash) => config.OutputFormat switch
    {
        HmacOutputFormat.HexLower => Convert.ToHexStringLower(hash),
        HmacOutputFormat.Base64 => Convert.ToBase64String(hash),
        _ => Convert.ToHexStringLower(hash)
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        // secretBytes — byte[], не IDisposable; обнуляем для безопасности
        CryptographicOperations.ZeroMemory(secretBytes);
    }
}

/// <summary>
/// Фабрики аутентификаторов для известных бирж.
/// </summary>
public static class MarketAuthenticators
{
    /// <summary>Binance HMAC-SHA256 (hex, query parameter).</summary>
    public static HmacAuthenticator Binance(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.QueryParameter,
            ApiKeyHeader = "X-MBX-APIKEY",
            SignatureQueryParam = "signature"
        },
        static (in SignatureContext ctx) => ctx.Query.Length > 0 ? ctx.Query : ctx.Body,
        static (req, cfg, _, _) => req.Headers.TryAddWithoutValidation("X-MBX-APIKEY", cfg.ApiKey));

    /// <summary>Kraken HMAC-SHA512 (base64, header).</summary>
    /// <remarks>
    /// Kraken: SHA256(nonce + postData), затем HMAC-SHA512(urlPath + sha256hash).
    /// Не подходит для простого HmacAuthenticator — требует двухэтапную подпись.
    /// </remarks>
    public static HmacAuthenticator Kraken(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA512,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.Base64,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "API-Key",
            SignatureHeader = "API-Sign"
        },
        static (in SignatureContext ctx) => $"{ctx.Path}{ctx.Nonce}{ctx.Body}");

    /// <summary>Coinbase Advanced Trade HMAC-SHA256 (hex, header).</summary>
    public static HmacAuthenticator Coinbase(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "CB-ACCESS-KEY",
            SignatureHeader = "CB-ACCESS-SIGN",
            TimestampHeader = "CB-ACCESS-TIMESTAMP",
            TimestampGenerator = static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        static (in SignatureContext ctx) => $"{ctx.Timestamp}{ctx.Method}{ctx.Path}{ctx.Body}");

    /// <summary>Bybit HMAC-SHA256 (hex, header).</summary>
    public static HmacAuthenticator Bybit(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "X-BAPI-API-KEY",
            SignatureHeader = "X-BAPI-SIGN",
            TimestampHeader = "X-BAPI-TIMESTAMP"
        },
        static (in SignatureContext ctx) =>
        {
            var paramStr = ctx.Body.Length > 0 ? ctx.Body : ctx.Query;
            return $"{ctx.Timestamp}{ctx.ApiKey}5000{paramStr}";
        },
        static (req, _, _, _) => req.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW", "5000"));

    /// <summary>OKX HMAC-SHA256 (base64, header).</summary>
    public static HmacAuthenticator Okx(string apiKey, string apiSecret, string passphrase) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            Passphrase = passphrase,
            OutputFormat = HmacOutputFormat.Base64,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "OK-ACCESS-KEY",
            SignatureHeader = "OK-ACCESS-SIGN",
            TimestampHeader = "OK-ACCESS-TIMESTAMP",
            TimestampGenerator = static () => DateTimeOffset.UtcNow
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
        },
        static (in SignatureContext ctx) => $"{ctx.Timestamp}{ctx.Method}{ctx.Path}{ctx.Body}",
        static (req, cfg, _, _) => req.Headers.TryAddWithoutValidation("OK-ACCESS-PASSPHRASE", cfg.Passphrase ?? ""));

    /// <summary>Bitfinex HMAC-SHA384 (hex, header).</summary>
    public static HmacAuthenticator Bitfinex(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA384,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "bfx-apikey",
            SignatureHeader = "bfx-signature"
        },
        static (in SignatureContext ctx) => $"/api{ctx.Path}{ctx.Nonce}{ctx.Body}",
        static (req, _, ts, _) => req.Headers.TryAddWithoutValidation("bfx-nonce", ts));

    /// <summary>Gate.io HMAC-SHA512 (hex, header).</summary>
    public static HmacAuthenticator GateIo(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA512,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "KEY",
            SignatureHeader = "SIGN",
            TimestampHeader = "Timestamp",
            TimestampGenerator = static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        static (in SignatureContext ctx) =>
        {
            var bodyHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA512.HashData(
                    Encoding.UTF8.GetBytes(ctx.Body)));
            return $"{ctx.Method}\n{ctx.Path}\n{ctx.Query}\n{bodyHash}\n{ctx.Timestamp}";
        });

    /// <summary>KuCoin HMAC-SHA256 (base64, header) + подписанный passphrase.</summary>
    public static HmacAuthenticator KuCoin(string apiKey, string apiSecret, string passphrase) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            Passphrase = passphrase,
            OutputFormat = HmacOutputFormat.Base64,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "KC-API-KEY",
            SignatureHeader = "KC-API-SIGN",
            TimestampHeader = "KC-API-TIMESTAMP"
        },
        static (in SignatureContext ctx) => $"{ctx.Timestamp}{ctx.Method}{ctx.Path}{ctx.Body}",
        static (req, cfg, _, _) =>
        {
            // KuCoin API v2: passphrase подписывается HMAC-SHA256(secret, passphrase)
            if (cfg.Passphrase is not null)
            {
                var signedPassphrase = Convert.ToBase64String(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(cfg.ApiSecret),
                        Encoding.UTF8.GetBytes(cfg.Passphrase)));
                req.Headers.TryAddWithoutValidation("KC-API-PASSPHRASE", signedPassphrase);
            }
            req.Headers.TryAddWithoutValidation("KC-API-KEY-VERSION", "2");
        });

    /// <summary>HTX (Huobi) HMAC-SHA256 (base64, query parameter).</summary>
    public static HmacAuthenticator Htx(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.Base64,
            Placement = SignaturePlacement.QueryParameter,
            SignatureQueryParam = "Signature"
        },
        static (in SignatureContext ctx) =>
            $"{ctx.Method}\napi.huobi.pro\n{ctx.Path}\n{ctx.Query}");

    /// <summary>MEXC HMAC-SHA256 (hex, query parameter — Binance-совместимый).</summary>
    public static HmacAuthenticator Mexc(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.QueryParameter,
            ApiKeyHeader = "X-MEXC-APIKEY",
            SignatureQueryParam = "signature"
        },
        static (in SignatureContext ctx) => ctx.Query.Length > 0 ? ctx.Query : ctx.Body,
        static (req, cfg, _, _) => req.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", cfg.ApiKey));

    /// <summary>Bitstamp HMAC-SHA256 (hex, header) — v2 аутентификация.</summary>
    /// <remarks>
    /// Signature string: "BITSTAMP {apiKey}" + method + host + path + query + contentType + nonce + timestamp + "v2" + body.
    /// Заголовки: X-Auth, X-Auth-Signature, X-Auth-Nonce, X-Auth-Timestamp, X-Auth-Version.
    /// </remarks>
    public static HmacAuthenticator Bitstamp(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "X-Auth",
            SignatureHeader = "X-Auth-Signature",
            TimestampHeader = "X-Auth-Timestamp"
        },
        static (in SignatureContext ctx) =>
            $"BITSTAMP {ctx.ApiKey}POST{ctx.Path}application/x-www-form-urlencoded{ctx.Nonce}{ctx.Timestamp}v2{ctx.Body}",
        static (req, _, ts, nonce) =>
        {
            req.Headers.TryAddWithoutValidation("X-Auth-Nonce", nonce);
            req.Headers.TryAddWithoutValidation("X-Auth-Version", "v2");
        });

    /// <summary>Crypto.com HMAC-SHA256 (hex, header).</summary>
    /// <remarks>
    /// Signature string: method + id + apiKey + отсортированные params + nonce.
    /// Используется для REST API v1.
    /// </remarks>
    public static HmacAuthenticator CryptoCom(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey, ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.Header,
            ApiKeyHeader = "api_key",
            SignatureHeader = "sig"
        },
        static (in SignatureContext ctx) => $"{ctx.Path}{ctx.Nonce}{ctx.ApiKey}{ctx.Body}");
}
