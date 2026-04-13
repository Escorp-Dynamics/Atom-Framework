using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Opera.
/// </summary>
public sealed class OperaProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Opera с указанным бинарным файлом и каналом.
    /// </summary>
    public OperaProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera с указанным бинарным файлом.
    /// </summary>
    public OperaProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public OperaProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera для стабильного канала.
    /// </summary>
    public OperaProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Opera по умолчанию для заданного канала.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Browser install candidates are intentional OS-specific defaults.")]
    private static new string GetDefaultBinaryPath(WebBrowserChannel channel)
        => ResolveInstalledBinary(GetChromiumCandidates(channel, @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera\opera.exe", @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera beta\opera.exe", @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera developer\opera.exe", "/Applications/Opera.app/Contents/MacOS/Opera", "/Applications/Opera Beta.app/Contents/MacOS/Opera Beta", "/Applications/Opera Developer.app/Contents/MacOS/Opera Developer", "opera", "opera-beta", "opera-developer"));
}