using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Text;

namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeBootstrapPreparation(
    BridgeSettings Settings,
    string SourceExtensionPath,
    string BrowserFamily,
    string ExtensionVersion);

internal sealed record BridgeBootstrapPlan(
    string SessionId,
    string BrowserFamily,
    string ExtensionVersion,
    ChromiumBootstrapStrategy Strategy,
    string Host,
    int Port,
    string? TransportUrl,
    int ManagedDeliveryPort,
    bool ManagedDeliveryRequiresCertificateBypass,
    BridgeManagedDeliveryTrustDiagnostics ManagedDeliveryTrustDiagnostics,
    string Secret,
    string LaunchBinaryPath,
    string LocalExtensionPath,
    string ExtensionId,
    string BundledConfigPath,
    string ManagedStorageConfigPath,
    string LocalStorageConfigPath,
    string ManagedPolicyPath,
    string ManagedPolicyPublishPath,
    BridgeManagedPolicyPublishDiagnostics ManagedPolicyDiagnostics,
    string ManagedUpdateUrl,
    string ManagedPackageUrl,
    string ManagedPackageArtifactPath,
    string DiscoveryUrl,
    TimeSpan ConnectionTimeout);

internal sealed record BridgeBootstrapConfigArtifacts(
    string BundledConfigPath,
    string ManagedStorageConfigPath,
    string LocalStorageConfigPath);

internal sealed record BridgeBootstrapManagedPolicyArtifacts(
    string PolicyPath,
    string PublishPath,
    BridgeManagedPolicyPublishDiagnostics Diagnostics,
    string UpdateUrl,
    string PackageUrl);

internal sealed record BridgeBootstrapDeliveryArtifacts(
    string ExtensionId,
    BridgeBootstrapManagedPolicyArtifacts ManagedPolicy,
    BridgePackagedExtensionArtifacts Package);

internal sealed record BridgeBootstrapMaterializedArtifacts(
    int ManagedDeliveryPort,
    BridgeBootstrapConfigArtifacts Config,
    BridgeBootstrapDeliveryArtifacts Delivery);

internal sealed record FirefoxManagedInstallationArtifacts(
    string LaunchBinaryPath,
    BridgeBootstrapManagedPolicyArtifacts ManagedPolicy);

internal sealed record BridgePackagedExtensionArtifacts(
    string PackagePath,
    byte[] PackageBytes);

internal sealed record ChromiumExtensionKeyArtifacts(
    string PublicKeyBase64,
    string ExtensionId,
    string PrivateKeyPath);

internal sealed record ProfileMaterializationResult(
    string? MaterializedProfilePath,
    BridgeBootstrapPlan? BridgeBootstrap);

internal enum ChromiumBootstrapInstallMode
{
    ProfileSeeded,
    SystemManagedPolicy,
    BrowserInstallationManagedPolicy,
}

internal enum ChromiumBootstrapTransportMode
{
    WebSocket,
    SecureWebSocket,
}

internal sealed record ChromiumBootstrapStrategy(
    ChromiumBootstrapInstallMode InstallMode,
    ChromiumBootstrapTransportMode TransportMode,
    bool UseCommandLineExtensionLoad)
{
    internal static readonly ChromiumBootstrapStrategy ProfileSeeded = new(ChromiumBootstrapInstallMode.ProfileSeeded, ChromiumBootstrapTransportMode.WebSocket, UseCommandLineExtensionLoad: true);

    internal static readonly ChromiumBootstrapStrategy ProfileSeededSecure = new(ChromiumBootstrapInstallMode.ProfileSeeded, ChromiumBootstrapTransportMode.SecureWebSocket, UseCommandLineExtensionLoad: true);

    internal static readonly ChromiumBootstrapStrategy SystemManagedPolicy = new(ChromiumBootstrapInstallMode.SystemManagedPolicy, ChromiumBootstrapTransportMode.SecureWebSocket, UseCommandLineExtensionLoad: false);

    internal static readonly ChromiumBootstrapStrategy FirefoxProfileSeeded = new(ChromiumBootstrapInstallMode.ProfileSeeded, ChromiumBootstrapTransportMode.SecureWebSocket, UseCommandLineExtensionLoad: false);

    internal static readonly ChromiumBootstrapStrategy FirefoxManagedPolicy = new(ChromiumBootstrapInstallMode.BrowserInstallationManagedPolicy, ChromiumBootstrapTransportMode.SecureWebSocket, UseCommandLineExtensionLoad: false);

}

internal static class BridgeExtensionBootstrap
{
    private const string ChromiumExtensionDirectoryName = "Extension";
    private const string MaterializedChromiumExtensionDirectoryName = "Atom.WebDriver.Extension";
    private const string FirefoxExtensionDirectoryName = "Extension.Firefox";
    private const string LinuxChromeManagedPolicyDirectory = "/etc/opt/chrome/policies/managed";
    private const string LinuxEdgeManagedPolicyDirectory = "/etc/opt/edge/policies/managed";
    private const string LinuxBraveManagedPolicyDirectory = "/etc/opt/BraveSoftware/Brave-Browser/policies/managed";
    private const string LinuxOperaManagedPolicyDirectory = "/etc/opt/opera/policies/managed";
    private const string LinuxVivaldiManagedPolicyDirectory = "/etc/opt/vivaldi/policies/managed";
    private const string LinuxChromeManagedPolicyFileName = "atom-webdriver-extension.json";
    private const string LegacyLinuxChromeManagedPolicyFileName = "escorp-browser.json";
    private const string RootPasswordEnv = "ATOM_WEBDRIVER_ROOT_PASSWORD";
    private const string LegacyRootPasswordEnv = "ESCORP_ROOT_PASSWORD";
    private const string FirefoxSignedPackagePathEnv = "ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH";
    private const string LegacyFirefoxSignedPackagePathEnv = "ESCORP_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH";
    private const string FirefoxManagedInstallDirectoryName = ".bridge-firefox-installation";
    private const string FirefoxManagedConfigDirectoryName = ".bridge-firefox-runtime-config";
    private const string FirefoxPolicyFileName = "policies.json";
    private const string LinuxFirefoxSystemPolicyPath = "/etc/firefox/policies/policies.json";
    private const string FirefoxSystemPolicyPathOverrideKey = "Atom.WebDriver.FirefoxSystemPolicyPath";

    internal static BridgeBootstrapPreparation? TryCreatePreparation(WebBrowserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Profile is not { } profile)
            return null;

        var sourceExtensionPath = ResolveSourceExtensionPath(profile);
        if (sourceExtensionPath is null)
            return null;

        var manifestPath = Path.Combine(sourceExtensionPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new JsonException("Не удалось разобрать manifest.json браузерного расширения");

        var extensionVersion = manifest["version"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(extensionVersion))
            return null;

        return new BridgeBootstrapPreparation(
            Settings: new BridgeSettings
            {
                Secret = GenerateSecret(),
                Logger = settings.Logger,
                UseRootlessChromiumBootstrap = settings.UseRootlessChromiumBootstrap,
            },
            SourceExtensionPath: sourceExtensionPath,
            BrowserFamily: profile is FirefoxProfile ? "firefox" : "chromium",
            ExtensionVersion: extensionVersion);
    }

    internal static async ValueTask<BridgeBootstrapPlan> MaterializeAsync(
        string profilePath,
        WebBrowserProfile profile,
        BridgeBootstrapPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();

        if (profile is FirefoxProfile)
            return await MaterializeFirefoxAsync(profilePath, profile, preparation, cancellationToken).ConfigureAwait(false);

        var localExtensionPath = Path.Combine(profilePath, MaterializedChromiumExtensionDirectoryName);
        CopyDirectoryRecursive(preparation.SourceExtensionPath, localExtensionPath);

        var manifestPath = Path.Combine(localExtensionPath, "manifest.json");
        var manifest = await ReadMaterializedManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);

        var sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var port = GetMaterializedBridgePort(preparation);
        var strategy = ResolveChromiumBootstrapStrategy(profile, preparation.Settings.UseRootlessChromiumBootstrap);
        var materializedArtifacts = await MaterializeRuntimeArtifactsAsync(
            profilePath,
            profile,
            localExtensionPath,
            manifestPath,
            manifest,
            preparation,
            strategy,
            sessionId,
            port,
            cancellationToken).ConfigureAwait(false);
        var (effectiveStrategy, effectiveArtifacts) = await ApplyChromiumManagedPolicyFallbackIfNeededAsync(
            profilePath,
            profile,
            localExtensionPath,
            manifest,
            strategy,
            materializedArtifacts,
            cancellationToken).ConfigureAwait(false);

