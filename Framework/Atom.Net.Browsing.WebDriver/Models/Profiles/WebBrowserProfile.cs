using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет базовый профиль браузера с путями и каналом установки.
/// </summary>
public abstract class WebBrowserProfile
{
    private const string FlatpakSystemExportsDirVariableName = "ATOM_WEBDRIVER_FLATPAK_SYSTEM_EXPORTS_DIR";
    private const string FlatpakUserExportsDirVariableName = "ATOM_WEBDRIVER_FLATPAK_USER_EXPORTS_DIR";
    private const string SnapBinDirVariableName = "ATOM_WEBDRIVER_SNAP_BIN_DIR";

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
    /// Получает профиль Opera GX по умолчанию.
    /// </summary>
    public static WebBrowserProfile OperaGx { get; } = new OperaGxProfile();

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
    /// Получает способ установки браузера, определённый по разрешённому пути бинарного файла.
    /// </summary>
    public BrowserInstallationKind InstallationKind { get; protected set; }

    /// <summary>
    /// Получает идентификатор пакета sandboxed-установки (Flatpak app id или имя snap-пакета),
    /// если бинарный файл разрешён через такую установку.
    /// </summary>
    internal string? SandboxedPackageId { get; private set; }

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
        string devLinuxBinary,
        string? flatpakApplicationId = null,
        string? snapPackageName = null)
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

        return AppendSandboxedInstallCandidates(
            channel switch
            {
                WebBrowserChannel.Beta => [betaLinuxBinary, stableLinuxBinary],
                WebBrowserChannel.Dev => [devLinuxBinary, betaLinuxBinary, stableLinuxBinary],
                _ => [stableLinuxBinary, betaLinuxBinary, devLinuxBinary],
            },
            flatpakApplicationId,
            snapPackageName);
    }

    /// <summary>
    /// Добавляет к нативным Linux-кандидатам пути sandboxed-установок (Flatpak exports и Snap launcher-скрипты).
    /// Нативные кандидаты сохраняют приоритет разрешения над sandboxed.
    /// </summary>
#pragma warning disable MA0050 // Validate arguments correctly in iterator methods
#pragma warning disable S4456 // Parameter validation in yielding methods should be wrapped
    protected static IEnumerable<string> AppendSandboxedInstallCandidates(
#pragma warning restore S4456 // Parameter validation in yielding methods should be wrapped
#pragma warning restore MA0050 // Validate arguments correctly in iterator methods
        IEnumerable<string> nativeCandidates,
        string? flatpakApplicationId = null,
        string? snapPackageName = null)
    {
        ArgumentNullException.ThrowIfNull(nativeCandidates);

        foreach (var candidate in nativeCandidates)
            yield return candidate;

        if (!string.IsNullOrWhiteSpace(flatpakApplicationId))
        {
            foreach (var candidate in GetFlatpakCandidates(flatpakApplicationId))
                yield return candidate;
        }

        if (!string.IsNullOrWhiteSpace(snapPackageName))
        {
            foreach (var candidate in GetSnapCandidates(snapPackageName))
                yield return candidate;
        }
    }

    /// <summary>
    /// Формирует кандидатов Flatpak для экспортированных launcher-скриптов приложения
    /// из системного и пользовательского exports-каталогов.
    /// </summary>
#pragma warning disable MA0050 // Validate arguments correctly in iterator methods
#pragma warning disable S4456 // Parameter validation in yielding methods should be wrapped
    protected static IEnumerable<string> GetFlatpakCandidates(params string[] applicationIds)
#pragma warning restore S4456 // Parameter validation in yielding methods should be wrapped
#pragma warning restore MA0050 // Validate arguments correctly in iterator methods
    {
        ArgumentNullException.ThrowIfNull(applicationIds);

        foreach (var applicationId in applicationIds)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
                continue;

            yield return IOPath.Combine(GetFlatpakSystemExportsDirectory(), applicationId);
            yield return IOPath.Combine(GetFlatpakUserExportsDirectory(), applicationId);
        }
    }

    /// <summary>
    /// Формирует кандидатов Snap для launcher-скриптов пакета из каталога бинарных файлов snap.
    /// </summary>
