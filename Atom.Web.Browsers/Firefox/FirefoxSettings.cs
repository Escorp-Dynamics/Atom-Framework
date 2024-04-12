using System.Runtime.InteropServices;
using System.Text;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет наст
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
/// </remarks>
/// <param name="binaryPath">Путь к бинарному файлу браузера Firefox.</param>
/// <param name="distributionPath">Путь к дистрибутиву браузера Firefox.</param>
/// <param name="profile">Настройки профиля для браузера Firefox.</param>
public class FirefoxSettings(string binaryPath, string distributionPath, FirefoxProfile profile) : WebBrowserSettings(binaryPath, distributionPath)
{
    private const string DefaultWindowsBinaryPath = "C:\\Program Files\\Mozilla Firefox\\firefox.exe";

    private const string DefaultWindowsDistributionPath = "C:\\Program Files\\Mozilla Firefox\\";

    private const string DefaultMacBinaryPath = "/Applications/Firefox.app/Contents/MacOS/firefox";

    private const string DefaultMacDistributionPath = "/Applications/Firefox.app/Contents/Resources/";

    private const string DefaultLinuxBinaryPath = "/usr/bin/firefox";

    private const string DefaultLinuxDistributionPath = "/usr/lib/firefox/";

    /// <summary>
    /// Настройки профиля для браузера Firefox.
    /// </summary>
    public FirefoxProfile Profile { get; init; } = profile;

    /// <summary>
    /// Настройки браузера Firefox по умолчанию.
    /// </summary>
    public static FirefoxSettings Default => new();

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    /// <param name="binaryPath">Путь к исполняемому файлу.</param>
    /// <param name="distributionPath">Путь к дистрибутиву браузера Firefox.</param>
    public FirefoxSettings(string binaryPath, string distributionPath) : this(binaryPath, distributionPath, FirefoxProfile.Default) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    /// <param name="profile">Настройки профиля для браузера Firefox.</param>
    public FirefoxSettings(FirefoxProfile profile) : this(GetBinaryPath(), GetDistributionPath(), profile) { }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxSettings"/>.
    /// </summary>
    public FirefoxSettings() : this(FirefoxProfile.Default) { }

    /// <inheritdoc/>
    public override string GetNativeBinaryPath() => GetBinaryPath();

    /// <inheritdoc/>
    public override string GetNativeDistributionPath() => GetDistributionPath();

    /// <summary>
    /// Преобразует текущий экземпляр класса <see cref="FirefoxSettings"/> в строку аргументов запуска браузера Firefox.
    /// </summary>
    /// <returns>Строка аргументов запуска браузера Firefox.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append($"--profile \"{Profile.Path}\" ");
        sb.Append("--no-remote ");
        sb.Append("--new-instance ");
        //sb.Append("--safe-mode ");

        if (IsHeadless) sb.Append("--headless ");
        if (IsIncognito) sb.Append("--private-window ");

        return sb.ToString().Trim();
    }

    private static string GetBinaryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return DefaultWindowsBinaryPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return DefaultMacBinaryPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return DefaultLinuxBinaryPath;

        throw new PlatformNotSupportedException();
    }

    private static string GetDistributionPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return DefaultWindowsDistributionPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return DefaultMacDistributionPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return DefaultLinuxDistributionPath;

        throw new PlatformNotSupportedException();
    }
}