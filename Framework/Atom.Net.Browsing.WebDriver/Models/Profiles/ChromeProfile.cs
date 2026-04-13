using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Google Chrome.
/// </summary>
public class ChromeProfile : WebBrowserProfile
{
    /// <summary>
    /// Инициализирует профиль Chrome с указанным бинарным файлом и каналом.
    /// </summary>
    public ChromeProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Chrome с указанным бинарным файлом.
    /// </summary>
    public ChromeProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Chrome для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public ChromeProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Chrome для стабильного канала.
    /// </summary>
    public ChromeProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Chrome по умолчанию для заданного канала.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Browser install candidates are intentional OS-specific defaults.")]
    protected static string GetDefaultBinaryPath(WebBrowserChannel channel)
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
            WebBrowserChannel.Beta => [@"C:\Program Files\Google\Chrome Beta\Application\chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe"],
            WebBrowserChannel.Dev => [@"C:\Program Files\Google\Chrome Dev\Application\chrome.exe", @"C:\Program Files\Google\Chrome Beta\Application\chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe"],
            _ => [@"C:\Program Files\Google\Chrome\Application\chrome.exe", @"C:\Program Files\Google\Chrome Beta\Application\chrome.exe", @"C:\Program Files\Google\Chrome Dev\Application\chrome.exe"],
        };

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Google Chrome Beta.app/Contents/MacOS/Google Chrome Beta", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"],
            WebBrowserChannel.Dev => ["/Applications/Google Chrome Dev.app/Contents/MacOS/Google Chrome Dev", "/Applications/Google Chrome Beta.app/Contents/MacOS/Google Chrome Beta", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"],
            _ => ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Google Chrome Beta.app/Contents/MacOS/Google Chrome Beta", "/Applications/Google Chrome Dev.app/Contents/MacOS/Google Chrome Dev"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["google-chrome-beta", "google-chrome-stable", "google-chrome"],
            WebBrowserChannel.Dev => ["google-chrome-unstable", "google-chrome-dev", "google-chrome-beta", "google-chrome-stable", "google-chrome"],
            _ => ["google-chrome-stable", "google-chrome", "google-chrome-beta", "google-chrome-unstable", "google-chrome-dev"],
        };
}