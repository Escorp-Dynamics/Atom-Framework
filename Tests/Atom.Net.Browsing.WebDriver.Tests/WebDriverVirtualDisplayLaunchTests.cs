using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Atom.Hardware.Display;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[NonParallelizable]
[Category("Hardware")]
public sealed class WebDriverVirtualDisplayLaunchTests
{
    [Test]
    public void WebBrowserSettingsExposeDisplaySurface()
    {
        var property = typeof(WebBrowserSettings).GetProperty(nameof(WebBrowserSettings.Display));

        Assert.Multiple(() =>
        {
            Assert.That(property, Is.Not.Null);
            Assert.That(property!.PropertyType, Is.EqualTo(typeof(VirtualDisplay)));
            Assert.That(property.CanWrite, Is.True);
        });
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LaunchAsyncAutoCreatesVisibleLinuxDisplayForHeadfulRun()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        try
        {
            var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
            var launchSettings = WebDriverTestEnvironment.GetLaunchSettings(browser);
            var display = launchSettings.Display;
            var displayNumber = display?.DisplayNumber ?? -1;

            Assert.That(launchSettings.Display, Is.Not.Null);
            Assert.That(launchSettings.Display!.Settings.IsVisible, Is.True);

            await browser.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(IsDisplayDisposed(display!), Is.True);
                AssertManagedDisplayProcessesCleared(displayNumber, "После browser dispose не должно оставаться живых auto-display процессов.");
                Assert.That(File.Exists("/tmp/.X" + displayNumber + "-lock"), Is.False,
                    "Lock-файл auto-created display должен быть удалён после browser dispose.");
            });
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void LaunchAsyncAutoDisplayBackendFailureIsConvertedToIgnore()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        var reasonField = typeof(WebDriverTestEnvironment).GetField(
            "linuxDisplayBackendUnavailableReason",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(reasonField, Is.Not.Null, "Не удалось найти cached display backend failure field.");

        var originalReason = (string?)reasonField!.GetValue(obj: null);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalBrowser = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER");
        var originalBrowserPath = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH");

        try
        {
            reasonField.SetValue(obj: null, value: null);
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", value: null);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", value: null);

            Assert.That(
                async () => await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false),
                Throws.InstanceOf<IgnoreException>());

