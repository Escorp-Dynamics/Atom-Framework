using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static class ProfileAutomationPresets
{
    internal static BrowserAutomationPreset Create(WebBrowserProfile profile, WebBrowserSettings settings, string profilePath, bool enableManagedChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profilePath);

        var useBrowserHeadlessMode = ShouldUseBrowserHeadlessMode(settings);
        var family = profile is FirefoxProfile ? "firefox" : "chromium";

        settings.Logger?.LogProfileAutomationPresetCreating(profile.Channel.ToString(), family, profilePath);
        settings.Logger?.LogProfileAutomationHeadlessModeResolved(settings.UseHeadlessMode, settings.Display is not null, useBrowserHeadlessMode);

        return profile switch
        {
            FirefoxProfile => CreateFirefoxPreset(settings, profilePath, useBrowserHeadlessMode),
            _ => CreateChromiumPreset(profile, settings, profilePath, useBrowserHeadlessMode, enableManagedChromiumBootstrap),
        };
    }

    private static BrowserAutomationPreset CreateChromiumPreset(WebBrowserProfile profile, WebBrowserSettings settings, string profilePath, bool useBrowserHeadlessMode, bool enableManagedChromiumBootstrap)
    {
        var preferences = BuildChromiumPreferences(profile);
        var localState = BuildChromiumLocalState();
        var defaultArguments = BuildChromiumDefaultArguments(profile, enableManagedChromiumBootstrap);
        var effectiveArguments = BuildChromiumEffectiveArguments(settings, profilePath, defaultArguments, useBrowserHeadlessMode);

        settings.Logger?.LogProfileAutomationChromiumPresetBuilt(profile.Channel.ToString(), defaultArguments.Count, effectiveArguments.Count);

        return new BrowserAutomationPreset
        {
            Family = "chromium",
            PreferenceFile = "Default/Preferences",
            LocalStateFile = "Local State",
            SeedFiles = ToJsonArray(["Default/Preferences", "Local State", "First Run"]),
            DefaultArguments = ToJsonArray(defaultArguments),
            EffectiveArguments = ToJsonArray(effectiveArguments),
            Preferences = preferences,
            LocalState = localState,
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Default/Preferences"] = preferences.ToJsonString(),
                ["Local State"] = localState.ToJsonString(),
                ["First Run"] = string.Empty,
            },
        };
    }

    private static BrowserAutomationPreset CreateFirefoxPreset(WebBrowserSettings settings, string profilePath, bool useBrowserHeadlessMode)
    {
        var preferences = BuildFirefoxPreferences(settings);
        var defaultArguments = BuildFirefoxDefaultArguments();
        var effectiveArguments = BuildFirefoxEffectiveArguments(settings, profilePath, defaultArguments, useBrowserHeadlessMode);

        settings.Logger?.LogProfileAutomationFirefoxPresetBuilt(defaultArguments.Count, effectiveArguments.Count);

        return new BrowserAutomationPreset
        {
            Family = "firefox",
            PreferenceFile = "user.js",
            SeedFiles = ToJsonArray(["user.js"]),
            DefaultArguments = ToJsonArray(defaultArguments),
            EffectiveArguments = ToJsonArray(effectiveArguments),
            Preferences = preferences,
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user.js"] = BuildFirefoxUserJs(preferences),
            },
        };
    }

    private static JsonObject BuildChromiumPreferences(WebBrowserProfile profile)
    {
        var preferences = new JsonObject
        {
            ["session"] = BuildChromiumSessionPreferences(),
            ["browser"] = BuildChromiumBrowserPreferences(),
            ["alternate_error_pages"] = BuildChromiumAlternateErrorPagePreferences(),
            ["autofill"] = BuildChromiumAutofillPreferences(),
            ["background_mode"] = BuildChromiumBackgroundModePreferences(),
            ["credentials_enable_service"] = false,
            ["dns_prefetching"] = BuildChromiumDnsPrefetchPreferences(),
            ["distribution"] = BuildChromiumDistributionPreferences(),
            ["profile"] = BuildChromiumProfilePreferences(),
            ["safebrowsing"] = BuildChromiumSafeBrowsingPreferences(),
            ["search"] = BuildChromiumSearchPreferences(),
            ["signin"] = BuildChromiumSigninPreferences(),
            ["translate"] = BuildChromiumTranslatePreferences(),
            ["first_run_tabs"] = new JsonArray(),
        };

        if (profile is VivaldiProfile)
        {
            preferences["vivaldi"] = new JsonObject
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
                    ["read_pages"] = ToJsonArray([
                        "intro",
                        "account",
                        "import_data",
                        "tracker_and_ad",
                        "personalize",
                        "tabs",
                        "welcome_feature_amount",
                        "mail_setup",
                    ]),
                },
                ["address_bar"] = new JsonObject
                {
                    ["show_bookmarks_menu"] = false,
                },
            };
        }

        return preferences;
    }

    private static JsonObject BuildChromiumSessionPreferences()
        => new()
        {
            ["restore_on_startup"] = 4,
            ["startup_urls"] = new JsonArray(),
        };

    private static JsonObject BuildChromiumBrowserPreferences()
        => new()
        {
            ["has_seen_welcome_page"] = true,
            ["show_hub_popup_on_browser_startup"] = false,
            ["check_default_browser"] = false,
        };

    private static JsonObject BuildChromiumAlternateErrorPagePreferences()
        => new()
        {
            ["enabled"] = false,
        };

    private static JsonObject BuildChromiumAutofillPreferences()
        => new()
        {
            ["credit_card_enabled"] = false,
            ["profile_enabled"] = false,
        };

    private static JsonObject BuildChromiumBackgroundModePreferences()
        => new()
        {
            ["enabled"] = false,
        };

    private static JsonObject BuildChromiumDnsPrefetchPreferences()
        => new()
        {
            ["enabled"] = false,
        };

    private static JsonObject BuildChromiumDistributionPreferences()
        => new()
        {
            ["skip_first_run_ui"] = true,
        };

    private static JsonObject BuildChromiumProfilePreferences()
        => new()
        {
            ["password_manager_enabled"] = false,
            ["password_manager_leak_detection"] = false,
        };

    private static JsonObject BuildChromiumSafeBrowsingPreferences()
        => new()
        {
            ["enabled"] = false,
            ["enhanced"] = false,
        };

    private static JsonObject BuildChromiumSearchPreferences()
        => new()
        {
            ["suggest_enabled"] = false,
        };

    private static JsonObject BuildChromiumSigninPreferences()
        => new()
        {
            ["allowed_on_next_startup"] = false,
        };

    private static JsonObject BuildChromiumTranslatePreferences()
        => new()
        {
            ["enabled"] = false,
        };

    private static JsonObject BuildChromiumLocalState()
        => new()
        {
            ["fre"] = new JsonObject
            {
                ["has_user_seen_fre"] = true,
            },
        };

    private static List<string> BuildChromiumDefaultArguments(WebBrowserProfile profile, bool enableManagedChromiumBootstrap)
    {
        List<string> arguments =
        [
            "--disable-breakpad",
            "--disable-client-side-phishing-detection",
            "--disable-default-apps",
            "--disable-domain-reliability",
            "--disable-search-engine-choice-screen",
            "--disable-sync",
            "--metrics-recording-only",
            "--no-pings",
            "--password-store=basic",
        ];

        if (!enableManagedChromiumBootstrap)
        {
            arguments.Add("--disable-background-networking");
            arguments.Add("--disable-component-update");
        }

        MergeCsvArgument(arguments, "--disable-features=", [
            "AutofillServerCommunication",
            "CertificateTransparencyComponentUpdater",
            "GlobalMediaControls",
            "InterestFeedContentSuggestions",
            "MediaRouter",
            "OptimizationHints",
            "PaintHolding",
            "Translate",
        ]);

        if (profile is EdgeProfile)
        {
            MergeCsvArgument(arguments, "--disable-features=", [
                "msEdgeFRE",
                "msEdgeFREOnboarding",
                "msEdgeNewTabPage",
            ]);
        }

        return arguments;
    }

    private static List<string> BuildChromiumEffectiveArguments(WebBrowserSettings settings, string profilePath, IEnumerable<string> defaultArguments, bool useBrowserHeadlessMode)
    {
        List<string> arguments =
        [
            $"--user-data-dir={profilePath}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-timer-throttling",
            "--extension-manifest-v2-availability=2",
        ];

        foreach (var argument in defaultArguments)
            AddChromiumArgument(arguments, argument);

        if (useBrowserHeadlessMode)
            AddChromiumArgument(arguments, "--headless=new");

        if (settings.UseIncognitoMode)
            AddChromiumArgument(arguments, "--incognito");

        if (TryResolveProxyArgument(settings.Proxy) is { } proxyArgument)
        {
            AddChromiumArgument(arguments, proxyArgument, "--proxy-server=");

            if (Uri.TryCreate(proxyArgument["--proxy-server=".Length..], UriKind.Absolute, out var proxyUri))
                settings.Logger?.LogProfileAutomationChromiumProxyApplied(proxyUri.Scheme, proxyUri.Host, proxyUri.Port);
        }

        if (TryResolveChromiumLanguageArgument(settings) is { } languageArgument)
            AddChromiumArgument(arguments, languageArgument, "--lang=");

        if (TryResolveChromiumUserAgentArgument(settings) is { } userAgentArgument)
            AddChromiumArgument(arguments, userAgentArgument, "--user-agent=");

        foreach (var argument in NormalizeArguments(settings.Args))
            AddChromiumArgument(arguments, argument);

        return arguments;
    }

    private static JsonObject BuildFirefoxPreferences(WebBrowserSettings settings)
    {
        var preferences = new JsonObject
        {
            ["xpinstall.signatures.required"] = false,
            ["extensions.autoDisableScopes"] = 0,
            ["extensions.enabledScopes"] = 15,
            ["app.normandy.first_run"] = false,
            ["app.shield.optoutstudies.enabled"] = false,
            ["browser.startup.homepage_override.mstone"] = "ignore",
            ["browser.shell.checkDefaultBrowser"] = false,
            ["browser.discovery.enabled"] = false,
            ["browser.newtabpage.enabled"] = false,
            ["browser.newtabpage.activity-stream.feeds.section.topstories"] = false,
            ["browser.newtabpage.activity-stream.feeds.snippets"] = false,
            ["browser.newtabpage.activity-stream.feeds.topsites"] = false,
            ["browser.pocket.enabled"] = false,
            ["datareporting.policy.dataSubmissionEnabled"] = false,
            ["network.dns.disablePrefetch"] = true,
            ["network.prefetch-next"] = false,
            ["toolkit.telemetry.enabled"] = false,
            ["toolkit.telemetry.reportingpolicy.firstRun"] = false,
            ["toolkit.telemetry.unified"] = false,
            ["layers.acceleration.disabled"] = true,
            ["layers.gpu-process.enabled"] = false,
            ["gfx.webrender.all"] = false,
            ["gfx.canvas.accelerated"] = false,
            ["widget.dmabuf.force-disabled"] = true,
            ["media.ffmpeg.vaapi.enabled"] = false,
            ["media.hardware-video-decoding.enabled"] = false,
            ["media.rdd-process.enabled"] = false,
            ["webgl.disabled"] = true,
            ["browser.startup.page"] = 0,
            ["browser.startup.homepage"] = "about:blank",
            ["startup.homepage_welcome_url"] = string.Empty,
            ["startup.homepage_welcome_url.additional"] = string.Empty,
            ["browser.aboutwelcome.enabled"] = false,
        };

        if (ResolveAcceptLanguages(settings) is { } acceptLanguages)
            preferences["intl.accept_languages"] = acceptLanguages;

        if (!string.IsNullOrWhiteSpace(settings.Device?.UserAgent))
            preferences["general.useragent.override"] = settings.Device.UserAgent;

        if (settings.UseIncognitoMode)
            preferences["browser.privatebrowsing.autostart"] = true;

        AppendFirefoxProxyPreferences(preferences, settings.Proxy, settings.Logger);

        return preferences;
    }

    private static List<string> BuildFirefoxDefaultArguments()
        => ["-no-remote"];

    private static List<string> BuildFirefoxEffectiveArguments(WebBrowserSettings settings, string profilePath, IEnumerable<string> defaultArguments, bool useBrowserHeadlessMode)
    {
        List<string> arguments =
        [
            "-profile",
            profilePath,
        ];

        foreach (var argument in defaultArguments)
            AddArgumentIfMissing(arguments, argument);

        if (useBrowserHeadlessMode)
            AddArgumentIfMissing(arguments, "-headless");

        foreach (var argument in NormalizeArguments(settings.Args))
            AddArgumentIfMissing(arguments, argument);

        return arguments;
    }

    private static bool ShouldUseBrowserHeadlessMode(WebBrowserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.UseHeadlessMode;
    }

    private static string BuildFirefoxUserJs(JsonObject preferences)
    {
        var lines = preferences
            .Select(static property => string.Concat("user_pref(\"", property.Key, "\", ", property.Value!.ToJsonString(), ");"))
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string? ResolveAcceptLanguages(WebBrowserSettings settings)
    {
        if (settings.Device?.Languages is { } languages)
        {
            var items = languages.Where(static language => !string.IsNullOrWhiteSpace(language)).ToArray();
            if (items.Length > 0)
                return string.Join(',', items);
        }

        return string.IsNullOrWhiteSpace(settings.Device?.Locale) ? null : settings.Device.Locale;
    }

    private static IEnumerable<string> NormalizeArguments(IEnumerable<string>? arguments)
        => arguments?.Where(static argument => !string.IsNullOrWhiteSpace(argument)).Distinct(StringComparer.Ordinal) ?? [];

    private static JsonArray ToJsonArray(IEnumerable<string> arguments)
        => new(arguments.Select(static argument => JsonValue.Create(argument)).ToArray());

    private static void AddChromiumArgument(List<string> arguments, string argument, string? uniquenessPrefix = null)
    {
        if (argument.StartsWith("--disable-features=", StringComparison.Ordinal))
        {
            MergeCsvArgument(arguments, "--disable-features=", SplitCsv(argument["--disable-features=".Length..]));
            return;
        }

        if (argument.StartsWith("--enable-features=", StringComparison.Ordinal))
        {
            MergeCsvArgument(arguments, "--enable-features=", SplitCsv(argument["--enable-features=".Length..]));
            return;
        }

        var resolvedUniquenessPrefix = uniquenessPrefix ?? argument switch
        {
            _ when argument.StartsWith("--password-store=", StringComparison.Ordinal) => "--password-store=",
            _ when argument.StartsWith("--user-data-dir=", StringComparison.Ordinal) => "--user-data-dir=",
            _ when argument.StartsWith("--proxy-server=", StringComparison.Ordinal) => "--proxy-server=",
            _ when argument.StartsWith("--lang=", StringComparison.Ordinal) => "--lang=",
            _ when argument.StartsWith("--user-agent=", StringComparison.Ordinal) => "--user-agent=",
            _ when argument.StartsWith("--headless", StringComparison.Ordinal) => "--headless",
            _ => argument,
        };

        AddArgumentIfMissing(arguments, argument, resolvedUniquenessPrefix);
    }

    private static string? TryResolveChromiumLanguageArgument(WebBrowserSettings settings)
    {
        var locale = settings.Device?.Locale;
        return string.IsNullOrWhiteSpace(locale) ? null : "--lang=" + locale;
    }

    private static string? TryResolveChromiumUserAgentArgument(WebBrowserSettings settings)
    {
        var userAgent = settings.Device?.UserAgent;
        return string.IsNullOrWhiteSpace(userAgent) ? null : "--user-agent=" + userAgent;
    }

    private static string? TryResolveProxyArgument(IWebProxy? proxy)
    {
        if (proxy is null)
            return null;

        return "--proxy-server=" + SerializeProxy(proxy, includeCredentials: false);
    }

    private static void AppendFirefoxProxyPreferences(JsonObject preferences, IWebProxy? proxy, ILogger? logger)
    {
        if (proxy is null)
            return;

        var proxyUrl = SerializeProxy(proxy, includeCredentials: false);
        if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxyUri))
        {
            logger?.LogProfileAutomationFirefoxProxyInvalid();
            return;
        }

        preferences["network.proxy.type"] = 1;

        if (proxyUri.Scheme is "socks5" or "socks")
        {
            preferences["network.proxy.socks"] = proxyUri.Host;
            preferences["network.proxy.socks_port"] = proxyUri.Port;
            preferences["network.proxy.socks_version"] = 5;
            preferences["network.proxy.socks_remote_dns"] = true;
            logger?.LogProfileAutomationFirefoxProxyApplied(proxyUri.Scheme, proxyUri.Host, proxyUri.Port);
            return;
        }

        preferences["network.proxy.http"] = proxyUri.Host;
        preferences["network.proxy.http_port"] = proxyUri.Port;
        preferences["network.proxy.ssl"] = proxyUri.Host;
        preferences["network.proxy.ssl_port"] = proxyUri.Port;
        logger?.LogProfileAutomationFirefoxProxyApplied(proxyUri.Scheme, proxyUri.Host, proxyUri.Port);
    }

    private static string SerializeProxy(IWebProxy proxy, bool includeCredentials)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var proxyUri = proxy switch
        {
            WebProxy webProxy when webProxy.Address is not null => webProxy.Address,
            _ => ResolveProxyUri(proxy),
        };

        if (proxyUri is null || !proxyUri.IsAbsoluteUri)
            throw new NotSupportedException("Интерфейс IWebProxy должен возвращать абсолютный адрес прокси");

        var builder = new UriBuilder(proxyUri);
        if (!includeCredentials)
        {
            builder.UserName = string.Empty;
            builder.Password = string.Empty;
            return builder.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }

        if (proxy.Credentials is NetworkCredential credentials)
        {
            builder.UserName = credentials.UserName;
            builder.Password = credentials.Password;
        }

        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "A fixed probe URI is required to resolve IWebProxy implementations consistently.")]
    private static Uri? ResolveProxyUri(IWebProxy proxy)
    {
        var probeUri = new Uri("https://example.com", UriKind.Absolute);
        Uri? candidate;

        try
        {
            candidate = proxy.GetProxy(probeUri);
        }
        catch (NotImplementedException)
        {
            return null;
        }

        return candidate == probeUri ? null : candidate;
    }

    private static void AddArgumentIfMissing(List<string> arguments, string argument, string? uniquenessPrefix = null)
    {
        uniquenessPrefix ??= argument;

        if (arguments.Exists(existingArgument => existingArgument.StartsWith(uniquenessPrefix, StringComparison.Ordinal)))
            return;

        arguments.Add(argument);
    }

    private static void MergeCsvArgument(List<string> arguments, string prefix, IEnumerable<string> values)
    {
        var mergedValues = new List<string>();

        for (var index = arguments.Count - 1; index >= 0; index--)
        {
            var argument = arguments[index];
            if (!argument.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            mergedValues.AddRange(SplitCsv(argument[prefix.Length..]));
            arguments.RemoveAt(index);
        }

        mergedValues.AddRange(values);

        var distinctValues = mergedValues.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctValues.Length > 0)
            arguments.Add(prefix + string.Join(',', distinctValues));
    }

    private static string[] SplitCsv(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class BrowserAutomationPreset
{
    internal required string Family { get; init; }

    internal string? PreferenceFile { get; init; }

    internal string? LocalStateFile { get; init; }

    internal JsonArray SeedFiles { get; init; } = [];

    internal JsonArray DefaultArguments { get; init; } = [];

    internal JsonArray EffectiveArguments { get; init; } = [];

    internal JsonObject? Preferences { get; init; }

    internal JsonObject? LocalState { get; init; }

    internal IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}