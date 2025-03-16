namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет настройки окна браузера Mozilla Firefox.
/// </summary>
public class FirefoxWindowSettings : FirefoxDriverContextSettings, IWebDriverWindowSettings
{
    private static readonly Lazy<FirefoxWindowSettings> defaultSettings = new(() => new FirefoxWindowSettings(), true);

    /// <inheritdoc/>
    public static new FirefoxWindowSettings Default => defaultSettings.Value;

    static IWebDriverWindowSettings IWebDriverWindowSettings.Default => Default;

    static IWebWindowSettings IWebWindowSettings.Default => Default;
}