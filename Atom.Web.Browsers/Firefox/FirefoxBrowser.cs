namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет браузер Firefox.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
/// </remarks>
/// <param name="settings">Настройки браузера.</param>
public class FirefoxBrowser(FirefoxSettings settings) : WebBrowser<FirefoxSettings, FirefoxServer>(settings)
{
    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
    /// </summary>
    public FirefoxBrowser() : this(FirefoxSettings.Default) { }

    /// <inheritdoc/>
    protected override async ValueTask StartProcessAsync(CancellationToken cancellationToken)
    {
        await Settings.Profile.SaveAsync(cancellationToken).ConfigureAwait(false);
        await base.StartProcessAsync(cancellationToken).ConfigureAwait(false);
    }
}