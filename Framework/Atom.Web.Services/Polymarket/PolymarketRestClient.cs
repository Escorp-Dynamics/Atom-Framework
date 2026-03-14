using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// REST-клиент для Polymarket CLOB API.
/// Покрывает все публичные и аутентифицированные эндпоинты.
/// </summary>
/// <remarks>
/// Полностью совместим с NativeAOT. Использует source-generated JSON-сериализацию.
/// Для аутентифицированных запросов использует HMAC-SHA256 подпись.
/// </remarks>
public sealed class PolymarketRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Базовый URL REST API Polymarket CLOB по умолчанию.
    /// </summary>
    public const string DefaultBaseUrl = "https://clob.polymarket.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string baseUrl;
    private PolymarketAuth? auth;
    private bool isDisposed;

    /// <summary>
    /// Rate limiter для ограничения частоты запросов. Null = без ограничений.
    /// </summary>
    public PolymarketRateLimiter? RateLimiter { get; set; }

    /// <summary>
    /// Политика повторных попыток при транзитных ошибках. Null = без повторов.
    /// </summary>
    public PolymarketRetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Middleware pipeline для перехвата запросов/ответов.
    /// Middleware выполняются в порядке добавления.
    /// </summary>
    public IList<IPolymarketMiddleware> Middleware { get; } = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketRestClient"/> с настройками по умолчанию.
    /// </summary>
    public PolymarketRestClient() : this(DefaultBaseUrl) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketRestClient"/> с указанным базовым URL.
    /// </summary>
    /// <param name="baseUrl">Базовый URL REST API Polymarket.</param>
    public PolymarketRestClient(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        this.baseUrl = baseUrl.TrimEnd('/');
        httpClient = new HttpClient();
        disposeHttpClient = true;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketRestClient"/> с внешним HttpClient.
    /// </summary>
    /// <param name="httpClient">Предоставленный HttpClient.</param>
    /// <param name="baseUrl">Базовый URL REST API Polymarket.</param>
    public PolymarketRestClient(HttpClient httpClient, string baseUrl = DefaultBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        this.baseUrl = baseUrl.TrimEnd('/');
        this.httpClient = httpClient;
        disposeHttpClient = false;
    }

    /// <summary>
    /// Устанавливает учётные данные для аутентифицированных запросов.
    /// </summary>
    /// <param name="credentials">Учётные данные API Polymarket.</param>
    public void SetAuth(PolymarketAuth credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        auth = credentials;
    }

    #region Публичные эндпоинты — Рынки

    /// <summary>
    /// Получает список всех рынков.
    /// </summary>
    /// <param name="nextCursor">Курсор пагинации (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketMarket[]?> GetMarketsAsync(
        string? nextCursor = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/markets";
        if (!string.IsNullOrEmpty(nextCursor))
            path += $"?next_cursor={Uri.EscapeDataString(nextCursor)}";

        return await GetAsync(path, PolymarketJsonContext.Default.PolymarketMarketArray, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает информацию о рынке по condition ID.
    /// </summary>
    /// <param name="conditionId">Идентификатор условия рынка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketMarket?> GetMarketAsync(
        string conditionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conditionId);
        return await GetAsync($"/market/{Uri.EscapeDataString(conditionId)}", PolymarketJsonContext.Default.PolymarketMarket, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Публичные эндпоинты — Стакан ордеров

    /// <summary>
    /// Получает стакан ордеров для указанного токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketOrderBook?> GetOrderBookAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return await GetAsync($"/book?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.PolymarketOrderBook, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает стаканы ордеров для нескольких токенов.
    /// </summary>
    /// <param name="tokenIds">Идентификаторы токенов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketOrderBook[]?> GetOrderBooksAsync(
        string[] tokenIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);
        var query = string.Join("&", tokenIds.Select(id => $"token_ids={Uri.EscapeDataString(id)}"));
        return await GetAsync($"/books?{query}", PolymarketJsonContext.Default.PolymarketOrderBookArray, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Публичные эндпоинты — Цены

    /// <summary>
    /// Получает текущую цену токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="side">Сторона (BUY/SELL).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse?> GetPriceAsync(
        string tokenId,
        PolymarketSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        var sideStr = side == PolymarketSide.Buy ? "BUY" : "SELL";
        return await GetAsync($"/price?token_id={Uri.EscapeDataString(tokenId)}&side={sideStr}", PolymarketJsonContext.Default.PolymarketPriceResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает цены для нескольких токенов.
    /// </summary>
    /// <param name="tokenIds">Идентификаторы токенов.</param>
    /// <param name="side">Сторона (BUY/SELL).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse[]?> GetPricesAsync(
        string[] tokenIds,
        PolymarketSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);
        var sideStr = side == PolymarketSide.Buy ? "BUY" : "SELL";
        var query = string.Join("&", tokenIds.Select(id => $"token_ids={Uri.EscapeDataString(id)}"));
        return await GetAsync($"/prices?{query}&side={sideStr}", PolymarketJsonContext.Default.PolymarketPriceResponseArray, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает середину спреда для токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse?> GetMidpointAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return await GetAsync($"/midpoint?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.PolymarketPriceResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает спред для токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse?> GetSpreadAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return await GetAsync($"/spread?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.PolymarketPriceResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает цену последней сделки для токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse?> GetLastTradePriceAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return await GetAsync($"/last-trade-price?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.PolymarketPriceResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает минимальный шаг цены для токена.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketPriceResponse?> GetTickSizeAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return await GetAsync($"/tick-size?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.PolymarketPriceResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет, является ли рынок neg-risk.
    /// </summary>
    /// <param name="tokenId">Идентификатор токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<bool> IsNegRiskAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        var response = await GetAsync($"/neg-risk?token_id={Uri.EscapeDataString(tokenId)}", PolymarketJsonContext.Default.Boolean, cancellationToken).ConfigureAwait(false);
        return response;
    }

    #endregion

    #region Аутентифицированные эндпоинты — Ордера

    /// <summary>
    /// Создаёт ордер в Polymarket CLOB.
    /// </summary>
    /// <param name="request">Запрос на создание ордера (с EIP-712 подписью).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketOrderResponse?> CreateOrderAsync(
        PolymarketCreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await PostAsync("/order", request, PolymarketJsonContext.Default.PolymarketCreateOrderRequest, PolymarketJsonContext.Default.PolymarketOrderResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отменяет ордер по идентификатору.
    /// </summary>
    /// <param name="orderId">Идентификатор ордера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketCancelResponse?> CancelOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(orderId);
        return await DeleteAsync($"/order/{Uri.EscapeDataString(orderId)}", PolymarketJsonContext.Default.PolymarketCancelResponse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отменяет все активные ордера пользователя.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketCancelResponse?> CancelAllOrdersAsync(
        CancellationToken cancellationToken = default) =>
        await PostEmptyAsync("/cancel-all", PolymarketJsonContext.Default.PolymarketCancelResponse, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Получает открытые ордера пользователя.
    /// </summary>
    /// <param name="market">Фильтр по рынку (condition ID, необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketOrder[]?> GetOpenOrdersAsync(
        string? market = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/orders";
        if (!string.IsNullOrEmpty(market))
            path += $"?market={Uri.EscapeDataString(market)}";

        return await GetAuthenticatedAsync(path, PolymarketJsonContext.Default.PolymarketOrderArray, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Получает историю сделок пользователя.
    /// </summary>
    /// <param name="market">Фильтр по рынку (condition ID, необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketTrade[]?> GetTradesAsync(
        string? market = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/trades";
        if (!string.IsNullOrEmpty(market))
            path += $"?market={Uri.EscapeDataString(market)}";

        return await GetAuthenticatedAsync(path, PolymarketJsonContext.Default.PolymarketTradeArray, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Аутентифицированные эндпоинты — Баланс

    /// <summary>
    /// Получает баланс и allowance пользователя.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<PolymarketBalanceAllowance?> GetBalanceAllowanceAsync(
        CancellationToken cancellationToken = default) =>
        await GetAuthenticatedAsync("/balance-allowance", PolymarketJsonContext.Default.PolymarketBalanceAllowance, cancellationToken).ConfigureAwait(false);

    #endregion

    #region HTTP-транспорт

    /// <summary>
    /// Выполняет публичный GET-запрос.
    /// </summary>
    private ValueTask<T?> GetAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) =>
        ExecuteWithPolicies(async ct =>
        {
            var url = $"{baseUrl}{path}";
            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }, cancellationToken, "GET", path);

    /// <summary>
    /// Выполняет аутентифицированный GET-запрос.
    /// </summary>
    private ValueTask<T?> GetAuthenticatedAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (auth is null)
            throw new PolymarketException("Учётные данные не установлены. Вызовите SetAuth() перед аутентифицированными запросами.");

        return ExecuteWithPolicies(async ct =>
        {
            var url = $"{baseUrl}{path}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuthHeaders(request, "GET", path);

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }, cancellationToken, "GET", path, isAuthenticated: true);
    }

    /// <summary>
    /// Выполняет аутентифицированный POST-запрос с телом.
    /// </summary>
    private ValueTask<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        if (auth is null)
            throw new PolymarketException("Учётные данные не установлены. Вызовите SetAuth() перед аутентифицированными запросами.");

        return ExecuteWithPolicies(async ct =>
        {
            var bodyJson = JsonSerializer.Serialize(body, requestTypeInfo);
            var url = $"{baseUrl}{path}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            ApplyAuthHeaders(request, "POST", path, bodyJson);

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, responseTypeInfo, ct).ConfigureAwait(false);
        }, cancellationToken, "POST", path, isAuthenticated: true, "POST", path, bodyJson: null, isAuthenticated: true);
    }

    /// <summary>
    /// Выполняет аутентифицированный POST-запрос без тела.
    /// </summary>
    private ValueTask<T?> PostEmptyAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (auth is null)
            throw new PolymarketException("Учётные данные не установлены. Вызовите SetAuth() перед аутентифицированными запросами.");

        return ExecuteWithPolicies(async ct =>
        {
            var url = $"{baseUrl}{path}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyAuthHeaders(request, "POST", path);

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }, cancellationToken, "POST", path, isAuthenticated: true, "POST", path, isAuthenticated: true);
    }

    /// <summary>
    /// Выполняет аутентифицированный DELETE-запрос.
    /// </summary>
    private ValueTask<T?> DeleteAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (auth is null)
            throw new PolymarketException("Учётные данные не установлены. Вызовите SetAuth() перед аутентифицированными запросами.");

        return ExecuteWithPolicies(async ct =>
        {
            var url = $"{baseUrl}{path}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyAuthHeaders(request, "DELETE", path);

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }, cancellationToken, "DELETE", path, isAuthenticated: true);
    }

    /// <summary>Выполняет операцию с политиками повторных попыток и ограничения скорости.</summary>
    private async ValueTask<T?> ExecuteWithPolicies<T>(
        Func<CancellationToken, ValueTask<T?>> operation,
        CancellationToken cancellationToken,
        string method = "GET",
        string path = "/",
        string? body = null,
        bool isAuthenticated = false)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        // Rate limiter
        if (RateLimiter is not null)
            await RateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Middleware: перед запросом
        PolymarketRequestContext? reqCtx = null;
        if (Middleware.Count > 0)
        {
            reqCtx = new PolymarketRequestContext
            {
                Method = method,
                Path = path,
                Url = $"{baseUrl}{path}",
                Body = body,
                IsAuthenticated = isAuthenticated
            };

            foreach (var mw in Middleware)
            {
                if (!await mw.OnRequestAsync(reqCtx, cancellationToken).ConfigureAwait(false))
                    return default; // Pipeline прерван
            }
        }

        var startTicks = Environment.TickCount64;
        Exception? exception = null;
        int statusCode = 0;

        try
        {
            T? result;
            if (RetryPolicy is not null)
                result = await RetryPolicy.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
            else
                result = await operation(cancellationToken).ConfigureAwait(false);

            statusCode = 200; // Успешный ответ (EnsureSuccessStatusCode в operation)
            return result;
        }
        catch (HttpRequestException ex)
        {
            exception = ex;
            statusCode = (int)(ex.StatusCode ?? 0);
            throw;
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            // Middleware: после ответа
            if (reqCtx is not null && Middleware.Count > 0)
            {
                var respCtx = new PolymarketResponseContext
                {
                    Request = reqCtx,
                    StatusCode = statusCode,
                    ElapsedMs = Environment.TickCount64 - startTicks,
                    Exception = exception
                };

                foreach (var mw in Middleware)
                    await mw.OnResponseAsync(respCtx, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Применяет заголовки HMAC-аутентификации к HTTP-запросу.
    /// </summary>
    private void ApplyAuthHeaders(HttpRequestMessage request, string method, string path, string? body = null)
    {
        var timestamp = PolymarketApiSigner.GetTimestamp();
        var nonce = PolymarketApiSigner.GenerateNonce();
        var signature = PolymarketApiSigner.Sign(auth!.Secret, timestamp, nonce, method, path, body);

        request.Headers.Add("POLY-ADDRESS", auth.ApiKey);
        request.Headers.Add("POLY-SIGNATURE", signature);
        request.Headers.Add("POLY-TIMESTAMP", timestamp);
        request.Headers.Add("POLY-NONCE", nonce);
        request.Headers.Add("POLY-API-KEY", auth.ApiKey);
        request.Headers.Add("POLY-PASSPHRASE", auth.Passphrase);
    }

    #endregion

    #region IMarketRestClient — явная реализация

    string IMarketRestClient.BaseUrl => baseUrl;

    async ValueTask<string?> IMarketRestClient.CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price,
        CancellationToken cancellationToken)
    {
        // Polymarket требует EIP-712 подпись — используйте CreateOrderAsync(PolymarketCreateOrderRequest)
        throw new MarketException(
            "Polymarket требует подписанный EIP-712 ордер. Используйте CreateOrderAsync(PolymarketCreateOrderRequest) напрямую.");
    }

    async ValueTask<bool> IMarketRestClient.CancelOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        var response = await CancelOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        return response is not null;
    }

    async ValueTask<double?> IMarketRestClient.GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken)
    {
        var polySide = side == TradeSide.Buy ? PolymarketSide.Buy : PolymarketSide.Sell;
        var response = await GetPriceAsync(assetId, polySide, cancellationToken).ConfigureAwait(false);
        return response?.Price is not null && double.TryParse(response.Price, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    async ValueTask<IMarketOrderBookSnapshot?> IMarketRestClient.GetOrderBookAsync(string assetId, CancellationToken cancellationToken)
    {
        var book = await GetOrderBookAsync(assetId, cancellationToken).ConfigureAwait(false);
        return book;
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы клиента.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (disposeHttpClient)
            httpClient.Dispose();
    }
}
