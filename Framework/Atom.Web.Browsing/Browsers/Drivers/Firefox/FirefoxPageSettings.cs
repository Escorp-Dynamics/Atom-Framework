namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет настройки страницы драйвера браузера Mozilla Firefox.
/// </summary>
public class FirefoxPageSettings : FirefoxWindowSettings, IWebDriverPageSettings
{
    private static readonly Lazy<FirefoxPageSettings> defaultSettings = new(() => new FirefoxPageSettings(), true);

    /// <inheritdoc/>
    public static new FirefoxPageSettings Default => defaultSettings.Value;

    static IWebDriverPageSettings IWebDriverPageSettings.Default => Default;

    static IWebPageSettings IWebPageSettings.Default => Default;
}