using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;
using Atom.Text;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    private const string CallbackRequestBridgeEventName = "atom-webdriver-callback-request";
    private const string CallbackFinalizedBridgeEventName = "atom-webdriver-callback-finalized";
    private const string CallbackBridgeStateKey = "__atomWebDriverCallbackBindings";
    private const string SubscribeCallbackScriptPublishBody = """
    const createRequestId = () => typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : 'callback_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 10);

    const dispatchBridgeEvent = (eventName) => {
        try {
            document.dispatchEvent(new Event(eventName));
        } catch {
        }

        try {
            globalThis.dispatchEvent(new Event(eventName));
        } catch {
        }
    };

    const publishRequest = (args) => {
        const root = document.documentElement ?? document.head ?? document.body;
        if (!root) {
            return { action: 'continue' };
        }

        const requestId = createRequestId();
        const node = document.createElement('script');
        node.type = 'application/json';
        node.dataset.atomCallbackRequest = '1';
        node.textContent = JSON.stringify({
            requestId,
            name: callbackPath,
            code: callbackPath + '(' + args.map((value) => JSON.stringify(value)).join(', ') + ')',
            args,
        });
        root.appendChild(node);
        dispatchBridgeEvent(requestDispatchEventName);

        const responseNode = document.getElementById('atom-callback-response-' + requestId);
        if (!responseNode) {
            return { action: 'continue' };
        }

        try {
            return JSON.parse(responseNode.textContent ?? 'null') ?? { action: 'continue' };
        } catch {
            return { action: 'continue' };
        } finally {
            responseNode.remove();
        }
    };

    const publishFinalized = () => {
        const root = document.documentElement ?? document.head ?? document.body;
        if (!root) {
            return;
        }

        const node = document.createElement('script');
        node.type = 'application/json';
        node.dataset.atomCallbackFinalized = '1';
        node.textContent = JSON.stringify({
            name: callbackPath,
        });
        root.appendChild(node);
        dispatchBridgeEvent(finalizedDispatchEventName);
    };

    const completeInvocation = (executor) => {
        try {
            const result = executor();
            if (result && typeof result.then === 'function') {
                return result.finally(() => publishFinalized());
            }

            publishFinalized();
            return result;
        } catch (error) {
            publishFinalized();
            throw error;
        }
    };

    const executeReplacement = (code, replacementArgs) => {
        if (typeof code !== 'string' || code.length === 0) {
            return undefined;
        }

        return Function('args', 'callbackPath', 'parent', 'previous', code)(
            replacementArgs,
            callbackPath,
            parent,
            previous);
    };
""";
    private const string SubscribeCallbackScriptClosure = """
    parent[property] = (...args) => {
        const decision = publishRequest(args);
        if (decision && decision.action === 'replace') {
            const replacementArgs = Array.isArray(decision.args) ? decision.args : args;
            return completeInvocation(() => executeReplacement(decision.code, replacementArgs));
        }

        if (decision && decision.action === 'abort') {
            return completeInvocation(() => undefined);
        }

        if (typeof previous !== 'function') {
            return completeInvocation(() => undefined);
        }

        const effectiveArgs = decision && Array.isArray(decision.args) ? decision.args : args;
        return completeInvocation(() => previous.apply(parent, effectiveArgs));
    };

    state.bindings[callbackPath] = {
        parent,
        property,
        hadOwn,
        previous,
        createdParents,
    };

    return true;
})();
""";

    public async ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageCookiesClearing(TabId);

        if (BridgeCommands is { } bridge)
        {
            await bridge.DeleteCookiesAsync(GetOrCreateBridgeContextId(), cancellationToken).ConfigureAwait(false);
            Transport.ClearAllCookies();
            return;
        }

        Transport.ClearAllCookies();
    }

    public ValueTask ClearAllCookiesAsync()
        => ClearAllCookiesAsync(CancellationToken.None);

    public async ValueTask<IEnumerable<Cookie>> GetAllCookiesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (BridgeCommands is { } bridge)
        {
            return await ReadBridgeCookiesAsync(bridge, GetOrCreateBridgeContextId(), cancellationToken).ConfigureAwait(false);
        }

        return Transport.GetAllCookies();
    }

    public ValueTask<IEnumerable<Cookie>> GetAllCookiesAsync()
        => GetAllCookiesAsync(CancellationToken.None);

    public async ValueTask SetCookiesAsync(IEnumerable<Cookie> cookies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var cookiesSnapshot = cookies as Cookie[] ?? cookies.ToArray();
        var cookieCount = cookiesSnapshot.Length;
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageCookiesSetting(TabId, cookieCount);

        if (BridgeCommands is { } bridge)
        {
            foreach (var cookie in cookiesSnapshot)
            {
                await bridge.SetCookieAsync(
                    GetOrCreateBridgeContextId(),
                    cookie.Name,
                    cookie.Value,
                    cookie.Domain,
                    cookie.Path,
                    cookie.Secure,
                    cookie.HttpOnly,
                    cookie.Expires != DateTime.MinValue ? new DateTimeOffset(cookie.Expires).ToUnixTimeSeconds() : null,
                    cancellationToken).ConfigureAwait(false);
            }

            Transport.SetCookies(cookiesSnapshot);

            return;
        }

        Transport.SetCookies(cookiesSnapshot);
    }

    public ValueTask SetCookiesAsync(IEnumerable<Cookie> cookies)
        => SetCookiesAsync(cookies, CancellationToken.None);

    public async ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        requestInterceptionState = RequestInterceptionState.Create(enabled, urlPatterns);
        await ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns)
        => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    public ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken)
        => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(bool enabled)
        => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    private static async ValueTask<IEnumerable<Cookie>> ReadBridgeCookiesAsync(PageBridgeCommandClient bridge, string contextId, CancellationToken cancellationToken)
    {
        var payload = await bridge.GetCookiesAsync(contextId, cancellationToken).ConfigureAwait(false);
        return ParseCookies(payload);
    }

    private static List<Cookie> ParseCookies(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Array)
            return [];

        List<Cookie> cookies = [];
        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(item, "name") ?? string.Empty;
            var value = GetString(item, "value") ?? string.Empty;
            var domain = GetString(item, "domain") ?? string.Empty;
            var path = GetString(item, "path") ?? "/";
            var cookie = new Cookie(name, value, path, domain);

            if (item.TryGetProperty("secure", out var secureProperty) && secureProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                cookie.Secure = secureProperty.GetBoolean();

            if (item.TryGetProperty("httpOnly", out var httpOnlyProperty) && httpOnlyProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                cookie.HttpOnly = httpOnlyProperty.GetBoolean();

            if (item.TryGetProperty("expires", out var expiresProperty) && expiresProperty.ValueKind == JsonValueKind.Number && expiresProperty.TryGetInt64(out var unixSeconds))
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

            cookies.Add(cookie);
        }

        return cookies;
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings(), cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url)
        => NavigateAsync(url, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Kind = kind }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind)
        => NavigateAsync(url, kind, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Headers = headers }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers)
        => NavigateAsync(url, headers, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Body = body }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body)
        => NavigateAsync(url, body, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Html = html }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html)
        => NavigateAsync(url, html, CancellationToken.None);

    public async ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ResetFrameDetachmentState();
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageNavigationStarting(TabId, url.ToString(), settings.Kind.ToString());

        var response = BridgeCommands is null
            ? await NavigateSyntheticWithRequestInterceptionAsync(url, settings, cancellationToken).ConfigureAwait(false)
            : Transport.Navigate(url, settings);

        await SyncTransportEventsAsync().ConfigureAwait(false);
        return response;
    }

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings)
        => NavigateAsync(url, settings, CancellationToken.None);

    private async ValueTask<HttpsResponseMessage> NavigateSyntheticWithRequestInterceptionAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken)
    {
        var interceptionState = GetEffectiveRequestInterceptionState();
        if (interceptionState?.Matches(url.ToString()) != true)
        {
            return Transport.Navigate(url, settings);
        }

        var effectiveUrl = url;
        var effectiveSettings = settings.Clone();
        var requestArgs = CreateSyntheticInterceptedRequestArgs(url, settings);

        await ReceiveBridgeEventAsync(CreateSyntheticRequestBridgeMessage(requestArgs), dispatchHandlers: false).ConfigureAwait(false);
        await OwnerWindow.OwnerBrowser.DispatchSyntheticRequestInterceptionAsync(this, requestArgs).ConfigureAwait(false);

        requestArgs.SetDefaultIfPending();
        var decision = await requestArgs.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);

        switch (decision.Action)
        {
            case InterceptAction.Abort:
                return CreateSyntheticAbortResponse(requestArgs, decision);

            case InterceptAction.Fulfill:
                return await ApplySyntheticResponseInterceptionAsync(effectiveUrl, effectiveSettings, ApplySyntheticFulfillment(effectiveUrl, effectiveSettings, decision.Fulfillment!), cancellationToken).ConfigureAwait(false);

            case InterceptAction.Continue:
                ApplySyntheticContinuation(decision.Continuation, ref effectiveUrl, ref effectiveSettings);
                var response = navigationTransport.NavigateWithoutNetworkEvents(effectiveUrl, effectiveSettings);
                return await ApplySyntheticResponseInterceptionAsync(effectiveUrl, effectiveSettings, response, cancellationToken).ConfigureAwait(false);

            default:
                return await ApplySyntheticResponseInterceptionAsync(effectiveUrl, effectiveSettings, navigationTransport.NavigateWithoutNetworkEvents(effectiveUrl, effectiveSettings), cancellationToken).ConfigureAwait(false);
        }
    }

    private InterceptedRequestEventArgs CreateSyntheticInterceptedRequestArgs(Uri url, NavigationSettings settings)
    {
        var request = new HttpsRequestMessage(settings.Body.IsEmpty ? HttpMethod.Get : HttpMethod.Post, url);

        if (settings.Headers is { Count: > 0 })
        {
            foreach (var header in settings.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!settings.Body.IsEmpty)
        {
            request.Content = new ByteArrayContent(settings.Body.ToArray());
        }

        return new InterceptedRequestEventArgs
        {
            IsNavigate = true,
            Request = request,
            Frame = MainFrame,
        };
    }

    private BridgeMessage CreateSyntheticRequestBridgeMessage(InterceptedRequestEventArgs args)
    {
        JsonObject payload = new()
        {
            ["url"] = args.Request.RequestUri?.ToString(),
            ["method"] = args.Request.Method.Method,
            ["isNavigate"] = true,
            ["type"] = "main_frame",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var headers = CreateSyntheticHeaderPayload(args.Request.Headers);
        if (headers is not null)
        {
            payload["headers"] = headers;
        }

        return new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Event,
            WindowId = WindowId,
            TabId = TabId,
            Event = BridgeEvent.RequestIntercepted,
            Payload = JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone(),
        };
    }

    private static JsonObject? CreateSyntheticHeaderPayload(HttpHeaders headers)
    {
        JsonObject? payload = null;

        foreach (var header in headers)
        {
            payload ??= new JsonObject();
            payload[header.Key] = string.Join(", ", header.Value);
        }

        return payload;
    }

    private static void ApplySyntheticContinuation(InterceptedRequestContinuation? continuation, ref Uri url, ref NavigationSettings settings)
    {
        if (continuation is null)
        {
            return;
        }

        if (continuation.RedirectUrl is not null)
        {
            url = continuation.RedirectUrl;
        }

        if (continuation.Request is not { } replacement)
        {
            return;
        }

        if (replacement.RequestUri is not null)
        {
            url = replacement.RequestUri;
        }

        var headers = replacement.Headers.ToDictionary(static header => header.Key, static header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        var body = continuation.Body is { Length: > 0 }
            ? continuation.Body
            : [];

        settings = new NavigationSettings
        {
            Kind = settings.Kind,
            Proxy = settings.Proxy,
            Html = settings.Html,
            Headers = headers.Count == 0 ? null : headers,
            Body = body,
        };
    }

    private HttpsResponseMessage ApplySyntheticFulfillment(Uri url, NavigationSettings settings, InterceptedRequestFulfillment fulfillment)
        => navigationTransport.ApplySyntheticNavigation(
            url,
            settings,
            EnsureSyntheticFulfillmentRequest(url, settings, fulfillment.Response, fulfillment.Body),
            fulfillment.Body,
            emitNetworkEvents: false);

    private async ValueTask<HttpsResponseMessage> ApplySyntheticResponseInterceptionAsync(Uri url, NavigationSettings settings, HttpsResponseMessage response, CancellationToken cancellationToken)
    {
        var responseArgs = new InterceptedResponseEventArgs
        {
            IsNavigate = true,
            Response = response,
            Frame = MainFrame,
        };

        await OwnerWindow.OwnerBrowser.DispatchSyntheticResponseInterceptionAsync(this, responseArgs).ConfigureAwait(false);

        responseArgs.SetDefaultIfPending();
        var decision = await responseArgs.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);
        var effectiveResponse = ApplySyntheticResponseDecision(url, settings, responseArgs, decision);

        await ReceiveBridgeEventAsync(navigationTransport.CreateSyntheticResponseEvent(effectiveResponse, url), dispatchHandlers: false).ConfigureAwait(false);
        return effectiveResponse;
    }

    private HttpsResponseMessage ApplySyntheticResponseDecision(Uri url, NavigationSettings settings, InterceptedResponseEventArgs args, InterceptedResponseEventArgs.ResponseInterceptDecision decision)
    {
        return decision.Action switch
        {
            InterceptAction.Abort => navigationTransport.ApplySyntheticResponseOverride(url, settings, CreateSyntheticAbortResponse(args, decision), body: null),
            InterceptAction.Fulfill => navigationTransport.ApplySyntheticResponseOverride(url, settings, EnsureSyntheticFulfillmentRequest(url, settings, decision.Fulfillment!.Response, decision.Fulfillment.Body), decision.Fulfillment.Body),
            _ => args.Response,
        };
    }

    private static HttpsResponseMessage EnsureSyntheticFulfillmentRequest(Uri url, NavigationSettings settings, HttpsResponseMessage response, byte[]? body)
    {
        response.RequestMessage ??= new HttpRequestMessage(body is { Length: > 0 } || !settings.Body.IsEmpty ? HttpMethod.Post : HttpMethod.Get, url);

        if (settings.Headers is { Count: > 0 })
        {
            foreach (var header in settings.Headers)
            {
                response.RequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    private static HttpsResponseMessage CreateSyntheticAbortResponse(InterceptedRequestEventArgs args, InterceptDecision decision)
        => new((HttpStatusCode)(decision.StatusCode ?? (int)HttpStatusCode.BadRequest))
        {
            ReasonPhrase = decision.ReasonPhrase,
            RequestMessage = new HttpRequestMessage(args.Request.Method, args.Request.RequestUri),
        };

    private static HttpsResponseMessage CreateSyntheticAbortResponse(InterceptedResponseEventArgs args, InterceptedResponseEventArgs.ResponseInterceptDecision decision)
        => new((HttpStatusCode)(decision.StatusCode ?? (int)HttpStatusCode.BadGateway))
        {
            ReasonPhrase = decision.ReasonPhrase,
            RequestMessage = args.Response.RequestMessage is { RequestUri: { } requestUri } request
                ? new HttpRequestMessage(request.Method, requestUri)
                : null,
        };

    public async ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ResetFrameDetachmentState();

        if (BridgeCommands is { } bridge && navigationTransport.CanUseBridgeReload())
        {
            var bridgeReloadUrl = await ResolveBridgeReloadUrlAsync(cancellationToken).ConfigureAwait(false) ?? new Uri("about:blank");

            OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageReloadStarting(TabId, bridgeReloadUrl.ToString());
            await bridge.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return CreateBridgeReloadAcknowledgementResponse(bridgeReloadUrl);
        }

        var transportReloadUrl = CurrentUrl ?? new Uri("about:blank");
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageReloadStarting(TabId, transportReloadUrl.ToString());
        var response = Transport.Reload(transportReloadUrl);
        await SyncTransportEventsAsync().ConfigureAwait(false);
        return response;
    }

    public ValueTask<HttpsResponseMessage> ReloadAsync()
        => ReloadAsync(CancellationToken.None);

    private async ValueTask<Uri?> ResolveBridgeReloadUrlAsync(CancellationToken cancellationToken)
    {
        var liveUrl = await GetUrlAsync(cancellationToken).ConfigureAwait(false);
        return liveUrl ?? CurrentUrl;
    }

    private static HttpsResponseMessage CreateBridgeReloadAcknowledgementResponse(Uri url)
    {
        var response = new HttpsResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url),
            ReasonPhrase = "Synthetic WebDriver navigation acknowledgement",
        };
        response.Headers.TryAddWithoutValidation("x-atom-webdriver-navigation", "synthetic");
        return response;
    }

    public ValueTask InjectScriptAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return InjectAndDispatchScriptAsync(script, cancellationToken);
    }

    public ValueTask InjectScriptAsync(string script)
        => InjectScriptAsync(script, CancellationToken.None);

    public ValueTask InjectScriptAsync(string script, bool injectToHead, CancellationToken cancellationToken)
    {
        _ = injectToHead;
        return InjectScriptAsync(script, cancellationToken);
    }

    public ValueTask InjectScriptAsync(string script, bool injectToHead)
        => InjectScriptAsync(script, injectToHead, CancellationToken.None);

    public ValueTask InjectScriptLinkAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return InjectScriptLinkCoreAsync(url, cancellationToken);
    }

    public ValueTask InjectScriptLinkAsync(Uri url)
        => InjectScriptLinkAsync(url, CancellationToken.None);

    private async ValueTask InjectAndDispatchScriptAsync(string script, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (BridgeCommands is { } bridge)
        {
            _ = await bridge.ExecuteScriptAsync(
                script,
                shadowHostElementId: null,
                frameHostElementId: null,
                preferPageContextOnNull: false,
                forcePageContextExecution: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = await EvaluateAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask InjectScriptLinkCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        if (BridgeCommands is { } bridge)
        {
            _ = await bridge.ExecuteScriptAsync(
                BuildInjectScriptLinkSource(url),
                shadowHostElementId: null,
                frameHostElementId: null,
                preferPageContextOnNull: false,
                forcePageContextExecution: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        if (TryReadInlineInjectedScript(url, out var script))
            await InjectAndDispatchScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildInjectScriptLinkSource(Uri url)
    {
        var serializedUrl = BuildJavaScriptStringLiteral(url.OriginalString);
        return $$"""
return await new Promise((resolve, reject) => {
    const source = {{serializedUrl}};
    const root = document.head ?? document.body ?? document.documentElement;
    if (!root) {
        reject(new Error('Document root is unavailable for script injection.'));
        return;
    }

    const script = document.createElement('script');
    script.async = false;
    script.src = source;
    script.addEventListener('load', () => resolve(source), { once: true });
    script.addEventListener('error', () => reject(new Error('Failed to load injected script: ' + source)), { once: true });
    root.appendChild(script);
});
""";
    }

    private static string BuildJavaScriptStringLiteral(string value)
    {
        using var builder = new ValueStringBuilder(value.Length + 16);
        builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\u2028':
                    builder.Append("\\u2028");
                    break;
                case '\u2029':
                    builder.Append("\\u2029");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static bool TryReadInlineInjectedScript(Uri url, out string script)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (url.Scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase))
        {
            script = Uri.UnescapeDataString(url.OriginalString["javascript:".Length..]);
            return !string.IsNullOrWhiteSpace(script);
        }

        if (!url.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            script = string.Empty;
            return false;
        }

        var raw = url.OriginalString;
        var separatorIndex = raw.IndexOf(',');
        if (separatorIndex < "data:".Length)
        {
            script = string.Empty;
            return false;
        }

        var metadata = raw["data:".Length..separatorIndex];
        var payload = raw[(separatorIndex + 1)..];
        var segments = metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mediaType = segments.Length > 0 && !segments[0].Equals("base64", StringComparison.OrdinalIgnoreCase)
            ? segments[0]
            : "text/plain";

        if (!IsJavaScriptMediaType(mediaType))
        {
            script = string.Empty;
            return false;
        }

        try
        {
            script = segments.Any(static segment => segment.Equals("base64", StringComparison.OrdinalIgnoreCase))
                ? Encoding.UTF8.GetString(Convert.FromBase64String(payload))
                : Uri.UnescapeDataString(payload);
            return !string.IsNullOrWhiteSpace(script);
        }
        catch (FormatException)
        {
            script = string.Empty;
            return false;
        }
    }

    private static bool IsJavaScriptMediaType(string mediaType)
        => mediaType.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/ecmascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/ecmascript", StringComparison.OrdinalIgnoreCase);

    public async ValueTask SubscribeAsync(string callbackPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        callbackSubscriptions[callbackPath] = 0;
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageCallbackSubscribed(TabId, callbackPath);
        Transport.SubscribeCallback(callbackPath);

        if (BridgeCommands is { } bridge)
            _ = await bridge.ExecuteScriptAsync(CreateSubscribeCallbackScript(callbackPath), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SubscribeAsync(string callbackPath)
        => SubscribeAsync(callbackPath, CancellationToken.None);

    public async ValueTask UnSubscribeAsync(string callbackPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        callbackSubscriptions.TryRemove(callbackPath, out _);
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageCallbackUnSubscribed(TabId, callbackPath);
        Transport.UnSubscribeCallback(callbackPath);

        if (BridgeCommands is { } bridge)
            _ = await bridge.ExecuteScriptAsync(CreateUnSubscribeCallbackScript(callbackPath), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask UnSubscribeAsync(string callbackPath)
        => UnSubscribeAsync(callbackPath, CancellationToken.None);

    private static string CreateSubscribeCallbackScript(string callbackPath)
        => string.Join(
            Environment.NewLine,
            CreateSubscribeCallbackScriptPrelude(callbackPath),
            SubscribeCallbackScriptPublishBody,
            SubscribeCallbackScriptClosure);

    private static string CreateUnSubscribeCallbackScript(string callbackPath)
        => $$"""
(() => {
    const callbackPath = {{SerializeJavaScriptString(callbackPath)}};
    const state = globalThis[{{SerializeJavaScriptString(CallbackBridgeStateKey)}}];
    const binding = state?.bindings?.[callbackPath];
    if (!binding) {
        return false;
    }

    if (binding.hadOwn) {
        binding.parent[binding.property] = binding.previous;
    } else {
        delete binding.parent[binding.property];
    }

    if (Array.isArray(binding.createdParents)) {
        for (let index = binding.createdParents.length - 1; index >= 0; index -= 1) {
            const created = binding.createdParents[index];
            const candidate = created.parent?.[created.key];
            if (candidate && typeof candidate === 'object' && Object.keys(candidate).length === 0) {
                delete created.parent[created.key];
            }
        }
    }

    delete state.bindings[callbackPath];
    return true;
})();
""";

    private static string SerializeJavaScriptString(string value)
        => string.Concat('"', JavaScriptEncoder.Default.Encode(value), '"');

    private static string CreateSubscribeCallbackScriptPrelude(string callbackPath)
        => $$"""
(() => {
    const callbackPath = {{SerializeJavaScriptString(callbackPath)}};
    const stateKey = {{SerializeJavaScriptString(CallbackBridgeStateKey)}};
    const requestDispatchEventName = {{SerializeJavaScriptString(CallbackRequestBridgeEventName)}};
    const finalizedDispatchEventName = {{SerializeJavaScriptString(CallbackFinalizedBridgeEventName)}};
    const segments = callbackPath.split('.').filter(Boolean);
    if (segments.length === 0) {
        return false;
    }

    const state = globalThis[stateKey] ?? (globalThis[stateKey] = {
        bindings: Object.create(null),
    });

    if (state.bindings[callbackPath]) {
        return true;
    }

    let parent = globalThis;
    const createdParents = [];

    for (let index = 0; index < segments.length - 1; index += 1) {
        const segment = segments[index];
        const current = parent[segment];
        if (current === null || current === undefined) {
            const container = {};
            parent[segment] = container;
            createdParents.push({ parent, key: segment });
            parent = container;
            continue;
        }

        if (typeof current !== 'object' && typeof current !== 'function') {
            return false;
        }

        parent = current;
    }

    const property = segments[segments.length - 1];
    const hadOwn = Object.prototype.hasOwnProperty.call(parent, property);
    const previous = parent[property];
""";

    public async ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        AttachedVirtualCamera = camera;
        ApplyAttachedMediaDevicesState();

        if (BridgeCommands is { } bridge)
            await bridge.SetTabContextAsync(WebBrowser.BuildSetTabContextPayload(this), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera)
        => AttachVirtualCameraAsync(camera, CancellationToken.None);

    public async ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        AttachedVirtualMicrophone = microphone;
        ApplyAttachedMediaDevicesState();

        if (BridgeCommands is { } bridge)
            await bridge.SetTabContextAsync(WebBrowser.BuildSetTabContextPayload(this), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone)
        => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    private void ApplyAttachedMediaDevicesState()
    {
        if (ResolvedDevice is null)
            return;

        var mediaDevices = ResolvedDevice.VirtualMediaDevices ??= new VirtualMediaDevicesSettings();

        if (AttachedVirtualCamera is { } camera)
        {
            mediaDevices.VideoInputEnabled = true;
            mediaDevices.VideoInputLabel = camera.Settings.Name;
            mediaDevices.VideoInputBrowserDeviceId = NormalizeDeviceIdentifier(camera.DeviceIdentifier);
            mediaDevices.GroupId ??= camera.Settings.DeviceId;
        }

        if (AttachedVirtualMicrophone is { } microphone)
        {
            mediaDevices.AudioInputEnabled = true;
            mediaDevices.AudioInputLabel = microphone.Settings.Name;
            mediaDevices.AudioInputBrowserDeviceId = NormalizeDeviceIdentifier(microphone.DeviceIdentifier);
            mediaDevices.GroupId ??= microphone.Settings.DeviceId;
        }

        var cameraGroupId = AttachedVirtualCamera?.Settings.DeviceId;
        var microphoneGroupId = AttachedVirtualMicrophone?.Settings.DeviceId;
        if (!string.IsNullOrWhiteSpace(cameraGroupId) && string.Equals(cameraGroupId, microphoneGroupId, StringComparison.Ordinal))
            mediaDevices.GroupId = cameraGroupId;
    }

    private static string? NormalizeDeviceIdentifier(string deviceIdentifier)
        => string.IsNullOrWhiteSpace(deviceIdentifier) ? null : deviceIdentifier;
}