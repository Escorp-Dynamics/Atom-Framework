using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Atom.Net.Http;

/// <summary>
/// Представляет безопасный обработчик HTTP-запросов.
/// </summary>
public class SafetyClientHandler : DelegatingHandler
{
    private readonly bool isNeedDisposeHandler;

    /// <summary>
    /// Внутренний обработчик запросов.
    /// </summary>
    public new HttpMessageHandler InnerHandler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref field);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Volatile.Write(ref field, value);
    }

    /// <summary>
    /// Данные о трафике.
    /// </summary>
    public Traffic Traffic { get; init; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SafetyClientHandler(HttpMessageHandler innerHandler, bool disposeHandler) : base(innerHandler)
    {
        InnerHandler = innerHandler;
        isNeedDisposeHandler = disposeHandler;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyClientHandler"/>.
    /// </summary>
    /// <param name="innerHandler">Базовый обработчик запросов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyClientHandler(HttpMessageHandler innerHandler) : this(innerHandler, true) => isNeedDisposeHandler = true;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SafetyClientHandler"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SafetyClientHandler() : base()
    {
        InnerHandler = new HttpClientHandler();
        isNeedDisposeHandler = true;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<SafetyHttpResponseMessage> SendInternalAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        base.InnerHandler = InnerHandler;

        try
        {
            using var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.RequestMessage ??= request;
            return new SafetyHttpResponseMessage(response, timer.Elapsed, default);
        }
        catch (Exception ex)
        {
            using var response = new HttpResponseMessage() { RequestMessage = request };
            return new SafetyHttpResponseMessage(response, timer.Elapsed, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await SendInternalAsync(request, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (isNeedDisposeHandler) InnerHandler.Dispose();
        base.Dispose(disposing);
    }
}