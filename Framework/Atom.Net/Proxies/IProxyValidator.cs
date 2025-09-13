using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https;

namespace Atom.Net.Proxies;

/// <summary>
/// Представляет базовый интерфейс для реализации валидаторов прокси.
/// </summary>
public interface IProxyValidator
{
    /// <summary>
    /// Строитель HTTP-запроса, который будет использоваться для валидации.
    /// </summary>
    HttpsRequestBuilder Request { get; set; }

    /// <summary>
    /// Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.
    /// </summary>
    TimeSpan Speed { get; set; }

    /// <summary>
    /// Код статуса ответа сервера, при котором прокси будет считаться валидным.
    /// </summary>
    HttpStatusCode StatusCode { get; set; }

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, request, speed, statusCode, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, Request, speed, statusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, speed, statusCode, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed, CancellationToken cancellationToken) => ValidateAsync(proxy, request, speed, StatusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed) => ValidateAsync(proxy, request, speed, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, request, Speed, statusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, HttpStatusCode statusCode) => ValidateAsync(proxy, request, statusCode, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, CancellationToken cancellationToken) => ValidateAsync(proxy, request, Speed, StatusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="request">Данные запроса.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request) => ValidateAsync(proxy, request, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, CancellationToken cancellationToken) => ValidateAsync(proxy, Request, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy) => ValidateAsync(proxy, CancellationToken.None);
}