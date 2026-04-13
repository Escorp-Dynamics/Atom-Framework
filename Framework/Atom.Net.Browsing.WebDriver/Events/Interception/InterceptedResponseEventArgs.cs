using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Содержит данные события перехвата входящего браузерного ответа.
/// </summary>
public sealed class InterceptedResponseEventArgs : MutableEventArgs
{
    private readonly TaskCompletionSource<ResponseInterceptDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Получает признак того, что ответ относится к навигационному запросу.
    /// </summary>
    public bool IsNavigate { get; init; }

    /// <summary>
    /// Получает HTTP-ответ, подготовленный к передаче браузеру.
    /// </summary>
    public required HttpsResponseMessage Response { get; init; } = new();

    /// <summary>
    /// Получает фрейм, для которого получен ответ.
    /// </summary>
    public required IFrame Frame { get; init; }

    /// <summary>
    /// Продолжает обработку ответа без изменений.
    /// </summary>
    public ValueTask ContinueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(ResponseInterceptDecision.Continue(Response));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Продолжает обработку ответа без изменений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ContinueAsync()
        => ContinueAsync(CancellationToken.None);

    /// <summary>
    /// Прерывает обработку ответа.
    /// </summary>
    public ValueTask AbortAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IsCancelled = true;
        decision.TrySetResult(ResponseInterceptDecision.Abort(statusCode: null, reasonPhrase: null));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Прерывает обработку ответа.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync()
        => AbortAsync(CancellationToken.None);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом.
    /// </summary>
    public ValueTask AbortAsync(HttpStatusCode statusCode, CancellationToken cancellationToken)
        => AbortAsync((int)statusCode, reasonPhrase: null, cancellationToken);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(HttpStatusCode statusCode)
        => AbortAsync(statusCode, reasonPhrase: null, CancellationToken.None);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом.
    /// </summary>
    public ValueTask AbortAsync(int statusCode, CancellationToken cancellationToken)
        => AbortAsync(statusCode, reasonPhrase: null, cancellationToken);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(int statusCode)
        => AbortAsync(statusCode, reasonPhrase: null, CancellationToken.None);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом и фразой причины.
    /// </summary>
    public ValueTask AbortAsync(HttpStatusCode statusCode, string? reasonPhrase, CancellationToken cancellationToken)
        => AbortAsync((int)statusCode, reasonPhrase, cancellationToken);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом и фразой причины.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(HttpStatusCode statusCode, string? reasonPhrase)
        => AbortAsync((int)statusCode, reasonPhrase, CancellationToken.None);

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом и фразой причины.
    /// </summary>
    public ValueTask AbortAsync(int statusCode, string? reasonPhrase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IsCancelled = true;
        decision.TrySetResult(ResponseInterceptDecision.Abort(statusCode, reasonPhrase));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Прерывает обработку ответа с HTTP-статусом и фразой причины.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync(int statusCode, string? reasonPhrase)
        => AbortAsync(statusCode, reasonPhrase, CancellationToken.None);

    /// <summary>
    /// Подменяет ответ перед передачей браузеру.
    /// </summary>
    public ValueTask FulfillAsync(HttpsResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        return FulfillCoreAsync(response, cancellationToken);
    }

    /// <summary>
    /// Подменяет ответ перед передачей браузеру.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask FulfillAsync(HttpsResponseMessage response)
        => FulfillAsync(response, CancellationToken.None);

    internal Task<ResponseInterceptDecision> WaitForDecisionAsync(CancellationToken cancellationToken)
        => decision.Task.WaitAsync(cancellationToken);

    internal void SetDefaultIfPending()
        => decision.TrySetResult(ResponseInterceptDecision.Continue(Response));

    private async ValueTask FulfillCoreAsync(HttpsResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        decision.TrySetResult(ResponseInterceptDecision.Fulfill(response, body));
    }

    internal sealed class ResponseInterceptDecision
    {
        public required InterceptAction Action { get; init; }

        public HttpsResponseMessage? Response { get; init; }

        public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }

        public InterceptedRequestFulfillment? Fulfillment { get; init; }

        public int? StatusCode { get; init; }

        public string? ReasonPhrase { get; init; }

        public static ResponseInterceptDecision Continue(HttpsResponseMessage response)
            => new()
            {
                Action = InterceptAction.Continue,
                Response = response,
                ResponseHeaders = ToResponseHeaders(response),
            };

        public static ResponseInterceptDecision Abort(int? statusCode, string? reasonPhrase)
            => new()
            {
                Action = InterceptAction.Abort,
                StatusCode = statusCode,
                ReasonPhrase = reasonPhrase,
            };

        public static ResponseInterceptDecision Fulfill(HttpsResponseMessage response, byte[]? body)
            => new()
            {
                Action = InterceptAction.Fulfill,
                Response = response,
                ResponseHeaders = ToResponseHeaders(response),
                Fulfillment = new InterceptedRequestFulfillment
                {
                    Response = response,
                    Body = body,
                },
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
            };

        private static Dictionary<string, string> ToResponseHeaders(HttpsResponseMessage response)
        {
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            if (response.Content is { } content)
            {
                foreach (var header in content.Headers)
                {
                    headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            return headers;
        }
    }
}