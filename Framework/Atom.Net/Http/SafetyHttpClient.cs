#pragma warning disable CA2000

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

/// <summary>
/// Представляет безопасную реализацию <see cref="HttpClient"/>.
/// </summary>
public partial class SafetyHttpClient : HttpClient
{
    private readonly SafetyClientHandler handler;

    /// <summary>
    /// Внутренний обработчик запросов.
    /// </summary>
    public HttpMessageHandler InnerHandler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => handler.InnerHandler;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => handler.InnerHandler = value;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    /// <param name="disposeHandler">Указывает, будут ли ресурсы обработчика высвобождены вместе с клиентом.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpClient(HttpMessageHandler handler, bool disposeHandler) : base(handler, disposeHandler)
        => this.handler = new SafetyClientHandler(handler, disposeHandler);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpClient(HttpMessageHandler handler) : base(handler) => this.handler = new SafetyClientHandler(handler);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpClient"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpClient() : base(new HttpClientHandler(), true) => handler = new SafetyClientHandler();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new virtual ValueTask<SafetyHttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => handler.SendInternalAsync(request, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> SendAsync([NotNull] HttpRequestBuilder request, CancellationToken cancellationToken)
        => SendAsync(request.Build(), cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> SendAsync(HttpRequestBuilder request) => SendAsync(request, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос с десериализацией JSON.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <typeparam name="T">Тип ответа.</typeparam>
    public virtual async ValueTask<SafetyHttpResponseMessage<T>> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsCompleted) return new SafetyHttpResponseMessage<T>(response, default, default);

        T? data = default;
        if (response.Content.Headers.ContentLength is 0) return new SafetyHttpResponseMessage<T>(response, data, default);

        try
        {
            data = await response.Content.AsJsonAsync(jsonTypeInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new SafetyHttpResponseMessage<T>(response, data, ex);
        }

        return new SafetyHttpResponseMessage<T>(response, data, default);
    }

    /// <summary>
    /// Осуществляет запрос с десериализацией JSON.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа ответа.</param>
    /// <typeparam name="T">Тип ответа.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> jsonTypeInfo)
        => SendAsync(request, jsonTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public virtual async IAsyncEnumerable<T?> SendAsyncEnumerable<T>(HttpRequestMessage request, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is 0) yield break;

        var enumerable = response.Content.AsJsonAsyncEnumerable(responseTypeInfo, cancellationToken).ConfigureAwait(false);
        var iterator = enumerable.GetAsyncEnumerator();

        await using (iterator)
        {
            while (iterator.Current is not null)
            {
                yield return iterator.Current;
                await iterator.MoveNextAsync();
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<TResponse?> SendAsyncEnumerable<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo) => SendAsyncEnumerable(request, responseTypeInfo, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new virtual SafetyHttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => SendAsync(request, cancellationToken).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpResponseMessage Send([NotNull] HttpRequestBuilder request, CancellationToken cancellationToken) => Send(request.Build(), cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        handler.Dispose();
        base.Dispose(disposing);
    }
}