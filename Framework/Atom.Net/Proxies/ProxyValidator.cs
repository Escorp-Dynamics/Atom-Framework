using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https;

namespace Atom.Net.Proxies;

/// <summary>
/// Представляет базовый валидатор прокси.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ProxyValidator"/>.
/// </remarks>
/// <param name="request">Данные запроса.</param>
/// <param name="speed">Максимально допустимый предел скорости ответа от сервера, при котором прокси будет считаться невалидным.</param>
/// <param name="statusCode">Код статуса ответа сервера, при котором прокси будет считаться валидным.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract class ProxyValidator(HttpsRequestBuilder request, TimeSpan speed, HttpStatusCode statusCode) : IProxyValidator
{
    /// <inheritdoc/>
    public virtual HttpsRequestBuilder Request { get; set; } = request;

    /// <inheritdoc/>
    public virtual TimeSpan Speed { get; set; } = speed;

    /// <inheritdoc/>
    public virtual HttpStatusCode StatusCode { get; set; } = statusCode;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, [NotNull] HttpsRequestBuilder request, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var handler = new HttpsClientHandler { Proxy = proxy, UseProxy = true, CheckCertificateRevocationList = true };
        using var client = new HttpsClient(handler, disposeHandler: true);
        using var speedCts = new CancellationTokenSource(speed);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, speedCts.Token);

        using var response = await client.SendAsync(request.Build(), linkedCts.Token).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        return new ProxyResponseMessage(response, response.IsCompleted && !speedCts.IsCancellationRequested, response.StatusCode == statusCode);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, request, speed, statusCode, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, TimeSpan speed, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, Request, speed, statusCode, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, TimeSpan speed, HttpStatusCode statusCode) => ValidateAsync(proxy, speed, statusCode, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed, CancellationToken cancellationToken) => ValidateAsync(proxy, request, speed, StatusCode, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, TimeSpan speed) => ValidateAsync(proxy, request, speed, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, HttpStatusCode statusCode, CancellationToken cancellationToken) => ValidateAsync(proxy, request, Speed, statusCode, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, HttpStatusCode statusCode) => ValidateAsync(proxy, request, statusCode, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request, CancellationToken cancellationToken) => ValidateAsync(proxy, request, Speed, StatusCode, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, HttpsRequestBuilder request) => ValidateAsync(proxy, request, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy, CancellationToken cancellationToken) => ValidateAsync(proxy, Request, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ProxyResponseMessage> ValidateAsync(Proxy proxy) => ValidateAsync(proxy, CancellationToken.None);
}