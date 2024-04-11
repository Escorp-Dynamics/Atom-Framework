using System.Text.Json;
using Atom.Net.Http;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет браузер Firefox.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
/// </remarks>
/// <param name="settings">Настройки браузера.</param>
public class FirefoxBrowser(FirefoxSettings settings) : WebBrowser<FirefoxSettings, FirefoxServer>(settings)
{
    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
    /// </summary>
    public FirefoxBrowser() : this(FirefoxSettings.Default) { }

    private async ValueTask SetDistributionPoliciesAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Settings.GetNativeDistributionPath(), "distribution");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        path = Path.Combine(path, "policies.json");

        if (!File.Exists(path))
        {
            var paths = new[] { Path.Combine(Environment.CurrentDirectory, "atom@escorp.dynamics.xpi") };
            var ids = new[] { "atom@escorp.dynamics" };

            var extensions = new Dictionary<string, object?>
            {
                { "Install", paths },
                { "Uninstall", ids },
                { "Locked", ids },
            };

            var policies = new Dictionary<string, object?>
            {
                { "policies", new Dictionary<string, object?> { { "Extensions", extensions } } },
            };

            var json = JsonSerializer.Serialize(policies!, JsonHttpContext.Default.Form);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask StartProcessAsync(CancellationToken cancellationToken)
    {
        await Settings.Profile.SaveAsync(cancellationToken).ConfigureAwait(false);
        await SetDistributionPoliciesAsync(cancellationToken).ConfigureAwait(false);
        await base.StartProcessAsync(cancellationToken).ConfigureAwait(false);
    }
}