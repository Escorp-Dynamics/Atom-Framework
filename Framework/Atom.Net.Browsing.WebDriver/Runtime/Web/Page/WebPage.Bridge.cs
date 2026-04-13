using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    internal PageBridgeCommandClient? BridgeCommands { get; private set; }

    internal string? BoundBridgeSessionId { get; private set; }

    internal string? BoundBridgeTabId { get; private set; }

    private string? BridgeContextId { get; set; }

    internal RequestInterceptionState? GetEffectiveRequestInterceptionState()
        => requestInterceptionState ?? OwnerWindow.GetEffectiveRequestInterceptionState();

    internal async ValueTask ApplyEffectiveRequestInterceptionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveState = GetEffectiveRequestInterceptionState();
        if (BridgeCommands is not { } bridge)
            return;

        if (RequestInterceptionState.AreEquivalent(appliedRequestInterceptionState, effectiveState))
            return;

        await bridge.SetRequestInterceptionAsync(
            effectiveState?.Enabled ?? false,
            effectiveState?.UrlPatterns,
            cancellationToken).ConfigureAwait(false);

        appliedRequestInterceptionState = effectiveState;
    }

    internal void BindBridgeCommands(string sessionId, BridgeCommandClient commands)
        => BindBridgeCommands(sessionId, TabId, commands);

    internal void BindBridgeCommands(string sessionId, string tabId, BridgeCommandClient commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentNullException.ThrowIfNull(commands);
        ThrowIfDisposed();

        BoundBridgeSessionId = sessionId;
        BoundBridgeTabId = tabId;
        BridgeCommands = new PageBridgeCommandClient(
            sessionId,
            tabId,
            commands,
            cancellationToken => commands.SetTabContextAsync(
                sessionId,
                tabId,
                WebBrowser.BuildSetTabContextPayload(this),
                cancellationToken),
            trackPendingNavigateUrl: SetPendingBridgeNavigationUrl);
        appliedRequestInterceptionState = null;
    }
}