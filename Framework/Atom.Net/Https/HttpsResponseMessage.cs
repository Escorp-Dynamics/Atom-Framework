using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https;

/// <summary>
/// Представляет расширенную версию <see cref="HttpResponseMessage"/>.
/// Содержит информацию о длительности запроса и возможном исключении.
/// </summary>
public class HttpsResponseMessage : HttpResponseMessage
{
    /// <summary>
    /// Длительность запроса.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Исключение, вызванное при запросе.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Определяет, был ли запрос завершён успешно (без исключений).
    /// </summary>
    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RequestMessage is not null && Exception is null;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsResponseMessage() : base() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage"/>.
    /// </summary>
    /// <param name="statusCode">Код статуса ответа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsResponseMessage(HttpStatusCode statusCode) : base(statusCode) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage"/> на основе <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <param name="response">Исходный ответ.</param>
    /// <param name="duration">Длительность запроса.</param>
    /// <param name="exception">Исключение при запросе, если было.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HttpsResponseMessage(HttpResponseMessage response, TimeSpan duration, Exception? exception) : base(response.StatusCode)
    {
        ReasonPhrase = response.ReasonPhrase;
        Content = response.Content;
        Version = response.Version;
        RequestMessage = response.RequestMessage;
        Duration = duration;
        Exception = exception;

        foreach (var header in response.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    /// <summary>
    /// Создаёт <see cref="HttpsResponseMessage"/> из исключения.
    /// </summary>
    /// <param name="request">Исходный запрос.</param>
    /// <param name="duration">Длительность до возникновения исключения.</param>
    /// <param name="exception">Исключение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static HttpsResponseMessage FromException(HttpRequestMessage request, TimeSpan duration, Exception exception) => new()
    {
        RequestMessage = request,
        Duration = duration,
        Exception = exception,
        StatusCode = HttpStatusCode.InternalServerError,
        Content = new ByteArrayContent([]),
    };
}

/// <summary>
/// Представляет расширенную версию <see cref="HttpResponseMessage"/> с десериализованными данными.
/// </summary>
/// <typeparam name="TResult">Тип данных ответа после десериализации JSON.</typeparam>
public class HttpsResponseMessage<TResult> : HttpsResponseMessage
{
    /// <summary>
    /// Десериализованные данные.
    /// </summary>
    public TResult? Data { get; init; }

    /// <summary>
    /// Определяет, доступны ли десериализованные данные.
    /// </summary>
    public bool HasData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data is not null;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage{TResult}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsResponseMessage() : base() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage{TResult}"/>.
    /// </summary>
    /// <param name="statusCode">Код статуса ответа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsResponseMessage(HttpStatusCode statusCode) : base(statusCode) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsResponseMessage{TResult}"/> на основе <see cref="HttpsResponseMessage"/>.
    /// </summary>
    /// <param name="response">Исходный ответ.</param>
    /// <param name="data">Десериализованные данные.</param>
    /// <param name="exception">Исключение при десериализации, если было.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HttpsResponseMessage(HttpsResponseMessage response, TResult? data, Exception? exception) : base(response.StatusCode)
    {
        ReasonPhrase = response.ReasonPhrase;
        Content = response.Content;
        Version = response.Version;
        RequestMessage = response.RequestMessage;
        Duration = response.Duration;
        Exception = exception ?? response.Exception;
        Data = data;

        foreach (var header in response.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
}
