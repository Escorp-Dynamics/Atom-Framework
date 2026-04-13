using System.Diagnostics;
using System.Reflection;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using RuntimeWebBrowser = Atom.Net.Browsing.WebDriver.WebBrowser;

namespace Atom.Net.Browsing.WebDriver.Tests;

internal static class WebDriverTestEnvironment
{
    private const string BrowserVariableName = "ATOM_TEST_WEBDRIVER_BROWSER";
    private const string BrowserPathVariableName = "ATOM_TEST_WEBDRIVER_BROWSER_PATH";
    private const string HeadlessVariableName = "ATOM_TEST_WEBDRIVER_HEADLESS";
    private static string? linuxDisplayBackendUnavailableReason;

    internal static ValueTask<RuntimeWebBrowser> LaunchAsync(CancellationToken cancellationToken = default)
        => LaunchAsync(new WebBrowserSettings(), cancellationToken);

    internal static ValueTask<RuntimeWebBrowser> LaunchAsync(WebBrowserSettings settings)
        => LaunchAsync(settings, CancellationToken.None);

    internal static ValueTask<RuntimeWebBrowser> LaunchAsync(WebBrowserSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var resolvedSettings = ApplyOverrides(settings);
        ThrowIfCachedLinuxDisplayBackendUnavailable(resolvedSettings);
        return LaunchCoreAsync(resolvedSettings, cancellationToken);
    }

