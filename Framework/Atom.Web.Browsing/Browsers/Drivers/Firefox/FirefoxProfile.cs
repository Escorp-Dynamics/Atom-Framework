using System.Text;
using Atom.Architect.Reactive;
using Atom.Buffers;
using Atom.Text.Json;

namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет профиль Mozilla Firefox.
/// </summary>
public partial class FirefoxProfile : Reactively, IUserProfile
{
    private readonly Dictionary<string, object?> profile;

    private static readonly Lazy<FirefoxProfile> defaultProfile = new(() => new FirefoxProfile(), true);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FirefoxProfile"/>.
    /// </summary>
    public FirefoxProfile()
    {
        var host = "dummy.test";

        profile = new Dictionary<string, object?>
        {
            { "app.normandy.api_url", string.Empty },
            { "app.update.checkInstallTime", false },
            { "app.update.disabledForTesting", true },
            { "apz.content_response_timeout", 60000 },
            { "browser.contentblocking.features.standard", "-tp,tpPrivate,cookieBehavior0,-cm,-fp" },
            { "browser.dom.window.dump.enabled", true },
            { "browser.newtabpage.activity-stream.feeds.system.topstories", false },
            { "browser.newtabpage.enabled", false },
            { "browser.pagethumbnails.capturing_disabled", true },
            { "browser.safebrowsing.blockedURIs.enabled", false },
            { "browser.safebrowsing.downloads.enabled", false },
            { "browser.safebrowsing.malware.enabled", false },
            { "browser.safebrowsing.passwords.enabled", false },
            { "browser.safebrowsing.phishing.enabled", false },
            { "browser.search.update", false },
            { "browser.sessionstore.resume_from_crash", false },
            { "browser.shell.checkDefaultBrowser", false },
            { "browser.startup.homepage", "about:blank" },
            { "browser.startup.homepage_override.mstone", "ignore" },
            { "browser.startup.page", 0 },
            { "browser.tabs.closeWindowWithLastTab", false },
            { "browser.tabs.disableBackgroundZombification", false },
            { "browser.tabs.warnOnCloseOtherTabs", false },
            { "browser.tabs.warnOnOpen", false },
            { "browser.toolbars.bookmarks.visibility", "never" },
            { "browser.uitour.enabled", false },
            { "browser.urlbar.suggest.searches", false },
            { "browser.usedOnWindows10.introURL", string.Empty },
            { "browser.warnOnQuit", false },
            { "datareporting.healthreport.documentServerURI", $"http://{host}/dummy/healthreport/" },
            { "datareporting.healthreport.logging.consoleEnabled", false },
            { "datareporting.healthreport.service.enabled", false },
            { "datareporting.healthreport.service.firstRun", false },
            { "datareporting.healthreport.uploadEnabled", false },
            { "datareporting.policy.dataSubmissionEnabled", false },
            { "datareporting.policy.dataSubmissionPolicyBypassNotification", true },
            { "devtools.enabled", false },
            { "devtools.jsonview.enabled", false },
            { "dom.disable_open_during_load", false },
            { "dom.file.createInChild", true },
            { "dom.ipc.reportProcessHangs", false },
            { "dom.max_chrome_script_run_time", 0 },
            { "dom.max_script_run_time", 0 },
            { "extensions.autoDisableScopes", 0 },
            { "extensions.enabledScopes", 5 },
            { "extensions.getAddons.cache.enabled", false },
            { "extensions.installDistroAddons", false },
            { "extensions.screenshots.disabled", true },
            { "extensions.update.enabled", false },
            { "extensions.update.notifyUser", false },
            { "extensions.webservice.discoverURL", $"http://{host}/dummy/discoveryURL" },
            { "fission.bfcacheInParent", false },
            { "fission.webContentIsolationStrategy", 0 },
            { "focusmanager.testmode", true },
            { "general.useragent.updates.enabled", false },
            { "geo.provider.testing", true },
            { "geo.wifi.scan", false },
            { "hangmonitor.timeout", 0 },
            { "javascript.options.showInConsole", true },
            { "media.gmp-manager.updateEnabled", false },
            { "media.sanity-test.disabled", true },
            { "network.cookie.cookieBehavior", 0 },
            { "network.cookie.sameSite.laxByDefault", false },
            { "network.http.prompt-temp-redirect", false },
            { "network.http.speculative-parallel-limit", 0 },
            { "network.manage-offline-status", false },
            { "network.sntp.pools", host },
            { "plugin.state.flash", 0 },
            { "privacy.trackingprotection.enabled", false },
            { "remote.active-protocols", 3 },
            { "remote.enabled", true },
            { "security.certerrors.mitm.priming.enabled", false },
            { "security.fileuri.strict_origin_policy", false },
            { "security.notification_enable_delay", 0 },
            { "services.settings.server", $"http://{host}/dummy/blocklist/" },
            { "signon.autofillForms", false },
            { "signon.rememberSignons", false },
            { "startup.homepage_welcome_url", "about:blank" },
            { "startup.homepage_welcome_url.additional", string.Empty },
            { "toolkit.cosmeticAnimations.enabled", false },
            { "toolkit.startup.max_resumed_crashes", -1 },
            { "toolkit.telemetry.server", $"http://{host}/telemetry-dummy/" },
        };
    }

    /// <summary>
    /// Настройки профиля по умолчанию.
    /// </summary>
    public static IUserProfile Default => defaultProfile.Value;

    /// <summary>
    /// Сохраняет профиль Firefox по указанному пути.
    /// </summary>
    /// <param name="path">Путь сохранения.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public virtual async ValueTask SaveAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        foreach (var preference in profile)
        {
            sb.Append("user_pref(\"")
              .Append(preference.Key)
              .Append("\", ")
              .Append(preference.Value.Serialize())
              .Append(");")
              .Append(Environment.NewLine);
        }

        var file = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        await File.WriteAllTextAsync(Path.Combine(path, "user.js"), file, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(path, "prefs.js"), string.Empty, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Сохраняет профиль Firefox по указанному пути.
    /// </summary>
    /// <param name="path">Путь сохранения.</param>
    public ValueTask SaveAsync(string path) => SaveAsync(path, CancellationToken.None);
}