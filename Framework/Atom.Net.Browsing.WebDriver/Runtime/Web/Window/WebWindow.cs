using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Atom.Hardware.Input;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет окно браузера и управляет набором открытых страниц в его пределах.
/// </summary>
public sealed partial class WebWindow : IWebWindow
{
    private readonly ConcurrentStack<WebPage> pages = [];
    private readonly ConcurrentQueue<BridgeMessage> bridgeEvents = [];
    private readonly Lock pageGate = new();
    private string? BridgeWindowId { get; set; }
    private RequestInterceptionState? requestInterceptionState;
    private int disposeState;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Current page belongs to the pages stack and is disposed through it in reverse order.")]
    private WebPage currentPage;

    public WebWindow(WebBrowser browser, WebWindowSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(browser);

        WindowId = Guid.NewGuid().ToString("N");
        OwnerBrowser = browser;
        Settings = settings.Clone();
        ResolvedDevice = browser.LaunchSettings.Device.ResolveDevice(Settings?.Device);
        ResolvedWindowSize = browser.LaunchSettings.ResolveWindowSize(Settings);
        ResolvedWindowPosition = browser.LaunchSettings.ResolveWindowPosition(Settings);
        var initialPage = new WebPage(this);
        pages.Push(initialPage);
        Volatile.Write(ref currentPage, initialPage);
        browser.LaunchSettings.Logger?.LogWebWindowCreated(WindowId);
    }

    internal WebBrowser OwnerBrowser { get; }

    internal string WindowId { get; }

    internal string EffectiveWindowId => BridgeWindowId ?? WindowId;

    internal WebWindowSettings? Settings { get; }

    internal Device? ResolvedDevice { get; }

    internal VirtualMouse? AssignedMouse => Settings?.Mouse;

    internal VirtualKeyboard? AssignedKeyboard => Settings?.Keyboard;

    internal Size ResolvedWindowSize { get; }

    internal Point ResolvedWindowPosition { get; }

    internal event Action<BridgeMessage>? BridgeEventReceived;

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
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowBridgeEventRelayed(WindowId, message.Event?.ToString() ?? message.Type.ToString(), message.TabId ?? "<none>");
        await OwnerBrowser.EnqueueBridgeEventAsync(message, dispatchHandlers).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IWebBrowser Browser => OwnerBrowser;

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => pages.Where(static page => !page.IsDisposed);

    /// <inheritdoc/>
    public IWebPage CurrentPage => Volatile.Read(ref currentPage);

    /// <inheritdoc/>
    public bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebWindow, ConsoleMessageEventArgs>? Console;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebWindow, InterceptedRequestEventArgs>? Request;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebWindow, InterceptedResponseEventArgs>? Response;

    public async ValueTask DisposeAsync()
    {
        List<WebPage> pagesToDispose = [];

        lock (pageGate)
        {
            if (disposeState != 0)
                return;

            disposeState = 1;

            while (pages.TryPop(out var page))
            {
                pagesToDispose.Add(page);
            }
        }

        foreach (var page in pagesToDispose)
        {
            await page.DisposeAsync().ConfigureAwait(false);
        }

        OwnerBrowser.OnWindowDisposed(this);
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowDisposed(WindowId);
    }

    public ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken)
        => OwnerBrowser.OpenPageCoreAsync(this, settings: null, cancellationToken);

    public ValueTask<IWebPage> OpenPageAsync()
        => OpenPageAsync(CancellationToken.None);

    public ValueTask<IWebPage> OpenPageAsync(WebPageSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return OwnerBrowser.OpenPageCoreAsync(this, settings, cancellationToken);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);

    internal void OnPageDisposed(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        lock (pageGate)
        {
            if (disposeState != 0 || !ReferenceEquals(currentPage, page))
            {
                return;
            }

            var nextPage = pages.FirstOrDefault(static candidate => !candidate.IsDisposed);
            if (nextPage is not null)
            {
                Volatile.Write(ref currentPage, nextPage);
                OwnerBrowser.LaunchSettings.Logger?.LogWebWindowCurrentPageSwitched(WindowId, nextPage.TabId);
            }
        }
    }

    public ValueTask<IWebPage> OpenPageAsync(WebPageSettings settings)
        => OpenPageAsync(settings, CancellationToken.None);

    internal string? BoundBridgeWindowId => BridgeWindowId;

    internal RequestInterceptionState? GetEffectiveRequestInterceptionState()
        => requestInterceptionState ?? OwnerBrowser.GetEffectiveRequestInterceptionState();

    internal void PublishOpenedPage(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        lock (pageGate)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(page.IsDisposed, page);
            pages.Push(page);
            Volatile.Write(ref currentPage, page);
            OwnerBrowser.LaunchSettings.Logger?.LogWebWindowPageOpened(WindowId, page.TabId);
        }
    }

    internal void BindBridgeWindowId(string? windowId)
    {
        if (!string.IsNullOrWhiteSpace(windowId))
            BridgeWindowId = windowId;
    }

    internal ValueTask<VirtualMouse> ResolveMouseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (AssignedMouse is not null)
            return ValueTask.FromResult(AssignedMouse);

        return OwnerBrowser.ResolveMouseAsync(cancellationToken);
    }

    internal ValueTask<VirtualKeyboard> ResolveKeyboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (AssignedKeyboard is not null)
            return ValueTask.FromResult(AssignedKeyboard);

        return OwnerBrowser.ResolveKeyboardAsync(cancellationToken);
    }
}