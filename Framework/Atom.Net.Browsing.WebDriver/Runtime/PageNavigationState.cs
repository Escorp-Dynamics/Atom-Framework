using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal sealed class PageNavigationState : IPageTransport
{
    private static readonly string[] CallbackRootPrefixes = ["window.", "globalThis.", "self.", "this."];
    private readonly HashSet<string> callbackSubscriptions = new(StringComparer.Ordinal);
    private readonly List<PageNavigationEntry> history = [];
    private readonly ConcurrentQueue<BridgeMessage> events = [];
    private readonly List<Cookie> cookieStore = [];
    private readonly Lock stateGate = new();
    private readonly string tabId;
    private readonly string windowId;
    private readonly ILogger? logger;
    private int currentIndex = -1;

    public PageNavigationState(string windowId, string tabId, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);

        this.windowId = windowId;
        this.tabId = tabId;
        this.logger = logger;
    }

    public Uri? CurrentUrl => ReadUri(BridgeCommand.GetUrl);

    public string? CurrentTitle => ReadString(BridgeCommand.GetTitle);

    public string? CurrentContent => ReadString(BridgeCommand.GetContent);

    public HttpsResponseMessage Navigate(Uri url, NavigationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);

        var response = Send(CreateNavigateRequest(url, settings));
        return ReadNavigationResponse(response, url);
    }

    internal HttpsResponseMessage NavigateWithoutNetworkEvents(Uri url, NavigationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);

        var response = Send(CreateNavigateRequest(url, settings), emitNetworkEvents: false);
        return ReadNavigationResponse(response, url);
    }

    public HttpsResponseMessage Reload(Uri fallbackUrl, NavigationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackUrl);

        var effectiveSettings = settings ?? new NavigationSettings();
        var reloadSettings = effectiveSettings.Kind == NavigationKind.Reload
            ? effectiveSettings
            : new NavigationSettings
            {
                Kind = NavigationKind.Reload,
                Headers = effectiveSettings.Headers,
                Proxy = effectiveSettings.Proxy,
                Body = effectiveSettings.Body,
                Html = effectiveSettings.Html,
            };

        var response = Send(CreateNavigateRequest(fallbackUrl, reloadSettings));
        return ReadNavigationResponse(response, fallbackUrl);
    }

    public IReadOnlyList<Cookie> GetAllCookies()
    {
        lock (stateGate)
        {
            return cookieStore.Select(CloneCookie).ToArray();
        }
    }

    public void SetCookies(IEnumerable<Cookie> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        lock (stateGate)
        {
            foreach (var cookie in cookies)
            {
                var normalized = CloneCookie(cookie);
                normalized.Path = NormalizeCookiePath(normalized.Path);
                normalized.Domain ??= string.Empty;
                var existingIndex = cookieStore.FindIndex(existing => CookieMatches(existing, normalized));
                if (existingIndex >= 0)
                    cookieStore[existingIndex] = normalized;
                else
                    cookieStore.Add(normalized);
            }
        }
    }

    public void ClearAllCookies()
    {
        lock (stateGate)
        {
            cookieStore.Clear();
        }
    }

    public JsonElement? Evaluate(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var payload = new JsonObject
        {
            ["script"] = script,
        };

        var response = Send(CreateRequest(BridgeCommand.ExecuteScript, CreateJsonObjectElement(payload)));
        return ReadPayload(response);
    }

    public void SubscribeCallback(string callbackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);

        lock (stateGate)
        {
            callbackSubscriptions.Add(callbackPath);
            logger?.LogPageTransportCallbackSubscribed(callbackPath, windowId, tabId);
        }
    }

    public void UnSubscribeCallback(string callbackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);

        lock (stateGate)
        {
            callbackSubscriptions.Remove(callbackPath);
            logger?.LogPageTransportCallbackUnSubscribed(callbackPath, windowId, tabId);
        }
    }

    internal BridgeMessage Send(BridgeMessage request)
        => Send(request, emitNetworkEvents: true);

    internal BridgeMessage Send(BridgeMessage request, bool emitNetworkEvents)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Type != BridgeMessageType.Request)
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, "Поддерживаются только мостовые запросы");
            return CreateErrorResponse(request, BridgeStatus.Error, "Only request messages are supported by the local page transport.");
        }

        if (request.Command is not BridgeCommand command)
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, "Мостовой запрос не содержит команду");
            return CreateErrorResponse(request, BridgeStatus.Error, "Локальный транспорт страницы требует команду моста");
        }

        logger?.LogPageTransportRequestReceived(command.ToString(), windowId, tabId);

        if (!string.IsNullOrWhiteSpace(request.WindowId) && !string.Equals(request.WindowId, windowId, StringComparison.Ordinal))
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, "Контекст окна мостового запроса не совпадает с локальным транспортом");
            return CreateErrorResponse(request, BridgeStatus.Disconnected, "The bridge request window context does not match the local page transport.");
        }

        if (!string.IsNullOrWhiteSpace(request.TabId) && !string.Equals(request.TabId, tabId, StringComparison.Ordinal))
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, "Контекст вкладки мостового запроса не совпадает с локальным транспортом");
            return CreateErrorResponse(request, BridgeStatus.Disconnected, "The bridge request tab context does not match the local page transport.");
        }

        lock (stateGate)
        {
            return command switch
            {
                BridgeCommand.Navigate => HandleNavigate(request, emitNetworkEvents),
                BridgeCommand.GetUrl => CreateSuccessResponse(request, CreateJsonStringElement(TryGetCurrentEntry()?.Url.ToString())),
                BridgeCommand.GetTitle => CreateSuccessResponse(request, CreateJsonStringElement(TryGetCurrentEntry()?.Title)),
                BridgeCommand.GetContent => CreateSuccessResponse(request, CreateJsonStringElement(TryGetCurrentEntry()?.Content)),
                BridgeCommand.SetCookie => HandleSetCookie(request),
                BridgeCommand.GetCookies => HandleGetCookies(request),
                BridgeCommand.DeleteCookies => HandleDeleteCookies(request),
                BridgeCommand.ExecuteScript => HandleEvaluate(request),
                _ => CreateErrorResponse(request, BridgeStatus.NotFound, "Локальный транспорт страницы не поддерживает эту команду моста"),
            };
        }
    }

    public bool TryDequeueEvent([NotNullWhen(true)] out BridgeMessage? message)
        => events.TryDequeue(out message);

    internal bool CanUseBridgeReload()
    {
        lock (stateGate)
        {
            return currentIndex < 0
                || history[currentIndex].SnapshotSource is PageNavigationSnapshotSource.LiveLifecycle;
        }
    }

    internal HttpsResponseMessage ApplySyntheticNavigation(Uri url, NavigationSettings settings, HttpsResponseMessage response, byte[]? body, bool emitNetworkEvents = true)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(response);

        lock (stateGate)
        {
            var entry = CreateSyntheticEntry(url, response, body);

            if (currentIndex + 1 < history.Count)
                history.RemoveRange(currentIndex + 1, history.Count - currentIndex - 1);

            history.Add(entry);
            currentIndex = history.Count - 1;

            var request = response.RequestMessage ?? CreateRequestMessage(url, settings, body);
            response.RequestMessage = request;

            if (emitNetworkEvents)
            {
                EnqueueSyntheticResponseEvents(response, entry, url);
            }

            EnqueueNavigationEvents(entry);

            return response;
        }
    }

    internal HttpsResponseMessage ApplySyntheticResponseOverride(Uri url, NavigationSettings settings, HttpsResponseMessage response, byte[]? body)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(response);

        lock (stateGate)
        {
            var entry = CreateSyntheticEntry(url, response, body);

            if (currentIndex < 0)
            {
                history.Add(entry);
                currentIndex = history.Count - 1;
            }
            else
            {
                history[currentIndex] = entry;
            }

            response.RequestMessage ??= CreateRequestMessage(url, settings, body);
            return response;
        }
    }

    internal BridgeMessage CreateSyntheticResponseEvent(HttpsResponseMessage response, Uri fallbackUrl)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(fallbackUrl);

        lock (stateGate)
        {
            var entry = currentIndex is >= 0 && currentIndex < history.Count
                ? history[currentIndex]
                : null;

            return CreateEventMessage(BridgeEvent.ResponseReceived, CreateResponsePayload(response, entry, fallbackUrl));
        }
    }

    internal void ApplyLiveLifecycleSnapshot(Uri? url, string? title)
    {
        lock (stateGate)
        {
            if (currentIndex < 0)
            {
                if (url is null)
                    return;

                history.Add(new PageNavigationEntry(url, title, Content: null, Headers: null, Body: [], SnapshotSource: PageNavigationSnapshotSource.LiveLifecycle));
                currentIndex = history.Count - 1;
                return;
            }

            var current = history[currentIndex];
            var effectiveUrl = url ?? current.Url;
            var effectiveTitle = title ?? current.Title;

            history[currentIndex] = current with
            {
                Url = effectiveUrl,
                Title = effectiveTitle,
                SnapshotSource = PageNavigationSnapshotSource.LiveLifecycle,
            };
        }
    }

    private BridgeMessage HandleNavigate(BridgeMessage request, bool emitNetworkEvents)
    {
        if (!TryReadNavigationRequest(request.Payload, out var url, out var settings, out var error))
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, error);
            return CreateErrorResponse(request, BridgeStatus.Error, error);
        }

        var response = settings.Kind switch
        {
            NavigationKind.Back => Move(-1),
            NavigationKind.Forward => Move(1),
            NavigationKind.Reload => ReloadCore(url, settings),
            _ => Push(url, settings),
        };

        var currentEntry = TryGetCurrentEntry();
        if (emitNetworkEvents)
        {
            EnqueueRequestResponseEvents(response, settings, currentEntry, url);
        }

        EnqueueNavigationEvents(currentEntry);
        logger?.LogPageTransportNavigationApplied(settings.Kind.ToString(), url.ToString(), windowId, tabId);

        return CreateSuccessResponse(request, CreateNavigationPayload(response, TryGetCurrentEntry(), url));
    }

    private BridgeMessage HandleEvaluate(BridgeMessage request)
    {
        var payload = request.Payload;
        if (payload is not JsonElement element || !element.TryGetProperty("script", out var scriptElement) || scriptElement.ValueKind != JsonValueKind.String)
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, "Мостовой запрос ExecuteScript должен содержать строковое поле script");
            return CreateErrorResponse(request, BridgeStatus.Error, "The execute-script request must contain a string script payload.");
        }

        var script = scriptElement.GetString()!;
        logger?.LogPageTransportExecuteScript(windowId, tabId);
        EnqueueCallbackEvents(script);
        var result = EvaluateCore(script);
        return CreateSuccessResponse(request, result);
    }

    private BridgeMessage HandleSetCookie(BridgeMessage request)
    {
        if (!TryReadCookiePayload(request.Payload, out var cookie, out var error))
        {
            logger?.LogPageTransportRequestRejected(windowId, tabId, error);
            return CreateErrorResponse(request, BridgeStatus.Error, error);
        }

        var existingIndex = cookieStore.FindIndex(existing => CookieMatches(existing, cookie));
        if (existingIndex >= 0)
            cookieStore[existingIndex] = cookie;
        else
            cookieStore.Add(cookie);

        return CreateSuccessResponse(request, payload: null);
    }

    private BridgeMessage HandleGetCookies(BridgeMessage request)
        => CreateSuccessResponse(request, CreateCookieArrayPayload(cookieStore));

    private BridgeMessage HandleDeleteCookies(BridgeMessage request)
    {
        cookieStore.Clear();
        return CreateSuccessResponse(request, payload: null);
    }

    private HttpsResponseMessage Move(int offset)
    {
        var nextIndex = currentIndex + offset;
        if (nextIndex < 0 || nextIndex >= history.Count)
            return TryGetCurrentEntry() is { } current ? BuildResponse(current) : new HttpsResponseMessage(HttpStatusCode.NoContent);

        currentIndex = nextIndex;
        return BuildResponse(history[currentIndex]);
    }

    private HttpsResponseMessage ReloadCore(Uri fallbackUrl, NavigationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackUrl);

        var current = TryGetCurrentEntry();
        if (current is null)
            return Push(fallbackUrl, settings ?? new NavigationSettings());

        return BuildResponse(current);
    }

    private HttpsResponseMessage Push(Uri url, NavigationSettings settings)
    {
        var content = ResolveContent(settings);
        var entry = new PageNavigationEntry(
            url,
            ExtractTitle(content),
            content,
            settings.Headers,
            settings.Body.IsEmpty ? [] : settings.Body.ToArray(),
            SnapshotSource: PageNavigationSnapshotSource.Transport);

        if (currentIndex + 1 < history.Count)
            history.RemoveRange(currentIndex + 1, history.Count - currentIndex - 1);

        history.Add(entry);
        currentIndex = history.Count - 1;
        return BuildResponse(entry);
    }

    private PageNavigationEntry CreateSyntheticEntry(Uri url, HttpsResponseMessage response, byte[]? body)
    {
        var content = ResolveSyntheticContent(response, body);

        return new PageNavigationEntry(
            url,
            ExtractTitle(content),
            content,
            ReadResponseHeaders(response),
            body ?? [],
            SnapshotSource: PageNavigationSnapshotSource.Transport);
    }

    private static string? ResolveSyntheticContent(HttpsResponseMessage response, byte[]? body)
    {
        if (body is not { Length: > 0 })
            return null;

        var mediaType = response.Content?.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType)
            || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(body);
        }

        return null;
    }

    private static Dictionary<string, string>? ReadResponseHeaders(HttpsResponseMessage response)
    {
        Dictionary<string, string>? headers = null;

        foreach (var header in response.Headers)
        {
            headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Content is { } content)
        {
            foreach (var header in content.Headers)
            {
                headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        return headers;
    }

    private static HttpRequestMessage CreateRequestMessage(Uri url, NavigationSettings settings, byte[]? body)
    {
        var request = new HttpRequestMessage(body is { Length: > 0 } || !settings.Body.IsEmpty ? HttpMethod.Post : HttpMethod.Get, url);

        if (settings.Headers is { Count: > 0 })
        {
            foreach (var header in settings.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return request;
    }

    private void EnqueueSyntheticResponseEvents(HttpsResponseMessage response, PageNavigationEntry entry, Uri fallbackUrl)
    {
        var responsePayload = CreateResponsePayload(response, entry, fallbackUrl);
        events.Enqueue(CreateEventMessage(BridgeEvent.ResponseReceived, responsePayload));
    }

    private PageNavigationEntry? TryGetCurrentEntry()
        => currentIndex >= 0 && currentIndex < history.Count ? history[currentIndex] : null;

    private static string? ResolveContent(NavigationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Html))
            return settings.Html;

        return settings.Body.IsEmpty ? null : Encoding.UTF8.GetString(settings.Body.Span);
    }

    private static string? ExtractTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        const string openTag = "<title>";
        const string closeTag = "</title>";
        var openIndex = content.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (openIndex < 0)
            return null;

        var titleStart = openIndex + openTag.Length;
        var closeIndex = content.IndexOf(closeTag, titleStart, StringComparison.OrdinalIgnoreCase);
        if (closeIndex < 0 || closeIndex <= titleStart)
            return null;

        return content[titleStart..closeIndex].Trim();
    }

    private static string NormalizeScript(string script)
        => script.Trim().TrimEnd(';');

    private void EnqueueCallbackEvents(string script)
    {
        if (!TryParseCallbackInvocation(script, out var callbackName, out var args, out var reason))
        {
            logger?.LogPageTransportCallbackSkipped(windowId, tabId, reason);
            return;
        }

        var callbackPayload = new JsonObject
        {
            ["name"] = callbackName,
            ["code"] = NormalizeScript(script),
            ["args"] = CreateArgsArray(args),
        };
        var finalizedPayload = new JsonObject
        {
            ["name"] = callbackName,
        };

        events.Enqueue(CreateEventMessage(BridgeEvent.Callback, CreateJsonObjectElement(callbackPayload)));
        events.Enqueue(CreateEventMessage(BridgeEvent.CallbackFinalized, CreateJsonObjectElement(finalizedPayload)));
        logger?.LogPageTransportCallbackQueued(callbackName, args.Length, windowId, tabId);
    }

    private bool TryParseCallbackInvocation(string script, out string callbackName, out object?[] args, out string reason)
    {
        callbackName = string.Empty;
        args = [];
        reason = string.Empty;

        if (callbackSubscriptions.Count == 0)
        {
            reason = "нет активных подписок";
            return false;
        }

        var normalized = NormalizeCallbackExpression(NormalizeScript(script));
        if (normalized.Length == 0)
        {
            reason = "сценарий не содержит вызываемое выражение";
            return false;
        }

        var openParenIndex = normalized.IndexOf('(');
        if (openParenIndex <= 0 || normalized[^1] != ')')
        {
            reason = "сценарий не похож на прямой вызов обратного вызова";
            return false;
        }

        var candidateName = NormalizeCallbackPath(normalized[..openParenIndex]);
        if (!callbackSubscriptions.Contains(candidateName))
        {
            reason = "обратный вызов не подписан";
            return false;
        }

        var argumentsSlice = normalized[(openParenIndex + 1)..^1];
        if (!TryParseArguments(argumentsSlice, out args))
        {
            reason = "аргументы обратного вызова не удалось разобрать";
            return false;
        }

        callbackName = candidateName;
        return true;
    }

    private static ReadOnlySpan<char> NormalizeCallbackExpression(string script)
    {
        var expression = script.AsSpan().Trim();

        while (true)
        {
            if (TryTrimLeadingKeyword(expression, "return", out var trimmedExpression)
                || TryTrimLeadingKeyword(expression, "await", out trimmedExpression)
                || TryTrimLeadingKeyword(expression, "void", out trimmedExpression)
                || TryUnwrapEnclosingParentheses(expression, out trimmedExpression))
            {
                expression = trimmedExpression;
                continue;
            }

            return expression;
        }
    }

    private static bool TryTrimLeadingKeyword(ReadOnlySpan<char> expression, string keyword, out ReadOnlySpan<char> trimmedExpression)
    {
        if (expression.StartsWith(keyword, StringComparison.Ordinal)
            && expression.Length > keyword.Length
            && char.IsWhiteSpace(expression[keyword.Length]))
        {
            trimmedExpression = expression[keyword.Length..].TrimStart();
            return true;
        }

        trimmedExpression = expression;
        return false;
    }

    private static bool TryUnwrapEnclosingParentheses(ReadOnlySpan<char> expression, out ReadOnlySpan<char> unwrappedExpression)
    {
        if (expression.Length < 2 || expression[0] != '(' || expression[^1] != ')')
        {
            unwrappedExpression = expression;
            return false;
        }

        if (!IsSingleWrappedExpression(expression))
        {
            unwrappedExpression = expression;
            return false;
        }

        unwrappedExpression = expression[1..^1].Trim();
        return true;
    }

    private static bool IsSingleWrappedExpression(ReadOnlySpan<char> expression)
    {
        var depth = 0;
        var insideString = false;
        var quote = '\0';
        var escaped = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (TryAdvanceStringState(character, ref insideString, ref quote, ref escaped))
            {
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character != ')')
            {
                continue;
            }

            depth--;
            if (depth == 0 && index != expression.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && !insideString;
    }

    private static string NormalizeCallbackPath(ReadOnlySpan<char> candidate)
    {
        var current = candidate.Trim();
        while (TryTrimKnownCallbackRoot(current, out var trimmed))
        {
            current = trimmed;
        }

        return current.ToString();
    }

    private static bool TryTrimKnownCallbackRoot(ReadOnlySpan<char> candidate, out ReadOnlySpan<char> trimmed)
    {
        foreach (var prefix in CallbackRootPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.Ordinal))
            {
                trimmed = candidate[prefix.Length..];
                return true;
            }
        }

        trimmed = candidate;
        return false;
    }

    private static bool TryParseArguments(ReadOnlySpan<char> text, out object?[] args)
    {
        if (text.Trim().Length == 0)
        {
            args = [];
            return true;
        }

        List<object?> values = [];
        var tokenStart = 0;
        var insideString = false;
        var quote = '\0';
        var escaped = false;
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (TryAdvanceStringState(character, ref insideString, ref quote, ref escaped))
            {
                continue;
            }

            if (!TryAdvanceDelimiterState(character, ref parenthesisDepth, ref bracketDepth, ref braceDepth, out var isTokenBoundary))
            {
                continue;
            }

            if (!isTokenBoundary)
            {
                continue;
            }

            if (!TryParseArgument(text[tokenStart..index], out var value))
            {
                args = [];
                return false;
            }

            values.Add(value);
            tokenStart = index + 1;
        }

        if (insideString || parenthesisDepth != 0 || bracketDepth != 0 || braceDepth != 0 || !TryParseArgument(text[tokenStart..], out var finalValue))
        {
            args = [];
            return false;
        }

        values.Add(finalValue);
        args = values.ToArray();
        return true;
    }

    private static bool TryAdvanceStringState(char character, ref bool insideString, ref char quote, ref bool escaped)
    {
        if (insideString)
        {
            if (escaped)
            {
                escaped = false;
                return true;
            }

            if (character == '\\')
            {
                escaped = true;
                return true;
            }

            if (character == quote)
            {
                insideString = false;
            }

            return true;
        }

        if (character is '\'' or '"')
        {
            insideString = true;
            quote = character;
            return true;
        }

        return false;
    }

    private static bool TryAdvanceDelimiterState(char character, ref int parenthesisDepth, ref int bracketDepth, ref int braceDepth, out bool isTokenBoundary)
    {
        isTokenBoundary = false;

        switch (character)
        {
            case '(':
                parenthesisDepth++;
                return true;
            case ')':
                parenthesisDepth--;
                return true;
            case '[':
                bracketDepth++;
                return true;
            case ']':
                bracketDepth--;
                return true;
            case '{':
                braceDepth++;
                return true;
            case '}':
                braceDepth--;
                return true;
            case ',':
                isTokenBoundary = parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseArgument(ReadOnlySpan<char> token, out object? value)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (trimmed.Length >= 2 && ((trimmed[0] == '\'' && trimmed[^1] == '\'') || (trimmed[0] == '"' && trimmed[^1] == '"')))
        {
            value = trimmed[1..^1].ToString();
            return true;
        }

        if (trimmed.SequenceEqual("true"))
        {
            value = true;
            return true;
        }

        if (trimmed.SequenceEqual("false"))
        {
            value = false;
            return true;
        }

        if (trimmed.SequenceEqual("null"))
        {
            value = null;
            return true;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64Value))
        {
            value = int64Value;
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            value = doubleValue;
            return true;
        }

        value = trimmed.ToString();
        return true;
    }

    private static JsonArray CreateArgsArray(IEnumerable<object?> args)
    {
        JsonArray values = [];
        foreach (var value in args)
        {
            values.Add(value switch
            {
                null => null,
                bool boolValue => JsonValue.Create(boolValue),
                long int64Value => JsonValue.Create(int64Value),
                double doubleValue => JsonValue.Create(doubleValue),
                string stringValue => JsonValue.Create(stringValue),
                _ => JsonValue.Create(value.ToString()),
            });
        }

        return values;
    }

    private JsonElement? EvaluateCore(string script)
    {
        return NormalizeScript(script) switch
        {
            "document.title" => CreateJsonStringElement(ReadString(BridgeCommand.GetTitle)),
            "window.location.href" or "location.href" or "document.URL" => CreateJsonStringElement(ReadUri(BridgeCommand.GetUrl)?.ToString()),
            "document.cookie" => CreateJsonStringElement(BuildCookieHeader(cookieStore)),
            "document.documentElement.outerHTML" or "document.body.outerHTML" or "document.body.innerHTML" => CreateJsonStringElement(ReadString(BridgeCommand.GetContent)),
            _ => null,
        };
    }

    private Uri? ReadUri(BridgeCommand command)
    {
        var payload = ReadPayload(Send(CreateRequest(command)));
        return payload is JsonElement element && element.ValueKind == JsonValueKind.String && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private string? ReadString(BridgeCommand command)
    {
        var payload = ReadPayload(Send(CreateRequest(command)));
        return payload is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => null,
                _ => element.ToString(),
            }
            : null;
    }

    private BridgeMessage CreateRequest(BridgeCommand command, JsonElement? payload = null)
    {
        return new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Request,
            WindowId = windowId,
            TabId = tabId,
            Command = command,
            Payload = payload,
        };
    }

    private BridgeMessage CreateSuccessResponse(BridgeMessage request, JsonElement? payload)
    {
        return new BridgeMessage
        {
            Id = request.Id,
            Type = BridgeMessageType.Response,
            WindowId = request.WindowId ?? windowId,
            TabId = request.TabId ?? tabId,
            Command = request.Command,
            Status = BridgeStatus.Ok,
            Payload = payload,
        };
    }

    private BridgeMessage CreateErrorResponse(BridgeMessage request, BridgeStatus status, string error)
    {
        return new BridgeMessage
        {
            Id = request.Id,
            Type = BridgeMessageType.Response,
            WindowId = request.WindowId ?? windowId,
            TabId = request.TabId ?? tabId,
            Command = request.Command,
            Status = status,
            Error = error,
        };
    }

    private BridgeMessage CreateNavigateRequest(Uri url, NavigationSettings settings)
    {
        var payload = new JsonObject
        {
            ["url"] = url.ToString(),
            ["kind"] = settings.Kind.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(settings.Html))
            payload["html"] = settings.Html;

        if (!settings.Body.IsEmpty)
            payload["bodyBase64"] = Convert.ToBase64String(settings.Body.Span);

        if (settings.Headers is { Count: > 0 })
        {
            var headers = new JsonObject();
            foreach (var header in settings.Headers)
                headers[header.Key] = header.Value;

            payload["headers"] = headers;
        }

        return CreateRequest(BridgeCommand.Navigate, CreateJsonObjectElement(payload));
    }

    private static bool TryReadNavigationRequest(JsonElement? payload, out Uri url, out NavigationSettings settings, out string error)
    {
        url = default!;
        settings = default!;
        error = string.Empty;

        if (payload is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            error = "The navigate request must contain an object payload.";
            return false;
        }

        if (!element.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            error = "The navigate request must contain an absolute url.";
            return false;
        }

        var urlValue = urlElement.GetString();
        if (!Uri.TryCreate(urlValue, UriKind.Absolute, out var parsedUrl))
        {
            error = "The navigate request must contain an absolute url.";
            return false;
        }

        url = parsedUrl;

        var kind = NavigationKind.Default;
        if (element.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String &&
            Enum.TryParse(kindElement.GetString(), ignoreCase: true, out NavigationKind parsedKind))
        {
            kind = parsedKind;
        }

        var headers = ReadHeaders(element);
        var html = element.TryGetProperty("html", out var htmlElement) && htmlElement.ValueKind == JsonValueKind.String
            ? htmlElement.GetString()
            : null;
        var body = ReadBody(element);

        settings = new NavigationSettings
        {
            Kind = kind,
            Headers = headers,
            Body = body,
            Html = html,
        };

        return true;
    }

    private static Dictionary<string, string>? ReadHeaders(JsonElement payload)
    {
        if (!payload.TryGetProperty("headers", out var headersElement) || headersElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, string>? headers = null;
        foreach (var property in headersElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            headers ??= [];
            headers[property.Name] = property.Value.GetString()!;
        }

        return headers;
    }

    private static ReadOnlyMemory<byte> ReadBody(JsonElement payload)
    {
        if (!payload.TryGetProperty("bodyBase64", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.String)
            return ReadOnlyMemory<byte>.Empty;

        var value = bodyElement.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? ReadOnlyMemory<byte>.Empty
            : Convert.FromBase64String(value);
    }

    private static HttpsResponseMessage ReadNavigationResponse(BridgeMessage response, Uri fallbackUrl)
    {
        EnsureSuccess(response);

        var payload = ReadPayload(response);
        if (payload is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return new HttpsResponseMessage(HttpStatusCode.NoContent)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, fallbackUrl),
            };
        }

        var statusCode = element.TryGetProperty("statusCode", out var statusCodeElement) && statusCodeElement.TryGetInt32(out var parsedStatusCode)
            ? (HttpStatusCode)parsedStatusCode
            : HttpStatusCode.OK;
        var effectiveUrl = element.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var parsedUrl)
            ? parsedUrl
            : fallbackUrl;
        var content = element.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString()
            : null;
        var body = ReadBody(element);
        var request = new HttpRequestMessage(body.Length > 0 ? HttpMethod.Post : HttpMethod.Get, effectiveUrl);

        var headers = ReadHeaders(element);
        if (headers is not null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return new HttpsResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = BuildTransportContent(content, body),
        };
    }

    private static HttpContent BuildTransportContent(string? content, ReadOnlyMemory<byte> body)
    {
        if (!string.IsNullOrWhiteSpace(content))
            return new StringContent(content, Encoding.UTF8, "text/html");

        return body.Length > 0 ? new ByteArrayContent(body.ToArray()) : new ByteArrayContent([]);
    }

    private static JsonElement? ReadPayload(BridgeMessage response)
    {
        EnsureSuccess(response);
        return response.Payload;
    }

    private static void EnsureSuccess(BridgeMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Type != BridgeMessageType.Response)
            throw new BridgeException("Локальный транспорт получил мостовое сообщение не типа ответа");

        if (response.Status is null or BridgeStatus.Ok)
            return;

        throw new BridgeException(response.Error ?? "Локальный транспорт вернул ответ с ошибкой");
    }

    private static JsonElement CreateNavigationPayload(HttpsResponseMessage response, PageNavigationEntry? entry, Uri fallbackUrl)
    {
        var payload = new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["url"] = (entry?.Url ?? fallbackUrl).ToString(),
        };

        if (!string.IsNullOrWhiteSpace(entry?.Title))
            payload["title"] = entry.Title;

        if (!string.IsNullOrWhiteSpace(entry?.Content))
            payload["content"] = entry.Content;

        if (entry?.Headers is { Count: > 0 })
        {
            var headers = new JsonObject();
            foreach (var header in entry.Headers)
                headers[header.Key] = header.Value;

            payload["headers"] = headers;
        }

        if (entry is { Body.Length: > 0 })
            payload["bodyBase64"] = Convert.ToBase64String(entry.Body);

        return CreateJsonObjectElement(payload);
    }

    private void EnqueueNavigationEvents(PageNavigationEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var payload = CreateEventPayload(entry);
        events.Enqueue(CreateEventMessage(BridgeEvent.DomContentLoaded, payload));
        events.Enqueue(CreateEventMessage(BridgeEvent.NavigationCompleted, payload));
        events.Enqueue(CreateEventMessage(BridgeEvent.PageLoaded, payload));
        logger?.LogPageTransportLifecycleEventsQueued(entry.Url.ToString(), windowId, tabId);
    }

    private void EnqueueRequestResponseEvents(HttpsResponseMessage response, NavigationSettings settings, PageNavigationEntry? entry, Uri fallbackUrl)
    {
        var requestPayload = CreateRequestPayload(response, settings, entry, fallbackUrl);
        var responsePayload = CreateResponsePayload(response, entry, fallbackUrl);
        events.Enqueue(CreateEventMessage(BridgeEvent.RequestIntercepted, requestPayload));
        events.Enqueue(CreateEventMessage(BridgeEvent.ResponseReceived, responsePayload));
    }

    private BridgeMessage CreateEventMessage(BridgeEvent @event, JsonElement payload)
    {
        return new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Event,
            WindowId = windowId,
            TabId = tabId,
            Event = @event,
            Payload = payload,
        };
    }

    private static JsonElement CreateEventPayload(PageNavigationEntry entry)
    {
        var payload = new JsonObject
        {
            ["url"] = entry.Url.ToString(),
            ["snapshotSource"] = "transport",
        };

        if (!string.IsNullOrWhiteSpace(entry.Title))
        {
            payload["title"] = entry.Title;
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            payload["content"] = entry.Content;
        }

        return CreateJsonObjectElement(payload);
    }

    private static JsonElement CreateRequestPayload(HttpsResponseMessage response, NavigationSettings settings, PageNavigationEntry? entry, Uri fallbackUrl)
    {
        var request = response.RequestMessage;
        var headers = request?.Headers.ToDictionary(static header => header.Key, static header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase)
            ?? entry?.Headers?.ToDictionary(static header => header.Key, static header => header.Value, StringComparer.OrdinalIgnoreCase);
        var payload = new JsonObject
        {
            ["url"] = (request?.RequestUri ?? entry?.Url ?? fallbackUrl).ToString(),
            ["method"] = request?.Method.Method ?? (settings.Body.IsEmpty ? HttpMethod.Get.Method : HttpMethod.Post.Method),
            ["isNavigate"] = true,
            ["type"] = "main_frame",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        if (headers is { Count: > 0 })
        {
            var headersObject = new JsonObject();
            foreach (var header in headers)
            {
                headersObject[header.Key] = header.Value;
            }

            payload["headers"] = headersObject;
        }

        if (!settings.Body.IsEmpty)
        {
            payload["bodyBase64"] = Convert.ToBase64String(settings.Body.Span);
        }

        return CreateJsonObjectElement(payload);
    }

    private static JsonElement CreateResponsePayload(HttpsResponseMessage response, PageNavigationEntry? entry, Uri fallbackUrl)
    {
        var payload = new JsonObject
        {
            ["url"] = (response.RequestMessage?.RequestUri ?? entry?.Url ?? fallbackUrl).ToString(),
            ["method"] = response.RequestMessage?.Method.Method ?? HttpMethod.Get.Method,
            ["statusCode"] = (int)response.StatusCode,
            ["isNavigate"] = true,
            ["type"] = "main_frame",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        if (!string.IsNullOrWhiteSpace(response.ReasonPhrase))
        {
            payload["reasonPhrase"] = response.ReasonPhrase;
        }

        if (entry?.Headers is { Count: > 0 })
        {
            var headersObject = new JsonObject();
            foreach (var header in entry.Headers)
            {
                headersObject[header.Key] = header.Value;
            }

            payload["headers"] = headersObject;
        }

        if (!string.IsNullOrWhiteSpace(entry?.Content))
        {
            payload["content"] = entry.Content;
        }

        if (entry is { Body.Length: > 0 })
        {
            payload["bodyBase64"] = Convert.ToBase64String(entry.Body);
        }

        return CreateJsonObjectElement(payload);
    }

    private static JsonElement CreateJsonObjectElement(JsonObject value)
        => JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();

    private static JsonElement CreateJsonStringElement(string? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement CreateCookieArrayPayload(IEnumerable<Cookie> values)
    {
        JsonArray payload = [];
        foreach (var cookie in values)
        {
            var cookiePayload = new JsonObject
            {
                ["name"] = cookie.Name,
                ["value"] = cookie.Value,
                ["domain"] = cookie.Domain,
                ["path"] = NormalizeCookiePath(cookie.Path),
                ["secure"] = cookie.Secure,
                ["httpOnly"] = cookie.HttpOnly,
            };

            if (cookie.Expires != DateTime.MinValue)
                cookiePayload["expires"] = new DateTimeOffset(cookie.Expires).ToUnixTimeSeconds();

            payload.Add(cookiePayload);
        }

        return JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
    }

    private static string BuildCookieHeader(IEnumerable<Cookie> values)
        => string.Join("; ", values.Select(static cookie => string.Concat(cookie.Name, "=", cookie.Value)));

    private static bool TryReadCookiePayload(JsonElement? payload, out Cookie cookie, out string error)
    {
        cookie = default!;
        error = string.Empty;

        if (payload is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            error = "Команда SetCookie должна содержать объект payload";
            return false;
        }

        if (!element.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            error = "Команда SetCookie должна содержать имя cookie";
            return false;
        }

        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Команда SetCookie должна содержать непустое имя cookie";
            return false;
        }

        var value = element.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString() ?? string.Empty
            : string.Empty;
        var domain = element.TryGetProperty("domain", out var domainElement) && domainElement.ValueKind == JsonValueKind.String
            ? domainElement.GetString() ?? string.Empty
            : string.Empty;
        var path = element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String
            ? NormalizeCookiePath(pathElement.GetString())
            : "/";

        cookie = new Cookie(name, value, path, domain);
        return true;
    }

    private static string NormalizeCookiePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? "/" : path;

    private static bool CookieMatches(Cookie left, Cookie right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal)
           && string.Equals(left.Domain ?? string.Empty, right.Domain ?? string.Empty, StringComparison.Ordinal)
           && string.Equals(NormalizeCookiePath(left.Path), NormalizeCookiePath(right.Path), StringComparison.Ordinal);

    private static Cookie CloneCookie(Cookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);

        var clone = new Cookie(cookie.Name, cookie.Value, NormalizeCookiePath(cookie.Path), cookie.Domain ?? string.Empty)
        {
            Secure = cookie.Secure,
            HttpOnly = cookie.HttpOnly,
            Expired = cookie.Expired,
            Version = cookie.Version,
        };

        if (cookie.Expires != DateTime.MinValue)
            clone.Expires = cookie.Expires;

        return clone;
    }

    private static HttpsResponseMessage BuildResponse(PageNavigationEntry entry)
    {
        var request = new HttpRequestMessage(entry.Body.Length > 0 ? HttpMethod.Post : HttpMethod.Get, entry.Url);
        if (entry.Headers is not null)
        {
            foreach (var header in entry.Headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var response = new HttpsResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = BuildContent(entry),
        };

        return response;
    }

    private static HttpContent BuildContent(PageNavigationEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Content))
            return new StringContent(entry.Content, Encoding.UTF8, "text/html");

        if (entry.Body.Length > 0)
            return new ByteArrayContent(entry.Body);

        return new ByteArrayContent([]);
    }
}

internal enum PageNavigationSnapshotSource
{
    Transport,
    LiveLifecycle,
}

internal sealed record PageNavigationEntry(
    Uri Url,
    string? Title,
    string? Content,
    IReadOnlyDictionary<string, string>? Headers,
    byte[] Body,
    PageNavigationSnapshotSource SnapshotSource);