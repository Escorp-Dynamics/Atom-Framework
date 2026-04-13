using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Vivaldi.
/// </summary>
public sealed class VivaldiProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Vivaldi с указанным бинарным файлом и каналом.
    /// </summary>
    public VivaldiProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Vivaldi с указанным бинарным файлом.
    /// </summary>
    public VivaldiProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Vivaldi для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public VivaldiProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Vivaldi для стабильного канала.
    /// </summary>
    public VivaldiProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Vivaldi по умолчанию для заданного канала.
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
            return GetWindowsCandidates();

        if (OperatingSystem.IsMacOS())
            return GetMacCandidates(channel);

        return GetLinuxCandidates(channel);
    }

    private static IEnumerable<string> GetWindowsCandidates()
        => [@"C:\Users\%USERNAME%\AppData\Local\Vivaldi\Application\vivaldi.exe"];

    private static IEnumerable<string> GetMacCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["/Applications/Vivaldi Snapshot.app/Contents/MacOS/Vivaldi Snapshot", "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi"],
            WebBrowserChannel.Dev => ["/Applications/Vivaldi Snapshot.app/Contents/MacOS/Vivaldi Snapshot", "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi"],
            _ => ["/Applications/Vivaldi.app/Contents/MacOS/Vivaldi", "/Applications/Vivaldi Snapshot.app/Contents/MacOS/Vivaldi Snapshot"],
        };

    private static IEnumerable<string> GetLinuxCandidates(WebBrowserChannel channel)
        => channel switch
        {
            WebBrowserChannel.Beta => ["vivaldi-snapshot", "vivaldi-stable", "vivaldi"],
            WebBrowserChannel.Dev => ["vivaldi-snapshot", "vivaldi-stable", "vivaldi"],
            _ => ["vivaldi-stable", "vivaldi", "vivaldi-snapshot"],
        };
}