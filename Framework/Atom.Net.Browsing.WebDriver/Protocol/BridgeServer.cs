using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Минимальный каркасный мостовой сервер для будущего транспорта WebSocket на стороне браузера
/// </summary>
internal sealed class BridgeServer(BridgeSettings settings) : IAsyncDisposable
{
    private const int MaxExtensionDebugEvents = 200;
    private const int MaxConnectedMessageBytes = 16 * 1024 * 1024;
    private string HostName { get; } = settings.Host;
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cts = new();
    private readonly BridgeServerState state = new(settings.Logger);
    private readonly ConcurrentQueue<string> extensionDebugEvents = new();
    private readonly ConcurrentDictionary<string, BridgePendingFulfillment> pendingFulfillments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WebSocket> sessionSockets = new(StringComparer.Ordinal);
    private readonly BridgeManagedDeliveryServer managedDeliveryServer = new(
        settings.Host,
        settings.ManagedDeliveryPort,
        settings.ManagedExtensionDelivery,
        settings.Logger);
    private BridgeManagedExtensionDelivery? managedExtensionDelivery = settings.ManagedExtensionDelivery;
    private BridgeNavigationProxyServer? navigationProxyServer;
    private ProxyNavigationDecisionRegistry? navigationProxyDecisions;
    private BridgeSecureTransportServer? secureTransportServer;
    private Task? acceptLoop;
    private bool isDisposed;

    /// <summary>
    /// Фактический порт, на котором запущен bridge endpoint.
    /// </summary>
    public int Port { get; private set; }

    internal string Host => HostName;

    /// <summary>
    /// Фактический TLS-порт, на котором запущен WSS transport endpoint.
    /// </summary>
    public int SecureTransportPort { get; private set; }

    /// <summary>
    /// Фактический TLS-порт, на котором запущен managed-delivery endpoint.
    /// </summary>
    public int ManagedDeliveryPort => managedDeliveryServer.Port;

    /// <summary>
    /// Фактический порт, на котором запущен navigation proxy endpoint.
    /// </summary>
    public int NavigationProxyPort { get; private set; }

    /// <summary>
    /// Требуются ли fallback-флаги браузеру для обхода недоверенного сертификата managed-delivery.
    /// </summary>
    public bool ManagedDeliveryRequiresCertificateBypass => managedDeliveryServer.RequiresCertificateBypass;

    /// <summary>
    /// Диагностика результата установки доверия для managed-delivery сертификата.
    /// </summary>
    public BridgeManagedDeliveryTrustDiagnostics ManagedDeliveryTrustDiagnostics => managedDeliveryServer.TrustDiagnostics;

    internal TimeSpan RequestTimeout { get; } = settings.RequestTimeout;

    /// <summary>
    /// Количество активных каналов вкладок. Пока каркас не маршрутизирует каналы
    /// </summary>
    [SuppressMessage("Performance", "MA0041:Make property static", Justification = "ConnectionCount is part of the future instance server surface and will become stateful as channels are added.")]
    public int ConnectionCount { get; private set; }

    internal BridgeCommandClient Commands => new(this);

    internal event Action<string, BridgeMessage>? RuntimeEventReceived;

    internal event BridgeCallbackHandler? CallbackRequested;

    internal event BridgeRequestInterceptionHandler? RequestInterceptionRequested;

    internal event BridgeResponseInterceptionHandler? ResponseInterceptionRequested;

    internal event BridgeObservedRequestHeadersHandler? RequestHeadersObserved;

    internal void ConfigureNavigationProxyDecisions(ProxyNavigationDecisionRegistry decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        navigationProxyDecisions = decisions;
    }

    internal string[] GetExtensionDebugEventsSnapshot() => [.. extensionDebugEvents];

    private void RecordSyntheticDebugEvent(string kind, JsonObject details)
    {
        JsonObject payload = new()
        {
            ["kind"] = kind,
            ["details"] = details,
        };

        extensionDebugEvents.Enqueue(payload.ToJsonString());
        TrimExtensionDebugEvents();
    }

    internal ValueTask<BridgeBrowserSessionSnapshot?> CreateSessionSnapshotAsync(string sessionId)
        => state.CreateSessionSnapshotAsync(sessionId);

    internal ValueTask<BridgeTabChannelSnapshot[]> GetTabsForSessionAsync(string sessionId)
        => state.GetTabsForSessionAsync(sessionId);

    internal void ConfigureManagedExtensionDelivery(BridgeManagedExtensionDelivery? delivery)
    {
        managedExtensionDelivery = delivery;
        managedDeliveryServer.Configure(delivery);
    }

    /// <summary>
    /// Запускает мостовой слушатель
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (listener.IsListening)
            return;

        secureTransportServer ??= new BridgeSecureTransportServer(
            settings.Host,
            settings.SecureTransportPort,
            settings.Secret,
            HandleAcceptedWebSocketConnectionAsync,
            settings.Logger);
        navigationProxyServer ??= new BridgeNavigationProxyServer(
            settings.Host,
            settings.NavigationProxyPort,
            () => navigationProxyDecisions,
            HandleNavigationProxyDirectRequestAsync,
            settings.Logger);

