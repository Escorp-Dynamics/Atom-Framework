using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver;
using IOPath = System.IO.Path;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Tests;

[TestFixture]
[NonParallelizable]
public class WebDriverBrowserProfileTests
{
    private static readonly string[] ExpectedArgs = ["--incognito", "--lang=de-DE"];
    private static readonly string[] ExpectedLanguages = ["de-DE", "de"];
    private static readonly string[] ChromiumSeedFiles = ["Default/Preferences", "Local State", "First Run"];
    private static readonly string[] FirefoxSeedFiles = ["user.js"];
    private const string ManifestKind = "atom.webdriver.profile-manifest";
    private const string FirefoxSystemPolicyPathOverrideKey = "Atom.WebDriver.FirefoxSystemPolicyPath";

    private static IEnumerable<TestCaseData> ChromiumProfileCases()
    {
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new ChromeProfile(path, WebBrowserChannel.Stable)), "Chrome", false, false)
            .SetName("Chrome materializes Chromium automation defaults");
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new EdgeProfile(path, WebBrowserChannel.Stable)), "Edge", true, false)
            .SetName("Edge materializes Chromium automation defaults");
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new BraveProfile(path, WebBrowserChannel.Stable)), "Brave", false, false)
            .SetName("Brave materializes Chromium automation defaults");
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new OperaProfile(path, WebBrowserChannel.Stable)), "Opera", false, false)
            .SetName("Opera materializes Chromium automation defaults");
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new VivaldiProfile(path, WebBrowserChannel.Stable)), "Vivaldi", false, true)
            .SetName("Vivaldi materializes Chromium automation defaults");
        yield return new TestCaseData(new Func<string, WebBrowserProfile>(static path => new YandexProfile(path, WebBrowserChannel.Stable)), "Yandex", false, false)
            .SetName("Yandex materializes Chromium automation defaults");
    }

    [Test]
    public void BrowserProfilesExposeExpectedSurface()
    {
        using (Assert.EnterMultipleScope())
        {
            var profileType = typeof(WebBrowserProfile);

            Assert.That(WebBrowserProfile.Chrome, Is.TypeOf<ChromeProfile>());
            Assert.That(WebBrowserProfile.Edge, Is.TypeOf<EdgeProfile>());
            Assert.That(WebBrowserProfile.Brave, Is.TypeOf<BraveProfile>());
            Assert.That(WebBrowserProfile.Opera, Is.TypeOf<OperaProfile>());
            Assert.That(WebBrowserProfile.Vivaldi, Is.TypeOf<VivaldiProfile>());
            Assert.That(WebBrowserProfile.Yandex, Is.TypeOf<YandexProfile>());
            Assert.That(WebBrowserProfile.Firefox, Is.TypeOf<FirefoxProfile>());

            Assert.That(profileType.GetProperty(nameof(WebBrowserProfile.Path)), Is.Not.Null);
            Assert.That(profileType.GetProperty(nameof(WebBrowserProfile.BinaryPath)), Is.Not.Null);
            Assert.That(profileType.GetProperty(nameof(WebBrowserProfile.Channel)), Is.Not.Null);
            Assert.That(profileType.GetProperty(nameof(WebBrowserProfile.IsInstalled)), Is.Not.Null);
            Assert.That(profileType.GetProperty("UserAgent"), Is.Null);
            Assert.That(profileType.GetProperty("Language"), Is.Null);
            Assert.That(profileType.GetProperty("Arguments"), Is.Null);
            Assert.That(profileType.GetProperty("Preferences"), Is.Null);
            Assert.That(profileType.GetProperty("UseAutomationControlledFlag"), Is.Null);
            Assert.That(profileType.GetProperty("HomePage"), Is.Null);
            Assert.That(profileType.GetMethod("EnsureInstalled", BindingFlags.Public | BindingFlags.Instance), Is.Null);
            Assert.That(profileType.GetMethod("SaveAsync", BindingFlags.Public | BindingFlags.Instance), Is.Null);
            Assert.That(profileType.GetMethod("LoadAsync", BindingFlags.Public | BindingFlags.Instance), Is.Null);
            Assert.That(profileType.GetMethod("RemoveAsync", BindingFlags.Public | BindingFlags.Instance), Is.Null);

            AssertConstructorSurface(typeof(ChromeProfile));
            AssertConstructorSurface(typeof(EdgeProfile));
            AssertConstructorSurface(typeof(BraveProfile));
            AssertConstructorSurface(typeof(OperaProfile));
            AssertConstructorSurface(typeof(VivaldiProfile));
            AssertConstructorSurface(typeof(YandexProfile));
            AssertConstructorSurface(typeof(FirefoxProfile));
        }
    }

    [Test]
    public async Task LaunchAsyncMaterializesProfileFromResolvedSettingsAndCleansItUpAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Beta);
        var device = Device.MacBookPro14;
        device.UserAgent = "Mozilla/5.0 Test Agent";
        device.Platform = "MacIntel";
        device.Locale = "de-DE";
        device.Timezone = "Europe/Berlin";
        device.Languages = ["de-DE", "de"];
        var proxy = new WebProxy("http://127.0.0.1:8181");
        device.VirtualMediaDevices = new VirtualMediaDevicesSettings
        {
            AudioInputBrowserDeviceId = "mic-1",
            VideoInputBrowserDeviceId = "cam-1",
            AudioOutputEnabled = true,
            GroupId = "devices-1",
        };

        try
        {
            await using (var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Profile = profile,
                Args = ["--incognito", "--lang=de-DE"],
                UseHeadlessMode = true,
                UseIncognitoMode = true,
                Proxy = proxy,
                Device = device,
            }))
            {
                var materializedPath = profile.Path;
                var manifestPath = IOPath.Combine(materializedPath, "profile.json");
                var preferencesPath = IOPath.Combine(materializedPath, "Default", "Preferences");
                var localStatePath = IOPath.Combine(materializedPath, "Local State");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
                using var preferencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
                using var localStateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(localStatePath));
                var bridge = document.RootElement.GetProperty("bridge");
                var managedPort = bridge.GetProperty("managedPort").GetInt32();
                var discoveryUrl = bridge.GetProperty("discoveryUrl").GetString();
                var extensionPath = bridge.GetProperty("extensionPath").GetString()
                    ?? throw new AssertionException("Bridge manifest не содержит extensionPath");
                var extensionId = bridge.GetProperty("extensionId").GetString()
                    ?? throw new AssertionException("Bridge manifest не содержит extensionId");
                var configPath = IOPath.Combine(extensionPath, "config.json");
                var managedStorageConfigPath = IOPath.Combine(extensionPath, "storage.managed.json");
                var localStorageConfigPath = IOPath.Combine(extensionPath, "storage.local.json");
                var managedPolicyPath = IOPath.Combine(profile.Path, "chromium.managed-policy.json");
                var managedPackageArtifactPath = IOPath.Combine(profile.Path, "managed-delivery", "atom-webdriver-extension.crx");
                using var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
                using var managedStorageDocument = JsonDocument.Parse(await File.ReadAllTextAsync(managedStorageConfigPath));
                using var localStorageDocument = JsonDocument.Parse(await File.ReadAllTextAsync(localStorageConfigPath));
                using var managedPolicyDocument = JsonDocument.Parse(await File.ReadAllTextAsync(managedPolicyPath));
                var managedPolicySettings = managedPolicyDocument.RootElement.GetProperty("ExtensionSettings").GetProperty(extensionId);
                var requiresCertificateBypass = bridge.GetProperty("managedDeliveryRequiresCertificateBypass").GetBoolean();
                var managedDeliveryTrust = bridge.GetProperty("managedDeliveryTrust");
                var managedPolicyDiagnostics = bridge.GetProperty("managedPolicyDiagnostics");
                var effectiveArguments = document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("effectiveArguments").EnumerateArray().Select(static item => item.GetString()).ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(string.IsNullOrWhiteSpace(materializedPath), Is.False);
                    Assert.That(Directory.Exists(materializedPath), Is.True);
                    Assert.That(document.RootElement.GetProperty("kind").GetString(), Is.EqualTo(ManifestKind));
                    Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("name").GetString(), Is.EqualTo("Chrome"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("family").GetString(), Is.EqualTo("chromium"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("binaryPath").GetString(), Is.EqualTo(binaryPath));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("channel").GetString(), Is.EqualTo(WebBrowserChannel.Beta.ToString()));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("seedFiles").EnumerateArray().Select(static item => item.GetString()).ToArray(), Is.EqualTo(ChromiumSeedFiles));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("defaultArguments").EnumerateArray().Select(static item => item.GetString()).ToArray(), Does.Not.Contain("--disable-background-networking"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("defaultArguments").EnumerateArray().Select(static item => item.GetString()).ToArray(), Does.Contain("--password-store=basic"));
                    Assert.That(effectiveArguments, Does.Contain($"--user-data-dir={materializedPath}"));
                    Assert.That(effectiveArguments, Does.Contain("--headless=new"));
                    Assert.That(effectiveArguments, Does.Contain("--incognito"));
                    Assert.That(effectiveArguments, Does.Contain("--proxy-server=http://127.0.0.1:8181"));
                    Assert.That(effectiveArguments, Does.Contain("--user-agent=Mozilla/5.0 Test Agent"));
                    Assert.That(effectiveArguments, Does.Contain($"--disable-extensions-except={extensionPath}"));
                    Assert.That(effectiveArguments, Does.Contain($"--load-extension={extensionPath}"));
                    Assert.That(effectiveArguments.Contains("--ignore-certificate-errors"), Is.EqualTo(requiresCertificateBypass));
                    Assert.That(effectiveArguments.Contains("--allow-insecure-localhost"), Is.EqualTo(requiresCertificateBypass));
                    Assert.That(effectiveArguments, Does.Contain(discoveryUrl));
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("path").GetString(), Is.EqualTo(materializedPath));
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("mode").GetString(), Is.EqualTo("temporary"));
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("cleanupOnDispose").GetBoolean(), Is.True);
                    Assert.That(document.RootElement.GetProperty("launch").GetProperty("headless").GetBoolean(), Is.True);
                    Assert.That(document.RootElement.GetProperty("launch").GetProperty("incognito").GetBoolean(), Is.True);
                    Assert.That(document.RootElement.GetProperty("launch").GetProperty("arguments").EnumerateArray().Select(static item => item.GetString()).ToArray(), Is.EqualTo(ExpectedArgs));
                    Assert.That(document.RootElement.GetProperty("launch").GetProperty("window").GetProperty("size").GetProperty("width").GetInt32(), Is.EqualTo(1280));
                    Assert.That(bridge.GetProperty("host").GetString(), Is.EqualTo("127.0.0.1"));
                    Assert.That(bridge.GetProperty("port").GetInt32(), Is.GreaterThan(0));
                    Assert.That(managedPort, Is.GreaterThan(0));
                    Assert.That(managedDeliveryTrust.GetProperty("status").GetString(), Is.EqualTo(requiresCertificateBypass ? "bypass-required" : "trusted"));
                    Assert.That(managedDeliveryTrust.GetProperty("method").GetString(), Is.Not.Empty);
                    Assert.That(bridge.GetProperty("browserFamily").GetString(), Is.EqualTo("chromium"));
                    Assert.That(bridge.GetProperty("sessionId").GetString(), Is.Not.Empty);
                    Assert.That(bridge.GetProperty("extensionVersion").GetString(), Is.Not.Empty);
                    Assert.That(extensionId, Is.Not.Empty);
                    Assert.That(bridge.GetProperty("bundledConfigPath").GetString(), Is.EqualTo(configPath));
                    Assert.That(bridge.GetProperty("managedStorageConfigPath").GetString(), Is.EqualTo(managedStorageConfigPath));
                    Assert.That(bridge.GetProperty("localStorageConfigPath").GetString(), Is.EqualTo(localStorageConfigPath));
                    Assert.That(bridge.GetProperty("managedPolicyPath").GetString(), Is.EqualTo(managedPolicyPath));
                    Assert.That(bridge.GetProperty("managedPolicyPublishPath").GetString(), Is.EqualTo(managedPolicyPath));
                    Assert.That(managedPolicyDiagnostics.GetProperty("status").GetString(), Is.EqualTo("profile-local"));
                    Assert.That(managedPolicyDiagnostics.GetProperty("method").GetString(), Is.EqualTo("profile-local"));
                    Assert.That(managedPolicyDiagnostics.GetProperty("targetPath").GetString(), Is.EqualTo(managedPolicyPath));
                    Assert.That(managedPolicyDiagnostics.GetProperty("requiresSystemPath").GetBoolean(), Is.False);
                    Assert.That(bridge.GetProperty("managedUpdateUrl").GetString(), Is.EqualTo($"https://127.0.0.1:{managedPort.ToString(CultureInfo.InvariantCulture)}/chromium/{extensionId}/manifest"));
                    Assert.That(bridge.GetProperty("managedPackageUrl").GetString(), Is.EqualTo($"https://127.0.0.1:{managedPort.ToString(CultureInfo.InvariantCulture)}/chromium/{extensionId}/extension.crx"));
                    Assert.That(bridge.GetProperty("managedPackageArtifactPath").GetString(), Is.EqualTo(managedPackageArtifactPath));
                    Assert.That(File.Exists(configPath), Is.True);
                    Assert.That(File.Exists(managedStorageConfigPath), Is.True);
                    Assert.That(File.Exists(localStorageConfigPath), Is.True);
                    Assert.That(File.Exists(managedPolicyPath), Is.True);
                    Assert.That(File.Exists(managedPackageArtifactPath), Is.True);
                    Assert.That(File.ReadAllBytes(managedPackageArtifactPath).Take(4).ToArray(), Is.EqualTo(new byte[] { (byte)'C', (byte)'r', (byte)'2', (byte)'4' }));
                    Assert.That(configDocument.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo(bridge.GetProperty("sessionId").GetString()));
                    Assert.That(configDocument.RootElement.GetProperty("secret").GetString(), Is.Not.Empty);
                    Assert.That(managedStorageDocument.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo(bridge.GetProperty("sessionId").GetString()));
                    Assert.That(localStorageDocument.RootElement.GetProperty("config").GetProperty("sessionId").GetString(), Is.EqualTo(bridge.GetProperty("sessionId").GetString()));
                    Assert.That(managedPolicyDocument.RootElement.GetProperty("ExtensionInstallForcelist")[0].GetString(), Is.EqualTo(bridge.GetProperty("managedUpdateUrl").GetString() is { } updateUrl ? $"{extensionId};{updateUrl}" : null));
                    Assert.That(managedPolicySettings.GetProperty("installation_mode").GetString(), Is.EqualTo("force_installed"));
                    Assert.That(managedPolicySettings.GetProperty("update_url").GetString(), Is.EqualTo(bridge.GetProperty("managedUpdateUrl").GetString()));
                    Assert.That(managedPolicySettings.GetProperty("install_sources")[0].GetString(), Is.EqualTo($"https://127.0.0.1:{managedPort.ToString(CultureInfo.InvariantCulture)}/*"));
                    Assert.That(managedPolicySettings.GetProperty("managed_configuration").GetProperty("sessionId").GetString(), Is.EqualTo(bridge.GetProperty("sessionId").GetString()));
                    Assert.That(preferencesDocument.RootElement.GetProperty("extensions").GetProperty("ui").GetProperty("developer_mode").GetBoolean(), Is.True);
                    Assert.That(preferencesDocument.RootElement.GetProperty("extensions").GetProperty("settings").EnumerateObject().Any(), Is.True);
                    Assert.That(preferencesDocument.RootElement.GetProperty("extensions").GetProperty("settings").TryGetProperty(extensionId, out _), Is.True);
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("identity").GetProperty("userAgent").GetString(), Is.EqualTo(device.UserAgent));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("identity").GetProperty("platform").GetString(), Is.EqualTo(device.Platform));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("identity").GetProperty("locale").GetString(), Is.EqualTo(device.Locale));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("identity").GetProperty("timezone").GetString(), Is.EqualTo(device.Timezone));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("identity").GetProperty("languages").EnumerateArray().Select(static item => item.GetString()).ToArray(), Is.EqualTo(ExpectedLanguages));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("viewport").GetProperty("deviceScaleFactor").GetDouble(), Is.EqualTo(device.DeviceScaleFactor));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("screen").GetProperty("availHeight").GetInt32(), Is.EqualTo(device.Screen!.AvailHeight));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("clientHints").GetProperty("platform").GetString(), Is.EqualTo(device.ClientHints!.Platform));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("network").GetProperty("effectiveType").GetString(), Is.EqualTo(device.NetworkInfo!.EffectiveType));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("geolocation").GetProperty("accuracy").GetDouble(), Is.EqualTo(device.Geolocation!.Accuracy));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("webGl").GetProperty("unmaskedRenderer").GetString(), Is.EqualTo(device.WebGL!.UnmaskedRenderer));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("webGlParameters").GetProperty("maxTextureSize").GetInt32(), Is.EqualTo(device.WebGLParams!.MaxTextureSize));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("speech").GetProperty("voices")[0].GetProperty("name").GetString(), Is.EqualTo(device.SpeechVoices!.First().Name));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("mediaDevices").GetProperty("groupId").GetString(), Is.EqualTo(device.VirtualMediaDevices.GroupId));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("hardware").GetProperty("hardwareConcurrency").GetInt32(), Is.EqualTo(device.HardwareConcurrency));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("hardware").GetProperty("battery").GetProperty("level").GetDouble(), Is.EqualTo(device.BatteryLevel));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("preferences").GetProperty("screenOrientation").GetString(), Is.EqualTo(device.ScreenOrientation));
                    Assert.That(document.RootElement.GetProperty("device").GetProperty("privacy").GetProperty("timerPrecisionMilliseconds").GetDouble(), Is.EqualTo(device.TimerPrecisionMilliseconds));
                    Assert.That(File.Exists(IOPath.Combine(materializedPath, "First Run")), Is.True);
                    Assert.That(preferencesDocument.RootElement.GetProperty("browser").GetProperty("check_default_browser").GetBoolean(), Is.False);
                    Assert.That(preferencesDocument.RootElement.GetProperty("dns_prefetching").GetProperty("enabled").GetBoolean(), Is.False);
                    Assert.That(preferencesDocument.RootElement.GetProperty("profile").GetProperty("password_manager_enabled").GetBoolean(), Is.False);
                    Assert.That(preferencesDocument.RootElement.GetProperty("translate").GetProperty("enabled").GetBoolean(), Is.False);
                    Assert.That(localStateDocument.RootElement.GetProperty("fre").GetProperty("has_user_seen_fre").GetBoolean(), Is.True);
                });
            }

            Assert.That(profile.Path, Is.Empty);
        }
        finally
        {
            DeleteIfExists(binaryPath);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetLaunchArgumentsAddsCertificateBypassFlagsOnlyWhenRequired(bool requiresCertificateBypass)
    {
        var profile = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var bridgeBootstrap = new BridgeBootstrapPlan(
            SessionId: "session-a",
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0",
            Strategy: ChromiumBootstrapStrategy.SystemManagedPolicy,
            Host: "127.0.0.1",
            Port: 9000,
            TransportUrl: null,
            ManagedDeliveryPort: 9443,
            ManagedDeliveryRequiresCertificateBypass: requiresCertificateBypass,
            ManagedDeliveryTrustDiagnostics: requiresCertificateBypass
                ? BridgeManagedDeliveryTrustDiagnostics.BypassRequired("test")
                : BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            Secret: "secret",
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: "/tmp/extension",
            ExtensionId: "abcdefghijklmnopabcdefghijklmnop",
            BundledConfigPath: "/tmp/extension/config.json",
            ManagedStorageConfigPath: "/tmp/extension/storage.managed.json",
            LocalStorageConfigPath: "/tmp/extension/storage.local.json",
            ManagedPolicyPath: "/tmp/profile/chromium.managed-policy.json",
            ManagedPolicyPublishPath: "/tmp/profile/chromium.managed-policy.json",
            ManagedPolicyDiagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal("/tmp/profile/chromium.managed-policy.json"),
            ManagedUpdateUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/manifest",
            ManagedPackageUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/extension.crx",
            ManagedPackageArtifactPath: "/tmp/profile/managed-delivery/atom-webdriver-extension.crx",
            DiscoveryUrl: "http://127.0.0.1:9000/",
            ConnectionTimeout: TimeSpan.FromSeconds(5));

        var arguments = BridgeExtensionBootstrap.GetLaunchArguments(profile, bridgeBootstrap);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Not.Contain("--disable-extensions-except=/tmp/extension"));
            Assert.That(arguments, Does.Not.Contain("--load-extension=/tmp/extension"));
            Assert.That(arguments.Contains("--ignore-certificate-errors"), Is.EqualTo(requiresCertificateBypass));
            Assert.That(arguments.Contains("--allow-insecure-localhost"), Is.EqualTo(requiresCertificateBypass));
        });
    }

    [Test]
    public void ShouldUseSecureTransportTargetsLinuxChromiumProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableBrave = new BraveProfile("/tmp/brave", WebBrowserChannel.Stable);
        var betaChrome = new ChromeProfile("/tmp/chrome-beta", WebBrowserChannel.Beta);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);
        var stableOpera = new OperaProfile("/tmp/opera", WebBrowserChannel.Stable);
        var stableVivaldi = new VivaldiProfile("/tmp/vivaldi", WebBrowserChannel.Stable);

        if (!OperatingSystem.IsLinux())
        {
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableChrome), Is.False);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableBrave), Is.False);
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableChrome), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableBrave), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(betaChrome), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableEdge), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableOpera), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableVivaldi), Is.True);
        });
    }

    [Test]
    public void ShouldUseSecureTransportKeepsLinuxChromeStableSecureUnderRootlessOptIn()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableChrome, useRootlessChromiumBootstrap: true), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldUseSecureTransport(stableEdge, useRootlessChromiumBootstrap: true), Is.True);
        });
    }

    [Test]
    public void ResolveChromiumBootstrapStrategyUsesSecureTransportForLinuxChromiumProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableBrave = new BraveProfile("/tmp/brave", WebBrowserChannel.Stable);
        var betaChrome = new ChromeProfile("/tmp/chrome-beta", WebBrowserChannel.Beta);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);
        var stableOpera = new OperaProfile("/tmp/opera", WebBrowserChannel.Stable);
        var stableVivaldi = new VivaldiProfile("/tmp/vivaldi", WebBrowserChannel.Stable);

        var stableChromeStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableChrome);
        var stableBraveStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableBrave);
        var betaChromeStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(betaChrome);
        var stableEdgeStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableEdge);
        var stableOperaStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableOpera);
        var stableVivaldiStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableVivaldi);

        if (!OperatingSystem.IsLinux())
        {
            Assert.Multiple(() =>
            {
                Assert.That(stableChromeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(stableChromeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.WebSocket));
                Assert.That(stableChromeStrategy.UseCommandLineExtensionLoad, Is.True);

                Assert.That(stableBraveStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(stableBraveStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.WebSocket));
                Assert.That(stableBraveStrategy.UseCommandLineExtensionLoad, Is.True);

                Assert.That(stableEdgeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(stableEdgeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.WebSocket));
                Assert.That(stableEdgeStrategy.UseCommandLineExtensionLoad, Is.True);

                Assert.That(stableOperaStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(stableOperaStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.WebSocket));
                Assert.That(stableOperaStrategy.UseCommandLineExtensionLoad, Is.True);

                Assert.That(stableVivaldiStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(stableVivaldiStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.WebSocket));
                Assert.That(stableVivaldiStrategy.UseCommandLineExtensionLoad, Is.True);
            });
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(stableChromeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.SystemManagedPolicy));
            Assert.That(stableChromeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableChromeStrategy.UseCommandLineExtensionLoad, Is.False);

            Assert.That(stableBraveStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.SystemManagedPolicy));
            Assert.That(stableBraveStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableBraveStrategy.UseCommandLineExtensionLoad, Is.False);

            Assert.That(betaChromeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
            Assert.That(betaChromeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(betaChromeStrategy.UseCommandLineExtensionLoad, Is.True);

            Assert.That(stableEdgeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.SystemManagedPolicy));
            Assert.That(stableEdgeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableEdgeStrategy.UseCommandLineExtensionLoad, Is.False);

            Assert.That(stableOperaStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.SystemManagedPolicy));
            Assert.That(stableOperaStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableOperaStrategy.UseCommandLineExtensionLoad, Is.False);

            Assert.That(stableVivaldiStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.SystemManagedPolicy));
            Assert.That(stableVivaldiStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableVivaldiStrategy.UseCommandLineExtensionLoad, Is.False);
        });
    }

    [Test]
    public void ResolveChromiumBootstrapStrategyAllowsRootlessOptInForLinuxStableChromiumManagedProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);
        var stableChromeStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableChrome, useRootlessChromiumBootstrap: true);
        var stableEdgeStrategy = BridgeExtensionBootstrap.ResolveChromiumBootstrapStrategy(stableEdge, useRootlessChromiumBootstrap: true);

        Assert.Multiple(() =>
        {
            Assert.That(stableChromeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
            Assert.That(stableChromeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableChromeStrategy.UseCommandLineExtensionLoad, Is.True);

            Assert.That(stableEdgeStrategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
            Assert.That(stableEdgeStrategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
            Assert.That(stableEdgeStrategy.UseCommandLineExtensionLoad, Is.True);
        });
    }

    [Test]
    public void BuildSecureTransportUrlUsesWssSchemeAndCarriesSecret()
    {
        var transportUrl = BridgeExtensionBootstrap.BuildSecureTransportUrl("127.0.0.1", 9444, "secret value");
        var uri = new Uri(transportUrl);

        Assert.Multiple(() =>
        {
            Assert.That(uri.Scheme, Is.EqualTo("wss"));
            Assert.That(uri.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(uri.Port, Is.EqualTo(9444));
            Assert.That(uri.Query, Is.EqualTo("?secret=secret%20value"));
        });
    }

    [Test]
    public void ResolveLinuxSystemManagedPolicyPathTargetsStableChromiumManagedProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var betaChrome = new ChromeProfile("/tmp/chrome-beta", WebBrowserChannel.Beta);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);
        var stableBrave = new BraveProfile("/tmp/brave", WebBrowserChannel.Stable);
        var stableOpera = new OperaProfile("/tmp/opera", WebBrowserChannel.Stable);
        var stableVivaldi = new VivaldiProfile("/tmp/vivaldi", WebBrowserChannel.Stable);

        if (!OperatingSystem.IsLinux())
        {
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableChrome), Is.Null);
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableChrome), Is.EqualTo("/etc/opt/chrome/policies/managed/atom-webdriver-extension.json"));
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(betaChrome), Is.Null);
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableEdge), Is.EqualTo("/etc/opt/edge/policies/managed/atom-webdriver-extension.json"));
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableBrave), Is.EqualTo("/etc/opt/BraveSoftware/Brave-Browser/policies/managed/atom-webdriver-extension.json"));
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableOpera), Is.EqualTo("/etc/opt/opera/policies/managed/atom-webdriver-extension.json"));
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableVivaldi), Is.EqualTo("/etc/opt/vivaldi/policies/managed/atom-webdriver-extension.json"));
        });
    }

    [Test]
    public void ResolveLinuxSystemManagedPolicyPathAllowsRootlessOptInForStableChromiumManagedProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableChrome, useRootlessChromiumBootstrap: true), Is.Null);
            Assert.That(BridgeExtensionBootstrap.ResolveLinuxSystemManagedPolicyPath(stableEdge, useRootlessChromiumBootstrap: true), Is.Null);
        });
    }

    [Test]
    public void ShouldSeedChromiumProfileExtensionSettingsSkipsLinuxStableChromiumManagedProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var betaChrome = new ChromeProfile("/tmp/chrome-beta", WebBrowserChannel.Beta);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);
        var stableBrave = new BraveProfile("/tmp/brave", WebBrowserChannel.Stable);
        var stableOpera = new OperaProfile("/tmp/opera", WebBrowserChannel.Stable);
        var stableVivaldi = new VivaldiProfile("/tmp/vivaldi", WebBrowserChannel.Stable);

        if (!OperatingSystem.IsLinux())
        {
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableChrome), Is.True);
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableChrome), Is.False);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(betaChrome), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableEdge), Is.False);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableBrave), Is.False);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableOpera), Is.False);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableVivaldi), Is.False);
        });
    }

    [Test]
    public void ShouldSeedChromiumProfileExtensionSettingsAllowsRootlessOptInForStableChromiumManagedProfiles()
    {
        var stableChrome = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var stableEdge = new EdgeProfile("/tmp/edge", WebBrowserChannel.Stable);

        Assert.Multiple(() =>
        {
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableChrome, useRootlessChromiumBootstrap: true), Is.True);
            Assert.That(BridgeExtensionBootstrap.ShouldSeedChromiumProfileExtensionSettings(stableEdge, useRootlessChromiumBootstrap: true), Is.True);
        });
    }

    [Test]
    public void GetLaunchArgumentsUsesBootstrapPlanStrategyForRootlessChromeStable()
    {
        var profile = new ChromeProfile("/tmp/chrome", WebBrowserChannel.Stable);
        var bridgeBootstrap = new BridgeBootstrapPlan(
            SessionId: "session-a",
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0",
            Strategy: ChromiumBootstrapStrategy.ProfileSeeded,
            Host: "127.0.0.1",
            Port: 9000,
            TransportUrl: null,
            ManagedDeliveryPort: 9443,
            ManagedDeliveryRequiresCertificateBypass: false,
            ManagedDeliveryTrustDiagnostics: BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            Secret: "secret",
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: "/tmp/extension",
            ExtensionId: "abcdefghijklmnopabcdefghijklmnop",
            BundledConfigPath: "/tmp/extension/config.json",
            ManagedStorageConfigPath: "/tmp/extension/storage.managed.json",
            LocalStorageConfigPath: "/tmp/extension/storage.local.json",
            ManagedPolicyPath: "/tmp/profile/chromium.managed-policy.json",
            ManagedPolicyPublishPath: "/tmp/profile/chromium.managed-policy.json",
            ManagedPolicyDiagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal("/tmp/profile/chromium.managed-policy.json"),
            ManagedUpdateUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/manifest",
            ManagedPackageUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/extension.crx",
            ManagedPackageArtifactPath: "/tmp/profile/managed-delivery/atom-webdriver-extension.crx",
            DiscoveryUrl: "http://127.0.0.1:9000/",
            ConnectionTimeout: TimeSpan.FromSeconds(5));

        var arguments = BridgeExtensionBootstrap.GetLaunchArguments(profile, bridgeBootstrap);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain("--disable-extensions-except=/tmp/extension"));
            Assert.That(arguments, Does.Contain("--load-extension=/tmp/extension"));
            Assert.That(arguments, Does.Contain("http://127.0.0.1:9000/"));
            Assert.That(arguments, Does.Not.Contain("--ignore-certificate-errors"));
        });
    }

    [Test]
    public void GetLaunchArgumentsKeepsFirefoxDiscoveryUrlWithoutChromiumExtensionSwitches()
    {
        var profile = new FirefoxProfile("/tmp/firefox", WebBrowserChannel.Stable);
        var bridgeBootstrap = new BridgeBootstrapPlan(
            SessionId: "session-firefox",
            BrowserFamily: "firefox",
            ExtensionVersion: "1.0.0",
            Strategy: ChromiumBootstrapStrategy.FirefoxProfileSeeded,
            Host: "127.0.0.1",
            Port: 9000,
            TransportUrl: null,
            ManagedDeliveryPort: 9000,
            ManagedDeliveryRequiresCertificateBypass: false,
            ManagedDeliveryTrustDiagnostics: BridgeManagedDeliveryTrustDiagnostics.Trusted("firefox-profile-extension"),
            Secret: "secret",
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: "/tmp/firefox-profile/extensions/escorp-automation@escorp.local",
            ExtensionId: "escorp-automation@escorp.local",
            BundledConfigPath: "/tmp/firefox-profile/extensions/escorp-automation@escorp.local/config.json",
            ManagedStorageConfigPath: "/tmp/firefox-profile/extensions/escorp-automation@escorp.local/storage.managed.json",
            LocalStorageConfigPath: "/tmp/firefox-profile/extensions/escorp-automation@escorp.local/storage.local.json",
            ManagedPolicyPath: string.Empty,
            ManagedPolicyPublishPath: string.Empty,
            ManagedPolicyDiagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal("/tmp/firefox-profile/extensions/escorp-automation@escorp.local", "firefox-profile-extension"),
            ManagedUpdateUrl: string.Empty,
            ManagedPackageUrl: string.Empty,
            ManagedPackageArtifactPath: string.Empty,
            DiscoveryUrl: "http://127.0.0.1:9000/",
            ConnectionTimeout: TimeSpan.FromSeconds(5));

        var arguments = BridgeExtensionBootstrap.GetLaunchArguments(profile, bridgeBootstrap);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Not.Contain("--disable-extensions-except=/tmp/firefox-profile/extensions/escorp-automation@escorp.local"));
            Assert.That(arguments, Does.Not.Contain("--load-extension=/tmp/firefox-profile/extensions/escorp-automation@escorp.local"));
            Assert.That(arguments, Does.Contain("http://127.0.0.1:9000/"));
        });
    }

    [Test]
    public void BindBridgeBootstrapPortPreservesRootlessChromiumBootstrapOptIn()
    {
        var method = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
            "BindBridgeBootstrapPort",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);

        var preparation = new BridgeBootstrapPreparation(
            Settings: new BridgeSettings
            {
                Host = "127.0.0.1",
                Secret = "secret",
                UseRootlessChromiumBootstrap = true,
            },
            SourceExtensionPath: "/tmp/extension",
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0");

        var boundPreparation = method!.Invoke(null,
        [
            preparation,
            9000,
            9444,
            9445,
            false,
            BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
        ]) as BridgeBootstrapPreparation;

        Assert.That(boundPreparation, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(boundPreparation!.Settings.UseRootlessChromiumBootstrap, Is.True);
            Assert.That(boundPreparation.Settings.Port, Is.EqualTo(9000));
            Assert.That(boundPreparation.Settings.SecureTransportPort, Is.EqualTo(9444));
            Assert.That(boundPreparation.Settings.ManagedDeliveryPort, Is.EqualTo(9445));
        });
    }

    [Test]
    public async Task ProfileMaterializationKeepsRootlessChromeStableProfileLocalAfterBridgeBindingAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
                UseRootlessChromiumBootstrap = true,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(materialization.BridgeBootstrap!.Strategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(materialization.BridgeBootstrap.ManagedPolicyDiagnostics.Status, Is.EqualTo("profile-local"));
                Assert.That(materialization.BridgeBootstrap.ManagedPolicyPublishPath, Is.EqualTo(IOPath.Combine(profilePath, "chromium.managed-policy.json")));
                Assert.That(materialization.BridgeBootstrap.ManagedPolicyDiagnostics.RequiresSystemPath, Is.False);
            });
        }
        finally
        {
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [TestCase("edge")]
    [TestCase("brave")]
    [TestCase("opera")]
    [TestCase("vivaldi")]
    public async Task ProfileMaterializationUsesSecureTransportForRootlessLinuxChromiumProfileAsync(string browserKind)
    {
        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var profile = CreateStableChromiumProfile(browserKind, binaryPath);
        profile.Path = profilePath;

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
                UseRootlessChromiumBootstrap = true,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);
            Assert.That(Uri.TryCreate(materialization.BridgeBootstrap!.TransportUrl, UriKind.Absolute, out var transportUri), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(materialization.BridgeBootstrap.Strategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.ProfileSeeded));
                Assert.That(materialization.BridgeBootstrap.Strategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
                Assert.That(materialization.BridgeBootstrap.ManagedPolicyDiagnostics.Status, Is.EqualTo("profile-local"));
                Assert.That(transportUri!.Scheme, Is.EqualTo("wss"));
            });
        }
        finally
        {
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    public async Task ProfileMaterializationInstallsFirefoxExtensionAndWritesBridgeSectionAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", null);

            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            var bootstrap = materialization.BridgeBootstrap!;
            var manifestPath = IOPath.Combine(profilePath, "profile.json");
            var addonManifestPath = IOPath.Combine(bootstrap.LocalExtensionPath, "manifest.json");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            using var addonManifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(addonManifestPath));
            var managedPolicyDiagnostics = manifestDocument.RootElement.GetProperty("bridge").GetProperty("managedPolicyDiagnostics");

            Assert.Multiple(() =>
            {
                Assert.That(bootstrap.BrowserFamily, Is.EqualTo("firefox"));
                Assert.That(bootstrap.LocalExtensionPath, Is.EqualTo(IOPath.Combine(profilePath, "extensions", "escorp-automation@escorp.local")));
                Assert.That(File.Exists(bootstrap.BundledConfigPath), Is.True);
                Assert.That(File.Exists(bootstrap.ManagedStorageConfigPath), Is.True);
                Assert.That(File.Exists(bootstrap.LocalStorageConfigPath), Is.True);
                Assert.That(manifestDocument.RootElement.GetProperty("bridge").GetProperty("sessionId").GetString(), Is.Not.Empty);
                Assert.That(manifestDocument.RootElement.GetProperty("bridge").GetProperty("extensionPath").GetString(), Is.EqualTo(bootstrap.LocalExtensionPath));
                Assert.That(managedPolicyDiagnostics.GetProperty("status").GetString(), Is.EqualTo("profile-local"));
                Assert.That(addonManifestDocument.RootElement.GetProperty("browser_specific_settings").GetProperty("gecko").GetProperty("id").GetString(), Is.EqualTo("escorp-automation@escorp.local"));
                Assert.That(addonManifestDocument.RootElement.GetProperty("background").TryGetProperty("persistent", out _), Is.False);
                Assert.That(addonManifestDocument.RootElement.GetProperty("permissions").EnumerateArray().Select(static item => item.GetString()).ToArray(), Does.Contain("storage"));
                Assert.That(bootstrap.Strategy.TransportMode, Is.EqualTo(ChromiumBootstrapTransportMode.SecureWebSocket));
                Assert.That(Uri.TryCreate(bootstrap.TransportUrl, UriKind.Absolute, out var transportUri), Is.True);
                Assert.That(transportUri!.Scheme, Is.EqualTo("wss"));
            });

            if (OperatingSystem.IsLinux())
            {
                Assert.That(managedPolicyDiagnostics.GetProperty("detail").GetString(), Does.Contain("Firefox Developer Edition"));
                Assert.That(managedPolicyDiagnostics.GetProperty("detail").GetString(), Does.Contain("Nightly"));
                Assert.That(managedPolicyDiagnostics.GetProperty("detail").GetString(), Does.Contain("Marionette"));
                Assert.That(managedPolicyDiagnostics.GetProperty("detail").GetString(), Does.Contain("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH"));
            }
            else
            {
                Assert.That(managedPolicyDiagnostics.GetProperty("detail").GetString(), Is.EqualTo("firefox-profile-extension"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    public async Task ProfileMaterializationKeepsFirefoxDeveloperBootstrapDiagnosticsNeutralAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Dev)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            var manifestPath = IOPath.Combine(profilePath, "profile.json");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var detail = manifestDocument.RootElement
                .GetProperty("bridge")
                .GetProperty("managedPolicyDiagnostics")
                .GetProperty("detail")
                .GetString();

            Assert.That(detail, Is.EqualTo("firefox-profile-extension"));
        }
        finally
        {
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ProfileMaterializationUsesFirefoxInstallOverlayWhenSignedXpiConfiguredAsync()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox install overlay path");

        var workspaceDirectory = CreateTemporaryDirectory();
        var installDirectory = IOPath.Combine(workspaceDirectory, "firefox-install");
        var launcherDirectory = IOPath.Combine(workspaceDirectory, "launcher");
        var profilePath = CreateTemporaryDirectory();
        var argumentsPath = IOPath.Combine(workspaceDirectory, "overlay-args.txt");
        var pidPath = IOPath.Combine(workspaceDirectory, "overlay-pid.txt");
        var actualBinaryPath = CreateExecutableBrowserHarness(installDirectory, argumentsPath, pidPath);
        var wrappedBinaryPath = CreateFirefoxWrapperScript(launcherDirectory, actualBinaryPath);
        var sourcePolicyDirectory = IOPath.Combine(installDirectory, "distribution");
        var sourcePolicyPath = IOPath.Combine(sourcePolicyDirectory, "policies.json");
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");

        Directory.CreateDirectory(sourcePolicyDirectory);
        await File.WriteAllTextAsync(sourcePolicyPath, "{\"policies\":{\"DisableTelemetry\":true}}");

        var profile = new FirefoxProfile(wrappedBinaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            var bootstrap = materialization.BridgeBootstrap!;
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(IOPath.Combine(profilePath, "profile.json")));
            using var policyDocument = JsonDocument.Parse(await File.ReadAllTextAsync(bootstrap.ManagedPolicyPublishPath));

            Assert.Multiple(() =>
            {
                Assert.That(bootstrap.Strategy.InstallMode, Is.EqualTo(ChromiumBootstrapInstallMode.BrowserInstallationManagedPolicy));
                Assert.That(bootstrap.ManagedPolicyDiagnostics.Status, Is.EqualTo("browser-installation-published"));
                Assert.That(bootstrap.ManagedPolicyDiagnostics.Detail, Does.Contain($"signed-package-path={signedPackagePath}"));
                Assert.That(bootstrap.ManagedPackageArtifactPath, Is.EqualTo(signedPackagePath));
                Assert.That(bootstrap.LocalExtensionPath, Is.EqualTo(signedPackagePath));
                Assert.That(bootstrap.LaunchBinaryPath, Does.Contain(".bridge-firefox-installation"));
                Assert.That(File.Exists(bootstrap.LaunchBinaryPath), Is.True);
                Assert.That(manifestDocument.RootElement.GetProperty("bridge").GetProperty("launchBinaryPath").GetString(), Is.EqualTo(bootstrap.LaunchBinaryPath));
                Assert.That(policyDocument.RootElement.GetProperty("policies").GetProperty("DisableTelemetry").GetBoolean(), Is.True);
                Assert.That(policyDocument.RootElement.GetProperty("policies").GetProperty("ExtensionSettings").GetProperty("escorp-automation@escorp.local").GetProperty("installation_mode").GetString(), Is.EqualTo("force_installed"));
                Assert.That(policyDocument.RootElement.GetProperty("policies").GetProperty("ExtensionSettings").GetProperty("escorp-automation@escorp.local").GetProperty("install_url").GetString(), Is.EqualTo(new Uri(signedPackagePath).AbsoluteUri));
                Assert.That(policyDocument.RootElement.GetProperty("policies").GetProperty("3rdparty").GetProperty("Extensions").GetProperty("escorp-automation@escorp.local").GetProperty("host").GetString(), Is.EqualTo("127.0.0.1"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteDirectoryIfExists(workspaceDirectory);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenManifestVersionDoesNotMatchCurrentExtensionVersion()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI manifest validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                "0.0.0",
                "escorp-automation@escorp.local",
                preparation!.SourceExtensionPath);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("имеет версию '0.0.0'"));
                Assert.That(exception.Message, Does.Contain($"ожидалась '{preparation.ExtensionVersion}'"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenStoragePermissionIsMissing()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI manifest validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath,
                includeStoragePermission: false);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("не содержит permission 'storage'"));
                Assert.That(exception.Message, Does.Contain("managed policy bootstrap"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenRuntimePayloadDoesNotMatchCurrentLayout()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI payload validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath,
                backgroundRuntimeContent: "stale-background-runtime");

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            AssertFirefoxSignedXpiStalePayloadDiagnostic(exception!, "background.runtime.js");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenContentScriptDoesNotMatchCurrentLayout()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI payload validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath,
                contentScriptContent: "stale-content-runtime");

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            AssertFirefoxSignedXpiStalePayloadDiagnostic(exception!, "content.js");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenBackgroundRuntimeEntryIsMissing()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI payload validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath,
                includeBackgroundRuntime: false);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            AssertFirefoxSignedXpiMissingRuntimeEntryDiagnostic(exception!, "background.runtime.js");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void ProfileMaterializationRejectsFirefoxSignedXpiWhenRequiredRuntimeEntryIsMissing()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox signed XPI payload validation");

        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var workspaceDirectory = CreateTemporaryDirectory();
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath,
                includeContentScript: false);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ProfileMaterialization.MaterializeAsync(
                    launchSettings,
                    boundPreparation,
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            AssertFirefoxSignedXpiMissingRuntimeEntryDiagnostic(exception!, "content.js");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
            DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ProfileMaterializationReportsFirefoxSystemPolicyShadowingWhenGlobalEntryConflictsAsync()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест проверяет Linux Firefox system policy shadowing diagnostics");

        var workspaceDirectory = CreateTemporaryDirectory();
        var installDirectory = IOPath.Combine(workspaceDirectory, "firefox-install");
        var launcherDirectory = IOPath.Combine(workspaceDirectory, "launcher");
        var profilePath = CreateTemporaryDirectory();
        var argumentsPath = IOPath.Combine(workspaceDirectory, "overlay-args.txt");
        var pidPath = IOPath.Combine(workspaceDirectory, "overlay-pid.txt");
        var actualBinaryPath = CreateExecutableBrowserHarness(installDirectory, argumentsPath, pidPath);
        var wrappedBinaryPath = CreateFirefoxWrapperScript(launcherDirectory, actualBinaryPath);
        var systemPolicyDirectory = IOPath.Combine(workspaceDirectory, "system-firefox-policy", "policies");
        var systemPolicyPath = IOPath.Combine(systemPolicyDirectory, "policies.json");
        var originalSignedPackagePath = Environment.GetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH");
        var originalSystemPolicyPath = AppContext.GetData(FirefoxSystemPolicyPathOverrideKey);

        Directory.CreateDirectory(systemPolicyDirectory);
        await File.WriteAllTextAsync(
            systemPolicyPath,
            """
            {"policies":{"ExtensionSettings":{"escorp-automation@escorp.local":{"installation_mode":"force_installed","install_url":"https://addons.mozilla.org/firefox/downloads/file/stale-policy.xpi"}}}}
            """);

        var profile = new FirefoxProfile(wrappedBinaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            AppContext.SetData(FirefoxSystemPolicyPathOverrideKey, systemPolicyPath);

            var launchSettings = new WebBrowserSettings
            {
                Profile = profile,
            };
            var preparation = BridgeExtensionBootstrap.TryCreatePreparation(launchSettings);
            var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
                "BindBridgeBootstrapPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(preparation, Is.Not.Null);
            Assert.That(bindMethod, Is.Not.Null);

            var signedPackagePath = CreateFirefoxSignedXpiPackage(
                workspaceDirectory,
                preparation!.ExtensionVersion,
                "escorp-automation@escorp.local",
                preparation.SourceExtensionPath);

            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", signedPackagePath);

            var boundPreparation = bindMethod!.Invoke(null,
            [
                preparation,
                9000,
                9444,
                9445,
                false,
                BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            ]) as BridgeBootstrapPreparation;

            Assert.That(boundPreparation, Is.Not.Null);

            var materialization = await ProfileMaterialization.MaterializeAsync(
                launchSettings,
                boundPreparation,
                CancellationToken.None);

            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            var detail = materialization.BridgeBootstrap!.ManagedPolicyDiagnostics.Detail;

            Assert.Multiple(() =>
            {
                Assert.That(materialization.BridgeBootstrap.ManagedPolicyDiagnostics.Status, Is.EqualTo("browser-installation-published"));
                Assert.That(detail, Does.Contain($"signed-package-path={signedPackagePath}"));
                Assert.That(detail, Does.Contain("firefox-system-policy-shadowing"));
                Assert.That(detail, Does.Contain(systemPolicyPath));
                Assert.That(detail, Does.Contain("conflicts-with-overlay-url"));
                Assert.That(detail, Does.Contain("stale-policy.xpi"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH", originalSignedPackagePath);
            AppContext.SetData(FirefoxSystemPolicyPathOverrideKey, originalSystemPolicyPath);
            DeleteDirectoryIfExists(workspaceDirectory);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    public void MergeManagedPolicyDiagnosticsPromotesLegacyAliasFailure()
    {
        var primaryDiagnostics = BridgeManagedPolicyPublishDiagnostics.SystemPublished(
            "linux-chrome-system-policy-sudo",
            "/etc/opt/chrome/policies/managed/atom-webdriver-extension.json");
        var legacyDiagnostics = BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
            "linux-chrome-legacy-system-policy-sudo",
            "/etc/opt/chrome/policies/managed/escorp-browser.json",
            "sudo: a password is required");

        var diagnostics = BridgeExtensionBootstrap.MergeManagedPolicyDiagnostics(primaryDiagnostics, legacyDiagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Status, Is.EqualTo("system-publish-required"));
            Assert.That(diagnostics.Method, Is.EqualTo("linux-chrome-legacy-system-policy-sudo"));
            Assert.That(diagnostics.TargetPath, Is.EqualTo("/etc/opt/chrome/policies/managed/escorp-browser.json"));
            Assert.That(diagnostics.RequiresSystemPath, Is.True);
            Assert.That(diagnostics.Detail, Does.Contain("Основной managed policy опубликован"));
            Assert.That(diagnostics.Detail, Does.Contain("atom-webdriver-extension.json"));
            Assert.That(diagnostics.Detail, Does.Contain("sudo: a password is required"));
        });
    }

    [Test]
    public void MergeManagedPolicyDiagnosticsKeepsPrimaryFailure()
    {
        var primaryDiagnostics = BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
            "linux-chrome-system-policy-sudo",
            "/etc/opt/chrome/policies/managed/atom-webdriver-extension.json",
            "sudo: a password is required");
        var legacyDiagnostics = BridgeManagedPolicyPublishDiagnostics.SystemPublished(
            "linux-chrome-legacy-system-policy-sudo",
            "/etc/opt/chrome/policies/managed/escorp-browser.json");

        var diagnostics = BridgeExtensionBootstrap.MergeManagedPolicyDiagnostics(primaryDiagnostics, legacyDiagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Status, Is.EqualTo(primaryDiagnostics.Status));
            Assert.That(diagnostics.Method, Is.EqualTo(primaryDiagnostics.Method));
            Assert.That(diagnostics.TargetPath, Is.EqualTo(primaryDiagnostics.TargetPath));
            Assert.That(diagnostics.Detail, Is.EqualTo(primaryDiagnostics.Detail));
        });
    }

    [Test]
    public void LaunchAsyncThrowsWhenProfileBinaryIsMissing()
    {
        var binaryPath = IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Stable);

        Assert.That(async () => await WebBrowser.LaunchAsync(new WebBrowserSettings { Profile = profile }),
            Throws.TypeOf<FileNotFoundException>());
    }

    [TestCaseSource(nameof(ChromiumProfileCases))]
    public async Task ChromiumProfilesMaterializeExpectedAutomationSeedFiles(
        Func<string, WebBrowserProfile> profileFactory,
        string browserName,
        bool expectEdgeFreDisable,
        bool expectVivaldiPreferences)
    {
        var binaryPath = CreateTemporaryFile();
        var profile = profileFactory(binaryPath);

        try
        {
            await using (var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Profile = profile,
            }))
            {
                var manifestPath = IOPath.Combine(profile.Path, "profile.json");
                var preferencesPath = IOPath.Combine(profile.Path, "Default", "Preferences");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
                using var preferencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
                var effectiveArguments = document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("effectiveArguments").EnumerateArray().Select(static item => item.GetString()).ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("name").GetString(), Is.EqualTo(browserName));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("family").GetString(), Is.EqualTo("chromium"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("seedFiles").EnumerateArray().Select(static item => item.GetString()).ToArray(), Is.EqualTo(ChromiumSeedFiles));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferenceFile").GetString(), Is.EqualTo("Default/Preferences"));
                    Assert.That(File.Exists(IOPath.Combine(profile.Path, "Local State")), Is.True);
                    Assert.That(File.Exists(IOPath.Combine(profile.Path, "First Run")), Is.True);
                    Assert.That(preferencesDocument.RootElement.GetProperty("browser").GetProperty("has_seen_welcome_page").GetBoolean(), Is.True);
                    Assert.That(preferencesDocument.RootElement.GetProperty("background_mode").GetProperty("enabled").GetBoolean(), Is.False);
                });

                if (expectEdgeFreDisable)
                {
                    Assert.That(effectiveArguments.Any(static argument => argument is not null
                        && argument.StartsWith("--disable-features=", StringComparison.Ordinal)
                        && argument.Contains("msEdgeFRE", StringComparison.Ordinal)), Is.True);
                }

                if (expectVivaldiPreferences)
                {
                    Assert.That(preferencesDocument.RootElement.TryGetProperty("vivaldi", out _), Is.True);
                }
            }
        }
        finally
        {
            DeleteIfExists(binaryPath);
        }
    }

    [Test]
    public async Task LaunchAsyncPreservesExplicitProfilePathAndMarksPersistentMaterializationAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profilePath = CreateTemporaryDirectory();
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Stable)
        {
            Path = profilePath,
        };

        try
        {
            await using (var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Profile = profile,
            }))
            {
                var manifestPath = IOPath.Combine(profilePath, "profile.json");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));

                Assert.Multiple(() =>
                {
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("path").GetString(), Is.EqualTo(profilePath));
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("mode").GetString(), Is.EqualTo("persistent"));
                    Assert.That(document.RootElement.GetProperty("profile").GetProperty("cleanupOnDispose").GetBoolean(), Is.False);
                });
            }

            Assert.Multiple(() =>
            {
                Assert.That(profile.Path, Is.EqualTo(profilePath));
                Assert.That(Directory.Exists(profilePath), Is.True);
            });
        }
        finally
        {
            DeleteIfExists(binaryPath);
            DeleteDirectoryIfExists(profilePath);
        }
    }

    [Test]
    public async Task LaunchAsyncMaterializesFirefoxAutomationPrefsAsync()
    {
        var binaryPath = CreateTemporaryFile();
        var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Stable);
        var device = Device.DesktopFullHd;
        device.UserAgent = "Firefox Test Agent";
        device.Locale = "fr-FR";
        device.Languages = ["fr-FR", "fr"];
        var proxy = new WebProxy("socks5://127.0.0.1:9050");

        try
        {
            await using (var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
            {
                Profile = profile,
                UseHeadlessMode = true,
                UseIncognitoMode = true,
                Proxy = proxy,
                Device = device,
            }))
            {
                var manifestPath = IOPath.Combine(profile.Path, "profile.json");
                var userJsPath = IOPath.Combine(profile.Path, "user.js");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
                var userJs = await File.ReadAllTextAsync(userJsPath);
                var effectiveArguments = document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("effectiveArguments").EnumerateArray().Select(static item => item.GetString()).ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("name").GetString(), Is.EqualTo("Firefox"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("family").GetString(), Is.EqualTo("firefox"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("seedFiles").EnumerateArray().Select(static item => item.GetString()).ToArray(), Is.EqualTo(FirefoxSeedFiles));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferenceFile").GetString(), Is.EqualTo("user.js"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferences").GetProperty("intl.accept_languages").GetString(), Is.EqualTo("fr-FR,fr"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferences").GetProperty("general.useragent.override").GetString(), Is.EqualTo("Firefox Test Agent"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferences").GetProperty("browser.privatebrowsing.autostart").GetBoolean(), Is.True);
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferences").GetProperty("network.proxy.socks").GetString(), Is.EqualTo("127.0.0.1"));
                    Assert.That(document.RootElement.GetProperty("browser").GetProperty("automation").GetProperty("preferences").GetProperty("network.proxy.socks_port").GetInt32(), Is.EqualTo(9050));
                    Assert.That(effectiveArguments, Does.Contain("-profile"));
                    Assert.That(effectiveArguments, Does.Contain(profile.Path));
                    Assert.That(effectiveArguments, Does.Contain("-headless"));
                    Assert.That(userJs, Does.Contain("user_pref(\"browser.newtabpage.enabled\", false);"));
                    Assert.That(userJs, Does.Contain("user_pref(\"toolkit.telemetry.enabled\", false);"));
                    Assert.That(userJs, Does.Contain("user_pref(\"intl.accept_languages\", \"fr-FR,fr\");"));
                    Assert.That(userJs, Does.Contain("user_pref(\"general.useragent.override\", \"Firefox Test Agent\");"));
                    Assert.That(userJs, Does.Contain("user_pref(\"browser.privatebrowsing.autostart\", true);"));
                    Assert.That(userJs, Does.Contain("user_pref(\"network.proxy.socks\", \"127.0.0.1\");"));
                });
            }
        }
        finally
        {
            DeleteIfExists(binaryPath);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public async Task LaunchAsyncStartsLaunchableBrowserProcessWithMaterializedArgumentsAsync()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Ignore("Тест использует POSIX executable harness.");

        var directory = CreateTemporaryDirectory();
        var argumentsPath = IOPath.Combine(directory, "args.txt");
        var pidPath = IOPath.Combine(directory, "pid.txt");
        var binaryPath = CreateExecutableBrowserHarness(directory, argumentsPath, pidPath);
        var profile = new ChromeProfile(binaryPath, WebBrowserChannel.Stable);
        var device = Device.MacBookPro14;
        device.UserAgent = "Mozilla/5.0 Harness Agent";
        device.Locale = "de-DE";
        device.Languages = ["de-DE", "de"];
        var proxy = new WebProxy("http://127.0.0.1:8181");
        var pid = 0;
        Process? process = null;
        var launchSettings = new WebBrowserSettings
        {
            Profile = profile,
            UseHeadlessMode = true,
            UseIncognitoMode = true,
            Proxy = proxy,
            Args = ["--custom-flag"],
            Device = device,
        };

        try
        {
            var materialization = await MaterializeLaunchArtifactsAsync(launchSettings).ConfigureAwait(false);
            Assert.That(materialization.BridgeBootstrap, Is.Not.Null);

            process = LaunchBrowserProcessForTests(launchSettings, materialization.BridgeBootstrap);

            var manifestPath = IOPath.Combine(profile.Path, "profile.json");
            await WaitForConditionAsync(
                static state => File.Exists(state.argumentsPath)
                    && new FileInfo(state.argumentsPath).Length > 0
                    && File.Exists(state.pidPath)
                    && new FileInfo(state.pidPath).Length > 0,
                (argumentsPath, pidPath),
                TimeSpan.FromSeconds(5));

            var launchedArguments = (await File.ReadAllLinesAsync(argumentsPath))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var discoveryUrl = manifestDocument.RootElement.GetProperty("bridge").GetProperty("discoveryUrl").GetString();
            var extensionPath = manifestDocument.RootElement.GetProperty("bridge").GetProperty("extensionPath").GetString();
            var useCommandLineExtensionLoad = materialization.BridgeBootstrap!.Strategy.UseCommandLineExtensionLoad;

            pid = int.Parse((await File.ReadAllTextAsync(pidPath)).Trim(), CultureInfo.InvariantCulture);

            using var launchedProcess = Process.GetProcessById(pid);

            Assert.Multiple(() =>
            {
                Assert.That(launchedProcess.HasExited, Is.False);
                Assert.That(launchedArguments, Does.Contain($"--user-data-dir={profile.Path}"));
                Assert.That(launchedArguments, Does.Contain("--headless=new"));
                Assert.That(launchedArguments, Does.Contain("--incognito"));
                Assert.That(launchedArguments, Does.Contain("--proxy-server=http://127.0.0.1:8181"));
                Assert.That(launchedArguments, Does.Contain("--lang=de-DE"));
                Assert.That(launchedArguments, Does.Contain("--user-agent=Mozilla/5.0 Harness Agent"));
                Assert.That(launchedArguments, Does.Contain("--custom-flag"));
                Assert.That(launchedArguments, Does.Contain(discoveryUrl));

                if (useCommandLineExtensionLoad)
                {
                    Assert.That(launchedArguments, Does.Contain($"--disable-extensions-except={extensionPath}"));
                    Assert.That(launchedArguments, Does.Contain($"--load-extension={extensionPath}"));
                }
                else
                {
                    Assert.That(launchedArguments, Does.Not.Contain($"--disable-extensions-except={extensionPath}"));
                    Assert.That(launchedArguments, Does.Not.Contain($"--load-extension={extensionPath}"));
                }
            });
        }
        finally
        {
            StopProcess(process);
            if (!string.IsNullOrWhiteSpace(profile.Path))
                DeleteDirectoryIfExists(profile.Path);

            if (pid > 0)
            {
                await WaitForConditionAsync(static processId => !IsProcessAlive(processId), pid, TimeSpan.FromSeconds(5));
                Assert.That(IsProcessAlive(pid), Is.False);
            }

            DeleteDirectoryIfExists(directory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public async Task LaunchAsyncAppliesGlobalBrowserAndHeadlessEnvironmentOverridesAsync()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Ignore("Тест использует POSIX executable harness.");

        var directory = CreateTemporaryDirectory();
        var argumentsPath = IOPath.Combine(directory, "env-args.txt");
        var pidPath = IOPath.Combine(directory, "env-pid.txt");
        var binaryPath = CreateExecutableBrowserHarness(directory, argumentsPath, pidPath);
        var originalBrowser = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER");
        var originalBrowserPath = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH");
        var originalHeadless = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_HEADLESS");
        var pid = 0;
        Process? process = null;
        WebBrowserSettings? resolvedSettings = null;

        try
        {
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", "chrome");
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", binaryPath);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_HEADLESS", "true");

            resolvedSettings = ApplyLaunchOverridesForTests(new WebBrowserSettings
            {
                Args = ["--env-flag"],
            });
            var materialization = await MaterializeLaunchArtifactsAsync(resolvedSettings).ConfigureAwait(false);
            process = LaunchBrowserProcessForTests(resolvedSettings, materialization.BridgeBootstrap);

            await WaitForConditionAsync(
                static state => File.Exists(state.argumentsPath)
                    && new FileInfo(state.argumentsPath).Length > 0
                    && File.Exists(state.pidPath)
                    && new FileInfo(state.pidPath).Length > 0,
                (argumentsPath, pidPath),
                TimeSpan.FromSeconds(5));

            var launchedArguments = (await File.ReadAllLinesAsync(argumentsPath))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            pid = int.Parse((await File.ReadAllTextAsync(pidPath)).Trim(), CultureInfo.InvariantCulture);

            using var launchedProcess = Process.GetProcessById(pid);

            Assert.Multiple(() =>
            {
                Assert.That(resolvedSettings.Profile, Is.TypeOf<ChromeProfile>());
                Assert.That(resolvedSettings.Profile!.BinaryPath, Is.EqualTo(binaryPath));
                Assert.That(resolvedSettings.UseHeadlessMode, Is.True);
                Assert.That(launchedProcess.HasExited, Is.False);
                Assert.That(launchedArguments, Does.Contain("--headless=new"));
                Assert.That(launchedArguments, Does.Contain("--env-flag"));
            });
        }
        finally
        {
            StopProcess(process);
            if (resolvedSettings?.Profile is { Path.Length: > 0 } resolvedProfile)
                DeleteDirectoryIfExists(resolvedProfile.Path);

            if (pid > 0)
            {
                await WaitForConditionAsync(static processId => !IsProcessAlive(processId), pid, TimeSpan.FromSeconds(5));
                Assert.That(IsProcessAlive(pid), Is.False);
            }

            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", originalBrowser);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", originalBrowserPath);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_HEADLESS", originalHeadless);
            DeleteDirectoryIfExists(directory);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public void LaunchAsyncThrowsWhenGlobalBrowserOverrideBinaryIsNotExecutable()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Ignore("Тест рассчитан на POSIX-проверку executable bit.");

        var binaryPath = CreateTemporaryFile();
        var originalBrowser = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER");
        var originalBrowserPath = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH");

        try
        {
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", "chrome");
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", binaryPath);

            Assert.That(async () => await WebBrowser.LaunchAsync(new WebBrowserSettings()),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER", originalBrowser);
            Environment.SetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER_PATH", originalBrowserPath);
            DeleteIfExists(binaryPath);
        }
    }

    [Test]
    public void GlobalFirefoxBrowserOverrideInfersDeveloperEditionChannelFromBinaryPath()
    {
        var method = typeof(WebBrowser).GetMethod("CreateProfile", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Не удалось найти WebDriverTestEnvironment.CreateProfile через reflection");

        var profile = method.Invoke(obj: null, parameters: ["firefox", "/usr/bin/firefox-developer-edition"]) as WebBrowserProfile;

        Assert.That(profile, Is.TypeOf<FirefoxProfile>());
        Assert.That(profile!.Channel, Is.EqualTo(WebBrowserChannel.Dev));
        Assert.That(profile.BinaryPath, Is.EqualTo("/usr/bin/firefox-developer-edition"));
    }

    [Test]
    public void BinaryPathSetterRefreshesInstallationState()
    {
        var existingBinary = CreateTemporaryFile();
        var missingBinary = IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var profile = new ChromeProfile(missingBinary);
            Assert.That(profile.IsInstalled, Is.False);

            profile.BinaryPath = existingBinary;
            Assert.That(profile.IsInstalled, Is.True);

            profile.BinaryPath = missingBinary;
            Assert.That(profile.IsInstalled, Is.False);
        }
        finally
        {
            DeleteIfExists(existingBinary);
        }
    }

    [Test]
    public void ChromiumProfilesResolveLinuxBinaryFromPathUsingChannelPriority()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux PATH resolution.");

        var directory = CreateTemporaryDirectory();
        var stable = CreateBinary(directory, "google-chrome");
        var beta = CreateBinary(directory, "google-chrome-beta");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", directory + IOPath.PathSeparator + originalPath);

            var profile = new ChromeProfile(WebBrowserChannel.Beta);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(profile.BinaryPath, Is.EqualTo(beta));
                Assert.That(profile.BinaryPath, Is.Not.EqualTo(stable));
                Assert.That(profile.IsInstalled, Is.True);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            DeleteDirectoryIfExists(directory);
        }
    }

    [Test]
    public void FirefoxProfileResolvesLinuxBinaryFromPathUsingChannelPriority()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux PATH resolution.");

        var directory = CreateTemporaryDirectory();
        var stable = CreateBinary(directory, "firefox");
        var beta = CreateBinary(directory, "firefox-beta");
        var developer = CreateBinary(directory, "firefox-developer-edition");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", directory + IOPath.PathSeparator + originalPath);

            var profile = new FirefoxProfile(WebBrowserChannel.Dev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(profile.BinaryPath, Is.EqualTo(developer));
                Assert.That(profile.BinaryPath, Is.Not.EqualTo(beta));
                Assert.That(profile.BinaryPath, Is.Not.EqualTo(stable));
                Assert.That(profile.IsInstalled, Is.True);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            DeleteDirectoryIfExists(directory);
        }
    }

    [TestCase(typeof(ChromeProfile), "google-chrome-stable", TestName = "Chrome profile resolves Arch stable binary name")]
    [TestCase(typeof(EdgeProfile), "microsoft-edge-stable", TestName = "Edge profile resolves Arch stable binary name")]
    [TestCase(typeof(BraveProfile), "brave", TestName = "Brave profile resolves Arch stable binary name")]
    [TestCase(typeof(VivaldiProfile), "vivaldi-stable", TestName = "Vivaldi profile resolves Arch stable binary name")]
    [TestCase(typeof(YandexProfile), "yandex-browser-corporate", TestName = "Yandex profile resolves Arch stable binary name")]
    public void ChromiumProfileResolvesInstalledLinuxDistributionBinaryNames(Type profileType, string binaryName)
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Тест рассчитан на Linux PATH resolution.");

        var directory = CreateTemporaryDirectory();
        var binaryPath = CreateBinary(directory, binaryName);
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", directory + IOPath.PathSeparator + originalPath);

            var profile = (WebBrowserProfile)Activator.CreateInstance(profileType, WebBrowserChannel.Stable)!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(profile.BinaryPath, Is.EqualTo(binaryPath));
                Assert.That(profile.IsInstalled, Is.True);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            DeleteDirectoryIfExists(directory);
        }
    }

    private static void AssertConstructorSurface(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.That(constructors.Any(ctor => HasParameters(ctor, typeof(string), typeof(WebBrowserChannel))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (string, WebBrowserChannel).");
        Assert.That(constructors.Any(ctor => HasParameters(ctor, typeof(string))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (string).");
        Assert.That(constructors.Any(ctor => HasParameters(ctor, typeof(WebBrowserChannel))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (WebBrowserChannel).");
        Assert.That(constructors.Any(ctor => ctor.GetParameters().Length == 0), Is.True,
            $"У типа '{type.Name}' ожидался ctor ().");
    }

    private static bool HasParameters(ConstructorInfo constructor, params Type[] parameters)
        => constructor.GetParameters().Select(static parameter => parameter.ParameterType).SequenceEqual(parameters);

    private static string CreateTemporaryDirectory()
    {
        var path = IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTemporaryFile()
    {
        var path = IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static WebBrowserProfile CreateStableChromiumProfile(string browserKind, string binaryPath)
        => browserKind switch
        {
            "edge" => new EdgeProfile(binaryPath, WebBrowserChannel.Stable),
            "brave" => new BraveProfile(binaryPath, WebBrowserChannel.Stable),
            "opera" => new OperaProfile(binaryPath, WebBrowserChannel.Stable),
            "vivaldi" => new VivaldiProfile(binaryPath, WebBrowserChannel.Stable),
            _ => throw new ArgumentOutOfRangeException(nameof(browserKind), browserKind, "Неизвестный Chromium-профиль для теста"),
        };

    private static string CreateBinary(string directory, string name)
    {
        var path = IOPath.Combine(directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string CreateExecutableBrowserHarness(string directory, string argumentsPath, string pidPath)
    {
        Directory.CreateDirectory(directory);
        var path = IOPath.Combine(directory, "browser-harness.sh");
        File.WriteAllText(
            path,
            string.Join(
                '\n',
                [
                    "#!/bin/sh",
                    "trap 'exit 0' TERM INT",
                    $"printf '%s\\n' \"$$\" > '{EscapeShellSingleQuoted(arguments: pidPath)}'",
                    $": > '{EscapeShellSingleQuoted(arguments: argumentsPath)}'",
                    $"for arg in \"$@\"; do printf '%s\\n' \"$arg\" >> '{EscapeShellSingleQuoted(arguments: argumentsPath)}'; done",
                    "while :; do sleep 1; done",
                ]));

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);

        return path;
    }

    [SupportedOSPlatform("linux")]
    private static string CreateFirefoxWrapperScript(string directory, string targetBinaryPath)
    {
        Directory.CreateDirectory(directory);

        var path = IOPath.Combine(directory, "firefox");
        File.WriteAllText(
                path,
                string.Join(
                        '\n',
                        [
                                "#!/bin/sh",
                                        $"exec '{EscapeShellSingleQuoted(targetBinaryPath)}' \"$@\"",
                        ]));

        File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);

        return path;
    }

    private static string CreateFirefoxSignedXpiPackage(
        string directory,
        string version,
        string addonId,
        string? sourceExtensionPath = null,
        string? backgroundRuntimeContent = null,
        string? contentScriptContent = null,
        bool includeBackgroundRuntime = true,
        bool includeContentScript = true,
        bool includeStoragePermission = true)
    {
        Directory.CreateDirectory(directory);

        var path = IOPath.Combine(directory, "atom-firefox-signed.xpi");
        DeleteIfExists(path);

        var storagePermissionEntry = includeStoragePermission
            ? "\n                            \"storage\","
            : string.Empty;

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        CreateFirefoxSignedXpiEntry(
            archive,
            "manifest.json",
            $$"""
                    {
                        "manifest_version": 2,
                        "name": "Atom WebDriver Connector",
                        "version": "{{version}}",
                        "permissions": [
                            {{storagePermissionEntry}}
                            "tabs"
                        ],
                        "browser_specific_settings": {
                            "gecko": {
                                "id": "{{addonId}}"
                            }
                        }
                    }
                    """);

        if (!string.IsNullOrWhiteSpace(sourceExtensionPath))
        {
            if (includeBackgroundRuntime)
            {
                CreateFirefoxSignedXpiEntry(
                    archive,
                    "background.runtime.js",
                    backgroundRuntimeContent ?? File.ReadAllText(IOPath.Combine(sourceExtensionPath, "background.runtime.js")));
            }

            if (includeContentScript)
            {
                CreateFirefoxSignedXpiEntry(
                    archive,
                    "content.js",
                    contentScriptContent ?? File.ReadAllText(IOPath.Combine(sourceExtensionPath, "content.js")));
            }
        }

        return path;
    }

    private static void AssertFirefoxSignedXpiStalePayloadDiagnostic(InvalidOperationException exception, string entryName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("stale payload"));
            Assert.That(exception.Message, Does.Contain(entryName));
            Assert.That(exception.Message, Does.Contain("не совпадает с текущим runtime output"));
            Assert.That(exception.Message, Does.Contain("Поднимите версию расширения"));
            Assert.That(exception.Message, Does.Contain("пересоберите и заново подпишите Firefox XPI"));
        });
    }

    private static void AssertFirefoxSignedXpiMissingRuntimeEntryDiagnostic(InvalidOperationException exception, string entryName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain($"не содержит '{entryName}'"));
            Assert.That(exception.Message, Does.Contain("managed policy bootstrap"));
        });
    }

    private static void CreateFirefoxSignedXpiEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string EscapeShellSingleQuoted(string arguments)
        => arguments.Replace("'", "'\"'\"'", StringComparison.Ordinal);

    private static WebBrowserSettings ApplyLaunchOverridesForTests(WebBrowserSettings settings)
    {
        var method = typeof(WebBrowser).GetMethod("ApplyOverrides", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(WebBrowser).FullName, "ApplyOverrides");

        return method.Invoke(null, [settings]) as WebBrowserSettings
            ?? throw new InvalidOperationException("Не удалось применить тестовые launch overrides.");
    }

    private static async ValueTask<ProfileMaterializationResult> MaterializeLaunchArtifactsAsync(WebBrowserSettings settings)
    {
        var preparation = BridgeExtensionBootstrap.TryCreatePreparation(settings);
        var boundPreparation = preparation is null ? null : BindBridgeBootstrapPreparation(preparation);
        return await ProfileMaterialization.MaterializeAsync(settings, boundPreparation, CancellationToken.None).ConfigureAwait(false);
    }

    private static BridgeBootstrapPreparation BindBridgeBootstrapPreparation(BridgeBootstrapPreparation preparation)
    {
        var bindMethod = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
            "BindBridgeBootstrapPort",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(Atom.Net.Browsing.WebDriver.WebBrowser).FullName, "BindBridgeBootstrapPort");

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

    private static Process LaunchBrowserProcessForTests(WebBrowserSettings settings, BridgeBootstrapPlan? bridgeBootstrap)
    {
        var method = typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetMethod(
            "LaunchBrowserProcess",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(Atom.Net.Browsing.WebDriver.WebBrowser).FullName, "LaunchBrowserProcess");

        return method.Invoke(null, [settings, bridgeBootstrap]) as Process
            ?? throw new InvalidOperationException("Не удалось запустить browser harness через private LaunchBrowserProcess.");
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitForConditionAsync<TState>(Func<TState, bool> predicate, TState state, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!predicate(state))
        {
            if (stopwatch.Elapsed >= timeout)
                Assert.Fail($"Condition was not satisfied within {timeout}.");

            await Task.Delay(50);
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}