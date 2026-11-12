using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Browsing.WebDriver.Protocol.Cdp;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Транспорт управления движком Obscura по протоколу отладки Chrome.
/// </summary>
/// <remarks>
/// Прокси, часовой пояс и геолокацию Obscura задаёт процессу целиком, поэтому транспорт держит по
/// процессу на каждое сочетание этих параметров и размещает окно или вкладку в подходящем. Окно
/// драйвера — логическое, а каждая вкладка получает собственный браузерный контекст: у драйвера
/// хранилище cookie своё у каждой страницы, как виртуальные cookie расширения.
/// </remarks>
internal sealed class CdpBrowserCommandTransport : IBrowserCommandTransport, ISurfaceProvisioningTransport
{
    private readonly Func<int, CdpServerKey, CancellationToken, ValueTask<CdpServerSlot>> serverFactory;
    private readonly CdpServerKey defaultKey;
    private readonly SemaphoreSlim serverGate = new(1, 1);
    private readonly Dictionary<CdpServerKey, CdpServerSlot> servers = [];
    private readonly ConcurrentDictionary<string, CdpTab> tabs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CdpTab> tabsBySession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CdpServerKey> windows = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetime = new();
    private int windowCounter;
    private int slotCounter = -1;
    private readonly string callbackBridgeSource;
    private int disposeState;

    public CdpBrowserCommandTransport(
        string sessionId,
        Func<int, CdpServerKey, CancellationToken, ValueTask<CdpServerSlot>> serverFactory,
        CdpServerKey defaultKey,
        string? identityRuntimeSource,
        ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(serverFactory);

        SessionId = sessionId;
        this.serverFactory = serverFactory;
        this.defaultKey = defaultKey;
        Logger = logger;
        IdentityRuntimeSource = identityRuntimeSource;
        RegistryKey = string.Concat("atom.webdriver.cdp.", Guid.NewGuid().ToString("N"));
        CallbackBindingName = string.Concat("__", Guid.NewGuid().ToString("N"));
        callbackBridgeSource = CdpPageScripts.CreateCallbackBridge(CallbackBindingName);
    }

    public event Action<string, BridgeMessage>? RuntimeEventReceived;

    public event BridgeCallbackHandler? CallbackRequested;

    public event BridgeRequestInterceptionHandler? RequestInterceptionRequested;

    // Перехват ответов Obscura не поддерживает (Fetch останавливает только запросы), поэтому событие никогда не поднимается.
    public event BridgeResponseInterceptionHandler? ResponseInterceptionRequested
    {
        add => _ = value;
        remove => _ = value;
    }

    /// <summary>Идентификатор сессии драйвера, под которым транспорт публикует события.</summary>
    public string SessionId { get; }

    /// <summary>Имя binding моста callback'ов (случайное для каждого транспорта).</summary>
    public string CallbackBindingName { get; }

    /// <summary>Ключ реестра элементов, общий для всех вкладок транспорта.</summary>
    internal string RegistryKey { get; }

    /// <summary>Текст раннего скрипта личности (<c lang="text">identity.main.js</c>), если он найден.</summary>
    internal string? IdentityRuntimeSource { get; }

    internal ILogger? Logger { get; }

    public ValueTask<BridgeTabChannelSnapshot?> TryGetRegisteredTabAsync(string sessionId, Func<BridgeTabChannelSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!string.Equals(sessionId, SessionId, StringComparison.Ordinal))
            return ValueTask.FromResult<BridgeTabChannelSnapshot?>(null);

        foreach (var tab in tabs.Values)
        {
            if (tab.IsClosed)
                continue;

            var snapshot = tab.CreateSnapshot(sessionId);
            if (predicate(snapshot))
                return ValueTask.FromResult<BridgeTabChannelSnapshot?>(snapshot);
        }

