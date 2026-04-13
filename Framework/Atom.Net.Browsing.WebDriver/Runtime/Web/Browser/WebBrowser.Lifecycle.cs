using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebBrowser
{
    private readonly string? materializedProfilePath;
    private readonly Process? browserProcess;

    private static async ValueTask<WebBrowser> LaunchCoreAsync(WebBrowserSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        settings.Logger?.LogWebBrowserLaunchStarting(
            settings.Profile?.BinaryPath ?? "<auto>",
            settings.UseHeadlessMode,
            settings.UseIncognitoMode,
            settings.Display is not null);

        var launchSettings = settings.Clone();
        string? materializedProfilePath = null;
        VirtualDisplay? autoDisplay = null;
        var ownsDisplay = false;
        var launchDisplay = launchSettings.Display;
        BridgeServer? bridgeServer = null;

        try
        {
            if (OperatingSystem.IsLinux())
                (autoDisplay, launchDisplay, ownsDisplay) = await PrepareLinuxLaunchAsync(launchSettings, cancellationToken).ConfigureAwait(false);

            (bridgeServer, var bridgeBootstrapPreparation) = await StartBridgeBootstrapAsync(launchSettings, cancellationToken).ConfigureAwait(false);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                bridgeBootstrapPreparation,
                cancellationToken).ConfigureAwait(false);
            materializedProfilePath = materialization.MaterializedProfilePath;
            ConfigureBridgeManagedDelivery(bridgeServer, materialization.BridgeBootstrap);

            if (!string.IsNullOrWhiteSpace(materializedProfilePath))
                launchSettings.Logger?.LogWebBrowserProfileMaterialized(materializedProfilePath);

            var browserProcess = LaunchBrowserProcess(launchSettings, materialization.BridgeBootstrap);
            return await CreateReadyBrowserAsync(
                launchSettings,
                materializedProfilePath,
                browserProcess,
                launchDisplay,
                ownsDisplay,
                bridgeServer,
                materialization.BridgeBootstrap,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (bridgeServer is not null)
                await bridgeServer.DisposeAsync().ConfigureAwait(false);

            CleanupMaterializedProfile(launchSettings, materializedProfilePath);
            if (OperatingSystem.IsLinux() && ownsDisplay && autoDisplay is not null)
                await autoDisplay.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<(VirtualDisplay? AutoDisplay, VirtualDisplay? LaunchDisplay, bool OwnsDisplay)> PrepareLinuxLaunchAsync(WebBrowserSettings launchSettings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return (null, launchSettings.Display, false);

        var autoDisplay = await AutoCreateDisplayAsync(launchSettings.Display, launchSettings, cancellationToken).ConfigureAwait(false);
        if (autoDisplay is not null)
        {
            launchSettings.Display = autoDisplay;
            LogAutoCreatedDisplay(launchSettings, autoDisplay);
        }

        var launchDisplay = launchSettings.Display;
        ValidateLinuxBrowserDisplayVisibilityCoupling(launchSettings, launchDisplay);
        return (autoDisplay, launchDisplay, autoDisplay is not null);
    }

    private static async ValueTask<WebBrowser> CreateReadyBrowserAsync(
        WebBrowserSettings launchSettings,
        string? materializedProfilePath,
        Process? browserProcess,
        VirtualDisplay? launchDisplay,
        bool ownsDisplay,
        BridgeServer? bridgeServer,
        BridgeBootstrapPlan? bridgeBootstrap,
        CancellationToken cancellationToken)
    {
        var browser = new WebBrowser(
            launchSettings,
            materializedProfilePath,
            browserProcess,
            launchDisplay,
            ownsDisplay,
            bridgeServer,
            bridgeBootstrap);

        try
        {
            await browser.EnsureReadyAfterLaunchAsync(cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch
        {
            await browser.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask EnsureReadyAfterLaunchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (bridgeBootstrapTask is null)
            return;

        if (!await WaitForInitialBridgeBootstrapAsync(cancellationToken).ConfigureAwait(false))
        {
            var diagnostics = await DescribeInitialBridgeBootstrapFailureAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"LaunchAsync не завершил initial discovery bridge-bootstrap до возврата браузера. {diagnostics}");
        }
    }

    private async ValueTask<string> DescribeInitialBridgeBootstrapFailureAsync()
    {
        var details = new List<string>
        {
            $"currentPageBridgeCommandsBound={((WebPage)CurrentPage).BridgeCommands is not null}",
        };

        if (bridgeServer is null)
        {
            details.Add("bridgeServer=missing");
            return string.Join(", ", details);
        }

        if (string.IsNullOrWhiteSpace(bridgeSessionId))
        {
            details.Add("bridgeSessionId=missing");
            return string.Join(", ", details);
        }

        details.Add($"bridgeSessionId={bridgeSessionId}");

        var session = await bridgeServer.CreateSessionSnapshotAsync(bridgeSessionId).ConfigureAwait(false);
        if (session is null)
        {
            details.Add("sessionSnapshot=null");
            return string.Join(", ", details);
        }

        details.Add($"sessionConnected={session.IsConnected}");
        details.Add($"sessionBrowserFamily={session.BrowserFamily}");
        details.Add($"sessionExtensionVersion={session.ExtensionVersion}");
        details.Add($"tabCount={session.Tabs.Length}");

        if (session.Tabs.Length > 0)
        {
            details.Add($"registeredTabCount={session.Tabs.Count(static tab => tab.IsRegistered)}");
            var tabDiagnostics = session.Tabs.Select(static tab => $"{tab.TabId}:registered={tab.IsRegistered}:window={tab.WindowId ?? "<null>"}");
            details.Add($"tabs=[{string.Join(';', tabDiagnostics)}]");
        }

        return string.Join(", ", details);
    }

    private static async ValueTask<(BridgeServer? BridgeServer, BridgeBootstrapPreparation? Preparation)> StartBridgeBootstrapAsync(WebBrowserSettings launchSettings, CancellationToken cancellationToken)
    {
        var bridgeBootstrapPreparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
        if (bridgeBootstrapPreparation is null)
            return (null, null);

        var bridgeServer = new BridgeServer(bridgeBootstrapPreparation.Settings);
        await bridgeServer.StartAsync(cancellationToken).ConfigureAwait(false);
        bridgeBootstrapPreparation = BindBridgeBootstrapPorts(
            bridgeBootstrapPreparation,
            bridgeServer.Port,
            bridgeServer.SecureTransportPort,
            bridgeServer.ManagedDeliveryPort,
            bridgeServer.NavigationProxyPort,
            bridgeServer.ManagedDeliveryRequiresCertificateBypass,
            bridgeServer.ManagedDeliveryTrustDiagnostics);
        return (bridgeServer, bridgeBootstrapPreparation);
    }

    private static BridgeBootstrapPreparation BindBridgeBootstrapPorts(
        BridgeBootstrapPreparation preparation,
        int port,
        int secureTransportPort,
        int managedDeliveryPort,
        int navigationProxyPort,
        bool managedDeliveryRequiresCertificateBypass,
        BridgeManagedDeliveryTrustDiagnostics bridgeServerManagedTrustDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        return preparation with
        {
            Settings = new BridgeSettings
            {
                Host = preparation.Settings.Host,
                Port = port,
                SecureTransportPort = secureTransportPort,
                ManagedDeliveryPort = managedDeliveryPort,
                NavigationProxyPort = navigationProxyPort,
                Secret = preparation.Settings.Secret,
                Logger = preparation.Settings.Logger,
                RequestTimeout = preparation.Settings.RequestTimeout,
                BootstrapTimeout = preparation.Settings.BootstrapTimeout,
                PingInterval = preparation.Settings.PingInterval,
                MaxMessageSize = preparation.Settings.MaxMessageSize,
                AutoCreateVirtualDisplay = preparation.Settings.AutoCreateVirtualDisplay,
                ManagedExtensionDelivery = preparation.Settings.ManagedExtensionDelivery,
                UseRootlessChromiumBootstrap = preparation.Settings.UseRootlessChromiumBootstrap,
                ManagedDeliveryRequiresCertificateBypass = managedDeliveryRequiresCertificateBypass,
                ManagedDeliveryTrustDiagnostics = bridgeServerManagedTrustDiagnostics,
            },
        };
    }

    [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Reflection seam retained for existing tests and bootstrap compatibility.")]
    [SuppressMessage("Style", "IDE0051:Remove unused private members", Justification = "Reflection seam retained for existing tests and bootstrap compatibility.")]
    private static BridgeBootstrapPreparation BindBridgeBootstrapPort(
        BridgeBootstrapPreparation preparation,
        int port,
        int secureTransportPort,
        int managedDeliveryPort,
        bool managedDeliveryRequiresCertificateBypass,
        BridgeManagedDeliveryTrustDiagnostics bridgeServerManagedTrustDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        return preparation with
        {
            Settings = new BridgeSettings
            {
                Host = preparation.Settings.Host,
                Port = port,
                SecureTransportPort = secureTransportPort,
                ManagedDeliveryPort = managedDeliveryPort,
                NavigationProxyPort = preparation.Settings.NavigationProxyPort,
                Secret = preparation.Settings.Secret,
                Logger = preparation.Settings.Logger,
                RequestTimeout = preparation.Settings.RequestTimeout,
                BootstrapTimeout = preparation.Settings.BootstrapTimeout,
                PingInterval = preparation.Settings.PingInterval,
                MaxMessageSize = preparation.Settings.MaxMessageSize,
                AutoCreateVirtualDisplay = preparation.Settings.AutoCreateVirtualDisplay,
                ManagedExtensionDelivery = preparation.Settings.ManagedExtensionDelivery,
                UseRootlessChromiumBootstrap = preparation.Settings.UseRootlessChromiumBootstrap,
                ManagedDeliveryRequiresCertificateBypass = managedDeliveryRequiresCertificateBypass,
                ManagedDeliveryTrustDiagnostics = bridgeServerManagedTrustDiagnostics,
            },
        };
    }

    private static void ConfigureBridgeManagedDelivery(BridgeServer? bridgeServer, BridgeBootstrapPlan? bridgeBootstrap)
    {
        if (bridgeServer is null
            || bridgeBootstrap is null
            || !string.Equals(bridgeBootstrap.BrowserFamily, "chromium", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(bridgeBootstrap.ManagedPackageUrl)
            || string.IsNullOrWhiteSpace(bridgeBootstrap.ManagedPackageArtifactPath)
            || !File.Exists(bridgeBootstrap.ManagedPackageArtifactPath))
        {
            return;
        }

        bridgeServer.ConfigureManagedExtensionDelivery(new BridgeManagedExtensionDelivery(
            bridgeBootstrap.ExtensionId,
            bridgeBootstrap.ExtensionVersion,
            bridgeBootstrap.ManagedUpdateUrl,
            bridgeBootstrap.ManagedPackageUrl,
            File.ReadAllBytes(bridgeBootstrap.ManagedPackageArtifactPath)));
    }

    private static ValueTask<VirtualMouse> CreateVirtualMouseAsync(VirtualDisplay? display, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            if (display is not null)
                LogMouseResolution(display);

            return CreateLinuxVirtualMouseAsync(display, cancellationToken);
        }

        return VirtualMouse.CreateDefaultAsync(cancellationToken);
    }

    private static ValueTask<VirtualKeyboard> CreateVirtualKeyboardAsync(VirtualDisplay? display, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            if (display is not null)
                LogKeyboardResolution(display);

            return CreateLinuxVirtualKeyboardAsync(display, cancellationToken);
        }

        return VirtualKeyboard.CreateDefaultAsync(cancellationToken);
    }

    [SupportedOSPlatform("linux")]
    private static void LogAutoCreatedDisplay(WebBrowserSettings launchSettings, VirtualDisplay display)
        => launchSettings.Logger?.LogWebBrowserAutoDisplayCreated(display.Display, display.Settings.IsVisible);

    [SupportedOSPlatform("linux")]
    private static void LogMouseResolution(VirtualDisplay display)
        => display.Settings.Logger?.LogWebBrowserMouseResolving(display.Display);

    [SupportedOSPlatform("linux")]
    private static void LogKeyboardResolution(VirtualDisplay display)
        => display.Settings.Logger?.LogWebBrowserKeyboardResolving(display.Display);

    [SupportedOSPlatform("linux")]
    private static ValueTask<VirtualMouse> CreateLinuxVirtualMouseAsync(VirtualDisplay? display, CancellationToken cancellationToken)
    {
        if (display is null)
            throw new InvalidOperationException("Доверенный ввод мышью на Linux требует браузерной сессии с виртуальным дисплеем");

        return VirtualMouse.CreateForDisplayAsync(display, cancellationToken: cancellationToken);
    }

    [SupportedOSPlatform("linux")]
    private static ValueTask<VirtualKeyboard> CreateLinuxVirtualKeyboardAsync(VirtualDisplay? display, CancellationToken cancellationToken)
    {
        if (display is null)
            throw new InvalidOperationException("Доверенный ввод с клавиатуры на Linux требует браузерной сессии с виртуальным дисплеем");

        return VirtualKeyboard.CreateForDisplayAsync(display, cancellationToken: cancellationToken);
    }

    [SupportedOSPlatform("linux")]
    internal static bool ShouldAutoCreateDisplay(VirtualDisplay? existingDisplay, bool isLinux)
        => existingDisplay is null && isLinux;

    [SupportedOSPlatform("linux")]
    internal static void ValidateLinuxBrowserDisplayVisibilityCoupling(WebBrowserSettings? launchSettings, VirtualDisplay? display)
    {
        if (!OperatingSystem.IsLinux() || display is null)
            return;

        var browserShouldBeHidden = launchSettings?.UseHeadlessMode ?? false;
        var displayIsVisible = display.Settings.IsVisible;

        if (browserShouldBeHidden != displayIsVisible)
            return;

        throw new InvalidOperationException(
            $"Смешанный режим видимости браузера и дисплея не поддерживается, запрошен режим без окна: {browserShouldBeHidden}, видимость дисплея: {displayIsVisible}, используйте либо режим без окна и скрытый дисплей, либо режим с окном и видимый дисплей");
    }

    [SupportedOSPlatform("linux")]
    private static ValueTask<VirtualDisplay?> AutoCreateDisplayAsync(VirtualDisplay? existingDisplay, WebBrowserSettings launchSettings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launchSettings);

        if (launchSettings.UseHeadlessMode && existingDisplay is null)
            return ValueTask.FromResult<VirtualDisplay?>(null);

        if (!ShouldAutoCreateDisplay(existingDisplay, OperatingSystem.IsLinux()))
            return ValueTask.FromResult<VirtualDisplay?>(null);

        launchSettings.Logger?.LogWebBrowserAutoDisplayCreating();

        return CreateLinuxDisplayAsync(launchSettings, cancellationToken);
    }

    [SupportedOSPlatform("linux")]
    private static ValueTask<VirtualDisplay?> CreateLinuxDisplayAsync(WebBrowserSettings launchSettings, CancellationToken cancellationToken)
        => CreateLinuxDisplayCoreAsync(launchSettings, cancellationToken);

    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualDisplay?> CreateLinuxDisplayCoreAsync(WebBrowserSettings launchSettings, CancellationToken cancellationToken)
    {
        return await VirtualDisplay.CreateAsync(
            new VirtualDisplaySettings
            {
                IsVisible = !launchSettings.UseHeadlessMode,
                Logger = launchSettings.Logger,
            }, cancellationToken).ConfigureAwait(false);
    }

    private void CleanupMaterializedProfile()
        => CleanupMaterializedProfile(LaunchSettings, materializedProfilePath);

    private static void CleanupMaterializedProfile(WebBrowserSettings settings, string? materializedProfilePath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(materializedProfilePath))
            return;

        if (ShouldPreserveTemporaryProfile())
            return;

        if (settings.Profile is { } profile
            && string.Equals(profile.Path, materializedProfilePath, StringComparison.Ordinal))
        {
            profile.Path = string.Empty;
        }

        if (Directory.Exists(materializedProfilePath))
            Directory.Delete(materializedProfilePath, recursive: true);
        else if (File.Exists(materializedProfilePath))
            File.Delete(materializedProfilePath);

        settings.Logger?.LogWebBrowserProfileCleaned(materializedProfilePath);
    }

    private static bool ShouldPreserveTemporaryProfile()
    {
        var value = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_KEEP_PROFILE");
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static Process? LaunchBrowserProcess(WebBrowserSettings settings, BridgeBootstrapPlan? bridgeBootstrap)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Profile is not { } profile)
            return null;

        var browserBinaryPath = !string.IsNullOrWhiteSpace(bridgeBootstrap?.LaunchBinaryPath)
            ? bridgeBootstrap.LaunchBinaryPath
            : profile.BinaryPath;
        var isFirefox = profile is FirefoxProfile;
        var useBrowserHeadlessMode = settings.UseHeadlessMode && (!OperatingSystem.IsLinux() || settings.Display is null);
        var stripProcessHeadlessArgument = OperatingSystem.IsLinux() && settings.Display is not null && settings.UseHeadlessMode;

        if (string.IsNullOrWhiteSpace(browserBinaryPath))
            throw new FileNotFoundException("Не указан бинарный файл браузера", browserBinaryPath);

        if (!CanLaunchBrowserBinary(browserBinaryPath))
            return null;

        if (string.IsNullOrWhiteSpace(profile.Path))
            throw new DirectoryNotFoundException("Путь профиля браузера не был материализован перед запуском процесса");

        var preset = ProfileAutomationPresets.Create(profile, settings, profile.Path, bridgeBootstrap is not null);
        var startInfo = CreateBrowserStartInfo(browserBinaryPath, settings, isFirefox, useBrowserHeadlessMode);
        var displayName = ResolveBrowserDisplayName(settings);

        settings.Logger?.LogWebBrowserProcessStarting(browserBinaryPath, displayName);

        AddPresetLaunchArguments(startInfo, preset.EffectiveArguments.Select(static node => node?.GetValue<string>()), stripProcessHeadlessArgument);
        AddBridgeLaunchArguments(startInfo, profile, bridgeBootstrap);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Не удалось запустить браузер '{browserBinaryPath}'");
        settings.Logger?.LogWebBrowserProcessStarted(browserBinaryPath);
        return process;
    }

    private static ProcessStartInfo CreateBrowserStartInfo(string browserBinaryPath, WebBrowserSettings settings, bool isFirefox, bool useBrowserHeadlessMode)
    {
        var startInfo = new ProcessStartInfo(browserBinaryPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = IOPath.GetDirectoryName(browserBinaryPath) ?? Environment.CurrentDirectory,
        };

        if (OperatingSystem.IsLinux())
            ConfigureLinuxBrowserDisplayEnvironment(startInfo, isFirefox, useBrowserHeadlessMode, settings.Display);

        return startInfo;
    }

    private static string ResolveBrowserDisplayName(WebBrowserSettings settings)
    {
        if (OperatingSystem.IsLinux() && settings.Display is not null)
            return settings.Display.Display;

        return "<system>";
    }

    private static void AddPresetLaunchArguments(ProcessStartInfo startInfo, IEnumerable<string?> arguments, bool stripProcessHeadlessArgument)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (stripProcessHeadlessArgument && IsHeadlessLaunchArgument(argument!))
                continue;

            startInfo.ArgumentList.Add(argument!);
        }
    }

    private static void AddBridgeLaunchArguments(ProcessStartInfo startInfo, WebBrowserProfile profile, BridgeBootstrapPlan? bridgeBootstrap)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var launchArgument in BridgeExtensionBootstrap.GetLaunchArguments(profile, bridgeBootstrap))
        {
            if (startInfo.ArgumentList.Any(argument => string.Equals(argument, launchArgument, StringComparison.Ordinal)))
                continue;

            startInfo.ArgumentList.Add(launchArgument);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void ConfigureLinuxBrowserDisplayEnvironment(ProcessStartInfo startInfo, bool isFirefox, bool useBrowserHeadlessMode, VirtualDisplay? display)
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (display is not null)
        {
            startInfo.Environment["DISPLAY"] = display.Display;

            if (!isFirefox)
            {
                startInfo.Environment["WAYLAND_DISPLAY"] = string.Empty;

                if (!startInfo.ArgumentList.Any(static argument => argument.StartsWith("--ozone-platform=", StringComparison.Ordinal)
                    || argument.StartsWith("--ozone-platform-hint=", StringComparison.Ordinal)))
                {
                    startInfo.ArgumentList.Add("--ozone-platform=x11");
                }

                return;
            }

            startInfo.Environment["WAYLAND_DISPLAY"] = string.Empty;
            startInfo.Environment["MOZ_ENABLE_WAYLAND"] = "0";
            startInfo.Environment["GDK_BACKEND"] = "x11";
            startInfo.Environment["MOZ_WEBRENDER"] = "0";
            startInfo.Environment["MOZ_ACCELERATED"] = "0";
            startInfo.Environment["MOZ_X11_EGL"] = "0";
            startInfo.Environment["LIBGL_ALWAYS_SOFTWARE"] = "1";
            return;
        }

        if (useBrowserHeadlessMode)
        {
            startInfo.Environment.Remove("DISPLAY");
            startInfo.Environment.Remove("WAYLAND_DISPLAY");
            startInfo.Environment.Remove("GDK_BACKEND");
            startInfo.Environment.Remove("MOZ_ENABLE_WAYLAND");
            return;
        }
    }

    private static bool IsHeadlessLaunchArgument(string argument)
        => string.Equals(argument, "-headless", StringComparison.Ordinal)
            || argument.StartsWith("--headless", StringComparison.Ordinal);

    private static bool CanLaunchBrowserBinary(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        if (!File.Exists(binaryPath))
            return false;

        if (OperatingSystem.IsWindows())
        {
            var extension = IOPath.GetExtension(binaryPath);
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

    private static async ValueTask DisposeBrowserProcessAsync(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Процесс мог завершиться между проверкой состояния и попыткой kill.
                }
                catch (NotSupportedException)
                {
                    process.Kill();
                }

                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Дескриптор процесса уже недоступен, дополнительного ожидания не требуется.
                }
                catch (TimeoutException)
                {
                    // Не блокируем освобождение браузера бесконечным ожиданием завершения дочернего процесса.
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}