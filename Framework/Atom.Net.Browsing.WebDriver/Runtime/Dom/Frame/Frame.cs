using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет runtime-реализацию DOM-фрейма внутри страницы браузера.
/// </summary>
public sealed class Frame : IFrame
{
    private readonly WebPage page;
    private readonly List<IFrame> childFrames = [];

    private readonly IFrame? parentFrame;

    private readonly Element? frameElement;

    private void ThrowIfDisposed() => page.ThrowIfDisposed();

    internal Frame(WebPage page, IFrame? parentFrame = null, Element? frameElement = null)
    {
        this.page = page;
        this.parentFrame = parentFrame;
        this.frameElement = frameElement;

        if (parentFrame is Frame parent)
        {
            lock (parent.childFrames)
                parent.childFrames.Add(this);
        }

        frameElement?.AttachContentFrame(this);
    }

    public IWebPage Page => page;

    public bool IsDisposed => ((WebPage)Page).IsDisposed;

    public IElement? Host => frameElement;

    public event MutableEventHandler<IFrame, WebLifecycleEventArgs>? DomContentLoaded;

    public event MutableEventHandler<IFrame, WebLifecycleEventArgs>? NavigationCompleted;

    public event MutableEventHandler<IFrame, WebLifecycleEventArgs>? PageLoaded;

    public IEnumerable<IFrame> Frames => SnapshotChildFrames();

    internal void InvokeLifecycle(Protocol.BridgeEvent lifecycleEvent, WebLifecycleEventArgs args)
    {
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameLifecycleEventReceived(page.TabId, lifecycleEvent.ToString(), args.Url?.ToString() ?? "<none>");

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
    }

    public async ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (page.BridgeCommands is { } bridge)
        {
            string? url;
            try
            {
                url = frameElement is null
                    ? await bridge.GetUrlAsync(cancellationToken).ConfigureAwait(false)
                    : await EvaluateAsync<string>("return globalThis.location?.href ?? null", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Observe(ex);
                return null;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        }

        return page.Transport.CurrentUrl;
    }

    public ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    public async ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (page.BridgeCommands is { } bridge)
        {
            if (frameElement is null)
                return await bridge.GetTitleAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await EvaluateAsync<string>("return document.title ?? null", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Observe(ex);
                return null;
            }
        }

        return page.Transport.CurrentTitle;
    }

    public ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    public async ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (page.BridgeCommands is { } bridge)
        {
            if (frameElement is null)
                return await bridge.GetContentAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await EvaluateAsync<string>("return document.documentElement?.outerHTML ?? document.body?.outerHTML ?? null", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Observe(ex);
                return null;
            }
        }

