using System.Net;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebBrowser
{
    public event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? DomContentLoaded;

    public event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? NavigationCompleted;

    public event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? PageLoaded;

    private async ValueTask OnBridgeEventReceivedAsync(Protocol.BridgeMessage message)
    {
        var page = FindPage(message.TabId);
        if (page is null)
        {
            return;
        }

        if (BridgeLifecycleEventMapper.TryRead(message, out var lifecycleEvent, out var url, out var title))
        {
            var args = new WebLifecycleEventArgs
            {
                Window = page.OwnerWindow,
                Page = page,
                Frame = page.MainFrame,
                Url = url,
                Title = title,
            };

            LaunchSettings.Logger?.LogWebBrowserLifecycleEventReceived(lifecycleEvent.ToString(), page.TabId, url?.ToString() ?? "<none>");

            switch (lifecycleEvent)
            {
                case Protocol.BridgeEvent.DomContentLoaded:
                    DomContentLoaded?.Invoke(this, args);
                    break;
                case Protocol.BridgeEvent.NavigationCompleted:
                    NavigationCompleted?.Invoke(this, args);
                    break;
                case Protocol.BridgeEvent.PageLoaded:
                    PageLoaded?.Invoke(this, args);
                    break;
            }

            return;
        }

        if (BridgeEventPayloadReader.TryReadConsoleMessage(message, page.MainFrame, out var consoleArgs))
        {
            Console?.Invoke(this, consoleArgs);
            return;
        }

        if (BridgeEventPayloadReader.TryReadInterceptedRequest(message, page.MainFrame, out var requestArgs))
        {
            await InvokeAsync(Request, requestArgs).ConfigureAwait(false);
            return;
        }

        if (BridgeEventPayloadReader.TryReadInterceptedResponse(message, page.MainFrame, out var responseArgs))
        {
            await InvokeAsync(Response, responseArgs).ConfigureAwait(false);
        }
    }

    internal async ValueTask DispatchSyntheticRequestInterceptionAsync(WebPage page, InterceptedRequestEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(args);

        await page.DispatchSyntheticRequestInterceptionAsync(args).ConfigureAwait(false);
        await page.OwnerWindow.DispatchSyntheticRequestInterceptionAsync(args).ConfigureAwait(false);
        await InvokeAsync(Request, args).ConfigureAwait(false);
    }

    internal async ValueTask DispatchSyntheticResponseInterceptionAsync(WebPage page, InterceptedResponseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(args);

        await page.DispatchSyntheticResponseInterceptionAsync(args).ConfigureAwait(false);
        await page.OwnerWindow.DispatchSyntheticResponseInterceptionAsync(args).ConfigureAwait(false);
        await InvokeAsync(Response, args).ConfigureAwait(false);
    }

    private async ValueTask<BridgeCallbackHttpResponse> OnBridgeServerCallbackRequested(BridgeCallbackRequestPayload request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = FindPage(request.TabId);
        if (page is null)
        {
            LaunchSettings.Logger?.LogWebBrowserCallbackSkipped(request.Name, request.TabId);
            return BridgeCallbackHttpResponse.Continue();
        }

        LaunchSettings.Logger?.LogWebBrowserCallbackDispatching(request.Name, request.TabId, request.Args.Length);

        var args = new CallbackEventArgs
        {
            Name = request.Name,
            Args = request.Args,
            Code = request.Code,
        };

        var decision = await page.DispatchSyntheticCallbackAsync(args, cancellationToken).ConfigureAwait(false);
        return CreateBridgeCallbackResponse(decision);
    }

    private async ValueTask<BridgeInterceptHttpResponse> OnBridgeServerRequestInterceptionRequested(BridgeInterceptedRequestPayload request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = FindPage(request.TabId);
        if (page is null)
        {
            return BridgeInterceptHttpResponse.Continue();
        }

        if (!TryCreateInterceptedRequestEventArgs(request, page.MainFrame, out var args))
        {
            return BridgeInterceptHttpResponse.Continue();
        }

        try
        {
            await DispatchSyntheticRequestInterceptionAsync(page, args).ConfigureAwait(false);
            args.SetDefaultIfPending();

            var decision = await args.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);

            var proxyNavigationDecision = TryCreateProxyNavigationPendingDecision(request, args.Request, decision);
            if (proxyNavigationDecision is not null)
            {
                if (!ProxyNavigationDecisions.EnqueueDecision(page.GetOrCreateBridgeContextId(), proxyNavigationDecision, DateTimeOffset.UtcNow))
                    return BridgeInterceptHttpResponse.Abort((int)HttpStatusCode.BadGateway, "Не удалось зарегистрировать proxy-owned navigation decision.");

                return BridgeInterceptHttpResponse.Continue();
            }

            return CreateBridgeRequestResponse(request.Url, args.Request, decision);
        }
        catch (RequestInterceptionNavigationFulfillmentNotSupportedException unsupported)
        {
            return BridgeInterceptHttpResponse.Abort((int)HttpStatusCode.NotImplemented, unsupported.Message);
        }
    }

    private async ValueTask<BridgeInterceptHttpResponse> OnBridgeServerResponseInterceptionRequested(BridgeInterceptedResponsePayload responsePayload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responsePayload);

        var page = FindPage(responsePayload.TabId);
        if (page is null)
        {
            return BridgeInterceptHttpResponse.Continue();
        }

        if (!TryCreateInterceptedResponseEventArgs(responsePayload, page.MainFrame, out var args))
        {
            return BridgeInterceptHttpResponse.Continue();
        }

        await DispatchSyntheticResponseInterceptionAsync(page, args).ConfigureAwait(false);
        args.SetDefaultIfPending();

        var decision = await args.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);
        return CreateBridgeResponseResponse(decision);
    }

    private ValueTask InvokeAsync(AsyncEventHandler<IWebBrowser, InterceptedRequestEventArgs>? handler, InterceptedRequestEventArgs args)
        => InvokeCoreAsync(handler, args);

    private ValueTask InvokeAsync(AsyncEventHandler<IWebBrowser, InterceptedResponseEventArgs>? handler, InterceptedResponseEventArgs args)
        => InvokeCoreAsync(handler, args);

    private async ValueTask InvokeCoreAsync<TEventArgs>(AsyncEventHandler<IWebBrowser, TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        foreach (var entry in handler.GetInvocationList())
        {
            await ((AsyncEventHandler<IWebBrowser, TEventArgs>)entry)(this, args).ConfigureAwait(false);
        }
    }

    private WebPage? FindPage(string? tabId)
    {
        if (string.IsNullOrWhiteSpace(tabId))
        {
            return null;
        }

        foreach (var window in windows)
        {
            var page = window.Pages.Cast<WebPage>().FirstOrDefault(candidate => string.Equals(candidate.TabId, tabId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(candidate.BoundBridgeTabId) && IsMatchingBridgeTabId(candidate.BoundBridgeTabId, tabId)));
            if (page is not null)
            {
                return page;
            }
        }

        return null;
    }

    private static bool TryCreateInterceptedRequestEventArgs(BridgeInterceptedRequestPayload payload, IFrame frame, out InterceptedRequestEventArgs args)
    {
        args = default!;
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var url))
        {
            return false;
        }

        var request = new HttpsRequestMessage(new HttpMethod(payload.Method), url);
        var body = TryDecodeBase64(payload.RequestBodyBase64);
        if (body is { Length: > 0 })
        {
            request.Content = new ByteArrayContent(body);
        }

        ApplyRequestHeaders(request, payload.Headers);
        var isNavigate = string.Equals(payload.ResourceType, "main_frame", StringComparison.OrdinalIgnoreCase);

        args = new InterceptedRequestEventArgs
        {
            IsNavigate = isNavigate,
            SupportsNavigationFulfillment = payload.SupportsNavigationFulfillment,
            Request = request,
            Frame = frame,
        };

        return true;
    }

    private static BridgeCallbackHttpResponse CreateBridgeCallbackResponse(CallbackDecision decision)
        => decision.Action switch
        {
            CallbackControlAction.Abort => BridgeCallbackHttpResponse.Abort(),
            CallbackControlAction.Replace => BridgeCallbackHttpResponse.Replace(decision.Code ?? string.Empty),
            _ => BridgeCallbackHttpResponse.Continue(decision.Args),
        };

    private static bool TryCreateInterceptedResponseEventArgs(BridgeInterceptedResponsePayload payload, IFrame frame, out InterceptedResponseEventArgs args)
    {
        args = default!;
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var url))
        {
            return false;
        }

        var response = new HttpsResponseMessage((System.Net.HttpStatusCode)payload.StatusCode)
        {
            ReasonPhrase = payload.ReasonPhrase,
            RequestMessage = new HttpRequestMessage(new HttpMethod(payload.Method), url),
        };

        ApplyResponseHeaders(response, payload.Headers);
        args = new InterceptedResponseEventArgs
        {
            IsNavigate = string.Equals(payload.ResourceType, "main_frame", StringComparison.OrdinalIgnoreCase),
            Response = response,
            Frame = frame,
        };

        return true;
    }

    private static BridgeInterceptHttpResponse CreateBridgeRequestResponse(string originalUrl, HttpsRequestMessage effectiveRequest, InterceptDecision decision)
    {
        ArgumentNullException.ThrowIfNull(effectiveRequest);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Action is InterceptAction.Abort)
        {
            return BridgeInterceptHttpResponse.Abort(decision.StatusCode, decision.ReasonPhrase);
        }

        if (decision.Action is InterceptAction.Fulfill && decision.Fulfillment is { } fulfillment)
        {
            return BridgeInterceptHttpResponse.Fulfill(
                bodyBase64: fulfillment.Body is { Length: > 0 } body ? Convert.ToBase64String(body) : null,
                responseHeaders: ToHeaderDictionary(fulfillment.Response),
                statusCode: (int)fulfillment.Response.StatusCode,
                reasonPhrase: fulfillment.Response.ReasonPhrase);
        }

        var request = decision.Continuation?.Request ?? effectiveRequest;
        var url = decision.Continuation?.RedirectUrl?.ToString();
        if (url is null && request.RequestUri is { } requestUri)
        {
            var requestUrl = requestUri.ToString();
            if (!string.Equals(requestUrl, originalUrl, StringComparison.Ordinal))
            {
                url = requestUrl;
            }
        }

        return BridgeInterceptHttpResponse.Continue(
            headers: ToHeaderDictionary(request),
            url: url);
    }

    private ProxyNavigationPendingDecision? TryCreateProxyNavigationPendingDecision(BridgeInterceptedRequestPayload request, HttpsRequestMessage effectiveRequest, InterceptDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(effectiveRequest);
        ArgumentNullException.ThrowIfNull(decision);

        if (!request.SupportsNavigationFulfillment
            || !string.Equals(request.ResourceType, "main_frame", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var issuedAtUtc = request.Timestamp == default ? DateTimeOffset.UtcNow : request.Timestamp;
        var expiresAtUtc = issuedAtUtc + (bridgeServer?.RequestTimeout ?? TimeSpan.FromSeconds(5));

        return decision.Action switch
        {
            InterceptAction.Abort => new ProxyNavigationPendingDecision
            {
                RequestId = request.RequestId,
                Method = request.Method,
                AbsoluteUrl = request.Url,
                IssuedAtUtc = issuedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                Action = ProxyNavigationDecisionAction.Abort,
                StatusCode = decision.StatusCode,
                ReasonPhrase = decision.ReasonPhrase,
            },
            InterceptAction.Fulfill when decision.Fulfillment is { } fulfillment => new ProxyNavigationPendingDecision
            {
                RequestId = request.RequestId,
                Method = request.Method,
                AbsoluteUrl = request.Url,
                IssuedAtUtc = issuedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                Action = ProxyNavigationDecisionAction.Fulfill,
                StatusCode = (int)fulfillment.Response.StatusCode,
                ReasonPhrase = fulfillment.Response.ReasonPhrase,
                ResponseHeaders = ToHeaderDictionary(fulfillment.Response),
                ResponseBody = fulfillment.Body,
            },
            InterceptAction.Continue when decision.Continuation is { } continuation => CreateProxyContinuationPendingDecision(request, effectiveRequest, issuedAtUtc, expiresAtUtc, continuation),
            _ => null,
        };
    }

    private static ProxyNavigationPendingDecision? CreateProxyContinuationPendingDecision(
        BridgeInterceptedRequestPayload request,
        HttpsRequestMessage effectiveRequest,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        InterceptedRequestContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(effectiveRequest);
        ArgumentNullException.ThrowIfNull(continuation);

        var effectiveUrl = continuation.Request?.RequestUri?.ToString();
        var redirectUrl = continuation.RedirectUrl?.ToString();
        var requestHeaders = ToHeaderDictionary(continuation.Request ?? effectiveRequest);
        var requestBody = continuation.Body;

        if (redirectUrl is null
            && (effectiveUrl is null || string.Equals(effectiveUrl, request.Url, StringComparison.Ordinal))
            && requestHeaders is null
            && (requestBody is null || requestBody.Length == 0))
        {
            return null;
        }

        return new ProxyNavigationPendingDecision
        {
            RequestId = request.RequestId,
            Method = request.Method,
            AbsoluteUrl = request.Url,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            Action = redirectUrl is not null
                ? ProxyNavigationDecisionAction.Redirect
                : ProxyNavigationDecisionAction.Continue,
            RedirectUrl = redirectUrl,
            ForwardUrl = redirectUrl is null && !string.IsNullOrWhiteSpace(effectiveUrl) && !string.Equals(effectiveUrl, request.Url, StringComparison.Ordinal)
                ? effectiveUrl
                : null,
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
        };
    }

    private static BridgeInterceptHttpResponse CreateBridgeResponseResponse(InterceptedResponseEventArgs.ResponseInterceptDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Action is InterceptAction.Abort)
        {
            return BridgeInterceptHttpResponse.Abort(decision.StatusCode, decision.ReasonPhrase);
        }

        if (decision.Action is InterceptAction.Fulfill && decision.Fulfillment is { } fulfillment)
        {
            return BridgeInterceptHttpResponse.Fulfill(
                bodyBase64: fulfillment.Body is { Length: > 0 } body ? Convert.ToBase64String(body) : null,
                responseHeaders: ToHeaderDictionary(fulfillment.Response),
                statusCode: decision.StatusCode,
                reasonPhrase: decision.ReasonPhrase);
        }

        return BridgeInterceptHttpResponse.Continue(responseHeaders: decision.ResponseHeaders);
    }

    private static void ApplyRequestHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            if (request.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            request.Content ??= new ByteArrayContent([]);
            request.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static void ApplyResponseHeaders(HttpResponseMessage response, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            if (response.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            response.Content ??= new ByteArrayContent([]);
            response.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static Dictionary<string, string>? ToHeaderDictionary(HttpRequestMessage request)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is { } content)
        {
            foreach (var header in content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        return headers.Count == 0 ? null : headers;
    }

    private static Dictionary<string, string>? ToHeaderDictionary(HttpResponseMessage response)
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

        return headers.Count == 0 ? null : headers;
    }

    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}