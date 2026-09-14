using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebBrowser
{
    private readonly string? materializedProfilePath;
    private readonly string? publishedManagedPolicyPath;
    private readonly string? publishedExtensionId;

    /// <summary>Каталог материализованного расширения: там лежит запечённый профиль раннего скрипта.</summary>
    private readonly string? localExtensionPath;
    private readonly Process? browserProcess;

    private static async ValueTask<WebBrowser> LaunchCoreAsync(WebBrowserSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        settings.Logger?.LogWebBrowserLaunchStarting(
            settings.Profile?.BinaryPath ?? "<auto>",
            settings.UseHeadlessMode,
            settings.UseIncognitoMode,
            settings.Display is not null);

        // Перед запуском выметаем браузеры прошлых запусков, чьи владельцы уже мертвы: при жёстком
        // завершении (SIGKILL/стоп отладчика) их DisposeAsync не отработал и они остались сиротами.
        BridgeBrowserProcessRegistry.SweepAbandoned(settings.Logger);

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
            ConfigureBridgeNavigationProxy(bridgeServer, materialization.BridgeBootstrap);

            if (!string.IsNullOrWhiteSpace(materializedProfilePath))
                launchSettings.Logger?.LogWebBrowserProfileMaterialized(materializedProfilePath);

            var browserProcess = LaunchBrowserProcess(launchSettings, materialization.BridgeBootstrap);

            // Регистрируем PID браузера за текущим владельцем, чтобы следующий запуск смог
            // вымести его, если нас убьют жёстко и DisposeAsync не отработает.
            if (browserProcess is not null)
            {
                BridgeBrowserProcessRegistry.Register(browserProcess.Id, materializedProfilePath);
                // Плюс к startup-sweep — немедленная уборка: watchdog убьёт браузер, как только
                // умрёт владелец, даже при SIGKILL (стоп в отладчике), не оставляя висеть до
                // следующего запуска.
                BridgeBrowserProcessRegistry.SpawnParentDeathWatchdog(browserProcess.Id, materializedProfilePath);
            }

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

        // Сервер возвращается наружу только после успешного старта; до этого момента
        // LaunchCoreAsync ещё не видит ссылку, поэтому его catch не смог бы освободить уже
        // забинденные listener'ы/сокеты/фоновые задачи, если StartAsync упадёт (порт занят,
        // отмена). Освобождаем частично-запущенный сервер здесь.
        try
        {
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
        catch
        {
            await bridgeServer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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
                ForwardProfile = preparation.Settings.ForwardProfile,
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
                ForwardProfile = preparation.Settings.ForwardProfile,
            },
        };
    }

    /// <summary>
    /// Настраивает навигационный прокси под способ доставки route token в запускаемом браузере.
    /// </summary>
    private static void ConfigureBridgeNavigationProxy(BridgeServer? bridgeServer, BridgeBootstrapPlan? bridgeBootstrap)
    {
        if (bridgeServer is null || bridgeBootstrap is null)
            return;

        // Firefox отвечает на 407 через blocking onAuthRequired и приносит токен так.
        // Chromium прокси-аутентификацию расширению не отдаёт: там токен ставится заголовком,
        // а требовать вызов значило бы отклонять весь неотслеживаемый трафик браузера.
        bridgeServer.ConfigureNavigationProxyRouteTokenChallenge(
            !string.Equals(bridgeBootstrap.BrowserFamily, "chromium", StringComparison.Ordinal));
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

        // Дисплей на собственном композиторе подаёт ввод прямо в протокол, без X-сервера.
        return display.Session is { } session
            ? VirtualMouse.CreateForSessionAsync(session, cancellationToken: cancellationToken)
            : VirtualMouse.CreateForDisplayAsync(display, cancellationToken: cancellationToken);
    }

    [SupportedOSPlatform("linux")]
    private static ValueTask<VirtualKeyboard> CreateLinuxVirtualKeyboardAsync(VirtualDisplay? display, CancellationToken cancellationToken)
    {
        if (display is null)
            throw new InvalidOperationException("Доверенный ввод с клавиатуры на Linux требует браузерной сессии с виртуальным дисплеем");

        return display.Session is { } session
            ? VirtualKeyboard.CreateForSessionAsync(session, cancellationToken: cancellationToken)
            : VirtualKeyboard.CreateForDisplayAsync(display, cancellationToken: cancellationToken);
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
    private static async ValueTask<VirtualDisplay?> AutoCreateDisplayAsync(VirtualDisplay? existingDisplay, WebBrowserSettings launchSettings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launchSettings);

        if (!ShouldAutoCreateDisplay(existingDisplay, OperatingSystem.IsLinux()))
            return null;

        launchSettings.Logger?.LogWebBrowserAutoDisplayCreating();

        try
        {
            return await CreateLinuxDisplayAsync(launchSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (launchSettings.UseHeadlessMode && exception is not OperationCanceledException)
        {
            // Раньше headless-запуск не создавал дисплей вовсе и уходил в настоящий --headless.
            // Это ломало всё, чему нужна живая поверхность окна: кадры/rAF на «невидимой» вкладке
            // не шли (виджеты проверок не монтировали свои iframe), а активация вкладки и
            // доверенный ввод падали — виртуальную мышь (XTEST) без дисплея создать нельзя.
            // Теперь headless получает СКРЫТЫЙ виртуальный дисплей, и браузер запускается на нём
            // ОБЫЧНЫМ ОКНОМ (аргументы --headless/-headless срезаются в LaunchBrowserProcess),
            // то есть ровно тот же рендер-конвейер, что и в headful: parity headless/headful,
            // независимо от заявляемого профиля устройства. Если X-бэкенда на машине нет —
            // откатываемся на прежний чисто-headless запуск вместо поломки запуска: хуже,
            // но это прежнее поведение.
            launchSettings.Logger?.LogWebBrowserHeadlessDisplayFallback(exception);
            return null;
        }
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
                Resolution = ResolveDisplayResolution(launchSettings.Device),

                // Сенсор заявляется по профилю: мышиный клик по документу, описывающему телефон,
                // невозможен физически и виден проверкам в каждом событии указателя.
                HasTouch = launchSettings.Device?.HasTouch ?? false,

                // По приложению окно получает заголовок и иконку — прослойка не видна на панели задач.
                ApplicationPath = launchSettings.Profile?.BinaryPath,
            }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Разрешение виртуального дисплея под заявленный экран профиля.
    /// </summary>
    /// <remarks>
    /// ★ Замер эмуляции показал разрыв в геометрии: страница получала подменённые
    /// <c lang="text">innerWidth/innerHeight</c> размером с экран профиля (1512×982), а настоящая область
    /// документа была 780×493 — и <c lang="text">documentElement.clientHeight</c> вместе с
    /// <c lang="text">visualViewport</c> отдавали именно её. Расхождение вдвое читается одной строкой и не
    /// лечится подменой: за <c lang="text">clientHeight</c> стоит настоящая раскладка страницы.
    ///
    /// Поэтому геометрию не подменяют, а ЗАДАЮТ: дисплей поднимается размером с заявленный экран,
    /// окно получает размер заявленной области просмотра, и тогда все метрики согласованы сами —
    /// включая те, до которых подмена не дотягивается.
    ///
    /// Дисплей меньше окна означал бы окно, обрезанное экраном, поэтому берётся именно экран
    /// профиля; когда профиль экрана не заявляет, остаётся разрешение по умолчанию.
    /// </remarks>
    private static Size ResolveDisplayResolution(Device? device)
    {
        var declared = device?.Screen switch
        {
            { Width: > 0 and var width, Height: > 0 and var height } => new Size(width, height),
            _ when device?.ViewportSize is { IsEmpty: false } viewport => viewport,
            _ => Size.Empty,
        };

        if (declared.IsEmpty)
            return new Size(1920, 1080);

        // Дисплей не должен быть уже окна: Chromium не делает окно уже своей нижней границы, и
        // мобильный профиль с экраном 412 px получал окно 500 px, торчащее за край дисплея.
        return declared.Width >= ProfileAutomationPresets.MinimumChromiumWindowWidth
            ? declared
            : new Size(ProfileAutomationPresets.MinimumChromiumWindowWidth, declared.Height);
    }

    private void CleanupMaterializedProfile()
        => CleanupMaterializedProfile(LaunchSettings, materializedProfilePath);

    /// <summary>
    /// Снимает системную managed policy, опубликованную ради установки расширения.
    /// </summary>
    /// <remarks>
    /// Политика форс-ставит расширение с update URL на локальный порт моста. Порт живёт ровно
    /// столько же, сколько запуск, поэтому оставленный файл заставляет КАЖДЫЙ последующий старт
    /// браузера — в том числе обычный, пользовательский — тянуть расширение с мёртвого адреса.
    /// Профильные политики не трогаем: они лежат внутри материализованного профиля и уходят
    /// вместе с ним.
    /// </remarks>
    private void CleanupPublishedManagedPolicy()
        => CleanupPublishedManagedPolicy(LaunchSettings, publishedManagedPolicyPath, publishedExtensionId, materializedProfilePath);

    private static void CleanupPublishedManagedPolicy(
        WebBrowserSettings settings,
        string? publishedManagedPolicyPath,
        string? publishedExtensionId,
        string? materializedProfilePath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(publishedManagedPolicyPath) || string.IsNullOrWhiteSpace(publishedExtensionId))
            return;

        if (!string.IsNullOrWhiteSpace(materializedProfilePath)
            && publishedManagedPolicyPath.StartsWith(materializedProfilePath, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            // Снимаем ТОЛЬКО свою запись: файл общий, и рядом может работать браузер другого
            // процесса, которому его force-install ещё нужен. Удаляем файл лишь когда наша
            // запись была последней.
            var remainingPolicyJson = BridgeManagedPolicyRegistry.BuildPolicyWithoutEntry(publishedManagedPolicyPath, publishedExtensionId);
            if (remainingPolicyJson is null)
            {
                if (File.Exists(publishedManagedPolicyPath))
                    File.Delete(publishedManagedPolicyPath);

                return;
            }

            File.WriteAllText(publishedManagedPolicyPath, remainingPolicyJson);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Прав на снятие политики может не быть (её кладут туда, куда пишет текущий
            // пользователь, но каталог системный). Освобождение браузера из-за этого не рушим.
            settings.Logger?.LogWebBrowserProfileCleanupFailed(publishedManagedPolicyPath, exception);
        }
    }

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

        // Процесс браузера мог не завершиться за отведённые 5 секунд и всё ещё писать в
        // user-data-dir, из-за чего рекурсивное удаление способно бросить IOException
        // («Directory not empty») / UnauthorizedAccessException. Это не должно прерывать
        // DisposeAsync или маскировать исходное исключение запуска в catch-ветке LaunchCoreAsync.
        try
        {
            if (Directory.Exists(materializedProfilePath))
                Directory.Delete(materializedProfilePath, recursive: true);
            else if (File.Exists(materializedProfilePath))
                File.Delete(materializedProfilePath);

            settings.Logger?.LogWebBrowserProfileCleaned(materializedProfilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            settings.Logger?.LogWebBrowserProfileCleanupFailed(materializedProfilePath, exception);
        }
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
        {
            ConfigureLinuxBrowserDisplayEnvironment(startInfo, isFirefox, useBrowserHeadlessMode, settings.Display);
            ConfigurePlatformFontEnvironment(startInfo, settings);
        }

        ConfigureTimezoneEnvironment(startInfo, settings);
        ConfigureLocaleEnvironment(startInfo, settings);
        ConfigureSoftwareGraphicsEnvironment(startInfo, settings);

        return startInfo;
    }

    /// <summary>
    /// Переводит процесс браузера в заявленную языковую среду.
    /// </summary>
    /// <remarks>
    /// ★ Замер эмуляции показал расхождение того же рода, что и с часовым поясом: заявлен профиль
    /// en-US, страница видела <c lang="text">navigator.languages = ['en-US','en']</c> (подмена расширением), а
    /// <c lang="text">Intl.DateTimeFormat().resolvedOptions().locale</c> отдавал <c lang="text">ru</c>, и название пояса в
    /// <c lang="text">Date.prototype.toString()</c> печаталось по-русски: «Восточная Америка, стандартное
    /// время» вместо «Eastern Standard Time». Одного чтения любого из этих путей хватало, чтобы
    /// увидеть настоящую машину сквозь профиль.
    ///
    /// Аргумент <c lang="text">--lang</c> здесь не помогает: он задаёт язык интерфейса и Accept-Language, а
    /// локаль ICU (её и читает <c lang="text">Intl</c>) Chromium на Linux берёт из окружения. Поэтому лечится
    /// это тем же способом, что и пояс, — переменными процесса, а не обёрткой в странице:
    /// согласованными становятся сразу все пути, включая воркеры и чужой код.
    ///
    /// Значение приводится к виду <c lang="text">en_US.UTF-8</c>: ICU ждёт разделитель подчёркиванием, а
    /// кодировку — явной; профильная же локаль записана в дефисной форме BCP-47.
    /// </remarks>
    /// <summary>
    /// Переводит графику браузера на программный рендерер, когда профиль заявляет WebGL.
    /// </summary>
    /// <remarks>
    /// На виртуальном дисплее GPU нет. Chromium получает контекст через SwiftShader (аргументы
    /// запуска), Firefox — через Mesa: без явного указания он пробует свой SWGL и падает
    /// («RenderCompositorSWGL failed mapping default framebuffer»), оставляя страницу вовсе без
    /// WebGL.
    ///
    /// ★ Только Firefox. Первая версия правки выставляла переменные ЛЮБОМУ браузеру, и Chromium
    /// уходил с ANGLE/SwiftShader на Mesa: замер на живом Cloudflare показал 0 задач из 4 с
    /// таймаутом 45 с на каждой (до правки — 3 из 3 за ~10 с), тогда как Firefox в том же окне
    /// решал 3 из 4. Чужая цепочка рендеринга ломает ровно то, ради чего SwiftShader и включён.
    /// </remarks>
    private static void ConfigureSoftwareGraphicsEnvironment(ProcessStartInfo startInfo, WebBrowserSettings settings)
    {
        if (settings.Device?.WebGL is null || settings.Profile is not FirefoxProfile) return;

        startInfo.Environment["LIBGL_ALWAYS_SOFTWARE"] = "1";
        startInfo.Environment["GALLIUM_DRIVER"] = "llvmpipe";
    }

    private static void ConfigureLocaleEnvironment(ProcessStartInfo startInfo, WebBrowserSettings settings)
    {
        if (settings.Device?.Locale is not { Length: > 0 } locale) return;

        var posixLocale = locale.Replace('-', '_') + ".UTF-8";

        startInfo.Environment["LANG"] = posixLocale;
        startInfo.Environment["LC_ALL"] = posixLocale;
        startInfo.Environment["LANGUAGE"] = locale.Replace('-', '_');
    }

    /// <summary>
    /// Переводит процесс браузера в заявленный часовой пояс.
    /// </summary>
    /// <remarks>
    /// ★ Замер эмуляции показал грубое расхождение: <c lang="text">Intl.DateTimeFormat().resolvedOptions()</c>
    /// сообщал заявленный пояс (America/New_York), а <c lang="text">Date.prototype.getTimezoneOffset()</c> —
    /// НАСТОЯЩИЙ пояс машины (−180, то есть UTC+3). Подменялся только Intl, и одного вычитания
    /// хватало, чтобы поймать подмену.
    ///
    /// Лечится не обёрткой над Date, а переменной окружения: пояс задаётся ВСЕМУ процессу, и
    /// согласованными становятся сразу все пути — Intl, Date, форматирование, воркеры, чужой код
    /// на странице. Обёртка же ловится тривиально: подменённая функция отличается от родной и
    /// строковым представлением, и поведением при наследовании.
    ///
    /// Переменная действует только на процесс браузера — системное время пользователя не
    /// затрагивается. Если пояс не заявлен, ничего не выставляется и остаётся свой.
    /// </remarks>
    private static void ConfigureTimezoneEnvironment(ProcessStartInfo startInfo, WebBrowserSettings settings)
    {
        if (settings.Device?.Timezone is not { Length: > 0 } timezone) return;

        startInfo.Environment["TZ"] = timezone;
    }

    /// <summary>
    /// Подключает браузеру набор шрифтов заявленной платформы.
    /// </summary>
    /// <remarks>
    /// Переменная действует ТОЛЬКО на процесс браузера, поэтому системные настройки шрифтов
    /// пользователя не затрагиваются. Файл готовится при материализации профиля; если платформа
    /// своя и подменять нечего, файла нет и переменная не выставляется.
    /// </remarks>
    private static void ConfigurePlatformFontEnvironment(ProcessStartInfo startInfo, WebBrowserSettings settings)
    {
        if (settings.Profile?.Path is not { Length: > 0 } profilePath)
            return;

        // Рубильник сравнительного замера: шрифтовой слой живёт в процессе браузера, а не в JS,
        // и рубильником ОС-подмен (suppressOsSurfaces) не выключался — то есть из сравнения
        // «Windows против Linux» до сих пор не исключался ни разу.
        if (string.Equals(Environment.GetEnvironmentVariable("ATOM_DISABLE_PLATFORM_FONTS"), "1", StringComparison.Ordinal))
            return;

        var configurationPath = IOPath.Combine(profilePath, PlatformFontConfiguration.FileName);
        if (!File.Exists(configurationPath))
            return;

        startInfo.Environment["FONTCONFIG_FILE"] = configurationPath;
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

            // Аргументы моста вытесняют одноимённые из пресета: например, локальный навигационный
            // прокси должен заменить пользовательский --proxy-server, а не соседствовать с ним
            // (upstream пользователя учитывается уже самим прокси при форварде).
            RemoveConflictingLaunchArguments(startInfo, launchArgument);
            startInfo.ArgumentList.Add(launchArgument);
        }
    }

    private static void RemoveConflictingLaunchArguments(ProcessStartInfo startInfo, string launchArgument)
    {
        var separatorIndex = launchArgument.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0 || !launchArgument.StartsWith("--", StringComparison.Ordinal))
            return;

        var prefix = launchArgument[..(separatorIndex + 1)];
        for (var index = startInfo.ArgumentList.Count - 1; index >= 0; index--)
        {
            if (startInfo.ArgumentList[index].StartsWith(prefix, StringComparison.Ordinal))
                startInfo.ArgumentList.RemoveAt(index);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void ConfigureLinuxBrowserDisplayEnvironment(ProcessStartInfo startInfo, bool isFirefox, bool useBrowserHeadlessMode, VirtualDisplay? display)
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (display is not null)
        {
            // Дисплей на собственном композиторе сам знает, какие переменные и флаги нужны браузеру.
            if (display.Session is { } session)
            {
                session.ConfigureEnvironment(startInfo);
                return;
            }

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

        // Graceful-освобождение: снимаем запись реестра, чтобы следующий запуск её не выметал.
        try
        {
            BridgeBrowserProcessRegistry.Unregister(process.Id);
        }
        catch (InvalidOperationException)
        {
            // Идентификатор процесса уже недоступен — записи и так не будет.
        }

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