using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

internal static class ProfileMaterialization
{
    internal static async ValueTask<ProfileMaterializationResult> MaterializeAsync(
        WebBrowserSettings settings,
        BridgeBootstrapPreparation? bridgeBootstrapPreparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (settings.Profile is not { } profile)
        {
            settings.Logger?.LogProfileMaterializationSkipped();
            return new ProfileMaterializationResult(MaterializedProfilePath: null, BridgeBootstrap: null);
        }

        if (!profile.IsInstalled)
            throw new FileNotFoundException($"Не найден бинарный файл браузера для канала '{profile.Channel}'", profile.BinaryPath);

        var shouldCleanup = string.IsNullOrWhiteSpace(profile.Path);
        var profilePath = shouldCleanup
            ? IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N"))
            : profile.Path;

        settings.Logger?.LogProfileMaterializationStarted(profile.Channel.ToString(), shouldCleanup, profilePath);

        Directory.CreateDirectory(profilePath);
        profile.Path = profilePath;

        var automationPreset = ProfileAutomationPresets.Create(profile, settings, profilePath, bridgeBootstrapPreparation is not null);
        await WriteAutomationFilesAsync(profilePath, automationPreset.Files, cancellationToken).ConfigureAwait(false);
        settings.Logger?.LogProfileAutomationFilesWritten(profilePath, automationPreset.Files.Count);

        BridgeBootstrapPlan? bridgeBootstrap = null;
        if (bridgeBootstrapPreparation is not null)
            bridgeBootstrap = await BridgeExtensionBootstrap.MaterializeAsync(profilePath, profile, bridgeBootstrapPreparation, cancellationToken).ConfigureAwait(false);

        LogBootstrapStrategyDiagnostics(settings.Logger, bridgeBootstrap);
        LogManagedPolicyDiagnostics(settings.Logger, bridgeBootstrap);

        var manifestPath = IOPath.Combine(profilePath, "profile.json");
        var manifest = BuildManifest(settings, profilePath, shouldCleanup, automationPreset, bridgeBootstrap);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), cancellationToken).ConfigureAwait(false);
        settings.Logger?.LogProfileManifestWritten(manifestPath);

        var returnedPath = shouldCleanup ? profilePath : "<persistent>";
        settings.Logger?.LogProfileMaterializationCompleted(returnedPath);

        return new ProfileMaterializationResult(
            MaterializedProfilePath: shouldCleanup ? profilePath : null,
            BridgeBootstrap: bridgeBootstrap);
    }

    private static void LogManagedPolicyDiagnostics(ILogger? logger, BridgeBootstrapPlan? bridgeBootstrap)
    {
        if (logger is null || bridgeBootstrap is null)
            return;

        if (string.Equals(bridgeBootstrap.ManagedPolicyDiagnostics.Status, "system-publish-required", StringComparison.Ordinal))
        {
            logger.LogProfileManagedPolicyPublishRequired(
                bridgeBootstrap.ManagedPolicyPublishPath,
                bridgeBootstrap.ManagedPolicyDiagnostics.Method,
                bridgeBootstrap.ManagedPolicyDiagnostics.Detail,
                bridgeBootstrap.ManagedPolicyDiagnostics.TargetPath);
            return;
        }

        logger.LogProfileManagedPolicyPublished(
            bridgeBootstrap.ManagedPolicyPublishPath,
            bridgeBootstrap.ManagedPolicyDiagnostics.Status,
            bridgeBootstrap.ManagedPolicyDiagnostics.Method,
            bridgeBootstrap.ManagedPolicyDiagnostics.Detail);
    }

    private static void LogBootstrapStrategyDiagnostics(ILogger? logger, BridgeBootstrapPlan? bridgeBootstrap)
    {
        if (logger is null || bridgeBootstrap is null)
            return;

        var transportUrlScheme = Uri.TryCreate(bridgeBootstrap.TransportUrl, UriKind.Absolute, out var transportUri)
            ? transportUri.Scheme
            : "none";

        logger.LogProfileBootstrapStrategyResolved(
            bridgeBootstrap.Strategy.InstallMode.ToString(),
            bridgeBootstrap.Strategy.TransportMode.ToString(),
            bridgeBootstrap.Strategy.UseCommandLineExtensionLoad,
            !string.IsNullOrWhiteSpace(bridgeBootstrap.TransportUrl),
            transportUrlScheme);
    }

    private static JsonObject BuildManifest(
        WebBrowserSettings settings,
        string profilePath,
        bool shouldCleanup,
        BrowserAutomationPreset automationPreset,
        BridgeBootstrapPlan? bridgeBootstrap)
    {
        var profile = settings.Profile!;
        var manifest = new JsonObject
        {
            ["kind"] = "atom.webdriver.profile-manifest",
            ["schemaVersion"] = 2,
            ["browser"] = BuildBrowserManifest(profile, automationPreset, bridgeBootstrap),
            ["profile"] = BuildProfileManifest(profilePath, shouldCleanup),
            ["launch"] = BuildLaunchManifest(settings),
        };

        if (settings.Device is { } device)
            manifest["device"] = BuildDeviceManifest(device);

        if (bridgeBootstrap is not null)
            manifest["bridge"] = BuildBridgeManifest(bridgeBootstrap);

        return manifest;
    }

    private static JsonObject BuildBridgeManifest(BridgeBootstrapPlan bridgeBootstrap)
    {
        var manifest = new JsonObject
        {
            ["host"] = bridgeBootstrap.Host,
            ["port"] = bridgeBootstrap.Port,
            ["managedPort"] = bridgeBootstrap.ManagedDeliveryPort,
            ["managedDeliveryRequiresCertificateBypass"] = bridgeBootstrap.ManagedDeliveryRequiresCertificateBypass,
            ["managedDeliveryTrust"] = new JsonObject
            {
                ["status"] = bridgeBootstrap.ManagedDeliveryTrustDiagnostics.Status,
                ["method"] = bridgeBootstrap.ManagedDeliveryTrustDiagnostics.Method,
                ["detail"] = bridgeBootstrap.ManagedDeliveryTrustDiagnostics.Detail,
            },
            ["sessionId"] = bridgeBootstrap.SessionId,
            ["browserFamily"] = bridgeBootstrap.BrowserFamily,
            ["extensionVersion"] = bridgeBootstrap.ExtensionVersion,
            ["strategy"] = new JsonObject
            {
                ["installMode"] = bridgeBootstrap.Strategy.InstallMode.ToString(),
                ["transportMode"] = bridgeBootstrap.Strategy.TransportMode.ToString(),
                ["useCommandLineExtensionLoad"] = bridgeBootstrap.Strategy.UseCommandLineExtensionLoad,
            },
            ["extensionPath"] = bridgeBootstrap.LocalExtensionPath,
            ["extensionId"] = bridgeBootstrap.ExtensionId,
            ["bundledConfigPath"] = bridgeBootstrap.BundledConfigPath,
            ["managedStorageConfigPath"] = bridgeBootstrap.ManagedStorageConfigPath,
            ["localStorageConfigPath"] = bridgeBootstrap.LocalStorageConfigPath,
            ["managedPolicyPath"] = bridgeBootstrap.ManagedPolicyPath,
            ["managedPolicyPublishPath"] = bridgeBootstrap.ManagedPolicyPublishPath,
            ["managedPolicyDiagnostics"] = new JsonObject
            {
                ["status"] = bridgeBootstrap.ManagedPolicyDiagnostics.Status,
                ["method"] = bridgeBootstrap.ManagedPolicyDiagnostics.Method,
                ["detail"] = bridgeBootstrap.ManagedPolicyDiagnostics.Detail,
                ["targetPath"] = bridgeBootstrap.ManagedPolicyDiagnostics.TargetPath,
                ["requiresSystemPath"] = bridgeBootstrap.ManagedPolicyDiagnostics.RequiresSystemPath,
            },
            ["managedUpdateUrl"] = bridgeBootstrap.ManagedUpdateUrl,
            ["managedPackageUrl"] = bridgeBootstrap.ManagedPackageUrl,
            ["managedPackageArtifactPath"] = bridgeBootstrap.ManagedPackageArtifactPath,
            ["discoveryUrl"] = bridgeBootstrap.DiscoveryUrl,
            ["requestTimeoutMs"] = (int)bridgeBootstrap.ConnectionTimeout.TotalMilliseconds,
        };

        if (!string.IsNullOrWhiteSpace(bridgeBootstrap.TransportUrl))
            manifest["transportUrl"] = bridgeBootstrap.TransportUrl;

        if (!string.IsNullOrWhiteSpace(bridgeBootstrap.LaunchBinaryPath))
            manifest["launchBinaryPath"] = bridgeBootstrap.LaunchBinaryPath;

        return manifest;
    }

    private static JsonObject BuildBrowserManifest(
        WebBrowserProfile profile,
        BrowserAutomationPreset automationPreset,
        BridgeBootstrapPlan? bridgeBootstrap)
    {
        var manifest = new JsonObject
        {
            ["name"] = GetBrowserName(profile),
            ["family"] = automationPreset.Family,
            ["binaryPath"] = profile.BinaryPath,
            ["channel"] = profile.Channel.ToString(),
            ["automation"] = BuildBrowserAutomationManifest(profile, automationPreset, bridgeBootstrap),
        };

        return manifest;
    }

    private static JsonObject BuildBrowserAutomationManifest(
        WebBrowserProfile profile,
        BrowserAutomationPreset automationPreset,
        BridgeBootstrapPlan? bridgeBootstrap)
    {
        var manifest = new JsonObject
        {
            ["preferenceFile"] = automationPreset.PreferenceFile,
            ["seedFiles"] = automationPreset.SeedFiles,
            ["defaultArguments"] = automationPreset.DefaultArguments,
            ["effectiveArguments"] = BuildEffectiveArguments(profile, automationPreset.EffectiveArguments, bridgeBootstrap),
        };

        if (!string.IsNullOrWhiteSpace(automationPreset.LocalStateFile))
            manifest["localStateFile"] = automationPreset.LocalStateFile;

        if (automationPreset.Preferences is { } preferences)
            manifest["preferences"] = JsonNode.Parse(preferences.ToJsonString());

        if (automationPreset.LocalState is { } localState)
            manifest["localState"] = JsonNode.Parse(localState.ToJsonString());

        return manifest;
    }

    private static JsonArray BuildEffectiveArguments(
        WebBrowserProfile profile,
        JsonArray effectiveArguments,
        BridgeBootstrapPlan? bridgeBootstrap)
    {
        var arguments = new JsonArray(effectiveArguments.Select(static argument => argument?.DeepClone()).ToArray());
        foreach (var launchArgument in BridgeExtensionBootstrap.GetLaunchArguments(profile, bridgeBootstrap))
        {
            if (arguments.Any(argument => string.Equals(argument?.GetValue<string>(), launchArgument, StringComparison.Ordinal)))
                continue;

            arguments.Add(JsonValue.Create(launchArgument));
        }

        return arguments;
    }

    private static JsonObject BuildProfileManifest(string profilePath, bool shouldCleanup)
        => new()
        {
            ["path"] = profilePath,
            ["mode"] = shouldCleanup ? "temporary" : "persistent",
            ["cleanupOnDispose"] = shouldCleanup,
        };

    private static JsonObject BuildLaunchManifest(WebBrowserSettings settings)
    {
        var manifest = new JsonObject
        {
            ["headless"] = settings.UseHeadlessMode,
            ["incognito"] = settings.UseIncognitoMode,
            ["window"] = new JsonObject
            {
                ["position"] = new JsonObject
                {
                    ["x"] = settings.Position.X,
                    ["y"] = settings.Position.Y,
                },
                ["size"] = new JsonObject
                {
                    ["width"] = settings.Size.Width,
                    ["height"] = settings.Size.Height,
                },
            },
        };

        if (BuildStringArray(settings.Args) is { } arguments)
            manifest["arguments"] = arguments;

        return manifest;
    }

    private static JsonObject BuildDeviceManifest(Device device)
    {
        var manifest = new JsonObject
        {
            ["name"] = device.Name,
            ["identity"] = BuildIdentityManifest(device),
            ["viewport"] = BuildViewportManifest(device),
            ["hardware"] = BuildHardwareManifest(device),
            ["preferences"] = BuildPreferencesManifest(device),
            ["privacy"] = BuildPrivacyManifest(device),
        };

        if (BuildScreenManifest(device.Screen) is { } screen)
            manifest["screen"] = screen;

        if (BuildClientHintsManifest(device.ClientHints) is { } clientHints)
            manifest["clientHints"] = clientHints;

        if (BuildGeolocationManifest(device.Geolocation) is { } geolocation)
            manifest["geolocation"] = geolocation;

        if (BuildNetworkManifest(device.NetworkInfo) is { } network)
            manifest["network"] = network;

        if (BuildWebGlManifest(device.WebGL) is { } webGl)
            manifest["webGl"] = webGl;

        if (BuildWebGlParametersManifest(device.WebGLParams) is { } webGlParameters)
            manifest["webGlParameters"] = webGlParameters;

        if (BuildSpeechManifest(device.SpeechVoices) is { } speech)
            manifest["speech"] = speech;

        if (BuildMediaDevicesManifest(device.VirtualMediaDevices) is { } mediaDevices)
            manifest["mediaDevices"] = mediaDevices;

        return manifest;
    }

    private static JsonObject BuildIdentityManifest(Device device)
    {
        var manifest = new JsonObject();

        AddString(manifest, "userAgent", device.UserAgent);
        AddString(manifest, "platform", device.Platform);
        AddString(manifest, "locale", device.Locale);
        AddString(manifest, "timezone", device.Timezone);

        if (BuildStringArray(device.Languages) is { } languages)
            manifest["languages"] = languages;

        return manifest;
    }

    private static JsonObject BuildViewportManifest(Device device)
        => new()
        {
            ["width"] = device.ViewportSize.Width,
            ["height"] = device.ViewportSize.Height,
            ["deviceScaleFactor"] = device.DeviceScaleFactor,
            ["isMobile"] = device.IsMobile,
            ["hasTouch"] = device.HasTouch,
            ["maxTouchPoints"] = device.MaxTouchPoints,
        };

    private static JsonObject BuildHardwareManifest(Device device)
    {
        var manifest = new JsonObject();

        AddInt32(manifest, "hardwareConcurrency", device.HardwareConcurrency);
        AddDouble(manifest, "deviceMemory", device.DeviceMemory);

        if (device.BatteryCharging.HasValue || device.BatteryLevel.HasValue)
        {
            var battery = new JsonObject();
            AddBoolean(battery, "charging", device.BatteryCharging);
            AddDouble(battery, "level", device.BatteryLevel);
            manifest["battery"] = battery;
        }

        return manifest;
    }

    private static JsonObject BuildPreferencesManifest(Device device)
    {
        var manifest = new JsonObject();

        AddString(manifest, "screenOrientation", device.ScreenOrientation);
        AddString(manifest, "colorScheme", device.ColorScheme);
        AddBoolean(manifest, "reducedMotion", device.ReducedMotion);

        return manifest;
    }

    private static JsonObject BuildPrivacyManifest(Device device)
    {
        var manifest = new JsonObject
        {
            ["intlSpoofing"] = device.IntlSpoofing,
            ["canvasNoise"] = device.CanvasNoise,
            ["audioNoise"] = device.AudioNoise,
            ["fontFiltering"] = device.FontFiltering,
        };

        AddBoolean(manifest, "doNotTrack", device.DoNotTrack);
        AddBoolean(manifest, "globalPrivacyControl", device.GlobalPrivacyControl);
        AddDouble(manifest, "timerPrecisionMilliseconds", device.TimerPrecisionMilliseconds);

        return manifest;
    }

    private static JsonObject? BuildScreenManifest(ScreenSettings? screen)
    {
        if (screen is null)
            return null;

        var manifest = new JsonObject();
        AddInt32(manifest, "width", screen.Width);
        AddInt32(manifest, "height", screen.Height);
        AddInt32(manifest, "availWidth", screen.AvailWidth);
        AddInt32(manifest, "availHeight", screen.AvailHeight);
        AddInt32(manifest, "colorDepth", screen.ColorDepth);
        AddInt32(manifest, "pixelDepth", screen.PixelDepth);
        return manifest;
    }

    private static JsonObject? BuildClientHintsManifest(ClientHintsSettings? clientHints)
    {
        if (clientHints is null)
            return null;

        var manifest = new JsonObject();
        AddString(manifest, "platform", clientHints.Platform);
        AddString(manifest, "platformVersion", clientHints.PlatformVersion);
        AddBoolean(manifest, "mobile", clientHints.Mobile);
        AddString(manifest, "architecture", clientHints.Architecture);
        AddString(manifest, "model", clientHints.Model);
        AddString(manifest, "bitness", clientHints.Bitness);

        if (BuildBrandArray(clientHints.Brands) is { } brands)
            manifest["brands"] = brands;

        if (BuildBrandArray(clientHints.FullVersionList) is { } fullVersionList)
            manifest["fullVersionList"] = fullVersionList;

        return manifest;
    }

    private static JsonObject? BuildGeolocationManifest(GeolocationSettings? geolocation)
    {
        if (geolocation is null)
            return null;

        var manifest = new JsonObject
        {
            ["latitude"] = geolocation.Latitude,
            ["longitude"] = geolocation.Longitude,
        };

        AddDouble(manifest, "accuracy", geolocation.Accuracy);
        return manifest;
    }

    private static JsonObject? BuildNetworkManifest(NetworkInfoSettings? network)
    {
        if (network is null)
            return null;

        var manifest = new JsonObject
        {
            ["effectiveType"] = network.EffectiveType,
            ["rtt"] = network.Rtt,
            ["downlink"] = network.Downlink,
            ["enableDataSaving"] = network.EnableDataSaving,
        };

        AddString(manifest, "type", network.Type);
        return manifest;
    }

    private static JsonObject? BuildWebGlManifest(WebGLSettings? webGl)
    {
        if (webGl is null)
            return null;

        var manifest = new JsonObject();
        AddString(manifest, "vendor", webGl.Vendor);
        AddString(manifest, "renderer", webGl.Renderer);
        AddString(manifest, "unmaskedVendor", webGl.UnmaskedVendor);
        AddString(manifest, "unmaskedRenderer", webGl.UnmaskedRenderer);
        AddString(manifest, "version", webGl.Version);
        AddString(manifest, "shadingLanguageVersion", webGl.ShadingLanguageVersion);
        return manifest;
    }

    private static JsonObject? BuildWebGlParametersManifest(WebGLParamsSettings? webGlParameters)
    {
        if (webGlParameters is null)
            return null;

        var manifest = new JsonObject();
        AddInt32(manifest, "maxTextureSize", webGlParameters.MaxTextureSize);
        AddInt32(manifest, "maxRenderbufferSize", webGlParameters.MaxRenderbufferSize);
        AddInt32(manifest, "maxVaryingVectors", webGlParameters.MaxVaryingVectors);
        AddInt32(manifest, "maxVertexUniformVectors", webGlParameters.MaxVertexUniformVectors);
        AddInt32(manifest, "maxFragmentUniformVectors", webGlParameters.MaxFragmentUniformVectors);

        if (BuildInt32Array(webGlParameters.MaxViewportDims) is { } maxViewportDims)
            manifest["maxViewportDims"] = maxViewportDims;

        return manifest;
    }

    private static JsonObject? BuildSpeechManifest(IEnumerable<SpeechVoiceSettings>? speechVoices)
    {
        if (BuildSpeechVoicesArray(speechVoices) is not { } voices)
            return null;

        return new JsonObject
        {
            ["voices"] = voices,
        };
    }

    private static JsonObject? BuildMediaDevicesManifest(VirtualMediaDevicesSettings? mediaDevices)
    {
        if (mediaDevices is null)
            return null;

        var manifest = new JsonObject
        {
            ["audioInputEnabled"] = mediaDevices.AudioInputEnabled,
            ["audioInputLabel"] = mediaDevices.AudioInputLabel,
            ["videoInputEnabled"] = mediaDevices.VideoInputEnabled,
            ["videoInputLabel"] = mediaDevices.VideoInputLabel,
            ["audioOutputEnabled"] = mediaDevices.AudioOutputEnabled,
            ["audioOutputLabel"] = mediaDevices.AudioOutputLabel,
        };

        AddString(manifest, "audioInputBrowserDeviceId", mediaDevices.AudioInputBrowserDeviceId);
        AddString(manifest, "videoInputBrowserDeviceId", mediaDevices.VideoInputBrowserDeviceId);
        AddString(manifest, "groupId", mediaDevices.GroupId);

        return manifest;
    }

    private static JsonArray? BuildStringArray(IEnumerable<string>? values)
    {
        if (values is null)
            return null;

        var items = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0
            ? null
            : new JsonArray(items.Select(static value => JsonValue.Create(value)).ToArray());
    }

    private static JsonArray? BuildInt32Array(IEnumerable<int>? values)
    {
        if (values is null)
            return null;

        var items = values.ToArray();
        return items.Length == 0
            ? null
            : new JsonArray(items.Select(static value => JsonValue.Create(value)).ToArray());
    }

    private static JsonArray? BuildBrandArray(IEnumerable<ClientHintBrand>? brands)
    {
        if (brands is null)
            return null;

        var items = brands
            .Where(static brand => !string.IsNullOrWhiteSpace(brand.Brand) && !string.IsNullOrWhiteSpace(brand.Version))
            .Select(static brand => (JsonNode)new JsonObject
            {
                ["brand"] = brand.Brand,
                ["version"] = brand.Version,
            })
            .ToArray();

        return items.Length == 0 ? null : new JsonArray(items);
    }

    private static JsonArray? BuildSpeechVoicesArray(IEnumerable<SpeechVoiceSettings>? speechVoices)
    {
        if (speechVoices is null)
            return null;

        var items = speechVoices
            .Where(static voice => !string.IsNullOrWhiteSpace(voice.Name) && !string.IsNullOrWhiteSpace(voice.Lang))
            .Select(static voice =>
            {
                var manifest = new JsonObject
                {
                    ["name"] = voice.Name,
                    ["lang"] = voice.Lang,
                    ["useLocalService"] = voice.UseLocalService,
                    ["isDefault"] = voice.IsDefault,
                };

                AddString(manifest, "voiceUri", voice.VoiceUri?.ToString());
                return (JsonNode)manifest;
            })
            .ToArray();

        return items.Length == 0 ? null : new JsonArray(items);
    }

    private static async ValueTask WriteAutomationFilesAsync(string profilePath, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = IOPath.Combine(profilePath, file.Key.Replace('/', IOPath.DirectorySeparatorChar));
            var directory = IOPath.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, file.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetBrowserName(WebBrowserProfile profile)
    {
        const string suffix = "Profile";
        var typeName = profile.GetType().Name;
        return typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[..^suffix.Length]
            : typeName;
    }

    private static void AddString(JsonObject target, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[propertyName] = value;
    }

    private static void AddInt32(JsonObject target, string propertyName, int? value)
    {
        if (value.HasValue)
            target[propertyName] = value.Value;
    }

    private static void AddDouble(JsonObject target, string propertyName, double? value)
    {
        if (value.HasValue)
            target[propertyName] = value.Value;
    }

    private static void AddBoolean(JsonObject target, string propertyName, bool? value)
    {
        if (value.HasValue)
            target[propertyName] = value.Value;
    }
}