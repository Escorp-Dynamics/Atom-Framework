using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

internal enum InterceptAction
{
    Continue,
    Abort,
    Fulfill,
}

internal sealed class InterceptedRequestContinuation
{
    public HttpsRequestMessage? Request { get; init; }

    public Uri? RedirectUrl { get; init; }

    public byte[]? Body { get; init; }
}

internal sealed class InterceptedRequestFulfillment
{
    public required HttpsResponseMessage Response { get; init; }

    public byte[]? Body { get; init; }
}

internal sealed class InterceptDecision
{
    public required InterceptAction Action { get; init; }

    public InterceptedRequestContinuation? Continuation { get; init; }

    public InterceptedRequestFulfillment? Fulfillment { get; init; }

    public int? StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }
}

/// <summary>
/// Содержит данные события перехвата исходящего браузерного запроса.
/// </summary>
public sealed class InterceptedRequestEventArgs : EventArgs
{
    private readonly TaskCompletionSource<InterceptDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool SupportsNavigationFulfillment { get; init; } = true;

    /// <summary>
    /// Получает признак того, что запрос является навигационным.
    /// </summary>
    public bool IsNavigate { get; init; }

    /// <summary>
    /// Получает исходный HTTP-запрос.
    /// </summary>
    public required HttpsRequestMessage Request { get; init; }

    /// <summary>
    /// Получает фрейм, из которого был инициирован запрос.
    /// </summary>
    public required IFrame Frame { get; init; }

    /// <summary>
    /// Продолжает выполнение исходного запроса без изменений.
    /// </summary>
    public ValueTask ContinueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Continue,
        });
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Продолжает выполнение исходного запроса без изменений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ContinueAsync()
        => ContinueAsync(CancellationToken.None);

    /// <summary>
    /// Продолжает выполнение с подменой исходного запроса.
    /// </summary>
    public ValueTask ContinueAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ContinueWithRequestCoreAsync(request, cancellationToken);
    }

    /// <summary>
    /// Продолжает выполнение с подменой исходного запроса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ContinueAsync(HttpsRequestMessage request)
        => ContinueAsync(request, CancellationToken.None);

    /// <summary>
    /// Выполняет редирект запроса на новый адрес.
    /// </summary>
    public ValueTask RedirectAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Continue,
            Continuation = new InterceptedRequestContinuation
            {
                RedirectUrl = url,
            },
        });

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Выполняет редирект запроса на новый адрес.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask RedirectAsync(Uri url)
        => RedirectAsync(url, CancellationToken.None);

    /// <summary>
    /// Прерывает выполнение запроса.
    /// </summary>
    public ValueTask AbortAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Abort,
        });

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Прерывает выполнение запроса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync()
        => AbortAsync(CancellationToken.None);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом.
    /// </summary>
    public ValueTask AbortAsync(HttpStatusCode statusCode, CancellationToken cancellationToken)
        => AbortAsync((int)statusCode, reasonPhrase: null, cancellationToken);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(HttpStatusCode statusCode)
        => AbortAsync(statusCode, reasonPhrase: null, CancellationToken.None);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом.
    /// </summary>
    public ValueTask AbortAsync(int statusCode, CancellationToken cancellationToken)
        => AbortAsync(statusCode, reasonPhrase: null, cancellationToken);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(int statusCode)
        => AbortAsync(statusCode, reasonPhrase: null, CancellationToken.None);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом и фразой причины.
    /// </summary>
    public ValueTask AbortAsync(HttpStatusCode statusCode, string? reasonPhrase, CancellationToken cancellationToken)
        => AbortAsync((int)statusCode, reasonPhrase, cancellationToken);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом и фразой причины.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(HttpStatusCode statusCode, string? reasonPhrase)
        => AbortAsync((int)statusCode, reasonPhrase, CancellationToken.None);

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом и фразой причины.
    /// </summary>
    public ValueTask AbortAsync(int statusCode, string? reasonPhrase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Abort,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
        });

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Прерывает выполнение запроса с HTTP-статусом и фразой причины.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(int statusCode, string? reasonPhrase)
        => AbortAsync(statusCode, reasonPhrase, CancellationToken.None);

    /// <summary>
    /// Подменяет исходный ответ собственным HTTP-ответом.
    /// </summary>
    public ValueTask FulfillAsync(HttpsResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        return FulfillCoreAsync(response, cancellationToken);
    }

    /// <summary>
    /// Подменяет исходный ответ собственным HTTP-ответом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask FulfillAsync(HttpsResponseMessage response)
        => FulfillAsync(response, CancellationToken.None);

    internal Task<InterceptDecision> WaitForDecisionAsync(CancellationToken cancellationToken)
        => decision.Task.WaitAsync(cancellationToken);

    internal void SetDefaultIfPending()
        => decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Continue,
        });

    private async ValueTask FulfillCoreAsync(HttpsResponseMessage response, CancellationToken cancellationToken)
    {
        if (IsNavigate && !SupportsNavigationFulfillment)
            throw new RequestInterceptionNavigationFulfillmentNotSupportedException();

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Fulfill,
            Fulfillment = new InterceptedRequestFulfillment
            {
                Response = response,
                Body = body,
            },
        });
    }

    private async ValueTask ContinueWithRequestCoreAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        decision.TrySetResult(new InterceptDecision
        {
            Action = InterceptAction.Continue,
            Continuation = new InterceptedRequestContinuation
            {
                Request = request,
                Body = body,
            },
        });
    }
}

internal sealed class RequestInterceptionNavigationFulfillmentNotSupportedException : NotSupportedException
{
    private const string DefaultMessage = "Bridge-backed request-side main_frame fulfill не поддерживается. Используйте response interception или synthetic navigation path.";

    public RequestInterceptionNavigationFulfillmentNotSupportedException()
        : base(DefaultMessage)
    {
    }

    public RequestInterceptionNavigationFulfillmentNotSupportedException(string message)
        : base(message)
    {
    }

    public RequestInterceptionNavigationFulfillmentNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}