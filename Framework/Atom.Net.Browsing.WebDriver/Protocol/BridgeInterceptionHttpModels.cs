namespace Atom.Net.Browsing.WebDriver;

internal delegate ValueTask<BridgeCallbackHttpResponse> BridgeCallbackHandler(BridgeCallbackRequestPayload request, CancellationToken cancellationToken);

internal delegate ValueTask<BridgeInterceptHttpResponse> BridgeRequestInterceptionHandler(BridgeInterceptedRequestPayload request, CancellationToken cancellationToken);

internal delegate ValueTask<BridgeInterceptHttpResponse> BridgeResponseInterceptionHandler(BridgeInterceptedResponsePayload response, CancellationToken cancellationToken);

internal delegate ValueTask BridgeObservedRequestHeadersHandler(ObservedRequestHeadersEventArgs request, CancellationToken cancellationToken);

internal sealed class BridgeCallbackRequestPayload
{
    public required string RequestId { get; init; }

    public required string TabId { get; init; }

    public required string Name { get; init; }

    public object?[] Args { get; init; } = [];

    public string? Code { get; init; }
}

internal sealed class BridgeCallbackHttpResponse
{
    public required string Action { get; init; }

    public object?[]? Args { get; init; }

    public string? Code { get; init; }

    public static BridgeCallbackHttpResponse Continue(object?[]? args = null)
        => new()
        {
            Action = "continue",
            Args = args,
        };

    public static BridgeCallbackHttpResponse Abort()
        => new()
        {
            Action = "abort",
        };

    public static BridgeCallbackHttpResponse Replace(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new BridgeCallbackHttpResponse
        {
            Action = "replace",
            Code = code,
        };
    }
}

internal sealed class BridgeInterceptedRequestPayload
{
    public required string RequestId { get; init; }

    public required string TabId { get; init; }

    public required string Url { get; init; }

    public required string Method { get; init; }

    public string ResourceType { get; init; } = "other";

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public string? RequestBodyBase64 { get; init; }

    public IReadOnlyDictionary<string, string[]>? FormData { get; init; }

    public bool SupportsNavigationFulfillment { get; init; }

    public DateTimeOffset Timestamp { get; init; }
}

internal sealed class BridgeInterceptedResponsePayload
{
    public required string RequestId { get; init; }

    public required string TabId { get; init; }

    public required string Url { get; init; }

    public required string Method { get; init; }

    public string ResourceType { get; init; } = "other";

    public int StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public DateTimeOffset Timestamp { get; init; }
}

internal sealed class BridgeInterceptHttpResponse
{
    public required string Action { get; init; }

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }

    public int? StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }

    public string? BodyBase64 { get; init; }

    public static BridgeInterceptHttpResponse Continue(
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null,
        string? url = null)
        => new()
        {
            Action = "continue",
            Headers = headers,
            ResponseHeaders = responseHeaders,
            Url = url,
        };

    public static BridgeInterceptHttpResponse Abort(int? statusCode = null, string? reasonPhrase = null)
        => new()
        {
            Action = "abort",
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
        };

    public static BridgeInterceptHttpResponse Fulfill(
        string? bodyBase64 = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null,
        int? statusCode = null,
        string? reasonPhrase = null,
        string? url = null)
        => new()
        {
            Action = "fulfill",
            BodyBase64 = bodyBase64,
            ResponseHeaders = responseHeaders,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            Url = url,
        };
}

internal sealed class ObservedRequestHeadersEventArgs : EventArgs
{
    public required string TabId { get; init; }

    public required string RequestId { get; init; }

    public required Uri Url { get; init; }

    public required string Method { get; init; }

    public required string ResourceType { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public DateTimeOffset Timestamp { get; init; }
}