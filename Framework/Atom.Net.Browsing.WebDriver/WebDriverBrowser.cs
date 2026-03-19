using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет браузер, управляемый через WebSocket-мост и расширение-коннектор.
/// </summary>
/// <remarks>
/// <para>
/// В отличие от классических WebDriver-реализаций (Selenium, Puppeteer), этот драйвер
/// не использует CDP (Chrome DevTools Protocol). Вместо этого связь осуществляется
/// через расширение браузера, работающее как мост: .NET ↔ WebSocket ↔ Extension ↔ DOM.
/// </para>
/// <para>
/// Каждая вкладка получает собственный изолированный WebSocket-канал, что обеспечивает
/// полную независимость контекстов — словно каждая вкладка работает в отдельном процессе.
/// Это делает драйвер устойчивым к детектированию и fingerprinting-системам.
/// </para>
/// </remarks>
public sealed class WebDriverBrowser : IWebBrowser
{
    private readonly ConcurrentDictionary<string, WebDriverWindow> windows = new(StringComparer.Ordinal);
    private readonly List<TabContext> contexts = [];
    private readonly SemaphoreSlim contextLock = new(1, 1);
    private readonly BridgeServer bridge;
    private readonly BridgeSettings bridgeSettings;
    private readonly Process? browserProcess;
    private readonly string? userDataDir;
    private string? currentWindowId;
    private bool isDisposed;

    /// <inheritdoc/>
    public IEnumerable<IWebWindow> Windows => windows.Values;

    /// <inheritdoc/>
#pragma warning disable CA1065 // Состояние «нет окон» — исключительная ситуация.
    public IWebWindow CurrentWindow
    {
        get
        {
            if (currentWindowId is not null && windows.TryGetValue(currentWindowId, out var window))
                return window;

            return windows.Values.FirstOrDefault()
                ?? throw new BridgeException("Нет открытых окон браузера.");
        }
    }
#pragma warning restore CA1065

    /// <summary>
    /// Порт WebSocket-сервера моста.
    /// </summary>
    public int BridgePort => bridge.Port;

    /// <summary>
    /// Секретный токен для подключения расширения.
    /// </summary>
    public string Secret => bridgeSettings.Secret;

    /// <summary>
    /// Количество подключённых вкладок.
    /// </summary>
    public int ConnectionCount => bridge.ConnectionCount;

    /// <summary>
    /// Происходит при подключении новой вкладки.
    /// </summary>
    public event AsyncEventHandler<WebDriverBrowser, TabConnectedEventArgs>? TabConnected;

    /// <summary>
    /// Происходит при отключении вкладки.
    /// </summary>
    public event AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs>? TabDisconnected;

    private WebDriverBrowser(BridgeSettings settings, Process? browserProcess)
    {
        bridgeSettings = settings;
        bridge = new BridgeServer(settings);
        this.browserProcess = browserProcess;

        bridge.TabConnected += OnTabConnected;
        bridge.TabDisconnected += OnTabDisconnected;
        bridge.RequestIntercepted += OnRequestIntercepted;
    }

    private WebDriverBrowser(BridgeServer bridgeServer, BridgeSettings settings, Process? browserProcess, string? userDataDir = null)
    {
        bridgeSettings = settings;
        bridge = bridgeServer;
        this.browserProcess = browserProcess;
        this.userDataDir = userDataDir;

        bridge.TabConnected += OnTabConnected;
        bridge.TabDisconnected += OnTabDisconnected;
        bridge.RequestIntercepted += OnRequestIntercepted;
    }

