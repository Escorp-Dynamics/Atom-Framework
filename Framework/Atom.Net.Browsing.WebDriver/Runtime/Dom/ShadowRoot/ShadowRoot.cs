using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет runtime-реализацию shadow root и операций поиска внутри него.
/// </summary>
public sealed class ShadowRoot : IShadowRoot
{
    internal ShadowRoot(IElement host, WebPage page, IFrame? frame = null)
        => (Host, Page, Frame) = (host, page, frame ?? page.MainFrame);

    private void ThrowIfDisposed() => ((WebPage)Page).ThrowIfDisposed();

    public IElement Host { get; }

    public IWebPage Page { get; }

    public bool IsDisposed => ((WebPage)Page).IsDisposed;

    public IFrame Frame { get; }

    public IEnumerable<IFrame> Frames { get; } = [];

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return Frame.GetUrlAsync(cancellationToken);
    }

    public ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return Frame.GetTitleAsync(cancellationToken);
    }

    public ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    public async ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
        => await EvaluateAsync<string>("return shadowRoot.innerHTML", cancellationToken).ConfigureAwait(false);

    public ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    public ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken)
        => EvaluateScriptCoreAsync(script, preferPageContextOnNull: false, cancellationToken);

    private async ValueTask<JsonElement?> EvaluateScriptCoreAsync(string script, bool preferPageContextOnNull, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var page = (WebPage)Page;
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebShadowRootEvaluateStarting(page.TabId, script.Length);

        if (page.BridgeCommands is not { } bridge)
            return null;

        var hostElementId = await ResolveShadowHostElementIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(hostElementId))
            return null;

        return await bridge.ExecuteScriptAsync(
            script,
            shadowHostElementId: hostElementId,
            frameHostElementId: null,
            preferPageContextOnNull: preferPageContextOnNull,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, ((WebPage)Page).WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector) => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), kind, ((WebPage)Page).WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), kind, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout) => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return WaitForElementAsync(settings.Selector ?? new CssSelector(string.Empty), settings.Kind, settings.Timeout ?? ((WebPage)Page).WaitingTimeout, cancellationToken);
    }

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings) => WaitForElementAsync(settings, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, ((WebPage)Page).WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector) => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, kind, ((WebPage)Page).WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    public async ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var page = (WebPage)Page;
        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebShadowRootDomOperationStarting(page.TabId, "ожидание элемента", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return null;

        var hostElementId = await ResolveShadowHostElementIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(hostElementId))
            return null;

        var elementId = await bridge.WaitForElementAsync(CreateWaitForElementPayload(selector, kind, timeout, hostElementId), cancellationToken).ConfigureAwait(false);
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

        var page = (WebPage)Page;
        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebShadowRootDomOperationStarting(page.TabId, "поиск элемента", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return null;

        var hostElementId = await ResolveShadowHostElementIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(hostElementId))
            return null;

        var elementId = await bridge.FindElementAsync(CreateElementSearchPayload(selector, hostElementId), cancellationToken).ConfigureAwait(false);
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

        var page = (WebPage)Page;
        var selectorDescription = DescribeSelector(selector);
        page.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebShadowRootDomOperationStarting(page.TabId, "поиск элементов", selectorDescription);

        if (page.BridgeCommands is not { } bridge)
            return [];

        var hostElementId = await ResolveShadowHostElementIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(hostElementId))
            return [];

        var elementIds = await bridge.FindElementsAsync(CreateElementSearchPayload(selector, hostElementId), cancellationToken).ConfigureAwait(false);
        return CreateBridgeElements(elementIds);
    }

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector) => GetElementsAsync(selector, CancellationToken.None);

    public async ValueTask<IShadowRoot?> GetShadowRootAsync(string selector, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ((WebPage)Page).OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebShadowRootDomOperationStarting(((WebPage)Page).TabId, "поиск теневого корня", selector);
        var host = await GetElementAsync(selector, cancellationToken).ConfigureAwait(false);
        return host is null ? null : await host.GetShadowRootAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IShadowRoot?> GetShadowRootAsync(string selector) => GetShadowRootAsync(selector, CancellationToken.None);

    private JsonObject CreateElementSearchPayload(ElementSelector selector, string hostElementId)
        => new()
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["shadowHostElementId"] = hostElementId,
        };

    private JsonObject CreateWaitForElementPayload(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout, string hostElementId)
        => new()
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["kind"] = kind.ToString(),
            ["timeoutMs"] = timeout.TotalMilliseconds,
            ["shadowHostElementId"] = hostElementId,
        };

    private async ValueTask<string?> ResolveShadowHostElementIdAsync(CancellationToken cancellationToken)
    {
        var hostHandle = await Host.GetElementHandleAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(hostHandle) ? null : hostHandle;
    }

    private Element? CreateBridgeElement(string? elementId)
        => string.IsNullOrWhiteSpace(elementId) ? null : new Element((WebPage)Page, Frame, elementId);

    private Element[] CreateBridgeElements(string[] elementIds)
    {
        if (elementIds.Length == 0)
            return [];

        var elements = new Element[elementIds.Length];
        for (var index = 0; index < elementIds.Length; index++)
            elements[index] = new Element((WebPage)Page, Frame, elementIds[index]);

        return elements;
    }

    private static string DescribeSelector<TSelector>(TSelector selector)
        => selector?.ToString() ?? "<none>";
}