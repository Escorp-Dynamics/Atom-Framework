using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Канал команд одной вкладки, независимый от способа управления браузером
/// (мостовое расширение или протокол отладки).
/// </summary>
internal interface IPageCommandClient
{
    /// <summary>
    /// Служебный адрес discovery-страницы транспорта либо <see langword="null"/>, если у транспорта её нет.
    /// </summary>
    Uri? GetDiscoveryUrl();

    ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default);

    ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, CancellationToken cancellationToken = default);

    ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(Point? position, CancellationToken cancellationToken = default);

    ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default);

    ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default);

    ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken = default);

    ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default);

    ValueTask ReloadAsync(CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ExecuteScriptAsync(string script, string shadowHostElementId, CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        string? elementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ExecuteScriptInFramesAsync(
        string script,
        bool isolatedWorld,
        bool includeMetadata,
        CancellationToken cancellationToken = default);

    ValueTask SetCookieAsync(
        string contextId,
        string name,
        string value,
        string? domain,
        string? path,
        bool secure,
        bool httpOnly,
        long? expires,
        CancellationToken cancellationToken = default);

    ValueTask<JsonElement> GetCookiesAsync(string contextId, CancellationToken cancellationToken = default);

    ValueTask DeleteCookiesAsync(string contextId, CancellationToken cancellationToken = default);

    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default);

    ValueTask SetTabContextAsync(JsonObject payload, CancellationToken cancellationToken = default);

    ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken = default);

    ValueTask CloseTabAsync(CancellationToken cancellationToken = default);

    ValueTask ActivateWindowAsync(string windowId, CancellationToken cancellationToken = default);

    ValueTask ActivateTabAsync(string targetTabId, CancellationToken cancellationToken = default);

    ValueTask<Rectangle> GetWindowBoundsAsync(CancellationToken cancellationToken = default);

    ValueTask<string?> FindElementAsync(JsonObject payload, CancellationToken cancellationToken = default);

    ValueTask<string[]> FindElementsAsync(JsonObject payload, CancellationToken cancellationToken = default);

    ValueTask<string?> WaitForElementAsync(JsonObject payload, TimeSpan waitTimeout, CancellationToken cancellationToken = default);

    ValueTask<string?> GetElementPropertyAsync(string elementId, string propertyName, CancellationToken cancellationToken = default);

    ValueTask<bool> CheckShadowRootAsync(string elementId, CancellationToken cancellationToken = default);

    ValueTask<PointF> ResolveElementScreenPointAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default);

    ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(CancellationToken cancellationToken = default);

    ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string elementId, CancellationToken cancellationToken = default);

    ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string elementId, CancellationToken cancellationToken = default);

    ValueTask FocusElementAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default);

    ValueTask ScrollElementIntoViewAsync(string elementId, CancellationToken cancellationToken = default);
}
