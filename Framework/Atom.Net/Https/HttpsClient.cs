#pragma warning disable CA1054, CA2000, CA2234, S4136, VSTHRD002

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Atom.Net.Proxies;

namespace Atom.Net.Https;

/// <summary>
/// Представляет средство обмена HTTPS-запросами и ответами с сервером.
/// В отличие от <see cref="HttpClient"/>, не выбрасывает исключения при ошибках —
/// информация об ошибке сохраняется в <see cref="HttpsResponseMessage.Exception"/>.
/// </summary>
public class HttpsClient : IDisposable
{
    /// <summary>
    /// User-Agent клиента по умолчанию.
    /// </summary>
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient client;
    private readonly HttpClientHandler handler;
    private readonly bool disposeHandler;
    private bool isDisposed;

    /// <summary>
    /// Возвращает или задаёт контейнер файлов cookie.
    /// </summary>
    public CookieContainer Cookies
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => handler.CookieContainer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => handler.CookieContainer = value;
    }

    /// <summary>
    /// Возвращает или задаёт сведения о прокси-сервере.
    /// </summary>
    public IWebProxy? Proxy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => handler.Proxy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            handler.Proxy = value;
            handler.UseProxy = value is not null;
        }
    }

    /// <summary>
    /// Возвращает или задаёт базовый адрес.
    /// </summary>
    public Uri? BaseAddress
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => client.BaseAddress;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => client.BaseAddress = value;
    }

    /// <summary>
    /// Возвращает или задаёт таймаут запроса.
    /// </summary>
    public TimeSpan Timeout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => client.Timeout;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => client.Timeout = value;
    }

    /// <summary>
    /// Возвращает заголовки запроса по умолчанию.
    /// </summary>
    public System.Net.Http.Headers.HttpRequestHeaders DefaultRequestHeaders
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => client.DefaultRequestHeaders;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient() : this(new HttpClientHandler(), disposeHandler: true) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    /// <param name="proxy">Прокси-сервер.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient(Proxy? proxy) : this()
    {
        if (proxy is not null) Proxy = proxy;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    /// <param name="disposeHandler">Указывает, будут ли ресурсы обработчика высвобождены вместе с клиентом.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient(HttpClientHandler handler, bool disposeHandler = true)
    {
        this.handler = handler;
        this.disposeHandler = disposeHandler;
        client = new HttpClient(handler, disposeHandler: false);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
    }

    #region Send Methods

    /// <summary>
    /// Отправляет HTTP-запрос.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Ответ сервера. Исключения не выбрасываются — информация об ошибке в <see cref="HttpsResponseMessage.Exception"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async Task<HttpsResponseMessage> SendAsync([NotNull] HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new HttpsResponseMessage(response, timer.Elapsed, exception: null);
        }
        catch (Exception ex)
        {
            return HttpsResponseMessage.FromException(request, timer.Elapsed, ex);
        }
    }

    /// <summary>
    /// Отправляет HTTP-запрос.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="completionOption">Опция завершения запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Ответ сервера. Исключения не выбрасываются — информация об ошибке в <see cref="HttpsResponseMessage.Exception"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async Task<HttpsResponseMessage> SendAsync([NotNull] HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            var response = await client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
            return new HttpsResponseMessage(response, timer.Elapsed, exception: null);
        }
        catch (Exception ex)
        {
            return HttpsResponseMessage.FromException(request, timer.Elapsed, ex);
        }
    }

    /// <summary>
    /// Отправляет HTTP-запрос синхронно.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Ответ сервера. Исключения не выбрасываются — информация об ошибке в <see cref="HttpsResponseMessage.Exception"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsResponseMessage Send([NotNull] HttpRequestMessage request, CancellationToken cancellationToken = default)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    #endregion

    #region GET Methods

    /// <summary>
    /// Отправляет GET-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> GetAsync([NotNull] string requestUri, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);

    /// <summary>
    /// Отправляет GET-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> GetAsync([NotNull] Uri requestUri, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);

    /// <summary>
    /// Отправляет GET-запрос и возвращает тело ответа как строку.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async Task<string> GetStringAsync([NotNull] string requestUri, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        return response.IsCompleted ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : string.Empty;
    }

    /// <summary>
    /// Отправляет GET-запрос и возвращает тело ответа как байты.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async Task<byte[]> GetByteArrayAsync([NotNull] string requestUri, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        return response.IsCompleted ? await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false) : [];
    }

    /// <summary>
    /// Отправляет GET-запрос и возвращает тело ответа как поток.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async Task<Stream> GetStreamAsync([NotNull] string requestUri, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        return response.IsCompleted ? await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false) : Stream.Null;
    }

    #endregion

    #region POST Methods

    /// <summary>
    /// Отправляет POST-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PostAsync([NotNull] string requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content }, cancellationToken);

    /// <summary>
    /// Отправляет POST-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PostAsync([NotNull] Uri requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content }, cancellationToken);

    #endregion

    #region PUT Methods

    /// <summary>
    /// Отправляет PUT-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PutAsync([NotNull] string requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content }, cancellationToken);

    /// <summary>
    /// Отправляет PUT-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PutAsync([NotNull] Uri requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content }, cancellationToken);

    #endregion

    #region PATCH Methods

    /// <summary>
    /// Отправляет PATCH-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PatchAsync([NotNull] string requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Patch, requestUri) { Content = content }, cancellationToken);

    /// <summary>
    /// Отправляет PATCH-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="content">Содержимое запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> PatchAsync([NotNull] Uri requestUri, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Patch, requestUri) { Content = content }, cancellationToken);

    #endregion

    #region DELETE Methods

    /// <summary>
    /// Отправляет DELETE-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> DeleteAsync([NotNull] string requestUri, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri), cancellationToken);

    /// <summary>
    /// Отправляет DELETE-запрос.
    /// </summary>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> DeleteAsync([NotNull] Uri requestUri, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri), cancellationToken);

    #endregion

    #region JSON Methods

    /// <summary>
    /// Отправляет запрос с десериализацией JSON-ответа.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="request">Данные запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа для сериализации.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public virtual async Task<HttpsResponseMessage<T>> SendAsync<T>([NotNull] HttpRequestMessage request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsCompleted)
            return new HttpsResponseMessage<T>(response, data: default, exception: null);

        if (response.Content.Headers.ContentLength is 0)
            return new HttpsResponseMessage<T>(response, data: default, exception: null);

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken).ConfigureAwait(false);
            return new HttpsResponseMessage<T>(response, data, exception: null);
        }
        catch (Exception ex)
        {
            return new HttpsResponseMessage<T>(response, data: default, ex);
        }
    }

    /// <summary>
    /// Отправляет GET-запрос с десериализацией JSON-ответа.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа для сериализации.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage<T>> GetFromJsonAsync<T>([NotNull] string requestUri, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), jsonTypeInfo, cancellationToken);

    /// <summary>
    /// Отправляет POST-запрос с JSON-телом и десериализацией JSON-ответа.
    /// </summary>
    /// <typeparam name="TRequest">Тип данных запроса.</typeparam>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="requestUri">URI запроса.</param>
    /// <param name="value">Данные для отправки.</param>
    /// <param name="requestTypeInfo">Метаданные типа запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public Task<HttpsResponseMessage<TResponse>> PostAsJsonAsync<TRequest, TResponse>(
        [NotNull] string requestUri,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, requestTypeInfo);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        return SendAsync(request, responseTypeInfo, cancellationToken);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Освобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, освобождаются ли управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;

        if (disposing)
        {
            client.Dispose();
            if (disposeHandler) handler.Dispose();
        }

        isDisposed = true;
    }

    #endregion
}