        return CreateMaterializedBridgeBootstrapPlan(
            preparation,
            effectiveStrategy,
            sessionId,
            localExtensionPath,
            port,
            effectiveArtifacts);
    }

    private static async ValueTask<BridgeBootstrapPlan> MaterializeFirefoxAsync(
        string profilePath,
        WebBrowserProfile profile,
        BridgeBootstrapPreparation preparation,
        CancellationToken cancellationToken)
    {
        var (addonId, localExtensionPath) = await PrepareFirefoxProfileExtensionAsync(
            profilePath,
            preparation.SourceExtensionPath,
            cancellationToken).ConfigureAwait(false);

        var strategy = ChromiumBootstrapStrategy.FirefoxProfileSeeded;
        var sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var port = GetMaterializedBridgePort(preparation);
        var transportUrl = ResolveTransportUrl(strategy, preparation.Settings);
        var runtimeConfig = BuildRuntimeConfig(
            preparation.Settings,
            sessionId,
            preparation.BrowserFamily,
            preparation.ExtensionVersion,
            transportUrl);

        if (IsLinuxFirefoxStableProfile(profile)
            && TryResolveConfiguredFirefoxSignedPackagePath(addonId, preparation.ExtensionVersion, preparation.SourceExtensionPath) is { } signedPackagePath)
        {
            return await MaterializeFirefoxStableManagedAsync(
                profilePath,
                profile,
                preparation,
                addonId,
                sessionId,
                port,
                transportUrl,
                runtimeConfig,
                signedPackagePath,
                cancellationToken).ConfigureAwait(false);
        }

        return await MaterializeFirefoxProfileSeededAsync(
            profilePath,
            profile,
            preparation,
            addonId,
            sessionId,
            localExtensionPath,
            strategy,
            port,
            transportUrl,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<BridgeBootstrapPlan> MaterializeFirefoxProfileSeededAsync(
        string profilePath,
        WebBrowserProfile profile,
        BridgeBootstrapPreparation preparation,
        string addonId,
        string sessionId,
        string localExtensionPath,
        ChromiumBootstrapStrategy strategy,
        int port,
        string? transportUrl,
        JsonObject runtimeConfig,
        CancellationToken cancellationToken)
    {
        var configArtifacts = await WriteRuntimeConfigArtifactsAsync(
            localExtensionPath,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);

        EnsureFirefoxProfileSecureTransportTrust(preparation, profilePath, strategy);

        return CreateBridgeBootstrapPlan(
            SessionId: sessionId,
            BrowserFamily: preparation.BrowserFamily,
            ExtensionVersion: preparation.ExtensionVersion,
            Strategy: strategy,
            Host: preparation.Settings.Host,
            Port: port,
            TransportUrl: transportUrl,
            ManagedDeliveryPort: port,
            ManagedDeliveryRequiresCertificateBypass: false,
            ManagedDeliveryTrustDiagnostics: BridgeManagedDeliveryTrustDiagnostics.Trusted("firefox-profile-extension"),
            Secret: preparation.Settings.Secret,
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: localExtensionPath,
            ExtensionId: addonId,
            BundledConfigPath: configArtifacts.BundledConfigPath,
            ManagedStorageConfigPath: configArtifacts.ManagedStorageConfigPath,
            LocalStorageConfigPath: configArtifacts.LocalStorageConfigPath,
            ManagedPolicyPath: string.Empty,
            ManagedPolicyPublishPath: string.Empty,
            ManagedPolicyDiagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal(localExtensionPath, GetFirefoxBootstrapDiagnosticsDetail(profile)),
            ManagedUpdateUrl: string.Empty,
            ManagedPackageUrl: string.Empty,
            ManagedPackageArtifactPath: string.Empty,
            DiscoveryUrl: BuildDiscoveryUrl(preparation.Settings.Host, port),
            ConnectionTimeout: preparation.Settings.BootstrapTimeout);
    }

    private static async ValueTask<BridgeBootstrapPlan> MaterializeFirefoxStableManagedAsync(
        string profilePath,
        WebBrowserProfile profile,
        BridgeBootstrapPreparation preparation,
        string addonId,
        string sessionId,
        int port,
        string? transportUrl,
        JsonObject runtimeConfig,
        string signedPackagePath,
        CancellationToken cancellationToken)
    {
        var managedStrategy = ChromiumBootstrapStrategy.FirefoxManagedPolicy;
        var configDirectoryPath = Path.Combine(profilePath, FirefoxManagedConfigDirectoryName);
        Directory.CreateDirectory(configDirectoryPath);

        var stableConfigArtifacts = await WriteRuntimeConfigArtifactsAsync(
            configDirectoryPath,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);
        var managedInstallation = await MaterializeFirefoxManagedInstallationAsync(
            profilePath,
            profile,
            addonId,
            signedPackagePath,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);

        EnsureFirefoxProfileSecureTransportTrust(preparation, profilePath, managedStrategy);

        return CreateBridgeBootstrapPlan(
            SessionId: sessionId,
            BrowserFamily: preparation.BrowserFamily,
            ExtensionVersion: preparation.ExtensionVersion,
            Strategy: managedStrategy,
            Host: preparation.Settings.Host,
            Port: port,
            TransportUrl: transportUrl,
            ManagedDeliveryPort: port,
            ManagedDeliveryRequiresCertificateBypass: false,
            ManagedDeliveryTrustDiagnostics: BridgeManagedDeliveryTrustDiagnostics.Trusted("firefox-install-overlay-policy"),
            Secret: preparation.Settings.Secret,
            LaunchBinaryPath: managedInstallation.LaunchBinaryPath,
            LocalExtensionPath: signedPackagePath,
            ExtensionId: addonId,
            BundledConfigPath: stableConfigArtifacts.BundledConfigPath,
            ManagedStorageConfigPath: stableConfigArtifacts.ManagedStorageConfigPath,
            LocalStorageConfigPath: stableConfigArtifacts.LocalStorageConfigPath,
            ManagedPolicyPath: managedInstallation.ManagedPolicy.PolicyPath,
            ManagedPolicyPublishPath: managedInstallation.ManagedPolicy.PublishPath,
            ManagedPolicyDiagnostics: managedInstallation.ManagedPolicy.Diagnostics,
            ManagedUpdateUrl: managedInstallation.ManagedPolicy.UpdateUrl,
            ManagedPackageUrl: managedInstallation.ManagedPolicy.PackageUrl,
            ManagedPackageArtifactPath: signedPackagePath,
            DiscoveryUrl: BuildDiscoveryUrl(preparation.Settings.Host, port),
            ConnectionTimeout: preparation.Settings.BootstrapTimeout);
    }

    private static async ValueTask<(string AddonId, string LocalExtensionPath)> PrepareFirefoxProfileExtensionAsync(
        string profilePath,
        string sourceExtensionPath,
        CancellationToken cancellationToken)
    {
        var sourceManifestPath = Path.Combine(sourceExtensionPath, "manifest.json");
        var sourceManifest = await ReadMaterializedManifestAsync(sourceManifestPath, cancellationToken).ConfigureAwait(false);
        var addonId = GetFirefoxAddonId(sourceManifest);
        var localExtensionPath = Path.Combine(profilePath, "extensions", addonId);

        CopyDirectoryRecursive(sourceExtensionPath, localExtensionPath);

        var materializedManifestPath = Path.Combine(localExtensionPath, "manifest.json");
        var materializedManifest = await ReadMaterializedManifestAsync(materializedManifestPath, cancellationToken).ConfigureAwait(false);
        NormalizeFirefoxManifest(materializedManifest);
        await File.WriteAllTextAsync(materializedManifestPath, materializedManifest.ToJsonString(), cancellationToken).ConfigureAwait(false);

        return (addonId, localExtensionPath);
    }

    private static async ValueTask<JsonObject> ReadMaterializedManifestAsync(string manifestPath, CancellationToken cancellationToken)
        => JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false))?.AsObject()
            ?? throw new JsonException("Не удалось разобрать materialized manifest.json расширения");

    private static void EnsureFirefoxProfileSecureTransportTrust(
        BridgeBootstrapPreparation preparation,
        string profilePath,
        ChromiumBootstrapStrategy strategy)
    {
        if (!OperatingSystem.IsLinux() || strategy.TransportMode is not ChromiumBootstrapTransportMode.SecureWebSocket)
            return;

        var certificate = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateAuthorityCertificate();
        var transportTrustDiagnostics = BridgeManagedDeliveryCertificateTrustInstaller.EnsureTrustedForFirefoxProfile(certificate, profilePath);
        preparation.Settings.Logger?.LogFirefoxProfileWssTrust(
            transportTrustDiagnostics.Status,
            transportTrustDiagnostics.Method,
            transportTrustDiagnostics.Detail,
            profilePath);
    }

    private static string GetFirefoxAddonId(JsonObject manifest)
        => manifest["browser_specific_settings"]?["gecko"]?["id"]?.GetValue<string>()
            ?? throw new JsonException("Firefox-расширение не содержит browser_specific_settings.gecko.id в manifest.json");

    private static string GetFirefoxBootstrapDiagnosticsDetail(WebBrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (OperatingSystem.IsLinux()
            && profile is FirefoxProfile { Channel: WebBrowserChannel.Stable })
        {
            return $"linux-firefox-stable-unsigned-addon: Firefox Stable на Linux не поддерживает надёжный неподписанный profile-local bootstrap расширения; runtime намеренно не переключается на Marionette, потому что он раскрывает automation state в JS-окружении страницы; для stable bootstrap используйте подписанный XPI через {FirefoxSignedPackagePathEnv} либо запускайте Firefox Developer Edition или Nightly для живой проверки";
        }

        return "firefox-profile-extension";
    }

    private static bool IsLinuxFirefoxStableProfile(WebBrowserProfile profile)
        => OperatingSystem.IsLinux() && profile is FirefoxProfile { Channel: WebBrowserChannel.Stable };

    private static string? TryResolveConfiguredFirefoxSignedPackagePath(string expectedAddonId, string expectedVersion, string expectedSourceExtensionPath)
    {
        var configuredPath = Environment.GetEnvironmentVariable(FirefoxSignedPackagePathEnv);
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = Environment.GetEnvironmentVariable(LegacyFirefoxSignedPackagePathEnv);

        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var packagePath = Path.GetFullPath(configuredPath.Trim());
        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"Не найден подписанный Firefox XPI из переменной {FirefoxSignedPackagePathEnv}", packagePath);

        ValidateFirefoxSignedPackage(packagePath, expectedAddonId, expectedVersion, expectedSourceExtensionPath);
        return packagePath;
    }

    private static void ValidateFirefoxSignedPackage(string packagePath, string expectedAddonId, string expectedVersion, string expectedSourceExtensionPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException($"Подписанный Firefox XPI '{packagePath}' не содержит manifest.json");

        using var manifestStream = manifestEntry.Open();
        var manifest = JsonNode.Parse(manifestStream)?.AsObject()
            ?? throw new JsonException($"Не удалось разобрать manifest.json в подписанном Firefox XPI '{packagePath}'");

        var addonId = GetFirefoxAddonId(manifest);
        if (!string.Equals(addonId, expectedAddonId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Подписанный Firefox XPI '{packagePath}' собран для другого add-on id: '{addonId}', ожидался '{expectedAddonId}'");
        }

        var packageVersion = manifest["version"]?.GetValue<string>();
        if (!string.Equals(packageVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Подписанный Firefox XPI '{packagePath}' имеет версию '{packageVersion}', ожидалась '{expectedVersion}'");
        }

        if (!HasFirefoxPermission(manifest, "storage"))
            throw new InvalidOperationException($"Подписанный Firefox XPI '{packagePath}' не содержит permission 'storage', необходимый для managed policy bootstrap");

        ValidateFirefoxSignedPackageRuntimePayload(archive, packagePath, expectedSourceExtensionPath, "background.runtime.js");
        ValidateFirefoxSignedPackageRuntimePayload(archive, packagePath, expectedSourceExtensionPath, "content.js");
    }

    private static bool HasFirefoxPermission(JsonObject manifest, string permission)
        => manifest["permissions"] is JsonArray permissions
            && permissions.Any(item => string.Equals(item?.GetValue<string>(), permission, StringComparison.Ordinal));

    private static void ValidateFirefoxSignedPackageRuntimePayload(
        ZipArchive archive,
        string packagePath,
        string expectedSourceExtensionPath,
        string entryName)
    {
        var packageEntry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Подписанный Firefox XPI '{packagePath}' не содержит '{entryName}', необходимый для managed policy bootstrap");

        var expectedFilePath = Path.Combine(expectedSourceExtensionPath, entryName);
        if (!File.Exists(expectedFilePath))
            throw new FileNotFoundException($"Не найден текущий Firefox runtime output для проверки signed XPI: {expectedFilePath}", expectedFilePath);

        using var packageStream = packageEntry.Open();
        var packageHash = SHA256.HashData(packageStream);
        var expectedHash = SHA256.HashData(File.ReadAllBytes(expectedFilePath));

        if (!CryptographicOperations.FixedTimeEquals(packageHash, expectedHash))
        {
            throw new InvalidOperationException(
                $"Подписанный Firefox XPI '{packagePath}' содержит stale payload для '{entryName}' и не совпадает с текущим runtime output '{expectedFilePath}'. Поднимите версию расширения, пересоберите и заново подпишите Firefox XPI.");
        }
    }

    private static void NormalizeFirefoxManifest(JsonObject manifest)
    {
        if (manifest["background"] is JsonObject background)
            background.Remove("persistent");
    }

    private static int GetMaterializedBridgePort(BridgeBootstrapPreparation preparation)
    {
        var port = preparation.Settings.Port;
        return port > 0
            ? port
            : throw new InvalidOperationException("BridgeServer должен выбрать порт до materialization расширения");
    }

    private static async ValueTask<BridgeBootstrapMaterializedArtifacts> MaterializeRuntimeArtifactsAsync(
        string profilePath,
        WebBrowserProfile profile,
        string localExtensionPath,
        string manifestPath,
        JsonObject manifest,
        BridgeBootstrapPreparation preparation,
        ChromiumBootstrapStrategy strategy,
        string sessionId,
        int port,
        CancellationToken cancellationToken)
    {
        var managedDeliveryPort = preparation.Settings.ManagedDeliveryPort > 0 ? preparation.Settings.ManagedDeliveryPort : port;
        var transportUrl = ResolveTransportUrl(strategy, preparation.Settings);
        var runtimeConfig = BuildRuntimeConfig(
            preparation.Settings,
            sessionId,
            preparation.BrowserFamily,
            preparation.ExtensionVersion,
            transportUrl);
        var configArtifacts = await WriteRuntimeConfigArtifactsAsync(
            localExtensionPath,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);
        var deliveryArtifacts = await MaterializeManagedDeliveryArtifactsAsync(
            profilePath,
            profile,
            localExtensionPath,
            manifestPath,
            manifest,
            preparation.Settings.Host,
            managedDeliveryPort,
            runtimeConfig,
            strategy,
            cancellationToken).ConfigureAwait(false);

        return new BridgeBootstrapMaterializedArtifacts(managedDeliveryPort, configArtifacts, deliveryArtifacts);
    }

    private static async ValueTask<(ChromiumBootstrapStrategy Strategy, BridgeBootstrapMaterializedArtifacts MaterializedArtifacts)> ApplyChromiumManagedPolicyFallbackIfNeededAsync(
        string profilePath,
        WebBrowserProfile profile,
        string localExtensionPath,
        JsonObject manifest,
        ChromiumBootstrapStrategy strategy,
        BridgeBootstrapMaterializedArtifacts materializedArtifacts,
        CancellationToken cancellationToken)
    {
        if (!ShouldFallbackToProfileSeeded(profile, strategy, materializedArtifacts.Delivery.ManagedPolicy.Diagnostics))
            return (strategy, materializedArtifacts);

        var fallbackStrategy = ResolveChromiumProfileSeededStrategy(profile);
        await ConfigureChromiumProfileExtensionSettingsAsync(
            profilePath,
            fallbackStrategy,
            localExtensionPath,
            materializedArtifacts.Delivery.ExtensionId,
            manifest,
            cancellationToken).ConfigureAwait(false);

        var fallbackManagedPolicy = CreateProfileSeededFallbackManagedPolicyArtifacts(profile, materializedArtifacts.Delivery.ManagedPolicy);
        var fallbackDelivery = new BridgeBootstrapDeliveryArtifacts(
            materializedArtifacts.Delivery.ExtensionId,
            fallbackManagedPolicy,
            materializedArtifacts.Delivery.Package);

        return (
            fallbackStrategy,
            new BridgeBootstrapMaterializedArtifacts(
                materializedArtifacts.ManagedDeliveryPort,
                materializedArtifacts.Config,
                fallbackDelivery));
    }

    private static bool ShouldFallbackToProfileSeeded(
        WebBrowserProfile profile,
        ChromiumBootstrapStrategy strategy,
        BridgeManagedPolicyPublishDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return IsLinuxChromiumProfile(profile)
            && strategy.InstallMode is ChromiumBootstrapInstallMode.SystemManagedPolicy
            && string.Equals(diagnostics.Status, "system-publish-required", StringComparison.Ordinal);
    }

    private static BridgeBootstrapManagedPolicyArtifacts CreateProfileSeededFallbackManagedPolicyArtifacts(
        WebBrowserProfile profile,
        BridgeBootstrapManagedPolicyArtifacts managedPolicy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(managedPolicy);

        return new BridgeBootstrapManagedPolicyArtifacts(
            PolicyPath: managedPolicy.PolicyPath,
            PublishPath: managedPolicy.PolicyPath,
            Diagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal(
                managedPolicy.PolicyPath,
                BuildProfileSeededFallbackDiagnosticsDetail(profile, managedPolicy.Diagnostics)),
            UpdateUrl: managedPolicy.UpdateUrl,
            PackageUrl: managedPolicy.PackageUrl);
    }

    private static string BuildProfileSeededFallbackDiagnosticsDetail(
        WebBrowserProfile profile,
        BridgeManagedPolicyPublishDiagnostics failedDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(failedDiagnostics);

        var browserName = profile.GetType().Name.Replace("Profile", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var targetPath = string.IsNullOrWhiteSpace(failedDiagnostics.TargetPath) ? "<unknown>" : failedDiagnostics.TargetPath;
        var detail = $"linux-{browserName}-managed-policy-fallback: публикация system managed policy в '{targetPath}' не удалась, поэтому bootstrap переключён на profile-local seeded extension settings; status={failedDiagnostics.Status}";

        return string.IsNullOrWhiteSpace(failedDiagnostics.Detail)
            ? detail
            : string.Concat(detail, "; ", failedDiagnostics.Detail);
    }

    internal static string? ResolveLinuxSystemManagedPolicyPath(WebBrowserProfile profile, bool useRootlessChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return ResolveChromiumBootstrapStrategy(profile, useRootlessChromiumBootstrap).InstallMode is ChromiumBootstrapInstallMode.SystemManagedPolicy
            && ResolveLinuxSystemManagedPolicyDirectory(profile) is { Length: > 0 } directoryPath
            ? Path.Combine(directoryPath, LinuxChromeManagedPolicyFileName)
            : null;
    }

    internal static string? ResolveLegacyLinuxSystemManagedPolicyPath(WebBrowserProfile profile, bool useRootlessChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (ResolveChromiumBootstrapStrategy(profile, useRootlessChromiumBootstrap).InstallMode is not ChromiumBootstrapInstallMode.SystemManagedPolicy)
            return null;

        if (ResolveLinuxSystemManagedPolicyDirectory(profile) is not { Length: > 0 } directoryPath)
            return null;

        var legacyPath = Path.Combine(directoryPath, LegacyLinuxChromeManagedPolicyFileName);
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    internal static bool ShouldSeedChromiumProfileExtensionSettings(WebBrowserProfile profile, bool useRootlessChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ResolveChromiumBootstrapStrategy(profile, useRootlessChromiumBootstrap).InstallMode is ChromiumBootstrapInstallMode.ProfileSeeded;
    }

    internal static bool ShouldUseSecureTransport(WebBrowserProfile profile, bool useRootlessChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ResolveChromiumBootstrapStrategy(profile, useRootlessChromiumBootstrap).TransportMode is ChromiumBootstrapTransportMode.SecureWebSocket;
    }

    internal static ChromiumBootstrapStrategy ResolveChromiumBootstrapStrategy(WebBrowserProfile profile, bool useRootlessChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!useRootlessChromiumBootstrap && ResolveLinuxSystemManagedPolicyDirectory(profile) is not null)
            return ChromiumBootstrapStrategy.SystemManagedPolicy;

        return ResolveChromiumProfileSeededStrategy(profile);
    }

    private static ChromiumBootstrapStrategy ResolveChromiumProfileSeededStrategy(WebBrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return IsLinuxChromiumProfile(profile)
            ? ChromiumBootstrapStrategy.ProfileSeededSecure
            : ChromiumBootstrapStrategy.ProfileSeeded;
    }

    internal static string BuildSecureTransportUrl(string host, int port, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return new UriBuilder("wss", host, port)
        {
            Query = string.Concat("secret=", Uri.EscapeDataString(secret)),
        }.Uri.ToString();
    }

    internal static IReadOnlyList<string> GetLaunchArguments(WebBrowserProfile profile, BridgeBootstrapPlan? bridgeBootstrap)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (bridgeBootstrap is null)
            return [];

        var strategy = bridgeBootstrap.Strategy;
        List<string> arguments = [];

        if (strategy.UseCommandLineExtensionLoad
            && !string.IsNullOrWhiteSpace(bridgeBootstrap.LocalExtensionPath))
        {
            arguments.Add($"--disable-extensions-except={bridgeBootstrap.LocalExtensionPath}");
            arguments.Add($"--load-extension={bridgeBootstrap.LocalExtensionPath}");
        }

        if (profile is not VivaldiProfile && !string.IsNullOrWhiteSpace(bridgeBootstrap.DiscoveryUrl))
            arguments.Add(bridgeBootstrap.DiscoveryUrl);

        if (Uri.TryCreate(bridgeBootstrap.ManagedUpdateUrl, UriKind.Absolute, out var managedUpdateUri)
            && string.Equals(managedUpdateUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && bridgeBootstrap.ManagedDeliveryRequiresCertificateBypass)
        {
            arguments.Add("--ignore-certificate-errors");
            arguments.Add("--allow-insecure-localhost");
        }

        return arguments;
    }

    private static string? ResolveSourceExtensionPath(WebBrowserProfile profile)
    {
        var directoryName = profile is FirefoxProfile
            ? FirefoxExtensionDirectoryName
            : ChromiumExtensionDirectoryName;

        var workingLayoutCandidate = Path.Combine(AppContext.BaseDirectory, "ExtensionWorkingLayout", directoryName);
        if (IsUsableExtensionLayout(workingLayoutCandidate))
            return workingLayoutCandidate;

        var candidate = Path.Combine(AppContext.BaseDirectory, directoryName);
        return IsUsableExtensionLayout(candidate) ? candidate : null;
    }

    private static bool IsUsableExtensionLayout(string path)
        => Directory.Exists(path)
            && File.Exists(Path.Combine(path, "manifest.json"))
            && File.Exists(Path.Combine(path, "background.runtime.js"))
            && File.Exists(Path.Combine(path, "content.js"));

    private static JsonObject BuildRuntimeConfig(
        BridgeSettings settings,
        string sessionId,
        string browserFamily,
        string extensionVersion,
        string? transportUrl)
    {
        var config = new JsonObject
        {
            ["host"] = settings.Host,
            ["port"] = settings.Port,
            ["sessionId"] = sessionId,
            ["secret"] = settings.Secret,
            ["protocolVersion"] = Protocol.BridgeHandshakeValidator.CurrentProtocolVersion,
            ["browserFamily"] = browserFamily,
            ["extensionVersion"] = extensionVersion,
            ["featureFlags"] = new JsonObject
            {
                ["enableNavigationEvents"] = true,
                ["enableCallbackHooks"] = true,
                ["enableInterception"] = true,
                ["enableDiagnostics"] = true,
                ["enableKeepAlive"] = true,
            },
        };

        if (!string.IsNullOrWhiteSpace(transportUrl))
            config["transportUrl"] = transportUrl;

        if (settings.NavigationProxyPort > 0)
            config["proxyPort"] = settings.NavigationProxyPort;

        return config;
    }

    private static async ValueTask<BridgeBootstrapConfigArtifacts> WriteRuntimeConfigArtifactsAsync(
        string extensionPath,
        JsonObject runtimeConfig,
        CancellationToken cancellationToken)
    {
        var bundledConfigPath = Path.Combine(extensionPath, "config.json");
        var managedStorageConfigPath = Path.Combine(extensionPath, "storage.managed.json");
        var localStorageConfigPath = Path.Combine(extensionPath, "storage.local.json");

        await File.WriteAllTextAsync(bundledConfigPath, runtimeConfig.ToJsonString(), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(managedStorageConfigPath, runtimeConfig.ToJsonString(), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            localStorageConfigPath,
            new JsonObject
            {
                ["config"] = runtimeConfig.DeepClone(),
            }.ToJsonString(),
            cancellationToken).ConfigureAwait(false);

        return new BridgeBootstrapConfigArtifacts(
            BundledConfigPath: bundledConfigPath,
            ManagedStorageConfigPath: managedStorageConfigPath,
            LocalStorageConfigPath: localStorageConfigPath);
    }

    private static async ValueTask<FirefoxManagedInstallationArtifacts> MaterializeFirefoxManagedInstallationAsync(
        string profilePath,
        WebBrowserProfile profile,
        string extensionId,
        string signedPackagePath,
        JsonObject runtimeConfig,
        CancellationToken cancellationToken)
    {
        var resolvedExecutablePath = ResolveFirefoxExecutablePath(profile.BinaryPath);
        var sourceInstallDirectory = Path.GetDirectoryName(resolvedExecutablePath)
            ?? throw new DirectoryNotFoundException($"Не удалось определить каталог установки Firefox для '{resolvedExecutablePath}'");
        var overlayRoot = Path.Combine(profilePath, FirefoxManagedInstallDirectoryName);

        RecreateDirectory(overlayRoot);
        MirrorFirefoxInstallationIntoOverlay(sourceInstallDirectory, overlayRoot, resolvedExecutablePath);

        var installUrl = new Uri(signedPackagePath).AbsoluteUri;
        var policyDocument = await BuildMergedFirefoxPolicyDocumentAsync(
            sourceInstallDirectory,
            extensionId,
            installUrl,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);
        var publishPath = await WriteFirefoxPolicyOverlayAsync(
            sourceInstallDirectory,
            overlayRoot,
            policyDocument,
            cancellationToken).ConfigureAwait(false);

        var launchBinaryPath = Path.Combine(overlayRoot, Path.GetFileName(resolvedExecutablePath));
        var diagnostics = BuildFirefoxInstallOverlayDiagnostics(extensionId, signedPackagePath, publishPath, installUrl);

        return new FirefoxManagedInstallationArtifacts(
            LaunchBinaryPath: launchBinaryPath,
            ManagedPolicy: new BridgeBootstrapManagedPolicyArtifacts(
                PolicyPath: publishPath,
                PublishPath: publishPath,
                Diagnostics: diagnostics,
                UpdateUrl: string.Empty,
                PackageUrl: installUrl));
    }

    private static BridgeManagedPolicyPublishDiagnostics BuildFirefoxInstallOverlayDiagnostics(
        string extensionId,
        string signedPackagePath,
        string publishPath,
        string installUrl)
    {
        var detail = BuildFirefoxInstallOverlayDiagnosticsDetail(extensionId, signedPackagePath, installUrl);
        return BridgeManagedPolicyPublishDiagnostics.BrowserInstallationPublished(
            "linux-firefox-install-overlay-policy",
            publishPath,
            detail);
    }

    private static string BuildFirefoxInstallOverlayDiagnosticsDetail(
        string extensionId,
        string signedPackagePath,
        string installUrl)
    {
        var detail = $"signed-package-path={signedPackagePath}";
        if (TryBuildFirefoxSystemPolicyShadowingDetail(extensionId, installUrl) is not { Length: > 0 } shadowingDetail)
            return detail;

        return string.Concat(detail, "; ", shadowingDetail);
    }

    private static string? TryBuildFirefoxSystemPolicyShadowingDetail(string extensionId, string installUrl)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var systemPolicyPath = ResolveFirefoxSystemPolicyPath();
        if (!File.Exists(systemPolicyPath))
            return null;

        try
        {
            var policyDocument = JsonNode.Parse(File.ReadAllText(systemPolicyPath))?.AsObject();
            var extensionSettings = policyDocument?["policies"]?["ExtensionSettings"]?.AsObject();
            var extensionPolicy = extensionSettings?[extensionId]?.AsObject();
            if (extensionPolicy is null)
                return null;

            var systemInstallUrl = extensionPolicy["install_url"]?.GetValue<string>();
            var systemUpdateUrl = extensionPolicy["update_url"]?.GetValue<string>();
            var configuredUrl = !string.IsNullOrWhiteSpace(systemInstallUrl)
                ? systemInstallUrl
                : systemUpdateUrl;
            var relation = string.Equals(configuredUrl, installUrl, StringComparison.Ordinal)
                ? "matches-overlay-url"
                : "conflicts-with-overlay-url";

            return $"firefox-system-policy-shadowing: {systemPolicyPath} defines {extensionId}; relation={relation}; configuredUrl={configuredUrl ?? "<missing>"}; Firefox Stable may prefer the system policy over the install overlay";
        }
        catch (Exception ex)
        {
            return $"firefox-system-policy-shadowing: {systemPolicyPath} exists but runtime could not inspect it; {FormatExceptionDetail(ex)}";
        }
    }

    internal static string ResolveFirefoxSystemPolicyPath()
    {
        if (AppContext.GetData(FirefoxSystemPolicyPathOverrideKey) is string overridePath
            && !string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return LinuxFirefoxSystemPolicyPath;
    }

    private static string ResolveFirefoxExecutablePath(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        if (!File.Exists(binaryPath))
            throw new FileNotFoundException("Не найден бинарный файл Firefox для install overlay", binaryPath);

        using var stream = File.OpenRead(binaryPath);
        Span<byte> prefix = stackalloc byte[2];
        var bytesRead = stream.Read(prefix);
        if (bytesRead == 2 && prefix[0] == (byte)'#' && prefix[1] == (byte)'!')
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
            var remainingText = reader.ReadToEnd();
            var fullText = "#!" + remainingText;
            if (TryResolveExecutableFromShellWrapper(fullText, binaryPath) is { } wrapperTarget)
                return wrapperTarget;
        }

        return binaryPath;
    }

    private static string? TryResolveExecutableFromShellWrapper(string wrapperText, string wrapperPath)
    {
        foreach (var rawLine in wrapperText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith("exec ", StringComparison.Ordinal))
                continue;

            var command = rawLine[5..].Trim();
            if (command.EndsWith(" \"$@\"", StringComparison.Ordinal))
                command = command[..^5].TrimEnd();
            else if (command.EndsWith(" '$@'", StringComparison.Ordinal))
                command = command[..^5].TrimEnd();

            command = TrimMatchingQuotes(command);
            if (string.IsNullOrWhiteSpace(command))
                continue;

            var candidate = Path.IsPathRooted(command)
                ? command
                : Path.GetFullPath(command, Path.GetDirectoryName(wrapperPath) ?? Environment.CurrentDirectory);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string TrimMatchingQuotes(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static void RecreateDirectory(string path)
    {
        DeleteFileSystemEntryIfExists(path);
        Directory.CreateDirectory(path);
    }

    private static void MirrorFirefoxInstallationIntoOverlay(string sourceInstallDirectory, string overlayRoot, string sourceExecutablePath)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceInstallDirectory))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, "distribution", StringComparison.Ordinal))
                continue;

            var overlayEntry = Path.Combine(overlayRoot, name);
            DeleteFileSystemEntryIfExists(overlayEntry);

            if (string.Equals(entry, sourceExecutablePath, StringComparison.Ordinal))
            {
                CopyExecutableWithMode(entry, overlayEntry);
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.CreateSymbolicLink(overlayEntry, entry);
                continue;
            }

            File.CreateSymbolicLink(overlayEntry, entry);
        }
    }

    private static void CopyExecutableWithMode(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
    }

    private static void DeleteFileSystemEntryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static async ValueTask<JsonObject> BuildMergedFirefoxPolicyDocumentAsync(
        string sourceInstallDirectory,
        string extensionId,
        string installUrl,
        JsonObject runtimeConfig,
        CancellationToken cancellationToken)
    {
        var sourcePolicyPath = Path.Combine(sourceInstallDirectory, "distribution", FirefoxPolicyFileName);
        var runtimePolicyDocument = BuildFirefoxManagedPolicyDocument(extensionId, installUrl, runtimeConfig);

        if (!File.Exists(sourcePolicyPath))
            return runtimePolicyDocument;

        var existingDocument = JsonNode.Parse(await File.ReadAllTextAsync(sourcePolicyPath, cancellationToken).ConfigureAwait(false))?.AsObject();
        if (existingDocument is null)
            return runtimePolicyDocument;

        return MergeJsonObjects(existingDocument, runtimePolicyDocument);
    }

    private static JsonObject BuildFirefoxManagedPolicyDocument(string extensionId, string installUrl, JsonObject runtimeConfig)
        => new()
        {
            ["policies"] = new JsonObject
            {
                ["ExtensionSettings"] = new JsonObject
                {
                    [extensionId] = new JsonObject
                    {
                        ["installation_mode"] = "force_installed",
                        ["install_url"] = installUrl,
                        ["updates_disabled"] = true,
                        ["private_browsing"] = true,
                    },
                },
                ["3rdparty"] = new JsonObject
                {
                    ["Extensions"] = new JsonObject
                    {
                        [extensionId] = runtimeConfig.DeepClone(),
                    },
                },
            },
        };

    private static JsonObject MergeJsonObjects(JsonObject baseObject, JsonObject overlayObject)
    {
        var merged = (baseObject.DeepClone() as JsonObject) ?? new JsonObject();

        foreach (var property in overlayObject)
        {
            if (property.Value is JsonObject overlayChild
                && merged[property.Key] is JsonObject baseChild)
            {
                merged[property.Key] = MergeJsonObjects(baseChild, overlayChild);
                continue;
            }

            merged[property.Key] = property.Value?.DeepClone();
        }

        return merged;
    }

    private static async ValueTask<string> WriteFirefoxPolicyOverlayAsync(
        string sourceInstallDirectory,
        string overlayRoot,
        JsonObject policyDocument,
        CancellationToken cancellationToken)
    {
        var sourceDistributionDirectory = Path.Combine(sourceInstallDirectory, "distribution");
        var overlayDistributionDirectory = Path.Combine(overlayRoot, "distribution");
        Directory.CreateDirectory(overlayDistributionDirectory);

        if (Directory.Exists(sourceDistributionDirectory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDistributionDirectory))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, FirefoxPolicyFileName, StringComparison.Ordinal))
                    continue;

                var overlayEntry = Path.Combine(overlayDistributionDirectory, name);
                DeleteFileSystemEntryIfExists(overlayEntry);

                if (Directory.Exists(entry))
                    Directory.CreateSymbolicLink(overlayEntry, entry);
                else
                    File.CreateSymbolicLink(overlayEntry, entry);
            }
        }

        var publishPath = Path.Combine(overlayDistributionDirectory, FirefoxPolicyFileName);
        await File.WriteAllTextAsync(publishPath, policyDocument.ToJsonString(), cancellationToken).ConfigureAwait(false);
        return publishPath;
    }

    private static async ValueTask<BridgeBootstrapManagedPolicyArtifacts> WriteChromiumManagedPolicyArtifactsAsync(
        string profilePath,
        WebBrowserProfile profile,
        ChromiumBootstrapStrategy strategy,
        string extensionId,
        string host,
        int port,
        JsonObject runtimeConfig,
        CancellationToken cancellationToken)
    {
        var updateUrl = BuildManagedUpdateUrl(host, port, extensionId);
        var packageUrl = BuildManagedPackageUrl(host, port, extensionId);
        var policyPath = Path.Combine(profilePath, "chromium.managed-policy.json");
        var policy = BuildManagedPolicy(extensionId, updateUrl, runtimeConfig);
        var policyJson = policy.ToJsonString();

        await File.WriteAllTextAsync(policyPath, policyJson, cancellationToken).ConfigureAwait(false);

        var (publishPath, diagnostics) = await PublishManagedPolicyAsync(profile, strategy, policyPath, policyJson, cancellationToken).ConfigureAwait(false);

        return new BridgeBootstrapManagedPolicyArtifacts(
            PolicyPath: policyPath,
            PublishPath: publishPath,
            Diagnostics: diagnostics,
            UpdateUrl: updateUrl,
            PackageUrl: packageUrl);
    }

    private static JsonObject BuildManagedPolicy(string extensionId, string updateUrl, JsonObject runtimeConfig)
    {
        var updateOrigin = new Uri(updateUrl).GetLeftPart(UriPartial.Authority);

        return new JsonObject
        {
            ["ExtensionInstallForcelist"] = new JsonArray($"{extensionId};{updateUrl}"),
            ["ExtensionSettings"] = new JsonObject
            {
                [extensionId] = new JsonObject
                {
                    ["installation_mode"] = "force_installed",
                    ["update_url"] = updateUrl,
                    ["install_sources"] = new JsonArray($"{updateOrigin}/*"),
                    ["managed_configuration"] = runtimeConfig.DeepClone(),
                },
            },
        };
    }

    private static string BuildManagedUpdateUrl(string host, int port, string extensionId)
        => new UriBuilder(Uri.UriSchemeHttps, host, port, $"chromium/{extensionId}/manifest").Uri.ToString();

    private static string BuildManagedPackageUrl(string host, int port, string extensionId)
        => new UriBuilder(Uri.UriSchemeHttps, host, port, $"chromium/{extensionId}/extension.crx").Uri.ToString();

    private static async ValueTask<BridgeBootstrapDeliveryArtifacts> MaterializeManagedDeliveryArtifactsAsync(
        string profilePath,
        WebBrowserProfile profile,
        string localExtensionPath,
        string manifestPath,
        JsonObject manifest,
        string host,
        int managedDeliveryPort,
        JsonObject runtimeConfig,
        ChromiumBootstrapStrategy strategy,
        CancellationToken cancellationToken)
    {
        var extensionKey = GetOrCreateExtensionKey(profilePath);
        var extensionId = AddChromiumExtensionKey(manifestPath, manifest, extensionKey);
        await ConfigureChromiumProfileExtensionSettingsAsync(
            profilePath,
            strategy,
            localExtensionPath,
            extensionId,
            manifest,
            cancellationToken).ConfigureAwait(false);

        var packagedExtension = await PackageChromiumExtensionAsync(
            profilePath,
            localExtensionPath,
            extensionKey.PrivateKeyPath,
            extensionId,
            cancellationToken).ConfigureAwait(false);

        var managedPolicyArtifacts = await WriteChromiumManagedPolicyArtifactsAsync(
            profilePath,
            profile,
            strategy,
            extensionId,
            host,
            managedDeliveryPort,
            runtimeConfig,
            cancellationToken).ConfigureAwait(false);

        return new BridgeBootstrapDeliveryArtifacts(extensionId, managedPolicyArtifacts, packagedExtension);
    }

    private static ValueTask ConfigureChromiumProfileExtensionSettingsAsync(
        string profilePath,
        ChromiumBootstrapStrategy strategy,
        string extensionPath,
        string extensionId,
        JsonObject manifest,
        CancellationToken cancellationToken)
        => strategy.InstallMode is ChromiumBootstrapInstallMode.ProfileSeeded
            ? WriteChromiumProfileExtensionSettingsAsync(profilePath, extensionPath, extensionId, manifest, cancellationToken)
            : RemoveChromiumProfileExtensionSettingsAsync(profilePath, extensionId, cancellationToken);

    private static BridgeBootstrapPlan CreateBridgeBootstrapPlan(
        string SessionId,
        string BrowserFamily,
        string ExtensionVersion,
        ChromiumBootstrapStrategy Strategy,
        string Host,
        int Port,
        string? TransportUrl,
        int ManagedDeliveryPort,
        bool ManagedDeliveryRequiresCertificateBypass,
        BridgeManagedDeliveryTrustDiagnostics ManagedDeliveryTrustDiagnostics,
        string Secret,
        string LaunchBinaryPath,
        string LocalExtensionPath,
        string ExtensionId,
        string BundledConfigPath,
        string ManagedStorageConfigPath,
        string LocalStorageConfigPath,
        string ManagedPolicyPath,
        string ManagedPolicyPublishPath,
        BridgeManagedPolicyPublishDiagnostics ManagedPolicyDiagnostics,
        string ManagedUpdateUrl,
        string ManagedPackageUrl,
        string ManagedPackageArtifactPath,
        string DiscoveryUrl,
        TimeSpan ConnectionTimeout)
        => new(
            SessionId,
            BrowserFamily,
            ExtensionVersion,
            Strategy,
            Host,
            Port,
            TransportUrl,
            ManagedDeliveryPort,
            ManagedDeliveryRequiresCertificateBypass,
            ManagedDeliveryTrustDiagnostics,
            Secret,
            LaunchBinaryPath,
            LocalExtensionPath,
            ExtensionId,
            BundledConfigPath,
            ManagedStorageConfigPath,
            LocalStorageConfigPath,
            ManagedPolicyPath,
            ManagedPolicyPublishPath,
            ManagedPolicyDiagnostics,
            ManagedUpdateUrl,
            ManagedPackageUrl,
            ManagedPackageArtifactPath,
            DiscoveryUrl,
            ConnectionTimeout);

    private static BridgeBootstrapPlan CreateMaterializedBridgeBootstrapPlan(
        BridgeBootstrapPreparation preparation,
        ChromiumBootstrapStrategy strategy,
        string sessionId,
        string localExtensionPath,
        int port,
        BridgeBootstrapMaterializedArtifacts materializedArtifacts)
        => CreateBridgeBootstrapPlan(
            SessionId: sessionId,
            BrowserFamily: preparation.BrowserFamily,
            ExtensionVersion: preparation.ExtensionVersion,
            Strategy: strategy,
            Host: preparation.Settings.Host,
            Port: port,
            TransportUrl: ResolveTransportUrl(strategy, preparation.Settings),
            ManagedDeliveryPort: materializedArtifacts.ManagedDeliveryPort,
            ManagedDeliveryRequiresCertificateBypass: preparation.Settings.ManagedDeliveryRequiresCertificateBypass,
            ManagedDeliveryTrustDiagnostics: preparation.Settings.ManagedDeliveryTrustDiagnostics,
            Secret: preparation.Settings.Secret,
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: localExtensionPath,
            ExtensionId: materializedArtifacts.Delivery.ExtensionId,
            BundledConfigPath: materializedArtifacts.Config.BundledConfigPath,
            ManagedStorageConfigPath: materializedArtifacts.Config.ManagedStorageConfigPath,
            LocalStorageConfigPath: materializedArtifacts.Config.LocalStorageConfigPath,
            ManagedPolicyPath: materializedArtifacts.Delivery.ManagedPolicy.PolicyPath,
            ManagedPolicyPublishPath: materializedArtifacts.Delivery.ManagedPolicy.PublishPath,
            ManagedPolicyDiagnostics: materializedArtifacts.Delivery.ManagedPolicy.Diagnostics,
            ManagedUpdateUrl: materializedArtifacts.Delivery.ManagedPolicy.UpdateUrl,
            ManagedPackageUrl: materializedArtifacts.Delivery.ManagedPolicy.PackageUrl,
            ManagedPackageArtifactPath: materializedArtifacts.Delivery.Package.PackagePath,
            DiscoveryUrl: BuildDiscoveryUrl(preparation.Settings.Host, port),
            ConnectionTimeout: preparation.Settings.BootstrapTimeout);

    private static async ValueTask<(string PublishPath, BridgeManagedPolicyPublishDiagnostics Diagnostics)> PublishManagedPolicyAsync(
        WebBrowserProfile profile,
        ChromiumBootstrapStrategy strategy,
        string localPolicyPath,
        string policyJson,
        CancellationToken cancellationToken)
    {
        if (strategy.InstallMode is not ChromiumBootstrapInstallMode.SystemManagedPolicy)
            return (localPolicyPath, BridgeManagedPolicyPublishDiagnostics.ProfileLocal(localPolicyPath));

        var systemPolicyPath = ResolveLinuxSystemManagedPolicyPath(profile);
        if (string.IsNullOrWhiteSpace(systemPolicyPath))
            return (localPolicyPath, BridgeManagedPolicyPublishDiagnostics.ProfileLocal(localPolicyPath));

        var primaryMethodName = ResolveLinuxSystemManagedPolicyMethodName(profile);
        var legacyMethodName = ResolveLinuxLegacySystemManagedPolicyMethodName(profile);

        var diagnostics = await PublishManagedPolicyFileAsync(
            localPolicyPath,
            systemPolicyPath,
            policyJson,
            primaryMethodName,
            cancellationToken).ConfigureAwait(false);

        var legacyDiagnostics = await PublishLegacyManagedPolicyAliasIfPresentAsync(profile, strategy, localPolicyPath, policyJson, legacyMethodName, cancellationToken).ConfigureAwait(false);
        return (systemPolicyPath, MergeManagedPolicyDiagnostics(diagnostics, legacyDiagnostics));
    }

    private static string? ResolveTransportUrl(ChromiumBootstrapStrategy strategy, BridgeSettings settings)
    {
        if (strategy.TransportMode is not ChromiumBootstrapTransportMode.SecureWebSocket
            || settings.SecureTransportPort <= 0)
        {
            return null;
        }

        return BuildSecureTransportUrl(settings.Host, settings.SecureTransportPort, settings.Secret);
    }

    private static async ValueTask<BridgeManagedPolicyPublishDiagnostics?> PublishLegacyManagedPolicyAliasIfPresentAsync(
        WebBrowserProfile profile,
        ChromiumBootstrapStrategy strategy,
        string localPolicyPath,
        string policyJson,
        string methodName,
        CancellationToken cancellationToken)
    {
        if (strategy.InstallMode is not ChromiumBootstrapInstallMode.SystemManagedPolicy)
            return null;

        var legacyPolicyPath = ResolveLegacyLinuxSystemManagedPolicyPath(profile);
        if (string.IsNullOrWhiteSpace(legacyPolicyPath))
            return null;

        var diagnostics = await PublishManagedPolicyFileAsync(
            localPolicyPath,
            legacyPolicyPath,
            policyJson,
            methodName,
            cancellationToken).ConfigureAwait(false);

        if (string.Equals(diagnostics.Status, "system-publish-required", StringComparison.Ordinal))
            Observe(new InvalidOperationException($"Не удалось синхронизировать legacy managed policy alias '{legacyPolicyPath}': {diagnostics.Detail}"));

        return diagnostics;
    }

    internal static BridgeManagedPolicyPublishDiagnostics MergeManagedPolicyDiagnostics(
        BridgeManagedPolicyPublishDiagnostics primaryDiagnostics,
        BridgeManagedPolicyPublishDiagnostics? legacyDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(primaryDiagnostics);

        if (legacyDiagnostics is null)
            return primaryDiagnostics;

        if (string.Equals(primaryDiagnostics.Status, "system-publish-required", StringComparison.Ordinal))
            return primaryDiagnostics;

        if (!string.Equals(legacyDiagnostics.Status, "system-publish-required", StringComparison.Ordinal))
            return primaryDiagnostics;

        var detail = string.IsNullOrWhiteSpace(legacyDiagnostics.Detail)
            ? $"Основной managed policy опубликован в '{primaryDiagnostics.TargetPath}', но синхронизация legacy alias завершилась без detail"
            : $"Основной managed policy опубликован в '{primaryDiagnostics.TargetPath}', но синхронизация legacy alias завершилась ошибкой: {legacyDiagnostics.Detail}";

        return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
            legacyDiagnostics.Method,
            legacyDiagnostics.TargetPath,
            detail);
    }

    private static async ValueTask<BridgeManagedPolicyPublishDiagnostics> PublishManagedPolicyFileAsync(
        string localPolicyPath,
        string targetPolicyPath,
        string policyJson,
        string methodName,
        CancellationToken cancellationToken)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(targetPolicyPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            await File.WriteAllTextAsync(targetPolicyPath, policyJson, cancellationToken).ConfigureAwait(false);
            return BridgeManagedPolicyPublishDiagnostics.SystemPublished(methodName + "-direct", targetPolicyPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Observe(ex);
            return TryPublishManagedPolicyWithSudo(localPolicyPath, targetPolicyPath, FormatExceptionDetail(ex), methodName);
        }
    }

    private static BridgeManagedPolicyPublishDiagnostics TryPublishManagedPolicyWithSudo(string localPolicyPath, string systemPolicyPath, string initialFailureDetail, string methodName)
    {
        var password = GetRootPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
                methodName,
                systemPolicyPath,
                $"{initialFailureDetail} Root password не задан через ATOM_WEBDRIVER_ROOT_PASSWORD");
        }

        if (!IsCommandAvailable("sudo"))
        {
            return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
                methodName,
                systemPolicyPath,
                $"{initialFailureDetail} Утилита sudo недоступна");
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-managed-policy-{Guid.NewGuid():N}.sh");

        try
        {
            WriteLinuxSystemManagedPolicyScript(scriptPath, localPolicyPath, systemPolicyPath);
            return RunLinuxSystemManagedPolicyScript(scriptPath, password, systemPolicyPath, initialFailureDetail, methodName);
        }
        catch (Exception ex)
        {
            Observe(ex);
            return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
                methodName,
                systemPolicyPath,
                $"{initialFailureDetail} {FormatExceptionDetail(ex)}");
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private static string? ResolveLinuxSystemManagedPolicyDirectory(WebBrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!OperatingSystem.IsLinux() || profile.Channel is not WebBrowserChannel.Stable)
            return null;

        return profile switch
        {
            EdgeProfile => LinuxEdgeManagedPolicyDirectory,
            BraveProfile => LinuxBraveManagedPolicyDirectory,
            OperaProfile => LinuxOperaManagedPolicyDirectory,
            VivaldiProfile => LinuxVivaldiManagedPolicyDirectory,
            ChromeProfile chrome when chrome.GetType() == typeof(ChromeProfile) => LinuxChromeManagedPolicyDirectory,
            _ => null,
        };
    }

    private static string ResolveLinuxSystemManagedPolicyMethodName(WebBrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile switch
        {
            EdgeProfile => "linux-edge-system-policy",
            BraveProfile => "linux-brave-system-policy",
            OperaProfile => "linux-opera-system-policy",
            VivaldiProfile => "linux-vivaldi-system-policy",
            ChromeProfile chrome when chrome.GetType() == typeof(ChromeProfile) => "linux-chrome-system-policy",
            _ => "linux-chromium-system-policy",
        };
    }

    private static string ResolveLinuxLegacySystemManagedPolicyMethodName(WebBrowserProfile profile)
        => profile switch
        {
            null => throw new ArgumentNullException(nameof(profile)),
            _ => string.Concat(ResolveLinuxSystemManagedPolicyMethodName(profile).Replace("-system-policy", string.Empty, StringComparison.Ordinal), "-legacy-system-policy"),
        };

    private static bool IsLinuxChromiumProfile(WebBrowserProfile profile)
        => OperatingSystem.IsLinux()
            && profile is ChromeProfile;

    private static void WriteLinuxSystemManagedPolicyScript(string scriptPath, string localPolicyPath, string systemPolicyPath)
    {
        var directoryPath = Path.GetDirectoryName(systemPolicyPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог системной managed policy");

        var script = $$"""
            #!/bin/sh
            set -e
            mkdir -p '{{EscapeForShell(directoryPath)}}'
            install -m 0644 '{{EscapeForShell(localPolicyPath)}}' '{{EscapeForShell(systemPolicyPath)}}'
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static BridgeManagedPolicyPublishDiagnostics RunLinuxSystemManagedPolicyScript(string scriptPath, string password, string systemPolicyPath, string initialFailureDetail, string methodName)
    {
        using var process = StartSudoProcess(scriptPath);
        if (process is null)
        {
            return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
                methodName + "-sudo",
                systemPolicyPath,
                $"{initialFailureDetail} Не удалось запустить sudo процесс");
        }

        process.StandardInput.WriteLine(password);
        process.StandardInput.Flush();
        process.StandardInput.Close();

        if (!process.WaitForExit(milliseconds: 20000))
        {
            TryKillProcess(process);
            return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
                methodName + "-sudo",
                systemPolicyPath,
                $"{initialFailureDetail} Превышен таймаут публикации системной managed policy");
        }

        if (process.ExitCode == 0)
            return BridgeManagedPolicyPublishDiagnostics.SystemPublished(methodName + "-sudo", systemPolicyPath);

        var standardError = process.StandardError.ReadToEnd().Trim();
        var detail = string.IsNullOrWhiteSpace(standardError)
            ? initialFailureDetail
            : $"{initialFailureDetail} {standardError}";

        return BridgeManagedPolicyPublishDiagnostics.SystemPublishRequired(
            methodName + "-sudo",
            systemPolicyPath,
            detail);
    }

    private static bool IsCommandAvailable(string command)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(segment, command)))
                    return true;
            }
            catch (Exception ex)
            {
                Observe(ex);
                // Ignore malformed PATH segments.
            }
        }

        return false;
    }

    private static string? GetRootPassword()
    {
        var password = Environment.GetEnvironmentVariable(RootPasswordEnv);
        if (!string.IsNullOrWhiteSpace(password))
            return password;

        password = Environment.GetEnvironmentVariable(LegacyRootPasswordEnv);
        return string.IsNullOrWhiteSpace(password) ? null : password;
    }

    private static Process? StartSudoProcess(string scriptPath)
    {
        var startInfo = new ProcessStartInfo("sudo")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-S");
        startInfo.ArgumentList.Add("/bin/sh");
        startInfo.ArgumentList.Add(scriptPath);
        return Process.Start(startInfo);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Observe(ex);
            // Ignore cleanup failures.
        }
    }

    private static string EscapeForShell(string value)
        => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private static string FormatExceptionDetail(Exception ex)
        => ex.ToString();

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Observe(ex);
            // Ignore best-effort cleanup failures.
        }
    }

    private static void Observe(Exception ex)
        => Trace.TraceWarning(ex.ToString());

    private static string BuildDiscoveryUrl(string host, int port)
        => string.Concat(
            "http://",
            host,
            ":",
            port.ToString(CultureInfo.InvariantCulture),
            "/");

    private static string AddChromiumExtensionKey(string manifestPath, JsonObject manifest, ChromiumExtensionKeyArtifacts extensionKey)
    {
        manifest["key"] = extensionKey.PublicKeyBase64;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        return extensionKey.ExtensionId;
    }

    private static async ValueTask WriteChromiumProfileExtensionSettingsAsync(
        string profilePath,
        string extensionPath,
        string extensionId,
        JsonObject manifest,
        CancellationToken cancellationToken)
    {
        var preferencesPath = Path.Combine(profilePath, "Default", "Preferences");
        var preferences = JsonNode.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken).ConfigureAwait(false))?.AsObject()
            ?? throw new JsonException("Не удалось разобрать Chromium Preferences");

        var extensions = GetOrCreateObject(preferences, "extensions");
        var ui = GetOrCreateObject(extensions, "ui");
        ui["developer_mode"] = true;

        var settings = GetOrCreateObject(extensions, "settings");
        settings[extensionId] = BuildExtensionSettings(extensionPath, manifest);

        await File.WriteAllTextAsync(preferencesPath, preferences.ToJsonString(), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask RemoveChromiumProfileExtensionSettingsAsync(
        string profilePath,
        string extensionId,
        CancellationToken cancellationToken)
    {
        var preferencesPath = Path.Combine(profilePath, "Default", "Preferences");
        if (!File.Exists(preferencesPath))
            return;

        var preferences = JsonNode.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken).ConfigureAwait(false))?.AsObject();
        if (preferences is null || preferences["extensions"] is not JsonObject extensions)
            return;

        if (extensions["settings"] is JsonObject settings)
        {
            settings.Remove(extensionId);
            if (settings.Count == 0)
                extensions.Remove("settings");
        }

        if (extensions["ui"] is JsonObject ui)
        {
            ui.Remove("developer_mode");
            if (ui.Count == 0)
                extensions.Remove("ui");
        }

        if (extensions.Count == 0)
            preferences.Remove("extensions");

        await File.WriteAllTextAsync(preferencesPath, preferences.ToJsonString(), cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject BuildExtensionSettings(string extensionPath, JsonNode manifest)
    {
        var installTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var apiPermissions = ReadStringArray(manifest["permissions"]);
        var hostPermissions = ReadStringArray(manifest["host_permissions"]);

        return new JsonObject
        {
            ["active_permissions"] = new JsonObject
            {
                ["api"] = ToJsonArray(apiPermissions),
                ["manifest_permissions"] = new JsonArray(),
                ["explicit_host"] = ToJsonArray(hostPermissions),
            },
            ["creation_flags"] = 1,
            ["from_webstore"] = false,
            ["granted_permissions"] = new JsonObject
            {
                ["api"] = ToJsonArray(apiPermissions),
                ["manifest_permissions"] = new JsonArray(),
                ["explicit_host"] = ToJsonArray(hostPermissions),
            },
            ["install_time"] = installTime,
            ["location"] = 4,
            ["manifest"] = manifest.DeepClone(),
            ["path"] = extensionPath,
            ["state"] = 1,
        };
    }

    private static string[] ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
            return [];

        return array
            .Select(static item => item?.GetValue<string>())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
        => new(values.Select(static value => JsonValue.Create(value)).ToArray());

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static ChromiumExtensionKeyArtifacts GetOrCreateExtensionKey(string profilePath)
    {
        var keyDirectoryPath = Path.Combine(profilePath, ".bridge-extension");
        Directory.CreateDirectory(keyDirectoryPath);

        var publicKeyPath = Path.Combine(keyDirectoryPath, "extension-key.der");
        var privateKeyPath = Path.Combine(keyDirectoryPath, "extension-key.pem");
        byte[] derBytes;

        if (File.Exists(publicKeyPath) && File.Exists(privateKeyPath))
        {
            derBytes = File.ReadAllBytes(publicKeyPath);
        }
        else
        {
            using var rsa = RSA.Create(2048);
            derBytes = rsa.ExportSubjectPublicKeyInfo();
            File.WriteAllBytes(publicKeyPath, derBytes);
            File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new ChromiumExtensionKeyArtifacts(
            PublicKeyBase64: Convert.ToBase64String(derBytes),
            ExtensionId: ComputeExtensionId(derBytes),
            PrivateKeyPath: privateKeyPath);
    }

    private static async ValueTask<BridgePackagedExtensionArtifacts> PackageChromiumExtensionAsync(
        string profilePath,
        string extensionPath,
        string privateKeyPath,
        string expectedExtensionId,
        CancellationToken cancellationToken)
    {
        var scriptPath = ResolveChromiumCrxPackagerScriptPath();
        var packageDirectoryPath = Path.Combine(profilePath, "managed-delivery");
        Directory.CreateDirectory(packageDirectoryPath);

        var crxPath = Path.Combine(packageDirectoryPath, "atom-webdriver-extension.crx");
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(extensionPath);
        startInfo.ArgumentList.Add(privateKeyPath);
        startInfo.ArgumentList.Add(crxPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить упаковщик Chromium CRX");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Упаковщик Chromium CRX завершился с кодом {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {standardError}");

        if (!File.Exists(crxPath))
            throw new FileNotFoundException("Упаковщик Chromium CRX не создал выходной файл", crxPath);

        using var metadata = JsonDocument.Parse(standardOutput);
        var packagedExtensionId = metadata.RootElement.GetProperty("extensionId").GetString();
        if (!string.Equals(packagedExtensionId, expectedExtensionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Упакованный CRX вернул другой extension id: {packagedExtensionId}");

        return new BridgePackagedExtensionArtifacts(
            PackagePath: crxPath,
            PackageBytes: await File.ReadAllBytesAsync(crxPath, cancellationToken).ConfigureAwait(false));
    }

    private static string ResolveChromiumCrxPackagerScriptPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var directCandidate = Path.Combine(directory.FullName, "ExtensionRuntime", "scripts", "package-bridge-extension.mjs");
            if (File.Exists(directCandidate))
                return directCandidate;

            var workspaceCandidate = Path.Combine(directory.FullName, "Framework", "Atom.Net.Browsing.WebDriver", "ExtensionRuntime", "scripts", "package-bridge-extension.mjs");
            if (File.Exists(workspaceCandidate))
                return workspaceCandidate;
        }

        throw new FileNotFoundException("Не найден скрипт упаковки Chromium CRX для bridge extension");
    }

    private static string ComputeExtensionId(byte[] derPublicKey)
    {
        var hash = SHA256.HashData(derPublicKey);
        using var builder = new ValueStringBuilder(32);

        for (var index = 0; index < 16; index++)
        {
            builder.Append((char)('a' + (hash[index] >> 4)));
            builder.Append((char)('a' + (hash[index] & 0x0F)));
        }

        return builder.ToString();
    }

    private static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private static void CopyDirectoryRecursive(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.EnumerateFiles(sourcePath))
            File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)), overwrite: true);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath))
            CopyDirectoryRecursive(directoryPath, Path.Combine(destinationPath, Path.GetFileName(directoryPath)));
    }
}
