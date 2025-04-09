using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Http;

/// <summary>
/// Представляет расширенную версию <see cref="HttpResponseMessage"/>.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SafetyHttpResponseMessage"/>.
/// </remarks>
/// <param name="statusCode">Код статуса ответа.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class SafetyHttpResponseMessage(HttpStatusCode statusCode) : HttpResponseMessage(statusCode)
{
    /// <summary>
    /// Длительность запроса.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Данные о трафике.
    /// </summary>
    public Traffic Traffic { get; init; }

    /// <summary>
    /// Исключение, вызванное при запросе.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Определяет, был ли запрос завершён успешно.
    /// </summary>
    public bool IsCompleted => RequestMessage is not null && Exception is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SafetyHttpResponseMessage(HttpResponseMessage message, TimeSpan duration, Exception? ex) : this()
    {
        StatusCode = message.StatusCode;
        ReasonPhrase = message.ReasonPhrase;
        Content = message.Content;
        Version = message.Version;
        RequestMessage = message.RequestMessage;
        Duration = duration;
        Exception = ex;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpResponseMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpResponseMessage() : this(default) { }
}

/// <summary>
/// Представляет расширенную версию <see cref="HttpResponseMessage"/>.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SafetyHttpResponseMessage{TResult}"/>.
/// </remarks>
/// <param name="statusCode">Код статуса ответа.</param>
/// <typeparam name="TResult">Тип данных ответа после десериализации JSON.</typeparam>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class SafetyHttpResponseMessage<TResult>(HttpStatusCode statusCode) : SafetyHttpResponseMessage(statusCode)
{
    /// <summary>
    /// Десериализованные данные.
    /// </summary>
    public TResult? Data { get; init; }

    /// <summary>
    /// Определяет, доступны ли десериализованные данные.
    /// </summary>
    public bool HasData => Data is not null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SafetyHttpResponseMessage(SafetyHttpResponseMessage message, TResult? data, Exception? ex) : this()
    {
        StatusCode = message.StatusCode;
        ReasonPhrase = message.ReasonPhrase;
        Content = message.Content;
        Version = message.Version;
        RequestMessage = message.RequestMessage;
        Duration = message.Duration;
        Exception = ex ?? message.Exception;
        Traffic = message.Traffic;
        Data = data;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyHttpResponseMessage{TResult}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyHttpResponseMessage() : this(default) { }
}