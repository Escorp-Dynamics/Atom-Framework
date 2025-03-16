namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет контекст драйвера для браузера Mozilla Firefox.
/// </summary>
public class FirefoxDriverContextSettings : FirefoxDriverSettings, IWebDriverContextSettings
{
    private static readonly Lazy<FirefoxDriverContextSettings> defaultSettings = new(() => new FirefoxDriverContextSettings(), true);

    /// <inheritdoc/>
    public static new FirefoxDriverContextSettings Default => defaultSettings.Value;

    static IWebDriverContextSettings IWebDriverContextSettings.Default => Default;

    static IWebBrowserContextSettings IWebBrowserContextSettings.Default => Default;

    /// <inheritdoc/>
    public static TResult CreateFrom<TBase, TResult>(TBase baseSettings)
        where TBase : IWebBrowserSettings
        where TResult : IWebBrowserContextSettings, new()
    {
        var wdSettings = baseSettings as IWebDriverSettings;

        var settings = new TResult
        {
            Logger = baseSettings.Logger,
            Cookies = baseSettings.Cookies,
            Handler = baseSettings.Handler,
            IsDOMEnabled = baseSettings.IsDOMEnabled,
            IsJavaScriptEnabled = baseSettings.IsJavaScriptEnabled,
            Proxy = baseSettings.Proxy,
        };

        if (settings is IWebDriverContextSettings wdcSettings)
        {
            wdcSettings.BinaryPath = wdSettings?.BinaryPath ?? string.Empty;
            wdcSettings.UserDataPath = wdSettings?.UserDataPath ?? string.Empty;
            wdcSettings.DebugPort = wdSettings?.DebugPort ?? default;
            wdcSettings.Mode = wdSettings?.Mode ?? WebDriverMode.Default;
            wdcSettings.Profile = wdSettings?.Profile;
        }

        return settings;
    }
}