namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebWindow
{
    public event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? DomContentLoaded;

    public event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? NavigationCompleted;

    public event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? PageLoaded;

    private async ValueTask OnBridgeEventReceivedAsync(Protocol.BridgeMessage message)
    {
        var page = FindPage(message.TabId);
        if (page is null)
        {
            return;
        }

        if (BridgeLifecycleEventMapper.TryRead(message, out var lifecycleEvent, out var url, out var title))
        {
            var args = new WebLifecycleEventArgs
            {
                Window = this,
                Page = page,
                Frame = page.MainFrame,
                Url = url,
                Title = title,
            };

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

            return;
        }

        if (BridgeEventPayloadReader.TryReadConsoleMessage(message, page.MainFrame, out var consoleArgs))
        {
            Console?.Invoke(this, consoleArgs);
            return;
        }

        if (BridgeEventPayloadReader.TryReadInterceptedRequest(message, page.MainFrame, out var requestArgs))
        {
            await InvokeAsync(Request, requestArgs).ConfigureAwait(false);
            return;
        }

        if (BridgeEventPayloadReader.TryReadInterceptedResponse(message, page.MainFrame, out var responseArgs))
        {
            await InvokeAsync(Response, responseArgs).ConfigureAwait(false);
        }
    }

    internal ValueTask DispatchSyntheticRequestInterceptionAsync(InterceptedRequestEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return InvokeAsync(Request, args);
    }

    internal ValueTask DispatchSyntheticResponseInterceptionAsync(InterceptedResponseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return InvokeAsync(Response, args);
    }

    private ValueTask InvokeAsync(AsyncEventHandler<IWebWindow, InterceptedRequestEventArgs>? handler, InterceptedRequestEventArgs args)
        => InvokeCoreAsync(handler, args);

    private ValueTask InvokeAsync(AsyncEventHandler<IWebWindow, InterceptedResponseEventArgs>? handler, InterceptedResponseEventArgs args)
        => InvokeCoreAsync(handler, args);

    private async ValueTask InvokeCoreAsync<TEventArgs>(AsyncEventHandler<IWebWindow, TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        foreach (var entry in handler.GetInvocationList())
        {
            await ((AsyncEventHandler<IWebWindow, TEventArgs>)entry)(this, args).ConfigureAwait(false);
        }
    }

    private WebPage? FindPage(string? tabId)
        => string.IsNullOrWhiteSpace(tabId)
            ? null
            : pages.FirstOrDefault(page => string.Equals(page.TabId, tabId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(page.BoundBridgeTabId) && IsMatchingBridgeTabId(page.BoundBridgeTabId, tabId)));

    private static bool IsMatchingBridgeTabId(string registeredTabId, string rawTabId)
        => string.Equals(registeredTabId, rawTabId, StringComparison.Ordinal)
            || registeredTabId.EndsWith(string.Concat(":", rawTabId), StringComparison.Ordinal);
}