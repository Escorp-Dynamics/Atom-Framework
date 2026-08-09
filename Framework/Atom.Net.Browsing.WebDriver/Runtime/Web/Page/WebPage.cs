using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;
using System.Text.Json;
using System.Threading;
using Atom.Hardware.Input;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет страницу браузера и делегирует DOM-операции её основному фрейму.
/// </summary>
public sealed partial class WebPage : IWebPage
{
    /// <summary>
    /// Верхняя граница буфера мостовых событий. Очередь дренируется только внешним потребителем
    /// через <see cref="TryDequeueBridgeEvent"/>; без ограничения долгоживущая страница
    /// накапливала бы все события навигации и перехвата вместе с их payload.
    /// </summary>
    private const int MaxBufferedBridgeEvents = 4096;

    private readonly ConcurrentQueue<BridgeMessage> bridgeEvents = [];
    private readonly ConcurrentDictionary<string, byte> callbackSubscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Frame> childFramesByHostElementId = new(StringComparer.Ordinal);
    private readonly Lock childFramesSync = new();
    private readonly ConcurrentDictionary<string, byte> detachedFrameElementIds = new(StringComparer.Ordinal);
    private readonly Frame mainFrame;
    private readonly PageNavigationState navigationTransport;
    private Uri? pendingBridgeNavigationUrl;
    private RequestInterceptionState? requestInterceptionState;
    private RequestInterceptionState? appliedRequestInterceptionState;
    private int disposeState;

    public WebPage(WebWindow window, WebPageSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        OwnerWindow = window;
        TabId = Guid.NewGuid().ToString("N");
        Settings = settings.Clone();
        ResolvedDevice = window.ResolvedDevice.ResolveDevice(Settings?.Device);
        navigationTransport = new PageNavigationState(window.WindowId, TabId, window.OwnerBrowser.LaunchSettings.Logger);
        mainFrame = new Frame(this);
        window.OwnerBrowser.LaunchSettings.Logger?.LogWebPageCreated(TabId, window.WindowId);
    }

    internal WebWindow OwnerWindow { get; }

    internal string TabId { get; }

    internal string WindowId => OwnerWindow.WindowId;

    internal WebPageSettings? Settings { get; private set; }

    internal Device? ResolvedDevice { get; private set; }

    internal string GetOrCreateBridgeContextId()
        => BridgeContextId ??= Guid.NewGuid().ToString("N");

    internal VirtualMouse? AssignedMouse => Settings?.Mouse;

    internal VirtualKeyboard? AssignedKeyboard => Settings?.Keyboard;

    internal VirtualCamera? AttachedVirtualCamera { get; private set; }

    internal VirtualMicrophone? AttachedVirtualMicrophone { get; private set; }

    internal IPageTransport Transport => navigationTransport;

    internal Uri? CurrentUrl => Transport.CurrentUrl;

    internal Uri? ResolveBridgeContextUrl()
        => pendingBridgeNavigationUrl ?? CurrentUrl;

    internal void SetPendingBridgeNavigationUrl(Uri? url)
        => pendingBridgeNavigationUrl = url;

    internal string? CurrentTitle => Transport.CurrentTitle;

    internal string? CurrentContent => Transport.CurrentContent;

    internal event Action<BridgeMessage>? BridgeEventReceived;

    internal bool TryDequeueBridgeEvent([NotNullWhen(true)] out BridgeMessage? message)
        => bridgeEvents.TryDequeue(out message);

