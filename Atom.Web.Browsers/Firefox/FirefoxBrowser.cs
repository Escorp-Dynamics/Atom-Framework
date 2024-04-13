using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private const string ExtensionId = "atom@escorp.dynamics";

    private const string ExtensionLink = $"https://gitflic.ru/project/escorp-lab/atom/blob/raw?file=Atom.Web.Browsers%2FFirefox%2FExtension%2Fatom%40escorp.dynamics.xpi&inline=false";

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxBrowser"/>.
    /// </summary>
    public FirefoxBrowser() : this(FirefoxSettings.Default) { }

    private async ValueTask UpdateDistributionPoliciesAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Settings.DistributionPath, "distribution");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        path = Path.Combine(path, "policies.json");
        if (File.Exists(path)) return;

        if (!IsRunningAsAdmin)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new InvalidOperationException("Требуются права администратора");
            if (string.IsNullOrEmpty(Settings.AdminPassword)) throw new InvalidDataException("Требуется задать пароль root-пользователя в FirefoxSettings.AdminPassword");
        }

        var ids = new[] { ExtensionId };
        var distributionPolicies = new DistributionPolicies();
        distributionPolicies.Policies.Extensions.Installed = [ExtensionLink];
        distributionPolicies.Policies.Extensions.Locked = ids;
        distributionPolicies.Policies.Extensions.AllowedInPrivateBrowsing = ids;

        var json = JsonSerializer.Serialize(distributionPolicies, JsonDistributionPoliciesContext.Default.DistributionPolicies);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
            return;
        }

        json = json.Replace("\"", "\\\\\\\"");

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"echo {Settings.AdminPassword} | sudo -S bash -c \\\"cat > {path} << EOF{Environment.NewLine}{json}{Environment.NewLine}EOF\\\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode is not 0)
        {
            var output = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Сохранение '{path}' завершилось с кодом {process.ExitCode}: {output}");
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask OnProcessStarted(CancellationToken cancellationToken)
    {
        await UpdateDistributionPoliciesAsync(cancellationToken).ConfigureAwait(false);
        await base.OnProcessStarted(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!Settings.Profile.IsPersistent && Directory.Exists(Settings.Profile.Path)) Directory.Delete(Settings.Profile.Path, true);

        var path = Path.Combine(Settings.DistributionPath, "distribution", "policies.json");

        if (File.Exists(path))
        {
            if (!IsRunningAsAdmin)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new InvalidOperationException("Требуются права администратора");
                if (string.IsNullOrEmpty(Settings.AdminPassword)) throw new InvalidDataException("Требуется задать пароль root-пользователя в FirefoxSettings.AdminPassword");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.Delete(path);
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"echo {Settings.AdminPassword} | sudo -S rm -f '{path}'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode is not 0)
                {
                    var output = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    throw new InvalidOperationException($"Удаление '{path}' завершилось с кодом {process.ExitCode}: {output}");
                }
            }
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}