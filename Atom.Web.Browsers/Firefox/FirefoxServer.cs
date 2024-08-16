using System.Runtime.InteropServices;
using Atom.Web.Browsers.NativeMessaging;
using Microsoft.Win32;

namespace Atom.Web.Browsers;

/// <summary>
/// Предоставляет доступ к браузеру Firefox через нативное сообщение.
/// </summary>
/// <inheritdoc/>
public class FirefoxServer(Manifest manifest) : WebBrowserServer(manifest)
{
    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="FirefoxServer"/>.
    /// </summary>
    public FirefoxServer() : this(new Manifest
    {
        Name = "Atom",
        Description = "Atom",
        AllowedExtensions = ["atom@escorp.dynamics"],
    })
    { }

    /// <inheritdoc/>
    protected override async ValueTask OnStarted(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser;
            using var subKey = key.CreateSubKey("Software\\Mozilla\\NativeMessagingHosts", true);

            subKey.SetValue(Manifest.Name, GetManifestPath());

            subKey.Close();
            key.Close();
        }

        await base.OnStarted(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async ValueTask OnStopped(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser;
            using var subKey = key.OpenSubKey("Software\\Mozilla\\NativeMessagingHosts", true);

            if (subKey is not null)
            {
                subKey.DeleteValue(Manifest.Name, false);
                subKey.Close();
            }

            key.Close();
        }

        await base.OnStopped(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override string GetManifestPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Path.Combine(Environment.CurrentDirectory, "manifest.json");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $"Library/Application Support/Mozilla/NativeMessagingHosts/{Manifest.Name}.json");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".mozilla/native-messaging-hosts/{Manifest.Name}.json");
        throw new InvalidOperationException("Неподдерживаемая платформа");
    }
}