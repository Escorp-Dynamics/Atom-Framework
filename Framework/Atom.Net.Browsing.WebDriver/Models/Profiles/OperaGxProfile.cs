using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль браузера Opera GX.
/// </summary>
public sealed class OperaGxProfile : ChromeProfile
{
    /// <summary>
    /// Инициализирует профиль Opera GX с указанным бинарным файлом и каналом.
    /// </summary>
    public OperaGxProfile(string binaryPath, WebBrowserChannel channel)
        : base(binaryPath, channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera GX с указанным бинарным файлом.
    /// </summary>
    public OperaGxProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera GX для заданного канала с бинарным путём по умолчанию.
    /// </summary>
    public OperaGxProfile(WebBrowserChannel channel)
        : base(GetDefaultBinaryPath(channel), channel)
    {
    }

    /// <summary>
    /// Инициализирует профиль Opera GX для стабильного канала.
    /// </summary>
    public OperaGxProfile()
        : this(WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Возвращает путь к бинарному файлу Opera GX по умолчанию для заданного канала.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Browser install candidates are intentional OS-specific defaults.")]
    private static new string GetDefaultBinaryPath(WebBrowserChannel channel)
        => ResolveInstalledBinary(GetChromiumCandidates(channel, @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera GX\opera.exe", @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera GX\opera.exe", @"C:\Users\%USERNAME%\AppData\Local\Programs\Opera GX\opera.exe", "/Applications/Opera GX.app/Contents/MacOS/Opera GX", "/Applications/Opera GX.app/Contents/MacOS/Opera GX", "/Applications/Opera GX.app/Contents/MacOS/Opera GX", "opera-gx", "opera-gx-stable", "opera-gx", flatpakApplicationId: "com.opera.OperaGX"));
}
