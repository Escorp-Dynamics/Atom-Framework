#pragma warning disable CA2000  // Ссылка на обработчик всегда удаляется в HttpClient.
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

/// <summary>
/// Представляет безопасную реализацию <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
/// </remarks>
/// <param name="handler">Обработчик запросов.</param>
/// <param name="disposeHandler">Указывает, будут ли ресурсы обработчика высвобождены вместе с клиентом.</param>
public partial class SafetyHttpClient(HttpClientHandler handler, bool disposeHandler) : IDisposable
{
    private readonly HttpClient client = new(handler, disposeHandler);

    /// <summary>
    /// Информация о прокси, используемом в запросах клиента.
    /// </summary>
    public IWebProxy? Proxy => handler.Proxy;

    /// <summary>
    /// Получает или задает базовый URI для всех запросов, отправленных этим клиентом.
    /// </summary>
    public Uri? BaseAddress
    {
        get => client.BaseAddress;
        set => client.BaseAddress = value;
    }

    /// <summary>
    /// Происходит в момент ошибки запроса.
    /// </summary>
    public event AsyncEventHandler<SafetyHttpClient, HttpRequestFailedEventArgs>? Failed;

    /// <summary>
    /// Происходит в момент получения ответа от сервера.
    /// </summary>
    public event AsyncEventHandler<SafetyHttpClient, HttpRequestEventArgs>? Requested;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    public SafetyHttpClient(HttpClientHandler handler) : this(handler, true) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
    /// </summary>
    public SafetyHttpClient() : this(new HttpClientHandler()) { }

    private async ValueTask<HttpResponseMessage> SendAsync(HttpRequestFailedEventArgs? failedEventArgs, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        try
        {
            var eventArgs = new HttpRequestEventArgs(request.RequestUri, request.Method, request.GetHeaders() ?? new Dictionary<string, string>(), request.Version, Proxy, cancellationToken);
            var response = await client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);

            eventArgs.StatusCode = response.StatusCode;
            eventArgs.ReasonPhrase = response.ReasonPhrase;
            eventArgs.ResponseHeaders = response.GetHeaders()?.AsReadOnly();

            if (completionOption is HttpCompletionOption.ResponseContentRead && request.Method != HttpMethod.Head)
                eventArgs.ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            await OnRequested(eventArgs).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            failedEventArgs ??= new HttpRequestFailedEventArgs(request.RequestUri, request.Method, request.GetHeaders() ?? new Dictionary<string, string>(), request.Version, Proxy, cancellationToken);
            failedEventArgs.Exception = ex;

            if (request.Content is not null) failedEventArgs.RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            await OnFailed(failedEventArgs).ConfigureAwait(false);

            if (failedEventArgs.Timeout != default) await Task.Delay(failedEventArgs.Timeout, cancellationToken).ConfigureAwait(false);
            failedEventArgs.Reset();

            if (failedEventArgs is { IsCancelled: false, IsRetry: true }) return await SendAsync(failedEventArgs, request, completionOption, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(default);
        }
    }

    /// <summary>
    /// Происходит в момент ошибки запроса.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    protected virtual ValueTask OnFailed(HttpRequestFailedEventArgs e) => Failed.On(this, e);

    /// <summary>
    /// Происходит в момент получения ответа от сервера.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    protected virtual ValueTask OnRequested(HttpRequestEventArgs e) => Requested.On(this, e);

    /// <summary>
    /// Высвобождает ресурсы, используемые экземпляром <see cref="SafetyHttpClient"/>.
    /// </summary>
    /// <param name="disposing">Указывает, происходит ли высвобождение управляемых ресурсов.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        client.Dispose();
        handler.Dispose();
    }

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public virtual async ValueTask<TResponse?> SendAsync<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is 0) return default;

        try
        {
            return await response.Content.AsJsonAsync(responseTypeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var failedEventArgs = new HttpRequestFailedEventArgs(request.RequestUri, request.Method, request.GetHeaders() ?? new Dictionary<string, string>(), request.Version, Proxy, cancellationToken)
            {
                Exception = ex,
                ResponseHeaders = response.GetHeaders()?.AsReadOnly(),
                ResponseVersion = response.Version,
                ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
                StatusCode = response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
            };

            if (request.Content is not null) failedEventArgs.RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            await OnFailed(failedEventArgs).ConfigureAwait(false);
            failedEventArgs.Pause();

            if (failedEventArgs.Timeout != default) await Task.Delay(failedEventArgs.Timeout, cancellationToken).ConfigureAwait(false);
            return default;
        }
    }

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> SendAsync<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo) => SendAsync(request, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public virtual async IAsyncEnumerable<TResponse?> SendAsyncEnumerable<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is 0) yield break;

        var enumerable = response.Content.AsJsonAsyncEnumerable(responseTypeInfo, cancellationToken).ConfigureAwait(false);
        var iterator = enumerable.GetAsyncEnumerator();

        await using (iterator)
            while (iterator.Current is not null)
            {
                yield return iterator.Current;

                try
                {
                    await iterator.MoveNextAsync();
                }
                catch (JsonException ex)
                {
                    var failedEventArgs = new HttpRequestFailedEventArgs(request.RequestUri, request.Method, request.GetHeaders() ?? new Dictionary<string, string>(), request.Version, Proxy, cancellationToken)
                    {
                        Exception = ex,
                        ResponseHeaders = response.GetHeaders()?.AsReadOnly(),
                        ResponseVersion = response.Version,
                        ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
                        StatusCode = response.StatusCode,
                        ReasonPhrase = response.ReasonPhrase,
                    };

                    if (request.Content is not null) failedEventArgs.RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    await OnFailed(failedEventArgs).ConfigureAwait(false);
                    failedEventArgs.Pause();

                    if (failedEventArgs.Timeout != default) await Task.Delay(failedEventArgs.Timeout, cancellationToken).ConfigureAwait(false);
                    yield break;
                }
            }
    }

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> SendAsyncEnumerable<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo) => SendAsyncEnumerable(request, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос.
    /// </summary>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Параметры ответа.</returns>
    public virtual ValueTask<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
        return SendAsync(default, request, completionOption, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос.
    /// </summary>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Параметры ответа.</returns>
    public ValueTask<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
        return SendAsync(default, request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    /// <summary>
    /// Высвобождает ресурсы, используемые экземпляром <see cref="SafetyHttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}