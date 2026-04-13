using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed class BridgeCommandClient(BridgeServer server)
{
    private Uri DiscoveryEndpoint { get; } = new(string.Concat("http://", server.Host, ":", server.Port.ToString(CultureInfo.InvariantCulture), "/"));

    internal Uri GetDiscoveryUrl() => DiscoveryEndpoint;

    public ValueTask<string> GetTitleAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.GetTitleAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string> GetUrlAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.GetUrlAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string> GetContentAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.GetContentAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string?> CaptureScreenshotAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.CaptureScreenshotAsync(sessionId, tabId, cancellationToken);

    public ValueTask NavigateAsync(string sessionId, string tabId, string rawTabId, string url, CancellationToken cancellationToken = default)
        => server.NavigateAsync(sessionId, tabId, rawTabId, url, cancellationToken);

    public ValueTask ReloadAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.ReloadAsync(sessionId, tabId, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string sessionId, string tabId, string script, CancellationToken cancellationToken = default)
        => server.ExecuteScriptAsync(sessionId, tabId, script, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string sessionId, string tabId, string script, string shadowHostElementId, CancellationToken cancellationToken = default)
        => server.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(
        string sessionId,
        string tabId,
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => server.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId, preferPageContextOnNull, forcePageContextExecution, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(
        string sessionId,
        string tabId,
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        string? elementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => server.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId, elementId, preferPageContextOnNull, forcePageContextExecution, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptInFramesAsync(
        string sessionId,
        string tabId,
        string script,
        bool isolatedWorld,
        bool includeMetadata,
        CancellationToken cancellationToken = default)
        => server.ExecuteScriptInFramesAsync(sessionId, tabId, script, isolatedWorld, includeMetadata, cancellationToken);

    public ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => server.OpenTabAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(string sessionId, string tabId, Point? position, CancellationToken cancellationToken = default)
        => server.OpenWindowAsync(sessionId, tabId, position, cancellationToken);

    public ValueTask SetCookieAsync(
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
        => server.SetCookieAsync(sessionId, tabId, contextId, name, value, domain, path, secure, httpOnly, expires, cancellationToken);

    public ValueTask<JsonElement> GetCookiesAsync(string sessionId, string tabId, string contextId, CancellationToken cancellationToken = default)
        => server.GetCookiesAsync(sessionId, tabId, contextId, cancellationToken);

    public ValueTask DeleteCookiesAsync(string sessionId, string tabId, string contextId, CancellationToken cancellationToken = default)
        => server.DeleteCookiesAsync(sessionId, tabId, contextId, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(string sessionId, string tabId, bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default)
        => server.SetRequestInterceptionAsync(sessionId, tabId, enabled, urlPatterns, cancellationToken);

    public ValueTask SetTabContextAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => server.SetTabContextAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask CloseWindowAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => server.CloseWindowAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask ActivateWindowAsync(string sessionId, string tabId, string windowId, CancellationToken cancellationToken = default)
        => server.ActivateWindowAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask ActivateTabAsync(string sessionId, string tabId, string targetTabId, CancellationToken cancellationToken = default)
        => server.ActivateTabAsync(sessionId, tabId, targetTabId, cancellationToken);

    public ValueTask<Rectangle> GetWindowBoundsAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.GetWindowBoundsAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string?> FindElementAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => server.FindElementAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string[]> FindElementsAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => server.FindElementsAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string?> WaitForElementAsync(string sessionId, string tabId, JsonObject payload, CancellationToken cancellationToken = default)
        => server.WaitForElementAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string?> GetElementPropertyAsync(string sessionId, string tabId, string elementId, string propertyName, CancellationToken cancellationToken = default)
        => server.GetElementPropertyAsync(sessionId, tabId, elementId, propertyName, cancellationToken);

    public ValueTask<bool> CheckShadowRootAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => server.CheckShadowRootAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask<PointF> ResolveElementScreenPointAsync(string sessionId, string tabId, string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => server.ResolveElementScreenPointAsync(sessionId, tabId, elementId, scrollIntoView, cancellationToken);

    public ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(string sessionId, string tabId, CancellationToken cancellationToken = default)
        => server.GetDebugPortStatusAsync(sessionId, tabId, cancellationToken);

    public ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => server.DescribeElementAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => server.TryDescribeElementAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask FocusElementAsync(string sessionId, string tabId, string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => server.FocusElementAsync(sessionId, tabId, elementId, scrollIntoView, cancellationToken);

    public ValueTask ScrollElementIntoViewAsync(string sessionId, string tabId, string elementId, CancellationToken cancellationToken = default)
        => server.ScrollElementIntoViewAsync(sessionId, tabId, elementId, cancellationToken);
}