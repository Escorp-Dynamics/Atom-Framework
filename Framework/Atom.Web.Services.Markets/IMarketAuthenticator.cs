using System.Security.Cryptography;
using System.Text;
using System.Globalization;

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
/// Kraken: двухшаговая подпись SHA256(nonce + postData) → HMAC-SHA512(path + sha256hash).
/// Генерирует nonce (микросекунды), prepend его к body, подписывает, добавляет заголовки.
/// </summary>
public sealed class KrakenAuthenticator : IMarketAuthenticator
{
    private readonly string apiKey;
    private readonly byte[] secretBytes;
    private bool isDisposed;

    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecretBase64">Секрет в формате Base64.</param>
    public KrakenAuthenticator(string apiKey, string apiSecretBase64)
    {
        this.apiKey = apiKey;
        secretBytes = Convert.FromBase64String(apiSecretBase64);
    }

    /// <inheritdoc />
    public string AlgorithmName => "SHA256+HMAC-SHA512 (Kraken)";

    /// <inheritdoc />
    public void SignRequest(HttpRequestMessage request, string body = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var path = request.RequestUri?.AbsolutePath ?? throw new MarketException("RequestUri is null");
        var nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000)
            .ToString(CultureInfo.InvariantCulture);

        var fullBody = $"nonce={nonce}&{body}";

        // Шаг 1: SHA256(nonce + fullBody)
        var sha256Hash = SHA256.HashData(Encoding.UTF8.GetBytes(nonce + fullBody));

        // Шаг 2: HMAC-SHA512(urlPath + sha256hash)
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var message = new byte[pathBytes.Length + sha256Hash.Length];
        pathBytes.CopyTo(message, 0);
        sha256Hash.CopyTo(message, pathBytes.Length);
        var signature = Convert.ToBase64String(HMACSHA512.HashData(secretBytes, message));

        request.Content = new StringContent(fullBody, Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.TryAddWithoutValidation("API-Key", apiKey);
        request.Headers.TryAddWithoutValidation("API-Sign", signature);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        CryptographicOperations.ZeroMemory(secretBytes);
    }
}

/// <summary>
/// HTX (Huobi): HMAC-SHA256 подпись "METHOD\nHOST\nPATH\nSortedAuthParams".
/// Добавляет auth-параметры (AccessKeyId, SignatureMethod, SignatureVersion, Timestamp, Signature) в URL query.
/// </summary>
public sealed class HtxAuthenticator : IMarketAuthenticator
{
    private readonly string apiKey;
    private readonly byte[] secretBytes;
    private readonly string host;
    private bool isDisposed;

    /// <summary>
    /// Создаёт аутентификатор HTX с HMAC-SHA256 подписью query-параметров.
    /// </summary>
    /// <param name="apiKey">Публичный API-ключ.</param>
    /// <param name="apiSecret">Секретный ключ для подписи.</param>
    /// <param name="host">Хост API, участвующий в pre-sign string.</param>
    public HtxAuthenticator(string apiKey, string apiSecret, string host = "api.huobi.pro")
    {
        this.apiKey = apiKey;
        this.host = host;
        secretBytes = Encoding.UTF8.GetBytes(apiSecret);
    }

    /// <inheritdoc />
    public string AlgorithmName => "HMAC-SHA256 (HTX)";

    /// <inheritdoc />
    public void SignRequest(HttpRequestMessage request, string body = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var uri = request.RequestUri ?? throw new MarketException("RequestUri is null");
        var path = uri.AbsolutePath;
        var method = request.Method.Method;

        var authParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = apiKey,
            ["SignatureMethod"] = "HmacSHA256",
            ["SignatureVersion"] = "2",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
        };

        var queryString = string.Join("&", authParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var preSign = $"{method}\n{host}\n{path}\n{queryString}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        var signature = Uri.EscapeDataString(Convert.ToBase64String(hash));

        request.RequestUri = new Uri($"{uri.GetLeftPart(UriPartial.Path)}?{queryString}&Signature={signature}", UriKind.RelativeOrAbsolute);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        CryptographicOperations.ZeroMemory(secretBytes);
    }
}

/// <summary>
/// Bitstamp v2: HMAC-SHA256 подпись "BITSTAMP {apiKey}{timestamp}{nonce}{contentType}{path}{query}{body}".
/// Добавляет 5 заголовков: X-Auth, X-Auth-Signature, X-Auth-Nonce, X-Auth-Timestamp, X-Auth-Version.
/// </summary>
public sealed class BitstampAuthenticator : IMarketAuthenticator
{
    private readonly string apiKey;
    private readonly byte[] secretBytes;
    private bool isDisposed;

    /// <summary>
    /// Создаёт аутентификатор Bitstamp v2 с HMAC-SHA256 подписью заголовков.
    /// </summary>
    /// <param name="apiKey">Публичный API-ключ.</param>
    /// <param name="apiSecret">Секретный ключ для подписи.</param>
    public BitstampAuthenticator(string apiKey, string apiSecret)
    {
        this.apiKey = apiKey;
        secretBytes = Encoding.UTF8.GetBytes(apiSecret);
    }

    /// <inheritdoc />
    public string AlgorithmName => "HMAC-SHA256 (Bitstamp v2)";