        StartBridgeListener();
        await managedDeliveryServer.StartAsync().ConfigureAwait(false);
        await secureTransportServer.StartAsync().ConfigureAwait(false);
        await navigationProxyServer.StartAsync().ConfigureAwait(false);
        SecureTransportPort = secureTransportServer.Port;
        NavigationProxyPort = navigationProxyServer.Port;
        LogManagedDeliveryTrustState();
        acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);
        settings.Logger?.LogBridgeServerStarted(settings.Host, Port);
    }

    internal ValueTask<string> GetTitleAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendStringCommandAsync(sessionId, tabId, BridgeCommand.GetTitle, "title", cancellationToken);

    internal ValueTask<string> GetUrlAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendStringCommandAsync(sessionId, tabId, BridgeCommand.GetUrl, "url", cancellationToken);

    internal ValueTask<string> GetContentAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendStringCommandAsync(sessionId, tabId, BridgeCommand.GetContent, "content", cancellationToken);

    internal ValueTask<string?> CaptureScreenshotAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendNullableStringCommandAsync(sessionId, tabId, BridgeCommand.CaptureScreenshot, new JsonObject(), cancellationToken);

    internal async ValueTask NavigateAsync(string sessionId, string tabId, string rawTabId, string url, CancellationToken cancellationToken = default)
        => _ = await SendJsonPayloadCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.Navigate,
            new JsonObject
            {
                ["url"] = url,
                ["tabId"] = rawTabId,
            },
            cancellationToken).ConfigureAwait(false);

    internal ValueTask ReloadAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(sessionId, tabId, BridgeCommand.Reload, payload: null, cancellationToken);

    internal ValueTask<JsonElement> ExecuteScriptAsync(string sessionId, string tabId, string script, CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId: null, frameHostElementId: null, elementId: null, preferPageContextOnNull: false, forcePageContextExecution: false, cancellationToken: cancellationToken);

    internal ValueTask<JsonElement> ExecuteScriptAsync(string sessionId, string tabId, string script, string shadowHostElementId, CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId: null, elementId: null, preferPageContextOnNull: false, forcePageContextExecution: false, cancellationToken: cancellationToken);

    internal ValueTask<JsonElement> ExecuteScriptAsync(
        string sessionId,
        string tabId,
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId, elementId: null, preferPageContextOnNull: preferPageContextOnNull, forcePageContextExecution: forcePageContextExecution, cancellationToken: cancellationToken);

    internal ValueTask<JsonElement> ExecuteScriptAsync(
        string sessionId,
        string tabId,
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        string? elementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => SendJsonPayloadCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.ExecuteScript,
            CreateExecuteScriptPayload(script, shadowHostElementId, frameHostElementId, elementId, preferPageContextOnNull, forcePageContextExecution),
            cancellationToken);

    private static JsonObject CreateExecuteScriptPayload(string script, string? shadowHostElementId, string? frameHostElementId, string? elementId, bool preferPageContextOnNull, bool forcePageContextExecution)
    {
        var payload = new JsonObject
        {
            ["script"] = script,
        };

        if (!string.IsNullOrWhiteSpace(shadowHostElementId))
            payload["shadowHostElementId"] = shadowHostElementId;

        if (!string.IsNullOrWhiteSpace(frameHostElementId))
            payload["frameHostElementId"] = frameHostElementId;

        if (!string.IsNullOrWhiteSpace(elementId))
            payload["elementId"] = elementId;

        if (preferPageContextOnNull)
            payload["preferPageContextOnNull"] = true;

        if (forcePageContextExecution)
            payload["forcePageContextExecution"] = true;

        return payload;
    }

    internal ValueTask<JsonElement> ExecuteScriptInFramesAsync(
        string sessionId,
        string tabId,
        string script,
        bool isolatedWorld,
        bool includeMetadata,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["script"] = script,
        };

        if (isolatedWorld)
            payload["world"] = "ISOLATED";

        if (includeMetadata)
            payload["includeMetadata"] = true;

        return SendJsonPayloadCommandAsync(sessionId, tabId, BridgeCommand.ExecuteScriptInFrames, payload, cancellationToken);
    }

    internal ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => SendOpenedSurfaceCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.OpenTab,
            new JsonObject
            {
                ["windowId"] = windowId,
            },
            cancellationToken);

    internal ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(string sessionId, string tabId, Point? position, CancellationToken cancellationToken = default)
        => SendOpenedSurfaceCommandAsync(sessionId, tabId, BridgeCommand.OpenWindow, CreateOpenWindowPayload(position), cancellationToken);

    internal ValueTask SetCookieAsync(
        string sessionId,
        string tabId,
        string contextId,
        string name,
        string value,
        string? domain,
        string? path,
        bool secure,
        bool httpOnly,
        long? expires,
        CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.SetCookie,
            new JsonObject
            {
                ["contextId"] = contextId,
                ["name"] = name,
                ["value"] = value,
                ["domain"] = domain,
                ["path"] = path,
                ["secure"] = secure,
                ["httpOnly"] = httpOnly,
                ["expires"] = expires,
            },
            cancellationToken);

    internal ValueTask<JsonElement> GetCookiesAsync(string sessionId, string tabId, string contextId, CancellationToken cancellationToken = default)
        => SendJsonPayloadCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.GetCookies,
            new JsonObject
            {
                ["contextId"] = contextId,
            },
            cancellationToken);

    internal ValueTask DeleteCookiesAsync(string sessionId, string tabId, string contextId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.DeleteCookies,
            new JsonObject
            {
                ["contextId"] = contextId,
            },
            cancellationToken);

    internal ValueTask SetRequestInterceptionAsync(string sessionId, string tabId, bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.InterceptRequest,
            CreateRequestInterceptionPayload(enabled, urlPatterns),
            cancellationToken);

    internal ValueTask SetTabContextAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(sessionId, tabId, BridgeCommand.SetTabContext, payload, cancellationToken);

    internal ValueTask CloseWindowAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.CloseWindow,
            new JsonObject
            {
                ["windowId"] = windowId,
            },
            cancellationToken);

    internal ValueTask ActivateWindowAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.ActivateWindow,
            new JsonObject
            {
                ["windowId"] = windowId,
            },
            cancellationToken);

    internal ValueTask ActivateTabAsync(string sessionId, string tabId, string targetTabId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.ActivateTab,
            new JsonObject
            {
                ["tabId"] = targetTabId,
            },
            cancellationToken);

    internal ValueTask<Rectangle> GetWindowBoundsAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendRectangleCommandAsync(sessionId, tabId, BridgeCommand.GetWindowBounds, cancellationToken);

    internal ValueTask<string?> FindElementAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => SendOptionalStringCommandAsync(sessionId, tabId, BridgeCommand.FindElement, payload, cancellationToken, BridgeStatus.NotFound);

    internal ValueTask<string[]> FindElementsAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => SendStringArrayCommandAsync(sessionId, tabId, BridgeCommand.FindElements, payload, cancellationToken);

    internal ValueTask<string?> WaitForElementAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => SendOptionalStringCommandAsync(sessionId, tabId, BridgeCommand.WaitForElement, payload, cancellationToken, BridgeStatus.NotFound, BridgeStatus.Timeout);

    internal ValueTask<string?> GetElementPropertyAsync(string sessionId, string tabId, string elementId, string propertyName, CancellationToken cancellationToken = default)
        => SendNullableStringCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.GetElementProperty,
            new JsonObject
            {
                ["elementId"] = elementId,
                ["propertyName"] = propertyName,
            },
            cancellationToken,
            BridgeStatus.NotFound);

    internal ValueTask<bool> CheckShadowRootAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => SendShadowRootPresenceCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.CheckShadowRoot,
            new JsonObject
            {
                ["elementId"] = elementId,
            },
            cancellationToken);

    internal ValueTask<PointF> ResolveElementScreenPointAsync(string sessionId, string tabId, string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => SendPointCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.ResolveElementScreenPoint,
            new JsonObject
            {
                ["elementId"] = elementId,
                ["scrollIntoView"] = scrollIntoView,
            },
            cancellationToken);

    internal ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => SendDebugPortStatusCommandAsync(sessionId, tabId, BridgeCommand.DebugPortStatus, cancellationToken);

    internal ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => SendElementDescriptionCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.DescribeElement,
            new JsonObject
            {
                ["elementId"] = elementId,
            },
            cancellationToken);

    internal ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => SendOptionalElementDescriptionCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.DescribeElement,
            new JsonObject
            {
                ["elementId"] = elementId,
            },
            cancellationToken);

    internal ValueTask FocusElementAsync(string sessionId, string tabId, string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.FocusElement,
            new JsonObject
            {
                ["elementId"] = elementId,
                ["scrollIntoView"] = scrollIntoView,
            },
            cancellationToken);

    internal ValueTask ScrollElementIntoViewAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => SendStatusOnlyCommandAsync(
            sessionId,
            tabId,
            BridgeCommand.ScrollElementIntoView,
            new JsonObject
            {
                ["elementId"] = elementId,
            },
            cancellationToken);

    internal async ValueTask<BridgeMessage> SendRequestAsync(string sessionId, BridgeMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateOutboundRequest(request);

        if (!sessionSockets.TryGetValue(sessionId, out var socket) || socket.State is not WebSocketState.Open)
            throw new InvalidOperationException($"Сеанс '{sessionId}' не подключён");

        var tabId = request.TabId!;
        var completionSource = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var addResult = await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor(
            MessageId: request.Id,
            SessionId: sessionId,
            TabId: tabId,
            Command: request.Command!.Value,
            CompletionSource: completionSource)).ConfigureAwait(false);
        if (addResult.Outcome is not PendingRequestAddResultKind.Added)
            throw CreatePendingRequestRegistrationException(sessionId, tabId, request.Id, addResult.Outcome);

        var commandName = request.Command?.ToString() ?? "<none>";
        settings.Logger?.LogBridgeServerRequestSent(request.Id, sessionId, tabId, commandName);

        try
        {
            await SendBridgeMessageAsync(socket, request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            settings.Logger?.LogBridgeServerRequestFailed(request.Id, sessionId, tabId, BridgeProtocolErrorCodes.SendFailed);
            _ = await state.TryFailPendingRequestAsync(request.Id, BridgeStatus.Disconnected, BridgeProtocolErrorCodes.SendFailed).ConfigureAwait(false);
            throw;
        }

        try
        {
            var response = await completionSource.Task.WaitAsync(settings.RequestTimeout, cancellationToken).ConfigureAwait(false);
            settings.Logger?.LogBridgeServerRequestCompleted(request.Id, sessionId, tabId, commandName, DescribeStatus(response.Status), response.Error ?? string.Empty);
            return response;
        }
        catch (TimeoutException)
        {
            settings.Logger?.LogBridgeServerRequestFailed(request.Id, sessionId, tabId, BridgeProtocolErrorCodes.RequestTimeout);
            _ = await state.TryFailPendingRequestAsync(request.Id, BridgeStatus.Timeout, BridgeProtocolErrorCodes.RequestTimeout).ConfigureAwait(false);
            return await completionSource.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            settings.Logger?.LogBridgeServerRequestFailed(request.Id, sessionId, tabId, BridgeProtocolErrorCodes.RequestCanceled);
            _ = await state.TryFailPendingRequestAsync(request.Id, BridgeStatus.Error, BridgeProtocolErrorCodes.RequestCanceled).ConfigureAwait(false);
            throw;
        }
    }

    private static InvalidOperationException CreatePendingRequestRegistrationException(
        string sessionId,
        string tabId,
        string requestId,
        PendingRequestAddResultKind outcome)
        => outcome switch
        {
            PendingRequestAddResultKind.SessionNotFound => new($"Сеанс '{sessionId}' не подключён"),
            PendingRequestAddResultKind.TabNotFound => new($"Вкладка '{tabId}' не зарегистрирована для сеанса '{sessionId}'"),
            PendingRequestAddResultKind.DuplicateMessageId => new($"Ожидающий запрос '{requestId}' уже зарегистрирован для сеанса '{sessionId}'"),
            _ => new($"Не удалось зарегистрировать ожидающий запрос '{requestId}' для сеанса '{sessionId}' (outcome={outcome})"),
        };

    private async ValueTask<string> SendStringCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        string? propertyName,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(tabId, command),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement payload || !TryParseStringPayload(payload, propertyName, out var value))
            throw new InvalidOperationException("Мостовая команда вернула неверные строковые данные");

        return value;
    }

    private async ValueTask<JsonElement> SendJsonPayloadCommandAsync(string sessionId, string tabId, BridgeCommand command, JsonObject? payload, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                payload is null ? null : JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
        {
            var errorSuffix = string.IsNullOrWhiteSpace(response.Error)
                ? string.Empty
                : $": {response.Error}";
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'{errorSuffix}");
        }

        return response.Payload ?? throw new InvalidOperationException("Мостовая команда не вернула обязательный payload");
    }

    private async ValueTask<string?> SendOptionalStringCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        JsonObject payload,
        CancellationToken cancellationToken,
        params BridgeStatus[] emptyStatuses)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is BridgeStatus.Ok)
        {
            if (response.Payload is not JsonElement stringPayload || !TryParseStringPayload(stringPayload, propertyName: null, out var value))
                throw new InvalidOperationException("Мостовая команда вернула неверные строковые данные");

            return value;
        }

        if (IsAcceptedEmptyStatus(response.Status, emptyStatuses))
            return null;

        var errorSuffix = string.IsNullOrWhiteSpace(response.Error)
            ? string.Empty
            : $": {response.Error}";
        throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'{errorSuffix}");
    }

    private async ValueTask<string?> SendNullableStringCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        JsonObject payload,
        CancellationToken cancellationToken,
        params BridgeStatus[] emptyStatuses)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is BridgeStatus.Ok)
        {
            if (IsNullPayload(response.Payload))
                return null;

            if (response.Payload is not JsonElement stringPayload || !TryParseStringPayload(stringPayload, propertyName: null, out var value))
                throw new InvalidOperationException("Мостовая команда вернула неверные строковые данные");

            return value;
        }

        if (IsAcceptedEmptyStatus(response.Status, emptyStatuses))
            return null;

        var errorSuffix = string.IsNullOrWhiteSpace(response.Error)
            ? string.Empty
            : $": {response.Error}";
        throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'{errorSuffix}");
    }

    private async ValueTask<string[]> SendStringArrayCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is BridgeStatus.NotFound)
            return [];

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement arrayPayload || !TryParseStringArrayPayload(arrayPayload, out var values))
            throw new InvalidOperationException("Мостовая команда вернула неверный список строк");

        return values;
    }

    private async ValueTask<bool> SendShadowRootPresenceCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is BridgeStatus.NotFound)
            return false;

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement shadowRootPayload || !TryParseStringPayload(shadowRootPayload, propertyName: null, out var shadowRootState))
            throw new InvalidOperationException("Мостовая команда вернула неверное состояние теневого корня");

        return string.Equals(shadowRootState, "open", StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask<(string TabId, string? WindowId)> SendOpenedSurfaceCommandAsync(
        string sessionId,
        string tabId,
        BridgeCommand command,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                payload is null ? null : JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement openedPayload || !TryParseOpenedSurfacePayload(openedPayload, out var openedTabId, out var openedWindowId))
            throw new InvalidOperationException("Мостовая команда открытия вернула неверные данные вкладки");

        return (openedTabId, openedWindowId);
    }

    private async ValueTask SendStatusOnlyCommandAsync(string sessionId, string tabId, BridgeCommand command, JsonObject? payload, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                payload is null ? null : JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
        {
            var error = string.IsNullOrWhiteSpace(response.Error) ? null : $": {response.Error}";
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'{error}");
        }
    }

    private async ValueTask<Rectangle> SendRectangleCommandAsync(string sessionId, string tabId, BridgeCommand command, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(tabId, command),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement payload || !TryParseRectanglePayload(payload, out var rectangle))
            throw new InvalidOperationException("Мостовая команда вернула неверные данные прямоугольника");

        return rectangle;
    }

    private async ValueTask<PointF> SendPointCommandAsync(string sessionId, string tabId, BridgeCommand command, JsonObject payload, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement pointPayload || !TryParsePointPayload(pointPayload, out var point))
            throw new InvalidOperationException("Мостовая команда вернула неверные данные точки");

        return point;
    }

    private async ValueTask<BridgeDebugPortStatusPayload> SendDebugPortStatusCommandAsync(string sessionId, string tabId, BridgeCommand command, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(tabId, command),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is not BridgeStatus.Ok)
        {
            var error = string.IsNullOrWhiteSpace(response.Error) ? null : $": {response.Error}";
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'{error}");
        }

        if (response.Payload is not JsonElement payload || !TryParseDebugPortStatusPayload(payload, out var debugPortStatus))
            throw new InvalidOperationException("Мостовая команда вернула неверные данные состояния отладочного порта");

        return debugPortStatus;
    }

    private async ValueTask<BridgeElementDescriptionPayload> SendElementDescriptionCommandAsync(string sessionId, string tabId, BridgeCommand command, JsonObject payload, CancellationToken cancellationToken)
    {
        var description = await SendOptionalElementDescriptionCommandAsync(sessionId, tabId, command, payload, cancellationToken).ConfigureAwait(false);
        if (description is not null)
            return description;

        throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(BridgeStatus.NotFound)}'");
    }

    private async ValueTask<BridgeElementDescriptionPayload?> SendOptionalElementDescriptionCommandAsync(string sessionId, string tabId, BridgeCommand command, JsonObject payload, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            sessionId,
            CreateCommandRequest(
                tabId,
                command,
                JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject)),
            cancellationToken).ConfigureAwait(false);

        if (response.Status is BridgeStatus.NotFound)
            return null;

        if (response.Status is not BridgeStatus.Ok)
            throw new InvalidOperationException($"Мостовая команда завершилась со статусом '{DescribeStatus(response.Status)}'");

        if (response.Payload is not JsonElement descriptionPayload || !TryParseElementDescriptionPayload(descriptionPayload, out var description))
            throw new InvalidOperationException("Мостовая команда вернула неверные данные описания элемента");

        return description;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                settings.Logger?.LogBridgeServerAcceptLoopStopped(settings.Host, Port);
                break;
            }
            catch (HttpListenerException exception)
            {
                settings.Logger?.LogBridgeServerAcceptFailed(exception, settings.Host, Port);
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        settings.Logger?.LogBridgeServerConnectionHandlingStarted(context.Request.HttpMethod, path);

        if (await TryHandleUtilityHttpRouteAsync(context, path).ConfigureAwait(false))
            return;

        if (await TryHandleManagedExtensionRouteAsync(context, path).ConfigureAwait(false))
            return;

        if (context.Request.IsWebSocketRequest)
        {
            await HandleWebSocketConnectionAsync(context).ConfigureAwait(false);
            return;
        }

        settings.Logger?.LogBridgeServerHttpRequestUnsupported(context.Request.HttpMethod, path);
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.Close();
    }

    private async Task<bool> TryHandleUtilityHttpRouteAsync(HttpListenerContext context, string path)
    {
        if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/debug/health", StringComparison.OrdinalIgnoreCase))
        {
            settings.Logger?.LogBridgeServerHealthRequested(path);
            await WriteHealthResponseAsync(context.Response).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteDiscoveryResponseAsync(context.Response).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/blank", StringComparison.OrdinalIgnoreCase))
        {
            await WriteBlankResponseAsync(context.Response).ConfigureAwait(false);
            return true;
        }

        if (path.StartsWith("/fulfill/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleFulfillRequestAsync(context, path).ConfigureAwait(false);
            return true;
        }

        if (await TryHandleUtilityPostRouteAsync(context, path).ConfigureAwait(false))
            return true;

        return false;
    }

    private async Task<bool> TryHandleUtilityPostRouteAsync(HttpListenerContext context, string path)
    {
        if (IsUtilityPostRoutePath(path)
            && string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUtilityResponseAsync(context.Response, CreateUtilityPreflightResponse()).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/debug-event", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDebugEventAsync(context).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCallbackRequestAsync(context).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/intercept", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleInterceptRequestAsync(context).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/intercept-response", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleInterceptResponseAsync(context).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "/observed-request-headers", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleObservedRequestHeadersAsync(context).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static bool IsUtilityPostRoutePath(string path)
        => string.Equals(path, "/debug-event", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/intercept", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/intercept-response", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/observed-request-headers", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryHandleManagedExtensionRouteAsync(HttpListenerContext context, string path)
    {
        if (!TryMatchManagedExtensionRoute(path, out var extensionId, out var routeKind))
            return false;

        var delivery = managedExtensionDelivery;
        if (delivery is null || !string.Equals(delivery.ExtensionId, extensionId, StringComparison.Ordinal))
            return false;

        if (string.Equals(routeKind, "manifest", StringComparison.OrdinalIgnoreCase))
        {
            await WriteManagedManifestResponseAsync(context.Response, delivery).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(routeKind, "package", StringComparison.OrdinalIgnoreCase))
        {
            await WriteManagedPackageResponseAsync(context.Response, delivery).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async ValueTask<BridgeNavigationProxyDirectResponse?> HandleNavigationProxyDirectRequestAsync(BridgeNavigationProxyDirectRequest request, CancellationToken cancellationToken)
    {
        if (!IsUtilityPostRoutePath(request.Path))
            return null;

        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            return CreateUtilityPreflightResponse();

        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            return CreateUtilityResponse(HttpStatusCode.BadRequest);

        if (!TryAuthorizeUtilitySecret(request.Secret, request.Path))
            return CreateUtilityResponse(HttpStatusCode.Forbidden);

        return request.Path switch
        {
            "/debug-event" => await ProcessDebugEventAsync(request.BodyText).ConfigureAwait(false),
            "/callback" => await ProcessCallbackRequestAsync(request.BodyText).ConfigureAwait(false),
            "/intercept" => await ProcessInterceptRequestAsync(request.BodyText).ConfigureAwait(false),
            "/intercept-response" => await ProcessInterceptResponseAsync(request.BodyText).ConfigureAwait(false),
            "/observed-request-headers" => await ProcessObservedRequestHeadersAsync(request.BodyText).ConfigureAwait(false),
            _ => null,
        };
    }

    private async Task HandleDebugEventAsync(HttpListenerContext context)
    {
        if (!TryAuthorizeUtilityPost(context))
            return;

        var response = await ProcessDebugEventAsync(await ReadRequestBodyAsync(context.Request).ConfigureAwait(false)).ConfigureAwait(false);
        await WriteUtilityResponseAsync(context.Response, response).ConfigureAwait(false);
    }

    private async Task HandleInterceptRequestAsync(HttpListenerContext context)
    {
        if (!TryAuthorizeUtilityPost(context))
            return;

        var response = await ProcessInterceptRequestAsync(await ReadRequestBodyAsync(context.Request).ConfigureAwait(false)).ConfigureAwait(false);
        await WriteUtilityResponseAsync(context.Response, response).ConfigureAwait(false);
    }

    private async Task HandleCallbackRequestAsync(HttpListenerContext context)
    {
        if (!TryAuthorizeUtilityPost(context))
            return;

        var response = await ProcessCallbackRequestAsync(await ReadRequestBodyAsync(context.Request).ConfigureAwait(false)).ConfigureAwait(false);
        await WriteUtilityResponseAsync(context.Response, response).ConfigureAwait(false);
    }

    private async Task HandleInterceptResponseAsync(HttpListenerContext context)
    {
        if (!TryAuthorizeUtilityPost(context))
            return;

        var response = await ProcessInterceptResponseAsync(await ReadRequestBodyAsync(context.Request).ConfigureAwait(false)).ConfigureAwait(false);
        await WriteUtilityResponseAsync(context.Response, response).ConfigureAwait(false);
    }

    private async Task HandleObservedRequestHeadersAsync(HttpListenerContext context)
    {
        if (!TryAuthorizeUtilityPost(context))
            return;

        var response = await ProcessObservedRequestHeadersAsync(await ReadRequestBodyAsync(context.Request).ConfigureAwait(false)).ConfigureAwait(false);
        await WriteUtilityResponseAsync(context.Response, response).ConfigureAwait(false);
    }

    private async Task<BridgeNavigationProxyDirectResponse> ProcessDebugEventAsync(string payload)
    {
        try
        {
            extensionDebugEvents.Enqueue(payload);
            TrimExtensionDebugEvents();

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var kind = root.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind is JsonValueKind.String
                ? kindElement.GetString()
                : null;
            var sessionId = root.TryGetProperty("sessionId", out var sessionIdElement) && sessionIdElement.ValueKind is JsonValueKind.String
                ? sessionIdElement.GetString()
                : null;
            var details = root.TryGetProperty("details", out var detailsElement)
                ? detailsElement.GetRawText()
                : "{}";

            settings.Logger?.LogBridgeServerDebugEventReceived(
                kind ?? "unknown",
                sessionId ?? string.Empty,
                details);

            return CreateUtilityResponse(HttpStatusCode.NoContent);
        }
        catch (JsonException ex)
        {
            settings.Logger?.LogBridgeServerDebugEventPayloadInvalid(ex);
            return CreateUtilityResponse(HttpStatusCode.BadRequest);
        }
    }

    private async Task<BridgeNavigationProxyDirectResponse> ProcessInterceptRequestAsync(string payloadText)
    {
        if (!TryParseJsonObject(payloadText, out var payload)
            || !TryReadInterceptedRequestPayload(payload, out var request))
        {
            return CreateUtilityResponse(HttpStatusCode.BadRequest);
        }

        var response = await InvokeRequestInterceptionAsync(request, CancellationToken.None).ConfigureAwait(false);
        return CreateJsonUtilityResponse(response);
    }

    private async Task<BridgeNavigationProxyDirectResponse> ProcessCallbackRequestAsync(string payloadText)
    {
        if (!TryReadCallbackRequestPayload(payloadText, out var request))
        {
            return CreateUtilityResponse(HttpStatusCode.BadRequest);
        }

        settings.Logger?.LogBridgeServerCallbackRequestReceived(
            request.RequestId,
            request.TabId,
            request.Name,
            request.Args.Length);
        RecordSyntheticDebugEvent("callback-request-received", new JsonObject
        {
            ["RequestId"] = request.RequestId,
            ["TabId"] = request.TabId,
            ["Name"] = request.Name,
            ["ArgumentCount"] = request.Args.Length,
        });

        using var timeout = settings.RequestTimeout > TimeSpan.Zero
            ? new CancellationTokenSource(settings.RequestTimeout)
            : new CancellationTokenSource();

        var response = await InvokeCallbackAsync(request, timeout.Token).ConfigureAwait(false);
        settings.Logger?.LogBridgeServerCallbackRequestCompleted(
            request.RequestId,
            request.TabId,
            request.Name,
            response.Action);
        RecordSyntheticDebugEvent("callback-request-completed", new JsonObject
        {
            ["RequestId"] = request.RequestId,
            ["TabId"] = request.TabId,
            ["Name"] = request.Name,
            ["Action"] = response.Action,
            ["HasArgs"] = response.Args is { Length: > 0 },
            ["HasCode"] = !string.IsNullOrWhiteSpace(response.Code),
        });

        return CreateJsonUtilityResponse(response);
    }

    private async Task<BridgeNavigationProxyDirectResponse> ProcessInterceptResponseAsync(string payloadText)
    {
        if (!TryParseJsonObject(payloadText, out var payload)
            || !TryReadInterceptedResponsePayload(payload, out var responsePayload))
        {
            return CreateUtilityResponse(HttpStatusCode.BadRequest);
        }

        var response = await InvokeResponseInterceptionAsync(responsePayload, CancellationToken.None).ConfigureAwait(false);
        return CreateJsonUtilityResponse(response);
    }

    private async Task<BridgeNavigationProxyDirectResponse> ProcessObservedRequestHeadersAsync(string payloadText)
    {
        if (!TryParseJsonObject(payloadText, out var payload)
            || !TryReadObservedRequestHeadersPayload(payload, out var observed))
        {
            return CreateUtilityResponse(HttpStatusCode.BadRequest);
        }

        if (RequestHeadersObserved is { } handlers)
        {
            foreach (var entry in handlers.GetInvocationList())
            {
                await ((BridgeObservedRequestHeadersHandler)entry)(observed, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return CreateUtilityResponse(HttpStatusCode.NoContent);
    }

    private async Task HandleFulfillRequestAsync(HttpListenerContext context, string path)
    {
        var response = context.Response;
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "GET,HEAD,OPTIONS");

        var requestId = path["/fulfill/".Length..];
        if (!pendingFulfillments.TryGetValue(requestId, out var fulfillment))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();
            return;
        }

        response.StatusCode = fulfillment.StatusCode;
        if (!string.IsNullOrWhiteSpace(fulfillment.ReasonPhrase))
            response.StatusDescription = fulfillment.ReasonPhrase;

        var exposedHeaders = fulfillment.Headers.Keys
            .Where(static key => !string.IsNullOrWhiteSpace(key)
                && !string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exposedHeaders.Length > 0)
            response.AddHeader("Access-Control-Expose-Headers", string.Join(',', exposedHeaders));

        foreach (var (key, value) in fulfillment.Headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                response.ContentType = value;
                continue;
            }

            if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            response.AddHeader(key, value);
        }

        var bodyBytes = fulfillment.Body;
        if (bodyBytes is { Length: > 0 })
            response.ContentLength64 = bodyBytes.Length;

        if (!string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            pendingFulfillments.TryRemove(requestId, out _);

            if (bodyBytes is { Length: > 0 })
                await response.OutputStream.WriteAsync(bodyBytes).ConfigureAwait(false);
        }

        response.Close();
    }

    private void TrimExtensionDebugEvents()
    {
        while (extensionDebugEvents.Count > MaxExtensionDebugEvents)
        {
            if (!extensionDebugEvents.TryDequeue(out _))
                break;
        }
    }

    private async Task WriteHealthResponseAsync(HttpListenerResponse response)
    {
        var payload = await CreateHealthPayloadAsync().ConfigureAwait(false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BridgeJsonContext.Default.BridgeServerHealthPayload);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private async ValueTask<BridgeServerHealthPayload> CreateHealthPayloadAsync()
    {
        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);
        var managedDeliveryDiagnostics = managedDeliveryServer.TrustDiagnostics;
        var secureTransportEnabled = SecureTransportPort > 0;
        var navigationProxyEnabled = NavigationProxyPort > 0;

        return new(
            Status: "ok",
            Server: "bridge-server-skeleton",
            Host: settings.Host,
            Port: Port,
            ManagedDelivery: new(
                Port: ManagedDeliveryPort,
                RequiresCertificateBypass: managedDeliveryDiagnostics.RequiresCertificateBypass,
                Status: managedDeliveryDiagnostics.Status,
                Method: managedDeliveryDiagnostics.Method,
                Detail: managedDeliveryDiagnostics.Detail),
            SecureTransport: new(
                Port: SecureTransportPort,
                Status: secureTransportEnabled ? "enabled" : "disabled",
                Scheme: secureTransportEnabled ? "wss" : "ws"),
            NavigationProxy: new(
                Port: NavigationProxyPort,
                Status: navigationProxyEnabled ? "enabled" : "disabled",
                Scheme: "http"),
            Connections: snapshot.SessionCount,
            Sessions: snapshot.SessionCount,
            Tabs: snapshot.TabCount,
            PendingRequests: snapshot.PendingRequestCount,
            CompletedRequests: snapshot.CompletedRequestCount,
            FailedRequests: snapshot.FailedRequestCount);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        var encoding = request.ContentEncoding ?? Encoding.UTF8;
        using var reader = new StreamReader(request.InputStream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private bool TryAuthorizeUtilitySecret(string? secret, string path)
    {
        if (string.Equals(secret, settings.Secret, StringComparison.Ordinal))
            return true;

        RecordSyntheticDebugEvent("utility-post-rejected", new JsonObject
        {
            ["Path"] = path,
            ["HasSecret"] = !string.IsNullOrEmpty(secret),
            ["SecretLength"] = secret?.Length ?? 0,
        });

        return false;
    }

    private bool TryAuthorizeUtilityPost(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;
        var secret = context.Request.QueryString["secret"];
        if (TryAuthorizeUtilitySecret(secret, path))
            return true;

        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.Close();
        return false;
    }

    private async ValueTask<BridgeInterceptHttpResponse> InvokeRequestInterceptionAsync(BridgeInterceptedRequestPayload request, CancellationToken cancellationToken)
    {
        var response = BridgeInterceptHttpResponse.Continue();
        if (RequestInterceptionRequested is not { } handlers)
            return response;

        foreach (var entry in handlers.GetInvocationList())
        {
            response = await ((BridgeRequestInterceptionHandler)entry)(request, cancellationToken).ConfigureAwait(false);
        }

        return RegisterRequestFulfillment(request.RequestId, response);
    }

    private async ValueTask<BridgeCallbackHttpResponse> InvokeCallbackAsync(BridgeCallbackRequestPayload request, CancellationToken cancellationToken)
    {
        var response = BridgeCallbackHttpResponse.Continue();
        if (CallbackRequested is not { } handlers)
            return response;

        foreach (var entry in handlers.GetInvocationList())
        {
            response = await ((BridgeCallbackHandler)entry)(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async ValueTask<BridgeInterceptHttpResponse> InvokeResponseInterceptionAsync(BridgeInterceptedResponsePayload responsePayload, CancellationToken cancellationToken)
    {
        var response = BridgeInterceptHttpResponse.Continue();
        if (ResponseInterceptionRequested is not { } handlers)
            return response;

        foreach (var entry in handlers.GetInvocationList())
        {
            response = await ((BridgeResponseInterceptionHandler)entry)(responsePayload, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static bool TryParseJsonObject(string payload, [NotNullWhen(true)] out JsonObject? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            value = JsonNode.Parse(payload) as JsonObject;
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadCallbackRequestPayload(string payload, [NotNullWhen(true)] out BridgeCallbackRequestPayload? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var requestId = root.TryGetProperty("requestId", out var requestIdElement) && requestIdElement.ValueKind == JsonValueKind.String
                ? requestIdElement.GetString()
                : null;
            var tabId = root.TryGetProperty("tabId", out var tabIdElement) && tabIdElement.ValueKind == JsonValueKind.String
                ? tabIdElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(tabId))
                return false;

            var message = new BridgeMessage
            {
                Id = requestId,
                Type = BridgeMessageType.Event,
                TabId = tabId,
                Event = BridgeEvent.Callback,
                Payload = root.Clone(),
            };

            if (!BridgeEventPayloadReader.TryReadCallback(message, out var callbackArgs))
                return false;

            request = new BridgeCallbackRequestPayload
            {
                RequestId = requestId,
                TabId = tabId,
                Name = callbackArgs.Name,
                Args = callbackArgs.Args,
                Code = callbackArgs.Code,
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadInterceptedRequestPayload(JsonObject payload, [NotNullWhen(true)] out BridgeInterceptedRequestPayload? request)
    {
        request = null;

        var requestId = ReadRequiredString(payload, "requestId");
        var tabId = ReadRequiredString(payload, "tabId");
        var url = ReadRequiredString(payload, "url");
        if (requestId is null || tabId is null || url is null)
            return false;

        request = new BridgeInterceptedRequestPayload
        {
            RequestId = requestId,
            TabId = tabId,
            Url = url,
            Method = ReadOptionalString(payload, "method") ?? HttpMethod.Get.Method,
            ResourceType = ReadOptionalString(payload, "type") ?? "other",
            Headers = ReadStringDictionary(payload, "headers"),
            RequestBodyBase64 = ReadOptionalString(payload, "requestBodyBase64"),
            FormData = ReadStringArrayDictionary(payload, "formData"),
            SupportsNavigationFulfillment = ReadOptionalBoolean(payload, "supportsNavigationFulfillment"),
            Timestamp = ReadTimestamp(payload, "timestamp"),
        };

        return true;
    }

    private static bool TryReadInterceptedResponsePayload(JsonObject payload, [NotNullWhen(true)] out BridgeInterceptedResponsePayload? response)
    {
        response = null;

        var requestId = ReadRequiredString(payload, "requestId");
        var tabId = ReadRequiredString(payload, "tabId");
        var url = ReadRequiredString(payload, "url");
        if (requestId is null || tabId is null || url is null)
            return false;

        response = new BridgeInterceptedResponsePayload
        {
            RequestId = requestId,
            TabId = tabId,
            Url = url,
            Method = ReadOptionalString(payload, "method") ?? HttpMethod.Get.Method,
            ResourceType = ReadOptionalString(payload, "type") ?? "other",
            StatusCode = ReadOptionalInt(payload, "statusCode") ?? (int)HttpStatusCode.OK,
            ReasonPhrase = ReadOptionalString(payload, "reasonPhrase"),
            Headers = ReadStringDictionary(payload, "headers"),
            Timestamp = ReadTimestamp(payload, "timestamp"),
        };

        return true;
    }

    private static bool TryReadObservedRequestHeadersPayload(JsonObject payload, [NotNullWhen(true)] out ObservedRequestHeadersEventArgs? observed)
    {
        observed = null;

        var requestId = ReadRequiredString(payload, "requestId");
        var tabId = ReadRequiredString(payload, "tabId");
        var url = ReadRequiredString(payload, "url");
        if (requestId is null || tabId is null || url is null || !Uri.TryCreate(url, UriKind.Absolute, out var observedUrl))
            return false;

        observed = new ObservedRequestHeadersEventArgs
        {
            RequestId = requestId,
            TabId = tabId,
            Url = observedUrl,
            Method = ReadOptionalString(payload, "method") ?? HttpMethod.Get.Method,
            ResourceType = ReadOptionalString(payload, "type") ?? "other",
            Headers = ReadStringDictionary(payload, "headers") ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Timestamp = ReadTimestamp(payload, "timestamp"),
        };

        return true;
    }

    private static string? ReadRequiredString(JsonObject payload, string propertyName)
    {
        var value = ReadOptionalString(payload, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadOptionalString(JsonObject payload, string propertyName)
        => payload[propertyName]?.GetValue<string>();

    private static int? ReadOptionalInt(JsonObject payload, string propertyName)
        => payload[propertyName]?.GetValue<int>();

    private static bool ReadOptionalBoolean(JsonObject payload, string propertyName)
        => payload[propertyName] is JsonValue value && value.TryGetValue<bool>(out var booleanValue) && booleanValue;

    private static DateTimeOffset ReadTimestamp(JsonObject payload, string propertyName)
    {
        var unixTime = payload[propertyName]?.GetValue<long?>();
        return unixTime is { } value
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.UtcNow;
    }

    private static Dictionary<string, string>? ReadStringDictionary(JsonObject payload, string propertyName)
    {
        if (payload[propertyName] is not JsonObject dictionaryObject)
            return null;

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionaryObject)
        {
            if (entry.Value is JsonValue value && value.TryGetValue<string>(out var stringValue))
            {
                result[entry.Key] = stringValue;
            }
        }

        return result;
    }

    private static Dictionary<string, string[]>? ReadStringArrayDictionary(JsonObject payload, string propertyName)
    {
        if (payload[propertyName] is not JsonObject dictionaryObject)
            return null;

        Dictionary<string, string[]> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionaryObject)
        {
            if (entry.Value is not JsonArray values)
                continue;

            result[entry.Key] = values
                .OfType<JsonValue>()
                .Select(static value => value.TryGetValue<string>(out var item) ? item : null)
                .OfType<string>()
                .ToArray();
        }

        return result;
    }

    private static async Task WriteUtilityResponseAsync(HttpListenerResponse response, BridgeNavigationProxyDirectResponse payload)
    {
        response.StatusCode = payload.StatusCode;
        response.ContentEncoding = Encoding.UTF8;

        if (payload.Headers is not null)
        {
            foreach (var (key, value) in payload.Headers)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    response.ContentType = value;
                    continue;
                }

                response.AddHeader(key, value);
            }
        }

        if (payload.Body is { Length: > 0 } body)
        {
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        }

        response.Close();
    }

    private static BridgeNavigationProxyDirectResponse CreateJsonUtilityResponse(BridgeInterceptHttpResponse payload)
    {
        JsonObject body = new()
        {
            ["action"] = payload.Action,
        };

        if (!string.IsNullOrWhiteSpace(payload.Url))
            body["url"] = payload.Url;

        if (payload.Headers is { Count: > 0 })
            body["headers"] = CreateJsonObject(payload.Headers);

        if (payload.ResponseHeaders is { Count: > 0 })
            body["responseHeaders"] = CreateJsonObject(payload.ResponseHeaders);

        if (payload.StatusCode is { } statusCode)
            body["statusCode"] = statusCode;

        if (!string.IsNullOrWhiteSpace(payload.ReasonPhrase))
            body["reasonPhrase"] = payload.ReasonPhrase;

        if (!string.IsNullOrWhiteSpace(payload.BodyBase64))
            body["bodyBase64"] = payload.BodyBase64;

        return CreateUtilityResponse(
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes(body.ToJsonString()),
            "application/json; charset=utf-8");
    }

    private static BridgeNavigationProxyDirectResponse CreateJsonUtilityResponse(BridgeCallbackHttpResponse payload)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("action", payload.Action);

            if (payload.Args is not null)
            {
                writer.WritePropertyName("args");
                WriteJsonArray(writer, payload.Args);
            }

            if (payload.Code is not null)
                writer.WriteString("code", payload.Code);

            writer.WriteEndObject();
            writer.Flush();
        }

        return CreateUtilityResponse(
            HttpStatusCode.OK,
            buffer.WrittenMemory.ToArray(),
            "application/json; charset=utf-8");
    }

    private static BridgeNavigationProxyDirectResponse CreateUtilityPreflightResponse()
        => CreateUtilityResponse(HttpStatusCode.NoContent);

    private static BridgeNavigationProxyDirectResponse CreateUtilityResponse(HttpStatusCode statusCode, byte[]? body = null, string? contentType = null)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Methods"] = "POST,OPTIONS",
            ["Access-Control-Allow-Headers"] = "Content-Type",
        };

        if (!string.IsNullOrWhiteSpace(contentType))
            headers["Content-Type"] = contentType;

        return new(
            StatusCode: (int)statusCode,
            ReasonPhrase: null,
            Headers: headers,
            Body: body);
    }

    private static JsonObject CreateJsonObject(IReadOnlyDictionary<string, string> values)
    {
        JsonObject result = new();
        foreach (var entry in values)
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }

    private static void WriteJsonArray(Utf8JsonWriter writer, IEnumerable<object?> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            WriteJsonValue(writer, value);
        }

        writer.WriteEndArray();
    }

    private static void WriteJsonObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> values)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in values)
        {
            writer.WritePropertyName(key);
            WriteJsonValue(writer, value);
        }

        writer.WriteEndObject();
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (TryWritePrimitiveJsonValue(writer, value))
            return;

        switch (value)
        {
            case JsonNode node:
                node.WriteTo(writer);
                return;
            case JsonElement element:
                element.WriteTo(writer);
                return;
            case IDictionary<string, object?> dictionary:
                WriteJsonObject(writer, dictionary);
                return;
            case IEnumerable<KeyValuePair<string, object?>> dictionaryEntries:
                WriteJsonObject(writer, dictionaryEntries);
                return;
            case IEnumerable<object?> array:
                WriteJsonArray(writer, array);
                return;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType());
                return;
        }
    }

    private static bool TryWritePrimitiveJsonValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return true;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                return true;
            case byte number:
                writer.WriteNumberValue(number);
                return true;
            case sbyte number:
                writer.WriteNumberValue(number);
                return true;
            case short number:
                writer.WriteNumberValue(number);
                return true;
            case ushort number:
                writer.WriteNumberValue(number);
                return true;
            case int number:
                writer.WriteNumberValue(number);
                return true;
            case uint number:
                writer.WriteNumberValue(number);
                return true;
            case long number:
                writer.WriteNumberValue(number);
                return true;
            case ulong number:
                writer.WriteNumberValue(number);
                return true;
            case float number:
                writer.WriteNumberValue(number);
                return true;
            case double number:
                writer.WriteNumberValue(number);
                return true;
            case decimal number:
                writer.WriteNumberValue(number);
                return true;
            default:
                return false;
        }
    }

    private static async Task WriteBlankResponseAsync(HttpListenerResponse response)
    {
        var payload = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title></title></head><body></body></html>");

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
        response.Close();
    }

    private static async Task WriteManagedManifestResponseAsync(HttpListenerResponse response, BridgeManagedExtensionDelivery delivery)
    {
        var payload = BuildManagedManifestPayload(delivery);

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/xml; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
        response.Close();
    }

    private static async Task WriteManagedPackageResponseAsync(HttpListenerResponse response, BridgeManagedExtensionDelivery delivery)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/x-chrome-extension";
        response.ContentLength64 = delivery.PackageBytes.Length;
        await response.OutputStream.WriteAsync(delivery.PackageBytes).ConfigureAwait(false);
        response.Close();
    }

    private async Task WriteDiscoveryResponseAsync(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Content-Security-Policy", "default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;");

        var port = Port.ToString(CultureInfo.InvariantCulture);
        var proxyPortMeta = NavigationProxyPort > 0
            ? string.Concat("<meta name=\"atom-bridge-proxy-port\" content=\"", NavigationProxyPort.ToString(CultureInfo.InvariantCulture), "\">")
            : string.Empty;
        var payload = Encoding.UTF8.GetBytes(
            $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta name="atom-bridge-port" content="{{port}}">
            {{proxyPortMeta}}
            <meta name="atom-bridge-secret" content="{{settings.Secret}}">
            <title>Atom Bridge Discovery</title>
            <script>
            new MutationObserver(function(muts){
                for(var i=0;i<muts.length;i++){
                    var nodes=muts[i].addedNodes;
                    for(var j=0;j<nodes.length;j++){
                        var n=nodes[j];
                        if(n.nodeType===1&&n.id&&n.id.indexOf('__atom_r_')===0&&n.dataset.code!==undefined){
                            try{var r;try{r=(0,eval)('('+n.dataset.code+')');}catch(ex){if(ex instanceof SyntaxError)try{r=(0,eval)(n.dataset.code);}catch(ex2){if(ex2 instanceof SyntaxError)r=new Function(n.dataset.code)();else throw ex2;}else throw ex;}n.dataset.s='ok';n.dataset.v=r!=null?String(r):'';}
                            catch(e){n.dataset.s='err';n.dataset.v=e.message;}
                        }
                    }
                }
            }).observe(document.documentElement,{childList:true,subtree:true});
            </script>
            </head>
            <body></body>
            </html>
            """);

        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
        response.Close();
    }

    private static byte[] BuildManagedManifestPayload(BridgeManagedExtensionDelivery delivery)
    {
        var payload = string.Concat(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n",
            "<gupdate xmlns=\"http://www.google.com/update2/response\" protocol=\"2.0\">\n",
            "  <app appid=\"", WebUtility.HtmlEncode(delivery.ExtensionId), "\">\n",
            "    <updatecheck codebase=\"", WebUtility.HtmlEncode(delivery.PackageUrl), "\" version=\"", WebUtility.HtmlEncode(delivery.ExtensionVersion), "\" />\n",
            "  </app>\n",
            "</gupdate>\n");

        return Encoding.UTF8.GetBytes(payload);
    }

    private static bool TryMatchManagedExtensionRoute(string path, [NotNullWhen(true)] out string? extensionId, [NotNullWhen(true)] out string? routeKind)
    {
        extensionId = null;
        routeKind = null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length is not 3 || !string.Equals(segments[0], "chromium", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(segments[1]) || string.IsNullOrWhiteSpace(segments[2]))
            return false;

        extensionId = segments[1];
        routeKind = segments[2];
        return true;
    }

    private static JsonObject CreateRequestInterceptionPayload(bool enabled, IEnumerable<string>? urlPatterns)
    {
        var payload = new JsonObject
        {
            ["enabled"] = enabled,
        };

        if (urlPatterns is null)
            return payload;

        var patterns = new JsonArray();
        foreach (var pattern in urlPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                patterns.Add(JsonValue.Create(pattern));
        }

        payload["patterns"] = patterns;
        return payload;
    }

    private async Task HandleWebSocketConnectionAsync(HttpListenerContext context)
    {
        var socket = await AcceptWebSocketAsync(context).ConfigureAwait(false);
        if (socket is null)
            return;

        await HandleAcceptedWebSocketConnectionAsync(socket, cts.Token).ConfigureAwait(false);
    }

    private async Task HandleAcceptedWebSocketConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {

        string? sessionId = null;

        try
        {
            sessionId = await RunHandshakeAsync(socket, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            sessionSockets[sessionId] = socket;
            settings.Logger?.LogBridgeServerSessionConnected(sessionId);
            await RunConnectedSessionAsync(socket, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Server shutdown interrupted the connection loop.
        }
        catch (WebSocketException exception)
        {
            settings.Logger?.LogBridgeServerWebSocketDisconnected(DescribeSessionForLogging(sessionId), exception);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                sessionSockets.TryRemove(sessionId, out _);
                _ = await state.RemoveSessionAsync(sessionId).ConfigureAwait(false);
                await RefreshConnectionCountAsync().ConfigureAwait(false);
                settings.Logger?.LogBridgeServerSessionDisconnected(sessionId);
            }

            socket.Dispose();
        }
    }

    private static async Task<WebSocket?> AcceptWebSocketAsync(HttpListenerContext context)
    {
        try
        {
            var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            return webSocketContext.WebSocket;
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    private async Task<string?> RunHandshakeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var validation = await ReceiveAndValidateHandshakeAsync(socket, cancellationToken).ConfigureAwait(false);
        if (validation.IsRejected)
        {
            settings.Logger?.LogBridgeServerHandshakeRejected(validation.CorrelationId ?? "без-идентификатора", validation.RejectCode ?? BridgeProtocolErrorCodes.InvalidPayload);
            await SendHandshakeRejectAsync(socket, validation).ConfigureAwait(false);
            return null;
        }

        var registrationFailure = await TryRegisterSessionAsync(validation).ConfigureAwait(false);
        if (registrationFailure is not null)
        {
            settings.Logger?.LogBridgeServerHandshakeRejected(registrationFailure.CorrelationId ?? "без-идентификатора", registrationFailure.RejectCode ?? BridgeProtocolErrorCodes.InvalidPayload);
            await SendHandshakeRejectAsync(socket, registrationFailure).ConfigureAwait(false);
            return null;
        }

        await RefreshConnectionCountAsync().ConfigureAwait(false);
        await SendHandshakeAcceptAsync(socket, validation).ConfigureAwait(false);
        settings.Logger?.LogBridgeServerHandshakeAccepted(validation.ClientPayload!.SessionId);
        return validation.ClientPayload!.SessionId;
    }

    private async Task<BridgeHandshakeValidationResult> ReceiveAndValidateHandshakeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var firstMessage = await ReceiveBridgeMessageAsync(socket, cancellationToken).ConfigureAwait(false);
        return BridgeHandshakeValidator.Validate(firstMessage, settings);
    }

    private async Task<BridgeHandshakeValidationResult?> TryRegisterSessionAsync(BridgeHandshakeValidationResult validation)
    {
        var payload = validation.ClientPayload!;
        var createResult = await state.CreateSessionAsync(new BridgeSessionDescriptor(
            SessionId: payload.SessionId,
            ProtocolVersion: payload.ProtocolVersion,
            BrowserFamily: payload.BrowserFamily,
            ExtensionVersion: payload.ExtensionVersion,
            BrowserVersion: payload.BrowserVersion)).ConfigureAwait(false);

        if (createResult.Outcome is SessionCreateResultKind.DuplicateSessionId)
            return CreateRejectFromValidation(validation, payload, BridgeProtocolErrorCodes.DuplicateSessionId);

        if (createResult.Outcome is not SessionCreateResultKind.Created)
            return CreateRejectFromValidation(validation, payload, BridgeProtocolErrorCodes.InvalidPayload);

        return null;
    }

    private static BridgeHandshakeValidationResult CreateRejectFromValidation(
        BridgeHandshakeValidationResult validation,
        BridgeHandshakeClientPayload payload,
        string rejectCode)
        => new(
            Outcome: BridgeHandshakeValidationOutcome.Rejected,
            CorrelationId: validation.CorrelationId,
            ClientPayload: payload,
            AcceptPayload: null,
            RejectCode: rejectCode,
            RejectPayload: null);

    private async Task RefreshConnectionCountAsync()
    {
        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);
        ConnectionCount = snapshot.SessionCount;
    }

    private static async Task<BridgeMessage?> ReceiveBridgeMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var totalBytes = 0;

        while (socket.State is WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(totalBytes), cancellationToken).ConfigureAwait(false);
            if (result.MessageType is WebSocketMessageType.Close)
                return null;

            if (result.MessageType is not WebSocketMessageType.Text)
                return null;

            totalBytes += result.Count;
            if (result.EndOfMessage)
            {
                return JsonSerializer.Deserialize(
                    buffer.AsSpan(0, totalBytes),
                    BridgeJsonContext.Default.BridgeMessage);
            }
        }

        return null;
    }

    private static async Task SendHandshakeAcceptAsync(WebSocket socket, BridgeHandshakeValidationResult validation)
    {
        var message = BridgeHandshakeMessageFactory.CreateAcceptMessage(validation);
        await SendBridgeMessageAsync(socket, message, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task SendHandshakeRejectAsync(WebSocket socket, BridgeHandshakeValidationResult validation)
    {
        var message = BridgeHandshakeMessageFactory.CreateRejectMessage(validation);
        if (message is null)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, validation.RejectCode ?? BridgeProtocolErrorCodes.InvalidPayload, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await SendBridgeMessageAsync(socket, message, CancellationToken.None).ConfigureAwait(false);
        await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, validation.RejectCode, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task SendBridgeMessageAsync(WebSocket socket, BridgeMessage message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJsonContext.Default.BridgeMessage);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunConnectedSessionAsync(WebSocket socket, string sessionId, CancellationToken cancellationToken)
    {
        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var shouldContinue = await HandleConnectedFrameAsync(socket, sessionId, cancellationToken).ConfigureAwait(false);
            if (!shouldContinue)
                break;
        }
    }

    private async Task<bool> HandleConnectedFrameAsync(WebSocket socket, string sessionId, CancellationToken cancellationToken)
    {
        var receiveResult = await ReceiveConnectedMessageAsync(socket, cancellationToken).ConfigureAwait(false);
        if (receiveResult.Outcome is ConnectedMessageReadOutcome.Close)
        {
            settings.Logger?.LogBridgeServerSessionDisconnected(sessionId);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, BridgeProtocolErrorCodes.Closing, CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        if (receiveResult.Outcome is not ConnectedMessageReadOutcome.Message)
        {
            settings.Logger?.LogBridgeServerProtocolViolation(sessionId, BridgeProtocolErrorCodes.InvalidPayload);
            await socket.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, BridgeProtocolErrorCodes.InvalidPayload, CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        var message = TryDeserializeConnectedMessage(receiveResult.Buffer, receiveResult.Count);
        if (message is null)
        {
            settings.Logger?.LogBridgeServerProtocolViolation(sessionId, BridgeProtocolErrorCodes.InvalidPayload);
            await socket.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, BridgeProtocolErrorCodes.InvalidPayload, CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        return await HandlePostHandshakeMessageAsync(socket, sessionId, message).ConfigureAwait(false);
    }

    private static BridgeMessage? TryDeserializeConnectedMessage(byte[] buffer, int count)
    {
        try
        {
            return JsonSerializer.Deserialize(buffer.AsSpan(0, count), BridgeJsonContext.Default.BridgeMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> HandlePostHandshakeMessageAsync(WebSocket socket, string sessionId, BridgeMessage message)
    {
        if (message.Type is BridgeMessageType.Handshake)
        {
            var validation = new BridgeHandshakeValidationResult(
                Outcome: BridgeHandshakeValidationOutcome.Rejected,
                CorrelationId: string.IsNullOrWhiteSpace(message.Id) ? null : message.Id,
                ClientPayload: null,
                AcceptPayload: null,
                RejectCode: BridgeProtocolErrorCodes.InvalidPayload,
                RejectPayload: null);
            settings.Logger?.LogBridgeServerHandshakeRejected(validation.CorrelationId ?? "без-идентификатора", validation.RejectCode ?? BridgeProtocolErrorCodes.InvalidPayload);
            await SendHandshakeRejectAsync(socket, validation).ConfigureAwait(false);
            return false;
        }

        if (message.Type is BridgeMessageType.Response)
        {
            var responseHandled = await HandlePostHandshakeResponseAsync(message).ConfigureAwait(false);
            if (responseHandled)
                return true;

            settings.Logger?.LogBridgeServerProtocolViolation(sessionId, BridgeProtocolErrorCodes.InvalidPayload);
            await socket.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, BridgeProtocolErrorCodes.InvalidPayload, CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        if (message.Type is not BridgeMessageType.Event)
            return true;

        var handled = await TryHandlePostHandshakeEventAsync(sessionId, message).ConfigureAwait(false);
        if (handled)
            return true;

        settings.Logger?.LogBridgeServerProtocolViolation(sessionId, BridgeProtocolErrorCodes.InvalidPayload);
        await socket.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, BridgeProtocolErrorCodes.InvalidPayload, CancellationToken.None).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandlePostHandshakeResponseAsync(BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Id)
            || message.Status is null
            || string.IsNullOrWhiteSpace(message.TabId))
        {
            settings.Logger?.LogBridgeServerResponseRejected(message.Id ?? "без-идентификатора", "в ответе отсутствуют обязательные поля");
            return false;
        }

        var pendingRequest = await state.CreatePendingRequestSnapshotAsync(message.Id).ConfigureAwait(false);
        if (pendingRequest is not null)
        {
            if (!string.Equals(pendingRequest.TabId, message.TabId, StringComparison.Ordinal))
            {
                settings.Logger?.LogBridgeServerResponseRejected(message.Id, "вкладка ответа не совпадает с ожидаемой");
                return false;
            }

            if (!IsValidResponsePayloadForCommand(pendingRequest.Command, message))
            {
                settings.Logger?.LogBridgeServerResponseRejected(
                    message.Id,
                    $"данные ответа не соответствуют ожидаемой форме; command={pendingRequest.Command}; status={DescribeStatus(message.Status)}; payload={(message.Payload is JsonElement payload ? payload.GetRawText() : "<null>")}");
                return false;
            }
        }

        var result = await state.TryCompletePendingRequestAsync(message.Id, message).ConfigureAwait(false);
        if (result.Outcome is PendingRequestCompletionResultKind.RequestNotFound)
            settings.Logger?.LogBridgeServerResponseRejected(message.Id, "ответ пришёл для неизвестного запроса");

        return result.Outcome is PendingRequestCompletionResultKind.Completed or PendingRequestCompletionResultKind.AlreadyCompleted;
    }

    private static async Task<ConnectedMessageReadResult> ReceiveConnectedMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var totalBytes = 0;

        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            if (totalBytes == buffer.Length)
            {
                if (buffer.Length >= MaxConnectedMessageBytes)
                    return new(ConnectedMessageReadOutcome.InvalidPayload, buffer, totalBytes);

                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxConnectedMessageBytes));
            }

            var result = await socket.ReceiveAsync(buffer.AsMemory(totalBytes), cancellationToken).ConfigureAwait(false);
            if (result.MessageType is WebSocketMessageType.Close)
                return new(ConnectedMessageReadOutcome.Close, buffer, totalBytes);

            if (result.MessageType is not WebSocketMessageType.Text)
                return new(ConnectedMessageReadOutcome.InvalidPayload, buffer, totalBytes);

            totalBytes += result.Count;
            if (result.EndOfMessage)
                return new(ConnectedMessageReadOutcome.Message, buffer, totalBytes);
        }

        return new(ConnectedMessageReadOutcome.Close, buffer, totalBytes);
    }

    private static bool IsValidResponsePayloadForCommand(BridgeCommand command, BridgeMessage message)
        => message.Status is BridgeStatus.Ok
            ? IsValidOkResponsePayloadForCommand(command, message)
            : IsValidNonOkResponsePayloadForCommand(command, message);

    private static bool IsValidOkResponsePayloadForCommand(BridgeCommand command, BridgeMessage message)
    {
        if (TryValidateOkStringPayloadCommand(command, message, out var stringResult))
        {
            return stringResult;
        }

        if (TryValidateOkOpenedSurfacePayloadCommand(command, message, out var openedSurfaceResult))
        {
            return openedSurfaceResult;
        }

        return command switch
        {
            BridgeCommand.GetWindowBounds
                => message.Payload is JsonElement rectanglePayload && TryParseRectanglePayload(rectanglePayload, out _),
            BridgeCommand.FindElement
                => message.Payload is JsonElement findElementPayload && TryParseStringPayload(findElementPayload, propertyName: null, out _),
            BridgeCommand.FindElements
                => message.Payload is JsonElement findElementsPayload && TryParseStringArrayPayload(findElementsPayload, out _),
            BridgeCommand.GetElementProperty
                => IsNullPayload(message.Payload)
                    || (message.Payload is JsonElement elementPropertyPayload && TryParseStringPayload(elementPropertyPayload, propertyName: null, out _)),
            BridgeCommand.WaitForElement
                => message.Payload is JsonElement waitForElementPayload && TryParseStringPayload(waitForElementPayload, propertyName: null, out _),
            BridgeCommand.CheckShadowRoot
                => message.Payload is JsonElement checkShadowRootPayload && TryParseStringPayload(checkShadowRootPayload, propertyName: null, out _),
            BridgeCommand.ResolveElementScreenPoint
                => message.Payload is JsonElement pointPayload && TryParsePointPayload(pointPayload, out _),
            BridgeCommand.DebugPortStatus
                => message.Payload is JsonElement debugPortPayload && TryParseDebugPortStatusPayload(debugPortPayload, out _),
            BridgeCommand.DescribeElement
                => message.Payload is JsonElement elementDescriptionPayload && TryParseElementDescriptionPayload(elementDescriptionPayload, out _),
            _ => true,
        };
    }

    private static bool IsValidNonOkResponsePayloadForCommand(BridgeCommand command, BridgeMessage message)
        => command switch
        {
            BridgeCommand.FindElement when message.Status is BridgeStatus.NotFound
                => IsNullPayload(message.Payload),
            BridgeCommand.GetElementProperty when message.Status is BridgeStatus.NotFound
                => IsNullPayload(message.Payload),
            BridgeCommand.WaitForElement when message.Status is BridgeStatus.NotFound or BridgeStatus.Timeout
                => IsNullPayload(message.Payload),
            BridgeCommand.CheckShadowRoot when message.Status is BridgeStatus.NotFound
                => IsNullPayload(message.Payload),
            _ => true,
        };

    private static bool TryValidateOkStringPayloadCommand(BridgeCommand command, BridgeMessage message, out bool result)
    {
        result = command switch
        {
            BridgeCommand.GetTitle
                => message.Payload is JsonElement titlePayload && TryParseStringPayload(titlePayload, "title", out _),
            BridgeCommand.GetUrl
                => message.Payload is JsonElement urlPayload && TryParseStringPayload(urlPayload, "url", out _),
            BridgeCommand.GetContent
                => message.Payload is JsonElement contentPayload && TryParseStringPayload(contentPayload, "content", out _),
            _ => false,
        };

        return command is BridgeCommand.GetTitle or BridgeCommand.GetUrl or BridgeCommand.GetContent;
    }

    private enum ConnectedMessageReadOutcome
    {
        Message,
        Close,
        InvalidPayload,
    }

    private readonly record struct ConnectedMessageReadResult(ConnectedMessageReadOutcome Outcome, byte[] Buffer, int Count);

    private static bool TryValidateOkOpenedSurfacePayloadCommand(BridgeCommand command, BridgeMessage message, out bool result)
    {
        result = command switch
        {
            BridgeCommand.OpenTab
                => message.Payload is JsonElement openTabPayload && TryParseOpenedSurfacePayload(openTabPayload, out _, out _),
            BridgeCommand.OpenWindow
                => message.Payload is JsonElement openWindowPayload && TryParseOpenedSurfacePayload(openWindowPayload, out _, out _),
            _ => false,
        };

        return command is BridgeCommand.OpenTab or BridgeCommand.OpenWindow;
    }

    private static BridgeMessage CreateCommandRequest(string tabId, BridgeCommand command, JsonElement? payload = null)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Request,
            TabId = tabId,
            Command = command,
            Payload = payload,
        };

    private static bool TryParseStringPayload(JsonElement payload, string? propertyName, out string value)
    {
        value = string.Empty;

        if (payload.ValueKind is JsonValueKind.String)
        {
            value = payload.GetString() ?? string.Empty;
            return true;
        }

        if (payload.ValueKind is not JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName))
            return false;

        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryParseStringArrayPayload(JsonElement payload, out string[] values)
    {
        values = [];

        if (payload.ValueKind is not JsonValueKind.Array)
            return false;

        var parsedValues = new string[payload.GetArrayLength()];
        var index = 0;

        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
                return false;

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            parsedValues[index++] = value;
        }

        values = parsedValues;
        return true;
    }

    private static bool IsNullPayload(JsonElement? payload)
        => payload is null || payload.Value.ValueKind is JsonValueKind.Null;

    private static bool IsAcceptedEmptyStatus(BridgeStatus? status, BridgeStatus[] acceptedStatuses)
    {
        if (status is not BridgeStatus resolvedStatus)
            return false;

        foreach (var acceptedStatus in acceptedStatuses)
        {
            if (acceptedStatus == resolvedStatus)
                return true;
        }

        return false;
    }

    private static bool TryParseOpenedSurfacePayload(JsonElement payload, out string tabId, out string? windowId)
    {
        tabId = string.Empty;
        windowId = null;

        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("tabId", out var tabIdProperty) || tabIdProperty.ValueKind is not JsonValueKind.String)
            return false;

        tabId = tabIdProperty.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tabId))
            return false;

        if (!payload.TryGetProperty("windowId", out var windowIdProperty))
            return true;

        if (windowIdProperty.ValueKind is JsonValueKind.Null)
            return true;

        if (windowIdProperty.ValueKind is not JsonValueKind.String)
            return false;

        windowId = windowIdProperty.GetString();
        return true;
    }

    private static JsonObject? CreateOpenWindowPayload(Point? position)
    {
        if (position is null)
            return null;

        return new JsonObject
        {
            ["windowPosition"] = new JsonObject
            {
                ["x"] = position.Value.X,
                ["y"] = position.Value.Y,
            },
        };
    }

    private static bool TryParseRectanglePayload(JsonElement payload, out Rectangle rectangle)
    {
        rectangle = default;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("left", out var left)
            || !payload.TryGetProperty("top", out var top)
            || !payload.TryGetProperty("width", out var width)
            || !payload.TryGetProperty("height", out var height)
            || !left.TryGetInt32(out var leftValue)
            || !top.TryGetInt32(out var topValue)
            || !width.TryGetInt32(out var widthValue)
            || !height.TryGetInt32(out var heightValue))
        {
            return false;
        }

        rectangle = new Rectangle(leftValue, topValue, widthValue, heightValue);
        return true;
    }

    private static bool TryParsePointPayload(JsonElement payload, out PointF point)
    {
        point = default;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("viewportX", out var viewportX)
            || !payload.TryGetProperty("viewportY", out var viewportY)
            || !viewportX.TryGetSingle(out var x)
            || !viewportY.TryGetSingle(out var y))
        {
            return false;
        }

        point = new PointF(x, y);
        return true;
    }

    private static bool TryParseDebugPortStatusPayload(JsonElement payload, out BridgeDebugPortStatusPayload debugPortStatus)
    {
        debugPortStatus = default!;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("tabId", out var tabId)
            || !payload.TryGetProperty("hasPort", out var hasPort)
            || !payload.TryGetProperty("queueLength", out var queueLength)
            || !payload.TryGetProperty("hasSocket", out var hasSocket)
            || !payload.TryGetProperty("isReady", out var isReady)
            || !payload.TryGetProperty("interceptEnabled", out var interceptEnabled)
            || !payload.TryGetProperty("hasTabContext", out var hasTabContext)
            || !tabId.TryGetInt32(out var tabIdValue)
            || !queueLength.TryGetInt32(out var queueLengthValue)
            || !TryGetBoolean(hasPort, out var hasPortValue)
            || !TryGetBoolean(hasSocket, out var hasSocketValue)
            || !TryGetBoolean(isReady, out var isReadyValue)
            || !TryGetBoolean(hasTabContext, out var hasTabContextValue)
            || !TryGetBoolean(interceptEnabled, out var interceptEnabledValue))
        {
            return false;
        }

        if (!TryReadOptionalString(payload, "contextId", out var contextIdValue)
            || !TryReadOptionalString(payload, "contextUserAgent", out var contextUserAgentValue)
            || !TryReadOptionalBoolean(payload, "hasBrowserTab", out var hasBrowserTabValue)
            || !TryReadOptionalString(payload, "browserTabUrl", out var browserTabUrlValue)
            || !TryReadOptionalString(payload, "browserTabPendingUrl", out var browserTabPendingUrlValue)
            || !TryReadOptionalString(payload, "browserTabStatus", out var browserTabStatusValue)
            || !TryReadOptionalString(payload, "runtimeCheckStatus", out var runtimeCheckStatusValue)
            || !TryReadOptionalString(payload, "runtimeHref", out var runtimeHrefValue)
            || !TryReadOptionalString(payload, "runtimeReadyState", out var runtimeReadyStateValue)
            || !TryReadOptionalString(payload, "runtimeCheckError", out var runtimeCheckErrorValue))
        {
            return false;
        }

        debugPortStatus = new BridgeDebugPortStatusPayload(
            TabId: tabIdValue,
            HasPort: hasPortValue,
            QueueLength: queueLengthValue,
            HasSocket: hasSocketValue,
            IsReady: isReadyValue,
            InterceptEnabled: interceptEnabledValue,
            HasTabContext: hasTabContextValue,
            ContextId: contextIdValue,
            ContextUserAgent: contextUserAgentValue,
            HasBrowserTab: hasBrowserTabValue,
            BrowserTabUrl: browserTabUrlValue,
            BrowserTabPendingUrl: browserTabPendingUrlValue,
            BrowserTabStatus: browserTabStatusValue,
            RuntimeCheckStatus: runtimeCheckStatusValue,
            RuntimeHref: runtimeHrefValue,
            RuntimeReadyState: runtimeReadyStateValue,
            RuntimeCheckError: runtimeCheckErrorValue);
        return true;
    }

    private static bool TryParseElementDescriptionPayload(JsonElement payload, out BridgeElementDescriptionPayload description)
    {
        description = default!;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("tagName", out var tagName)
            || !payload.TryGetProperty("isVisible", out var isVisible)
            || !payload.TryGetProperty("boundingBox", out var boundingBox)
            || tagName.ValueKind is not JsonValueKind.String
            || !TryGetBoolean(isVisible, out var isVisibleValue)
            || !TryParseRectangleFPayload(boundingBox, out var rectangle))
        {
            return false;
        }

        var tagNameValue = tagName.GetString();
        if (string.IsNullOrWhiteSpace(tagNameValue))
            return false;

        if (!TryReadOptionalBoolean(payload, "checked", out var isCheckedValue)
            || !TryReadOptionalInt32(payload, "selectedIndex", defaultValue: -1, out var selectedIndexValue)
            || !TryReadOptionalBoolean(payload, "isActive", out var isActiveValue)
            || !TryReadOptionalBoolean(payload, "isConnected", defaultValue: true, out var isConnectedValue)
            || !TryReadOptionalString(payload, "associatedControlId", out var associatedControlId)
            || !TryParseOptionalStringMap(payload, "computedStyle", out var computedStyle)
            || !TryParseOptionalElementOptions(payload, out var options))
        {
            return false;
        }

        description = new BridgeElementDescriptionPayload(
            TagName: tagNameValue,
            Checked: isCheckedValue,
            SelectedIndex: selectedIndexValue,
            IsActive: isActiveValue,
            IsConnected: isConnectedValue,
            IsVisible: isVisibleValue,
            AssociatedControlId: associatedControlId,
            BoundingBox: rectangle,
            ComputedStyle: computedStyle,
            Options: options);
        return true;
    }

    private static bool TryParseElementOptionPayload(JsonElement payload, out BridgeElementOptionPayload option)
    {
        option = default!;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("value", out var value)
            || !payload.TryGetProperty("text", out var text)
            || value.ValueKind is not JsonValueKind.String
            || text.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        option = new BridgeElementOptionPayload(
            Value: value.GetString()!,
            Text: text.GetString()!);
        return true;
    }

    private static bool TryReadOptionalBoolean(JsonElement payload, string propertyName, out bool value)
    {
        value = false;
        return !payload.TryGetProperty(propertyName, out var property)
            || TryGetBoolean(property, out value);
    }

    private static bool TryReadOptionalBoolean(JsonElement payload, string propertyName, bool defaultValue, out bool value)
    {
        value = defaultValue;
        return !payload.TryGetProperty(propertyName, out var property)
            || TryGetBoolean(property, out value);
    }

    private static bool TryReadOptionalInt32(JsonElement payload, string propertyName, int defaultValue, out int value)
    {
        value = defaultValue;
        return !payload.TryGetProperty(propertyName, out var property)
            || property.TryGetInt32(out value);
    }

    private static bool TryReadOptionalString(JsonElement payload, string propertyName, out string? value)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out var property))
            return true;

        if (property.ValueKind is JsonValueKind.Null)
            return true;

        if (property.ValueKind is not JsonValueKind.String)
            return false;

        value = property.GetString();
        return true;
    }

    private static bool TryParseOptionalStringMap(JsonElement payload, string propertyName, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!payload.TryGetProperty(propertyName, out var property))
            return true;

        if (property.ValueKind is not JsonValueKind.Object)
            return false;

        foreach (var child in property.EnumerateObject())
        {
            if (child.Value.ValueKind is JsonValueKind.Null)
            {
                values[child.Name] = string.Empty;
                continue;
            }

            if (child.Value.ValueKind is not JsonValueKind.String)
                return false;

            values[child.Name] = child.Value.GetString()!;
        }

        return true;
    }

    private static bool TryParseOptionalElementOptions(JsonElement payload, out BridgeElementOptionPayload[] options)
    {
        options = [];
        if (!payload.TryGetProperty("options", out var property))
            return true;

        if (property.ValueKind is not JsonValueKind.Array)
            return false;

        var parsedOptions = new List<BridgeElementOptionPayload>();
        foreach (var optionElement in property.EnumerateArray())
        {
            if (!TryParseElementOptionPayload(optionElement, out var option))
                return false;

            parsedOptions.Add(option);
        }

        options = [.. parsedOptions];
        return true;
    }

    private static bool TryParseRectangleFPayload(JsonElement payload, out RectangleF rectangle)
    {
        rectangle = default;
        if (payload.ValueKind is not JsonValueKind.Object)
            return false;

        if (!payload.TryGetProperty("left", out var left)
            || !payload.TryGetProperty("top", out var top)
            || !payload.TryGetProperty("width", out var width)
            || !payload.TryGetProperty("height", out var height)
            || !left.TryGetSingle(out var leftValue)
            || !top.TryGetSingle(out var topValue)
            || !width.TryGetSingle(out var widthValue)
            || !height.TryGetSingle(out var heightValue))
        {
            return false;
        }

        rectangle = new RectangleF(leftValue, topValue, widthValue, heightValue);
        return true;
    }

    private static bool TryGetBoolean(JsonElement element, out bool value)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    private async Task<bool> TryHandlePostHandshakeEventAsync(string sessionId, BridgeMessage message)
    {
        if (message.Event is null)
            return false;

        return message.Event.Value switch
        {
            BridgeEvent.TabConnected => await HandleTabConnectedAsync(sessionId, message).ConfigureAwait(false),
            BridgeEvent.TabDisconnected => await HandleTabDisconnectedAsync(sessionId, message).ConfigureAwait(false),
            _ => await DispatchRuntimeEventAsync(sessionId, message).ConfigureAwait(false),
        };
    }

    private async ValueTask<bool> DispatchRuntimeEventAsync(string sessionId, BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.TabId))
            return false;

        var existingTab = await state.CreateTabSnapshotAsync(message.TabId).ConfigureAwait(false);
        if (existingTab is null)
        {
            var registration = await state.RegisterTabAsync(new BridgeTabChannelDescriptor(
                SessionId: sessionId,
                TabId: message.TabId,
                WindowId: message.WindowId)).ConfigureAwait(false);

            if (registration.Outcome is TabRegistrationResultKind.Registered or TabRegistrationResultKind.AlreadyOwnedBySession)
                settings.Logger?.LogBridgeServerTabRegistered(sessionId, message.TabId, message.WindowId ?? "без-окна");
        }

        RuntimeEventReceived?.Invoke(sessionId, message);
        return true;
    }

    private async Task<bool> HandleTabConnectedAsync(string sessionId, BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.TabId))
            return false;

        var result = await state.RegisterTabAsync(new BridgeTabChannelDescriptor(
            SessionId: sessionId,
            TabId: message.TabId,
            WindowId: message.WindowId)).ConfigureAwait(false);

        if (result.Outcome is TabRegistrationResultKind.Registered or TabRegistrationResultKind.AlreadyOwnedBySession)
            settings.Logger?.LogBridgeServerTabRegistered(sessionId, message.TabId, message.WindowId ?? "без-окна");

        return result.Outcome is TabRegistrationResultKind.Registered or TabRegistrationResultKind.AlreadyOwnedBySession;
    }

    private async Task<bool> HandleTabDisconnectedAsync(string sessionId, BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.TabId))
            return false;

        var result = await state.UnregisterTabAsync(sessionId, message.TabId).ConfigureAwait(false);
        if (result.Outcome is TabRemovalResultKind.Removed or TabRemovalResultKind.TabNotFound)
            settings.Logger?.LogBridgeServerTabRemoved(sessionId, message.TabId);

        return result.Outcome is TabRemovalResultKind.Removed or TabRemovalResultKind.TabNotFound;
    }

    private static void ValidateOutboundRequest(BridgeMessage request)
    {
        if (request.Type is not BridgeMessageType.Request)
            throw new ArgumentException("Исходящее сообщение моста должно быть запросом", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Id))
            throw new ArgumentException("Исходящий запрос моста должен содержать непустой идентификатор сообщения", nameof(request));

        if (string.IsNullOrWhiteSpace(request.TabId))
            throw new ArgumentException("Исходящий запрос моста должен указывать идентификатор вкладки", nameof(request));

        if (request.Command is null)
            throw new ArgumentException("Исходящий запрос моста должен содержать команду", nameof(request));
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        await cts.CancelAsync().ConfigureAwait(false);

        try
        {
            listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Listener already closed during concurrent teardown.
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (TimeoutException)
            {
                // Best-effort teardown; do not hang on accept loop.
            }
        }

        try
        {
            listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Listener already disposed.
        }

        if (secureTransportServer is not null)
            await secureTransportServer.DisposeAsync().ConfigureAwait(false);

        if (navigationProxyServer is not null)
            await navigationProxyServer.DisposeAsync().ConfigureAwait(false);

        await managedDeliveryServer.DisposeAsync().ConfigureAwait(false);
        cts.Dispose();
        pendingFulfillments.Clear();
        await state.DisposeAsync().ConfigureAwait(false);
        settings.Logger?.LogBridgeServerStopped(settings.Host, Port);
    }

    private BridgeInterceptHttpResponse RegisterRequestFulfillment(string requestId, BridgeInterceptHttpResponse response)
    {
        if (!string.Equals(response.Action, "fulfill", StringComparison.OrdinalIgnoreCase))
            return response;

        var effectiveRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        pendingFulfillments[effectiveRequestId] = new BridgePendingFulfillment
        {
            StatusCode = response.StatusCode ?? (int)HttpStatusCode.OK,
            ReasonPhrase = response.ReasonPhrase,
            Headers = response.ResponseHeaders is { Count: > 0 }
                ? new Dictionary<string, string>(response.ResponseHeaders, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Body = TryDecodeBase64(response.BodyBase64),
        };

        return BridgeInterceptHttpResponse.Fulfill(
            responseHeaders: response.ResponseHeaders,
            statusCode: response.StatusCode,
            reasonPhrase: response.ReasonPhrase,
            url: CreateFulfillmentUrl(effectiveRequestId));
    }

    private string CreateFulfillmentUrl(string requestId)
        => string.Concat("http://127.0.0.1:", Port.ToString(CultureInfo.InvariantCulture), "/fulfill/", requestId);

    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class BridgePendingFulfillment
    {
        public required int StatusCode { get; init; }

        public string? ReasonPhrase { get; init; }

        public required IReadOnlyDictionary<string, string> Headers { get; init; }

        public byte[]? Body { get; init; }
    }

    private void LogManagedDeliveryTrustState()
    {
        var logger = settings.Logger;
        if (logger is null)
            return;

        var diagnostics = ManagedDeliveryTrustDiagnostics;
        if (diagnostics.RequiresCertificateBypass)
        {
            logger.LogBridgeServerManagedDeliveryTrustBypassRequired(
                ManagedDeliveryPort,
                diagnostics.Method,
                diagnostics.Detail);
            return;
        }

        logger.LogBridgeServerManagedDeliveryTrustResolved(
            ManagedDeliveryPort,
            diagnostics.Method,
            diagnostics.Status,
            diagnostics.Detail);
    }

    private static int FindFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private void StartBridgeListener()
    {
        if (settings.Port != 0)
        {
            Port = settings.Port;
            ConfigureBridgePrefix(Port);
            listener.Start();
            return;
        }

        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            Port = FindFreePort();
            ConfigureBridgePrefix(Port);

            try
            {
                listener.Start();
                return;
            }
            catch (HttpListenerException) when (attempt < maxAttempts - 1)
            {
                listener.Abort();
            }
        }

        throw new HttpListenerException(unchecked((int)0x80004005), "Не удалось запустить bridge HttpListener на автоматически выделенном порту.");
    }

    private void ConfigureBridgePrefix(int port)
    {
        listener.Prefixes.Clear();
        listener.Prefixes.Add(string.Concat(
            "http://",
            settings.Host,
            ":",
            port.ToString(CultureInfo.InvariantCulture),
            "/"));
    }

    private static string DescribeStatus(BridgeStatus? status)
        => status switch
        {
            BridgeStatus.Ok => "успех",
            BridgeStatus.Error => "ошибка",
            BridgeStatus.Timeout => "истекло время ожидания",
            BridgeStatus.NotFound => "не найдено",
            BridgeStatus.Disconnected => "отключено",
            null => "не указан",
            _ => "неизвестно",
        };

    private static string DescribeSessionForLogging(string? sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? "без сеанса"
            : sessionId;
    }
}