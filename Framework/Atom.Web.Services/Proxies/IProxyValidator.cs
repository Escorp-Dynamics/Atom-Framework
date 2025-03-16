using System.Net;
using Atom.Buffers;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации валидаторов прокси.
/// </summary>
public interface IProxyValidator : IPooled
{
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
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, url, speed, statusCode, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, CancellationToken cancellationToken) => ValidateAsync(proxy, url, speed, StatusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed) => ValidateAsync(proxy, url, speed, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, url, Speed, statusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, HttpStatusCode statusCode) => ValidateAsync(proxy, url, statusCode, CancellationToken.None);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, CancellationToken cancellationToken) => ValidateAsync(proxy, url, Speed, StatusCode, cancellationToken);

    /// <summary>
    /// Валидирует прокси.
    /// </summary>
    /// <param name="proxy">Прокси.</param>
    /// <param name="url">Ссылка для валидации.</param>
    /// <returns><c>True</c>, если прокси валиден, иначе <c>false</c></returns>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url) => ValidateAsync(proxy, url, CancellationToken.None);
}