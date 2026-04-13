using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Yandex Browser.
/// </summary>
public sealed class YandexProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Yandex Browser с указанным бинарным файлом и каналом.
    /// </summary>
    public YandexProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Yandex Browser с указанным бинарным файлом.
    /// </summary>
    public YandexProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Yandex Browser для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public YandexProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Yandex Browser для стабильного канала.
    /// </summary>
    public YandexProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Yandex Browser по умолчанию для заданного канала.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Browser install candidates are intentional OS-specific defaults.")]
    private static new string GetDefaultBinaryPath(WebBrowserChannel channel)
    {
        var candidates = GetCandidates(channel);

        return ResolveInstalledBinary(candidates);
    }

    private static IEnumerable<string> GetCandidates(WebBrowserChannel channel)
    {
        if (OperatingSystem.IsWindows())
            return GetWindowsCandidates(channel);

        if (OperatingSystem.IsMacOS())
            return GetMacCandidates(channel);

        return GetLinuxCandidates(channel);
    }

    private static IEnumerable<string> GetWindowsCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => [@"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser Beta\Application\browser.exe", @"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser\Application\browser.exe"],
            WebBrowserChannel.Dev => [@"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser Dev\Application\browser.exe", @"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser Beta\Application\browser.exe", @"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser\Application\browser.exe"],
            _ => [@"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser\Application\browser.exe", @"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser Beta\Application\browser.exe", @"C:\Users\%USERNAME%\AppData\Local\Yandex\YandexBrowser Dev\Application\browser.exe"],
        };

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Yandex Beta.app/Contents/MacOS/Yandex Beta", "/Applications/Yandex.app/Contents/MacOS/Yandex"],
            WebBrowserChannel.Dev => ["/Applications/Yandex Dev.app/Contents/MacOS/Yandex Dev", "/Applications/Yandex Beta.app/Contents/MacOS/Yandex Beta", "/Applications/Yandex.app/Contents/MacOS/Yandex"],
            _ => ["/Applications/Yandex.app/Contents/MacOS/Yandex", "/Applications/Yandex Beta.app/Contents/MacOS/Yandex Beta", "/Applications/Yandex Dev.app/Contents/MacOS/Yandex Dev"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["yandex-browser-beta", "yandex-browser-corporate", "yandex-browser"],
            WebBrowserChannel.Dev => ["yandex-browser-dev", "yandex-browser-beta", "yandex-browser-corporate", "yandex-browser"],
            _ => ["yandex-browser-corporate", "yandex-browser", "/opt/yandex/browser/yandex-browser", "yandex-browser-beta", "yandex-browser-dev"],
        };
}