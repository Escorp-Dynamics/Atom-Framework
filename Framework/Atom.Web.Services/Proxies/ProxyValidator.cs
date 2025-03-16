using System.Net;
using Atom.Net.Http;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовый валидатор прокси.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ProxyValidator"/>.
/// </remarks>
/// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
/// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
public class ProxyValidator(TimeSpan speed, HttpStatusCode statusCode) : IProxyValidator
{
    /// <inheritdoc/>
    public virtual TimeSpan Speed { get; set; } = speed;

    /// <inheritdoc/>
    public virtual HttpStatusCode StatusCode { get; set; } = statusCode;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProxyValidator"/>.
    /// </summary>
    /// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
    public ProxyValidator(TimeSpan speed) : this(speed, HttpStatusCode.OK) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProxyValidator"/>.
    /// </summary>
    /// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
    public ProxyValidator(HttpStatusCode statusCode) : this(TimeSpan.FromSeconds(30), statusCode) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProxyValidator"/>.
    /// </summary>
    public ProxyValidator() : this(HttpStatusCode.OK) { }

    /// <inheritdoc/>
    public virtual async ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new SafetyHttpClient(handler, true);
        using var request = new HttpRequestMessage(HttpMethod.Connect, url);
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(speed);
        var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

        return response is not null && response.StatusCode == statusCode;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, url, speed, statusCode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed, CancellationToken cancellationToken) => ValidateAsync(proxy, url, speed, StatusCode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, TimeSpan speed) => ValidateAsync(proxy, url, speed, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, url, Speed, statusCode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, HttpStatusCode statusCode) => ValidateAsync(proxy, url, statusCode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, CancellationToken cancellationToken) => ValidateAsync(proxy, url, Speed, StatusCode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url) => ValidateAsync(proxy, url, CancellationToken.None);
}