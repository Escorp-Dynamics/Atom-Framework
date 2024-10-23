using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет веб-браузер.
/// </summary>
public class WebBrowser : IWebBrowser
{
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly List<IWebBrowserContext> contexts = [];

    private bool isDisposed;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserContext>? ContextCreated;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserContext>? ContextClosed;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage>? PageOpened;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage>? PageClosed;

    /// <inheritdoc/>
    public IWebBrowserSettings Settings { get; set; }

    /// <inheritdoc/>
    public IEnumerable<IWebBrowserContext> Contexts => contexts;

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => Contexts.SelectMany(x => x.Pages);

    /// <inheritdoc/>
    public IWebBrowserContext CurrentContext { get; set; }

    /// <inheritdoc/>
    public IWebPage CurrentPage
    {
        get => CurrentContext.CurrentPage;

        set
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
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public CookieContainer Cookies => CurrentPage.Cookies;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowser"/>.
    /// </summary>
    /// <param name="settings">Настройки веб-браузера.</param>
    public WebBrowser([NotNull] IWebBrowserSettings settings)
    {
        Settings = settings;
        CurrentContext = CreateContextAsync(WebBrowserContextSettings.FromBrowserSettings(settings)).AsTask().GetAwaiter().GetResult();
        CurrentPage = CurrentContext.CurrentPage;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowser"/>.
    /// </summary>
    public WebBrowser() : this(WebBrowserSettings.Default) { }

    /// <summary>
    /// Происходит в момент создания контекста веб-браузера.
    /// </summary>
    /// <param name="context">Контекст веб-браузера.</param>
    protected virtual ValueTask OnContextCreated(IWebBrowserContext context) => ContextCreated?.Invoke(context) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    /// <param name="context">Контекст веб-браузера.</param>
    protected virtual ValueTask OnContextClosed(IWebBrowserContext context) => ContextClosed?.Invoke(context) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    /// <param name="page">Веб-страница.</param>
    protected virtual ValueTask OnPageOpened(IWebPage page) => PageOpened?.Invoke(page) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    /// <param name="page">Веб-страница.</param>
    protected virtual ValueTask OnPageClosed(IWebPage page) => PageClosed?.Invoke(page) ?? ValueTask.CompletedTask;

    /// <inheritdoc/>
    public async ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed || IsClosed, this);

        var context = new WebBrowserContext(this, contextSettings);
        context.PageOpened += OnPageOpened;
        context.PageClosed += OnPageClosed;

        context.Closed += async () =>
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            contexts.Remove(context);
            locker.Release();
            await OnContextClosed(context).ConfigureAwait(false);
        };

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        contexts.Add(context);
        await OnContextCreated(context).ConfigureAwait(false);
        locker.Release();

        return CurrentContext = context;
    }

    /// <inheritdoc/>
    public ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings) => CreateContextAsync(contextSettings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWebBrowserContext> CreateContextAsync(CancellationToken cancellationToken) => CreateContextAsync(WebBrowserContextSettings.FromBrowserSettings(Settings), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWebBrowserContext> CreateContextAsync() => CreateContextAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(IWebPageSettings pageSettings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed && !IsClosed, this);
        return CurrentContext.OpenPageAsync(pageSettings, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(IWebPageSettings pageSettings) => OpenPageAsync(pageSettings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken) => OpenPageAsync(WebPageSettings.FromContextSettings(CurrentContext.Settings), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync() => OpenPageAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed || IsClosed, this);
        return CurrentPage.GoToAsync(url, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return;
        IsClosed = true;

        for (var i = 0; i < contexts.Count; ++i) await contexts[i].CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        isDisposed = true;

        await CloseAsync().ConfigureAwait(false);
        locker.Dispose();

        GC.SuppressFinalize(this);
    }
}