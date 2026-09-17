using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
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
    /// <summary>
    /// Верхняя граница буфера мостовых событий на уровне браузера. Это самый долгоживущий
    /// буфер (живёт весь сеанс браузера) и он не дренируется потребителем, поэтому без
    /// ограничения он накапливал бы все события навигации и перехвата с их payload.
    /// </summary>
    private const int MaxBufferedBridgeEvents = 4096;

    private readonly ConcurrentStack<WebWindow> windows = [];

    /// <summary>Счётчик созданных окон: индекс назначается окну при создании.</summary>
    private int windowIndexCounter;

    /// <summary>Выдаёт порядковый индекс нового окна — он же индекс в каскадной раскладке.</summary>
    internal int AllocateWindowIndex() => Interlocked.Increment(ref windowIndexCounter) - 1;
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
    private readonly SemaphoreSlim touchscreenResolutionGate = new(1, 1);
    private bool OwnsMouse { get; set; }
    private bool OwnsKeyboard { get; set; }
    private VirtualMouse? ResolvedMouse { get; set; }
    private VirtualKeyboard? ResolvedKeyboard { get; set; }
    private Atom.Hardware.Input.Touch.VirtualTouchscreen? ResolvedTouchscreen { get; set; }
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
        publishedManagedPolicyPath = bridgeBootstrap?.ManagedPolicyPublishPath;
        publishedExtensionId = bridgeBootstrap?.ExtensionId;
        localExtensionPath = bridgeBootstrap?.LocalExtensionPath;
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
    internal Rectangle? TryGetLinuxNativeWindowBounds(Size? expectedSize = null, string? windowTitle = null, int windowIndex = 0)
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

        // У собственного композитора геометрия окна известна напрямую — обходить дерево окон не нужно.
        // При N окнах каждое имеет СВОЮ позицию: индекс окна — ключ к его геометрии.
        if (Display.Session is { } session)
            return session.GetWindowBoundsByIndex(ResolveCompositorWindowIndex(session, windowTitle, windowIndex));

        var resolution = LinuxX11WindowDiscovery.ResolveTopLevelWindow(Display.Display, browserProcess.Id, expectedSize, windowTitle);
        LastLinuxNativeWindowBoundsDiagnostics = resolution.Diagnostics;
        return resolution.Bounds;
    }

    /// <summary>
    /// Переводит номер окна драйвера в номер окна композитора.
    /// </summary>
    /// <remarks>
    /// ★ Счётчики у драйвера и композитора свои, и совпадают они лишь пока каждому окну драйвера
    /// отвечает ровно одно окно на экране. Заголовок известен обеим сторонам и разрешает номер
    /// точно; счётчик остаётся запасным вариантом, пока заголовок ещё не заявлен клиентом.
    /// </remarks>
    [SupportedOSPlatform("linux")]
    private int ResolveCompositorWindowIndex(Atom.Display.WaylandDisplaySession session, string? windowTitle, int windowIndex)
    {
        if (session.TryResolveWindowIndexByTitle(windowTitle) is { } resolved)
        {
            LastLinuxNativeWindowBoundsDiagnostics = "strategy=compositor-title";
            return resolved;
        }

        LastLinuxNativeWindowBoundsDiagnostics = "strategy=compositor-counter";
        return windowIndex;
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
        while (bridgeEvents.Count > MaxBufferedBridgeEvents && bridgeEvents.TryDequeue(out _))
        {
            // Вытесняем самые старые события: свежие важнее для потребителя.
        }

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

        bridgeEvents.Clear();
        await DisposeBridgeBootstrapAsync().ConfigureAwait(false);
        await DisposeBrowserProcessAsync(browserProcess).ConfigureAwait(false);
        await DisposeBridgeServerAsync().ConfigureAwait(false);
        await DisposeOwnedInputDevicesAsync().ConfigureAwait(false);
        await DisposeOwnedDisplayAsync().ConfigureAwait(false);
        CleanupMaterializedProfile();
        CleanupPublishedManagedPolicy();
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
                // Кандидат создаётся ЛОКАЛЬНО и публикуется атомарно через CompareExchange.
                // Использовать LazyInitializer.EnsureInitialized здесь нельзя: он сам публикует
                // источник в поле до CompareExchange, из-за чего сравнение с null никогда не
                // срабатывало, ветка CompleteMouseResolutionAsync становилась недостижимой, и
                // ожидание висло на никем не завершаемом TaskCompletionSource.
#pragma warning disable MA0173 // Use LazyInitializer.EnsureInitialize
                var createdSource = new TaskCompletionSource<VirtualMouse>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref mouseResolutionSource, createdSource, comparand: null) is null)
                {
                    return await CompleteMouseResolutionAsync(createdSource, cancellationToken).ConfigureAwait(false);
                }