        return page.Transport.CurrentContent;
    }

    public ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    public ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken)
        => EvaluateScriptCoreAsync(script, preferPageContextOnNull: true, cancellationToken);

    internal async ValueTask<JsonElement?> EvaluateScriptCoreAsync(string script, bool preferPageContextOnNull, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameEvaluateStarting(page.TabId, script.Length);

        if (page.BridgeCommands is { } bridge)
        {
            var frameHostElementId = GetFrameHostElementId();
            return frameHostElementId is null
                ? await bridge.ExecuteScriptAsync(
                    script,
                    shadowHostElementId: null,
                    frameHostElementId: null,
                    preferPageContextOnNull: preferPageContextOnNull,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : await bridge.ExecuteScriptAsync(
                    script,
                    shadowHostElementId: null,
                    frameHostElementId: frameHostElementId,
                    preferPageContextOnNull: preferPageContextOnNull,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var result = page.Transport.Evaluate(script);
        await page.SyncTransportEventsAsync().ConfigureAwait(false);
        return result;
    }

    public ValueTask<JsonElement?> EvaluateAsync(string script) => EvaluateAsync(script, CancellationToken.None);

    public async ValueTask<TResult?> EvaluateAsync<TResult>(string script, CancellationToken cancellationToken)
    {
        var result = await EvaluateScriptCoreAsync(script, preferPageContextOnNull: true, cancellationToken).ConfigureAwait(false);
        if (result is not JsonElement element)
            return default;

        if (typeof(TResult) == typeof(string))
            return (TResult?)(object?)element.GetString();

        if (typeof(TResult) == typeof(bool) && (element.ValueKind is JsonValueKind.True or JsonValueKind.False))
            return (TResult?)(object)element.GetBoolean();

        if (typeof(TResult) == typeof(int) && element.TryGetInt32(out var intValue))
            return (TResult?)(object)intValue;

        if (typeof(TResult) == typeof(double) && element.TryGetDouble(out var doubleValue))
            return (TResult?)(object)doubleValue;

        if (typeof(TResult) == typeof(Uri) && element.ValueKind == JsonValueKind.String && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri))
            return (TResult?)(object)uri;

        if (typeof(TResult) == typeof(JsonElement))
            return (TResult?)(object)element;

        return default;
    }

    public ValueTask<TResult?> EvaluateAsync<TResult>(string script) => EvaluateAsync<TResult>(script, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, page.WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector) => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), kind, page.WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    public async ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitForElementAsync(new CssSelector(selector), kind, timeout, cancellationToken).ConfigureAwait(false);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout) => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return WaitForElementAsync(settings.Selector ?? new CssSelector(string.Empty), settings.Kind, settings.Timeout ?? page.WaitingTimeout, cancellationToken);
    }

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings) => WaitForElementAsync(settings, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, page.WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector) => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, kind, page.WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    public async ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameDomOperationStarting(page.TabId, "ожидание элемента", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return CreateFallbackElement(HtmlFallbackDomQuery.FindFirst(page.CurrentContent, selector));

        var elementId = await bridge.WaitForElementAsync(CreateWaitForElementPayload(selector, kind, timeout), cancellationToken).ConfigureAwait(false);
        return CreateBridgeElement(elementId);
    }

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout) => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    public ValueTask<IElement?> GetElementAsync(string selector, CancellationToken cancellationToken)
        => GetElementAsync(new CssSelector(selector), cancellationToken);

    public ValueTask<IElement?> GetElementAsync(string selector) => GetElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> GetElementAsync(CssSelector selector, CancellationToken cancellationToken)
        => GetElementAsync((ElementSelector)selector, cancellationToken);

    public ValueTask<IElement?> GetElementAsync(CssSelector selector) => GetElementAsync(selector, CancellationToken.None);

    public async ValueTask<IElement?> GetElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameDomOperationStarting(page.TabId, "поиск элемента", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return CreateFallbackElement(HtmlFallbackDomQuery.FindFirst(page.CurrentContent, selector));

        var elementId = await bridge.FindElementAsync(CreateElementSearchPayload(selector), cancellationToken).ConfigureAwait(false);
        return CreateBridgeElement(elementId);
    }

    public ValueTask<IElement?> GetElementAsync(ElementSelector selector) => GetElementAsync(selector, CancellationToken.None);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector, CancellationToken cancellationToken)
        => GetElementsAsync(new CssSelector(selector), cancellationToken);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector) => GetElementsAsync(selector, CancellationToken.None);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(CssSelector selector, CancellationToken cancellationToken)
        => GetElementsAsync((ElementSelector)selector, cancellationToken);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(CssSelector selector) => GetElementsAsync(selector, CancellationToken.None);

    public async ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameDomOperationStarting(page.TabId, "поиск элементов", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return CreateFallbackElements(HtmlFallbackDomQuery.FindAll(page.CurrentContent, selector));

        var elementIds = await bridge.FindElementsAsync(CreateElementSearchPayload(selector), cancellationToken).ConfigureAwait(false);
        return CreateBridgeElements(elementIds);
    }

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector) => GetElementsAsync(selector, CancellationToken.None);

    public async ValueTask<IShadowRoot?> GetShadowRootAsync(string selector, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebFrameDomOperationStarting(page.TabId, "поиск теневого корня", selector);
        var host = await GetElementAsync(selector, cancellationToken).ConfigureAwait(false);
        return host is null ? null : await host.GetShadowRootAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IShadowRoot?> GetShadowRootAsync(string selector) => GetShadowRootAsync(selector, CancellationToken.None);

    public async ValueTask<IEnumerable<IFrame>> GetChildFramesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureChildFramesDiscoveredAsync(cancellationToken).ConfigureAwait(false);
        return SnapshotChildFrames();
    }

    public ValueTask<IEnumerable<IFrame>> GetChildFramesAsync() => GetChildFramesAsync(CancellationToken.None);

    public ValueTask<IFrame?> GetParentFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(parentFrame);
    }

    public ValueTask<IFrame?> GetParentFrameAsync() => GetParentFrameAsync(CancellationToken.None);

    public ValueTask<IElement?> GetFrameElementAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Host);
    }

    public ValueTask<IElement?> GetFrameElementAsync() => GetFrameElementAsync(CancellationToken.None);

    public async ValueTask<string?> GetNameAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (frameElement is not null)
            return await frameElement.GetAttributeAsync("name", cancellationToken).ConfigureAwait(false);

        return nameof(IWebPage.MainFrame);
    }

    public ValueTask<string?> GetNameAsync() => GetNameAsync(CancellationToken.None);

    private JsonObject CreateElementSearchPayload(ElementSelector selector)
    {
        var payload = new JsonObject
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
        };

        if (GetFrameHostElementId() is { } frameHostElementId)
            payload["frameHostElementId"] = frameHostElementId;

        return payload;
    }

    private static JsonObject CreateFrameDiscoveryPayload()
        => new()
        {
            ["strategy"] = nameof(ElementSelectorStrategy.Css),
            ["value"] = "iframe,frame",
            ["allowShadowRootDiscovery"] = true,
        };

    private JsonObject CreateWaitForElementPayload(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout)
    {
        var payload = new JsonObject
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["kind"] = kind.ToString(),
            ["timeoutMs"] = timeout.TotalMilliseconds,
        };

        if (GetFrameHostElementId() is { } frameHostElementId)
            payload["frameHostElementId"] = frameHostElementId;

        return payload;
    }

    internal Frame[] SnapshotChildFrames()
    {
        lock (childFrames)
            return [.. childFrames.OfType<Frame>()];
    }

    internal void DetachFromParent()
    {
        if (parentFrame is not Frame parent)
            return;

        lock (parent.childFrames)
            _ = parent.childFrames.Remove(this);
    }

    internal async ValueTask EnsureChildFramesDiscoveredAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (page.BridgeCommands is not { } bridge)
            return;

        string[] frameHostElementIds;
        try
        {
            frameHostElementIds = await bridge.FindElementsAsync(
                CreateFrameDiscoveryPayload(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Observe(ex);
            return;
        }

        foreach (var frameHostElementId in frameHostElementIds)
        {
            if (string.IsNullOrWhiteSpace(frameHostElementId))
                continue;

            _ = page.GetOrCreateChildFrame(this, frameHostElementId);
        }
    }

    private string? GetFrameHostElementId()
        => frameElement?.BridgeElementId;

    private Element? CreateBridgeElement(string? elementId)
        => string.IsNullOrWhiteSpace(elementId) ? null : new Element(this, elementId);

    private Element[] CreateBridgeElements(string[] elementIds)
    {
        if (elementIds.Length == 0)
            return [];

        var elements = new Element[elementIds.Length];
        for (var index = 0; index < elementIds.Length; index++)
            elements[index] = new Element(this, elementIds[index]);

        return elements;
    }

    private Element? CreateFallbackElement(HtmlFallbackElementState? state)
        => state is null ? null : new Element(this, fallbackState: state);

    private IElement[] CreateFallbackElements(IEnumerable<HtmlFallbackElementState> states)
        => states.Select(state => (IElement)new Element(this, fallbackState: state)).ToArray();

    private static string DescribeSelector<TSelector>(TSelector selector)
        => selector?.ToString() ?? "<none>";

    public ValueTask<string?> GetFrameElementHandleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return frameElement is null
            ? ValueTask.FromResult<string?>(null)
            : frameElement.GetElementHandleAsync(cancellationToken);
    }

    public ValueTask<string?> GetFrameElementHandleAsync() => GetFrameElementHandleAsync(CancellationToken.None);

    private static void Observe(Exception ex)
        => Trace.TraceWarning(ex.ToString());

    public ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken)
        => GetScreenshotCoreAsync(cancellationToken);

    public ValueTask<Memory<byte>> GetScreenshotAsync() => GetScreenshotAsync(CancellationToken.None);

    private async ValueTask<Memory<byte>> GetScreenshotCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (frameElement is null)
        {
            return page.BridgeCommands is null
                ? Memory<byte>.Empty
                : await page.GetScreenshotAsync(cancellationToken).ConfigureAwait(false);
        }

        var pageScreenshot = await page.GetScreenshotAsync(cancellationToken).ConfigureAwait(false);
        if (pageScreenshot.IsEmpty)
            return Memory<byte>.Empty;

        var bounds = await frameElement.GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);
        return bounds is Rectangle rectangle
            ? ScreenshotCropper.CropPng(pageScreenshot, rectangle)
            : Memory<byte>.Empty;
    }

    public ValueTask<bool> IsDetachedAsync(CancellationToken cancellationToken)
        => IsDetachedCoreAsync(cancellationToken);

    public ValueTask<bool> IsDetachedAsync() => IsDetachedAsync(CancellationToken.None);

    private async ValueTask<bool> IsDetachedCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (frameElement is not { BridgeElementId: { } frameElementId })
            return false;

        if (page.IsFrameElementDetached(frameElementId))
            return true;

        if (page.BridgeCommands is not { } bridge)
            return false;

        var description = await bridge.TryDescribeElementAsync(frameElementId, cancellationToken).ConfigureAwait(false);
        if (description is null || !description.IsConnected)
        {
            page.MarkFrameElementDetached(frameElementId);
            return true;
        }

        return false;
    }

    public async ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (frameElement is not null)
            return await frameElement.IsVisibleAsync(cancellationToken).ConfigureAwait(false);

        var bounds = await GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);
        return bounds is { Width: > 0, Height: > 0 };
    }

    public ValueTask<bool> IsVisibleAsync() => IsVisibleAsync(CancellationToken.None);

    public async ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (frameElement is not null)
            return await frameElement.GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);

        var viewport = await page.GetViewportSizeAsync(cancellationToken).ConfigureAwait(false);
        return viewport is { IsEmpty: false } size ? new Rectangle(Point.Empty, size) : null;
    }

    public ValueTask<Rectangle?> GetBoundingBoxAsync() => GetBoundingBoxAsync(CancellationToken.None);

    public ValueTask<IFrame?> GetContentFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IFrame?>(frameElement is null ? null : this);
    }

    public ValueTask<IFrame?> GetContentFrameAsync() => GetContentFrameAsync(CancellationToken.None);
}