    /// <inheritdoc />
    public void SignRequest(HttpRequestMessage request, string body = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var uri = request.RequestUri ?? throw new MarketException("RequestUri is null");
        var path = uri.AbsolutePath;
        var query = uri.Query.TrimStart('?');
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var contentType = body.Length > 0 ? "application/x-www-form-urlencoded" : "";

        var preSign = $"BITSTAMP {apiKey}{timestamp}{nonce}{contentType}{path}{query}{body}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        var signature = Convert.ToHexStringLower(hash);

        request.Headers.TryAddWithoutValidation("X-Auth", $"BITSTAMP {apiKey}");
        request.Headers.TryAddWithoutValidation("X-Auth-Signature", signature);
        request.Headers.TryAddWithoutValidation("X-Auth-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Auth-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Auth-Version", "v2");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        CryptographicOperations.ZeroMemory(secretBytes);
    }
}

/// <summary>
/// Crypto.com: HMAC-SHA256 подпись "method + id + apiKey + sortedParams + nonce".
/// Парсит JSON-body, вычисляет подпись, добавляет api_key и sig в JSON.
/// </summary>
public sealed class CryptoComAuthenticator : IMarketAuthenticator
{
    private readonly string apiKey;
    private readonly byte[] secretBytes;
    private bool isDisposed;

    /// <summary>
    /// Создаёт аутентификатор Crypto.com с HMAC-SHA256 подписью JSON payload.
    /// </summary>
    /// <param name="apiKey">Публичный API-ключ.</param>
    /// <param name="apiSecret">Секретный ключ для подписи.</param>
    public CryptoComAuthenticator(string apiKey, string apiSecret)
    {
        this.apiKey = apiKey;
        secretBytes = Encoding.UTF8.GetBytes(apiSecret);
    }

    /// <inheritdoc />
    public string AlgorithmName => "HMAC-SHA256 (Crypto.com)";

    /// <inheritdoc />
    public void SignRequest(HttpRequestMessage request, string body = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var node = System.Text.Json.Nodes.JsonNode.Parse(body)?.AsObject()
            ?? throw new MarketException("Невалидный JSON body для Crypto.com подписи");

        var method = node["method"]?.GetValue<string>() ?? "";
        var id = node["id"]?.GetValue<int>() ?? 0;
        var nonce = node["nonce"]?.GetValue<long>().ToString(CultureInfo.InvariantCulture) ?? "";

        var paramStr = "";
        if (node.TryGetPropertyValue("params", out var paramsNode) && paramsNode is System.Text.Json.Nodes.JsonObject paramsObj)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in paramsObj)
                sorted[kv.Key] = kv.Value?.ToString() ?? "";
            paramStr = string.Join("", sorted.Select(kv => $"{kv.Key}{kv.Value}"));
        }

        var preSign = $"{method}{id}{apiKey}{paramStr}{nonce}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));

        node["api_key"] = apiKey;
        node["sig"] = Convert.ToHexStringLower(hash);

        request.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.QueryParameter,
            ApiKeyHeader = "X-MBX-APIKEY",
            SignatureQueryParam = "signature"
        },
        static (in SignatureContext ctx) => ctx.Query.Length > 0 ? ctx.Query : ctx.Body,
        static (req, cfg, _, _) => req.Headers.TryAddWithoutValidation("X-MBX-APIKEY", cfg.ApiKey));

    /// <summary>Kraken SHA256→HMAC-SHA512 (base64, header) — двухэтапная подпись.</summary>
    public static KrakenAuthenticator Kraken(string apiKey, string apiSecretBase64) => new(apiKey, apiSecretBase64);

    /// <summary>Coinbase Advanced Trade HMAC-SHA256 (hex, header).</summary>
    public static HmacAuthenticator Coinbase(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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
            ApiKey = apiKey,
            ApiSecret = apiSecret,
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

    /// <summary>HTX (Huobi) HMAC-SHA256 — auth-параметры + Signature в URL query.</summary>
    public static HtxAuthenticator Htx(string apiKey, string apiSecret, string host = "api.huobi.pro") =>
        new(apiKey, apiSecret, host);

    /// <summary>MEXC HMAC-SHA256 (hex, query parameter — Binance-совместимый).</summary>
    public static HmacAuthenticator Mexc(string apiKey, string apiSecret) => new(
        HashAlgorithmName.SHA256,
        new HmacAuthenticatorConfig
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            OutputFormat = HmacOutputFormat.HexLower,
            Placement = SignaturePlacement.QueryParameter,
            ApiKeyHeader = "X-MEXC-APIKEY",
            SignatureQueryParam = "signature"
        },
        static (in SignatureContext ctx) => ctx.Query.Length > 0 ? ctx.Query : ctx.Body,
        static (req, cfg, _, _) => req.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", cfg.ApiKey));

    /// <summary>Bitstamp HMAC-SHA256 (hex, header) — v2 аутентификация, 5 заголовков.</summary>
    public static BitstampAuthenticator Bitstamp(string apiKey, string apiSecret) => new(apiKey, apiSecret);

    /// <summary>Crypto.com HMAC-SHA256 — подпись method+id+apiKey+sortedParams+nonce в JSON body.</summary>
    public static CryptoComAuthenticator CryptoCom(string apiKey, string apiSecret) => new(apiKey, apiSecret);
}
