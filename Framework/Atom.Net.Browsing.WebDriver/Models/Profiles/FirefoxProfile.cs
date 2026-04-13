using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Mozilla Firefox.
/// </summary>
public class FirefoxProfile : WebBrowserProfile
{
    /// <summary>
    /// Инициализирует профиль Firefox с указанным бинарным файлом и каналом.
    /// </summary>
    public FirefoxProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Firefox с указанным бинарным файлом.
    /// </summary>
    public FirefoxProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Firefox для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public FirefoxProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Firefox для стабильного канала.
    /// </summary>
    public FirefoxProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Firefox по умолчанию для заданного канала.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Browser install candidates are intentional OS-specific defaults.")]
    private static string GetDefaultBinaryPath(WebBrowserChannel channel)
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
            WebBrowserChannel.Beta => [@"C:\Program Files\Mozilla Firefox Beta\firefox.exe", @"C:\Program Files\Mozilla Firefox\firefox.exe"],
            WebBrowserChannel.Dev => [@"C:\Program Files\Firefox Developer Edition\firefox.exe", @"C:\Program Files\Mozilla Firefox Beta\firefox.exe", @"C:\Program Files\Mozilla Firefox\firefox.exe"],
            _ => [@"C:\Program Files\Mozilla Firefox\firefox.exe", @"C:\Program Files\Mozilla Firefox Beta\firefox.exe", @"C:\Program Files\Firefox Developer Edition\firefox.exe"],
        };

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Firefox Beta.app/Contents/MacOS/firefox", "/Applications/Firefox.app/Contents/MacOS/firefox"],
            WebBrowserChannel.Dev => ["/Applications/Firefox Developer Edition.app/Contents/MacOS/firefox", "/Applications/Firefox Beta.app/Contents/MacOS/firefox", "/Applications/Firefox.app/Contents/MacOS/firefox"],
            _ => ["/Applications/Firefox.app/Contents/MacOS/firefox", "/Applications/Firefox Beta.app/Contents/MacOS/firefox", "/Applications/Firefox Developer Edition.app/Contents/MacOS/firefox"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["firefox-beta", "firefox"],
            WebBrowserChannel.Dev => ["firefox-developer-edition", "firefox-beta", "firefox"],
            _ => ["firefox", "firefox-beta", "firefox-developer-edition"],
        };
}