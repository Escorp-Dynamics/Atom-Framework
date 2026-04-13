namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeDebugPortStatusPayload(
    int TabId,
    bool HasPort,
    int QueueLength,
    bool HasSocket,
    bool IsReady,
    bool InterceptEnabled,
    bool HasTabContext,
    string? ContextId,
    string? ContextUserAgent,
    bool HasBrowserTab,
    string? BrowserTabUrl,
    string? BrowserTabPendingUrl,
    string? BrowserTabStatus,
    string? RuntimeCheckStatus,
    string? RuntimeHref,
    string? RuntimeReadyState,
    string? RuntimeCheckError);