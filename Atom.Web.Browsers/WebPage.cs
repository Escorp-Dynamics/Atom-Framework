using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Net.Http;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет веб-страницу браузера.
/// </summary>
public class WebPage : IWebPage
{
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly HttpClientHandler handler;
    private readonly SafetyHttpClient client;

    private bool isDisposed;

    /// <inheritdoc/>
    public event AsyncEventHandler? Closed;

    /// <inheritdoc/>
    public event AsyncEventHandler<Uri>? Navigate;

    /// <inheritdoc/>
    public event AsyncEventHandler<Uri>? Navigated;

    /// <inheritdoc/>
    public IWebBrowserContext Context { get; }

    /// <inheritdoc/>
    public IWebPageSettings Settings { get; set; }

    /// <inheritdoc/>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public string Source { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public Uri UrL { get; protected set; } = new Uri("about:blank");

    /// <inheritdoc/>
    public IConsole Console { get; protected set; }

    /// <inheritdoc/>
    public CookieContainer Cookies { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebPage"/>.
    /// </summary>
    /// <param name="context">Контекст веб-страницы.</param>
    /// <param name="settings">Настройки веб-страницы.</param>
    protected internal WebPage(IWebBrowserContext context, [NotNull] IWebPageSettings settings)
    {
        Context = context;
        Settings = settings;

        handler = settings.Handler ??= new HttpClientHandler();
        Cookies = handler.CookieContainer;
        client = new SafetyHttpClient(handler, false);

        Console = new BOM.Console();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebPage"/>.
    /// </summary>
    /// <param name="context">Контекст веб-страницы.</param>
    protected internal WebPage([NotNull] IWebBrowserContext context) : this(context, WebPageSettings.FromContextSettings(context.Settings)) { }

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    protected virtual ValueTask OnClosed() => Closed.On();

    /// <summary>
    /// Происходит в момент начала навигации по странице.
    /// </summary>
    /// <param name="url">Адрес навигации.</param>
    protected virtual ValueTask OnNavigate(Uri url) => Navigate.On(url);

    /// <summary>
    /// Происходит в момент окончания навигации по странице.
    /// </summary>
    /// <param name="url">Адрес навигации.</param>
    protected virtual ValueTask OnNavigated(Uri url) => Navigated.On(url);

    /// <inheritdoc/>
    public async ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed || IsClosed, this);

        await OnNavigate(url).ConfigureAwait(false);
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await OnNavigated(url).ConfigureAwait(false);

        Source = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        //var root = HtmlParser.Parse(Source);
        return response.StatusCode;
    }

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return;
        IsClosed = true;

        ObjectDisposedException.ThrowIf(isDisposed, this);
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        await OnClosed().ConfigureAwait(false);
        locker.Release();
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        isDisposed = true;

        await CloseAsync().ConfigureAwait(false);
        client.Dispose();
        locker.Dispose();

        GC.SuppressFinalize(this);
    }
}