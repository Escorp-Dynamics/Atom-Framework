using System.Runtime.InteropServices;
using System.Text;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет наст
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
/// </remarks>
/// <param name="binaryPath">Путь к бинарному файлу.</param>
/// <param name="profile">Настройки профиля для браузера Firefox.</param>
public class FirefoxSettings(string binaryPath, FirefoxProfile profile) : WebBrowserSettings(binaryPath)
{
    private const string DefaultWindowsBinaryPath = "C:\\Program Files\\Mozilla Firefox\\firefox.exe";

    private const string DefaultMacBinaryPath = "/Applications/Firefox.app/Contents/MacOS/firefox";

    private const string DefaultLinuxBinaryPath = "/usr/bin/firefox";

    /// <summary>
    /// Настройки профиля для браузера Firefox.
    /// </summary>
    public FirefoxProfile Profile { get; init; } = profile;

    /// <summary>
    /// Настройки браузера Firefox по умолчанию.
    /// </summary>
    public static FirefoxSettings Default => new(DefaultWindowsBinaryPath);

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    /// <param name="binaryPath">Путь к исполняемому файлу.</param>
    public FirefoxSettings(string binaryPath) : this(binaryPath, FirefoxProfile.Default) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    /// <param name="profile">Настройки профиля для браузера Firefox.</param>
    public FirefoxSettings(FirefoxProfile profile) : this(GetBinaryPath(), profile) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    public FirefoxSettings() : this(FirefoxProfile.Default) { }

    private static string GetBinaryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return DefaultWindowsBinaryPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return DefaultMacBinaryPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return DefaultLinuxBinaryPath;

        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Преобразует текущий экземпляр класса <see cref="FirefoxSettings"/> в строку аргументов запуска браузера Firefox.
    /// </summary>
    /// <returns>Строка аргументов запуска браузера Firefox.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append($"--profile \"{Profile.Path}\" ");
        sb.Append("--no-remote ");

        if (IsHeadless) sb.Append("--headless ");
        if (IsIncognito) sb.Append("--private-window ");

        return sb.ToString().Trim();
    }
}