        return ValueTask.FromResult<BridgeTabChannelSnapshot?>(null);
    }

    public IPageCommandClient CreatePageCommands(WebPage page, string sessionId, string tabId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);

        if (!tabs.TryGetValue(tabId, out var tab))
            throw new InvalidOperationException($"Вкладка Obscura '{tabId}' не зарегистрирована в транспорте");

        tab.Page = page;
        return new CdpPageCommandClient(this, tab, page);
    }

    public async ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(SurfaceProvisioningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        var windowId = string.Create(CultureInfo.InvariantCulture, $"obscura-window-{Interlocked.Increment(ref windowCounter)}");
        var key = ResolveKey(request);
        windows[windowId] = key;

        try
        {
            var tab = await CreateTabAsync(windowId, key, cancellationToken).ConfigureAwait(false);
            return (tab.TabId, windowId);
        }
        catch
        {
            windows.TryRemove(windowId, out _);
            throw;
        }
    }

    public ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, SurfaceProvisioningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OpenTabCoreAsync(windowId, ResolveKey(request), cancellationToken);
    }

    /// <summary>Открывает вкладку в процессе окна по умолчанию (без собственных параметров процесса).</summary>
    internal ValueTask<(string TabId, string? WindowId)> OpenTabInWindowAsync(string windowId, CancellationToken cancellationToken)
    {
        if (!windows.TryGetValue(windowId, out var key))
            throw new BridgeCommandException($"Окно Obscura '{windowId}' не найдено", BridgeStatus.NotFound);

        return OpenTabCoreAsync(windowId, key, cancellationToken);
    }

    internal async ValueTask CloseTabAsync(CdpTab tab, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // Вкладку могли уже снять (движок закрыл цель) — тогда остаётся только проверить, не
        // опустел ли процесс.
        if (ForgetTab(tab))
        {
            await SendBestEffortAsync(tab.Server, "Target.closeTarget", new JsonObject { ["targetId"] = tab.TargetId }, cancellationToken).ConfigureAwait(false);
            await SendBestEffortAsync(tab.Server, "Target.disposeBrowserContext", new JsonObject { ["browserContextId"] = tab.ContextId }, cancellationToken).ConfigureAwait(false);
        }

        await ReleaseIdleServerAsync(tab.Server).ConfigureAwait(false);
    }

    /// <summary>
    /// Останавливает процесс без вкладок, если он не процесс по умолчанию: иначе окна с меняющимися
    /// прокси оставляли бы по процессу V8 на каждое значение до конца жизни браузера.
    /// </summary>
    private async ValueTask ReleaseIdleServerAsync(CdpServerSlot slot)
    {
        if (slot.TabCount > 0 || slot.Key == defaultKey || Volatile.Read(ref disposeState) != 0)
            return;

        try
        {
            await serverGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // Транспорт освобождается и сам остановит все процессы.
            return;
        }

        try
        {
            if (slot.TabCount > 0 || !servers.TryGetValue(slot.Key, out var current) || !ReferenceEquals(current, slot))
                return;

            servers.Remove(slot.Key);
        }
        finally
        {
            serverGate.Release();
        }

        await slot.DisposeAsync().ConfigureAwait(false);
    }

    internal async ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken)
    {
        windows.TryRemove(windowId, out _);

        foreach (var tab in tabs.Values.Where(tab => string.Equals(tab.WindowId, windowId, StringComparison.Ordinal)).ToArray())
            await CloseTabAsync(tab, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Команда освобождения, которой не мешает уже закрытая цель или остановленный процесс.</summary>
    private static async ValueTask SendBestEffortAsync(CdpServerSlot slot, string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        if (!slot.Connection.IsOpen)
            return;

        try
        {
            await slot.Connection.SendAsync(method, parameters, sessionId: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or ObjectDisposedException)
        {
            // Цель уже закрыта движком либо процесс остановлен — освобождать нечего.
        }
    }

    /// <summary>
    /// Включает приостановку запросов в сессии вкладки.
    /// </summary>
    /// <remarks>
    /// <c lang="text">Fetch.disable</c> у Obscura снимает перехват со всего соединения, то есть и с соседних
    /// вкладок процесса, поэтому однажды включённый перехват не выключается: ненужные вкладке
    /// запросы транспорт сам пропускает дальше.
    /// </remarks>
    internal async ValueTask EnsureFetchEnabledAsync(CdpTab tab, CancellationToken cancellationToken)
    {
        if (tab.IsFetchEnabled)
            return;

        await tab.SendAsync(
            "Fetch.enable",
            new JsonObject { ["patterns"] = new JsonArray(new JsonObject { ["urlPattern"] = "*" }) },
            cancellationToken).ConfigureAwait(false);
        tab.IsFetchEnabled = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        await lifetime.CancelAsync().ConfigureAwait(false);

        CdpServerSlot[] slots;
        await serverGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            slots = [.. servers.Values];
            servers.Clear();
        }
        finally
        {
            serverGate.Release();
        }

        foreach (var tab in tabs.Values)
            tab.MarkClosed();

        foreach (var slot in slots)
            await slot.DisposeAsync().ConfigureAwait(false);

        tabs.Clear();
        tabsBySession.Clear();
        windows.Clear();
        serverGate.Dispose();
        lifetime.Dispose();
    }

    internal CdpServerKey ResolveKey(SurfaceProvisioningRequest request)
    {
        var proxyUrl = request.Proxy is null ? null : ObscuraServer.ResolveProxyUrl(request.Proxy);
        var timeZone = string.IsNullOrWhiteSpace(request.Device?.Timezone) ? defaultKey.TimeZone : request.Device.Timezone;
        var geolocation = request.Device?.Geolocation is { } settings
            ? (settings.Latitude, settings.Longitude)
            : defaultKey.Geolocation;

        // Прокси не наследуется от ключа по умолчанию: отсутствие прокси у поверхности означает
        // «без прокси» — наследование от окна и браузера уже выполнил WebBrowser.
        return new CdpServerKey(proxyUrl, timeZone, geolocation);
    }

    private async ValueTask<(string TabId, string? WindowId)> OpenTabCoreAsync(string windowId, CdpServerKey key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        ThrowIfDisposed();

        if (!windows.ContainsKey(windowId))
            throw new BridgeCommandException($"Окно Obscura '{windowId}' не найдено", BridgeStatus.NotFound);

        var tab = await CreateTabAsync(windowId, key, cancellationToken).ConfigureAwait(false);
        return (tab.TabId, windowId);
    }

    private async ValueTask<CdpTab> CreateTabAsync(string windowId, CdpServerKey key, CancellationToken cancellationToken)
    {
        // Слот выдаётся уже с учтённой вкладкой (под serverGate), иначе параллельное закрытие
        // последней вкладки успело бы остановить процесс между выдачей и учётом.
        var slot = await GetOrStartServerAsync(key, cancellationToken).ConfigureAwait(false);

        CdpTab tab;
        try
        {
            tab = await CreateTargetAsync(slot, windowId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            slot.RemoveTab();
            await ReleaseIdleServerAsync(slot).ConfigureAwait(false);
            throw;
        }

        tabs[tab.TabId] = tab;
        tabsBySession[SessionKey(slot, tab.SessionId)] = tab;

        try
        {
            await tab.SendAsync("Page.enable", parameters: null, cancellationToken).ConfigureAwait(false);
            await tab.SendAsync("Runtime.enable", parameters: null, cancellationToken).ConfigureAwait(false);
            await tab.SendAsync("Runtime.addBinding", new JsonObject { ["name"] = CallbackBindingName }, cancellationToken).ConfigureAwait(false);
            await tab.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new JsonObject { ["source"] = callbackBridgeSource, ["runImmediately"] = true },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CloseTabAsync(tab, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return tab;
    }

    /// <summary>Контекст, цель и flat-сессия вкладки; при сбое уже созданное освобождается.</summary>
    private static async ValueTask<CdpTab> CreateTargetAsync(CdpServerSlot slot, string windowId, CancellationToken cancellationToken)
    {
        var connection = slot.Connection;
        string? contextId = null;
        string? targetId = null;

        try
        {
            var context = await connection.SendAsync("Target.createBrowserContext", parameters: null, sessionId: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            contextId = context.GetProperty("browserContextId").GetString()
                ?? throw new InvalidOperationException("Obscura не вернула идентификатор браузерного контекста");

            var created = await connection.SendAsync(
                "Target.createTarget",
                new JsonObject { ["url"] = "about:blank", ["browserContextId"] = contextId },
                sessionId: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            targetId = created.GetProperty("targetId").GetString()
                ?? throw new InvalidOperationException("Obscura не вернула идентификатор созданной вкладки");

            var attached = await connection.SendAsync(
                "Target.attachToTarget",
                new JsonObject { ["targetId"] = targetId, ["flatten"] = true },
                sessionId: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var cdpSessionId = attached.GetProperty("sessionId").GetString()
                ?? throw new InvalidOperationException("Obscura не вернула сессию созданной вкладки");

            var tabId = string.Create(CultureInfo.InvariantCulture, $"obscura-{slot.Index}:{targetId}");
            return new CdpTab(slot, tabId, windowId, targetId, cdpSessionId, contextId);
        }
        catch
        {
            if (targetId is not null)
                await SendBestEffortAsync(slot, "Target.closeTarget", new JsonObject { ["targetId"] = targetId }, CancellationToken.None).ConfigureAwait(false);

            if (contextId is not null)
                await SendBestEffortAsync(slot, "Target.disposeBrowserContext", new JsonObject { ["browserContextId"] = contextId }, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    private async ValueTask<CdpServerSlot> GetOrStartServerAsync(CdpServerKey key, CancellationToken cancellationToken)
    {
        await serverGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (servers.TryGetValue(key, out var existing) && existing.Connection.IsOpen)
            {
                existing.AddTab();
                return existing;
            }

            if (existing is not null)
            {
                servers.Remove(key);
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            // Индекс монотонный: он входит в идентификаторы вкладок, и вкладка нового процесса не
            // должна совпасть по идентификатору с вкладкой упавшего.
            var slot = await serverFactory(Interlocked.Increment(ref slotCounter), key, cancellationToken).ConfigureAwait(false);
            slot.Connection.EventReceived += cdpEvent => OnCdpEvent(slot, cdpEvent);
            slot.Connection.Closed += _ => OnServerClosed(slot);
            servers[key] = slot;
            slot.AddTab();
            return slot;
        }
        finally
        {
            serverGate.Release();
        }
    }

    private void OnServerClosed(CdpServerSlot slot)
    {
        foreach (var tab in tabs.Values.Where(tab => ReferenceEquals(tab.Server, slot)).ToArray())
            ForgetTab(tab);

        // Вызывается из цикла приёма соединения: освобождение (оно ждёт этот цикл) — только в фоне.
        _ = ReleaseDeadServerAsync(slot);
    }

    private async Task ReleaseDeadServerAsync(CdpServerSlot slot)
    {
        await Task.Yield();
        if (Volatile.Read(ref disposeState) != 0)
            return;

        try
        {
            await serverGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (!servers.TryGetValue(slot.Key, out var current) || !ReferenceEquals(current, slot))
                return;

            servers.Remove(slot.Key);
        }
        finally
        {
            serverGate.Release();
        }

        try
        {
            await slot.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Logger?.LogObscuraHandlerFailed(exception, "server-release", slot.Index.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Снимает вкладку с учёта; <see langword="false"/>, если её уже сняли.</summary>
    private bool ForgetTab(CdpTab tab)
    {
        tab.MarkClosed();

        // Удаляется только собственная запись: под тем же ключом может уже жить другая вкладка.
        if (!tabs.TryRemove(new KeyValuePair<string, CdpTab>(tab.TabId, tab)))
            return false;

        tabsBySession.TryRemove(new KeyValuePair<string, CdpTab>(SessionKey(tab.Server, tab.SessionId), tab));
        tab.Server.RemoveTab();
        return true;
    }

    private static string SessionKey(CdpServerSlot slot, string cdpSessionId)
        => string.Create(CultureInfo.InvariantCulture, $"{slot.Index}|{cdpSessionId}");

    private void OnCdpEvent(CdpServerSlot slot, CdpEvent cdpEvent)
    {
        if (Volatile.Read(ref disposeState) != 0)
            return;

        if (cdpEvent.Method is "Target.targetDestroyed" or "Target.detachedFromTarget")
        {
            HandleTargetGone(slot, cdpEvent);
            return;
        }

        if (cdpEvent.SessionId is not { } cdpSessionId
            || !tabsBySession.TryGetValue(SessionKey(slot, cdpSessionId), out var tab))
        {
            return;
        }

        switch (cdpEvent.Method)
        {
            case "Page.domContentEventFired":
                tab.EnqueueEventWork(() => PublishLifecycleAsync(tab, [BridgeEvent.DomContentLoaded]));
                break;

            case "Page.loadEventFired":
                tab.EnqueueEventWork(() => PublishLifecycleAsync(tab, [BridgeEvent.PageLoaded, BridgeEvent.NavigationCompleted]));
                break;

            case "Runtime.consoleAPICalled":
                var consolePayload = BuildConsolePayload(cdpEvent.Params);
                tab.EnqueueEventWork(() =>
                {
                    Publish(tab, BridgeEvent.ConsoleMessage, consolePayload);
                    return Task.CompletedTask;
                });
                break;

            case "Runtime.bindingCalled":
                HandleBindingCalled(tab, cdpEvent.Params);
                break;

            case "Fetch.requestPaused":
                _ = HandleRequestPausedAsync(tab, cdpEvent.Params);
                break;
        }
    }

    private void HandleTargetGone(CdpServerSlot slot, CdpEvent cdpEvent)
    {
        if (!cdpEvent.Params.TryGetProperty("targetId", out var targetElement) || targetElement.GetString() is not { } targetId)
            return;

        var released = false;
        foreach (var tab in tabs.Values.Where(tab => ReferenceEquals(tab.Server, slot) && string.Equals(tab.TargetId, targetId, StringComparison.Ordinal)).ToArray())
            released |= ForgetTab(tab);

        // Цель закрыл сам движок (window.close и т.п.): процесс с прокси не должен пережить вкладку.
        if (released)
            _ = ReleaseIdleServerInBackgroundAsync(slot);
    }

    private async Task ReleaseIdleServerInBackgroundAsync(CdpServerSlot slot)
    {
        await Task.Yield();
        try
        {
            await ReleaseIdleServerAsync(slot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Logger?.LogObscuraHandlerFailed(exception, "server-release", slot.Index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private async Task PublishLifecycleAsync(CdpTab tab, BridgeEvent[] events)
    {
        JsonObject payload;
        try
        {
            payload = await ReadDocumentSnapshotAsync(tab, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or JsonException or ObjectDisposedException)
        {
            // Документ сменился или вкладка закрылась раньше, чем мы его прочли: событие устарело.
            return;
        }

        foreach (var lifecycleEvent in events)
            Publish(tab, lifecycleEvent, payload);
    }

    internal static async ValueTask<JsonObject> ReadDocumentSnapshotAsync(CdpTab tab, CancellationToken cancellationToken)
    {
        var result = await tab.SendAsync(
            "Runtime.evaluate",
            new JsonObject { ["expression"] = CdpPageScripts.DocumentSnapshot, ["returnByValue"] = true },
            cancellationToken).ConfigureAwait(false);

        var text = result.GetProperty("result").GetProperty("value").GetString() ?? "{}";
        var snapshot = JsonNode.Parse(text)?.AsObject() ?? [];
        return new JsonObject
        {
            ["href"] = snapshot["href"]?.GetValue<string>(),
            ["title"] = snapshot["title"]?.GetValue<string>(),
            ["readyState"] = snapshot["readyState"]?.GetValue<string>(),
        };
    }

    private static JsonObject BuildConsolePayload(JsonElement parameters)
    {
        var level = parameters.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var args = new JsonArray();
        var message = new StringBuilder();

        if (parameters.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var argument in argsElement.EnumerateArray())
            {
                var text = DescribeConsoleArgument(argument);
                if (message.Length > 0)
                    message.Append(' ');

                message.Append(text);
                args.Add(argument.TryGetProperty("value", out var raw) ? JsonNode.Parse(raw.GetRawText()) : JsonValue.Create(text));
            }
        }

        return new JsonObject
        {
            ["level"] = MapConsoleLevel(level),
            ["message"] = message.ToString(),
            ["args"] = args,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static string? DescribeConsoleArgument(JsonElement argument)
    {
        if (argument.TryGetProperty("value", out var value))
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

        return argument.TryGetProperty("description", out var description) ? description.GetString() : null;
    }

    private static string MapConsoleLevel(string? level) => level switch
    {
        "warning" => "warn",
        "error" or "assert" => "error",
        "info" => "info",
        "debug" => "debug",
        _ => "log",
    };

    private void HandleBindingCalled(CdpTab tab, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("name", out var nameElement)
            || !string.Equals(nameElement.GetString(), CallbackBindingName, StringComparison.Ordinal)
            || !parameters.TryGetProperty("payload", out var payloadElement)
            || payloadElement.GetString() is not { Length: > 0 } rawPayload)
        {
            return;
        }

        JsonElement envelope;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            envelope = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        // Binding доступен любому скрипту страницы, поэтому конверт проверяется полностью.
        if (envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String
            || !envelope.TryGetProperty("payload", out var callbackPayload)
            || callbackPayload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (kindElement.GetString())
        {
            case "request":
                tab.EnqueueCallbackWork(() => DispatchCallbackRequestAsync(tab, callbackPayload));
                break;

            case "event":
                tab.EnqueueCallbackWork(() =>
                {
                    Publish(tab, BridgeEvent.Callback, callbackPayload);
                    return Task.CompletedTask;
                });
                break;

            case "finalized":
                tab.EnqueueCallbackWork(() =>
                {
                    Publish(tab, BridgeEvent.CallbackFinalized, callbackPayload);
                    return Task.CompletedTask;
                });
                break;
        }
    }

    private async Task DispatchCallbackRequestAsync(CdpTab tab, JsonElement payload)
    {
        if (CallbackRequested is not { } handler)
            return;

        var message = new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Event,
            TabId = tab.TabId,
            WindowId = tab.WindowId,
            Event = BridgeEvent.Callback,
            Payload = payload,
        };

        if (!BridgeEventPayloadReader.TryReadCallback(message, out var callbackArgs))
            return;

        var request = new BridgeCallbackRequestPayload
        {
            RequestId = payload.TryGetProperty("requestId", out var requestId) && requestId.ValueKind == JsonValueKind.String && requestId.GetString() is { Length: > 0 } id ? id : message.Id,
            TabId = tab.TabId,
            Name = callbackArgs.Name,
            Args = callbackArgs.Args,
            Code = callbackArgs.Code,
        };

        try
        {
            // Решение не возвращается в страницу: binding только уведомляет, и вызов уже продолжился
            // с исходными аргументами (см. CdpPageScripts.CreateCallbackBridge).
            var decision = await handler(request, lifetime.Token).ConfigureAwait(false);
            if (!string.Equals(decision.Action, "continue", StringComparison.Ordinal) || decision.Args is not null || decision.Code is not null)
                Logger?.LogObscuraCallbackDecisionIgnored(decision.Action, request.Name, tab.TabId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Logger?.LogObscuraHandlerFailed(exception, "callback", tab.TabId);
        }
    }

    private async Task HandleRequestPausedAsync(CdpTab tab, JsonElement parameters)
    {
        var requestId = parameters.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() : null;
        if (string.IsNullOrEmpty(requestId))
            return;

        try
        {
            var (method, commandParameters) = await DecideRequestAsync(tab, requestId, parameters).ConfigureAwait(false);
            await tab.Server.Connection.SendAsync(method, commandParameters, tab.SessionId, cancellationToken: lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Logger?.LogObscuraHandlerFailed(exception, "request", tab.TabId);
            await TryContinueRequestAsync(tab, requestId).ConfigureAwait(false);
        }
    }

    private async Task TryContinueRequestAsync(CdpTab tab, string requestId)
    {
        try
        {
            await tab.Server.Connection.SendAsync(
                "Fetch.continueRequest",
                new JsonObject { ["requestId"] = requestId },
                tab.SessionId,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or ObjectDisposedException)
        {
            // Вкладка закрыта — продолжать нечего.
        }
    }

    private async ValueTask<(string Method, JsonObject Parameters)> DecideRequestAsync(CdpTab tab, string requestId, JsonElement parameters)
    {
        var request = parameters.GetProperty("request");
        var url = request.GetProperty("url").GetString() ?? string.Empty;
        var method = request.TryGetProperty("method", out var methodElement) ? methodElement.GetString() ?? "GET" : "GET";
        var resourceType = parameters.TryGetProperty("resourceType", out var typeElement) ? typeElement.GetString() : null;

        var isMainFrame = IsMainFrameRequest(resourceType, requestId, tab.TargetId);
        if (isMainFrame && tab.TakePendingDocument(url) is { } document)
        {
            return ("Fetch.fulfillRequest", CreateFulfillParameters(
                requestId,
                200,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = "text/html; charset=utf-8",
                    ["Cache-Control"] = "no-store, no-cache, must-revalidate",
                },
                Convert.ToBase64String(document)));
        }

        if (tab.Interception?.Matches(url, method) != true || RequestInterceptionRequested is not { } handler)
            return ("Fetch.continueRequest", new JsonObject { ["requestId"] = requestId });

        var payload = new BridgeInterceptedRequestPayload
        {
            RequestId = string.Concat(tab.TabId, "/", requestId),
            TabId = tab.TabId,
            Url = url,
            Method = method,
            ResourceType = MapResourceType(resourceType, isMainFrame),
            Headers = ReadHeaders(request),
            SupportsNavigationFulfillment = true,
            AppliedByTransport = true,
            Timestamp = DateTimeOffset.UtcNow,
        };

        var decision = await handler(payload, lifetime.Token).ConfigureAwait(false);
        return MapDecision(requestId, decision);
    }

    internal static (string Method, JsonObject Parameters) MapDecision(string requestId, BridgeInterceptHttpResponse decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        switch (decision.Action)
        {
            case "abort":
                return ("Fetch.failRequest", new JsonObject { ["requestId"] = requestId, ["errorReason"] = "BlockedByClient" });

            case "fulfill":
                return ("Fetch.fulfillRequest", CreateFulfillParameters(
                    requestId,
                    decision.StatusCode ?? 200,
                    decision.ResponseHeaders,
                    decision.BodyBase64 ?? string.Empty));

            default:
                var parameters = new JsonObject { ["requestId"] = requestId };
                if (!string.IsNullOrEmpty(decision.Url))
                    parameters["url"] = decision.Url;

                if (decision.Headers is { Count: > 0 } headers)
                    parameters["headers"] = ToHeaderEntries(headers);

                return ("Fetch.continueRequest", parameters);
        }
    }

    /// <summary>
    /// Документ главного фрейма вкладки.
    /// </summary>
    /// <remarks>
    /// Obscura сообщает <c lang="text">frameId</c> вкладки и для документов iframe, но навигации
    /// главного фрейма получают идентификатор запроса вида <c lang="text">&lt;targetId&gt;.N</c> (loader
    /// вкладки), а загрузки iframe идут как обычные выборки <c lang="text">fetch-N</c>.
    /// </remarks>
    internal static bool IsMainFrameRequest(string? cdpType, string requestId, string targetId)
        => string.Equals(cdpType, "Document", StringComparison.Ordinal)
            && requestId.StartsWith(targetId, StringComparison.Ordinal)
            && requestId.Length > targetId.Length
            && requestId[targetId.Length] == '.';

    internal static string MapResourceType(string? cdpType, bool isMainFrame)
        => cdpType switch
        {
            "Document" => isMainFrame ? "main_frame" : "sub_frame",
            "Stylesheet" => "stylesheet",
            "Image" => "image",
            "Media" => "media",
            "Font" => "font",
            "Script" => "script",
            "XHR" or "Fetch" => "xmlhttprequest",
            "WebSocket" => "websocket",
            "Ping" => "ping",
            _ => "other",
        };

    private static Dictionary<string, string> ReadHeaders(JsonElement request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headersElement.EnumerateObject())
                headers[header.Name] = header.Value.ValueKind == JsonValueKind.String ? header.Value.GetString() ?? string.Empty : header.Value.GetRawText();
        }

        return headers;
    }

    private static JsonObject CreateFulfillParameters(string requestId, int statusCode, IReadOnlyDictionary<string, string>? headers, string bodyBase64)
        => new()
        {
            ["requestId"] = requestId,
            ["responseCode"] = statusCode,
            ["responseHeaders"] = ToHeaderEntries(headers ?? new Dictionary<string, string>(StringComparer.Ordinal)),
            ["body"] = bodyBase64,
        };

    private static JsonArray ToHeaderEntries(IReadOnlyDictionary<string, string> headers)
    {
        var entries = new JsonArray();
        foreach (var (name, value) in headers)
            entries.Add(new JsonObject { ["name"] = name, ["value"] = value });

        return entries;
    }

    private void Publish(CdpTab tab, BridgeEvent bridgeEvent, JsonObject payload)
        => Publish(tab, bridgeEvent, JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject));

    private void Publish(CdpTab tab, BridgeEvent bridgeEvent, JsonElement payload)
    {
        if (RuntimeEventReceived is not { } handler || tab.IsClosed)
            return;

        handler(SessionId, new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Event,
            Event = bridgeEvent,
            TabId = tab.TabId,
            WindowId = tab.WindowId,
            Payload = payload,
        });
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);
}
