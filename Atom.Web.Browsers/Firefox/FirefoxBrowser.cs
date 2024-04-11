using System.Text.Json;

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
        const string id = "atom@escorp.dynamics";
        string xpi = Path.Combine(Environment.CurrentDirectory, $"{id}.xpi");

        if (!File.Exists(path))
        {
            var ids = new[] { id };

            var distributionPolicies = new DistributionPolicies
            {
                Policies = new Extensions
                {
                    Install = new[] { xpi },
                    Uninstall = ids,
                    Locked = ids,
                },
            };

            var json = JsonSerializer.Serialize(distributionPolicies, JsonDistributionPoliciesContext.Default.DistributionPolicies);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var distributionPolicies = JsonSerializer.Deserialize(json, JsonDistributionPoliciesContext.Default.DistributionPolicies);
            
            if (!distributionPolicies!.Policies.Install.Any(x => x.Contains(id, StringComparison.InvariantCultureIgnoreCase)))
                distributionPolicies.Policies.Install = distributionPolicies.Policies.Install
                    .Where(x => !x.Contains(id, StringComparison.InvariantCultureIgnoreCase))
                    .Append(xpi);
            
            if (!distributionPolicies.Policies.Uninstall.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                distributionPolicies.Policies.Uninstall = distributionPolicies.Policies.Install.Append(id);
            
            if (!distributionPolicies!.Policies.Locked.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                distributionPolicies.Policies.Locked = distributionPolicies.Policies.Install.Append(id);
            
            json = JsonSerializer.Serialize(distributionPolicies, JsonDistributionPoliciesContext.Default.DistributionPolicies);
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