#pragma warning restore MA0173 // Use LazyInitializer.EnsureInitialize

                resolutionSource = Volatile.Read(ref mouseResolutionSource);
            }

            try
            {
                return await resolutionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                // См. комментарий в ResolveMouseAsync: кандидат создаётся локально и публикуется
                // атомарно; LazyInitializer.EnsureInitialized сделал бы ветку завершения
                // недостижимой и приводил к зависанию.
#pragma warning disable MA0173 // Use LazyInitializer.EnsureInitialize
                var createdSource = new TaskCompletionSource<VirtualKeyboard>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref keyboardResolutionSource, createdSource, comparand: null) is null)
                {
                    return await CompleteKeyboardResolutionAsync(createdSource, cancellationToken).ConfigureAwait(false);
                }
#pragma warning restore MA0173 // Use LazyInitializer.EnsureInitialize

                resolutionSource = Volatile.Read(ref keyboardResolutionSource);
            }

            try
            {
                return await resolutionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Выдаёт виртуальный тачскрин, создавая его при первом обращении.
    /// </summary>
    /// <remarks>
    /// Устройство одно на браузер и живёт до его закрытия: пересоздавать его на задачу нельзя —
    /// каждое создание проходит через udev и стоит десятки миллисекунд, а видимых различий между
    /// экземплярами нет. Размер матрицы берётся у дисплея: касание адресуется абсолютными
    /// экранными координатами, как и XTEST-клик.
    /// </remarks>
    internal async ValueTask<Atom.Hardware.Input.Touch.VirtualTouchscreen> ResolveTouchscreenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ResolvedTouchscreen is { } existing)
            return existing;

        await touchscreenResolutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (ResolvedTouchscreen is { } created)
                return created;

            // Тачскрин существует только на Linux, там же живёт и виртуальный дисплей.
            var screenSize = OperatingSystem.IsLinux() && Display is { } display
                ? display.Resolution
                : new System.Drawing.Size(1920, 1080);

            // На собственном композиторе касание идёт в протокол, а не через устройство ядра.
            var touchscreen = OperatingSystem.IsLinux() && Display?.Session is { } touchSession
                ? await Atom.Hardware.Input.Touch.VirtualTouchscreen.CreateForSessionAsync(
                    touchSession,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : await Atom.Hardware.Input.Touch.VirtualTouchscreen.CreateAsync(
                    new Atom.Hardware.Input.Touch.VirtualTouchscreenSettings { ScreenSize = screenSize },
                    cancellationToken).ConfigureAwait(false);

            ResolvedTouchscreen = touchscreen;
            return touchscreen;
        }
        finally
        {
            touchscreenResolutionGate.Release();
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

        // Тачскрин всегда наш: снаружи его не передают, поэтому и владение безусловное.
        if (ResolvedTouchscreen is { } touchscreen)
        {
            await touchscreen.DisposeAsync().ConfigureAwait(false);
            ResolvedTouchscreen = null;
        }

        touchscreenResolutionGate.Dispose();

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
            await ApplyBridgeTabContextAsync(initialPage, CancellationToken.None).ConfigureAwait(false);
            await initialPage.ApplyEffectiveRequestInterceptionAsync(CancellationToken.None).ConfigureAwait(false);
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
            await bridgeBootstrapTask.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
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

    private void OnBridgeServerRuntimeEventReceived(string sessionId, BridgeMessage message)
    {
        if (string.IsNullOrWhiteSpace(bridgeSessionId)
            || !string.Equals(sessionId, bridgeSessionId, StringComparison.Ordinal))
        {
            return;
        }

        // Ретрансляция намеренно не ожидается — приём событий моста не должен блокироваться
        // обработчиками пользователя. Но брошенное исключение обязано быть замечено: раньше задача
        // отбрасывалась целиком, и любой сбой доставки (например падение при разборе полезной
        // нагрузки) исчезал бесследно — подписка просто не срабатывала, без ошибки и без записи.
        _ = ObserveBridgeEventRelayAsync(message);
    }

    private async Task ObserveBridgeEventRelayAsync(BridgeMessage message)
    {
        try
        {
            await RelayBridgeServerRuntimeEventAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LaunchSettings.Logger?.LogWebBrowserBridgeEventRelayFailed(
                exception,
                message.Event?.ToString() ?? "<нет>",
                message.TabId ?? "<нет>");
        }
    }

    private async Task RelayBridgeServerRuntimeEventAsync(BridgeMessage message)
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
        var position = window.Settings?.Position;
        var (openedTabId, openedWindowId) = await OpenBridgeSurfaceAsync(
            sourcePage,
            (commands, token) => commands.OpenWindowAsync(position, token),
            cancellationToken).ConfigureAwait(false);
        var registeredTab = await WaitForRegisteredTabAsync(openedTabId, cancellationToken).ConfigureAwait(false);
        window.BindBridgeWindowId(registeredTab.WindowId ?? openedWindowId);
        currentPage.BindBridgeCommands(bridgeSessionId!, registeredTab.TabId, bridgeServer!.Commands);
        await ApplyBridgeTabContextAsync(currentPage, cancellationToken).ConfigureAwait(false);
        await currentPage.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
    }


    // Порт контент-скрипта отключается при КАЖДОЙ навигации, и на это время вкладка снята с
    // регистрации. Команда, отправленная в это окно, падает с «вкладка-отключена». Пути навигации
    // и применения перехвата это уже терпят и повторяют; открытие окна и вкладки — не терпело, и
    // под нагрузкой роняло вызов пользователю. Повторяем ограниченно, каждый раз заново выбирая
    // страницу-отправитель: исходная могла и не восстановиться, а мост держит любая живая.
    private async ValueTask<(string TabId, string? WindowId)> OpenBridgeSurfaceAsync(
        WebPage preferredSourcePage,
        Func<PageBridgeCommandClient, CancellationToken, ValueTask<(string TabId, string? WindowId)>> open,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + BridgeSurfaceRecoveryBudget;
        var sourcePage = preferredSourcePage;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourcePage.BridgeCommands is { } commands)
            {
                try
                {
                    return await open(commands, cancellationToken).ConfigureAwait(false);
                }
                catch (Protocol.BridgeCommandException exception)
                    when (Protocol.BridgeCommandException.IsSurfaceDisconnect(exception) && DateTime.UtcNow < deadline)
                {
                    // Ожидаемое окно перерегистрации — пробуем снова.
                }
            }
            else if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("Мостовая поверхность недоступна для открытия окна или вкладки");
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            sourcePage = GetBridgeCommandPage() ?? sourcePage;
        }
    }

    private static readonly TimeSpan BridgeSurfaceRecoveryBudget = TimeSpan.FromSeconds(5);

    private async ValueTask PrepareBridgePageAsync(WebPage sourcePage, WebWindow window, WebPage page, CancellationToken cancellationToken)
    {
        var bridgeWindowId = window.BoundBridgeWindowId
            ?? throw new InvalidOperationException("Bridge-backed OpenPageAsync requires a bound browser window identifier");
        var (openedTabId, _) = await OpenBridgeSurfaceAsync(
            sourcePage,
            (commands, token) => commands.OpenTabAsync(bridgeWindowId, token),
            cancellationToken).ConfigureAwait(false);
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

        var browser = page.OwnerWindow.OwnerBrowser;

        // Proxy-режим активируется только для страниц с включённым перехватом запросов:
        // именно тогда навигационные fulfill/abort решаются мостом, и только тогда main_frame
        // гарантированно получает отложенное решение. Остальные страницы остаются на webrequest,
        // чтобы их навигации не проходили через локальный MITM-прокси без необходимости.
        if (page.GetEffectiveRequestInterceptionState()?.Enabled == true)
        {
            browser.EnsureBridgeNavigationProxyRoute(page, contextId);
        }
        else if (browser.ProxyNavigationDecisions.TryResolveToken(contextId, out var existingToken)
            && existingToken.StartsWith(AutoNavigationProxyRouteTokenPrefix, StringComparison.Ordinal))
        {
            // Перехват выключен: снимаем только маршруты, созданные автоматически
            // (внешние маршруты с пользовательскими токенами не трогаем).
            browser.ProxyNavigationDecisions.RemoveRouteByContextId(contextId);
        }

        return browser.ProxyNavigationDecisions.TryResolveToken(contextId, out _)
            ? "proxy"
            : "webrequest";
    }

    private void EnsureBridgeNavigationProxyRoute(WebPage page, string contextId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);

        if (bridgeServer is null
            || bridgeServer.NavigationProxyPort <= 0
            || string.IsNullOrWhiteSpace(bridgeSessionId)
            || string.IsNullOrWhiteSpace(contextId))
        {
            return;
        }

        if (ProxyNavigationDecisions.TryResolveToken(contextId, out _))
            return;

        ProxyNavigationDecisions.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = bridgeSessionId,
            TabId = page.BoundBridgeTabId ?? page.TabId,
            ContextId = contextId,
            RouteToken = CreateBridgeNavigationProxyRouteToken(),
            UpstreamProxy = ResolveBridgeNavigationProxyUpstream(page),
            ForwardProfile = ResolveTaskForwardProfile(page),
            Revision = 1,
        });
    }

    /// <summary>
    /// TLS-профиль переотправки по личности задачи вкладки.
    /// </summary>
    /// <remarks>
    /// Профиль задачи кладётся в <see cref="WebPage.BridgeTaskForwardProfile"/> при переприменении
    /// контекста; здесь он просто переносится в маршрут, чтобы nav-proxy переотправлял запросы
    /// этой задачи отпечатком, согласованным с заявленной вкладкой личностью, а не с профилем
    /// запуска браузера.
    /// </remarks>
    private static Atom.Net.Https.Profiles.BrowserProfile? ResolveTaskForwardProfile(WebPage page)
    {
        if (page.BridgeTaskForwardProfile is { } taskProfile)
            return taskProfile;

        var userAgent = page.ResolvedDevice?.UserAgent;
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        // Резолвер отдаёт готовый профиль СО СВОЕЙ строкой агента; без подмены на провод уехал бы
        // браузер каталога, тогда как вкладка заявляет строку устройства.
        var resolved = Atom.Net.Https.Profiles.BrowserProfileResolver.Resolve(userAgent);

        return resolved with
        {
            UserAgent = userAgent,
            IsMobile = page.ResolvedDevice?.IsMobile ?? resolved.IsMobile,
        };
    }

    // Префикс помечает маршруты, зарегистрированные автоматически при включении перехвата:
    // такие маршруты снимаются при его выключении, а зарегистрированные снаружи — сохраняются.
    private const string AutoNavigationProxyRouteTokenPrefix = "nav-";

    // Случайный некороткий токен: он же — учётный параметр локального прокси (username
    // в Proxy-Authorization), поэтому должен быть непредсказуем для постороннего контента вкладки.
    private static string CreateBridgeNavigationProxyRouteToken()
        => string.Concat(AutoNavigationProxyRouteTokenPrefix, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant());

    private static string? ResolveBridgeNavigationProxyUpstream(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var proxy = ResolveBridgeProxy(page);
        if (proxy is null)
            return null;

        try
        {
            // Типовой случай — WebProxy с фиксированным адресом; для остальных IWebProxy
            // берём адрес, который вернулся бы для условной навигационной цели.
            var address = proxy is WebProxy webProxy
                ? webProxy.Address
                : proxy.GetProxy(new UriBuilder { Scheme = Uri.UriSchemeHttps, Host = "upstream.invalid", Path = "/" }.Uri);

            if (address is not { IsAbsoluteUri: true })
                return null;

            if (!string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (proxy.Credentials is NetworkCredential credential && !string.IsNullOrEmpty(credential.UserName))
            {
                var builder = new UriBuilder(address)
                {
                    UserName = credential.UserName,
                    Password = credential.Password ?? string.Empty,
                };
                return builder.Uri.AbsoluteUri;
            }

            return address.AbsoluteUri;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
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

    /// <summary>
    /// Собирает поля профиля для контекста вкладки.
    /// </summary>
    /// <remarks>
    /// Метод сделан internal, чтобы РАННИЙ скрипт личности (`identity.profile.js`, пишется при
    /// материализации расширения) получал ровно тот же состав полей, что и мостовой контекст.
    /// Иначе профиль запуска и профиль, приходящий по `SetTabContext`, разъехались бы, а
    /// расхождение личности внутри одного документа — самостоятельный признак подделки.
    /// </remarks>
    internal static void AppendDeviceContext(JsonObject payload, Device? device)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (device is null)
            return;

        // Рубильник сравнительного замера: отключает ОС-зависимые подмены (шрифты, голоса, экран),
        // оставляя строку агента, платформу и подсказки. Нужен, чтобы отделить их вклад от прочего.
        if (string.Equals(Environment.GetEnvironmentVariable("VC_NO_OS_SURFACES"), "1", StringComparison.Ordinal))
            payload["suppressOsSurfaces"] = true;

        AppendDisabledOsSurfaces(payload);
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

        // ★ Клиентские подсказки — расширение Chromium; у WebKit их нет ВОВСЕ. Профиль iPhone,
        // отдающий 'Sec-CH-UA-Platform: "iOS"' и 'navigator.userAgentData', противоречит
        // собственной строке агента ещё до исполнения любого скрипта — это видно прямо в
        // заголовках запроса. Поэтому для профиля на WebKit подсказки в контекст не кладутся.
        if (DeclaresChromiumEngine(device.UserAgent))
            AppendOptionalObject(payload, "clientHints", BuildClientHintsPayload(device.ClientHints));

        // WebGL передаём вместе с остальным контекстом вкладки: заявленная платформа обязана
        // подтверждаться и здесь. Настоящий десктопный браузер всегда отдаёт vendor/renderer, и
        // расхождение с User-Agent (или пустые значения) выделяет клиента не хуже прямого признака
        // автоматизации.
        AppendOptionalObject(payload, "webGl", BuildWebGlPayload(device.WebGL));

        // ★ Числовые пределы идут ВМЕСТЕ с именем видеокарты. Подменять одно без другого хуже,
        // чем не подменять вовсе: пара «карта — её пределы» известна и сверяется таблицей, а
        // замер показывал Apple M1 Pro с максимальным размером текстуры 8192 вместо 16384 —
        // то есть карту, которой не бывает.
        AppendOptionalObject(payload, "webGlParameters", BuildWebGlParametersPayload(device.WebGLParams));

        if (!device.ViewportSize.IsEmpty)
        {
            payload["viewport"] = new JsonObject
            {
                ["width"] = device.ViewportSize.Width,
                ["height"] = device.ViewportSize.Height,
            };
        }

        // Экран передаётся ОТДЕЛЬНО от области просмотра. Прежде страница получала экран, выведенный
        // из размеров окна, и доступная область совпадала с полной — почерк среды без оболочки
        // рабочего стола, где нет ни строки меню, ни панели задач.
        AppendOptionalObject(payload, "screen", BuildScreenPayload(device.Screen));

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

        // Предпочтения оформления читаются медиа-запросами, а не свойствами навигатора, поэтому
        // передаются отдельно: заявленная светлая тема при тёмной теме окружения — расхождение,
        // которое сайт видит одной строкой matchMedia.
        AppendOptionalString(payload, "colorScheme", device.ColorScheme);

        // Только при заданном значении: у JsonObject присвоение null пишет JSON-null, а расширение
        // принимает либо отсутствие поля, либо логическое значение — на null оно отвергало ВЕСЬ
        // контекст вкладки ("Контекст вкладки содержит неверный reducedMotion") и браузер падал
        // ещё на инициализации. Пресеты устройств задают это поле всегда, поэтому промах вылезал
        // только там, где устройство собирается из конфигурации (боевой солвер).
        if (device.ReducedMotion is { } reducedMotion)
            payload["reducedMotion"] = reducedMotion;

        // Сеть: заявленный профиль и наблюдаемое соединение расходились полностью — замер видел
        // 3g/rtt 400/downlink 1.35 при заявленном 4g. Соединение читается тем же скриптом, что и
        // остальная личность, и расхождение здесь ничем не отличается от расхождения платформы.
        AppendOptionalObject(payload, "network", BuildNetworkPayload(device.NetworkInfo));

        if (device.VirtualMediaDevices is { } virtualMediaDevices)
            payload["virtualMediaDevices"] = BuildVirtualMediaDevicesPayload(virtualMediaDevices);
    }

    /// <summary>
    /// Гасит отдельные ветви ОС-подмен по именам из <c lang="text">VC_DISABLE_OS_SURFACES</c>.
    /// </summary>
    /// <remarks>
    /// Инструмент бинарного поиска виновника отказа: общий рубильник
    /// <c lang="text">VC_NO_OS_SURFACES</c> выключает слой целиком и вклад отдельной ветви не
    /// разделяет. Имена через запятую: voices, fonts, keyboard, colors.
    /// </remarks>
    private static void AppendDisabledOsSurfaces(JsonObject payload)
    {
        if (Environment.GetEnvironmentVariable("VC_DISABLE_OS_SURFACES") is not { Length: > 0 } disabledSurfaces)
            return;

        var names = new JsonArray();

        foreach (var name in disabledSurfaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            names.Add(name);

        payload["disabledOsSurfaces"] = names;
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

    /// <summary>
    /// Готовит WebGL-часть контекста вкладки.
    /// </summary>
    /// <remarks>
    /// Маскированные (<c lang="text">Vendor</c>/<c lang="text">Renderer</c>) и немаскированные значения передаются отдельно:
    /// страница читает их разными путями — обычным <c lang="text">getParameter</c> и через расширение
    /// <c lang="text">WEBGL_debug_renderer_info</c>, — и подменять нужно оба, иначе они разойдутся между собой.
    /// </remarks>
    /// <summary>
    /// Собирает числовые пределы WebGL для контекста вкладки.
    /// </summary>
    /// <param name="parameters">Пределы заявленной видеокарты.</param>
    /// <returns>Полезная нагрузка либо <see langword="null"/>, если пределы не заданы.</returns>
    private static JsonObject? BuildWebGlParametersPayload(WebGLParamsSettings? parameters)
    {
        if (parameters is null) return null;

        var payload = new JsonObject();

        if (parameters.MaxTextureSize > 0) payload["maxTextureSize"] = parameters.MaxTextureSize;
        if (parameters.MaxRenderbufferSize > 0) payload["maxRenderbufferSize"] = parameters.MaxRenderbufferSize;
        if (parameters.MaxVaryingVectors > 0) payload["maxVaryingVectors"] = parameters.MaxVaryingVectors;
        if (parameters.MaxVertexUniformVectors > 0) payload["maxVertexUniformVectors"] = parameters.MaxVertexUniformVectors;
        if (parameters.MaxFragmentUniformVectors > 0) payload["maxFragmentUniformVectors"] = parameters.MaxFragmentUniformVectors;

        if (parameters.MaxViewportDims is { } viewportDims)
        {
            var dims = viewportDims.ToArray();

            if (dims.Length is 2)
                payload["maxViewportDims"] = new JsonArray(JsonValue.Create(dims[0]), JsonValue.Create(dims[1]));
        }

        return payload.Count > 0 ? payload : null;
    }

    /// <summary>
    /// Кладёт в нагрузку вложенный объект, если он собран.
    /// </summary>
    /// <param name="payload">Нагрузка контекста вкладки.</param>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Собранный объект либо <see langword="null"/>.</param>
    private static void AppendOptionalObject(JsonObject payload, string name, JsonObject? value)
    {
        if (value is not null)
            payload[name] = value;
    }

    /// <summary>
    /// Собирает параметры соединения заявленного устройства.
    /// </summary>
    /// <param name="network">Сеть профиля.</param>
    /// <returns>Полезная нагрузка либо <see langword="null"/>, если сеть не заявлена.</returns>
    private static JsonObject? BuildNetworkPayload(NetworkInfoSettings? network)
    {
        if (network is null)
            return null;

        var payload = new JsonObject();

        AppendOptionalString(payload, "effectiveType", network.EffectiveType);
        AppendOptionalString(payload, "type", network.Type);

        if (network.Downlink is { } downlink && downlink > 0) payload["downlink"] = downlink;
        if (network.Rtt is { } rtt && rtt >= 0) payload["rtt"] = rtt;

        return payload.Count > 0 ? payload : null;
    }

    /// <summary>
    /// Собирает метрики экрана заявленного устройства.
    /// </summary>
    /// <param name="screen">Экран профиля.</param>
    /// <returns>Полезная нагрузка либо <see langword="null"/>, если экран не заявлен.</returns>
    /// <summary>
    /// Заявляет ли строка агента движок WebKit.
    /// </summary>
    /// <param name="userAgent">Строка агента профиля.</param>
    /// <returns><see langword="true"/>, если профиль объявляет iOS/iPadOS либо Safari.</returns>
    /// <remarks>
    /// На iOS и iPadOS других движков не бывает: Chrome и Firefox там — оболочки над системным
    /// WebKit. Поэтому признак движка выводится из ОС, а не только из имени браузера.
    /// </remarks>
    internal static bool DeclaresWebKitEngine(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        return userAgent.Contains("iPhone", StringComparison.Ordinal)
            || userAgent.Contains("iPad", StringComparison.Ordinal)
            || userAgent.Contains("iPod", StringComparison.Ordinal)
            || (userAgent.Contains("Safari/", StringComparison.Ordinal)
                && userAgent.Contains("Version/", StringComparison.Ordinal)
                && !userAgent.Contains("Chrome/", StringComparison.Ordinal)
                && !userAgent.Contains("Chromium/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Объявляет ли профиль браузер на движке Chromium.
    /// </summary>
    /// <remarks>
    /// ★ Клиентские подсказки существуют ТОЛЬКО в Chromium: ни Safari, ни Firefox их не шлют и
    /// не объявляют <c lang="text">navigator.userAgentData</c>. Прежнее условие «не WebKit» пропускало Gecko,
    /// и профиль Firefox уезжал с заголовком <c lang="text">Sec-CH-UA: "Chromium";v="131"</c> при собственном
    /// фаерфоксовом <c lang="text">Accept</c> — Cloudflare отвечал на это 600010 («среда слишком ограничена»),
    /// 0 задач из 14, тогда как тот же Firefox без профиля решал 3 из 3.
    /// </remarks>
    internal static bool DeclaresChromiumEngine(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        if (DeclaresWebKitEngine(userAgent))
            return false;

        if (userAgent.Contains("Firefox/", StringComparison.Ordinal)
            || userAgent.Contains("Gecko/", StringComparison.Ordinal))
        {
            return false;
        }

        return userAgent.Contains("Chrome/", StringComparison.Ordinal)
            || userAgent.Contains("Chromium/", StringComparison.Ordinal);
    }

    private static JsonObject? BuildScreenPayload(ScreenSettings? screen)
    {
        if (screen is null || screen.Width <= 0 || screen.Height <= 0)
            return null;

        var payload = new JsonObject
        {
            ["width"] = screen.Width,
            ["height"] = screen.Height,
        };

        if (screen.AvailWidth > 0) payload["availWidth"] = screen.AvailWidth;
        if (screen.AvailHeight > 0) payload["availHeight"] = screen.AvailHeight;
        if (screen.ColorDepth > 0) payload["colorDepth"] = screen.ColorDepth;
        if (screen.PixelDepth > 0) payload["pixelDepth"] = screen.PixelDepth;

        return payload;
    }

    // ★ Заявленная видеокарта приводится к ТОЙ, ЧТО РЕАЛЬНО РИСУЕТ.
    //
    // Браузер живёт на виртуальном дисплее и всегда запускается с '--use-angle=swiftshader':
    // кадры даёт программный растеризатор. Профиль же заявлял дискретную карту —
    // 'ANGLE (Intel, Intel(R) UHD Graphics 630 ... Direct3D11)'. Проверка не читает эти строки,
    // она РИСУЕТ: попиксельный вывод и тайминги SwiftShader с аппаратным Direct3D11 не
    // совпадают, и расхождение видно без разбора строк.
    //
    // Замер на живом Cloudflare, один стенд и один дисплей: с заявленной дискретной картой —
    // error-callback 600010, tokenLen=0; без подмены WebGL (SwiftShader виден как есть) — токен 794.
    //
    // Программный рендеринг сам по себе НЕ признак автоматики: так рисуют машины без драйвера
    // GPU, виртуалки и удалённые рабочие столы. Признак — несовпадение заявленного с фактическим.
    private const string SoftwareWebGlVendor = "Google Inc. (Google)";

    private const string SoftwareWebGlRenderer =
        "ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), SwiftShader driver-5.0.0)";

    private static JsonObject? BuildWebGlPayload(WebGLSettings? settings)
    {
        if (settings is null)
            return null;

        var payload = new JsonObject();
        AppendOptionalString(payload, "vendor", SoftwareWebGlVendor);
        AppendOptionalString(payload, "renderer", SoftwareWebGlRenderer);
        AppendOptionalString(payload, "unmaskedVendor", SoftwareWebGlVendor);
        AppendOptionalString(payload, "unmaskedRenderer", SoftwareWebGlRenderer);
        AppendOptionalString(payload, "version", settings.Version);
        AppendOptionalString(payload, "shadingLanguageVersion", settings.ShadingLanguageVersion);

        return payload.Count > 0 ? payload : null;
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

    // Открытие вкладки под Chrome-семейством будит/перезапускает service worker расширения
    // (Manifest V3): новый воркер переподключается с тем же sessionId, сервер вытесняет прежний
    // сокет close 1008 «идентификатор-сеанса-уже-занят», и ожидающая команда контекста вкладки
    // падает с surface-disconnect. Транспорт клиента автопереподключается (transport-reconnected),
    // поэтому отправку контекста достаточно повторить: вкладка перерегистрируется, и следующая
    // попытка доходит. Бюджет как у перехвата запросов в WebPage.Bridge.cs (≈5 c).
    private const int TabContextRetryAttempts = 100;

    private static readonly TimeSpan TabContextRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Приводит запечённый профиль раннего скрипта в соответствие с личностью вкладки.
    /// </summary>
    /// <remarks>
    /// Скрипт <c lang="text">document_start</c> ставит личность ДО любого кода страницы — ради этого он и
    /// заведён, динамическое внедрение опаздывает. Но нёс он профиль ЗАПУСКА браузера, то есть чужую
    /// для задачи личность, и код страницы, успевший исполниться до динамического контекста, читал её.
    ///
    /// Ошибка записи не должна ронять задачу: динамический контекст идёт следом и личность всё равно выставит.
    /// </remarks>
    private static async ValueTask SyncEarlyIdentityProfileAsync(WebPage page, CancellationToken cancellationToken)
    {
        var extensionPath = page.OwnerWindow.OwnerBrowser.localExtensionPath;
        if (string.IsNullOrWhiteSpace(extensionPath) || !Directory.Exists(extensionPath))
            return;

        try
        {
            await BridgeExtensionBootstrap.UpdateIdentityProfileAsync(extensionPath, page.ResolvedDevice, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Молча: динамический контекст идёт следом и личность всё равно выставит.
            _ = exception;
        }
    }

    internal static async ValueTask ApplyBridgeTabContextAsync(WebPage page, CancellationToken cancellationToken)
    {
        var bridgeCommands = page.BridgeCommands
            ?? throw new InvalidOperationException("Bridge-backed page is not bound to command transport");

        await SyncEarlyIdentityProfileAsync(page, cancellationToken).ConfigureAwait(false);

        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await bridgeCommands.SetTabContextAsync(BuildSetTabContextPayload(page), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException exception)
                when (attempt < TabContextRetryAttempts
                    && Protocol.BridgeCommandException.IsSurfaceDisconnect(exception))
            {
                await Task.Delay(TabContextRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
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

        return ValueTask.FromResult(ReferenceEquals(element.Page.Window.Browser, this) ? element.Page.Window : null);
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
        return ValueTask.FromResult(ReferenceEquals(element.Page.Window.Browser, this) ? element.Page : null);
    }

    public ValueTask<IWebPage?> GetPageAsync(IElement element)
        => GetPageAsync(element, CancellationToken.None);
}