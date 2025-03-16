using System.Diagnostics.CodeAnalysis;
using System.Net;
using Atom.Net.Http;
using Atom.Threading;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет веб-страницу браузера.
/// </summary>
public class WebPage : IWebPage
{
    private readonly Locker locker = new();
    private readonly HttpClientHandler handler;
    private readonly SafetyHttpClient client;

    private bool isDisposed;

    /// <inheritdoc/>
    public event MutableEventHandler<Uri>? Navigate;

    /// <inheritdoc/>
    public event MutableEventHandler<Uri>? Navigated;

    /// <inheritdoc/>
    public event MutableEventHandler? Closed;

    /// <inheritdoc/>
    public IWebWindow Window { get; protected set; }

    /// <inheritdoc/>
    public IWebBrowserContext Context => Window.Context;

    /// <inheritdoc/>
    public IWebBrowser Browser => Context.Browser;

    /// <inheritdoc/>
    public IWebPageSettings Settings { get; set; }

    /// <inheritdoc/>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public string Source { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public Uri Url { get; protected set; } = new Uri("about:blank");

    /// <inheritdoc/>
    public IConsole Console { get; protected set; }

    /// <inheritdoc/>
    public CookieContainer Cookies { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebPage"/>.
    /// </summary>
    /// <param name="window">Окно еб-браузера.</param>
    /// <param name="settings">Настройки веб-страницы.</param>
    protected internal WebPage(IWebWindow window, [NotNull] IWebPageSettings settings)
    {
        Window = window;
        Settings = settings;

        handler = settings.Handler ??= new HttpClientHandler();
        Cookies = handler.CookieContainer;
        client = new SafetyHttpClient(handler, false);

        Console = new BOM.Console();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebPage"/>.
    /// </summary>
    /// <param name="window">Окно еб-браузера.</param>
    protected internal WebPage([NotNull] IWebWindow window) : this(window, WebPageSettings.CreateFrom<WebWindowSettings, WebPageSettings>((WebWindowSettings)window.Settings)) { }

    /// <summary>
    /// Происходит в момент начала навигации по странице.
    /// </summary>
    /// <param name="url">Адрес навигации.</param>
    protected virtual void OnNavigate(Uri url) => Navigate?.Invoke(url);

    /// <summary>
    /// Происходит в момент окончания навигации по странице.
    /// </summary>
    /// <param name="url">Адрес навигации.</param>
    protected virtual void OnNavigated(Uri url) => Navigated?.Invoke(url);

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    protected virtual void OnClosed() => Closed?.Invoke();

    /// <inheritdoc/>
    public async ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) || IsClosed, this);

        OnNavigate(url);
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        OnNavigated(url);

        Source = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        //var root = HtmlParser.Parse(Source);
        return response.StatusCode;
    }

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait) => GoToAsync(url, headers, wait, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) => GoToAsync(url, headers, ReadinessState.Complete, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers) => GoToAsync(url, headers, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, ReadinessState wait, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(referer)) headers["referer"] = referer;
        return GoToAsync(url, headers, wait, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, ReadinessState wait) => GoToAsync(url, referer, wait, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, CancellationToken cancellationToken) => GoToAsync(url, referer, ReadinessState.Complete, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer) => GoToAsync(url, referer, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, ReadinessState wait, CancellationToken cancellationToken) => GoToAsync(url, string.Empty, wait, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, ReadinessState wait) => GoToAsync(url, wait, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken) => GoToAsync(url, ReadinessState.Complete, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return;
        IsClosed = true;

        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);
        OnClosed();
        locker.Release();
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        await CloseAsync().ConfigureAwait(false);
        client.Dispose();
        locker.Dispose();

        GC.SuppressFinalize(this);
    }
}