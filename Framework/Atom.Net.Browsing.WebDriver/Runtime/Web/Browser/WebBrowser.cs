using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет браузер верхнего уровня и управляет окнами, страницами и общими событиями WebDriver-окружения.
/// </summary>
public sealed partial class WebBrowser : IWebBrowser
{
    private readonly ConcurrentStack<WebWindow> windows = [];
    private readonly ConcurrentQueue<BridgeMessage> bridgeEvents = [];
    private readonly Lock windowGate = new();
    private readonly bool ownsDisplay;
    private readonly BridgeServer? bridgeServer;
    private readonly string? bridgeSessionId;
    private readonly TimeSpan? bridgeOpenTimeout;
    private readonly CancellationTokenSource? bridgeBootstrapCancellation;
    private readonly Task<bool>? bridgeBootstrapTask;
    private RequestInterceptionState? requestInterceptionState;
    private TaskCompletionSource<VirtualMouse>? mouseResolutionSource;
    private TaskCompletionSource<VirtualKeyboard>? keyboardResolutionSource;
    private bool OwnsMouse { get; set; }
    private bool OwnsKeyboard { get; set; }
    private VirtualMouse? ResolvedMouse { get; set; }
    private VirtualKeyboard? ResolvedKeyboard { get; set; }
    private int disposeState;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Current window belongs to the windows stack and is disposed through it in reverse order.")]
    private WebWindow currentWindow;

    internal WebBrowser(
        WebBrowserSettings settings,
        string? materializedProfilePath,
        Process? browserProcess,
        VirtualDisplay? display,
        bool ownsDisplay,
        BridgeServer? bridgeServer = null,
        BridgeBootstrapPlan? bridgeBootstrap = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LaunchSettings = settings;
        this.materializedProfilePath = materializedProfilePath;
        this.browserProcess = browserProcess;
        Display = display;
        this.ownsDisplay = ownsDisplay;
        this.bridgeServer = bridgeServer;
        bridgeServer?.ConfigureNavigationProxyDecisions(ProxyNavigationDecisions);
        bridgeSessionId = bridgeBootstrap?.SessionId;
        bridgeOpenTimeout = bridgeBootstrap?.ConnectionTimeout;
        ResolvedMouse = settings.Mouse;
        ResolvedKeyboard = settings.Keyboard;
        var initialWindow = new WebWindow(this);
        windows.Push(initialWindow);
        Volatile.Write(ref currentWindow, initialWindow);

        if (bridgeServer is not null && bridgeBootstrap is not null && browserProcess is not null)
        {
            bridgeBootstrapCancellation = new CancellationTokenSource();
            bridgeBootstrapTask = BootstrapInitialBridgeSurfaceAsync(
                initialWindow,
                (WebPage)initialWindow.CurrentPage,
                bridgeBootstrap,
                bridgeBootstrapCancellation.Token);
        }

        if (bridgeServer is not null)
        {
            bridgeServer.RuntimeEventReceived += OnBridgeServerRuntimeEventReceived;
            bridgeServer.CallbackRequested += OnBridgeServerCallbackRequested;
            bridgeServer.RequestInterceptionRequested += OnBridgeServerRequestInterceptionRequested;
            bridgeServer.ResponseInterceptionRequested += OnBridgeServerResponseInterceptionRequested;
        }
    }

    internal WebBrowserSettings LaunchSettings { get; }

    internal ProxyNavigationDecisionRegistry ProxyNavigationDecisions { get; } = new();

    internal RequestInterceptionState? GetEffectiveRequestInterceptionState()
        => requestInterceptionState;

    internal VirtualDisplay? Display { get; }

    internal VirtualMouse? CurrentMouse => ResolvedMouse;

    internal VirtualKeyboard? CurrentKeyboard => ResolvedKeyboard;

    internal string? LastLinuxNativeWindowBoundsDiagnostics { get; private set; }

    [SupportedOSPlatform("linux")]
    internal Rectangle? TryGetLinuxNativeWindowBounds(Size? expectedSize = null, string? windowTitle = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            LastLinuxNativeWindowBoundsDiagnostics = "strategy=unsupported-os";
            return null;
        }

        if (Display is null)
        {
            LastLinuxNativeWindowBoundsDiagnostics = "strategy=no-display";
            return null;
        }

        if (browserProcess is null)
        {
            LastLinuxNativeWindowBoundsDiagnostics = "strategy=no-browser-process";
            return null;
        }

