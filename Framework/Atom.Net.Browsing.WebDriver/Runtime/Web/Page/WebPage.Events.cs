using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    public event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? DomContentLoaded;

    public event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? NavigationCompleted;

    public event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? PageLoaded;

    private async ValueTask OnBridgeEventReceivedAsync(Protocol.BridgeMessage message)
    {
        if (TryHandleLifecycleEvent(message))
            return;

        if (TryHandleFrameDetached(message))
            return;

        if (TryHandleConsoleMessage(message))
            return;

        if (TryHandleScriptError(message))
            return;

        if (await TryHandleCallbackEventAsync(message).ConfigureAwait(false))
            return;

        await TryHandleNetworkEventAsync(message).ConfigureAwait(false);
    }

    private bool TryHandleLifecycleEvent(Protocol.BridgeMessage message)
    {
        if (!BridgeLifecycleEventMapper.TryRead(message, out var lifecycleEvent, out var url, out var title))
            return false;

        if (ShouldApplyLiveLifecycleSnapshot(message, url))
            navigationTransport.ApplyLiveLifecycleSnapshot(url, title);

        var args = new WebLifecycleEventArgs
        {
            Window = OwnerWindow,
            Page = this,
            Frame = mainFrame,
            Url = url,
            Title = title,
        };

        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageLifecycleEventReceived(TabId, lifecycleEvent.ToString(), url?.ToString() ?? "<none>");
        InvokeLifecycle(lifecycleEvent, args);
        mainFrame.InvokeLifecycle(lifecycleEvent, args);
        return true;
    }

    private bool ShouldApplyLiveLifecycleSnapshot(Protocol.BridgeMessage message, Uri? url)
    {
        if (IsTransportLifecyclePayload(message))
            return false;

        if (BridgeCommands is null && !HasHrefLifecyclePayload(message))
            return false;

        if (url is not null
            && CurrentUrl is not null
            && BridgeCommands is { } bridgeCommands
            && url == bridgeCommands.GetDiscoveryUrl()
            && CurrentUrl != url)
        {
            return false;
        }

        return true;
    }

    private static bool HasHrefLifecyclePayload(Protocol.BridgeMessage message)
        => message.Payload is JsonElement payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("href", out var href)
            && href.ValueKind == JsonValueKind.String;

    private static bool IsTransportLifecyclePayload(Protocol.BridgeMessage message)
        => message.Payload is JsonElement payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("snapshotSource", out var source)
            && source.ValueKind == JsonValueKind.String
            && string.Equals(source.GetString(), "transport", StringComparison.Ordinal);

    private bool TryHandleFrameDetached(Protocol.BridgeMessage message)
    {
        if (!BridgeEventPayloadReader.TryReadFrameDetached(message, out var frameElementId))
            return false;

        MarkFrameElementDetached(frameElementId);
        return true;
    }

    private bool TryHandleConsoleMessage(Protocol.BridgeMessage message)
    {
        if (!BridgeEventPayloadReader.TryReadConsoleMessage(message, mainFrame, out var consoleArgs))
            return false;

        Console?.Invoke(this, consoleArgs);
        return true;
    }

    private bool TryHandleScriptError(Protocol.BridgeMessage message)
    {
        if (!BridgeEventPayloadReader.TryReadScriptError(message, out var scriptErrorDetails))
            return false;

        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageScriptErrorReceived(TabId, scriptErrorDetails);
        return true;
    }

    private async ValueTask<bool> TryHandleCallbackEventAsync(Protocol.BridgeMessage message)
    {
        if (BridgeEventPayloadReader.TryReadCallback(message, out var callbackArgs))
        {
            if (callbackSubscriptions.ContainsKey(callbackArgs.Name))
                await InvokeAsync(Callback, callbackArgs).ConfigureAwait(false);

            return true;
        }

        if (!BridgeEventPayloadReader.TryReadCallbackFinalized(message, out var finalizedArgs))
            return false;

        if (callbackSubscriptions.ContainsKey(finalizedArgs.Name))
            CallbackFinalized?.Invoke(this, finalizedArgs);

        return true;
    }

    private async ValueTask<bool> TryHandleNetworkEventAsync(Protocol.BridgeMessage message)
    {
        if (BridgeEventPayloadReader.TryReadInterceptedRequest(message, mainFrame, out var requestArgs))
        {
            await InvokeAsync(Request, requestArgs).ConfigureAwait(false);
            return true;
        }

        if (!BridgeEventPayloadReader.TryReadInterceptedResponse(message, mainFrame, out var responseArgs))
            return false;

        await InvokeAsync(Response, responseArgs).ConfigureAwait(false);
        return true;
    }

    internal async ValueTask DispatchSyntheticRequestInterceptionAsync(InterceptedRequestEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        await InvokeAsync(Request, args).ConfigureAwait(false);
    }

    internal async ValueTask DispatchSyntheticResponseInterceptionAsync(InterceptedResponseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        await InvokeAsync(Response, args).ConfigureAwait(false);
    }

    internal async ValueTask<CallbackDecision> DispatchSyntheticCallbackAsync(CallbackEventArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (callbackSubscriptions.ContainsKey(args.Name))
            await InvokeAsync(Callback, args).ConfigureAwait(false);

        args.SetDefaultIfPending();
        return await args.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask InvokeAsync<TEventArgs>(AsyncEventHandler<IWebPage, TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs
        => InvokeCoreAsync(handler, args);

    private async ValueTask InvokeCoreAsync<TEventArgs>(AsyncEventHandler<IWebPage, TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        foreach (var entry in handler.GetInvocationList())
        {
            await ((AsyncEventHandler<IWebPage, TEventArgs>)entry)(this, args).ConfigureAwait(false);
        }
    }

    private void InvokeLifecycle(Protocol.BridgeEvent lifecycleEvent, WebLifecycleEventArgs args)
    {
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
}