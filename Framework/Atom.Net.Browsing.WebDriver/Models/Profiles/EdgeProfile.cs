using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Microsoft Edge.
/// </summary>
public sealed class EdgeProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Edge с указанным бинарным файлом и каналом.
    /// </summary>
    public EdgeProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Edge с указанным бинарным файлом.
    /// </summary>
    public EdgeProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Edge для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public EdgeProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Edge для стабильного канала.
    /// </summary>
    public EdgeProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Edge по умолчанию для заданного канала.
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
            WebBrowserChannel.Beta => [@"C:\Program Files (x86)\Microsoft\Edge Beta\Application\msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"],
            WebBrowserChannel.Dev => [@"C:\Program Files (x86)\Microsoft\Edge Dev\Application\msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge Beta\Application\msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"],
            _ => [@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge Beta\Application\msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge Dev\Application\msedge.exe"],
        };

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Microsoft Edge Beta.app/Contents/MacOS/Microsoft Edge Beta", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"],
            WebBrowserChannel.Dev => ["/Applications/Microsoft Edge Dev.app/Contents/MacOS/Microsoft Edge Dev", "/Applications/Microsoft Edge Beta.app/Contents/MacOS/Microsoft Edge Beta", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"],
            _ => ["/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/Applications/Microsoft Edge Beta.app/Contents/MacOS/Microsoft Edge Beta", "/Applications/Microsoft Edge Dev.app/Contents/MacOS/Microsoft Edge Dev"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["microsoft-edge-beta", "microsoft-edge-stable", "microsoft-edge"],
            WebBrowserChannel.Dev => ["microsoft-edge-dev", "microsoft-edge-beta", "microsoft-edge-stable", "microsoft-edge"],
            _ => ["microsoft-edge-stable", "microsoft-edge", "microsoft-edge-beta", "microsoft-edge-dev"],
        };
}