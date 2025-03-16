namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет браузер Mozilla Firefox на базе драйвера.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FirefoxDriver"/>.
/// </remarks>
/// <param name="settings">Настройки браузера.</param>
public class FirefoxDriver(FirefoxDriverSettings settings) : WebDriver(settings)
{
    private readonly FirefoxDriverSettings settings = settings;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FirefoxDriver"/>.
    /// </summary>
    public FirefoxDriver() : this(FirefoxDriverSettings.Default) { }

    /// <inheritdoc/>
    public override async ValueTask<IWebBrowserContext> CreateContextAsync(IWebBrowserContextSettings contextSettings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);

        if (contextSettings is not IWebDriverContextSettings s) throw new InvalidOperationException("Допускаются только настройки типа IWebDriverContextSettings");

        s.Update();
        if (s.Profile is not null) await s.Profile.SaveAsync(s.UserDataPath, cancellationToken).ConfigureAwait(false);

        var context = new FirefoxDriverContext(this, s);

        await context.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return await CreateContextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override ValueTask<IWebBrowserContext> CreateContextAsync(CancellationToken cancellationToken) => CreateContextAsync(FirefoxDriverContextSettings.CreateFrom<FirefoxDriverSettings, FirefoxDriverContextSettings>(settings), cancellationToken);
}