    internal async ValueTask ReceiveBridgeEventAsync(BridgeMessage message, bool dispatchHandlers = true)
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
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageBridgeEventSynced(TabId, message.Event?.ToString() ?? message.Type.ToString());
        await OwnerWindow.EnqueueBridgeEventAsync(message, dispatchHandlers).ConfigureAwait(false);
    }

    internal async ValueTask SyncTransportEventsAsync()
    {
        ThrowIfDisposed();

        while (Transport.TryDequeueEvent(out var message))
        {
            if (message is null)
            {
                continue;
            }

            if (!ShouldDispatchTransportEvent(message))
            {
                continue;
            }

            await ReceiveBridgeEventAsync(message).ConfigureAwait(false);
        }
    }

    private bool ShouldDispatchTransportEvent(BridgeMessage message)
    {
        if (message.Event is not (BridgeEvent.RequestIntercepted or BridgeEvent.ResponseReceived))
            return true;

        return GetEffectiveRequestInterceptionState()?.Matches(ReadTransportEventUrl(message)) ?? false;
    }

    private static string? ReadTransportEventUrl(BridgeMessage message)
    {
        if (message.Payload is not JsonElement payload
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("url", out var url)
            || url.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return url.GetString();
    }

    /// <inheritdoc/>
    public IWebWindow Window => OwnerWindow;

    /// <inheritdoc/>
    public IFrame MainFrame => mainFrame;

    /// <inheritdoc/>
    public IEnumerable<IFrame> Frames => SnapshotFrames();

    /// <inheritdoc/>
    public TimeSpan WaitingTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage, ConsoleMessageEventArgs>? Console;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage, InterceptedRequestEventArgs>? Request;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage, InterceptedResponseEventArgs>? Response;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage, CallbackEventArgs>? Callback;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage, CallbackFinalizedEventArgs>? CallbackFinalized;

    internal void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);

    internal ValueTask<VirtualMouse> ResolveMouseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (AssignedMouse is not null)
            return ValueTask.FromResult(AssignedMouse);

        return OwnerWindow.ResolveMouseAsync(cancellationToken);
    }

    internal ValueTask<VirtualKeyboard> ResolveKeyboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (AssignedKeyboard is not null)
            return ValueTask.FromResult(AssignedKeyboard);

        return OwnerWindow.ResolveKeyboardAsync(cancellationToken);
    }

    internal ValueTask<string?> GetLookupTitleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var currentTitle = CurrentTitle;
        if (currentTitle is not null)
            return ValueTask.FromResult<string?>(currentTitle);

        return BridgeCommands is null
            ? ValueTask.FromResult<string?>(null)
            : GetTitleAsync(cancellationToken);
    }

    internal ValueTask<Uri?> GetLookupUrlAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return GetLookupUrlCoreAsync(cancellationToken);
    }

    private async ValueTask<Uri?> GetLookupUrlCoreAsync(CancellationToken cancellationToken)
    {
        if (BridgeCommands is not null && ReferenceEquals(OwnerWindow.CurrentPage, this))
        {
            var liveUrl = await GetUrlAsync(cancellationToken).ConfigureAwait(false);
            if (liveUrl is not null)
                return liveUrl;
        }

        return CurrentUrl;
    }

    internal void MarkFrameElementDetached(string frameElementId)
    {
        if (string.IsNullOrWhiteSpace(frameElementId))
            return;

        detachedFrameElementIds.TryAdd(frameElementId, 0);
        PruneDetachedFrameFromGraph(frameElementId);
    }

    internal bool IsFrameElementDetached(string frameElementId)
        => !string.IsNullOrWhiteSpace(frameElementId) && detachedFrameElementIds.ContainsKey(frameElementId);

    internal void ResetFrameDetachmentState()
        => detachedFrameElementIds.Clear();

    private void PruneDetachedFrameFromGraph(string frameElementId)
    {
        Frame? detachedFrame;

        lock (childFramesSync)
        {
            if (!childFramesByHostElementId.TryGetValue(frameElementId, out detachedFrame))
                detachedFrame = FindFrameInGraph(mainFrame, frameElementId);

            if (detachedFrame is null)
                return;

            RemoveFrameSubtreeFromLookup(detachedFrame);
        }

        detachedFrame.DetachFromParent();
    }

    private static Frame? FindFrameInGraph(Frame parentFrame, string frameElementId)
    {
        foreach (var childFrame in parentFrame.SnapshotChildFrames())
        {
            if (childFrame.Host is Element host && string.Equals(host.BridgeElementId, frameElementId, StringComparison.Ordinal))
                return childFrame;

            if (FindFrameInGraph(childFrame, frameElementId) is { } nestedFrame)
                return nestedFrame;
        }

        return null;
    }

    private void RemoveFrameSubtreeFromLookup(Frame frame)
    {
        if (frame.Host is Element host && host.BridgeElementId is { Length: > 0 } hostElementId)
            childFramesByHostElementId.Remove(hostElementId);

        foreach (var childFrame in frame.SnapshotChildFrames())
            RemoveFrameSubtreeFromLookup(childFrame);
    }

    internal Frame GetOrCreateChildFrame(Frame parentFrame, string frameHostElementId)
    {
        ArgumentNullException.ThrowIfNull(parentFrame);
        ArgumentException.ThrowIfNullOrWhiteSpace(frameHostElementId);

        return GetOrCreateChildFrame(parentFrame, new Element(parentFrame, frameHostElementId));
    }

    internal Frame GetOrCreateChildFrame(Frame parentFrame, Element frameHostElement)
    {
        ArgumentNullException.ThrowIfNull(parentFrame);
        ArgumentNullException.ThrowIfNull(frameHostElement);

        if (frameHostElement.BridgeElementId is not { Length: > 0 } frameHostElementId)
            return new Frame(this, parentFrame, frameHostElement);

        lock (childFramesSync)
        {
            if (childFramesByHostElementId.TryGetValue(frameHostElementId, out var existingFrame))
                return existingFrame;

            var createdFrame = new Frame(this, parentFrame, frameHostElement);
            childFramesByHostElementId[frameHostElementId] = createdFrame;
            return createdFrame;
        }
    }

    internal async ValueTask EnsureFramesDiscoveredAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Queue<Frame> pendingFrames = new();
        HashSet<Frame> visitedFrames = [];
        pendingFrames.Enqueue(mainFrame);

        while (pendingFrames.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentFrame = pendingFrames.Dequeue();
            if (!visitedFrames.Add(currentFrame))
                continue;

            await currentFrame.EnsureChildFramesDiscoveredAsync(cancellationToken).ConfigureAwait(false);

            foreach (var childFrame in currentFrame.SnapshotChildFrames())
                pendingFrames.Enqueue(childFrame);
        }
    }

    private IFrame[] SnapshotFrames()
    {
        List<IFrame> frames = [mainFrame];
        AppendFrames(mainFrame, frames);
        return [.. frames];
    }

    private static void AppendFrames(Frame parentFrame, List<IFrame> frames)
    {
        foreach (var childFrame in parentFrame.SnapshotChildFrames())
        {
            frames.Add(childFrame);
            AppendFrames(childFrame, frames);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        ReleasePageScopedState();
        OwnerWindow.OnPageDisposed(this);
        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageDisposed(TabId);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Освобождает состояние, живущее дольше самой страницы. Маршрут навигационного прокси
    /// снимался только при <see cref="ReconfigureAsync(WebPageSettings)"/>, поэтому закрытая
    /// вкладка навсегда оставляла в реестре рабочий route token, а неразобранные события —
    /// в очереди страницы.
    /// </summary>
    private void ReleasePageScopedState()
    {
        if (BridgeContextId is { } contextId)
        {
            OwnerWindow.OwnerBrowser.ProxyNavigationDecisions.RemoveRouteByContextId(contextId);
        }

        bridgeEvents.Clear();
        callbackSubscriptions.Clear();
        detachedFrameElementIds.Clear();

        lock (childFramesSync)
        {
            childFramesByHostElementId.Clear();
        }
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageClosing(TabId);

        if (BridgeCommands is { } bridge)
        {
            try
            {
                await bridge.CloseTabAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (IsExpectedBridgeCloseDisconnect(exception))
            {
                // Закрытие собственной вкладки моста может отключить отправителя до прихода ответа.
            }
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask CloseAsync()
        => CloseAsync(CancellationToken.None);

    public async ValueTask ReconfigureAsync(WebPageSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageReconfiguring(TabId);

        var previousContextId = BridgeContextId;

        // Полная перенастройка окружения вкладки из новых настроек без переоткрытия вкладки.
        // Прокси и фингерпринт переприменяются через повторную отправку мостового контекста:
        // payload строится из Settings (ResolveBridgeProxy) и ResolvedDevice (AppendDeviceContext),
        // а background/content runtime применяют их при новом contextId.
        Settings = settings.Clone();
        ResolvedDevice = OwnerWindow.ResolvedDevice.ResolveDevice(Settings?.Device);
        ApplyAttachedMediaDevicesState();

        BridgeContextId = null;
        appliedRequestInterceptionState = null;

        // Новый contextId порождает новый navigation-proxy маршрут; освобождаем маршрут
        // прежнего контекста, чтобы старый upstream прокси не оставался зарегистрированным.
        if (previousContextId is not null)
        {
            OwnerWindow.OwnerBrowser.ProxyNavigationDecisions.RemoveRouteByContextId(previousContextId);
        }

        if (BridgeCommands is not null)
        {
            await WebBrowser.ApplyBridgeTabContextAsync(this, cancellationToken).ConfigureAwait(false);
            await ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
        }

        OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebPageReconfigured(TabId);
    }

    public ValueTask ReconfigureAsync(WebPageSettings settings)
        => ReconfigureAsync(settings, CancellationToken.None);

    /// <summary>
    /// Закрытие собственной вкладки моста может отключить отправителя до прихода ответа.
    /// Признак берётся из типизированного исключения, а не из текста сообщения.
    /// </summary>
    private static bool IsExpectedBridgeCloseDisconnect(InvalidOperationException exception)
        => BridgeCommandException.IsSurfaceDisconnect(exception);
}