            var cachedReason = (string?)reasonField.GetValue(obj: null);
            Assert.That(cachedReason, Does.Contain("xpra").Or.Contain("Xvfb"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", originalBrowser);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", originalBrowserPath);
            reasonField.SetValue(obj: null, value: originalReason);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LaunchAsyncDoesNotAutoCreateLinuxDisplayForHeadlessRunAndKeepsBrowserHeadlessFlag()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        var directory = CreateTemporaryDirectory();
        var binaryPath = CreateExecutableBrowserHarness(directory);
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Stable);
        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
                UseHeadlessMode = true,
            };

            var (autoDisplay, _, ownsDisplay) = await PrepareLinuxLaunchForTestsAsync(launchSettings).ConfigureAwait(false);
            _ = await MaterializeLaunchArtifactsAsync(launchSettings).ConfigureAwait(false);
            var manifestPath = Path.Combine(profile.Path, "profile.json");
            var manifestText = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(autoDisplay, Is.Null);
                Assert.That(ownsDisplay, Is.False);
                Assert.That(launchSettings.Display, Is.Null);
                Assert.That(manifestText, Does.Contain("\"headless\":true"));
                Assert.That(manifestText, Does.Contain("--headless=new"));
            });
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(profile.Path))
                DeleteDirectoryIfExists(profile.Path);

            DeleteDirectoryIfExists(directory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LaunchAsyncUsesExplicitDisplayWithoutTakingOwnership()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
            {
                IsVisible = true,
            }).ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
            return;
        }

        await using (display)
        {
            var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Display = display,
            }).ConfigureAwait(false);

            var launchSettings = WebDriverTestEnvironment.GetLaunchSettings(browser);

            Assert.That(launchSettings.Display, Is.SameAs(display));

            await browser.DisposeAsync().ConfigureAwait(false);

            Assert.That(IsDisplayDisposed(display), Is.False);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LaunchAsyncRejectsHeadfulBrowserWithHiddenExplicitDisplay()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        await using var display = await CreateDisplayOrIgnoreAsync(isVisible: false).ConfigureAwait(false);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Display = display,
                UseHeadlessMode = false,
            }).ConfigureAwait(false));

        Assert.That(exception!.Message, Does.Contain("Смешанный режим видимости браузера и дисплея"));
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LaunchAsyncRejectsHeadlessBrowserWithVisibleExplicitDisplay()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux virtual display lifecycle.");

        await using var display = await CreateDisplayOrIgnoreAsync(isVisible: true).ConfigureAwait(false);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Display = display,
                UseHeadlessMode = true,
            }).ConfigureAwait(false));

        Assert.That(exception!.Message, Does.Contain("Смешанный режим видимости браузера и дисплея"));
    }

    [SupportedOSPlatform("linux")]
    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex)
        => ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualDisplay> CreateDisplayOrIgnoreAsync(bool isVisible)
    {
        try
        {
            return await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
            {
                IsVisible = isVisible,
            }).ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool IsDisplayDisposed(VirtualDisplay display)
    {
        var property = typeof(VirtualDisplay).GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.NonPublic);
        return property is not null && property.GetValue(display) is bool isDisposed && isDisposed;
    }

    [SupportedOSPlatform("linux")]
    private static void AssertManagedDisplayProcessesCleared(int displayNumber, string message)
    {
        var remainingProcessIds = WaitForManagedDisplayProcessesToExit(displayNumber, TimeSpan.FromSeconds(2));
        Assert.That(remainingProcessIds, Is.Empty, message + " Остались PID: " + string.Join(",", remainingProcessIds));
    }

    [SupportedOSPlatform("linux")]
    private static HashSet<int> WaitForManagedDisplayProcessesToExit(int displayNumber, TimeSpan timeout)
    {
        var startTime = Stopwatch.GetTimestamp();

        while (true)
        {
            var remainingProcessIds = CaptureManagedDisplayProcessIds(displayNumber);
            if (remainingProcessIds.Count == 0)
                return remainingProcessIds;

            if (Stopwatch.GetElapsedTime(startTime) >= timeout)
                return remainingProcessIds;

            Thread.Sleep(100);
        }
    }

    [SupportedOSPlatform("linux")]
    private static HashSet<int> CaptureManagedDisplayProcessIds(int displayNumber)
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "CaptureDisplayProcessIds",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static CaptureDisplayProcessIds.");

        return new HashSet<int>((HashSet<int>)method!.Invoke(obj: null, [displayNumber])!);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [SupportedOSPlatform("linux")]
    private static string CreateExecutableBrowserHarness(string directory)
    {
        var path = Path.Combine(directory, "browser-harness.sh");
        File.WriteAllText(path, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    [SupportedOSPlatform("linux")]
    private static async ValueTask<(VirtualDisplay? AutoDisplay, VirtualDisplay? LaunchDisplay, bool OwnsDisplay)> PrepareLinuxLaunchForTestsAsync(WebBrowserSettings settings)
    {
        var method = typeof(WebBrowser).GetMethod("PrepareLinuxLaunchAsync", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(WebBrowser).FullName, "PrepareLinuxLaunchAsync");
        dynamic valueTask = method.Invoke(null, [settings, CancellationToken.None])
            ?? throw new InvalidOperationException("PrepareLinuxLaunchAsync не вернул ValueTask результата.");
        return await valueTask.AsTask().ConfigureAwait(false);
    }

    private static async ValueTask<ProfileMaterializationResult> MaterializeLaunchArtifactsAsync(WebBrowserSettings settings)
    {
        var preparation = BridgeExtensionBootstrap.TryCreatePreparation(settings);
        var boundPreparation = preparation is null ? null : BindBridgeBootstrapPreparation(preparation);
        return await ProfileMaterialization.MaterializeAsync(settings, boundPreparation, CancellationToken.None).ConfigureAwait(false);
    }

    private static BridgeBootstrapPreparation BindBridgeBootstrapPreparation(BridgeBootstrapPreparation preparation)
    {
        var bindMethod = typeof(WebBrowser).GetMethod("BindBridgeBootstrapPort", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(WebBrowser).FullName, "BindBridgeBootstrapPort");

        return bindMethod.Invoke(null,
        [
            preparation,
            9000,
            9444,
            9445,
            false,
            BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
        ]) as BridgeBootstrapPreparation
            ?? throw new InvalidOperationException("Не удалось связать bridge bootstrap с тестовыми портами.");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}