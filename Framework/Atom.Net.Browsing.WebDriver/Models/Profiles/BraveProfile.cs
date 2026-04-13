using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Brave.
/// </summary>
public sealed class BraveProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Brave с указанным бинарным файлом и каналом.
    /// </summary>
    public BraveProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Brave с указанным бинарным файлом.
    /// </summary>
    public BraveProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Brave для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public BraveProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Brave для стабильного канала.
    /// </summary>
    public BraveProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Brave по умолчанию для заданного канала.
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
            WebBrowserChannel.Beta => [@"C:\Program Files\BraveSoftware\Brave-Browser-Beta\Application\brave.exe", @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"],
            WebBrowserChannel.Dev => [@"C:\Program Files\BraveSoftware\Brave-Browser-Nightly\Application\brave.exe", @"C:\Program Files\BraveSoftware\Brave-Browser-Beta\Application\brave.exe", @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"],
            _ => [@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe", @"C:\Program Files\BraveSoftware\Brave-Browser-Beta\Application\brave.exe", @"C:\Program Files\BraveSoftware\Brave-Browser-Nightly\Application\brave.exe"],
        };

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Brave Browser Beta.app/Contents/MacOS/Brave Browser Beta", "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"],
            WebBrowserChannel.Dev => ["/Applications/Brave Browser Nightly.app/Contents/MacOS/Brave Browser Nightly", "/Applications/Brave Browser Beta.app/Contents/MacOS/Brave Browser Beta", "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"],
            _ => ["/Applications/Brave Browser.app/Contents/MacOS/Brave Browser", "/Applications/Brave Browser Beta.app/Contents/MacOS/Brave Browser Beta", "/Applications/Brave Browser Nightly.app/Contents/MacOS/Brave Browser Nightly"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["brave-browser-beta", "brave", "brave-browser", "brave-browser-stable"],
            WebBrowserChannel.Dev => ["brave-browser-nightly", "brave-nightly", "brave-browser-beta", "brave", "brave-browser", "brave-browser-stable"],
            _ => ["brave", "brave-browser", "brave-browser-stable", "brave-browser-beta", "brave-browser-nightly"],
        };
}