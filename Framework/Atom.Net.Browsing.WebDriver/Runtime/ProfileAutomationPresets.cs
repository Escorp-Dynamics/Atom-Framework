using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static class ProfileAutomationPresets
{
    /// <summary>
    /// Нижняя граница ширины окна Chromium: уже этого браузер окно не делает.
    /// </summary>
    /// <remarks>
    /// Мобильный профиль просит 412 px, браузер отдаёт 500 — и окно оказывается шире и экрана
    /// профиля, и виртуального дисплея. Дисплей поэтому поднимается не уже этой границы.
    /// </remarks>
    internal const int MinimumChromiumWindowWidth = 500;

    internal static BrowserAutomationPreset Create(WebBrowserProfile profile, WebBrowserSettings settings, string profilePath, bool enableManagedChromiumBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profilePath);

        var useBrowserHeadlessMode = ShouldUseBrowserHeadlessMode(settings);
        var family = profile is FirefoxProfile ? "firefox" : "chromium";

        WarnOnWebKitEngineMismatch(settings, family);

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
            // Не «усыплять» невыбранные (фоновые) вкладки/окна: при нескольких вкладках в одном окне
            // невыбранная вкладка иначе замораживает requestAnimationFrame, и её Turnstile-виджет не
            // монтирует challenge-iframe (редкие no-frame-no-token при windows=1/tabs=N). Держим все
            // рендереры «на переднем плане» по приоритету и таймерам.
            "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows",
            "--disable-background-timer-throttling",

            // Не трогать аппаратную видеоподсистему хоста: браузер живёт на виртуальном дисплее,
            // выводить через GPU некуда, а GPU-процесс всё равно открывал драйвер реальной карты и
            // срывал внешний монитор.
            "--disable-gpu",
            "--disable-gpu-compositing",

            // ...но WebGL при этом обязан остаться рабочим. Замер показал webgl=НЕТ-КОНТЕКСТА и
            // пустые glVendor/glRenderer — для настоящего десктопного Chrome это невозможно, и
            // Cloudflare отвечал на такой отпечаток error-callback 300010 («bot behavior detected»
            // по его документации). SwiftShader даёт контекст программно, без обращения к железу,
            // поэтому монитор остаётся нетронутым, а страница видит нормальный ANGLE-рендерер.
            "--enable-unsafe-swiftshader",
            "--use-angle=swiftshader",
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
            // Отключаем троттлинг/усыпление фоновых страниц и детект перекрытия окон — чтобы
            // невыбранные вкладки продолжали рендерить (rAF) и Turnstile монтировался и на них.
            "CalculateNativeWinOcclusion",
            "IntensiveWakeUpThrottling",
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

        // Геометрию окна ЗАДАЁМ, а не подменяем: см. TryResolveChromiumWindowArguments.
        foreach (var windowArgument in TryResolveChromiumWindowArguments(settings))
            AddChromiumArgument(arguments, windowArgument, windowArgument[..(windowArgument.IndexOf('=', StringComparison.Ordinal) + 1)]);

        // Сенсорный ввод включаем НАСТОЯЩИЙ, когда устройство его заявляет: подменённый
        // 'maxTouchPoints' без событий касания противоречит сам себе — страница проверяет наличие
        // 'ontouchstart' и конструктора TouchEvent, а их подмена значения не создаёт.
        if (settings.Device is { HasTouch: true })
        {
            AddChromiumArgument(arguments, "--touch-events=enabled", "--touch-events");

            // ★ Мало объявить сенсорный ввод — им надо ПОЛЬЗОВАТЬСЯ. Доверенный клик шлёт события
            // мыши, а на телефоне страница видит касания: touchstart/touchend и pointerType
            // 'touch'. Виджет проверки, отрисованный для мобильного профиля, ждёт именно их, и
            // мышиный клик по нему остаётся без ответа. Ключ переводит ввод указателя в касания,
            // поэтому клик доходит до страницы тем же способом, что и палец.
            AddChromiumArgument(arguments, "--simulate-touch-screen-with-mouse");
        }

        // Программный WebGL — только когда вызывающий задал синтетический профиль устройства.
        // На виртуальном дисплее GPU нет, и контекст WebGL не создаётся вовсе: getContext('webgl')
        // возвращает null, а vendor/renderer пустые. Для профиля «как есть» это терпимо и проверено
        // (КПД 100%), но заявляя чужую платформу, отсутствие контекста нечем объяснить: настоящий
        // десктопный браузер всегда отдаёт рабочий WebGL. SwiftShader даёт контекст без GPU, причём
        // сразу в формате ANGLE — том же, что у настоящего Windows-браузера.
        // Условие — ИМЕННО подмена WebGL, а не наличие профиля вообще. Программный рендеринг нужен
        // ровно затем, чтобы контекст существовал и было чему отдавать подменённые строки; профилю,
        // который WebGL не трогает, он не даёт ничего.
        // Цена измерена на реальном таргете и оказалась втрое выше прежней оценки: median
        // 8.48→10.11с, p90 10.48→13.30с, mean 8.87→11.01с при одинаковом КПД 100%. Платить столько
        // за неиспользуемую поверхность нельзя — при подмене браузера/устройства в пределах своей
        // ОС отсутствие WebGL-контекста терпимо и проверено (КПД 100%).
        if (settings.Device?.WebGL is not null)
        {
            AddChromiumArgument(arguments, "--enable-unsafe-swiftshader");
            AddChromiumArgument(arguments, "--use-angle=swiftshader", "--use-angle=");
        }

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
            // Firefox по умолчанию форсирует DIRECT для loopback (127.0.0.1/localhost/::1) и игнорирует
            // любой прокси, который вернул proxy.onRequest. Навигационный MITM-прокси моста слушает на
            // loopback, поэтому без этого расширение НЕ может маршрутизировать main_frame-навигацию через
            // прокси — и main_frame fulfill/abort (fail-closed перехват) не работает (запрос уходит на
            // origin напрямую). Также очищаем список bypass, чтобы loopback не оседал в нём на части сборок.
            ["network.proxy.allow_hijacking_localhost"] = true,
            ["network.proxy.no_proxies_on"] = string.Empty,
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
            // WebGL по умолчанию выключен: на виртуальном дисплее он стоит заметного времени, а
            // профилю, который его не заявляет, не даёт ничего (у Chromium ровно то же условие).
            // Значение переопределяется ниже, если профиль устройства WebGL всё-таки заявляет.
            ["webgl.disabled"] = true,
            ["browser.startup.page"] = 0,
            ["browser.startup.homepage"] = "about:blank",
            ["startup.homepage_welcome_url"] = string.Empty,
            ["startup.homepage_welcome_url.additional"] = string.Empty,
            ["browser.aboutwelcome.enabled"] = false,
            // Многовкладочный солвер: фоновые (неактивные) вкладки НЕ должны тормозиться/замораживаться,
            // иначе таймеры и колбэки виджета в них не идут — Turnstile не решается (chromium-аналог
            // уже стоит: --disable-background-timer-throttling). Отключаем throttling таймеров, бюджетное
            // троттлинг и выгрузку/заморозку неактивных вкладок.
            ["dom.min_background_timeout_value"] = 4,
            ["dom.min_background_timeout_value_without_budget_throttling"] = 4,
            ["dom.timeout.enable_budget_timer_throttling"] = false,
            ["dom.timeout.background_throttling_max_budget"] = -1,
            ["dom.timeout.throttling_delay"] = 0,
            ["dom.suspend_inactive_tab.enabled"] = false,
            ["browser.tabs.unloadOnLowMemory"] = false,
            ["browser.tabs.min_inactive_duration_before_unload"] = 0,
            ["page_load.deprioritization_period"] = 0,
        };

        // ★ Программный WebGL для заявленного профиля.
        //
        // Firefox запускался с полностью выключенным WebGL, и страница видела браузер, у которого
        // getContext('webgl') не создаётся вовсе. Замер на живом Cloudflare: виджет отвечал
        // error-callback 600010 («среда слишком ограничена») за 6–13 секунд, 0 задач из 14. У
        // Chromium в той же ситуации поднимается SwiftShader, здесь — программный Mesa (llvmpipe),
        // а строки vendor/renderer всё равно подменяет расширение.
        //
        // Условие то же, что у Chromium: платим за контекст только когда профиль его заявляет.
        if (settings.Device?.WebGL is not null)
        {
            preferences["webgl.disabled"] = false;
            preferences["webgl.force-enabled"] = true;
            preferences["webgl.disable-fail-if-major-performance-caveat"] = true;
        }

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

        // Тот же довод, что и у Chromium: размер окна задаём по-настоящему, иначе метрики
        // раскладки расходятся с заявленным профилем, а подмена до них не дотягивается.
        //
        // Значения кладутся напрямую, а не через AddArgumentIfMissing: тот отбрасывает повтор
        // одинаковых аргументов, и у квадратного окна вторая величина потерялась бы.
        if (ResolveDeclaredWindowBounds(settings.Device) is { } bounds && !arguments.Contains("-width", StringComparer.Ordinal))
        {
            arguments.Add("-width");
            arguments.Add(bounds.Size.Width.ToString(CultureInfo.InvariantCulture));
            arguments.Add("-height");
            arguments.Add(bounds.Size.Height.ToString(CultureInfo.InvariantCulture));
        }

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
            _ when argument.StartsWith("--touch-events", StringComparison.Ordinal) => "--touch-events",
            _ when argument.StartsWith("--window-size=", StringComparison.Ordinal) => "--window-size=",
            _ when argument.StartsWith("--window-position=", StringComparison.Ordinal) => "--window-position=",
            _ when argument.StartsWith("--user-agent=", StringComparison.Ordinal) => "--user-agent=",
            _ when argument.StartsWith("--headless", StringComparison.Ordinal) => "--headless",
            _ => argument,
        };

        AddArgumentIfMissing(arguments, argument, resolvedUniquenessPrefix);
    }

    /// <summary>
    /// Предупреждает о профиле, который не может быть достоверным на этом браузере.
    /// </summary>
    /// <remarks>
    /// ★ На iOS и iPadOS ЛЮБОЙ браузер работает на WebKit — там нет ни Blink, ни Gecko: Chrome
    /// (CriOS) и Firefox (FxiOS) там лишь оболочки над системным движком. Поэтому профиль iPhone
    /// или iPad, запущенный на Chromium, противоречив не в свойствах, а в самом движке: формат
    /// <c lang="text">Error.stack</c>, тексты исключений, состав глобальных объектов и поведение встроенных
    /// типов принадлежат V8 и подменой свойств не закрываются.
    ///
    /// Замер это подтвердил напрямую: живой Cloudflare Turnstile отдаёт такому профилю
    /// error-callback 600010 — 0 успехов из 10 и с агентом Safari, и с агентом Chrome for iOS,
    /// тогда как профили Windows, macOS и Android (то есть заявляющие Blink) проходят 10 из 10.
    /// Молчать об этом нельзя: вызывающий получил бы стабильные отказы без объяснения причины.
    /// </remarks>
    private static void WarnOnWebKitEngineMismatch(WebBrowserSettings settings, string family)
    {
        if (settings.Device?.UserAgent is not { Length: > 0 } userAgent)
            return;

        var declaresWebKit = userAgent.Contains("iPhone", StringComparison.Ordinal)
            || userAgent.Contains("iPad", StringComparison.Ordinal)
            || userAgent.Contains("iPod", StringComparison.Ordinal)
            || (userAgent.Contains("Safari/", StringComparison.Ordinal)
                && userAgent.Contains("Version/", StringComparison.Ordinal)
                && !userAgent.Contains("Chrome/", StringComparison.Ordinal)
                && !userAgent.Contains("Chromium/", StringComparison.Ordinal));

        if (declaresWebKit)
        {
            settings.Logger?.LogProfileAutomationWebKitEngineMismatch(userAgent, family);
            return;
        }

        // ★ Обратный случай: Gecko, которому подсунули профиль Chromium. Он так же безнадёжен —
        // у Firefox нет ни клиентских подсказок, ни 'navigator.userAgentData', ни объекта 'chrome',
        // а формат исходника нативных функций и тексты исключений принадлежат SpiderMonkey.
        // Замер: 0 из 4, error-callback 600010. Обратное направление (профиль Firefox на Chromium)
        // при этом работает — там лишние поверхности можно убрать, а недостающие объявить.
        var declaresChromium = !userAgent.Contains("Firefox/", StringComparison.Ordinal)
            && (userAgent.Contains("Chrome/", StringComparison.Ordinal)
                || userAgent.Contains("Chromium/", StringComparison.Ordinal));

        if (declaresChromium && string.Equals(family, "firefox", StringComparison.Ordinal))
            settings.Logger?.LogProfileAutomationChromiumEngineMismatch(userAgent);
    }

    /// <summary>
    /// Аргументы размера и положения окна под заявленную область просмотра.
    /// </summary>
    /// <remarks>
    /// ★ Замер эмуляции показал разрыв: подменённые <c lang="text">innerWidth/innerHeight</c> отдавали размер
    /// профиля (1512×982), а настоящая область документа была 780×493 — и её же отдавали
    /// <c lang="text">documentElement.clientHeight</c> и <c lang="text">visualViewport</c>, до которых подмена не
    /// дотягивается: за ними стоит настоящая раскладка страницы. Расхождение вдвое видно одной
    /// строкой.
    ///
    /// Поэтому окно получает заявленный размер по-настоящему. Тогда согласованы сразу все метрики,
    /// включая недоступные подмене, а разница <c lang="text">outerHeight − innerHeight</c> становится
    /// настоящей высотой рамки браузера вместо нуля — который сам по себе выдавал среду без окна.
    /// </remarks>
    private static IEnumerable<string> TryResolveChromiumWindowArguments(WebBrowserSettings settings)
    {
        if (ResolveDeclaredWindowBounds(settings.Device) is not { } bounds)
            yield break;

        yield return string.Create(CultureInfo.InvariantCulture, $"--window-size={bounds.Size.Width},{bounds.Size.Height}");
        yield return string.Create(CultureInfo.InvariantCulture, $"--window-position=0,{bounds.Location.Y}");
    }

    /// <summary>
    /// Размер и положение окна, вытекающие из заявленного устройства.
    /// </summary>
    /// <param name="device">Профиль устройства либо <see langword="null"/>.</param>
    /// <returns>Границы окна либо <see langword="null"/>, если область просмотра не заявлена.</returns>
    private static Rectangle? ResolveDeclaredWindowBounds(Device? device)
    {
        var viewport = device?.ViewportSize ?? Size.Empty;

        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0)
            return null;

        // Окно не может выходить за ДОСТУПНУЮ область экрана: строку меню macOS и панель задач
        // Windows оно не перекрывает. Размер во весь экран делал availHeight равной полной высоте
        // и стирал признак оболочки рабочего стола, ради которого доступная область и заявляется.
        var screen = device?.Screen;
        var availableWidth = screen?.AvailWidth is > 0 and var declaredWidth ? declaredWidth : viewport.Width;
        var availableHeight = screen?.AvailHeight is > 0 and var declaredHeight ? declaredHeight : viewport.Height;

        // Верх доступной области: на macOS её занимает строка меню, поэтому окно начинается ниже;
        // панель задач Windows по умолчанию снизу, и начало остаётся нулевым.
        var top = device?.Platform?.StartsWith("Mac", StringComparison.OrdinalIgnoreCase) == true
            && screen is { Height: > 0 and var screenHeight, AvailHeight: > 0 and var screenAvailHeight }
                ? Math.Max(0, screenHeight - screenAvailHeight)
                : 0;

        return new Rectangle(0, top, Math.Min(viewport.Width, availableWidth), Math.Min(viewport.Height, availableHeight));
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