using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет базовый профиль браузера с путями и каналом установки.
/// </summary>
public abstract class WebBrowserProfile
{
    private string binaryPath;

    /// <summary>
    /// Получает профиль Google Chrome по умолчанию.
    /// </summary>
    public static WebBrowserProfile Chrome { get; } = new ChromeProfile();

    /// <summary>
    /// Получает профиль Microsoft Edge по умолчанию.
    /// </summary>
    public static WebBrowserProfile Edge { get; } = new EdgeProfile();

    /// <summary>
    /// Получает профиль Brave по умолчанию.
    /// </summary>
    public static WebBrowserProfile Brave { get; } = new BraveProfile();

    /// <summary>
    /// Получает профиль Opera по умолчанию.
    /// </summary>
    public static WebBrowserProfile Opera { get; } = new OperaProfile();

    /// <summary>
    /// Получает профиль Vivaldi по умолчанию.
    /// </summary>
    public static WebBrowserProfile Vivaldi { get; } = new VivaldiProfile();

    /// <summary>
    /// Получает профиль Yandex Browser по умолчанию.
    /// </summary>
    public static WebBrowserProfile Yandex { get; } = new YandexProfile();

    /// <summary>
    /// Получает профиль Firefox по умолчанию.
    /// </summary>
    public static WebBrowserProfile Firefox { get; } = new FirefoxProfile();

    /// <summary>
    /// Инициализирует профиль браузера с указанным бинарным файлом и каналом.
    /// </summary>
    protected WebBrowserProfile(string binaryPath, WebBrowserChannel channel)
    {
        this.binaryPath = binaryPath;
        Channel = channel;
        RefreshInstallationState();
    }

    /// <summary>
    /// Инициализирует профиль браузера с указанным бинарным файлом и стабильным каналом.
    /// </summary>
    protected WebBrowserProfile(string binaryPath)
        : this(binaryPath, WebBrowserChannel.Stable)
    {
    }

    /// <summary>
    /// Получает или задаёт путь к данным профиля.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Получает или задаёт путь к бинарному файлу браузера.
    /// </summary>
    public string BinaryPath
    {
        get => binaryPath;
        set
        {
            binaryPath = value;
            RefreshInstallationState();
        }
    }

    /// <summary>
    /// Получает или задаёт канал браузера.
    /// </summary>
    public WebBrowserChannel Channel { get; set; }

    /// <summary>
    /// Получает признак того, что браузер установлен в системе.
    /// </summary>
    public bool IsInstalled { get; protected set; }

    /// <summary>
    /// Разрешает путь к бинарному файлу через набор кандидатов и системный PATH.
    /// </summary>
    protected static string ResolveInstalledBinary(IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var candidate in candidates)
        {
            var resolved = TryResolveCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return string.Empty;
    }

    /// <summary>
    /// Формирует кандидатов для Chromium-подобных браузеров по каналу и платформе.
    /// </summary>
    protected static IEnumerable<string> GetChromiumCandidates(
        WebBrowserChannel channel,
        string stableWindowsPath,
        string betaWindowsPath,
        string devWindowsPath,
        string stableMacPath,
        string betaMacPath,
        string devMacPath,
        string stableLinuxBinary,
        string betaLinuxBinary,
        string devLinuxBinary)
    {
        if (OperatingSystem.IsWindows())
        {
            return channel switch
            {
                WebBrowserChannel.Beta => [betaWindowsPath, stableWindowsPath],
                WebBrowserChannel.Dev => [devWindowsPath, betaWindowsPath, stableWindowsPath],
                _ => [stableWindowsPath, betaWindowsPath, devWindowsPath],
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return channel switch
            {
                WebBrowserChannel.Beta => [betaMacPath, stableMacPath],
                WebBrowserChannel.Dev => [devMacPath, betaMacPath, stableMacPath],
                _ => [stableMacPath, betaMacPath, devMacPath],
            };
        }

        return channel switch
        {
            WebBrowserChannel.Beta => [betaLinuxBinary, stableLinuxBinary],
            WebBrowserChannel.Dev => [devLinuxBinary, betaLinuxBinary, stableLinuxBinary],
            _ => [stableLinuxBinary, betaLinuxBinary, devLinuxBinary],
        };
    }

    private void RefreshInstallationState()
        => IsInstalled = !string.IsNullOrWhiteSpace(binaryPath) && File.Exists(binaryPath);

    private static string TryResolveCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(candidate);
        if (File.Exists(expanded))
            return expanded;

        if (ContainsDirectorySeparators(expanded))
            return string.Empty;

        return TryResolveFromPath(expanded);
    }

    private static bool ContainsDirectorySeparators(string value)
        => value.Contains(IOPath.DirectorySeparatorChar, StringComparison.Ordinal)
            || value.Contains(IOPath.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static string TryResolveFromPath(string candidate)
    {
        var pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnvironment))
            return string.Empty;

        foreach (var segment in pathEnvironment.Split(IOPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var resolved = IOPath.Combine(segment, candidate);
            var executable = TryResolveExistingFile(resolved);
            if (!string.IsNullOrWhiteSpace(executable))
                return executable;
        }

        return string.Empty;
    }

    private static string TryResolveExistingFile(string path)
    {
        if (File.Exists(path))
            return path;

        if (OperatingSystem.IsWindows())
        {
            var executable = path + ".exe";
            if (File.Exists(executable))
                return executable;
        }

        return string.Empty;
    }
}