using Atom.Net.Browsing.WebDriver.Protocol.Cdp;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebBrowser
{
    /// <summary>
    /// Запускает браузер на движке Obscura: процессы движка поднимаются транспортом по требованию,
    /// первое окно открывается сразу.
    /// </summary>
    private static async ValueTask<WebBrowser> LaunchObscuraAsync(WebBrowserSettings launchSettings, ObscuraProfile profile, CancellationToken cancellationToken)
    {
        var executablePath = profile.IsInstalled
            ? profile.BinaryPath
            : await ObscuraBinaryProvisioner.EnsureAsync(profile.Variant, cancellationToken).ConfigureAwait(false);

        var sessionId = string.Concat("obscura-", Guid.NewGuid().ToString("N"));
        var logger = launchSettings.Logger;
        var defaultKey = new CdpServerKey(ProxyUrl: null, profile.TimeZone, profile.Geolocation);

        var transport = new CdpBrowserCommandTransport(
            sessionId,
            (index, key, token) => StartObscuraServerSlotAsync(executablePath, profile, index, key, logger, token),
            defaultKey,
            TryReadIdentityRuntimeSource(),
            logger);

        WebBrowser browser;
        try
        {
            browser = new WebBrowser(
                launchSettings,
                materializedProfilePath: null,
                browserProcess: null,
                display: null,
                ownsDisplay: false,
                bridgeServer: null,
                bridgeBootstrap: null,
                commandTransport: transport,
                transportSessionId: sessionId);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            await browser.BindInitialProvisionedSurfaceAsync(transport, cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch
        {
            await browser.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask BindInitialProvisionedSurfaceAsync(CdpBrowserCommandTransport provisioning, CancellationToken cancellationToken)
    {
        var window = (WebWindow)CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var (tabId, windowId) = await provisioning.OpenWindowAsync(
            new SurfaceProvisioningRequest(ResolveBridgeProxy(page), page.ResolvedDevice),
            cancellationToken).ConfigureAwait(false);

        window.BindBridgeWindowId(windowId);
        BindTransportPageCommands(page, bridgeSessionId!, tabId);
        await ApplyBridgeTabContextAsync(page, cancellationToken).ConfigureAwait(false);
        await page.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<CdpServerSlot> StartObscuraServerSlotAsync(
        string executablePath,
        ObscuraProfile profile,
        int index,
        CdpServerKey key,
        Microsoft.Extensions.Logging.ILogger? logger,
        CancellationToken cancellationToken)
    {
        var server = await ObscuraServer.StartAsync(
            executablePath,
            new ObscuraServerOptions
            {
                UseStealth = profile.UseStealth,
                AllowPrivateNetwork = profile.AllowPrivateNetwork,
                ProxyUrl = key.ProxyUrl,
                TimeZone = key.TimeZone,
                Geolocation = key.Geolocation,
            },
            logger,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var connection = await server.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new CdpServerSlot(index, key, connection, server);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Текст раннего скрипта личности расширения: на Obscura он ставится тем же кодом, что и в
    /// браузерах с расширением, чтобы подмена окружения была одинаковой.
    /// </summary>
    private static string? TryReadIdentityRuntimeSource()
    {
        string[] candidates =
        [
            IOPath.Combine(AppContext.BaseDirectory, "ExtensionWorkingLayout", "Extension", "identity.main.js"),
            IOPath.Combine(AppContext.BaseDirectory, "Extension", "identity.main.js"),
        ];

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Следующий кандидат.
            }
        }

        return null;
    }
}