    internal static bool IsRealBrowserRunConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BrowserVariableName));

    internal static bool? GetRequestedHeadlessMode()
        => TryGetBooleanOverride(HeadlessVariableName);

    internal static Process? GetLaunchedBrowserProcess(RuntimeWebBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var field = typeof(RuntimeWebBrowser).GetField("browserProcess", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Process?)field?.GetValue(browser);
    }

    internal static WebBrowserSettings GetLaunchSettings(RuntimeWebBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var property = typeof(RuntimeWebBrowser).GetProperty("LaunchSettings", BindingFlags.Instance | BindingFlags.NonPublic);
        return (WebBrowserSettings?)property?.GetValue(browser)
            ?? throw new InvalidOperationException("Не удалось получить internal LaunchSettings у runtime browser для test inspection.");
    }

    private static async ValueTask<RuntimeWebBrowser> LaunchCoreAsync(WebBrowserSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return await RuntimeWebBrowser.LaunchAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex) when (ShouldIgnoreLinuxDisplayBackendFailure(settings, ex))
        {
            linuxDisplayBackendUnavailableReason ??= ex.Message;
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    private static WebBrowserSettings ApplyOverrides(WebBrowserSettings settings)
    {
        var resolvedProfile = settings.Profile;

        if (resolvedProfile is null)
        {
            var browserName = Environment.GetEnvironmentVariable(BrowserVariableName);
            if (!string.IsNullOrWhiteSpace(browserName))
            {
                var browserPath = Environment.GetEnvironmentVariable(BrowserPathVariableName);
                resolvedProfile = CreateProfile(browserName, browserPath);
                EnsureLaunchableProfile(resolvedProfile);
            }
        }

        var headlessOverride = TryGetBooleanOverride(HeadlessVariableName);

        return new WebBrowserSettings
        {
            Profile = resolvedProfile,
            Proxy = settings.Proxy,
            Logger = settings.Logger,
            Display = settings.Display,
            Mouse = settings.Mouse,
            Keyboard = settings.Keyboard,
            UseHeadlessMode = headlessOverride ?? settings.UseHeadlessMode,
            UseIncognitoMode = settings.UseIncognitoMode,
            UseRootlessChromiumBootstrap = settings.UseRootlessChromiumBootstrap,
            Position = settings.Position,
            Size = settings.Size,
            Args = settings.Args is null ? null : [.. settings.Args],
            Device = settings.Device.Clone(),
        };
    }

    private static void ThrowIfCachedLinuxDisplayBackendUnavailable(WebBrowserSettings settings)
    {
        if (!RequiresLinuxAutoDisplay(settings))
            return;

        if (string.IsNullOrWhiteSpace(linuxDisplayBackendUnavailableReason))
            return;

        Assert.Ignore("Display backend недоступен — пропускаем: " + linuxDisplayBackendUnavailableReason);
    }

    private static bool ShouldIgnoreLinuxDisplayBackendFailure(WebBrowserSettings settings, VirtualDisplayException ex)
        => RequiresLinuxAutoDisplay(settings) && IsDisplayBackendUnavailable(ex);

    private static bool RequiresLinuxAutoDisplay(WebBrowserSettings settings)
        => OperatingSystem.IsLinux()
            && settings.Display is null
            && !IsRealBrowserRunConfigured();

    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex)
        => ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Xvfb", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    private static WebBrowserProfile CreateProfile(string browserName, string? browserPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserName);

        var normalizedBrowser = browserName.Trim().ToLowerInvariant();
        var channel = ResolveBrowserChannel(normalizedBrowser, browserPath);
        WebBrowserProfile profile = normalizedBrowser switch
        {
            "chrome" or "google-chrome" => new ChromeProfile(channel),
            "edge" or "microsoft-edge" => new EdgeProfile(channel),
            "brave" => new BraveProfile(channel),
            "opera" => new OperaProfile(channel),
            "vivaldi" => new VivaldiProfile(channel),
            "yandex" or "yandex-browser" => new YandexProfile(channel),
            "firefox" or "firefox-beta" or "firefox-developer-edition" or "firefox-dev" or "firefox-nightly" => new FirefoxProfile(channel),
            _ => throw new InvalidOperationException($"Неизвестное значение {BrowserVariableName}: '{browserName}'. Ожидался один из браузеров Chrome, Edge, Brave, Opera, Vivaldi, Yandex или Firefox."),
        };

        if (!string.IsNullOrWhiteSpace(browserPath))
            profile.BinaryPath = browserPath.Trim();

        return profile;
    }

    private static WebBrowserChannel ResolveBrowserChannel(string normalizedBrowser, string? browserPath)
    {
        if (normalizedBrowser.Contains("beta", StringComparison.Ordinal))
            return WebBrowserChannel.Beta;

        if (normalizedBrowser.Contains("developer", StringComparison.Ordinal)
            || normalizedBrowser.Contains("nightly", StringComparison.Ordinal)
            || normalizedBrowser.EndsWith("-dev", StringComparison.Ordinal))
        {
            return WebBrowserChannel.Dev;
        }

        if (string.IsNullOrWhiteSpace(browserPath))
            return WebBrowserChannel.Stable;

        var pathHint = browserPath.Trim();

        return normalizedBrowser switch
        {
            "firefox" => ResolveFirefoxChannel(pathHint),
            _ => WebBrowserChannel.Stable,
        };
    }

    private static WebBrowserChannel ResolveFirefoxChannel(string pathHint)
    {
        if (ContainsIgnoreCase(pathHint, "firefox-developer-edition")
            || ContainsIgnoreCase(pathHint, "Firefox Developer Edition")
            || ContainsIgnoreCase(pathHint, "firefox-nightly")
            || ContainsIgnoreCase(pathHint, "Firefox Nightly"))
        {
            return WebBrowserChannel.Dev;
        }

        if (ContainsIgnoreCase(pathHint, "firefox-beta")
            || ContainsIgnoreCase(pathHint, "Firefox Beta"))
        {
            return WebBrowserChannel.Beta;
        }

        return WebBrowserChannel.Stable;
    }

    private static bool ContainsIgnoreCase(string value, string fragment)
        => value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static void EnsureLaunchableProfile(WebBrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.IsInstalled)
            throw new FileNotFoundException($"Не найден бинарный файл браузера для глобального test override '{profile.GetType().Name}'.", profile.BinaryPath);

        if (!CanLaunchBrowserBinary(profile.BinaryPath))
            throw new InvalidOperationException($"Бинарный файл браузера '{profile.BinaryPath}' существует, но не может быть запущен как процесс.");
    }

    private static bool CanLaunchBrowserBinary(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        if (!File.Exists(binaryPath))
            return false;

        if (OperatingSystem.IsWindows())
        {
            var extension = Path.GetExtension(binaryPath);
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                var mode = File.GetUnixFileMode(binaryPath);
                return mode.HasFlag(UnixFileMode.UserExecute)
                    || mode.HasFlag(UnixFileMode.GroupExecute)
                    || mode.HasFlag(UnixFileMode.OtherExecute);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool? TryGetBooleanOverride(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new InvalidOperationException($"Не удалось разобрать булево значение переменной {variableName}: '{value}'. Ожидались 1/0, true/false, yes/no или on/off."),
        };
    }
}