        var resolution = LinuxX11WindowDiscovery.ResolveTopLevelWindow(Display.Display, browserProcess.Id, expectedSize, windowTitle);
        LastLinuxNativeWindowBoundsDiagnostics = resolution.Diagnostics;
        return resolution.Bounds;
    }

    internal event Action<BridgeMessage>? BridgeEventReceived;

    internal void OnWindowDisposed(WebWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (windowGate)
        {
            if (disposeState != 0 || !ReferenceEquals(currentWindow, window))
            {
                return;
            }

            var nextWindow = windows.FirstOrDefault(static candidate => !candidate.IsDisposed);
            if (nextWindow is not null)
            {
                Volatile.Write(ref currentWindow, nextWindow);
            }
        }
    }

    internal void ActivateWindow(WebWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (windowGate)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(window.IsDisposed, window);

            if (!windows.Any(candidate => !candidate.IsDisposed && ReferenceEquals(candidate, window)))
                throw new InvalidOperationException("Окно не зарегистрировано среди живых окон браузера");

            Volatile.Write(ref currentWindow, window);
        }
    }

    internal bool TryDequeueBridgeEvent([NotNullWhen(true)] out BridgeMessage? message)
        => bridgeEvents.TryDequeue(out message);

    internal async ValueTask EnqueueBridgeEventAsync(BridgeMessage message, bool dispatchHandlers = true)
    {
        ArgumentNullException.ThrowIfNull(message);
        bridgeEvents.Enqueue(message);
        if (dispatchHandlers)
        {
            await OnBridgeEventReceivedAsync(message).ConfigureAwait(false);
        }

        BridgeEventReceived?.Invoke(message);
        LaunchSettings.Logger?.LogWebBrowserBridgeEventSynced(message.Event?.ToString() ?? message.Type.ToString(), message.TabId ?? "<none>");
    }

    public static ValueTask<WebBrowser> LaunchAsync(WebBrowserSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return LaunchCoreAsync(settings, cancellationToken);
    }

    public static ValueTask<WebBrowser> LaunchAsync(WebBrowserSettings settings)
        => LaunchAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public IEnumerable<IWebWindow> Windows => windows.Where(static window => !window.IsDisposed);

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => windows.Where(static window => !window.IsDisposed).SelectMany(static window => window.Pages);

    /// <inheritdoc/>
    public bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <inheritdoc/>
    public IWebWindow CurrentWindow => Volatile.Read(ref currentWindow);

    /// <inheritdoc/>
    public IWebPage CurrentPage => CurrentWindow.CurrentPage;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebBrowser, ConsoleMessageEventArgs>? Console;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowser, InterceptedRequestEventArgs>? Request;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowser, InterceptedResponseEventArgs>? Response;

    public async ValueTask DisposeAsync()
    {
        LaunchSettings.Logger?.LogWebBrowserDisposeStarting();

        List<WebWindow> windowsToDispose = [];

        lock (windowGate)
        {
            if (disposeState != 0)
                return;

            disposeState = 1;

            while (windows.TryPop(out var window))
            {
                windowsToDispose.Add(window);
            }
        }

        foreach (var window in windowsToDispose)
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }

        await DisposeBridgeBootstrapAsync().ConfigureAwait(false);
        await DisposeBrowserProcessAsync(browserProcess).ConfigureAwait(false);
        await DisposeBridgeServerAsync().ConfigureAwait(false);
        await DisposeOwnedInputDevicesAsync().ConfigureAwait(false);
        await DisposeOwnedDisplayAsync().ConfigureAwait(false);
        CleanupMaterializedProfile();
        LaunchSettings.Logger?.LogWebBrowserDisposeCompleted();
    }

    internal async ValueTask<bool> WaitForInitialBridgeBootstrapAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (((WebPage)CurrentPage).BridgeCommands is not null)
            return true;

        if (bridgeBootstrapTask is null)
            return false;

        return await bridgeBootstrapTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<VirtualMouse> ResolveMouseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ResolvedMouse is not null)
            return ResolvedMouse;

        while (true)
        {
            var resolutionSource = Volatile.Read(ref mouseResolutionSource);
            if (resolutionSource is null)
            {
                var createdSource = new TaskCompletionSource<VirtualMouse>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref mouseResolutionSource, createdSource, comparand: null) is null)
                {
                    return await CompleteMouseResolutionAsync(createdSource, cancellationToken).ConfigureAwait(false);
                }

                resolutionSource = Volatile.Read(ref mouseResolutionSource);
            }

            try
            {
                return await resolutionSource!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (resolutionSource.Task.IsCompleted)
            {
                Interlocked.CompareExchange(location1: ref mouseResolutionSource, value: null, comparand: resolutionSource);
                throw;
            }
        }
    }

    internal async ValueTask<VirtualKeyboard> ResolveKeyboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ResolvedKeyboard is not null)
            return ResolvedKeyboard;

        while (true)
        {
            var resolutionSource = Volatile.Read(ref keyboardResolutionSource);
            if (resolutionSource is null)
            {
                var createdSource = new TaskCompletionSource<VirtualKeyboard>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref keyboardResolutionSource, createdSource, comparand: null) is null)
                {
                    return await CompleteKeyboardResolutionAsync(createdSource, cancellationToken).ConfigureAwait(false);
                }

                resolutionSource = Volatile.Read(ref keyboardResolutionSource);
            }

            try
            {
                return await resolutionSource!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (resolutionSource.Task.IsCompleted)
            {
                Interlocked.CompareExchange(location1: ref keyboardResolutionSource, value: null, comparand: resolutionSource);
                throw;
            }
        }
    }

    private async ValueTask<VirtualMouse> CompleteMouseResolutionAsync(TaskCompletionSource<VirtualMouse> resolutionSource, CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfDisposed();

            if (ResolvedMouse is not null)
            {
                resolutionSource.TrySetResult(ResolvedMouse);
                return ResolvedMouse;
            }

            var resolvedMouse = await CreateVirtualMouseAsync(Display, cancellationToken).ConfigureAwait(false);
            ResolvedMouse = resolvedMouse;
            OwnsMouse = true;
            resolutionSource.TrySetResult(resolvedMouse);
            return resolvedMouse;
        }
        catch (OperationCanceledException ex)
        {
            resolutionSource.TrySetCanceled(ex.CancellationToken);
            Interlocked.CompareExchange(location1: ref mouseResolutionSource, value: null, comparand: resolutionSource);
            throw;
        }
        catch (Exception ex)
        {
            resolutionSource.TrySetException(ex);
            Interlocked.CompareExchange(location1: ref mouseResolutionSource, value: null, comparand: resolutionSource);
            throw;
        }
    }

    private async ValueTask<VirtualKeyboard> CompleteKeyboardResolutionAsync(TaskCompletionSource<VirtualKeyboard> resolutionSource, CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfDisposed();

            if (ResolvedKeyboard is not null)
            {
                resolutionSource.TrySetResult(ResolvedKeyboard);
                return ResolvedKeyboard;
            }

            var resolvedKeyboard = await CreateVirtualKeyboardAsync(Display, cancellationToken).ConfigureAwait(false);
            ResolvedKeyboard = resolvedKeyboard;
            OwnsKeyboard = true;
            resolutionSource.TrySetResult(resolvedKeyboard);
            return resolvedKeyboard;
        }
        catch (OperationCanceledException ex)
        {
            resolutionSource.TrySetCanceled(ex.CancellationToken);
            Interlocked.CompareExchange(location1: ref keyboardResolutionSource, value: null, comparand: resolutionSource);
            throw;
        }
        catch (Exception ex)
        {
            resolutionSource.TrySetException(ex);
            Interlocked.CompareExchange(location1: ref keyboardResolutionSource, value: null, comparand: resolutionSource);
            throw;
        }
    }

    private async ValueTask DisposeOwnedInputDevicesAsync()
    {
        var hadKeyboard = OwnsKeyboard && ResolvedKeyboard is not null;
        var hadMouse = OwnsMouse && ResolvedMouse is not null;

        if (OwnsKeyboard && ResolvedKeyboard is not null)
            await ResolvedKeyboard.DisposeAsync().ConfigureAwait(false);

        if (OwnsMouse && ResolvedMouse is not null)
            await ResolvedMouse.DisposeAsync().ConfigureAwait(false);

        if (hadMouse || hadKeyboard)
            LaunchSettings.Logger?.LogWebBrowserOwnedInputDisposed(hadMouse, hadKeyboard);
    }

    private async Task<bool> BootstrapInitialBridgeSurfaceAsync(
        WebWindow initialWindow,
        WebPage initialPage,
        BridgeBootstrapPlan bridgeBootstrap,
        CancellationToken cancellationToken)
    {
        if (bridgeServer is null)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(bridgeBootstrap.ConnectionTimeout);

        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();

                if (await TryBindInitialBridgeSurfaceAsync(initialWindow, initialPage, bridgeBootstrap).ConfigureAwait(false))
                    return true;

                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async ValueTask<bool> TryBindInitialBridgeSurfaceAsync(
        WebWindow initialWindow,
        WebPage initialPage,
        BridgeBootstrapPlan bridgeBootstrap)
    {
        if (bridgeServer is null)
            return false;

        var initialTab = await TryGetInitialRegisteredTabAsync(bridgeBootstrap.SessionId).ConfigureAwait(false);
        if (initialTab is null)
            return false;

        if (!initialWindow.IsDisposed && !string.IsNullOrWhiteSpace(initialTab.WindowId))
            initialWindow.BindBridgeWindowId(initialTab.WindowId);

        if (!initialPage.IsDisposed)
        {
            initialPage.BindBridgeCommands(bridgeBootstrap.SessionId, initialTab.TabId, bridgeServer.Commands);
            await ApplyBridgeTabContextAsync(initialPage, cancellationToken: default).ConfigureAwait(false);
            await initialPage.ApplyEffectiveRequestInterceptionAsync(default).ConfigureAwait(false);
        }

        return !initialWindow.IsDisposed && !initialPage.IsDisposed;
    }

    private async ValueTask<BridgeTabChannelSnapshot?> TryGetInitialRegisteredTabAsync(string sessionId)
    {
        if (bridgeServer is null)
            return null;

        var session = await bridgeServer.CreateSessionSnapshotAsync(sessionId).ConfigureAwait(false);
        if (session is not { IsConnected: true })
            return null;

        var tabs = await bridgeServer.GetTabsForSessionAsync(sessionId).ConfigureAwait(false);
        return tabs.FirstOrDefault(static tab => tab.IsRegistered);
    }

    private async ValueTask DisposeBridgeBootstrapAsync()
    {
        if (bridgeBootstrapCancellation is { } cancellation)
            await cancellation.CancelAsync().ConfigureAwait(false);

        if (bridgeBootstrapTask is null)
        {
            bridgeBootstrapCancellation?.Dispose();
            return;
        }

        try
        {
            await bridgeBootstrapTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Bootstrap task is best-effort during dispose; timed-out teardown is acceptable.
        }
        catch (OperationCanceledException)
        {
            // Dispose cancels bootstrap intentionally.
        }
        finally
        {
            bridgeBootstrapCancellation?.Dispose();
        }
    }

    private async ValueTask DisposeBridgeServerAsync()
    {
        if (bridgeServer is null)
            return;

        bridgeServer.RuntimeEventReceived -= OnBridgeServerRuntimeEventReceived;
        bridgeServer.CallbackRequested -= OnBridgeServerCallbackRequested;
        bridgeServer.RequestInterceptionRequested -= OnBridgeServerRequestInterceptionRequested;
        bridgeServer.ResponseInterceptionRequested -= OnBridgeServerResponseInterceptionRequested;
        await bridgeServer.DisposeAsync().ConfigureAwait(false);
    }

    private void OnBridgeServerRuntimeEventReceived(string sessionId, Protocol.BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(bridgeSessionId)
            || !string.Equals(sessionId, bridgeSessionId, StringComparison.Ordinal))
        {
            return;
        }

        _ = RelayBridgeServerRuntimeEventAsync(message);
    }

    private async Task RelayBridgeServerRuntimeEventAsync(Protocol.BridgeMessage message)
    {
        if (IsDisposed)
            return;

        var page = FindPage(message.TabId);
        if (page is null || page.IsDisposed)
            return;

        await page.ReceiveBridgeEventAsync(message).ConfigureAwait(false);
    }

    private async ValueTask DisposeOwnedDisplayAsync()
    {
        if (!OperatingSystem.IsLinux() || !ownsDisplay || Display is null)
            return;

        await Display.DisposeAsync().ConfigureAwait(false);
        LaunchSettings.Logger?.LogWebBrowserOwnedDisplayDisposed(Display.Display);
    }

    public ValueTask<IWebWindow> OpenWindowAsync(CancellationToken cancellationToken)
        => OpenWindowCoreAsync(settings: null, cancellationToken);

    public ValueTask<IWebWindow> OpenWindowAsync()
        => OpenWindowAsync(CancellationToken.None);

    public ValueTask<IWebWindow> OpenWindowAsync(WebWindowSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return OpenWindowCoreAsync(settings, cancellationToken);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public ValueTask<IWebWindow> OpenWindowAsync(WebWindowSettings settings)
        => OpenWindowAsync(settings, CancellationToken.None);

    internal void PublishOpenedWindow(WebWindow window)
    {
        lock (windowGate)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(window.IsDisposed, window);
            windows.Push(window);
            Volatile.Write(ref currentWindow, window);
        }
    }

    internal async ValueTask<IWebPage> OpenPageCoreAsync(WebWindow window, WebPageSettings? settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        var page = new WebPage(window, settings);

        try
        {
            if (TryGetBridgeSourcePageForWindow(window, out var sourcePage))
                await PrepareBridgePageAsync(sourcePage, window, page, cancellationToken).ConfigureAwait(false);

            window.PublishOpenedPage(page);
            return page;
        }
        catch
        {
            await page.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<IWebWindow> OpenWindowCoreAsync(WebWindowSettings? settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new WebWindow(this, settings);

        try
        {
            if (TryGetBridgeSourcePageForBrowser(out var sourcePage))
                await PrepareBridgeWindowAsync(sourcePage, window, cancellationToken).ConfigureAwait(false);

            PublishOpenedWindow(window);
            return window;
        }
        catch
        {
            await window.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private bool TryGetBridgeSourcePageForBrowser([NotNullWhen(true)] out WebPage? sourcePage)
    {
        sourcePage = GetBridgeCommandPage();
        return sourcePage is not null;
    }

    internal WebPage? GetBridgeCommandPage(WebWindow? excludedWindow = null)
    {
        if (bridgeServer is null || string.IsNullOrWhiteSpace(bridgeSessionId))
            return null;

        var livePages = Pages.OfType<WebPage>()
            .Where(static page => !page.IsDisposed && page.BridgeCommands is not null)
            .ToArray();

        if (excludedWindow is not null)
        {
            var nonExcludedPage = livePages.FirstOrDefault(page => !ReferenceEquals(page.OwnerWindow, excludedWindow));
            if (nonExcludedPage is not null)
                return nonExcludedPage;
        }

        return livePages.FirstOrDefault();
    }

    private bool TryGetBridgeSourcePageForWindow(WebWindow window, [NotNullWhen(true)] out WebPage? sourcePage)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (bridgeServer is null
            || string.IsNullOrWhiteSpace(bridgeSessionId)
            || string.IsNullOrWhiteSpace(window.BoundBridgeWindowId))
        {
            sourcePage = null;
            return false;
        }

        sourcePage = window.Pages.OfType<WebPage>().FirstOrDefault(static page => !page.IsDisposed && page.BridgeCommands is not null);
        return sourcePage is not null;
    }

    private async ValueTask PrepareBridgeWindowAsync(WebPage sourcePage, WebWindow window, CancellationToken cancellationToken)
    {
        var currentPage = (WebPage)window.CurrentPage;
        var (openedTabId, openedWindowId) = await sourcePage.BridgeCommands!.OpenWindowAsync(window.Settings?.Position, cancellationToken).ConfigureAwait(false);
        var registeredTab = await WaitForRegisteredTabAsync(openedTabId, cancellationToken).ConfigureAwait(false);
        window.BindBridgeWindowId(registeredTab.WindowId ?? openedWindowId);
        currentPage.BindBridgeCommands(bridgeSessionId!, registeredTab.TabId, bridgeServer!.Commands);
        await ApplyBridgeTabContextAsync(currentPage, cancellationToken).ConfigureAwait(false);
        await currentPage.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PrepareBridgePageAsync(WebPage sourcePage, WebWindow window, WebPage page, CancellationToken cancellationToken)
    {
        var bridgeWindowId = window.BoundBridgeWindowId
            ?? throw new InvalidOperationException("Bridge-backed OpenPageAsync requires a bound browser window identifier");
        var (openedTabId, _) = await sourcePage.BridgeCommands!.OpenTabAsync(bridgeWindowId, cancellationToken).ConfigureAwait(false);
        var registeredTab = await WaitForRegisteredTabAsync(openedTabId, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(registeredTab.WindowId))
            window.BindBridgeWindowId(registeredTab.WindowId);

        page.BindBridgeCommands(bridgeSessionId!, registeredTab.TabId, bridgeServer!.Commands);
        await ApplyBridgeTabContextAsync(page, cancellationToken).ConfigureAwait(false);
        await page.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static JsonObject BuildSetTabContextPayload(WebPage page)
    {
        var contextId = page.GetOrCreateBridgeContextId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new JsonObject
        {
            ["sessionId"] = page.BoundBridgeSessionId,
            ["contextId"] = contextId,
            ["tabId"] = page.BoundBridgeTabId,
            ["connectedAt"] = now,
            ["readyAt"] = now,
            ["isReady"] = true,
            ["navigationInterceptionMode"] = ResolveBridgeNavigationInterceptionMode(page, contextId),
        };

        if (ResolveBridgeNavigationProxyRouteToken(page, contextId) is { } navigationProxyRouteToken)
            payload["navigationProxyRouteToken"] = navigationProxyRouteToken;

        if (!string.IsNullOrWhiteSpace(page.OwnerWindow.BoundBridgeWindowId))
            payload["windowId"] = page.OwnerWindow.BoundBridgeWindowId;

        if (page.ResolveBridgeContextUrl() is { } bridgeContextUrl)
            payload["url"] = bridgeContextUrl.AbsoluteUri;

        payload["proxy"] = ResolveBridgeProxy(page) is { } proxy
            ? SerializeBridgeProxy(proxy, includeCredentials: true)
            : null;

        AppendDeviceContext(payload, page.ResolvedDevice);

        return payload;
    }

    private static string ResolveBridgeNavigationInterceptionMode(WebPage page, string contextId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);

        return page.OwnerWindow.OwnerBrowser.ProxyNavigationDecisions.TryResolveToken(contextId, out _)
            ? "proxy"
            : "webrequest";
    }

    private static string? ResolveBridgeNavigationProxyRouteToken(WebPage page, string contextId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);

        return page.OwnerWindow.OwnerBrowser.ProxyNavigationDecisions.TryResolveToken(contextId, out var routeToken)
            ? routeToken
            : null;
    }

    private static IWebProxy? ResolveBridgeProxy(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Settings?.UseProxy is false)
            return null;

        if (page.Settings?.Proxy is not null)
            return page.Settings.Proxy;

        return ResolveBridgeProxy(page.OwnerWindow);
    }

    private static IWebProxy? ResolveBridgeProxy(WebWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Settings?.UseProxy is false)
            return null;

        if (window.Settings?.Proxy is not null)
            return window.Settings.Proxy;

        return window.OwnerBrowser.LaunchSettings.Proxy;
    }

    private static string SerializeBridgeProxy(IWebProxy proxy, bool includeCredentials)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var proxyUri = proxy switch
        {
            WebProxy webProxy when webProxy.Address is not null => webProxy.Address,
            _ => ResolveBridgeProxyUri(proxy),
        };

        if (proxyUri is null || !proxyUri.IsAbsoluteUri)
            throw new NotSupportedException("Интерфейс IWebProxy должен возвращать абсолютный адрес прокси");

        var builder = new UriBuilder(proxyUri);
        if (!includeCredentials)
        {
            builder.UserName = string.Empty;
            builder.Password = string.Empty;
            return builder.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }

        if (proxy.Credentials is NetworkCredential credentials)
        {
            builder.UserName = credentials.UserName;
            builder.Password = credentials.Password;
        }

        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "A fixed probe URI is required to resolve IWebProxy implementations consistently.")]
    private static Uri? ResolveBridgeProxyUri(IWebProxy proxy)
    {
        var probeUri = new Uri("https://example.com", UriKind.Absolute);
        Uri? candidate;

        try
        {
            candidate = proxy.GetProxy(probeUri);
        }
        catch (NotImplementedException)
        {
            return null;
        }

        return candidate == probeUri ? null : candidate;
    }

    private static void AppendDeviceContext(JsonObject payload, Device? device)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (device is null)
            return;

        AppendOptionalString(payload, "userAgent", device.UserAgent);
        AppendOptionalString(payload, "platform", device.Platform);
        AppendOptionalString(payload, "locale", device.Locale);
        AppendOptionalString(payload, "timezone", device.Timezone);

        if (device.Languages is not null)
        {
            var languages = device.Languages
                .Where(static language => !string.IsNullOrWhiteSpace(language))
                .ToArray();

            if (languages.Length > 0)
                payload["languages"] = new JsonArray(languages.Select(static language => (JsonNode?)JsonValue.Create(language)).ToArray());
        }

        if (BuildClientHintsPayload(device.ClientHints) is { } clientHints)
            payload["clientHints"] = clientHints;

        if (!device.ViewportSize.IsEmpty)
        {
            payload["viewport"] = new JsonObject
            {
                ["width"] = device.ViewportSize.Width,
                ["height"] = device.ViewportSize.Height,
            };
        }

        if (device.DeviceScaleFactor > 0)
            payload["deviceScaleFactor"] = device.DeviceScaleFactor;

        if (device.HardwareConcurrency is { } hardwareConcurrency)
            payload["hardwareConcurrency"] = hardwareConcurrency;

        if (device.DeviceMemory is { } deviceMemory)
            payload["deviceMemory"] = deviceMemory;

        if (device.Geolocation is { } geolocation)
            payload["geolocation"] = BuildGeolocationPayload(geolocation);

        if (device.DoNotTrack is { } doNotTrack)
            payload["doNotTrack"] = doNotTrack;

        if (device.GlobalPrivacyControl is { } globalPrivacyControl)
            payload["globalPrivacyControl"] = globalPrivacyControl;

        payload["maxTouchPoints"] = device.MaxTouchPoints;
        payload["isMobile"] = device.IsMobile;
        payload["hasTouch"] = device.HasTouch;

        if (device.VirtualMediaDevices is { } virtualMediaDevices)
            payload["virtualMediaDevices"] = BuildVirtualMediaDevicesPayload(virtualMediaDevices);
    }

    private static void AppendOptionalString(JsonObject payload, string propertyName, string? value)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!string.IsNullOrWhiteSpace(value))
            payload[propertyName] = value;
    }

    private static JsonObject BuildGeolocationPayload(GeolocationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = new JsonObject
        {
            ["latitude"] = settings.Latitude,
            ["longitude"] = settings.Longitude,
        };

        if (settings.Accuracy is { } accuracy)
            payload["accuracy"] = accuracy;

        return payload;
    }

    private static JsonObject? BuildClientHintsPayload(ClientHintsSettings? settings)
    {
        if (settings is null)
            return null;

        var payload = new JsonObject();
        AppendOptionalString(payload, "platform", settings.Platform);
        AppendOptionalString(payload, "platformVersion", settings.PlatformVersion);
        AppendOptionalString(payload, "architecture", settings.Architecture);
        AppendOptionalString(payload, "model", settings.Model);
        AppendOptionalString(payload, "bitness", settings.Bitness);

        if (settings.Mobile is { } mobile)
            payload["mobile"] = mobile;

        if (BuildClientHintBrandArray(settings.Brands) is { } brands)
            payload["brands"] = brands;

        if (BuildClientHintBrandArray(settings.FullVersionList) is { } fullVersionList)
            payload["fullVersionList"] = fullVersionList;

        return payload.Count > 0 ? payload : null;
    }

    private static JsonArray? BuildClientHintBrandArray(IEnumerable<ClientHintBrand>? brands)
    {
        if (brands is null)
            return null;

        var items = brands
            .Where(static brand => !string.IsNullOrWhiteSpace(brand.Brand) && !string.IsNullOrWhiteSpace(brand.Version))
            .Select(static brand => (JsonNode)new JsonObject
            {
                ["brand"] = brand.Brand,
                ["version"] = brand.Version,
            })
            .ToArray();

        return items.Length == 0 ? null : new JsonArray(items);
    }

    private static JsonObject BuildVirtualMediaDevicesPayload(VirtualMediaDevicesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = new JsonObject
        {
            ["audioInputEnabled"] = settings.AudioInputEnabled,
            ["audioInputLabel"] = settings.AudioInputLabel,
            ["videoInputEnabled"] = settings.VideoInputEnabled,
            ["videoInputLabel"] = settings.VideoInputLabel,
            ["audioOutputEnabled"] = settings.AudioOutputEnabled,
            ["audioOutputLabel"] = settings.AudioOutputLabel,
        };

        if (!string.IsNullOrWhiteSpace(settings.AudioInputBrowserDeviceId))
            payload["audioInputBrowserDeviceId"] = settings.AudioInputBrowserDeviceId;

        if (!string.IsNullOrWhiteSpace(settings.VideoInputBrowserDeviceId))
            payload["videoInputBrowserDeviceId"] = settings.VideoInputBrowserDeviceId;

        if (!string.IsNullOrWhiteSpace(settings.GroupId))
            payload["groupId"] = settings.GroupId;

        return payload;
    }

    private static async ValueTask ApplyBridgeTabContextAsync(WebPage page, CancellationToken cancellationToken)
    {
        var bridgeCommands = page.BridgeCommands
            ?? throw new InvalidOperationException("Bridge-backed page is not bound to command transport");

        await bridgeCommands.SetTabContextAsync(BuildSetTabContextPayload(page), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BridgeTabChannelSnapshot> WaitForRegisteredTabAsync(string rawTabId, CancellationToken cancellationToken)
    {
        if (bridgeServer is null || string.IsNullOrWhiteSpace(bridgeSessionId))
            throw new InvalidOperationException("Bridge-backed open requires an active bridge session");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(bridgeOpenTimeout ?? TimeSpan.FromSeconds(30));

        try
        {
            while (true)
            {
                var tab = await TryGetRegisteredTabAsync(bridgeSessionId, rawTabId).ConfigureAwait(false);
                if (tab is not null)
                    return tab;

                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Bridge-backed open did not register tab '{rawTabId}' before timeout");
        }
    }

    private async ValueTask<BridgeTabChannelSnapshot?> TryGetRegisteredTabAsync(string sessionId, string rawTabId)
    {
        if (bridgeServer is null)
            return null;

        var session = await bridgeServer.CreateSessionSnapshotAsync(sessionId).ConfigureAwait(false);
        if (session is not { IsConnected: true })
            return null;

        var tabs = await bridgeServer.GetTabsForSessionAsync(sessionId).ConfigureAwait(false);
        return tabs.FirstOrDefault(tab => tab.IsRegistered && IsMatchingBridgeTabId(tab.TabId, rawTabId));
    }

    private static bool IsMatchingBridgeTabId(string registeredTabId, string rawTabId)
        => string.Equals(registeredTabId, rawTabId, StringComparison.Ordinal)
            || registeredTabId.EndsWith(string.Concat(":", rawTabId), StringComparison.Ordinal);

    public async ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebWindow[] windowsSnapshot;
        lock (windowGate)
        {
            ThrowIfDisposed();
            windowsSnapshot = windows.Where(static window => !window.IsDisposed).ToArray();
        }

        foreach (var window in windowsSnapshot)
        {
            await window.ClearAllCookiesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask ClearAllCookiesAsync()
        => ClearAllCookiesAsync(CancellationToken.None);

    public async ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebPage[] pagesSnapshot;
        lock (windowGate)
        {
            ThrowIfDisposed();
            requestInterceptionState = RequestInterceptionState.Create(enabled, urlPatterns);
            pagesSnapshot = windows
                .Where(static window => !window.IsDisposed)
                .SelectMany(static window => window.Pages)
                .OfType<WebPage>()
                .Where(static page => !page.IsDisposed)
                .ToArray();
        }

        foreach (var page in pagesSnapshot)
        {
            await page.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns)
        => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    public ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken)
        => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(bool enabled)
        => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings(), cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url)
        => NavigateAsync(url, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Kind = kind }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind)
        => NavigateAsync(url, kind, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Headers = headers }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers)
        => NavigateAsync(url, headers, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Body = body }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body)
        => NavigateAsync(url, body, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Html = html }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html)
        => NavigateAsync(url, html, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        return CurrentWindow.NavigateAsync(url, settings, cancellationToken);
    }

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings)
        => NavigateAsync(url, settings, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return CurrentWindow.ReloadAsync(cancellationToken);
    }

    public ValueTask<HttpsResponseMessage> ReloadAsync()
        => ReloadAsync(CancellationToken.None);

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfDisposed();
        return currentWindow.AttachVirtualCameraAsync(camera, cancellationToken);
    }

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera)
        => AttachVirtualCameraAsync(camera, CancellationToken.None);

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ThrowIfDisposed();
        return currentWindow.AttachVirtualMicrophoneAsync(microphone, cancellationToken);
    }

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone)
        => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    public ValueTask<IWebWindow?> GetWindowAsync(string name, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetWindowByNameAsync(name, cancellationToken);
    }

    public ValueTask<IWebWindow?> GetWindowAsync(string name)
        => GetWindowAsync(name, CancellationToken.None);

    public ValueTask<IWebWindow?> GetWindowAsync(Uri url, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetWindowByUrlAsync(url, cancellationToken);
    }

    public ValueTask<IWebWindow?> GetWindowAsync(Uri url)
        => GetWindowAsync(url, CancellationToken.None);

    public ValueTask<IWebWindow?> GetWindowAsync(IElement element, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return ValueTask.FromResult<IWebWindow?>(ReferenceEquals(element.Page.Window.Browser, this) ? element.Page.Window : null);
    }

    public ValueTask<IWebWindow?> GetWindowAsync(IElement element)
        => GetWindowAsync(element, CancellationToken.None);

    public ValueTask<IWebPage?> GetPageAsync(string name, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetPageByNameAsync(name, cancellationToken);
    }

    public ValueTask<IWebPage?> GetPageAsync(string name)
        => GetPageAsync(name, CancellationToken.None);

    public ValueTask<IWebPage?> GetPageAsync(Uri url, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetPageByUrlAsync(url, cancellationToken);
    }

    public ValueTask<IWebPage?> GetPageAsync(Uri url)
        => GetPageAsync(url, CancellationToken.None);

    public ValueTask<IWebPage?> GetPageAsync(IElement element, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return ValueTask.FromResult<IWebPage?>(ReferenceEquals(element.Page.Window.Browser, this) ? element.Page : null);
    }

    public ValueTask<IWebPage?> GetPageAsync(IElement element)
        => GetPageAsync(element, CancellationToken.None);
}