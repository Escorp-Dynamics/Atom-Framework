using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Buffers;
using Atom.Threading;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет контекст веб-браузера.
/// </summary>
public class WebBrowserContext : IWebBrowserContext
{
    private readonly Locker locker = new();
    private readonly List<IWebWindow> windows = ObjectPool<List<IWebWindow>>.Shared.Rent();

    private bool isDisposed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebWindow>? WindowOpened;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebWindow>? WindowClosed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageOpened;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageClosed;

    /// <inheritdoc/>
    public event MutableEventHandler? Destroyed;

    /// <inheritdoc/>
    public IWebBrowser Browser { get; protected set; }

    /// <inheritdoc/>
    public IWebBrowserContextSettings Settings { get; protected set; }

    /// <inheritdoc/>
    public IEnumerable<IWebWindow> Windows => windows;

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => windows.SelectMany(x => x.Pages);

    /// <inheritdoc/>
    [field: MaybeNull]
    public IWebWindow CurrentWindow
    {
        get;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <inheritdoc/>
    public IWebPage CurrentPage
    {
        get => CurrentWindow.CurrentPage;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            CurrentWindow = value.Window;
        }
    }

    /// <inheritdoc/>
    public string Source => CurrentPage.Source;

    /// <inheritdoc/>
    public IConsole Console => CurrentPage.Console;

    /// <inheritdoc/>
    public CookieContainer Cookies => CurrentPage.Cookies;

    /// <inheritdoc/>
    public bool IsDestroyed { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserContext"/>.
    /// </summary>
    /// <param name="browser">Экземпляр веб-браузера, в котором был создан текущий контекст.</param>
    /// <param name="settings">Настройки контекста веб-браузера.</param>
    protected internal WebBrowserContext(IWebBrowser browser, IWebBrowserContextSettings settings)
    {
        Browser = browser;
        Settings = settings;
    }

    /// <summary>
    /// Происходит в момент открытия окна браузера.
    /// </summary>
    protected virtual void OnWindowOpened(IWebWindow window) => WindowOpened?.Invoke(window);

    /// <summary>
    /// Происходит в момент закрытия окна браузера.
    /// </summary>
    protected virtual void OnWindowClosed(IWebWindow window) => WindowClosed?.Invoke(window);

    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    /// <param name="page">Веб-страница.</param>
    protected virtual void OnPageOpened(IWebPage page) => PageOpened?.Invoke(page);

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    /// <param name="page">Веб-страница.</param>
    protected virtual void OnPageClosed(IWebPage page) => PageClosed?.Invoke(page);

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    protected virtual void OnClosed() => Destroyed?.Invoke();

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (!disposing) return;

        DestroyAsync().AsTask().GetAwaiter().GetResult();
        locker.Dispose();

        ObjectPool<List<IWebWindow>>.Shared.Return(windows, x => x.Clear());
    }

    /// <inheritdoc/>
    public virtual async ValueTask<IWebWindow> OpenWindowAsync(IWebWindowSettings settings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) || IsDestroyed, this);
        var window = new WebWindow(this, settings);

        window.Closed += async () =>
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            windows.Remove(window);
            locker.Release();
            OnWindowClosed(window);
        };

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        windows.Add(window);
        locker.Release();
        OnWindowOpened(window);

        return CurrentWindow = window;
    }

    /// <inheritdoc/>
    public ValueTask<IWebWindow> OpenWindowAsync(IWebWindowSettings settings) => OpenWindowAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask<IWebWindow> OpenWindowAsync(CancellationToken cancellationToken) => OpenWindowAsync(WebWindowSettings.CreateFrom<WebBrowserContextSettings, WebWindowSettings>((WebBrowserContextSettings)Settings), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWebWindow> OpenWindowAsync() => OpenWindowAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask DestroyAsync(CancellationToken cancellationToken)
    {
        if (IsDestroyed) return;
        IsDestroyed = true;

        for (var i = 0; i < windows.Count; ++i) await windows[i].CloseAsync(cancellationToken).ConfigureAwait(false);
        OnClosed();
    }

    /// <inheritdoc/>
    public ValueTask DestroyAsync() => DestroyAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await DestroyAsync().ConfigureAwait(false);
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}