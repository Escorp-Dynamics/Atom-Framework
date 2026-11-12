using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Hardware.Input;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Канал команд вкладки Obscura: команды драйвера переводятся в вызовы протокола отладки и в
/// JS-функции <see cref="CdpPageScripts"/>.
/// </summary>
/// <remarks>
/// Элементы адресуются идентификаторами реестра, который живёт в самом документе: ссылки на объекты
/// CDP у Obscura ненадёжны (см. <see cref="CdpPageScripts.CreateRegistryPrelude(string)"/>).
/// Ввод доставляется движком напрямую (<c lang="text">Input.*</c>), без ОС и окна на экране.
/// </remarks>
internal sealed class CdpPageCommandClient(CdpBrowserCommandTransport transport, CdpTab tab, WebPage page)
    : IPageCommandClient, IPageDirectControlClient
{
    private const string TargetElementNotFoundMessage = "Target element not found.";
    private const string ShadowHostNotFoundMessage = "Shadow host element not found.";
    private const string FrameHostNotFoundMessage = "Frame host element not found.";

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StableSampleInterval = TimeSpan.FromMilliseconds(120);

    internal CdpTab Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public Uri? GetDiscoveryUrl() => null;

    public async ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default)
        => (await EvaluateValueAsync("String(document.title ?? '')", cancellationToken).ConfigureAwait(false)).GetString() ?? string.Empty;

    public async ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default)
        => (await EvaluateValueAsync("String(location.href)", cancellationToken).ConfigureAwait(false)).GetString() ?? string.Empty;

    public async ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default)
        => (await EvaluateValueAsync(CdpPageScripts.DocumentContent, cancellationToken).ConfigureAwait(false)).GetString() ?? string.Empty;

    public ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, CancellationToken cancellationToken = default)
        => transport.OpenTabInWindowAsync(windowId, cancellationToken);

    public ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(Point? position, CancellationToken cancellationToken = default)
        => transport.OpenWindowAsync(SurfaceProvisioningRequest.Default, cancellationToken);

    public async ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
    {
        var result = await Tab.SendAsync("Page.captureScreenshot", new JsonObject { ["format"] = "png" }, cancellationToken).ConfigureAwait(false);
        return result.TryGetProperty("data", out var data) ? data.GetString() : null;
    }

    public ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return RunNavigationAsync("Page.navigate", new JsonObject { ["url"] = url.AbsoluteUri }, cancellationToken);
    }

    public ValueTask ReloadAsync(CancellationToken cancellationToken = default)
        => RunNavigationAsync("Page.reload", parameters: null, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(script, shadowHostElementId: null, frameHostElementId: null, elementId: null, cancellationToken: cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string script, string shadowHostElementId, CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(script, shadowHostElementId, frameHostElementId: null, elementId: null, cancellationToken: cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => ExecuteScriptAsync(script, shadowHostElementId, frameHostElementId, elementId: null, preferPageContextOnNull, forcePageContextExecution, cancellationToken);

    // Флаги выбора мира игнорируются: у Obscura нет изолированного мира расширения, и скрипт всегда исполняется один раз в мире страницы.
    public async ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        string? elementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
    {
        var (kind, targetId, missingMessage) = (elementId, shadowHostElementId, frameHostElementId) switch
        {
            ({ Length: > 0 } id, _, _) => ("element", id, TargetElementNotFoundMessage),
            (_, { Length: > 0 } id, _) => ("shadow", id, ShadowHostNotFoundMessage),
            (_, _, { Length: > 0 } id) => ("frame", id, FrameHostNotFoundMessage),
            _ => ("page", null, null),
        };

        var outcome = await InvokeAsync(
            CdpPageScripts.Execute,
            targetId,
            missingMessage,
            "__result",
            new JsonArray(script ?? string.Empty, kind),
            cancellationToken).ConfigureAwait(false);

        if (outcome.ValueKind != JsonValueKind.Object || !outcome.TryGetProperty("ok", out var ok))
            throw new BridgeCommandException("Скрипт Obscura вернул неверный результат", BridgeStatus.Error);

        if (ok.ValueKind != JsonValueKind.True)
        {
            var error = outcome.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            throw new BridgeCommandException(error ?? "Ошибка исполнения скрипта", BridgeStatus.Error);
        }

        return outcome.TryGetProperty("value", out var value) ? value.Clone() : CreateStringElement("null");
    }

    public async ValueTask<JsonElement> ExecuteScriptInFramesAsync(
        string script,
        bool isolatedWorld,
        bool includeMetadata,
        CancellationToken cancellationToken = default)
    {
        var serialized = await InvokeAsync(
            CdpPageScripts.ExecuteInFrames,
            targetId: null,
            missingTargetMessage: null,
            "__result",
            new JsonArray(script ?? string.Empty, includeMetadata),
            cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(serialized.GetString() ?? "[]");
        return document.RootElement.Clone();
    }

    public async ValueTask SetCookieAsync(
        string contextId,
        string name,
        string value,
        string? domain,
        string? path,
        bool secure,
        bool httpOnly,
        long? expires,
        CancellationToken cancellationToken = default)
    {
        var cookie = new JsonObject
        {
            ["name"] = name,
            ["value"] = value,
            ["path"] = string.IsNullOrEmpty(path) ? "/" : path,
            ["secure"] = secure,
            ["httpOnly"] = httpOnly,
        };

        if (!string.IsNullOrWhiteSpace(domain))
            cookie["domain"] = domain;
        else
            cookie["url"] = await GetUrlAsync(cancellationToken).ConfigureAwait(false);

        if (expires is { } expiresSeconds)
            cookie["expires"] = expiresSeconds;

        await Tab.SendAsync(
            "Storage.setCookies",
            new JsonObject { ["browserContextId"] = Tab.ContextId, ["cookies"] = new JsonArray(cookie) },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<JsonElement> GetCookiesAsync(string contextId, CancellationToken cancellationToken = default)
    {
        var result = await Tab.SendAsync("Storage.getCookies", new JsonObject { ["browserContextId"] = Tab.ContextId }, cancellationToken).ConfigureAwait(false);
        var cookies = new JsonArray();

        if (result.TryGetProperty("cookies", out var cookieArray) && cookieArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var cookie in cookieArray.EnumerateArray())
            {
                var mapped = new JsonObject
                {
                    ["name"] = ReadString(cookie, "name"),
                    ["value"] = ReadString(cookie, "value"),
                    ["domain"] = ReadString(cookie, "domain"),
                    ["path"] = ReadString(cookie, "path") ?? "/",
                    ["secure"] = cookie.TryGetProperty("secure", out var secure) && secure.ValueKind == JsonValueKind.True,
                    ["httpOnly"] = cookie.TryGetProperty("httpOnly", out var httpOnly) && httpOnly.ValueKind == JsonValueKind.True,
                };

                if (cookie.TryGetProperty("expires", out var expires) && expires.TryGetDouble(out var expiresSeconds) && expiresSeconds > 0)
                    mapped["expires"] = (long)expiresSeconds;

                cookies.Add(mapped);
            }
        }

        return JsonSerializer.SerializeToElement<JsonNode>(cookies, BridgeJsonContext.Default.JsonNode);
    }

    public async ValueTask DeleteCookiesAsync(string contextId, CancellationToken cancellationToken = default)
        => _ = await Tab.SendAsync("Storage.clearCookies", new JsonObject { ["browserContextId"] = Tab.ContextId }, cancellationToken).ConfigureAwait(false);

    public async ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default)
    {
        Tab.Interception = RequestInterceptionState.Create(enabled, urlPatterns);

        if (enabled)
            await transport.EnsureFetchEnabledAsync(Tab, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Применяет личность вкладки средствами эмуляции и ранним скриптом подмены.
    /// </summary>
    /// <remarks>
    /// Прокси, часовой пояс и геолокация в Obscura задаются процессу; их применил транспорт, когда
    /// выбирал процесс для вкладки, поэтому здесь они не трогаются.
    /// </remarks>
    public async ValueTask SetTabContextAsync(JsonObject payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await Tab.ContextGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyEmulationAsync(payload, cancellationToken).ConfigureAwait(false);
            await ApplyIdentityScriptAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Tab.ContextGate.Release();
        }
    }

    public ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken = default)
        => transport.CloseWindowAsync(windowId, cancellationToken);

    public ValueTask CloseTabAsync(CancellationToken cancellationToken = default)
        => transport.CloseTabAsync(Tab, cancellationToken);

    // У headless-движка нет переднего плана: все вкладки получают кадры и ввод одновременно.
    public ValueTask ActivateWindowAsync(string windowId, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ActivateTabAsync(string targetTabId, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    // Окна на экране нет: границы окна совпадают с областью просмотра.
    public async ValueTask<Rectangle> GetWindowBoundsAsync(CancellationToken cancellationToken = default)
    {
        var metrics = await Tab.SendAsync("Page.getLayoutMetrics", parameters: null, cancellationToken).ConfigureAwait(false);
        var viewport = metrics.TryGetProperty("cssLayoutViewport", out var css) ? css : metrics.GetProperty("layoutViewport");
        var width = viewport.TryGetProperty("clientWidth", out var widthElement) ? (int)Math.Round(widthElement.GetDouble()) : 0;
        var height = viewport.TryGetProperty("clientHeight", out var heightElement) ? (int)Math.Round(heightElement.GetDouble()) : 0;
        return new Rectangle(0, 0, width, height);
    }

    public async ValueTask<string?> FindElementAsync(JsonObject payload, CancellationToken cancellationToken = default)
    {
        var result = await FindCoreAsync(payload, multiple: false, CdpPageScripts.MapSingleElement, cancellationToken).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    }

    public async ValueTask<string[]> FindElementsAsync(JsonObject payload, CancellationToken cancellationToken = default)
    {
        var result = await FindCoreAsync(payload, multiple: true, CdpPageScripts.MapElementList, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array)
            return [];

        return [.. result.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String).Select(static item => item.GetString()!)];
    }

    /// <summary>
    /// Ожидание элемента опросом из драйвера с той же семантикой видов, что у content-скрипта.
    /// </summary>
    public async ValueTask<string?> WaitForElementAsync(JsonObject payload, TimeSpan waitTimeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var kinds = (payload["kind"]?.GetValue<string>() ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var requireStable = kinds.Contains("Stable", StringComparer.Ordinal);
        var requireVisible = requireStable || kinds.Contains("Visible", StringComparer.Ordinal);

        var deadline = Stopwatch.StartNew();
        var stability = new StabilityTracker();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = await TryFindWaitCandidateAsync(payload, cancellationToken).ConfigureAwait(false);
            if (IsSatisfied(candidate, requireVisible, requireStable, stability, deadline.Elapsed) && candidate is { } found)
                return found.Id;

            if (deadline.Elapsed >= waitTimeout)
                return null;

            await Task.Delay(WaitPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsSatisfied(WaitCandidate? candidate, bool requireVisible, bool requireStable, StabilityTracker stability, TimeSpan elapsed)
    {
        if (candidate is not { } found)
        {
            stability.Reset();
            return false;
        }

        if (!requireVisible)
            return true;

        if (!found.Visible)
        {
            stability.Reset();
            return false;
        }

        return !requireStable || stability.Observe(found.Box, elapsed);
    }

    /// <summary>
    /// Устойчивость рамки: два замера с интервалом не меньше <see cref="StableSampleInterval"/> совпали.
    /// </summary>
    private sealed class StabilityTracker
    {
        private RectangleF? previousBox;
        private TimeSpan previousSampleAt;

        public void Reset() => previousBox = null;

        public bool Observe(RectangleF box, TimeSpan elapsed)
        {
            if (previousBox is { } previous)
            {
                if (elapsed - previousSampleAt < StableSampleInterval)
                    return false;

                if (IsSameBox(previous, box))
                    return true;
            }

            previousBox = box;
            previousSampleAt = elapsed;
            return false;
        }

        private static bool IsSameBox(RectangleF left, RectangleF right)
            => Math.Abs(left.X - right.X) < 0.5f
                && Math.Abs(left.Y - right.Y) < 0.5f
                && Math.Abs(left.Width - right.Width) < 0.5f
                && Math.Abs(left.Height - right.Height) < 0.5f;
    }

    private readonly record struct WaitCandidate(string Id, bool Visible, RectangleF Box);

    public async ValueTask<string?> GetElementPropertyAsync(string elementId, string propertyName, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(CdpPageScripts.GetProperty, elementId, missingTargetMessage: null, "__result", new JsonArray(propertyName), cancellationToken).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    }

    public async ValueTask<bool> CheckShadowRootAsync(string elementId, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(CdpPageScripts.HasShadowRoot, elementId, missingTargetMessage: null, "__result", [], cancellationToken).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.True;
    }

    public async ValueTask<PointF> ResolveElementScreenPointAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(
            CdpPageScripts.ViewportPoint,
            elementId,
            CdpPageScripts.ElementNotFoundMessage,
            "__result",
            new JsonArray(scrollIntoView),
            cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(result.GetString() ?? "{}");
        if (!BridgeServer.TryParsePointPayload(document.RootElement, out var point))
            throw new BridgeCommandException("Obscura вернула неверные координаты элемента", BridgeStatus.Error);

        return point;
    }

    public async ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CdpBrowserCommandTransport.ReadDocumentSnapshotAsync(Tab, cancellationToken).ConfigureAwait(false);
        var href = snapshot["href"]?.GetValue<string>();

        return new BridgeDebugPortStatusPayload(
            TabId: 0,
            HasPort: true,
            QueueLength: 0,
            HasSocket: true,
            IsReady: true,
            InterceptEnabled: Tab.Interception?.Enabled == true,
            HasTabContext: true,
            ContextId: Tab.ContextId,
            ContextUserAgent: null,
            HasBrowserTab: true,
            BrowserTabUrl: href,
            BrowserTabPendingUrl: null,
            BrowserTabStatus: "complete",
            RuntimeCheckStatus: "ok",
            RuntimeHref: href,
            RuntimeReadyState: snapshot["readyState"]?.GetValue<string>(),
            RuntimeCheckError: null);
    }

    public async ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string elementId, CancellationToken cancellationToken = default)
        => await TryDescribeElementAsync(elementId, cancellationToken).ConfigureAwait(false)
            ?? throw new BridgeCommandException(CdpPageScripts.ElementNotFoundMessage, BridgeStatus.NotFound);

    public async ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string elementId, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(CdpPageScripts.Describe, elementId, missingTargetMessage: null, "__result", [], cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.String)
            return null;

        using var document = JsonDocument.Parse(result.GetString()!);
        if (!BridgeServer.TryParseElementDescriptionPayload(document.RootElement, out var description))
            throw new BridgeCommandException("Obscura вернула неверное описание элемента", BridgeStatus.Error);

        return description;
    }

    public async ValueTask FocusElementAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => _ = await InvokeAsync(CdpPageScripts.Focus, elementId, CdpPageScripts.ElementNotFoundMessage, "__result", new JsonArray(scrollIntoView), cancellationToken).ConfigureAwait(false);

    public async ValueTask ScrollElementIntoViewAsync(string elementId, CancellationToken cancellationToken = default)
        => _ = await InvokeAsync(CdpPageScripts.ScrollIntoView, elementId, CdpPageScripts.ElementNotFoundMessage, "__result", [], cancellationToken).ConfigureAwait(false);

    public async ValueTask MoveMouseAsync(PointF viewportPoint, CancellationToken cancellationToken)
        => await DispatchMouseAsync("mouseMoved", viewportPoint, button: "none", buttons: 0, clickCount: 0, cancellationToken).ConfigureAwait(false);

    public async ValueTask ClickAsync(PointF viewportPoint, VirtualMouseButton button, int clickCount, CancellationToken cancellationToken)
    {
        var (name, mask) = button switch
        {
            VirtualMouseButton.Right => ("right", 2),
            VirtualMouseButton.Middle => ("middle", 4),
            _ => ("left", 1),
        };

        await MoveMouseAsync(viewportPoint, cancellationToken).ConfigureAwait(false);

        for (var index = 1; index <= Math.Max(1, clickCount); index++)
        {
            await DispatchMouseAsync("mousePressed", viewportPoint, name, mask, index, cancellationToken).ConfigureAwait(false);
            await DispatchMouseAsync("mouseReleased", viewportPoint, name, buttons: 0, index, cancellationToken).ConfigureAwait(false);
        }
    }

    // Касания Obscura не обрабатывает (Input.dispatchTouchEvent пуст), поэтому касание — щелчок.
    public ValueTask TapAsync(PointF viewportPoint, CancellationToken cancellationToken)
        => ClickAsync(viewportPoint, VirtualMouseButton.Left, clickCount: 1, cancellationToken);

    public async ValueTask TypeTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var character = enumerator.GetTextElement();
            var (definition, modifiers) = CdpKeyMap.ResolveCharacter(character);

            if (definition.VirtualKeyCode == 0)
            {
                await Tab.SendAsync("Input.insertText", new JsonObject { ["text"] = character }, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await DispatchKeyAsync(definition, modifiers, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask PressKeyAsync(ConsoleKey key, ConsoleModifiers modifiers, CancellationToken cancellationToken)
        => DispatchKeyAsync(CdpKeyMap.Resolve(key, modifiers), CdpKeyMap.ToModifierMask(modifiers), cancellationToken);

    public async ValueTask NavigateWithDocumentAsync(Uri url, string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(html);

        await transport.EnsureFetchEnabledAsync(Tab, cancellationToken).ConfigureAwait(false);
        Tab.SetPendingDocument(url, Encoding.UTF8.GetBytes(html));

        try
        {
            await NavigateAsync(url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Tab.ClearPendingDocument(url);
        }
    }

    private async ValueTask RunNavigationAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var result = await Tab.SendAsync(method, parameters, NavigationTimeout, cancellationToken).ConfigureAwait(false);

        if (result.TryGetProperty("errorText", out var errorText) && errorText.GetString() is { Length: > 0 } navigationError)
            throw new BridgeCommandException(navigationError, BridgeStatus.Error);

        // Ответ на навигацию у Obscura приходит после load: дожидаемся публикации уже полученных
        // событий жизненного цикла, чтобы вызывающий видел их до возврата, как и на мостовом пути.
#pragma warning disable VSTHRD003 // Барьер ставится в очередь вкладки этим же вызовом.
        await Tab.FlushEventsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private async ValueTask ApplyEmulationAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        if (payload["userAgent"]?.GetValue<string>() is { Length: > 0 } userAgent)
        {
            var parameters = new JsonObject { ["userAgent"] = userAgent };
            if (payload["languages"] is JsonArray languages && languages.Count > 0)
                parameters["acceptLanguage"] = string.Join(',', languages.Select(static language => language?.GetValue<string>()).Where(static language => !string.IsNullOrWhiteSpace(language)));

            await Tab.SendAsync("Emulation.setUserAgentOverride", parameters, cancellationToken).ConfigureAwait(false);
        }

        if (payload["locale"]?.GetValue<string>() is { Length: > 0 } locale)
            await Tab.SendAsync("Emulation.setLocaleOverride", new JsonObject { ["locale"] = locale }, cancellationToken).ConfigureAwait(false);

        if (BuildDeviceMetrics(payload) is { } metrics)
            await Tab.SendAsync("Emulation.setDeviceMetricsOverride", metrics, cancellationToken).ConfigureAwait(false);

        if (BuildMediaFeatures(payload) is { Count: > 0 } features)
            await Tab.SendAsync("Emulation.setEmulatedMedia", new JsonObject { ["features"] = features }, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject? BuildDeviceMetrics(JsonObject payload)
    {
        if (payload["viewport"] is not JsonObject viewport
            || viewport["width"]?.GetValue<int>() is not { } width
            || viewport["height"]?.GetValue<int>() is not { } height
            || width <= 0
            || height <= 0)
        {
            return null;
        }

        var metrics = new JsonObject
        {
            ["width"] = width,
            ["height"] = height,
            ["deviceScaleFactor"] = payload["deviceScaleFactor"]?.GetValue<double>() ?? 1d,
            ["mobile"] = payload["isMobile"]?.GetValue<bool>() ?? false,
        };

        if (payload["screen"] is JsonObject screen
            && screen["width"]?.GetValue<int>() is int screenWidth and > 0
            && screen["height"]?.GetValue<int>() is int screenHeight and > 0)
        {
            metrics["screenWidth"] = screenWidth;
            metrics["screenHeight"] = screenHeight;
        }

        return metrics;
    }

    private static JsonArray BuildMediaFeatures(JsonObject payload)
    {
        var features = new JsonArray();
        if (payload["colorScheme"]?.GetValue<string>() is { Length: > 0 } colorScheme)
            features.Add(new JsonObject { ["name"] = "prefers-color-scheme", ["value"] = colorScheme });

        if (payload["reducedMotion"]?.GetValue<bool>() is bool reducedMotion)
            features.Add(new JsonObject { ["name"] = "prefers-reduced-motion", ["value"] = reducedMotion ? "reduce" : "no-preference" });

        return features;
    }

    /// <summary>
    /// Ставит ранний скрипт личности (тот же <c lang="text">identity.main.js</c>, что у расширения) на каждый
    /// новый документ и применяет его к текущему.
    /// </summary>
    private async ValueTask ApplyIdentityScriptAsync(CancellationToken cancellationToken)
    {
        if (transport.IdentityRuntimeSource is not { } runtimeSource)
            return;

        var profile = new JsonObject();
        WebBrowser.AppendDeviceContext(profile, page.ResolvedDevice);
        var signature = profile.Count == 0 ? null : profile.ToJsonString();

        if (string.Equals(signature, Tab.IdentitySignature, StringComparison.Ordinal))
            return;

        if (Tab.IdentityScriptId is { } previousScriptId)
        {
            await Tab.SendAsync("Page.removeScriptToEvaluateOnNewDocument", new JsonObject { ["identifier"] = previousScriptId }, cancellationToken).ConfigureAwait(false);
            Tab.IdentityScriptId = null;
        }

        Tab.IdentitySignature = signature;
        if (signature is null)
            return;

        var source = string.Concat("try { globalThis.__ATOM_IDENTITY_PROFILE = ", signature, ";\n", runtimeSource, "\n} catch (error) { }");
        var added = await Tab.SendAsync("Page.addScriptToEvaluateOnNewDocument", new JsonObject { ["source"] = source }, cancellationToken).ConfigureAwait(false);
        Tab.IdentityScriptId = added.TryGetProperty("identifier", out var identifier) ? identifier.GetString() : null;

        await Tab.SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = source }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<JsonElement> FindCoreAsync(JsonObject payload, bool multiple, string resultMapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var (rootKind, rootId) = ResolveSearchRoot(payload);
        var arguments = new JsonArray(
            payload["strategy"]?.GetValue<string>() ?? "Css",
            payload["value"]?.GetValue<string>() ?? string.Empty,
            rootKind,
            multiple,
            payload["allowShadowRootDiscovery"]?.GetValue<bool>() ?? false);

        return InvokeAsync(CdpPageScripts.Find, rootId, missingTargetMessage: null, resultMapping, arguments, cancellationToken);
    }

    private async ValueTask<WaitCandidate?> TryFindWaitCandidateAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var result = await FindCoreAsync(payload, multiple: false, CdpPageScripts.MapWaitCandidate, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object || result.GetProperty("id").GetString() is not { } id)
            return null;

        var box = new RectangleF(
            (float)result.GetProperty("x").GetDouble(),
            (float)result.GetProperty("y").GetDouble(),
            (float)result.GetProperty("width").GetDouble(),
            (float)result.GetProperty("height").GetDouble());

        return new WaitCandidate(id, result.GetProperty("visible").ValueKind == JsonValueKind.True, box);
    }

    private static (string RootKind, string? RootId) ResolveSearchRoot(JsonObject payload)
    {
        if (payload["shadowHostElementId"]?.GetValue<string>() is { Length: > 0 } shadowHost)
            return ("shadow", shadowHost);

        if (payload["frameHostElementId"]?.GetValue<string>() is { Length: > 0 } frameHost)
            return ("frame", frameHost);

        if (payload["parentElementId"]?.GetValue<string>() is { Length: > 0 } parent)
            return ("parent", parent);

        return ("document", null);
    }

    private async ValueTask DispatchMouseAsync(string type, PointF point, string button, int buttons, int clickCount, CancellationToken cancellationToken)
        => _ = await Tab.SendAsync(
            "Input.dispatchMouseEvent",
            new JsonObject
            {
                ["type"] = type,
                ["x"] = point.X,
                ["y"] = point.Y,
                ["button"] = button,
                ["buttons"] = buttons,
                ["clickCount"] = clickCount,
            },
            cancellationToken).ConfigureAwait(false);

    private async ValueTask DispatchKeyAsync(CdpKeyDefinition definition, int modifiers, CancellationToken cancellationToken)
    {
        var down = new JsonObject
        {
            ["type"] = definition.Text is null ? "rawKeyDown" : "keyDown",
            ["key"] = definition.Key,
            ["code"] = definition.Code,
            ["windowsVirtualKeyCode"] = definition.VirtualKeyCode,
            ["modifiers"] = modifiers,
        };

        if (definition.Text is { } text)
            down["text"] = text;

        await Tab.SendAsync("Input.dispatchKeyEvent", down, cancellationToken).ConfigureAwait(false);
        await Tab.SendAsync(
            "Input.dispatchKeyEvent",
            new JsonObject
            {
                ["type"] = "keyUp",
                ["key"] = definition.Key,
                ["code"] = definition.Code,
                ["windowsVirtualKeyCode"] = definition.VirtualKeyCode,
                ["modifiers"] = modifiers,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<JsonElement> InvokeAsync(
        string function,
        string? targetId,
        string? missingTargetMessage,
        string resultMapping,
        JsonArray arguments,
        CancellationToken cancellationToken)
        => EvaluateValueAsync(
            CdpPageScripts.BuildInvocation(transport.RegistryKey, function, targetId, missingTargetMessage, resultMapping, arguments),
            cancellationToken);

    /// <summary>
    /// Вычисляет выражение в мире страницы и возвращает его значение (с ожиданием промиса).
    /// </summary>
    private async ValueTask<JsonElement> EvaluateValueAsync(string expression, CancellationToken cancellationToken)
    {
        var response = await Tab.SendAsync(
            "Runtime.evaluate",
            new JsonObject
            {
                ["expression"] = expression,
                ["awaitPromise"] = true,
                ["returnByValue"] = true,
            },
            cancellationToken).ConfigureAwait(false);

        if (response.TryGetProperty("exceptionDetails", out var exceptionDetails))
            throw new BridgeCommandException(DescribeException(exceptionDetails), BridgeStatus.Error);

        if (!response.TryGetProperty("result", out var result))
            return default;

        return result.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    /// <summary>Сообщение исключения страницы — как <c lang="text">error.message</c> у content-скрипта.</summary>
    internal static string DescribeException(JsonElement exceptionDetails)
    {
        var description = exceptionDetails.TryGetProperty("exception", out var exception)
            && exception.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;

        description ??= exceptionDetails.TryGetProperty("text", out var text) ? text.GetString() : null;
        if (string.IsNullOrWhiteSpace(description))
            return "Ошибка исполнения скрипта";

        var firstLine = description.Split('\n', 2)[0].TrimEnd();
        var separator = firstLine.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0 && firstLine.AsSpan(0, separator).EndsWith("Error", StringComparison.Ordinal) && !firstLine.AsSpan(0, separator).Contains(' '))
            return firstLine[(separator + 2)..];

        return firstLine;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static JsonElement CreateStringElement(string value)
        => JsonSerializer.SerializeToElement<JsonNode>(JsonValue.Create(value), BridgeJsonContext.Default.JsonNode);
}