#pragma warning disable MA0050 // Validate arguments correctly in iterator methods
#pragma warning disable S4456 // Parameter validation in yielding methods should be wrapped
    protected static IEnumerable<string> GetSnapCandidates(params string[] packageNames)
#pragma warning restore S4456 // Parameter validation in yielding methods should be wrapped
#pragma warning restore MA0050 // Validate arguments correctly in iterator methods
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        foreach (var packageName in packageNames)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                continue;

            yield return IOPath.Combine(GetSnapBinaryDirectory(), packageName);
        }
    }

    /// <summary>
    /// Раскрывает переменные окружения и префикс домашнего каталога '~' в пути-кандидате.
    /// </summary>
    internal static string ExpandCandidatePath(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var expanded = Environment.ExpandEnvironmentVariables(candidate);

        if (!expanded.StartsWith('~'))
            return expanded;

        if (expanded.Length > 1 && expanded[1] is not ('/' or '\\'))
            return expanded;

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
            return expanded;

        return expanded.Length == 1
            ? homeDirectory
            : IOPath.Combine(homeDirectory, expanded[2..]);
    }

    internal static string GetFlatpakSystemExportsDirectory()
        => GetDirectoryOverride(FlatpakSystemExportsDirVariableName) ?? "/var/lib/flatpak/exports/bin";

    internal static string GetFlatpakUserExportsDirectory()
        => GetDirectoryOverride(FlatpakUserExportsDirVariableName) ?? "~/.local/share/flatpak/exports/bin";

    internal static string GetSnapBinaryDirectory()
        => GetDirectoryOverride(SnapBinDirVariableName) ?? "/snap/bin";

    private void RefreshInstallationState()
    {
        IsInstalled = !string.IsNullOrWhiteSpace(binaryPath) && File.Exists(binaryPath);
        (InstallationKind, SandboxedPackageId) = ClassifyInstallation(binaryPath);
    }

    private static string? GetDirectoryOverride(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static (BrowserInstallationKind Kind, string? PackageId) ClassifyInstallation(string resolvedBinaryPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedBinaryPath) || !OperatingSystem.IsLinux())
            return (BrowserInstallationKind.Native, null);

        var fullPath = IOPath.GetFullPath(resolvedBinaryPath);

        if (TryMatchDirectoryEntry(fullPath, GetFlatpakSystemExportsDirectory(), out var packageId)
            || TryMatchDirectoryEntry(fullPath, GetFlatpakUserExportsDirectory(), out packageId))
        {
            return (BrowserInstallationKind.Flatpak, packageId);
        }

        if (TryMatchDirectoryEntry(fullPath, GetSnapBinaryDirectory(), out packageId))
            return (BrowserInstallationKind.Snap, packageId);

        return (BrowserInstallationKind.Native, null);
    }

    private static bool TryMatchDirectoryEntry(string fullPath, string directory, out string? entryName)
    {
        entryName = null;

        if (string.IsNullOrWhiteSpace(directory))
            return false;

        var fullDirectory = IOPath.GetFullPath(ExpandCandidatePath(directory));
        var directoryWithSeparator = fullDirectory.EndsWith(IOPath.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + IOPath.DirectorySeparatorChar;

        if (!fullPath.StartsWith(directoryWithSeparator, StringComparison.Ordinal))
            return false;

        var relativeName = fullPath[directoryWithSeparator.Length..];
        if (relativeName.Length == 0 || ContainsDirectorySeparators(relativeName))
            return false;

        entryName = relativeName;
        return true;
    }

    private static string TryResolveCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        var expanded = ExpandCandidatePath(candidate);
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