using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Threading;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет окно веб-браузера.
/// </summary>
public class WebWindow : IWebWindow
{
    private readonly Locker locker = new();
    private readonly List<IWebPage> pages = [];

    private bool isDisposed;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageOpened;

    /// <inheritdoc/>
    public event MutableEventHandler<IWebPage>? PageClosed;

    /// <inheritdoc/>
    public event MutableEventHandler? Closed;

    /// <inheritdoc/>
    public IWebWindowSettings Settings { get; protected set; }

    /// <inheritdoc/>
    public IWebBrowserContext Context { get; protected set; }

    /// <inheritdoc/>
    public IWebBrowser Browser => Context.Browser;

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => pages;

    /// <inheritdoc/>
    [field: MaybeNull]
    public IWebPage CurrentPage
    {
        get;

        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
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
    /// Инициализирует новый экземпляр <see cref="WebWindow"/>.
    /// </summary>
    /// <param name="context">Экземпляр контекста веб-браузера, в котором было открыто текущее окно.</param>
    /// <param name="settings">Настройки окна веб-браузера.</param>
    protected internal WebWindow(IWebBrowserContext context, IWebWindowSettings settings)
    {
        Context = context;
        Settings = settings;
    }

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
    protected virtual void OnClosed() => Closed?.Invoke();

    /// <inheritdoc/>
    public async ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) || IsClosed, this);
        var page = new WebPage(this, settings);

        page.Closed += async () =>
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            pages.Remove(page);
            locker.Release();
            OnPageClosed(page);
        };

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        pages.Add(page);
        locker.Release();
        OnPageOpened(page);

        return CurrentPage = page;
    }

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(IWebPageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken) => OpenPageAsync(WebPageSettings.CreateFrom<WebWindowSettings, WebPageSettings>((WebWindowSettings)Settings), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWebPage> OpenPageAsync() => OpenPageAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsClosed) return;
        IsClosed = true;

        for (var i = 0; i < pages.Count; ++i) await pages[i].CloseAsync(cancellationToken).ConfigureAwait(false);
        OnClosed();
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        await CloseAsync().ConfigureAwait(false);
        locker.Dispose();

        GC.SuppressFinalize(this);
    }
}