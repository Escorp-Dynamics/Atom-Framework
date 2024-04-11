namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет браузер Firefox.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
/// </remarks>
/// <param name="settings">Настройки браузера.</param>
public class FirefoxBrowser(FirefoxSettings settings) : WebBrowser<FirefoxSettings>(settings)
{
    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
    /// </summary>
    public FirefoxBrowser() : this(FirefoxSettings.Default) { }
}