    /// <summary>
    /// Создаёт и запускает экземпляр драйвера браузера.
    /// </summary>
    /// <param name="settings">Настройки WebSocket-моста.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовый к работе экземпляр драйвера.</returns>
    public static async ValueTask<WebDriverBrowser> CreateAsync(
        BridgeSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= new BridgeSettings { Secret = GenerateSecret() };
        var browser = new WebDriverBrowser(settings, browserProcess: null);

        try
        {
            await browser.bridge.StartAsync(cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch
        {
            await browser.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Создаёт драйвер и запускает процесс браузера с расширением-коннектором.
    /// </summary>
    /// <param name="browserPath">Путь к исполняемому файлу браузера.</param>
    /// <param name="extensionPath">Путь к папке расширения-коннектора.</param>
    /// <param name="settings">Настройки WebSocket-моста.</param>
    /// <param name="arguments">Дополнительные аргументы запуска браузера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовый к работе экземпляр драйвера.</returns>
    public static async ValueTask<WebDriverBrowser> LaunchAsync(
        string browserPath,
        string extensionPath,
        BridgeSettings? settings = null,
        IEnumerable<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browserPath);

        settings ??= new BridgeSettings { Secret = GenerateSecret() };

        // Запускаем мост отдельно, чтобы узнать порт до запуска браузера.
        var bridgeServer = new BridgeServer(settings);
        string? tempDir = null;
        Process? process = null;

        try
        {
            await bridgeServer.StartAsync(cancellationToken).ConfigureAwait(false);

            tempDir = Path.Combine(Path.GetTempPath(), $"atom-webdriver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var localExtensionPath = CopyExtensionWithConfig(extensionPath, tempDir, bridgeServer.Port, settings.Secret ?? string.Empty);
            var discoveryUrl = $"http://127.0.0.1:{bridgeServer.Port.ToString(CultureInfo.InvariantCulture)}/";

            var isFirefox = IsFirefoxBrowser(browserPath);
            if (isFirefox)
                PatchManifestForFirefox(localExtensionPath);

            var args = isFirefox
                ? SetupFirefoxProfile(tempDir, localExtensionPath, discoveryUrl, arguments)
                : SetupProfile(tempDir, localExtensionPath, discoveryUrl, arguments);

            process = Process.Start(new ProcessStartInfo(browserPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return new WebDriverBrowser(bridgeServer, settings, process!, tempDir);
        }
        catch
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Процесс уже завершился. */ }
            }

            process?.Dispose();
            await bridgeServer.DisposeAsync().ConfigureAwait(false);

            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (IOException) { /* Не критично — ОС удалит при перезагрузке. */ }
            }

            throw;
        }
    }

    /// <summary>
    /// Создаёт изолированный контекст — отдельный процесс браузера с собственным профилем,
    /// подключённый к общему WebSocket-мосту.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Каждый контекст получает уникальный временный профиль, что обеспечивает полную
    /// изоляцию cookies, localStorage, кэша и сетевого стека на уровне ОС.
    /// </para>
    /// <para>
    /// Прокси, User-Agent, локаль и часовой пояс настраиваются индивидуально
    /// для каждого контекста через <paramref name="settings"/>.
    /// </para>
    /// </remarks>
    /// <param name="browserPath">Путь к исполняемому файлу браузера.</param>
    /// <param name="extensionPath">Путь к папке расширения-коннектора.</param>
    /// <param name="settings">Настройки изолированного контекста.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Изолированный контекст с готовой к работе страницей.</returns>
    public async ValueTask<TabContext> CreateContextAsync(
        string browserPath,
        string extensionPath,
        TabContextSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browserPath);
        ArgumentNullException.ThrowIfNull(extensionPath);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        settings ??= new TabContextSettings();

        // Сериализуем создание контекстов, чтобы корректно определить,
        // какая новая вкладка принадлежит какому процессу.
        await contextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CreateContextCoreAsync(browserPath, extensionPath, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            contextLock.Release();
        }
    }

    private async ValueTask<TabContext> CreateContextCoreAsync(
        string browserPath,
        string extensionPath,
        TabContextSettings settings,
        CancellationToken cancellationToken)
    {
        var existingTabIds = new HashSet<string>(
            bridge.GetChannels().Select(c => c.TabId),
            StringComparer.Ordinal);

        var tempDir = Path.Combine(Path.GetTempPath(), $"atom-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (process, isFirefox) = LaunchContextProcess(browserPath, extensionPath, tempDir, settings);

            try
            {
                var page = await WaitForContextPageAsync(existingTabIds, cancellationToken).ConfigureAwait(false);

                if (isFirefox)
                    AppendFirefoxContextPrefs(tempDir, settings);

                var context = new TabContext(process, tempDir, page, settings);
                contexts.Add(context);
                return context;
            }
            catch
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* Процесс уже завершился. */ }
                }

                process.Dispose();
                throw;
            }
        }
        catch
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { /* Не критично. */ }

            throw;
        }
    }

    private (Process Process, bool IsFirefox) LaunchContextProcess(
        string browserPath,
        string extensionPath,
        string tempDir,
        TabContextSettings settings)
    {
        var localExtensionPath = CopyExtensionWithConfig(
            extensionPath, tempDir, bridge.Port, bridgeSettings.Secret ?? string.Empty);

        var discoveryUrl = settings.StartUrl?.AbsoluteUri
            ?? $"http://127.0.0.1:{bridge.Port.ToString(CultureInfo.InvariantCulture)}/";

        var isFirefox = IsFirefoxBrowser(browserPath);
        var extraArgs = BuildContextArguments(settings);

        if (isFirefox)
            PatchManifestForFirefox(localExtensionPath);

        var args = isFirefox
            ? SetupFirefoxProfile(tempDir, localExtensionPath, discoveryUrl, extraArgs)
            : SetupProfile(tempDir, localExtensionPath, discoveryUrl, extraArgs);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (settings.Timezone is not null)
            process.StartInfo.Environment["TZ"] = settings.Timezone;

        process.Start();
        return (process, isFirefox);
    }

    private async ValueTask<WebDriverPage> WaitForContextPageAsync(
        HashSet<string> existingTabIds,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<TabConnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask OnNewTab(WebDriverBrowser _, TabConnectedEventArgs e)
        {
            if (!existingTabIds.Contains(e.TabId))
                tcs.TrySetResult(e);
            return ValueTask.CompletedTask;
        }

        TabConnected += OnNewTab;
        try
        {
            // Вкладка могла подключиться до подписки.
            foreach (var page in GetAllPages())
            {
                if (!existingTabIds.Contains(page.TabId))
                    return page;
            }

            var result = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return GetPage(result.TabId) ?? CreatePage(result.Channel);
        }
        finally
        {
            TabConnected -= OnNewTab;
        }
    }

    private static List<string> BuildContextArguments(TabContextSettings settings)
    {
        var args = new List<string>();

        if (settings.Arguments is not null)
            args.AddRange(settings.Arguments);

        if (settings.Proxy is not null)
            args.Add($"--proxy-server={settings.Proxy}");

        if (settings.Locale is not null)
            args.Add($"--lang={settings.Locale}");

        return args;
    }

    private static void AppendFirefoxContextPrefs(string profileDir, TabContextSettings settings)
    {
        var prefs = new List<string>();

        if (settings.Proxy is not null && Uri.TryCreate(settings.Proxy, UriKind.Absolute, out var proxyUri))
        {
            var host = proxyUri.Host;
            var port = proxyUri.Port.ToString(CultureInfo.InvariantCulture);

            prefs.Add("""user_pref("network.proxy.type", 1);""");

            if (proxyUri.Scheme is "socks5" or "socks")
            {
                prefs.Add(FormatStringPref("network.proxy.socks", host));
                prefs.Add(FormatRawPref("network.proxy.socks_port", port));
                prefs.Add("""user_pref("network.proxy.socks_version", 5);""");
                prefs.Add("""user_pref("network.proxy.socks_remote_dns", true);""");
            }
            else
            {
                prefs.Add(FormatStringPref("network.proxy.http", host));
                prefs.Add(FormatRawPref("network.proxy.http_port", port));
                prefs.Add(FormatStringPref("network.proxy.ssl", host));
                prefs.Add(FormatRawPref("network.proxy.ssl_port", port));
            }
        }

        if (settings.UserAgent is not null)
            prefs.Add(FormatStringPref("general.useragent.override", settings.UserAgent));

        if (settings.Locale is not null)
            prefs.Add(FormatStringPref("intl.accept_languages", settings.Locale));

        if (prefs.Count > 0)
            File.AppendAllLines(Path.Combine(profileDir, "user.js"), prefs);
    }

    private static string FormatStringPref(string name, string value)
        => string.Concat("user_pref(\"", name, "\", \"", value, "\");");

    private static string FormatRawPref(string name, string value)
        => string.Concat("user_pref(\"", name, "\", ", value, ");");

    /// <summary>
    /// Ожидает подключения вкладки с указанным идентификатором.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница подключённой вкладки.</returns>
    public async ValueTask<WebDriverPage> WaitForTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var channel = await bridge.WaitForTabAsync(tabId, cancellationToken).ConfigureAwait(false);
        return CreatePage(channel);
    }

    /// <summary>
    /// Открывает новую вкладку в текущем окне через расширение.
    /// </summary>
    /// <param name="url">URL для загрузки в новой вкладке.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница открытой вкладки после подключения к мосту.</returns>
    public async ValueTask<WebDriverPage> OpenTabAsync(Uri? url = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.OpenTab,
            url is not null ? new JsonObject { ["url"] = url.AbsoluteUri } : null,
            filter: null,
            cancellationToken).ConfigureAwait(false);

        var tabId = ExtractNewTabId(response);
        return await WaitForNewPageAsync(tabId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новое окно браузера через расширение.
    /// </summary>
    /// <param name="url">URL для загрузки в новом окне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница первой вкладки нового окна после подключения к мосту.</returns>
    public async ValueTask<WebDriverPage> OpenWindowAsync(Uri? url = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.OpenWindow,
            url is not null ? new JsonObject { ["url"] = url.AbsoluteUri } : null,
            filter: null,
            cancellationToken).ConfigureAwait(false);

        var tabId = ExtractNewTabId(response);
        return await WaitForNewPageAsync(tabId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новую вкладку с изоляцией на уровне расширения (cookies, storage, UA).
    /// </summary>
    /// <remarks>
    /// <para>
    /// В отличие от <see cref="CreateContextAsync"/>, не создаёт отдельный процесс.
    /// Вкладка открывается в текущем окне браузера, а изоляция обеспечивается расширением:
    /// виртуальное cookie-хранилище, пространства имён для localStorage/sessionStorage
    /// и подмена navigator-свойств в MAIN world.
    /// </para>
    /// <para>
    /// User-Agent подменяется на сетевом уровне: <c>declarativeNetRequest</c> (Chromium)
    /// или <c>webRequest.onBeforeSendHeaders</c> (Firefox).
    /// </para>
    /// </remarks>
    /// <param name="url">URL для загрузки в новой вкладке.</param>
    /// <param name="settings">Настройки изоляции. Если <see langword="null"/>, открывает обычную вкладку.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница с настроенной изоляцией.</returns>
    public async ValueTask<WebDriverPage> OpenIsolatedTabAsync(
        Uri? url = null,
        TabContextSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var page = await OpenTabAsync(url: null, cancellationToken).ConfigureAwait(false);

        if (settings is not null)
        {
            var payload = BuildSetTabContextPayload(settings);
            await page.SendBridgeCommandAsync(
                Protocol.BridgeCommand.SetTabContext, payload, cancellationToken).ConfigureAwait(false);
        }

        if (url is not null)
            await page.NavigateAsync(url, cancellationToken).ConfigureAwait(false);

        return page;
    }

    internal static JsonObject BuildSetTabContextPayload(TabContextSettings settings)
    {
        var payload = new JsonObject
        {
            ["contextId"] = Guid.NewGuid().ToString("N"),
        };

        if (settings.UserAgent is not null)
            payload["userAgent"] = settings.UserAgent;

        if (settings.Locale is not null)
            payload["locale"] = settings.Locale;

        if (settings.Timezone is not null)
            payload["timezone"] = settings.Timezone;

        if (settings.Platform is not null)
            payload["platform"] = settings.Platform;

        if (settings.Languages is { Count: > 0 })
            payload["languages"] = new JsonArray(settings.Languages.Select(l => (JsonNode)JsonValue.Create(l)).ToArray());

        if (settings.Proxy is not null)
            payload["proxy"] = settings.Proxy;

        if (settings.Screen is not null)
            payload["screen"] = BuildScreenNode(settings.Screen);

        if (settings.WebGL is not null)
            payload["webgl"] = BuildWebGLNode(settings.WebGL);

        if (settings.CanvasNoise)
            payload["canvasNoise"] = true;

        if (settings.WebRtcPolicy is not null)
            payload["webrtcPolicy"] = settings.WebRtcPolicy;

        ApplyExtendedFingerprintSettings(payload, settings);
        return payload;
    }

    internal static void ApplyExtendedFingerprintSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.Geolocation is not null)
            payload["geolocation"] = BuildGeolocationNode(settings.Geolocation);

        if (settings.AllowedFonts is { Count: > 0 })
            payload["allowedFonts"] = new JsonArray(settings.AllowedFonts.Select(f => (JsonNode)JsonValue.Create(f)).ToArray());

        if (settings.AudioNoise)
            payload["audioNoise"] = true;

        if (settings.HardwareConcurrency is not null)
            payload["hardwareConcurrency"] = settings.HardwareConcurrency;

        if (settings.DeviceMemory is not null)
            payload["deviceMemory"] = settings.DeviceMemory;

        if (settings.BatteryProtection)
            payload["batteryProtection"] = true;

        if (settings.PermissionsProtection)
            payload["permissionsProtection"] = true;

        if (settings.ClientHints is not null)
            payload["clientHints"] = BuildClientHintsNode(settings.ClientHints);

        if (settings.NetworkInfo is not null)
            payload["networkInfo"] = BuildNetworkInfoNode(settings.NetworkInfo);

        if (settings.SpeechVoices is not null)
        {
            payload["speechVoices"] = new JsonArray(settings.SpeechVoices.Select(v => (JsonNode)new JsonObject
            {
                ["name"] = v.Name,
                ["lang"] = v.Lang,
                ["localService"] = v.LocalService,
            }).ToArray());
        }

        if (settings.MediaDevicesProtection)
            payload["mediaDevicesProtection"] = true;

        if (settings.VirtualMediaDevices is not null)
            payload["virtualMediaDevices"] = BuildVirtualMediaDevicesNode(settings.VirtualMediaDevices);

        if (settings.WebGLParams is not null)
            payload["webglParams"] = BuildWebGLParamsNode(settings.WebGLParams);

        if (settings.DoNotTrack is not null)
            payload["doNotTrack"] = settings.DoNotTrack;

        if (settings.GlobalPrivacyControl is not null)
            payload["globalPrivacyControl"] = settings.GlobalPrivacyControl;

        ApplyEnvironmentFingerprintSettings(payload, settings);
    }

    internal static void ApplyEnvironmentFingerprintSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.IntlSpoofing)
            payload["intlSpoofing"] = true;

        if (settings.ScreenOrientation is not null)
            payload["screenOrientation"] = settings.ScreenOrientation;

        if (settings.ColorScheme is not null)
            payload["colorScheme"] = settings.ColorScheme;

        if (settings.ReducedMotion is not null)
            payload["reducedMotion"] = settings.ReducedMotion;

        if (settings.TimerPrecisionMs is not null)
            payload["timerPrecisionMs"] = settings.TimerPrecisionMs;

        if (settings.WebSocketProtection is not null)
            payload["webSocketProtection"] = settings.WebSocketProtection;

        if (settings.WebGLNoise)
            payload["webglNoise"] = true;

        if (settings.StorageQuota is not null)
            payload["storageQuota"] = settings.StorageQuota;

        if (settings.KeyboardLayout is not null)
            payload["keyboardLayout"] = settings.KeyboardLayout;

        if (settings.WebRtcIcePolicy is not null)
            payload["webrtcIcePolicy"] = settings.WebRtcIcePolicy;

        if (settings.PluginSpoofing)
            payload["pluginSpoofing"] = true;

        ApplyMiscFingerprintSettings(payload, settings);
    }

    internal static void ApplyMiscFingerprintSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.SpeechRecognitionProtection)
            payload["speechRecognitionProtection"] = true;

        if (settings.MaxTouchPoints is not null)
            payload["maxTouchPoints"] = settings.MaxTouchPoints;

        if (settings.AudioSampleRate is not null)
            payload["audioSampleRate"] = settings.AudioSampleRate;

        if (settings.AudioChannelCount is not null)
            payload["audioChannelCount"] = settings.AudioChannelCount;

        if (settings.PdfViewerEnabled is not null)
            payload["pdfViewerEnabled"] = settings.PdfViewerEnabled;

        if (settings.NotificationPermission is not null)
            payload["notificationPermission"] = settings.NotificationPermission;

        if (settings.GamepadProtection)
            payload["gamepadProtection"] = true;

        if (settings.HardwareApiProtection)
            payload["hardwareApiProtection"] = true;

        if (settings.PerformanceProtection)
            payload["performanceProtection"] = true;

        if (settings.DocumentReferrer is not null)
            payload["documentReferrer"] = settings.DocumentReferrer;

        if (settings.HistoryLength is not null)
            payload["historyLength"] = settings.HistoryLength;

        ApplySensorFingerprintSettings(payload, settings);
    }

    internal static void ApplySensorFingerprintSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.DeviceMotionProtection)
            payload["deviceMotionProtection"] = true;

        if (settings.AmbientLightProtection)
            payload["ambientLightProtection"] = true;

        if (settings.ConnectionRtt is not null)
            payload["connectionRtt"] = settings.ConnectionRtt;

        if (settings.ConnectionDownlink is not null)
            payload["connectionDownlink"] = settings.ConnectionDownlink;

        if (settings.MediaCapabilitiesProtection)
            payload["mediaCapabilitiesProtection"] = true;

        if (settings.ClipboardProtection)
            payload["clipboardProtection"] = true;

        if (settings.WebShareProtection)
            payload["webShareProtection"] = true;

        if (settings.WakeLockProtection)
            payload["wakeLockProtection"] = true;

        if (settings.IdleDetectionProtection)
            payload["idleDetectionProtection"] = true;

        if (settings.CredentialProtection)
            payload["credentialProtection"] = true;

        if (settings.PaymentProtection)
            payload["paymentProtection"] = true;

        ApplyApiHardeningSettings(payload, settings);
        ApplyBrowserApiBlockingSettings(payload, settings);
        ApplyPlatformApiBlockingSettings(payload, settings);
        ApplyAdvancedApiBlockingSettings(payload, settings);
        ApplyEmergingApiSettings(payload, settings);
    }

    internal static void ApplyApiHardeningSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.StorageEstimateUsage is not null)
            payload["storageEstimateUsage"] = settings.StorageEstimateUsage;

        if (settings.FileSystemAccessProtection)
            payload["fileSystemAccessProtection"] = true;

        if (settings.BeaconProtection)
            payload["beaconProtection"] = true;

        if (settings.VisibilityStateOverride is not null)
            payload["visibilityStateOverride"] = settings.VisibilityStateOverride;

        if (settings.ColorDepth is not null)
            payload["colorDepth"] = settings.ColorDepth;

        if (settings.InstalledAppsProtection)
            payload["installedAppsProtection"] = true;

        if (settings.FontMetricsProtection)
            payload["fontMetricsProtection"] = true;

        if (settings.CrossOriginIsolationOverride is not null)
            payload["crossOriginIsolationOverride"] = settings.CrossOriginIsolationOverride;

        if (settings.PerformanceNowJitter is not null)
            payload["performanceNowJitter"] = settings.PerformanceNowJitter;

        if (settings.WindowControlsOverlayProtection)
            payload["windowControlsOverlayProtection"] = true;

        if (settings.ScreenOrientationLockProtection)
            payload["screenOrientationLockProtection"] = true;

        if (settings.KeyboardApiProtection)
            payload["keyboardApiProtection"] = true;

        if (settings.UsbHidSerialProtection)
            payload["usbHidSerialProtection"] = true;

        if (settings.PresentationApiProtection)
            payload["presentationApiProtection"] = true;

        if (settings.ContactsApiProtection)
            payload["contactsApiProtection"] = true;
    }

    internal static void ApplyBrowserApiBlockingSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.BluetoothProtection)
            payload["bluetoothProtection"] = true;

        if (settings.EyeDropperProtection)
            payload["eyeDropperProtection"] = true;

        if (settings.MultiScreenProtection)
            payload["multiScreenProtection"] = true;

        if (settings.InkApiProtection)
            payload["inkApiProtection"] = true;

        if (settings.VirtualKeyboardProtection)
            payload["virtualKeyboardProtection"] = true;

        if (settings.NfcProtection)
            payload["nfcProtection"] = true;

        if (settings.FileHandlingProtection)
            payload["fileHandlingProtection"] = true;

        if (settings.WebXrProtection)
            payload["webXrProtection"] = true;

        if (settings.WebNnProtection)
            payload["webNnProtection"] = true;

        if (settings.SchedulingProtection)
            payload["schedulingProtection"] = true;
    }

    internal static void ApplyPlatformApiBlockingSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.StorageAccessProtection)
            payload["storageAccessProtection"] = true;

        if (settings.ContentIndexProtection)
            payload["contentIndexProtection"] = true;

        if (settings.BackgroundSyncProtection)
            payload["backgroundSyncProtection"] = true;

        if (settings.CookieStoreProtection)
            payload["cookieStoreProtection"] = true;

        if (settings.WebLocksProtection)
            payload["webLocksProtection"] = true;

        if (settings.ShapeDetectionProtection)
            payload["shapeDetectionProtection"] = true;

        if (settings.WebTransportProtection)
            payload["webTransportProtection"] = true;

        if (settings.RelatedAppsProtection)
            payload["relatedAppsProtection"] = true;

        if (settings.DigitalGoodsProtection)
            payload["digitalGoodsProtection"] = true;

        if (settings.ComputePressureProtection)
            payload["computePressureProtection"] = true;
    }

    internal static void ApplyAdvancedApiBlockingSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.FileSystemPickerProtection)
            payload["fileSystemPickerProtection"] = true;

        if (settings.DisplayOverrideProtection)
            payload["displayOverrideProtection"] = true;

        if (settings.BatteryLevelOverride is not null)
            payload["batteryLevelOverride"] = settings.BatteryLevelOverride.Value;

        if (settings.PictureInPictureProtection)
            payload["pictureInPictureProtection"] = true;

        if (settings.DevicePostureProtection)
            payload["devicePostureProtection"] = true;

        if (settings.WebAuthnProtection)
            payload["webAuthnProtection"] = true;

        if (settings.FedCmProtection)
            payload["fedCmProtection"] = true;

        if (settings.LocalFontAccessProtection)
            payload["localFontAccessProtection"] = true;

        if (settings.AutoplayPolicyProtection)
            payload["autoplayPolicyProtection"] = true;

        if (settings.LaunchHandlerProtection)
            payload["launchHandlerProtection"] = true;

        if (settings.TopicsApiProtection)
            payload["topicsApiProtection"] = true;

        if (settings.AttributionReportingProtection)
            payload["attributionReportingProtection"] = true;

        if (settings.FencedFrameProtection)
            payload["fencedFrameProtection"] = true;

        if (settings.SharedStorageProtection)
            payload["sharedStorageProtection"] = true;

        if (settings.PrivateAggregationProtection)
            payload["privateAggregationProtection"] = true;
    }

    internal static void ApplyEmergingApiSettings(JsonObject payload, TabContextSettings settings)
    {
        if (settings.WebOtpProtection)
            payload["webOtpProtection"] = true;

        if (settings.WebMidiProtection)
            payload["webMidiProtection"] = true;

        if (settings.WebCodecsProtection)
            payload["webCodecsProtection"] = true;

        if (settings.NavigationApiProtection)
            payload["navigationApiProtection"] = true;

        if (settings.ScreenCaptureProtection)
            payload["screenCaptureProtection"] = true;
    }

    internal static JsonObject BuildScreenNode(ScreenSettings screen)
    {
        var node = new JsonObject();
        if (screen.Width is not null) node["width"] = screen.Width;
        if (screen.Height is not null) node["height"] = screen.Height;
        if (screen.ColorDepth is not null) node["colorDepth"] = screen.ColorDepth;
        return node;
    }

    internal static JsonObject BuildWebGLNode(WebGLSettings webgl)
    {
        var node = new JsonObject();
        if (webgl.Vendor is not null) node["vendor"] = webgl.Vendor;
        if (webgl.Renderer is not null) node["renderer"] = webgl.Renderer;
        return node;
    }

    internal static JsonObject BuildGeolocationNode(GeolocationSettings geo)
    {
        var node = new JsonObject
        {
            ["latitude"] = geo.Latitude,
            ["longitude"] = geo.Longitude,
        };
        if (geo.Accuracy is not null) node["accuracy"] = geo.Accuracy;
        return node;
    }

    internal static JsonObject BuildClientHintsNode(ClientHintsSettings hints)
    {
        var node = new JsonObject();
        if (hints.Platform is not null) node["platform"] = hints.Platform;
        if (hints.PlatformVersion is not null) node["platformVersion"] = hints.PlatformVersion;
        if (hints.Mobile is not null) node["mobile"] = hints.Mobile;
        if (hints.Architecture is not null) node["architecture"] = hints.Architecture;
        if (hints.Model is not null) node["model"] = hints.Model;
        if (hints.Bitness is not null) node["bitness"] = hints.Bitness;

        if (hints.Brands is { Count: > 0 })
            node["brands"] = new JsonArray(hints.Brands.Select(b => (JsonNode)new JsonObject { ["brand"] = b.Brand, ["version"] = b.Version }).ToArray());

        if (hints.FullVersionList is { Count: > 0 })
            node["fullVersionList"] = new JsonArray(hints.FullVersionList.Select(b => (JsonNode)new JsonObject { ["brand"] = b.Brand, ["version"] = b.Version }).ToArray());

        return node;
    }

    internal static JsonObject BuildNetworkInfoNode(NetworkInfoSettings info)
    {
        return new JsonObject
        {
            ["effectiveType"] = info.EffectiveType,
            ["rtt"] = info.Rtt,
            ["downlink"] = info.Downlink,
            ["saveData"] = info.SaveData,
        };
    }

    internal static JsonObject BuildWebGLParamsNode(WebGLParamsSettings p)
    {
        var node = new JsonObject();
        if (p.MaxTextureSize is not null) node["maxTextureSize"] = p.MaxTextureSize;
        if (p.MaxRenderbufferSize is not null) node["maxRenderbufferSize"] = p.MaxRenderbufferSize;
        if (p.MaxViewportDims is { Count: 2 }) node["maxViewportDims"] = new JsonArray(p.MaxViewportDims[0], p.MaxViewportDims[1]);
        if (p.MaxVaryingVectors is not null) node["maxVaryingVectors"] = p.MaxVaryingVectors;
        if (p.MaxVertexUniformVectors is not null) node["maxVertexUniformVectors"] = p.MaxVertexUniformVectors;
        if (p.MaxFragmentUniformVectors is not null) node["maxFragmentUniformVectors"] = p.MaxFragmentUniformVectors;
        return node;
    }

    internal static JsonObject BuildVirtualMediaDevicesNode(VirtualMediaDevicesSettings settings)
    {
        return new JsonObject
        {
            ["audioInputEnabled"] = settings.AudioInputEnabled,
            ["videoInputEnabled"] = settings.VideoInputEnabled,
            ["audioOutputEnabled"] = settings.AudioOutputEnabled,
            ["audioInputLabel"] = settings.AudioInputLabel,
            ["audioInputBrowserDeviceId"] = settings.AudioInputBrowserDeviceId,
            ["videoInputLabel"] = settings.VideoInputLabel,
            ["videoInputBrowserDeviceId"] = settings.VideoInputBrowserDeviceId,
            ["audioOutputLabel"] = settings.AudioOutputLabel,
            ["groupId"] = settings.GroupId,
        };
    }

    /// <summary>
    /// Закрывает вкладку по идентификатору через расширение.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки для закрытия.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask CloseTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tabId);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var colonIndex = tabId.IndexOf(':', StringComparison.Ordinal);
        var rawTabId = colonIndex >= 0 ? tabId[(colonIndex + 1)..] : tabId;

        await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.CloseTab,
            new JsonObject { ["tabId"] = rawTabId },
            c => !string.Equals(c.TabId, tabId, StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Закрывает окно браузера по идентификатору через расширение.
    /// Все вкладки в окне будут закрыты.
    /// </summary>
    /// <param name="windowId">Идентификатор окна для закрытия.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.CloseWindow,
            new JsonObject { ["windowId"] = windowId },
            c => !c.TabId.StartsWith(windowId + ":", StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Активирует (переключает фокус на) указанную вкладку.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки для активации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask ActivateTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tabId);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var colonIndex = tabId.IndexOf(':', StringComparison.Ordinal);
        var rawTabId = colonIndex >= 0 ? tabId[(colonIndex + 1)..] : tabId;

        await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.ActivateTab,
            new JsonObject { ["tabId"] = rawTabId },
            filter: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Активирует (переключает фокус на) указанное окно.
    /// </summary>
    /// <param name="windowId">Идентификатор окна для активации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask ActivateWindowAsync(string windowId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await SendViaAnyChannelAsync(
            Protocol.BridgeCommand.ActivateWindow,
            new JsonObject { ["windowId"] = windowId },
            filter: null,
            cancellationToken).ConfigureAwait(false);
    }

    private TabChannel GetAnySenderChannel()
    {
        var channel = bridge.GetChannels().FirstOrDefault(c => c.IsConnected)
            ?? throw new BridgeException("Нет подключённых вкладок для отправки команды.");
        return channel;
    }

    private async ValueTask<Protocol.BridgeMessage> SendViaAnyChannelAsync(
        Protocol.BridgeCommand command,
        object? payload,
        Func<TabChannel, bool>? filter,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var channels = bridge.GetChannels().Where(c => c.IsConnected);
            var channel = filter is not null ? channels.FirstOrDefault(filter) ?? GetAnySenderChannel() : GetAnySenderChannel();

            try
            {
                return await channel.SendCommandAsync(command, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new BridgeException("Не удалось отправить команду: все каналы разорваны.");
    }

    private static string ExtractNewTabId(Protocol.BridgeMessage response)
    {
        if (response.Status != Protocol.BridgeStatus.Ok)
            throw new BridgeException($"Не удалось выполнить команду: {response.Error}");

        if (response.Payload is System.Text.Json.JsonElement el &&
            el.TryGetProperty("tabId", out var tabIdProp))
        {
            // tabId может быть числом или строкой — принимаем оба варианта.
            return tabIdProp.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => tabIdProp.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Json.JsonValueKind.String => tabIdProp.GetString() ?? throw new BridgeException("tabId в ответе пуст."),
                _ => throw new BridgeException($"Неожиданный тип tabId: {tabIdProp.ValueKind}."),
            };
        }

        throw new BridgeException("Ответ не содержит tabId.");
    }

    private async ValueTask<WebDriverPage> WaitForNewPageAsync(string rawTabId, CancellationToken cancellationToken)
    {
        // rawTabId — числовой ID вкладки из ответа расширения (напр. "84914200").
        // Канал регистрируется с составным ID "windowId:tabId" (напр. "84914194:84914200").
        // Ожидаем подключения канала, чей ID заканчивается на ":rawTabId".
        var suffix = string.Concat(":", rawTabId);
        var tcs = new TaskCompletionSource<TabConnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask OnTabConnectedForWait(WebDriverBrowser _, TabConnectedEventArgs e)
        {
            if (e.TabId.EndsWith(suffix, StringComparison.Ordinal) ||
                string.Equals(e.TabId, rawTabId, StringComparison.Ordinal))
            {
                tcs.TrySetResult(e);
            }

            return ValueTask.CompletedTask;
        }

        TabConnected += OnTabConnectedForWait;
        try
        {
            // Возможно, вкладка уже подключилась ранее.
            foreach (var page in GetAllPages())
            {
                if (page.TabId.EndsWith(suffix, StringComparison.Ordinal) ||
                    string.Equals(page.TabId, rawTabId, StringComparison.Ordinal))
                {
                    return page;
                }
            }

            var result = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            var existingPage = GetPage(result.TabId);
            return existingPage ?? CreatePage(result.Channel);
        }
        finally
        {
            TabConnected -= OnTabConnectedForWait;
        }
    }

    /// <summary>
    /// Возвращает все подключённые страницы из всех окон.
    /// </summary>
    public IEnumerable<WebDriverPage> GetAllPages() => windows.Values.SelectMany(w => w.Pages).OfType<WebDriverPage>();

    /// <summary>
    /// Возвращает страницу по идентификатору вкладки.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    public WebDriverPage? GetPage(string tabId)
    {
        foreach (var window in windows.Values)
        {
            var page = window.GetPage(tabId);
            if (page is not null) return page;
        }

        return null;
    }

    private async ValueTask OnTabConnected(BridgeServer sender, TabConnectedEventArgs e)
    {
        var windowId = ExtractWindowId(e.TabId);
        var window = windows.GetOrAdd(windowId, static id => new WebDriverWindow(id));
#pragma warning disable CA2000 // Страница передаётся в window.AddPage и будет диспозирована через окно.
        var page = CreatePage(e.Channel);
#pragma warning restore CA2000
        page.Disposing += OnPageDisposing;
        window.AddPage(page);

        currentWindowId ??= windowId;

        if (TabConnected is { } handler)
            await handler(this, e).ConfigureAwait(false);
    }

    private async ValueTask OnPageDisposing(WebDriverPage page)
    {
        page.Disposing -= OnPageDisposing;

        if (isDisposed) return;

        try
        {
            await CloseTabAsync(page.TabId).ConfigureAwait(false);
        }
        catch (BridgeException)
        {
            // Нет доступных каналов для отправки команды — ничего не делаем.
        }
    }

    private async ValueTask OnRequestIntercepted(BridgeServer sender, InterceptedRequestEventArgs e)
    {
        var page = GetPage(e.TabId);
        if (page is not null)
            await page.OnRequestInterceptedAsync(e).ConfigureAwait(false);
        else
            e.SetDefaultIfPending();
    }

    private async ValueTask OnTabDisconnected(BridgeServer sender, TabDisconnectedEventArgs e)
    {
        var windowId = ExtractWindowId(e.TabId);

        if (windows.TryGetValue(windowId, out var window))
        {
            window.RemovePage(e.TabId);

            if (window.PageCount == 0)
                windows.TryRemove(windowId, out _);
        }

        if (string.Equals(currentWindowId, windowId, StringComparison.Ordinal) && !windows.ContainsKey(windowId))
            currentWindowId = windows.Keys.FirstOrDefault();

        if (TabDisconnected is { } handler)
            await handler(this, e).ConfigureAwait(false);
    }

    private static string ExtractWindowId(string tabId)
    {
        // Формат tabId: "windowId:tabIndex" или просто "tabId" (для единственного окна).
        var separatorIndex = tabId.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex >= 0 ? tabId[..separatorIndex] : "default";
    }

    private WebDriverPage CreatePage(TabChannel channel)
    {
        return new WebDriverPage(channel, browserProcess)
        {
            RegisterFulfillment = bridge.RegisterFulfillment,
        };
    }

    /// <summary>
    /// Модифицирует manifest.json скопированного расширения для совместимости с Firefox:
    /// добавляет <c>browser_specific_settings.gecko</c> и убирает <c>persistent</c>.
    /// </summary>
    private static void PatchManifestForFirefox(string extensionDir)
    {
        var manifestPath = Path.Combine(extensionDir, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();

        // 1. Добавляем gecko-настройки (ID + минимальная версия).
        manifest["browser_specific_settings"] = new JsonObject
        {
            ["gecko"] = new JsonObject
            {
                ["id"] = "atom-webdriver@escorpdynamics.com",
                ["strict_min_version"] = "109.0",
            },
        };

        // 2. Убираем persistent из background (Firefox не поддерживает).
        if (manifest["background"] is JsonObject bg)
            bg.Remove("persistent");

        // 3. Записываем обратно.
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(manifestPath, manifest.ToJsonString(options));
    }

    private static bool IsFirefoxBrowser(string browserPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(browserPath);
        return fileName.Contains("firefox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Настраивает профиль Firefox и возвращает аргументы командной строки.
    /// Расширение устанавливается через proxy-файл в extensions/,
    /// а xpinstall.signatures.required отключается через user.js.
    /// Требует Firefox Developer Edition или Nightly (release-сборки игнорируют эту настройку).
    /// </summary>
    private static string SetupFirefoxProfile(
        string profileDir,
        string extensionDir,
        string discoveryUrl,
        IEnumerable<string>? extraArguments)
    {
        // Читаем gecko ID из manifest.json расширения.
        var manifestPath = Path.Combine(extensionDir, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var addonId = manifest["browser_specific_settings"]?["gecko"]?["id"]?.GetValue<string>()
            ?? throw new BridgeException("Firefox-расширение не содержит browser_specific_settings.gecko.id в manifest.json.");

        // Копируем расширение (с config.json) в extensions/<addon-id>/.
        // Firefox загружает расширения из этой папки и browser.runtime.getURL
        // резолвит ресурсы (включая config.json) из неё.
        var addonDir = Path.Combine(profileDir, "extensions", addonId);
        CopyDirectoryRecursive(extensionDir, addonDir);

        // user.js — настройки профиля.
        var userJs = new StringBuilder()
            .AppendLine("""user_pref("xpinstall.signatures.required", false);""")
            .AppendLine("""user_pref("extensions.autoDisableScopes", 0);""")
            .AppendLine("""user_pref("extensions.enabledScopes", 15);""")
            .AppendLine("""user_pref("app.normandy.first_run", false);""")
            .AppendLine("""user_pref("browser.startup.homepage_override.mstone", "ignore");""")
            .AppendLine("""user_pref("browser.shell.checkDefaultBrowser", false);""")
            .AppendLine("""user_pref("datareporting.policy.dataSubmissionEnabled", false);""")
            .AppendLine("""user_pref("toolkit.telemetry.reportingpolicy.firstRun", false);""")
            // Подавляем домашнюю/приветственную страницу.
            .AppendLine("""user_pref("browser.startup.page", 0);""")
            .AppendLine("""user_pref("browser.startup.homepage", "about:blank");""")
            .AppendLine("""user_pref("startup.homepage_welcome_url", "");""")
            .AppendLine("""user_pref("startup.homepage_welcome_url.additional", "");""")
            .AppendLine("""user_pref("browser.aboutwelcome.enabled", false);""");

        File.WriteAllText(Path.Combine(profileDir, "user.js"), userJs.ToString());

        return BuildFirefoxArguments(profileDir, discoveryUrl, extraArguments);
    }

    private static string BuildFirefoxArguments(
        string profileDir,
        string discoveryUrl,
        IEnumerable<string>? extraArguments)
    {
        List<string> args =
        [
            "-profile",
            profileDir,
            "-no-remote",
        ];

        if (extraArguments is not null)
            args.AddRange(extraArguments);

        args.Add(discoveryUrl);

        return string.Join(' ', args);
    }

    private static string BuildBrowserArguments(
        string userDataDir,
        string discoveryUrl,
        IEnumerable<string>? extraArguments)
    {
        var args = NormalizeChromiumLaunchArguments(extraArguments);
        args.InsertRange(0,
        [
            $"--user-data-dir={userDataDir}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-timer-throttling",
            "--disable-features=msEdgeFRE,msEdgeFREOnboarding,msEdgeNewTabPage",
            "--extension-manifest-v2-availability=2",
        ]);

        // Discovery URL передаётся как аргумент запуска, чтобы браузер открыл
        // его в первом табе. Это необходимо для Chrome, где service worker
        // расширения не стартует на пустом профиле без реального URL.
        // Для Vivaldi тот же результат достигается через config.json + tabs.create.
        args.Add(discoveryUrl);

        return string.Join(' ', args);
    }

    internal static List<string> NormalizeChromiumLaunchArguments(IEnumerable<string>? extraArguments)
    {
        List<string> args =
        [
        ];

        if (extraArguments is not null)
        {
            args.AddRange(extraArguments);
        }

        EnsureChromiumPipeWireCameraSupport(args);

        return args;
    }

    internal static void MergeChromiumEnabledFeature(List<string> args, string featureName)
    {
        const string enableFeaturesPrefix = "--enable-features=";

        var enabledFeatures = new List<string>();
        for (var index = args.Count - 1; index >= 0; index--)
        {
            var argument = args[index];
            if (!argument.StartsWith(enableFeaturesPrefix, StringComparison.Ordinal))
                continue;

            enabledFeatures.AddRange(argument[enableFeaturesPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            args.RemoveAt(index);
        }

        if (!enabledFeatures.Contains(featureName, StringComparer.Ordinal))
            enabledFeatures.Add(featureName);

        args.Add(enableFeaturesPrefix + string.Join(',', enabledFeatures.Distinct(StringComparer.Ordinal)));
    }

    private static void EnsureChromiumPipeWireCameraSupport(List<string> args)
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (!string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            return;

        const string featureName = "WebRtcPipeWireCamera";
        const string disableFeaturesPrefix = "--disable-features=";
        const string ozonePlatformPrefix = "--ozone-platform=";
        const string ozonePlatformHintPrefix = "--ozone-platform-hint=";

        if (!args.Exists(static argument => argument.StartsWith(ozonePlatformPrefix, StringComparison.Ordinal)
            || argument.StartsWith(ozonePlatformHintPrefix, StringComparison.Ordinal)))
        {
            args.Add("--ozone-platform-hint=auto");
        }

        foreach (var argument in args)
        {
            if (!argument.StartsWith(disableFeaturesPrefix, StringComparison.Ordinal))
                continue;

            var disabledFeatures = argument[disableFeaturesPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (disabledFeatures.Contains(featureName, StringComparer.Ordinal))
                return;
        }

        MergeChromiumEnabledFeature(args, featureName);
    }

    /// <summary>
    /// Копирует расширение в <paramref name="tempDir"/> и записывает config.json
    /// с параметрами подключения. Это позволяет background.js автоматически
    /// сконфигурировать мост при запуске — без ожидания discovery-страницы.
    /// </summary>
    /// <returns>Путь к локальной копии расширения.</returns>
    private static string CopyExtensionWithConfig(string extensionPath, string tempDir, int port, string secret)
    {
        var localExtensionDir = Path.Combine(tempDir, "extension");

        CopyDirectoryRecursive(extensionPath, localExtensionDir);

        var configJson = new JsonObject
        {
            ["host"] = "127.0.0.1",
            ["port"] = port,
            ["secret"] = secret,
        };

        File.WriteAllText(Path.Combine(localExtensionDir, "config.json"), configJson.ToJsonString());

        return localExtensionDir;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }

    /// <summary>
    /// Настраивает профиль браузера и возвращает аргументы командной строки.
    /// Расширение прописывается в Preferences (pre-seeded profile) с RSA-ключом,
    /// что обеспечивает загрузку расширения во всех Chromium-браузерах.
    /// </summary>
    private static string SetupProfile(
        string userDataDir,
        string extensionDir,
        string discoveryUrl,
        IEnumerable<string>? extraArguments)
    {
        AddExtensionKey(extensionDir);
        WritePreSeededProfile(userDataDir, extensionDir);
        return BuildBrowserArguments(userDataDir, discoveryUrl, extraArguments);
    }

    /// <summary>
    /// Добавляет RSA-ключ в manifest.json расширения для pre-seeded профиля.
    /// Chromium использует ключ для вычисления стабильного extension ID.
    /// </summary>
    private static void AddExtensionKey(string extensionDir)
    {
        var manifestPath = Path.Combine(extensionDir, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();

        var (publicKeyBase64, _) = GetOrCreateExtensionKey(extensionDir);
        manifest["key"] = publicKeyBase64;

        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Создаёт профиль с прописанным расширением в Preferences.
    /// Это позволяет Chromium-браузерам загружать расширение без --load-extension.
    /// </summary>
    private static void WritePreSeededProfile(string userDataDir, string extensionDir)
    {
        var (_, extensionId) = GetOrCreateExtensionKey(extensionDir);

        var manifestPath = Path.Combine(extensionDir, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));

        var defaultDir = Path.Combine(userDataDir, "Default");
        Directory.CreateDirectory(defaultDir);

        var installTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var preferences = BuildPreferences(extensionDir, extensionId, manifest, installTime);

        File.WriteAllText(Path.Combine(defaultDir, "Preferences"), preferences.ToJsonString());

        const string localState = """
            {
                "fre": { "has_user_seen_fre": true }
            }
            """;
        File.WriteAllText(Path.Combine(userDataDir, "Local State"), localState);

        // Sentinel-файл «First Run» — Vivaldi и другие Chromium-браузеры проверяют
        // его наличие. Если файл существует, welcome-страница не открывается.
        // --no-first-run решает это для большинства браузеров, но Vivaldi его игнорирует.
        File.WriteAllText(Path.Combine(userDataDir, "First Run"), string.Empty);
    }

    private static JsonObject BuildPreferences(
        string extensionDir, string extensionId, JsonNode? manifest, string installTime)
    {
        return new JsonObject
        {
            ["session"] = new JsonObject
            {
                ["restore_on_startup"] = 4,
                ["startup_urls"] = new JsonArray(),
            },
            ["browser"] = new JsonObject
            {
                ["has_seen_welcome_page"] = true,
                ["show_hub_popup_on_browser_startup"] = false,
                ["check_default_browser"] = false,
            },
            ["distribution"] = new JsonObject { ["skip_first_run_ui"] = true },
            ["extensions"] = new JsonObject
            {
                ["ui"] = new JsonObject { ["developer_mode"] = true },
                ["settings"] = new JsonObject
                {
                    [extensionId] = BuildExtensionSettings(extensionDir, manifest, installTime),
                },
            },
            ["vivaldi"] = new JsonObject
            {
                ["startup"] = new JsonObject
                {
                    ["homepage"] = "about:blank",
                    ["has_seen_welcome_page"] = true,
                    ["has_seen_feature"] = 1,
                    ["first_seen_version"] = "99.0.0.0",
                    ["last_seen_version"] = "99.0.0.0",
                    ["type"] = "speeddial",
                },
                ["welcome"] = new JsonObject
                {
                    ["read_pages"] = new JsonArray(
                        "intro", "account", "import_data", "tracker_and_ad",
                        "personalize", "tabs", "welcome_feature_amount", "mail_setup"),
                },
                ["address_bar"] = new JsonObject { ["show_bookmarks_menu"] = false },
            },
            ["first_run_tabs"] = new JsonArray(),
        };
    }

    private static JsonObject BuildExtensionSettings(string extensionDir, JsonNode? manifest, string installTime)
    {
        return new JsonObject
        {
            ["active_permissions"] = new JsonObject
            {
                ["api"] = new JsonArray("tabs", "cookies", "webRequest", "webRequestBlocking", "proxy"),
                ["manifest_permissions"] = new JsonArray(),
                ["explicit_host"] = new JsonArray("<all_urls>"),
            },
            ["creation_flags"] = 1,
            ["from_webstore"] = false,
            ["granted_permissions"] = new JsonObject
            {
                ["api"] = new JsonArray("tabs", "cookies", "webRequest", "webRequestBlocking", "proxy"),
                ["manifest_permissions"] = new JsonArray(),
                ["explicit_host"] = new JsonArray("<all_urls>"),
            },
            ["install_time"] = installTime,
            ["location"] = 4,
            ["manifest"] = manifest,
            ["path"] = extensionDir,
            ["state"] = 1,
        };
    }

    /// <summary>
    /// Получает или создаёт RSA-ключ для расширения и вычисляет идентификатор.
    /// Chromium вычисляет extension ID как SHA-256 от DER SubjectPublicKeyInfo,
    /// первые 16 байт, каждый полубайт → символ 'a'-'p'.
    /// </summary>
    private static (string PublicKeyBase64, string ExtensionId) GetOrCreateExtensionKey(string extensionDir)
    {
        var keyPath = Path.Combine(extensionDir, ".extension_key.der");
        byte[] derBytes;

        if (File.Exists(keyPath))
        {
            derBytes = File.ReadAllBytes(keyPath);
        }
        else
        {
            using var rsa = RSA.Create(2048);
            derBytes = rsa.ExportSubjectPublicKeyInfo();
            File.WriteAllBytes(keyPath, derBytes);
        }

        var publicKeyBase64 = Convert.ToBase64String(derBytes);
        var extensionId = ComputeExtensionId(derBytes);

        return (publicKeyBase64, extensionId);
    }

    /// <summary>
    /// Вычисляет Chromium extension ID из DER-кодированного публичного ключа.
    /// </summary>
    private static string ComputeExtensionId(byte[] derPublicKey)
    {
        var hash = SHA256.HashData(derPublicKey);
        var sb = new StringBuilder(32);

        for (var i = 0; i < 16; i++)
        {
            sb.Append((char)('a' + (hash[i] >> 4)));
            sb.Append((char)('a' + (hash[i] & 0x0F)));
        }

        return sb.ToString();
    }

    private static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        bridge.TabConnected -= OnTabConnected;
        bridge.TabDisconnected -= OnTabDisconnected;
        bridge.RequestIntercepted -= OnRequestIntercepted;

        foreach (var context in contexts)
            await context.DisposeAsync().ConfigureAwait(false);

        contexts.Clear();
        contextLock.Dispose();

        foreach (var window in windows.Values)
            await window.DisposeAsync().ConfigureAwait(false);

        windows.Clear();
        await bridge.DisposeAsync().ConfigureAwait(false);

        if (browserProcess is { HasExited: false })
        {
            try
            {
                browserProcess.Kill(entireProcessTree: true);
                await browserProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Процесс мог уже завершиться.
            }
            catch (TimeoutException)
            {
                // Процесс не завершился за 5 с после Kill — продолжаем очистку.
            }
            finally
            {
                browserProcess.Dispose();
            }
        }

        if (userDataDir is not null)
        {
            try { Directory.Delete(userDataDir, recursive: true); }
            catch (IOException) { /* Не критично — ОС удалит при перезагрузке. */ }
        }
    }
}
