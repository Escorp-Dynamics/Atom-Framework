using System.Runtime.InteropServices;
using Atom.Buffers;

namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет настройки драйвера Mozilla Firefox.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FirefoxDriverSettings"/>.
/// </remarks>
public partial class FirefoxDriverSettings(IUserProfile profile) : WebDriverSettings(profile), IWebDriverSettings
{
    private static readonly Lazy<FirefoxDriverSettings> defaultSettings = new(() => new FirefoxDriverSettings(), true);

    /// <inheritdoc/>
    public static new FirefoxDriverSettings Default => defaultSettings.Value;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FirefoxDriverSettings"/>.
    /// </summary>
    public FirefoxDriverSettings() : this(FirefoxProfile.Default) { }

    /// <inheritdoc/>
    public override string GetDefaultBinaryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "/Applications/Firefox.app/Contents/MacOS/firefox";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "C:\\Program Files\\Mozilla Firefox\\firefox.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "/usr/bin/firefox";

        return string.Empty;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> CreateArguments()
    {
        var args = ObjectPool<List<string>>.Shared.Rent();
        args.Add("--no-remote");
        args.Add("--console");
        args.Add("--remote-allow-origins=*");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) args.Add("--foreground");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) args.Add("--wait-for-browser");

        args.Add("--profile");
        args.Add(UserDataPath);
        args.Add("--remote-debugging-port");
        args.Add(DebugPort.ToString());

        IEnumerable<string> result = [.. args];
        ObjectPool<List<string>>.Shared.Return(args, x => x.Clear());

        return result;
    }
}