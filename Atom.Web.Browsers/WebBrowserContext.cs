using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет контекст веб-браузера.
/// </summary>
public class WebBrowserContext : IWebBrowserContext
{
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly List<IWebPage> pages = [];

    private bool isDisposed;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage>? PageOpened;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebPage>? PageClosed;

    /// <inheritdoc/>
    public event AsyncEventHandler? Closed;

    /// <inheritdoc/>
    public IWebBrowser Browser { get; }

    /// <inheritdoc/>
    public IWebBrowserContextSettings Settings { get; set; }

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => pages;

    /// <inheritdoc/>
    public IWebPage CurrentPage { get; set; }

    /// <inheritdoc/>
    public string Source => CurrentPage.Source;

    /// <inheritdoc/>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public IConsole Console => CurrentPage.Console;

    /// <inheritdoc/>
    public CookieContainer Cookies => CurrentPage.Cookies;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserContext"/>.
    /// </summary>
    /// <param name="browser">Экземпляр веб-браузера, в котором был создан текущий контекст.</param>
    /// <param name="settings">Настройки контекста веб-браузера.</param>
    protected internal WebBrowserContext(IWebBrowser browser, IWebBrowserContextSettings settings)
    {
        Browser = browser;
        Settings = settings;
        CurrentPage = OpenPageAsync(WebPageSettings.FromContextSettings(settings)).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebBrowserContext"/>.
    /// </summary>
    /// <param name="browser">Экземпляр веб-браузера, в котором был создан текущий контекст.</param>
    protected internal WebBrowserContext([NotNull] IWebBrowser browser) : this(browser, WebBrowserContextSettings.FromBrowserSettings(browser.Settings)) { }

    /// <summary>
    /// Происходит в момент открытия веб-страницы.
    /// </summary>
    protected virtual ValueTask OnPageOpened(IWebPage page) => PageOpened.On(page);

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    protected virtual ValueTask OnPageClosed(IWebPage page) => PageClosed.On(page);

    /// <summary>
    /// Происходит в момент закрытия контекста веб-браузера.
    /// </summary>
    protected virtual ValueTask OnClosed() => Closed.On();

    /// <inheritdoc/>
    public async ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed || IsClosed, this);
        var page = new WebPage(this, settings);

        page.Closed += async () =>
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            pages.Remove(page);
            locker.Release();
            await OnPageClosed(page).ConfigureAwait(false);
        };

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        pages.Add(page);
        await OnPageOpened(page).ConfigureAwait(false);
        locker.Release();

        return CurrentPage = page;
    }

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken) => OpenPageAsync(WebPageSettings.FromContextSettings(Settings), cancellationToken);

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

        for (var i = 0; i < pages.Count; ++i) await pages[i].CloseAsync(cancellationToken).ConfigureAwait(false);
        await OnClosed().ConfigureAwait(false);
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