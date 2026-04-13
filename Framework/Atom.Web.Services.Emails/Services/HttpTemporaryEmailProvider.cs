using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация для HTTP-провайдеров временной почты.
/// </summary>
public abstract class HttpTemporaryEmailProvider<TOptions> : TemporaryEmailProvider
    where TOptions : HttpTemporaryEmailProviderOptions
{
    private readonly bool disposeHttpClient;

    /// <summary>
    /// Инициализирует HTTP-провайдер временной почты.
    /// </summary>
    protected HttpTemporaryEmailProvider(TOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        Options = options;
        HttpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute) };
        disposeHttpClient = httpClient is null;

        if (HttpClient.BaseAddress is null)
        {
            HttpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        }
    }

    /// <summary>
    /// Настройки текущего HTTP-провайдера.
    /// </summary>
    protected TOptions Options { get; }

    /// <summary>
    /// HTTP-клиент для обращения к upstream API.
    /// </summary>
    protected HttpClient HttpClient { get; }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && disposeHttpClient)
        {
            HttpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Создаёт JSON body для HTTP-запроса.
    /// </summary>
    protected static StringContent CreateJsonContent<T>(T payload, JsonTypeInfo<T> typeInfo, string mediaType)
        => new(JsonSerializer.Serialize(payload, typeInfo), Encoding.UTF8, mediaType);

    /// <summary>
    /// Создаёт HTTP-запрос с нужным Accept и User-Agent.
    /// </summary>
    protected static HttpRequestMessage CreateRequest(HttpMethod method, string path, bool acceptJsonLd)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptJsonLd ? "application/ld+json" : "application/json"));
        request.Headers.UserAgent.ParseAdd("Atom.TemporaryEmailProvider/1.0");
        return request;
    }

    /// <summary>
    /// Создаёт авторизованный HTTP-запрос с Bearer token.
    /// </summary>
    protected static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string token, bool acceptJsonLd)
    {
        var request = CreateRequest(method, path, acceptJsonLd);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Десериализует JSON из HTTP content через source-generated JsonTypeInfo.
    /// </summary>
    protected static async ValueTask<T?> ReadFromJsonAsync<T>(HttpContent content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет запрос, проверяет успешность ответа и десериализует JSON body.
    /// </summary>
    protected async ValueTask<T?> SendAndReadJsonAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);

        using (request)
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await ReadFromJsonAsync(response.Content, typeInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Отправляет запрос и проверяет успешность ответа без десериализации body.
    /// </summary>
    protected async ValueTask SendAndEnsureSuccessAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using (request)
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Выполняет thread-safe refresh доменов через внешний loader и обновляет provider cache.
    /// </summary>
    protected static async ValueTask<string[]> RefreshDomainCacheAsync(
        SemaphoreSlim refreshGate,
        Func<CancellationToken, ValueTask<string[]>> loader,
        Action<string[]> cacheSetter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshGate);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(cacheSetter);

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var domains = await loader(cancellationToken).ConfigureAwait(false);
            cacheSetter(domains);
            return domains;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private static void ValidateOptions(TOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GeneratedAliasPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GeneratedPasswordSuffix);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.GeneratedAliasRandomLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.GeneratedPasswordRandomLength, 1);
    }
}