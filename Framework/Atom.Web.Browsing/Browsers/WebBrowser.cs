#pragma warning disable CA2000
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Buffers;
using Atom.Threading;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет веб-браузер.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="WebBrowser"/>.
/// </remarks>
/// <param name="settings">Настройки веб-браузера.</param>
public class WebBrowser([NotNull] IWebBrowserSettings settings) : IWebBrowser
{
    private readonly Locker locker = new();
    private readonly List<IWebBrowserContext> contexts = ObjectPool<List<IWebBrowserContext>>.Shared.Rent();

    private bool isDisposed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebBrowserContext>? ContextCreated;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebBrowserContext>? ContextDestroyed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebWindow>? WindowOpened;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebWindow>? WindowClosed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageOpened;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageClosed;

    /// <inheritdoc/>
    public IWebBrowserSettings Settings { get; protected set; } = settings;

    /// <inheritdoc/>
    public IEnumerable<IWebBrowserContext> Contexts => contexts;

    /// <inheritdoc/>
    public IEnumerable<IWebWindow> Windows => contexts.SelectMany(x => x.Windows);

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => contexts.SelectMany(x => x.Pages);

    /// <inheritdoc/>
    [field: MaybeNull]
    public IWebBrowserContext CurrentContext
    {
        get;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <inheritdoc/>
    public IWebWindow CurrentWindow
    {
        get => CurrentContext.CurrentWindow;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            CurrentContext = value.Context;
        }
    }

    /// <inheritdoc/>
    public IWebPage CurrentPage
    {
        get => CurrentContext.CurrentPage;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            CurrentContext = value.Context;
        }
    }

    /// <inheritdoc/>
    public string Source => CurrentPage.Source;

    /// <inheritdoc/>
    public IConsole Console => CurrentPage.Console;

    /// <inheritdoc/>
    public CookieContainer Cookies => CurrentPage.Cookies;

    /// <inheritdoc/>
    public bool IsClosed { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowser"/>.
    /// </summary>
    public WebBrowser() : this(WebBrowserSettings.Default) { }

    /// <summary>
    /// Происходит в момент создания контекста веб-браузера.
    /// </summary>
    /// <param name="context">Контекст веб-браузера.</param>
    protected virtual void OnContextCreated(IWebBrowserContext context) => ContextCreated?.Invoke(context);

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    /// <param name="context">Контекст веб-браузера.</param>
    protected virtual void OnContextDestroyed(IWebBrowserContext context) => ContextDestroyed?.Invoke(context);

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
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (!disposing) return;

        CloseAsync().AsTask().GetAwaiter().GetResult();
        locker.Dispose();

        ObjectPool<List<IWebBrowserContext>>.Shared.Return(contexts, x => x.Clear());
    }

    internal async ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContext context, CancellationToken cancellationToken)
    {
        context.WindowOpened += OnWindowOpened;
        context.WindowClosed += OnWindowOpened;

        context.PageOpened += OnPageOpened;
        context.PageClosed += OnPageClosed;

        context.Destroyed += async () =>
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            contexts.Remove(context);
            locker.Release();
            OnContextDestroyed(context);
        };

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        contexts.Add(context);
        locker.Release();
        OnContextCreated(context);

        return CurrentContext = context;
    }

    /// <inheritdoc/>
    public virtual ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) || IsClosed, this);
        return CreateContextAsync(new WebBrowserContext(this, contextSettings), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings) => CreateContextAsync(contextSettings, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask<IWebBrowserContext> CreateContextAsync(CancellationToken cancellationToken) => CreateContextAsync(WebBrowserContextSettings.CreateFrom<WebBrowserSettings, WebBrowserContextSettings>((WebBrowserSettings)Settings), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWebBrowserContext> CreateContextAsync() => CreateContextAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return;
        IsClosed = true;

        for (var i = 0; i < contexts.Count; ++i) await contexts[i].DestroyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}