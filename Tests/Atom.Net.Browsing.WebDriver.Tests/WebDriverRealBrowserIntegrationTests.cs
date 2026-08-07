using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Debug.Logging;
using Atom.Hardware.Display;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;
using Atom.Media.Video;
using Atom.Media.Video.Backends;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver.Tests;

[NonParallelizable]
public sealed class WebDriverRealBrowserIntegrationTests
{
    private static readonly TimeSpan BrowserShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BridgeBootstrapTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CookieSyncTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] StandardLifecycleSequence = ["DomContentLoaded", "NavigationCompleted", "PageLoaded"];
    private const string LocalCookieDomain = "127.0.0.1";

    [Test]
    public async Task RealBrowserLaunchBootstrapsExtensionBackedDiscoverySurface()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        });
        var currentPage = (WebPage)browser.CurrentPage;
        var currentWindow = (WebWindow)browser.CurrentWindow;
        var bootstrapped = IsBridgeCommandsBound(currentPage);
        var diagnostics = bootstrapped ? null : await DescribeBootstrapFailureAsync(browser, currentPage).ConfigureAwait(false);

        Assert.That(bootstrapped, Is.True, $"LaunchAsync должен дождаться живого bridge-bootstrap текущей discovery-вкладки. {diagnostics}");

        var title = await currentPage.GetTitleAsync().ConfigureAwait(false);
        var url = await currentPage.GetUrlAsync().ConfigureAwait(false);
        var bounds = await currentWindow.GetBoundingBoxAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
            Assert.That(bounds, Is.Not.Null);
        });
    }

    [Test]
    public async Task RealBrowserLaunchKeepsWindowAndPageSurfaceOperational()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var currentWindow = (WebWindow)browser.CurrentWindow;
        var currentPage = (WebPage)browser.CurrentPage;

        await currentPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-surface"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Surface</title></head><body>ok</body></html>",
        }).ConfigureAwait(false);

        var nextWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var nextPage = (WebPage)nextWindow.CurrentPage;
        var nextWindowBootstrapped = IsBridgeCommandsBound(nextPage);
        var nextWindowDiagnostics = nextWindowBootstrapped ? null : await DescribeBootstrapFailureAsync(browser, nextPage).ConfigureAwait(false);
        var nextPageUrl = await nextPage.GetUrlAsync().ConfigureAwait(false);

        Assert.That(nextWindowBootstrapped, Is.True, $"OpenWindowAsync должен дождаться bridge-bootstrap нового окна до возврата. {nextWindowDiagnostics}");

        Assert.Multiple(() =>
        {
            Assert.That(currentWindow.IsDisposed, Is.False);
            Assert.That(currentPage.IsDisposed, Is.False);
            Assert.That(nextWindow.IsDisposed, Is.False);
            Assert.That(nextPage.IsDisposed, Is.False);
            Assert.That(currentPage.CurrentUrl, Is.EqualTo(new Uri("https://127.0.0.1/real-browser-surface")));
            Assert.That(currentPage.CurrentTitle, Is.EqualTo("Real Browser Surface"));
            Assert.That(nextPageUrl, Is.Not.Null);
            Assert.That(nextPageUrl!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
            Assert.That(browser.Windows, Has.Some.SameAs(nextWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(nextPage));
        });
    }

    [Test]
    public async Task RealBrowserOpenPageBootstrapsExtensionBackedSurface()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        var bootstrapped = IsBridgeCommandsBound(page);
        var diagnostics = bootstrapped ? null : await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);

        Assert.That(bootstrapped, Is.True, $"OpenPageAsync должен дождаться bridge-bootstrap новой вкладки до возврата. {diagnostics}");

        var title = await page.GetTitleAsync().ConfigureAwait(false);
        var url = await page.GetUrlAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(page));
            Assert.That(browser.CurrentPage, Is.SameAs(page));
            Assert.That(browser.Pages, Has.Some.SameAs(page));
            Assert.That(title, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
        });
    }

    [Test]
    public async Task RealBrowserBrowserDeviceEmulationPersistsAcrossNavigationReloadAndNewWindow()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var initialPage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, initialPage, "Browser-level device emulation must bootstrap the initial page.").ConfigureAwait(false);

        var initialUrl = server.CreatePageUrl("browser-device-initial");
        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, initialUrl).ConfigureAwait(false);
        using var initialSnapshot = await CaptureDeviceFingerprintSnapshotAsync(initialPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(initialSnapshot.RootElement, Device.Pixel7);

        var navigationUrl = server.CreatePageUrl("browser-device-navigation");
        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, navigationUrl).ConfigureAwait(false);
        using var navigatedSnapshot = await CaptureDeviceFingerprintSnapshotAsync(initialPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(navigatedSnapshot.RootElement, Device.Pixel7);

        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, navigationUrl).ConfigureAwait(false);
        using var reloadedSnapshot = await CaptureDeviceFingerprintSnapshotAsync(initialPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(reloadedSnapshot.RootElement, Device.Pixel7);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        await AssertPageBootstrappedAsync(browser, secondPage, "Browser-level device emulation must bootstrap future windows too.").ConfigureAwait(false);

        await secondWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, secondPage, server.CreatePageUrl("browser-device-window")).ConfigureAwait(false);
        using var secondWindowSnapshot = await CaptureDeviceFingerprintSnapshotAsync(secondPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(secondWindowSnapshot.RootElement, Device.Pixel7);
    }

    [Test]
    public async Task RealBrowserWindowDeviceEmulationFansOutAcrossWindowPagesWithoutCrossWindowLeak()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var baselinePage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, baselinePage, "Baseline page must stay bridge-backed before device isolation checks.").ConfigureAwait(false);
        var baselineUrl = server.CreatePageUrl("window-device-baseline");
        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, baselineUrl).ConfigureAwait(false);
        using var baselineSnapshot = await CaptureDeviceFingerprintSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(baselineSnapshot.RootElement, Device.DesktopFullHd);

        var mobileWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            Device = Device.Pixel2,
        }).ConfigureAwait(false);
        var mobilePage = (WebPage)mobileWindow.CurrentPage;
        AssertResolvedDevice(mobilePage, Device.Pixel2, "window current page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, mobilePage, "Window-scoped device emulation must bootstrap the target window page.").ConfigureAwait(false);

        await mobileWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, mobilePage, server.CreatePageUrl("window-device-mobile-current")).ConfigureAwait(false);
        using var mobileCurrentSnapshot = await CaptureDeviceFingerprintSnapshotAsync(mobilePage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(mobileCurrentSnapshot.RootElement, Device.Pixel2);

        var mobileSiblingPage = (WebPage)await mobileWindow.OpenPageAsync().ConfigureAwait(false);
        AssertResolvedDevice(mobileSiblingPage, Device.Pixel2, "window sibling page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, mobileSiblingPage, "Window-scoped device emulation must fan out to new pages in the same window.").ConfigureAwait(false);

        await mobileWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, mobileSiblingPage, server.CreatePageUrl("window-device-mobile-sibling")).ConfigureAwait(false);
        using var mobileSiblingSnapshot = await CaptureDeviceFingerprintSnapshotAsync(mobileSiblingPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(mobileSiblingSnapshot.RootElement, Device.Pixel2);

        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, baselineUrl).ConfigureAwait(false);
        using var reloadedBaselineSnapshot = await CaptureDeviceFingerprintSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(reloadedBaselineSnapshot.RootElement, Device.DesktopFullHd);
    }

    [Test]
    public async Task RealBrowserPageDeviceEmulationPersistsAcrossNavigationReloadAndDoesNotLeakToSiblingPage()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var siblingPage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, siblingPage, "Sibling baseline page must stay bridge-backed before page-level isolation checks.").ConfigureAwait(false);
        var siblingUrl = server.CreatePageUrl("page-device-sibling");
        await NavigateToDeviceFingerprintPageAsync(browser, siblingPage, siblingUrl).ConfigureAwait(false);
        using var siblingSnapshot = await CaptureDeviceFingerprintSnapshotAsync(siblingPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(siblingSnapshot.RootElement, Device.DesktopFullHd);

        var targetPage = (WebPage)await ((WebWindow)browser.CurrentWindow).OpenPageAsync(new WebPageSettings
        {
            Device = Device.iPhone14Pro,
        }).ConfigureAwait(false);
        AssertResolvedDevice(targetPage, Device.iPhone14Pro, "page target resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, targetPage, "Page-scoped device emulation must bootstrap the target page.").ConfigureAwait(false);

        await ((WebWindow)browser.CurrentWindow).ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, server.CreatePageUrl("page-device-target")).ConfigureAwait(false);
        using var targetSnapshot = await CaptureDeviceFingerprintSnapshotAsync(targetPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(targetSnapshot.RootElement, Device.iPhone14Pro);

        var targetNavigationUrl = server.CreatePageUrl("page-device-navigation");
        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, targetNavigationUrl).ConfigureAwait(false);
        using var navigatedTargetSnapshot = await CaptureDeviceFingerprintSnapshotAsync(targetPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(navigatedTargetSnapshot.RootElement, Device.iPhone14Pro);

        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, targetNavigationUrl).ConfigureAwait(false);
        using var reloadedTargetSnapshot = await CaptureDeviceFingerprintSnapshotAsync(targetPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(reloadedTargetSnapshot.RootElement, Device.iPhone14Pro);

        await NavigateToDeviceFingerprintPageAsync(browser, siblingPage, siblingUrl).ConfigureAwait(false);
        using var reloadedSiblingSnapshot = await CaptureDeviceFingerprintSnapshotAsync(siblingPage).ConfigureAwait(false);
        AssertDeviceFingerprintSnapshot(reloadedSiblingSnapshot.RootElement, Device.DesktopFullHd);
    }

    [Test]
    public async Task RealBrowserWindowGeolocationOverrideFansOutAcrossWindowPagesWithoutCrossWindowLeak()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        var baselineDevice = Device.DesktopFullHd;
        baselineDevice.Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 50,
        };

        var isolatedWindowDevice = Device.DesktopFullHd;
        isolatedWindowDevice.Geolocation = new GeolocationSettings
        {
            Latitude = 55.7558,
            Longitude = 37.6176,
            Accuracy = 15,
        };

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = baselineDevice,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var baselinePage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, baselinePage, "Browser-level geolocation emulation must bootstrap the baseline page.").ConfigureAwait(false);

        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, server.CreatePageUrl("window-geolocation-baseline")).ConfigureAwait(false);
        var baselineSnapshot = await CaptureGeolocationSurfaceSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertGeolocationSnapshot(baselineSnapshot, baselineDevice.Geolocation!, "browser baseline geolocation");

        var isolatedWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            Device = isolatedWindowDevice,
        }).ConfigureAwait(false);
        var isolatedCurrentPage = (WebPage)isolatedWindow.CurrentPage;
        AssertResolvedDevice(isolatedCurrentPage, isolatedWindowDevice, "window geolocation current page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedCurrentPage, "Window-scoped geolocation emulation must bootstrap the target window page.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedCurrentPage, server.CreatePageUrl("window-geolocation-current")).ConfigureAwait(false);
        var isolatedCurrentSnapshot = await CaptureGeolocationSurfaceSnapshotAsync(isolatedCurrentPage).ConfigureAwait(false);
        AssertGeolocationSnapshot(isolatedCurrentSnapshot, isolatedWindowDevice.Geolocation!, "window current geolocation");

        var isolatedSiblingPage = (WebPage)await isolatedWindow.OpenPageAsync().ConfigureAwait(false);
        AssertResolvedDevice(isolatedSiblingPage, isolatedWindowDevice, "window geolocation sibling page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedSiblingPage, "Window-scoped geolocation emulation must fan out to new pages in the same window.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedSiblingPage, server.CreatePageUrl("window-geolocation-sibling")).ConfigureAwait(false);
        var isolatedSiblingSnapshot = await CaptureGeolocationSurfaceSnapshotAsync(isolatedSiblingPage).ConfigureAwait(false);
        AssertGeolocationSnapshot(isolatedSiblingSnapshot, isolatedWindowDevice.Geolocation!, "window sibling geolocation");

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, server.CreatePageUrl("window-geolocation-baseline-reloaded")).ConfigureAwait(false);
        var reloadedBaselineSnapshot = await CaptureGeolocationSurfaceSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertGeolocationSnapshot(reloadedBaselineSnapshot, baselineDevice.Geolocation!, "reloaded baseline geolocation");
    }

    [Test]
    public async Task RealBrowserWindowPrivacySignalOverrideFansOutAcrossWindowPagesWithoutCrossWindowLeak()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        var baselineDevice = Device.DesktopFullHd;
        baselineDevice.DoNotTrack = false;
        baselineDevice.GlobalPrivacyControl = false;

        var isolatedWindowDevice = Device.DesktopFullHd;
        isolatedWindowDevice.DoNotTrack = true;
        isolatedWindowDevice.GlobalPrivacyControl = true;

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = baselineDevice,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var baselinePage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, baselinePage, "Browser-level privacy signal emulation must bootstrap the baseline page.").ConfigureAwait(false);

        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, server.CreatePageUrl("window-privacy-signal-baseline")).ConfigureAwait(false);
        var baselineSnapshot = await CapturePrivacySignalSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertPrivacySignalSnapshot(baselineSnapshot, baselineDevice, "browser baseline privacy signals");

        var isolatedWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            Device = isolatedWindowDevice,
        }).ConfigureAwait(false);
        var isolatedCurrentPage = (WebPage)isolatedWindow.CurrentPage;
        AssertResolvedDevice(isolatedCurrentPage, isolatedWindowDevice, "window privacy current page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedCurrentPage, "Window-scoped privacy signal emulation must bootstrap the target window page.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedCurrentPage, server.CreatePageUrl("window-privacy-signal-current")).ConfigureAwait(false);
        var isolatedCurrentSnapshot = await CapturePrivacySignalSnapshotAsync(isolatedCurrentPage).ConfigureAwait(false);
        AssertPrivacySignalSnapshot(isolatedCurrentSnapshot, isolatedWindowDevice, "window current privacy signals");

        var isolatedSiblingPage = (WebPage)await isolatedWindow.OpenPageAsync().ConfigureAwait(false);
        AssertResolvedDevice(isolatedSiblingPage, isolatedWindowDevice, "window privacy sibling page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedSiblingPage, "Window-scoped privacy signal emulation must fan out to new pages in the same window.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedSiblingPage, server.CreatePageUrl("window-privacy-signal-sibling")).ConfigureAwait(false);
        var isolatedSiblingSnapshot = await CapturePrivacySignalSnapshotAsync(isolatedSiblingPage).ConfigureAwait(false);
        AssertPrivacySignalSnapshot(isolatedSiblingSnapshot, isolatedWindowDevice, "window sibling privacy signals");

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, server.CreatePageUrl("window-privacy-signal-baseline-reloaded")).ConfigureAwait(false);
        var reloadedBaselineSnapshot = await CapturePrivacySignalSnapshotAsync(baselinePage).ConfigureAwait(false);
        AssertPrivacySignalSnapshot(reloadedBaselineSnapshot, baselineDevice, "reloaded baseline privacy signals");
    }

    [Test]
    public async Task RealBrowserBrowserVirtualMediaDevicesPersistAcrossNavigationReloadAndNewWindow()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        var mediaDeviceId = Guid.NewGuid().ToString("N")[..8];
        var mediaDevice = CreateVirtualMediaDevice(mediaDeviceId);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = mediaDevice,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var initialPage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, initialPage, "Browser-level virtual media devices must bootstrap the initial page.").ConfigureAwait(false);

        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, server.CreatePageUrl("browser-media-initial")).ConfigureAwait(false);
        var initialSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(initialPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(initialSnapshot, mediaDevice.VirtualMediaDevices!, "browser initial mediaDevices");

        var navigationUrl = server.CreatePageUrl("browser-media-navigation");
        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, navigationUrl).ConfigureAwait(false);
        var navigatedSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(initialPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(navigatedSnapshot, mediaDevice.VirtualMediaDevices!, "browser navigated mediaDevices");

        await NavigateToDeviceFingerprintPageAsync(browser, initialPage, navigationUrl).ConfigureAwait(false);
        var reloadedSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(initialPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(reloadedSnapshot, mediaDevice.VirtualMediaDevices!, "browser reloaded mediaDevices");

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        await AssertPageBootstrappedAsync(browser, secondPage, "Browser-level virtual media devices must bootstrap future windows too.").ConfigureAwait(false);

        await secondWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, secondPage, server.CreatePageUrl("browser-media-window")).ConfigureAwait(false);
        var secondWindowSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(secondPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(secondWindowSnapshot, mediaDevice.VirtualMediaDevices!, "browser new window mediaDevices");
    }

    [Test]
    public async Task RealBrowserWindowVirtualMediaDevicesFanOutAcrossWindowPagesWithoutCrossWindowLeak()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        var baselineDevice = Device.DesktopFullHd;
        var isolatedWindowDevice = CreateVirtualMediaDevice(Guid.NewGuid().ToString("N")[..8]);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = baselineDevice,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var baselinePage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, baselinePage, "Baseline page must stay bridge-backed before virtual media isolation checks.").ConfigureAwait(false);
        var baselineUrl = server.CreatePageUrl("window-media-baseline");
        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, baselineUrl).ConfigureAwait(false);
        var baselineSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(baselinePage, requestVideo: false).ConfigureAwait(false);
        AssertMediaDevicesSnapshotDoesNotLeak(baselineSnapshot, isolatedWindowDevice.VirtualMediaDevices!, "browser baseline mediaDevices");

        var isolatedWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            Device = isolatedWindowDevice,
        }).ConfigureAwait(false);
        var isolatedCurrentPage = (WebPage)isolatedWindow.CurrentPage;
        AssertResolvedDevice(isolatedCurrentPage, isolatedWindowDevice, "window media current page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedCurrentPage, "Window-scoped virtual media devices must bootstrap the target window page.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedCurrentPage, server.CreatePageUrl("window-media-current")).ConfigureAwait(false);
        var isolatedCurrentSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(isolatedCurrentPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(isolatedCurrentSnapshot, isolatedWindowDevice.VirtualMediaDevices!, "window current mediaDevices");

        var isolatedSiblingPage = (WebPage)await isolatedWindow.OpenPageAsync().ConfigureAwait(false);
        AssertResolvedDevice(isolatedSiblingPage, isolatedWindowDevice, "window media sibling page resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, isolatedSiblingPage, "Window-scoped virtual media devices must fan out to new pages in the same window.").ConfigureAwait(false);

        await isolatedWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, isolatedSiblingPage, server.CreatePageUrl("window-media-sibling")).ConfigureAwait(false);
        var isolatedSiblingSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(isolatedSiblingPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(isolatedSiblingSnapshot, isolatedWindowDevice.VirtualMediaDevices!, "window sibling mediaDevices");

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, baselinePage, baselineUrl).ConfigureAwait(false);
        var reloadedBaselineSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(baselinePage, requestVideo: false).ConfigureAwait(false);
        AssertMediaDevicesSnapshotDoesNotLeak(reloadedBaselineSnapshot, isolatedWindowDevice.VirtualMediaDevices!, "reloaded baseline mediaDevices");
    }

    [Test]
    public async Task RealBrowserPageVirtualMediaDevicesPersistAcrossNavigationReloadAndDoesNotLeakToSiblingPage()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        var baselineDevice = Device.DesktopFullHd;
        var isolatedPageDevice = CreateVirtualMediaDevice(Guid.NewGuid().ToString("N")[..8]);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = baselineDevice,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var siblingPage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, siblingPage, "Sibling baseline page must stay bridge-backed before page-level media isolation checks.").ConfigureAwait(false);
        var siblingUrl = server.CreatePageUrl("page-media-sibling");
        await NavigateToDeviceFingerprintPageAsync(browser, siblingPage, siblingUrl).ConfigureAwait(false);
        var siblingSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(siblingPage, requestVideo: false).ConfigureAwait(false);
        AssertMediaDevicesSnapshotDoesNotLeak(siblingSnapshot, isolatedPageDevice.VirtualMediaDevices!, "page sibling baseline mediaDevices");

        var targetPage = (WebPage)await ((WebWindow)browser.CurrentWindow).OpenPageAsync(new WebPageSettings
        {
            Device = isolatedPageDevice,
        }).ConfigureAwait(false);
        AssertResolvedDevice(targetPage, isolatedPageDevice, "page media target resolved device mismatch");
        await AssertPageBootstrappedAsync(browser, targetPage, "Page-scoped virtual media devices must bootstrap the target page.").ConfigureAwait(false);

        await ((WebWindow)browser.CurrentWindow).ActivateAsync().ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, server.CreatePageUrl("page-media-target")).ConfigureAwait(false);
        var targetSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(targetPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(targetSnapshot, isolatedPageDevice.VirtualMediaDevices!, "page target mediaDevices");

        var targetNavigationUrl = server.CreatePageUrl("page-media-navigation");
        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, targetNavigationUrl).ConfigureAwait(false);
        var navigatedTargetSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(targetPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(navigatedTargetSnapshot, isolatedPageDevice.VirtualMediaDevices!, "page navigated mediaDevices");

        await NavigateToDeviceFingerprintPageAsync(browser, targetPage, targetNavigationUrl).ConfigureAwait(false);
        var reloadedTargetSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(targetPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(reloadedTargetSnapshot, isolatedPageDevice.VirtualMediaDevices!, "page reloaded mediaDevices");

        await NavigateToDeviceFingerprintPageAsync(browser, siblingPage, siblingUrl).ConfigureAwait(false);
        var reloadedSiblingSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(siblingPage, requestVideo: false).ConfigureAwait(false);
        AssertMediaDevicesSnapshotDoesNotLeak(reloadedSiblingSnapshot, isolatedPageDevice.VirtualMediaDevices!, "reloaded sibling mediaDevices");
    }

    [Test]
    public async Task RealBrowserPageDirectMediaAttachmentUpdatesLiveMediaDevicesContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: string.Empty));
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: string.Empty));
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 320,
            Height = 180,
            Name = "Page Attach Camera",
            DeviceId = "page-attach-group",
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Page Attach Microphone",
            DeviceId = "page-attach-group",
        }).ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var page = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, page, "Page direct media attachment requires a bootstrapped current page.").ConfigureAwait(false);

        await page.AttachVirtualCameraAsync(camera).ConfigureAwait(false);
        await page.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false);

        var expectedMedia = page.ResolvedDevice?.VirtualMediaDevices;

        Assert.Multiple(() =>
        {
            Assert.That(page.AttachedVirtualCamera, Is.SameAs(camera), "Page direct media attachment must keep the attached camera on the page snapshot.");
            Assert.That(page.AttachedVirtualMicrophone, Is.SameAs(microphone), "Page direct media attachment must keep the attached microphone on the page snapshot.");
            Assert.That(expectedMedia, Is.Not.Null, "Page direct media attachment must materialize resolved virtual media settings.");
        });

        await NavigateToDeviceFingerprintPageAsync(browser, page, server.CreatePageUrl("page-direct-media-attachment")).ConfigureAwait(false);
        var snapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(page, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(snapshot, expectedMedia!, "page direct attachment mediaDevices");
    }

    [Test]
    public async Task RealBrowserBrowserAndWindowDirectMediaAttachmentUpdateLiveCurrentPageContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserDeviceFingerprintLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        using var browserCameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: string.Empty));
        using var browserMicrophoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: string.Empty));
        await using var browserCamera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 320,
            Height = 180,
            Name = "Browser Attach Camera",
            DeviceId = "browser-attach-group",
        }).ConfigureAwait(false);
        await using var browserMicrophone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Browser Attach Microphone",
            DeviceId = "browser-attach-group",
        }).ConfigureAwait(false);

        browserCameraOverride.Dispose();
        browserMicrophoneOverride.Dispose();

        using var windowCameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: string.Empty));
        using var windowMicrophoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: string.Empty));
        await using var windowCamera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 320,
            Height = 180,
            Name = "Window Attach Camera",
            DeviceId = "window-attach-group",
        }).ConfigureAwait(false);
        await using var windowMicrophone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Window Attach Microphone",
            DeviceId = "window-attach-group",
        }).ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);

        var window = (WebWindow)browser.CurrentWindow;
        var browserPage = (WebPage)browser.CurrentPage;
        await AssertPageBootstrappedAsync(browser, browserPage, "Browser direct media attachment requires a bootstrapped current page.").ConfigureAwait(false);

        await browser.AttachVirtualCameraAsync(browserCamera).ConfigureAwait(false);
        await browser.AttachVirtualMicrophoneAsync(browserMicrophone).ConfigureAwait(false);

        var browserExpectedMedia = browserPage.ResolvedDevice?.VirtualMediaDevices;

        Assert.Multiple(() =>
        {
            Assert.That(browserPage.AttachedVirtualCamera, Is.SameAs(browserCamera), "Browser direct camera attachment must target the current page snapshot.");
            Assert.That(browserPage.AttachedVirtualMicrophone, Is.SameAs(browserMicrophone), "Browser direct microphone attachment must target the current page snapshot.");
            Assert.That(browserExpectedMedia, Is.Not.Null, "Browser direct media attachment must materialize resolved virtual media settings.");
        });

        await NavigateToDeviceFingerprintPageAsync(browser, browserPage, server.CreatePageUrl("browser-direct-media-attachment")).ConfigureAwait(false);
        var browserSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(browserPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(browserSnapshot, browserExpectedMedia!, "browser direct attachment mediaDevices");

        var windowPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, windowPage, "Window direct media attachment requires a bootstrapped current page.").ConfigureAwait(false);

        await window.AttachVirtualCameraAsync(windowCamera).ConfigureAwait(false);
        await window.AttachVirtualMicrophoneAsync(windowMicrophone).ConfigureAwait(false);

        var windowExpectedMedia = windowPage.ResolvedDevice?.VirtualMediaDevices;

        Assert.Multiple(() =>
        {
            Assert.That(windowPage.AttachedVirtualCamera, Is.SameAs(windowCamera), "Window direct camera attachment must target the current page snapshot.");
            Assert.That(windowPage.AttachedVirtualMicrophone, Is.SameAs(windowMicrophone), "Window direct microphone attachment must target the current page snapshot.");
            Assert.That(windowExpectedMedia, Is.Not.Null, "Window direct media attachment must materialize resolved virtual media settings.");
        });

        await NavigateToDeviceFingerprintPageAsync(browser, windowPage, server.CreatePageUrl("window-direct-media-attachment")).ConfigureAwait(false);
        var windowSnapshot = await CaptureMediaDevicesSurfaceSnapshotAsync(windowPage, requestVideo: true).ConfigureAwait(false);
        AssertMediaDevicesSnapshot(windowSnapshot, windowExpectedMedia!, "window direct attachment mediaDevices");
    }

    [Test]
    public async Task RealBrowserOpenPageInNonCurrentWindowPreservesBrowserCurrentBoundaryAndBootstrapsTargetPage()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowInitialPage = (WebPage)firstWindow.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;
        var openedPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var bootstrapped = IsBridgeCommandsBound(openedPage);
        var diagnostics = bootstrapped ? null : await DescribeBootstrapFailureAsync(browser, openedPage).ConfigureAwait(false);

        Assert.That(bootstrapped, Is.True, $"OpenPageAsync в non-current окне должен дождаться bridge-bootstrap новой вкладки до возврата. {diagnostics}");

        var title = await openedPage.GetTitleAsync().ConfigureAwait(false);
        var url = await openedPage.GetUrlAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(openedPage));
            Assert.That(firstWindow.CurrentPage, Is.Not.SameAs(firstWindowInitialPage));
            Assert.That(browser.Pages, Has.Some.SameAs(openedPage));
            Assert.That(title, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
        });
    }

    [Test]
    public async Task RealBrowserWindowActivateAndCloseSurfaceStaysOperational()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        var bootstrapped = IsBridgeCommandsBound(secondPage);
        var diagnostics = bootstrapped ? null : await DescribeBootstrapFailureAsync(browser, secondPage).ConfigureAwait(false);

        Assert.That(bootstrapped, Is.True, $"ActivateAsync и CloseAsync для bridge-opened окна должны работать по уже bootstrap-нутой вкладке. {diagnostics}");

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await secondWindow.CloseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.IsDisposed, Is.False);
            Assert.That(firstPage.IsDisposed, Is.False);
            Assert.That(secondWindow.IsDisposed, Is.True);
            Assert.That(secondPage.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(firstPage));
            Assert.That(browser.Windows, Has.None.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.None.SameAs(secondPage));
        });
    }

    [Test]
    public async Task RealBrowserActivateNonCurrentBridgeOpenedWindowRepublishesBrowserCurrentSnapshots()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        var thirdWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var thirdPage = (WebPage)thirdWindow.CurrentPage;

        Assert.That(IsBridgeCommandsBound(secondPage), Is.True);

        await secondWindow.ActivateAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.IsDisposed, Is.False);
            Assert.That(firstPage.IsDisposed, Is.False);
            Assert.That(secondWindow.IsDisposed, Is.False);
            Assert.That(secondPage.IsDisposed, Is.False);
            Assert.That(thirdWindow.IsDisposed, Is.False);
            Assert.That(thirdPage.IsDisposed, Is.False);
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondPage));
            Assert.That(browser.CurrentWindow, Is.Not.SameAs(thirdWindow));
            Assert.That(browser.CurrentPage, Is.Not.SameAs(thirdPage));
        });
    }

    [Test]
    public async Task RealBrowserCloseNonCurrentBridgeOpenedWindowKeepsOtherCurrentWindowStableAcrossMultipleWindows()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        var thirdWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var thirdPage = (WebPage)thirdWindow.CurrentPage;

        Assert.That(IsBridgeCommandsBound(secondPage), Is.True);

        await secondWindow.CloseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.IsDisposed, Is.False);
            Assert.That(firstPage.IsDisposed, Is.False);
            Assert.That(secondWindow.IsDisposed, Is.True);
            Assert.That(secondPage.IsDisposed, Is.True);
            Assert.That(thirdWindow.IsDisposed, Is.False);
            Assert.That(thirdPage.IsDisposed, Is.False);
            Assert.That(browser.CurrentWindow, Is.SameAs(thirdWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(thirdPage));
            Assert.That(browser.Windows, Has.None.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.None.SameAs(secondPage));
        });
    }

    [Test]
    public async Task RealBrowserClosedWindowRetainsFinalPageSnapshotWhileCurrentLookupSkipsDisposedChildren()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, secondPage, "Closed-window snapshot parity requires a bootstrapped bridge-opened page.").ConfigureAwait(false);

        var closedTarget = new LookupTarget(
            "Real Browser Closed Window Snapshot",
            new Uri("https://127.0.0.1/real-browser-lookup/closed-window-snapshot"),
            "closed-window-snapshot",
            "closed");

        await NavigateToLookupTargetAsync(secondPage, closedTarget).ConfigureAwait(false);
        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await secondWindow.CloseAsync().ConfigureAwait(false);

        var browserCurrentWindow = await browser.GetWindowAsync("current").ConfigureAwait(false);
        var browserCurrentPage = await browser.GetPageAsync("current").ConfigureAwait(false);
        var windowByClosedTitle = await browser.GetWindowAsync(closedTarget.Title).ConfigureAwait(false);
        var pageByClosedUrl = await browser.GetPageAsync(closedTarget.Url).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(secondWindow.IsDisposed, Is.True);
            Assert.That(secondPage.IsDisposed, Is.True);
            Assert.That(secondPage.CurrentTitle, Is.EqualTo(closedTarget.Title));
            Assert.That(secondPage.CurrentUrl, Is.EqualTo(closedTarget.Url));

            Assert.That(browserCurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browserCurrentPage, Is.SameAs(firstPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(firstPage));

            Assert.That(windowByClosedTitle, Is.Null);
            Assert.That(pageByClosedUrl, Is.Null);
            Assert.That(browser.Windows, Has.None.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.None.SameAs(secondPage));
        });
    }

    [Test]
    public async Task RealBrowserCookieSurfaceOnBridgeOpenedPageInNonCurrentWindowKeepsTargetOperationalWithoutChangingCurrentWindow()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;
        var openedPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);

        await openedPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]).ConfigureAwait(false);

        var openedPageCookies = (await openedPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var openedPageDocumentCookie = (await openedPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;

        await openedPage.ClearAllCookiesAsync().ConfigureAwait(false);

        var openedPageCookiesAfterClear = (await openedPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var openedPageDocumentCookieAfterClear = (await openedPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var openedPageAfterSetDiagnostics = openedPageDocumentCookie.Contains("session=alpha", StringComparison.Ordinal)
            ? null
            : await DescribeDocumentCookieFailureAsync(browser, openedPage, openedPageDocumentCookie).ConfigureAwait(false);
        var openedPageAfterClearDiagnostics = string.IsNullOrEmpty(openedPageDocumentCookieAfterClear)
            ? null
            : await DescribeDocumentCookieFailureAsync(browser, openedPage, openedPageDocumentCookieAfterClear).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(openedPage));

            Assert.That(openedPageCookies, Has.Length.EqualTo(1));
            Assert.That(openedPageCookies[0].Name, Is.EqualTo("session"));
            Assert.That(openedPageCookies[0].Value, Is.EqualTo("alpha"));
            Assert.That(openedPageDocumentCookie, Does.Contain("session=alpha"), openedPageAfterSetDiagnostics);
            Assert.That(openedPageCookiesAfterClear, Is.Empty);
            Assert.That(openedPageDocumentCookieAfterClear, Is.Empty, openedPageAfterClearDiagnostics);
        });
    }

    [Test]
    public async Task RealBrowserLookupSurfaceSeparatesWindowCurrentTitleFromAnyPageUrlAndElementOwnership()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowNonCurrentPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync(new WebPageSettings()).ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings()).ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, firstWindowCurrentPage, "Lookup surface requires a bootstrapped first-window current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "Lookup surface requires a bootstrapped second-window current page.").ConfigureAwait(false);

        var firstNonCurrentTarget = new LookupTarget("Lookup Window One Hidden", new Uri("https://127.0.0.1/real-browser-lookup/window-one-hidden"), "lookup-window-one-hidden", "alpha");
        var firstCurrentTarget = new LookupTarget("Lookup Window One Current", new Uri("https://127.0.0.1/real-browser-lookup/window-one-current"), "lookup-window-one-current", "beta");

        await NavigateToLookupTargetAsync(firstWindowNonCurrentPage, firstNonCurrentTarget).ConfigureAwait(false);
        await NavigateToLookupTargetAsync(firstWindowCurrentPage, firstCurrentTarget).ConfigureAwait(false);

        var secondWindowCurrentTitle = await secondWindowCurrentPage.GetTitleAsync().ConfigureAwait(false);
        var secondWindowCurrentUrl = await secondWindowCurrentPage.GetUrlAsync().ConfigureAwait(false);
        var secondCurrentElement = await secondWindowCurrentPage.GetElementAsync("body").ConfigureAwait(false);
        var browserCurrentWindow = await browser.GetWindowAsync("current").ConfigureAwait(false);
        var browserCurrentPage = await browser.GetPageAsync("current").ConfigureAwait(false);
        var windowByFirstHiddenTitle = await browser.GetWindowAsync(firstNonCurrentTarget.Title).ConfigureAwait(false);
        var windowByFirstCurrentTitle = await browser.GetWindowAsync(firstCurrentTarget.Title).ConfigureAwait(false);
        var windowByFirstHiddenUrl = await browser.GetWindowAsync(firstNonCurrentTarget.Url).ConfigureAwait(false);
        var pageByFirstHiddenTitle = await browser.GetPageAsync(firstNonCurrentTarget.Title).ConfigureAwait(false);
        var pageByFirstHiddenUrl = await browser.GetPageAsync(firstNonCurrentTarget.Url).ConfigureAwait(false);
        var windowBySecondCurrentElement = await browser.GetWindowAsync(secondCurrentElement!).ConfigureAwait(false);
        var pageBySecondCurrentElement = await browser.GetPageAsync(secondCurrentElement!).ConfigureAwait(false);
        var firstWindowCurrentPageByName = await firstWindow.GetPageAsync("current").ConfigureAwait(false);
        var firstWindowHiddenPageByTitle = await firstWindow.GetPageAsync(firstNonCurrentTarget.Title).ConfigureAwait(false);
        var firstWindowHiddenPageByUrl = await firstWindow.GetPageAsync(firstNonCurrentTarget.Url).ConfigureAwait(false);
        var firstWindowForeignElementLookup = await firstWindow.GetPageAsync(secondCurrentElement!).ConfigureAwait(false);
        var secondWindowOwnElementLookup = await secondWindow.GetPageAsync(secondCurrentElement!).ConfigureAwait(false);
        var frameByName = await secondWindowCurrentPage.GetFrameAsync(nameof(IWebPage.MainFrame)).ConfigureAwait(false);
        var frameByUrl = await secondWindowCurrentPage.GetFrameAsync(secondWindowCurrentUrl!).ConfigureAwait(false);
        var frameByElement = await secondWindowCurrentPage.GetFrameAsync(secondCurrentElement!).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browserCurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browserCurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));

            Assert.That(windowByFirstHiddenTitle, Is.Null, "Browser window lookup by title must consider only each window's current page title.");
            Assert.That(windowByFirstCurrentTitle, Is.SameAs(firstWindow));
            Assert.That(windowByFirstHiddenUrl, Is.SameAs(firstWindow), "Browser window lookup by URL must resolve any page inside the window.");

            Assert.That(pageByFirstHiddenTitle, Is.SameAs(firstWindowNonCurrentPage));
            Assert.That(pageByFirstHiddenUrl, Is.SameAs(firstWindowNonCurrentPage));
            Assert.That(windowBySecondCurrentElement, Is.SameAs(secondWindow));
            Assert.That(pageBySecondCurrentElement, Is.SameAs(secondWindowCurrentPage));

            Assert.That(firstWindowCurrentPageByName, Is.SameAs(firstWindowCurrentPage));
            Assert.That(firstWindowHiddenPageByTitle, Is.SameAs(firstWindowNonCurrentPage));
            Assert.That(firstWindowHiddenPageByUrl, Is.SameAs(firstWindowNonCurrentPage));
            Assert.That(firstWindowForeignElementLookup, Is.Null);
            Assert.That(secondWindowOwnElementLookup, Is.SameAs(secondWindowCurrentPage));

            Assert.That(frameByName, Is.SameAs(secondWindowCurrentPage.MainFrame));
            Assert.That(frameByUrl, Is.SameAs(secondWindowCurrentPage.MainFrame));
            Assert.That(frameByElement, Is.SameAs(secondWindowCurrentPage.MainFrame));
        });
    }

    [Test]
    public async Task RealBrowserNonCurrentPageDomSurfaceRemainsLocalAcrossWindowsAndTabs()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var nonCurrentPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync(new WebPageSettings()).ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings()).ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, firstWindowCurrentPage, "DOM surface requires a bootstrapped first-window current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "DOM surface requires a bootstrapped second-window current page.").ConfigureAwait(false);

        var nonCurrentTarget = new LookupTarget("DOM Surface NonCurrent", new Uri("https://127.0.0.1/real-browser-dom/non-current"), "dom-surface-non-current", "alpha");
        var firstCurrentTarget = new LookupTarget("DOM Surface Window Current", new Uri("https://127.0.0.1/real-browser-dom/window-current"), "dom-surface-window-current", "beta");

        await NavigateToLookupTargetAsync(nonCurrentPage, nonCurrentTarget).ConfigureAwait(false);
        await NavigateToLookupTargetAsync(firstWindowCurrentPage, firstCurrentTarget).ConfigureAwait(false);

        var browserCurrentUrl = await secondWindowCurrentPage.GetUrlAsync().ConfigureAwait(false);
        var waitedByString = await secondWindowCurrentPage.WaitForElementAsync("body").ConfigureAwait(false);
        var waitedByTimeout = await secondWindowCurrentPage.WaitForElementAsync("body", TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var waitedByKind = await secondWindowCurrentPage.WaitForElementAsync("body", WaitForElementKind.Attached).ConfigureAwait(false);
        var waitedBySettings = await secondWindowCurrentPage.WaitForElementAsync(new WaitForElementSettings
        {
            Selector = ElementSelector.Css("body"),
            Timeout = TimeSpan.FromSeconds(1),
        }).ConfigureAwait(false);
        var waitedBySelector = await secondWindowCurrentPage.WaitForElementAsync(ElementSelector.Css("body"), WaitForElementKind.Attached, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var elementByString = await secondWindowCurrentPage.GetElementAsync("body").ConfigureAwait(false);
        var elementByCssSelector = await secondWindowCurrentPage.GetElementAsync(new CssSelector("body")).ConfigureAwait(false);
        var elementsByString = (await secondWindowCurrentPage.GetElementsAsync("body").ConfigureAwait(false)).ToArray();
        var elementsBySelector = (await secondWindowCurrentPage.GetElementsAsync(ElementSelector.Css("body")).ConfigureAwait(false)).ToArray();
        var shadowRoot = await secondWindowCurrentPage.GetShadowRootAsync("body").ConfigureAwait(false);
        var frameByName = await secondWindowCurrentPage.GetFrameAsync(nameof(IWebPage.MainFrame)).ConfigureAwait(false);
        var frameByUrl = await secondWindowCurrentPage.GetFrameAsync(browserCurrentUrl!).ConfigureAwait(false);
        var frameByElement = await secondWindowCurrentPage.GetFrameAsync(elementByString!).ConfigureAwait(false);
        var frameTitle = await frameByName!.GetTitleAsync().ConfigureAwait(false);
        var frameUrl = await frameByUrl!.GetUrlAsync().ConfigureAwait(false);
        var screenshot = await secondWindowCurrentPage.GetScreenshotAsync().ConfigureAwait(false);
        var isVisible = await secondWindowCurrentPage.IsVisibleAsync().ConfigureAwait(false);
        var viewport = await secondWindowCurrentPage.GetViewportSizeAsync().ConfigureAwait(false);
        var inlineScriptLink = new Uri($"data:text/javascript;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes("window.__atomLinkProbe = 'linked';"))}");

        await secondWindowCurrentPage.InjectScriptAsync("window.__atomNoopProbe = 1;").ConfigureAwait(false);
        await secondWindowCurrentPage.InjectScriptAsync("window.__atomHeadNoopProbe = 1;", injectToHead: true).ConfigureAwait(false);
        await secondWindowCurrentPage.InjectScriptLinkAsync(inlineScriptLink).ConfigureAwait(false);
        await secondWindowCurrentPage.SubscribeAsync("app.ready").ConfigureAwait(false);
        var subscribed = HasCallbackSubscription(secondWindowCurrentPage, "app.ready");
        await secondWindowCurrentPage.UnSubscribeAsync("app.ready").ConfigureAwait(false);
        var unsubscribed = !HasCallbackSubscription(secondWindowCurrentPage, "app.ready");
        var injectedProbe = await secondWindowCurrentPage.EvaluateAsync<string>("String(window.__atomNoopProbe ?? '')").ConfigureAwait(false);
        var injectedHeadProbe = await secondWindowCurrentPage.EvaluateAsync<string>("String(window.__atomHeadNoopProbe ?? '')").ConfigureAwait(false);
        var injectedLinkProbe = await secondWindowCurrentPage.EvaluateAsync<string>("String(window.__atomLinkProbe ?? '')").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(nonCurrentPage.CurrentTitle, Is.EqualTo(nonCurrentTarget.Title));
            Assert.That(nonCurrentPage.CurrentUrl, Is.EqualTo(nonCurrentTarget.Url));
            Assert.That(firstWindowCurrentPage.CurrentTitle, Is.EqualTo(firstCurrentTarget.Title));
            Assert.That(firstWindowCurrentPage.CurrentUrl, Is.EqualTo(firstCurrentTarget.Url));

            Assert.That(waitedByString, Is.Not.Null);
            Assert.That(waitedByTimeout, Is.Not.Null);
            Assert.That(waitedByKind, Is.Not.Null);
            Assert.That(waitedBySettings, Is.Not.Null);
            Assert.That(waitedBySelector, Is.Not.Null);
            Assert.That(elementByString, Is.Not.Null);
            Assert.That(elementByCssSelector, Is.Not.Null);
            Assert.That(elementsByString, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(elementsBySelector, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(shadowRoot, Is.Null);

            Assert.That(frameByName, Is.SameAs(secondWindowCurrentPage.MainFrame));
            Assert.That(frameByUrl, Is.SameAs(secondWindowCurrentPage.MainFrame));
            Assert.That(frameByElement, Is.SameAs(secondWindowCurrentPage.MainFrame));
            Assert.That(frameTitle, Is.Not.Null.And.Not.Empty);
            Assert.That(frameUrl, Is.EqualTo(browserCurrentUrl));

            Assert.That(screenshot.Length, Is.GreaterThanOrEqualTo(0));
            Assert.That(isVisible, Is.True);
            Assert.That(viewport, Is.Not.Null);
            Assert.That(injectedProbe, Is.EqualTo("1"));
            Assert.That(injectedHeadProbe, Is.EqualTo("1"));
            Assert.That(injectedLinkProbe, Is.EqualTo("linked"));
            Assert.That(subscribed, Is.True);
            Assert.That(unsubscribed, Is.True);
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageShadowRootSurfaceSupportsScopedQueries()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Shadow-root surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Shadow Root';
                document.body.innerHTML = '<div id="shadow-host"></div>';

                const host = document.getElementById('shadow-host');
                if (!host) {
                    return;
                }

                const shadow = host.attachShadow({ mode: 'open' });
                shadow.innerHTML = `<span id="inner-node">inner-text</span><div id="nested-host"></div>`;
                const nestedHost = shadow.getElementById('nested-host');
                if (!nestedHost) {
                    return;
                }

                nestedHost.attachShadow({ mode: 'open' }).innerHTML = `<b id="deep-node">deep-text</b>`;
            })();
            """).ConfigureAwait(false);

        var shadowState = await page.EvaluateAsync<string>(
            "JSON.stringify({ title: document.title ?? null, hostPresent: document.getElementById('shadow-host') !== null, shadowRootPresent: document.getElementById('shadow-host')?.shadowRoot !== null, body: document.body?.innerHTML ?? null })")
            .ConfigureAwait(false);
        var livePageUrl = await page.GetUrlAsync().ConfigureAwait(false);
        var shadowRoot = await page.GetShadowRootAsync("#shadow-host").ConfigureAwait(false);
        Assert.That(shadowRoot, Is.Not.Null, $"Shadow-root lookup must return the live open shadow root. state={shadowState ?? "<null>"}");
        var innerElement = await shadowRoot!.GetElementAsync("#inner-node").ConfigureAwait(false);
        var shadowContent = await shadowRoot.GetContentAsync().ConfigureAwait(false);
        var shadowText = await shadowRoot.EvaluateAsync<string>("return shadowRoot.querySelector('#inner-node')?.textContent ?? ''").ConfigureAwait(false);
        var shadowTitle = await shadowRoot.GetTitleAsync().ConfigureAwait(false);
        var shadowUrl = await shadowRoot.GetUrlAsync().ConfigureAwait(false);
        var nestedShadowRoot = await shadowRoot.GetShadowRootAsync("#nested-host").ConfigureAwait(false);
        Assert.That(nestedShadowRoot, Is.Not.Null, $"Nested shadow-root lookup must return the live nested open shadow root. state={shadowState ?? "<null>"}");
        var nestedText = await nestedShadowRoot!.EvaluateAsync<string>("return shadowRoot.querySelector('#deep-node')?.textContent ?? ''").ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(shadowRoot, Is.Not.Null);
            Assert.That(innerElement, Is.Not.Null);
            Assert.That(await innerElement!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("inner-text"));
            Assert.That(shadowContent, Does.Contain("inner-text"));
            Assert.That(shadowText, Is.EqualTo("inner-text"));
            Assert.That(shadowTitle, Is.EqualTo("Real Browser Shadow Root"));
            Assert.That(shadowUrl, Is.EqualTo(livePageUrl));
            Assert.That(nestedShadowRoot, Is.Not.Null);
            Assert.That(nestedText, Is.EqualTo("deep-text"));
            Assert.That(shadowRoot.Page, Is.SameAs(page));
            Assert.That(shadowRoot.Frame, Is.SameAs(page.MainFrame));
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageFrameExecutionSurfaceObservesChildIframe()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Iframe live surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Iframe';
                document.body.dataset.frameKind = 'root';
                document.body.innerHTML = `<iframe id="frame-probe-host" srcdoc="<html><body data-frame-kind='child'><span id='frame-probe'>child-ready</span></body></html>"></iframe>`;
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#frame-probe-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var selectorFound = await page.WaitForSelectorInFramesAsync("#frame-probe", TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        var frameStates = await page.ExecuteInAllFramesWithMetadataAsync(
            "JSON.stringify({ frameElementId: globalThis.frameElement?.id ?? null, frameKind: document.body?.dataset?.frameKind ?? null, marker: document.getElementById('frame-probe')?.textContent ?? null, title: document.title ?? '' })",
            isolatedWorld: false).ConfigureAwait(false);
        var snapshots = ReadFrameExecutionSnapshots(frameStates);
        var rootSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "root");
        var childSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "child");
        var frameDiagnostics = frameStates?.GetRawText() ?? "<null>";

        Assert.Multiple(() =>
        {
            Assert.That(frameHost, Is.Not.Null, "Iframe host must be discoverable on the live page before probing frame execution.");
            Assert.That(selectorFound, Is.True, $"Frame selector polling must observe iframe-local markup. payload={frameDiagnostics}");
            Assert.That(snapshots.Count, Is.GreaterThanOrEqualTo(2), $"Frame execution must surface at least the main frame and one child iframe. payload={frameDiagnostics}");
            Assert.That(rootSnapshot, Is.Not.Null, $"Frame execution metadata must include the root document snapshot. payload={frameDiagnostics}");
            Assert.That(rootSnapshot?.Title, Is.EqualTo("Real Browser Iframe"), $"Root frame snapshot must preserve the live document title. payload={frameDiagnostics}");
            Assert.That(childSnapshot, Is.Not.Null, $"Frame execution metadata must include the iframe document snapshot. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Status, Is.EqualTo("ok"), $"Iframe execution result must complete successfully. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.FrameElementId, Is.EqualTo("frame-probe-host"), $"Iframe snapshot must resolve the live host element id. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Marker, Is.EqualTo("child-ready"), $"Iframe snapshot must observe the child document probe marker. payload={frameDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageFrameExecutionSurfaceObservesAboutBlankChildIframe()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "about:blank iframe live surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser About Blank Iframe';
                document.body.dataset.frameKind = 'root';
                document.body.replaceChildren();

                const frame = document.createElement('iframe');
                frame.id = 'blank-frame-host';
                document.body.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write(`<html><head><title>About Blank Child</title></head><body data-frame-kind="about-blank-child"><span id="blank-probe">blank-ready</span></body></html>`);
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#blank-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var selectorFound = await page.WaitForSelectorInFramesAsync("#blank-probe", TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        var frameStates = await page.ExecuteInAllFramesWithMetadataAsync(
            "JSON.stringify({ frameElementId: globalThis.frameElement?.id ?? null, frameKind: document.body?.dataset?.frameKind ?? null, marker: document.getElementById('blank-probe')?.textContent ?? null, title: document.title ?? '' })",
            isolatedWorld: false).ConfigureAwait(false);
        var snapshots = ReadFrameExecutionSnapshots(frameStates);
        var rootSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "root");
        var childSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "about-blank-child");
        var frameDiagnostics = frameStates?.GetRawText() ?? "<null>";

        Assert.Multiple(() =>
        {
            Assert.That(frameHost, Is.Not.Null, "about:blank iframe host must be discoverable on the live page before probing frame execution.");
            Assert.That(selectorFound, Is.True, $"Frame selector polling must observe about:blank iframe-local markup. payload={frameDiagnostics}");
            Assert.That(snapshots.Count, Is.GreaterThanOrEqualTo(2), $"Frame execution must surface the main frame and the about:blank child iframe. payload={frameDiagnostics}");
            Assert.That(rootSnapshot, Is.Not.Null, $"Frame execution metadata must include the root document snapshot. payload={frameDiagnostics}");
            Assert.That(rootSnapshot?.Title, Is.EqualTo("Real Browser About Blank Iframe"), $"Root frame snapshot must preserve the live document title. payload={frameDiagnostics}");
            Assert.That(childSnapshot, Is.Not.Null, $"Frame execution metadata must include the about:blank iframe document snapshot. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Status, Is.EqualTo("ok"), $"about:blank iframe execution result must complete successfully. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Title, Is.EqualTo("About Blank Child"), $"about:blank iframe snapshot must preserve the child document title. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.FrameElementId, Is.EqualTo("blank-frame-host"), $"about:blank iframe snapshot must resolve the live host element id. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Marker, Is.EqualTo("blank-ready"), $"about:blank iframe snapshot must observe the child document probe marker. payload={frameDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageFrameExecutionSurfaceObservesIframeHostedInsideShadowRoot()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Shadow-hosted iframe live surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Shadow Hosted Iframe';
                document.body.dataset.frameKind = 'root';
                document.body.replaceChildren();

                const host = document.createElement('div');
                host.id = 'shadow-frame-wrapper';
                document.body.appendChild(host);

                const shadow = host.attachShadow({ mode: 'open' });
                const frame = document.createElement('iframe');
                frame.id = 'shadow-frame-host';
                shadow.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><body data-frame-kind="shadow-frame-child"><div id="child-shadow-host"></div></body></html>');
                childDocument.close();

                const childHost = frame.contentWindow?.document.getElementById('child-shadow-host');
                if (!childHost) {
                    return;
                }

                const childShadow = childHost.attachShadow({ mode: 'open' });
                childShadow.innerHTML = '<span id="shadow-frame-probe">shadow-frame-ready</span>';
            })();
            """).ConfigureAwait(false);

        var hostState = await page.EvaluateAsync<string>(
            "JSON.stringify({ hostPresent: document.getElementById('shadow-frame-wrapper') !== null, framePresent: document.getElementById('shadow-frame-wrapper')?.shadowRoot?.querySelector('#shadow-frame-host') !== null })")
            .ConfigureAwait(false);
        var frameStates = await page.ExecuteInAllFramesWithMetadataAsync(
            "JSON.stringify({ frameElementId: globalThis.frameElement?.id ?? null, frameKind: document.body?.dataset?.frameKind ?? null, marker: document.getElementById('child-shadow-host')?.shadowRoot?.querySelector('#shadow-frame-probe')?.textContent ?? null, title: document.title ?? '' })",
            isolatedWorld: false).ConfigureAwait(false);
        var snapshots = ReadFrameExecutionSnapshots(frameStates);
        var rootSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "root");
        var childSnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.FrameKind == "shadow-frame-child");
        var frameDiagnostics = frameStates?.GetRawText() ?? "<null>";

        Assert.Multiple(() =>
        {
            Assert.That(hostState, Does.Contain("\"hostPresent\":true"), $"Shadow host must exist on the live page before probing child frame execution. state={hostState ?? "<null>"}");
            Assert.That(hostState, Does.Contain("\"framePresent\":true"), $"Iframe hosted inside the live shadow root must exist before probing frame execution. state={hostState ?? "<null>"}");
            Assert.That(snapshots.Count, Is.GreaterThanOrEqualTo(2), $"Frame execution must surface the main frame and the child iframe hosted inside the shadow root. payload={frameDiagnostics}");
            Assert.That(rootSnapshot, Is.Not.Null, $"Frame execution metadata must include the root document snapshot. payload={frameDiagnostics}");
            Assert.That(rootSnapshot?.Title, Is.EqualTo("Real Browser Shadow Hosted Iframe"), $"Root frame snapshot must preserve the live document title. payload={frameDiagnostics}");
            Assert.That(childSnapshot, Is.Not.Null, $"Frame execution metadata must include the iframe hosted inside the shadow root. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Status, Is.EqualTo("ok"), $"Shadow-hosted iframe execution result must complete successfully. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.FrameElementId, Is.EqualTo("shadow-frame-host"), $"Shadow-hosted iframe snapshot must resolve the live host element id. payload={frameDiagnostics}");
            Assert.That(childSnapshot?.Marker, Is.EqualTo("shadow-frame-ready"), $"Shadow-hosted iframe snapshot must observe shadow-root content inside the child document. payload={frameDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageChildIframeObjectModelMaterializesShadowHostedLiveFrameContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Shadow-hosted child iframe object model requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Shadow Hosted Child Iframe';
                document.body.replaceChildren();

                const host = document.createElement('div');
                host.id = 'shadow-frame-wrapper';
                document.body.appendChild(host);

                const shadow = host.attachShadow({ mode: 'open' });
                const frame = document.createElement('iframe');
                frame.id = 'shadow-frame-host';
                frame.name = 'live-shadow-child-frame';
                frame.src = 'about:blank#live-shadow-child-frame';
                shadow.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Shadow Hosted Child Frame</title></head><body><span id="child-shadow-frame-probe">shadow-child-object-ready</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var shadowRoot = await page.GetShadowRootAsync("#shadow-frame-wrapper").ConfigureAwait(false);
        var frameHost = shadowRoot is null
            ? null
            : await shadowRoot.WaitForElementAsync("#shadow-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var frameByName = await page.GetFrameAsync("live-shadow-child-frame").ConfigureAwait(false);
        var mainChildFrames = (await page.MainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var childFrame = mainChildFrames.SingleOrDefault();
        var pageFrames = page.Frames.ToArray();
        var hostChildFrames = frameHost is null
            ? []
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var frameElement = childFrame is null ? null : await childFrame.GetFrameElementAsync().ConfigureAwait(false);
        var frameHostHandle = frameHost is null ? null : await frameHost.GetElementHandleAsync().ConfigureAwait(false);
        var frameElementHandle = frameElement is null ? null : await frameElement.GetElementHandleAsync().ConfigureAwait(false);
        var parentFrame = childFrame is null ? null : await childFrame.GetParentFrameAsync().ConfigureAwait(false);
        var contentFrame = childFrame is null ? null : await childFrame.GetContentFrameAsync().ConfigureAwait(false);
        var frameTitle = childFrame is null ? null : await childFrame.GetTitleAsync().ConfigureAwait(false);
        var frameUrl = childFrame is null ? null : await childFrame.GetUrlAsync().ConfigureAwait(false);
        var frameProbe = childFrame is null
            ? null
            : await childFrame.WaitForElementAsync("#child-shadow-frame-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var frameText = childFrame is null
            ? null
            : await childFrame.EvaluateAsync<string>("return document.getElementById('child-shadow-frame-probe')?.textContent ?? null").ConfigureAwait(false);
        var frameByElement = frameProbe is null ? null : await page.GetFrameAsync(frameProbe).ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(shadowRoot, Is.Not.Null, "Open shadow root must remain discoverable on the live page before probing shadow-hosted child frame materialization.");
            Assert.That(frameHost, Is.Not.Null, "Iframe host inside the open shadow root must be discoverable through the shadow-root scope.");
            Assert.That(frameByName, Is.Not.Null, "Name-based page lookup must materialize the shadow-hosted child frame without touching the host element path first.");
            Assert.That(mainChildFrames, Has.Length.EqualTo(1), "Main frame discovery must materialize a same-origin iframe hosted inside an open shadow root.");
            Assert.That(childFrame, Is.Not.Null, "Shadow-hosted iframe must materialize a child frame runtime object.");
            Assert.That(frameByName, Is.SameAs(childFrame));
            Assert.That(pageFrames, Has.Length.EqualTo(2), "Page frame snapshot must include the main frame and the shadow-hosted child frame once discovered.");
            Assert.That(pageFrames[0], Is.SameAs(page.MainFrame));
            Assert.That(pageFrames[1], Is.SameAs(childFrame));
            Assert.That(hostChildFrames, Has.Length.EqualTo(1), "Iframe host inside the shadow root must still expose the same child frame through the host path.");
            Assert.That(hostChildFrames[0], Is.SameAs(childFrame));
            Assert.That(frameElement, Is.Not.Null, "Shadow-hosted child frame must still expose a concrete iframe host element.");
            Assert.That(frameElementHandle, Is.EqualTo(frameHostHandle), "Shadow-hosted child frame must point back to the same iframe host bridge handle even when discovered through the page frame graph.");
            Assert.That(parentFrame, Is.SameAs(page.MainFrame), "Shadow-hosted child frame parent lookup must resolve the main frame.");
            Assert.That(contentFrame, Is.SameAs(childFrame), "Shadow-hosted child frame content-frame lookup must return the same child frame instance.");
            Assert.That(frameTitle, Is.EqualTo("Shadow Hosted Child Frame"), "Shadow-hosted child frame title lookup must execute inside the live iframe document.");
            Assert.That(frameUrl, Is.Not.Null, "Shadow-hosted child frame URL lookup must resolve a concrete live iframe browsing context URL.");
            Assert.That(frameProbe, Is.Not.Null, "Shadow-hosted child frame DOM wait must resolve markup inside the live iframe document.");
            Assert.That(frameText, Is.EqualTo("shadow-child-object-ready"), "Shadow-hosted child frame evaluation must execute inside the live iframe document.");
            Assert.That(frameByElement, Is.SameAs(childFrame), "Element-to-frame lookup must resolve the shadow-hosted child frame for iframe-local elements.");
            Assert.That(await frameProbe!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("shadow-child-object-ready"));
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageClosedShadowRootRemainsOpaqueToShadowLookup()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Closed shadow-root surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Closed Shadow Root';
                document.body.innerHTML = '<div id="closed-shadow-host"></div>';

                const host = document.getElementById('closed-shadow-host');
                if (!host) {
                    return;
                }

                const shadow = host.attachShadow({ mode: 'closed' });
                shadow.innerHTML = '<span id="closed-shadow-node">closed-text</span>';
                host.dataset.shadowMode = host.shadowRoot === null ? 'closed' : 'open';
            })();
            """).ConfigureAwait(false);

        var hostElement = await page.WaitForElementAsync("#closed-shadow-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var closedShadowRoot = await page.GetShadowRootAsync("#closed-shadow-host").ConfigureAwait(false);
        var directClosedShadowRoot = hostElement is null ? null : await hostElement.GetShadowRootAsync().ConfigureAwait(false);
        var closedState = await page.EvaluateAsync<string>(
            "JSON.stringify({ title: document.title ?? null, hostPresent: document.getElementById('closed-shadow-host') !== null, shadowMode: document.getElementById('closed-shadow-host')?.dataset?.shadowMode ?? null, scriptVisibleShadowRoot: document.getElementById('closed-shadow-host')?.shadowRoot !== null })")
            .ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(hostElement, Is.Not.Null, $"Closed shadow host must be discoverable as a regular DOM element. state={closedState ?? "<null>"}");
            Assert.That(closedShadowRoot, Is.Null, $"Page-level shadow-root lookup must stay opaque for closed shadow roots. state={closedState ?? "<null>"}");
            Assert.That(directClosedShadowRoot, Is.Null, $"Direct host shadow-root lookup must stay opaque for closed shadow roots. state={closedState ?? "<null>"}");
            Assert.That(closedState, Does.Contain("\"title\":\"Real Browser Closed Shadow Root\""), $"Closed shadow-root setup must preserve the live document title. state={closedState ?? "<null>"}");
            Assert.That(closedState, Does.Contain("\"hostPresent\":true"), $"Closed shadow host must remain attached on the live page. state={closedState ?? "<null>"}");
            Assert.That(closedState, Does.Contain("\"shadowMode\":\"closed\""), $"Live setup must confirm that the host is using a closed shadow root. state={closedState ?? "<null>"}");
            Assert.That(closedState, Does.Contain("\"scriptVisibleShadowRoot\":false"), $"Closed shadow roots must stay inaccessible via host.shadowRoot from page scripts. state={closedState ?? "<null>"}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageChildIframeObjectModelMaterializesLiveFrameContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Child iframe object model requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Child Iframe Object Model';
                document.body.replaceChildren();

                const frame = document.createElement('iframe');
                frame.id = 'child-frame-host';
                frame.name = 'live-child-frame';
                frame.src = 'about:blank#live-child-frame';
                document.body.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Live Child Frame</title></head><body><span id="child-probe">child-object-ready</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#child-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var hostChildFrames = frameHost is null
            ? []
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var mainChildFrames = (await page.MainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var childFrame = hostChildFrames.SingleOrDefault();
        var pageFrames = page.Frames.ToArray();
        var frameByName = await page.GetFrameAsync("live-child-frame").ConfigureAwait(false);
        var frameElement = childFrame is null ? null : await childFrame.GetFrameElementAsync().ConfigureAwait(false);
        var parentFrame = childFrame is null ? null : await childFrame.GetParentFrameAsync().ConfigureAwait(false);
        var contentFrame = childFrame is null ? null : await childFrame.GetContentFrameAsync().ConfigureAwait(false);
        var frameTitle = childFrame is null ? null : await childFrame.GetTitleAsync().ConfigureAwait(false);
        var frameUrl = childFrame is null ? null : await childFrame.GetUrlAsync().ConfigureAwait(false);
        var frameProbe = childFrame is null
            ? null
            : await childFrame.WaitForElementAsync("#child-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var frameText = childFrame is null
            ? null
            : await childFrame.EvaluateAsync<string>("return document.getElementById('child-probe')?.textContent ?? null").ConfigureAwait(false);
        var frameByElement = frameProbe is null ? null : await page.GetFrameAsync(frameProbe).ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(frameHost, Is.Not.Null, "Iframe host must be discoverable on the live page before materializing the child frame object model.");
            Assert.That(hostChildFrames, Has.Length.EqualTo(1), "Iframe host must expose exactly one live child frame.");
            Assert.That(mainChildFrames, Has.Length.EqualTo(1), "Main frame must materialize the same live child iframe.");
            Assert.That(childFrame, Is.Not.Null, "Live iframe host must materialize a child frame runtime object.");
            Assert.That(mainChildFrames[0], Is.SameAs(childFrame));
            Assert.That(pageFrames, Has.Length.EqualTo(2), "Page frame snapshot must include the main frame and the live child frame once materialized.");
            Assert.That(pageFrames[0], Is.SameAs(page.MainFrame));
            Assert.That(pageFrames[1], Is.SameAs(childFrame));
            Assert.That(frameByName, Is.SameAs(childFrame), "Name-based lookup must resolve the materialized child frame.");
            Assert.That(frameElement, Is.SameAs(frameHost), "Child frame must point back to its iframe host element.");
            Assert.That(parentFrame, Is.SameAs(page.MainFrame), "Child frame parent lookup must resolve the main frame.");
            Assert.That(contentFrame, Is.SameAs(childFrame), "Child frame content-frame lookup must return the same child frame instance.");
            Assert.That(frameTitle, Is.EqualTo("Live Child Frame"), "Child frame title lookup must execute inside the live iframe document.");
            Assert.That(frameUrl, Is.Not.Null, "Child frame URL lookup must resolve a concrete live iframe browsing context URL.");
            Assert.That(frameProbe, Is.Not.Null, "Child frame DOM wait must resolve markup inside the live iframe document.");
            Assert.That(frameText, Is.EqualTo("child-object-ready"), "Child frame evaluation must execute inside the live iframe document.");
            Assert.That(frameByElement, Is.SameAs(childFrame), "Element-to-frame lookup must resolve the live child frame for iframe-local elements.");
            Assert.That(await frameProbe!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("child-object-ready"));
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageChildIframeDetachmentMarksMaterializedFrameDetached()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Child iframe detachment requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Child Iframe Detachment';
                document.body.replaceChildren();

                const frame = document.createElement('iframe');
                frame.id = 'detached-frame-host';
                frame.name = 'live-detached-child-frame';
                frame.src = 'about:blank#live-detached-child-frame';
                document.body.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Live Detached Child Frame</title></head><body><span id="child-detached-probe">child-detach-ready</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#detached-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var hostChildFrames = frameHost is null
            ? []
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var childFrame = hostChildFrames.SingleOrDefault();
        var frameProbe = childFrame is null
            ? null
            : await childFrame.WaitForElementAsync("#child-detached-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        var removalState = await page.EvaluateAsync<string>(
            "JSON.stringify((() => { const frame = document.getElementById('detached-frame-host'); if (frame) { frame.remove(); } return { hostPresent: document.getElementById('detached-frame-host') !== null, bodyChildCount: document.body.childElementCount }; })())")
            .ConfigureAwait(false);

        var detached = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            detached = childFrame is not null && await childFrame.IsDetachedAsync().ConfigureAwait(false);
            if (detached)
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var mainDetached = await page.MainFrame.IsDetachedAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(frameHost, Is.Not.Null, "Iframe host must be discoverable before validating live detachment.");
            Assert.That(hostChildFrames, Has.Length.EqualTo(1), "Iframe host must materialize exactly one live child frame before detachment.");
            Assert.That(childFrame, Is.Not.Null, "Live iframe must materialize a child frame runtime object before detachment.");
            Assert.That(frameProbe, Is.Not.Null, "Iframe-local probe must resolve before the iframe host is removed.");
            Assert.That(removalState, Does.Contain("\"hostPresent\":false"), $"Iframe host removal must succeed on the live page. state={removalState ?? "<null>"}");
            Assert.That(detached, Is.True, $"Materialized child frame must become detached after its iframe host is removed from the live DOM. state={removalState ?? "<null>"}");
            Assert.That(mainDetached, Is.False, "Main frame must remain attached after a child iframe host is removed.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageShadowHostedChildIframeDetachmentMarksMaterializedFrameDetached()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Shadow-hosted child iframe detachment requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Shadow Hosted Child Iframe Detachment';
                document.body.replaceChildren();

                const host = document.createElement('div');
                host.id = 'shadow-detached-frame-wrapper';
                document.body.appendChild(host);

                const shadow = host.attachShadow({ mode: 'open' });
                const frame = document.createElement('iframe');
                frame.id = 'shadow-detached-frame-host';
                frame.name = 'live-shadow-detached-child-frame';
                frame.src = 'about:blank#live-shadow-detached-child-frame';
                shadow.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Shadow Detached Child Frame</title></head><body><span id="child-shadow-detached-probe">shadow-child-detach-ready</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var shadowRoot = await page.GetShadowRootAsync("#shadow-detached-frame-wrapper").ConfigureAwait(false);
        var frameHost = shadowRoot is null
            ? null
            : await shadowRoot.WaitForElementAsync("#shadow-detached-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var hostChildFrames = frameHost is null
            ? []
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var childFrame = hostChildFrames.SingleOrDefault();
        var frameProbe = childFrame is null
            ? null
            : await childFrame.WaitForElementAsync("#child-shadow-detached-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        var removalState = await page.EvaluateAsync<string>(
            "JSON.stringify((() => { const host = document.getElementById('shadow-detached-frame-wrapper'); const shadow = host?.shadowRoot; const frame = shadow?.querySelector('#shadow-detached-frame-host'); if (frame) { frame.remove(); } return { wrapperPresent: host !== null, framePresent: shadow?.querySelector('#shadow-detached-frame-host') !== null, bodyChildCount: document.body.childElementCount }; })())")
            .ConfigureAwait(false);

        var detached = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            detached = childFrame is not null && await childFrame.IsDetachedAsync().ConfigureAwait(false);
            if (detached)
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var mainDetached = await page.MainFrame.IsDetachedAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(shadowRoot, Is.Not.Null, "Open shadow root must remain discoverable before validating shadow-hosted child frame detachment.");
            Assert.That(frameHost, Is.Not.Null, "Iframe host inside the open shadow root must be discoverable before detachment.");
            Assert.That(hostChildFrames, Has.Length.EqualTo(1), "Shadow-hosted iframe must materialize exactly one live child frame before detachment.");
            Assert.That(childFrame, Is.Not.Null, "Shadow-hosted iframe must materialize a child frame runtime object before detachment.");
            Assert.That(frameProbe, Is.Not.Null, "Shadow-hosted iframe-local probe must resolve before the iframe host is removed from the shadow root.");
            Assert.That(removalState, Does.Contain("\"wrapperPresent\":true"), $"Shadow host wrapper must remain attached after removing the iframe from the shadow root. state={removalState ?? "<null>"}");
            Assert.That(removalState, Does.Contain("\"framePresent\":false"), $"Iframe host must be removed from the open shadow root. state={removalState ?? "<null>"}");
            Assert.That(detached, Is.True, $"Materialized shadow-hosted child frame must become detached after its iframe host is removed from the open shadow root. state={removalState ?? "<null>"}");
            Assert.That(mainDetached, Is.False, "Main frame must remain attached after removing a shadow-hosted child iframe.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageShadowHostedChildIframeRemoveReattachMaterializesFreshFrameContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Shadow-hosted child iframe remove-reattach requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Shadow Hosted Child Iframe Reattach';
                document.body.replaceChildren();

                const host = document.createElement('div');
                host.id = 'shadow-reattach-frame-wrapper';
                document.body.appendChild(host);

                const shadow = host.attachShadow({ mode: 'open' });
                const frame = document.createElement('iframe');
                frame.id = 'shadow-reattach-frame-host';
                frame.name = 'live-shadow-reattach-child-frame';
                frame.src = 'about:blank#live-shadow-reattach-child-frame';
                shadow.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Shadow Reattach Child Frame Initial</title></head><body><span id="child-shadow-reattach-probe">shadow-reattach-initial</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var shadowRoot = await page.GetShadowRootAsync("#shadow-reattach-frame-wrapper").ConfigureAwait(false);
        var initialFrameHost = shadowRoot is null
            ? null
            : await shadowRoot.WaitForElementAsync("#shadow-reattach-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var initialHostChildFrames = initialFrameHost is null
            ? []
            : (await initialFrameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var initialChildFrame = initialHostChildFrames.SingleOrDefault();
        var initialProbe = initialChildFrame is null
            ? null
            : await initialChildFrame.WaitForElementAsync("#child-shadow-reattach-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        var removalState = await page.EvaluateAsync<string>(
            "JSON.stringify((() => { const host = document.getElementById('shadow-reattach-frame-wrapper'); const shadow = host?.shadowRoot; const frame = shadow?.querySelector('#shadow-reattach-frame-host'); if (frame) { frame.remove(); } return { wrapperPresent: host !== null, framePresent: shadow?.querySelector('#shadow-reattach-frame-host') !== null }; })())")
            .ConfigureAwait(false);

        var initialDetached = false;
        var initialDetachDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < initialDetachDeadline)
        {
            initialDetached = initialChildFrame is not null && await initialChildFrame.IsDetachedAsync().ConfigureAwait(false);
            if (initialDetached)
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        await page.EvaluateAsync(
            """
            (() => {
                const host = document.getElementById('shadow-reattach-frame-wrapper');
                const shadow = host?.shadowRoot;
                if (!shadow) {
                    return;
                }

                const frame = document.createElement('iframe');
                frame.id = 'shadow-reattach-frame-host';
                frame.name = 'live-shadow-reattach-child-frame';
                frame.src = 'about:blank#live-shadow-reattach-child-frame-reattached';
                shadow.appendChild(frame);

                const childDocument = frame.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><title>Shadow Reattach Child Frame Reattached</title></head><body><span id="child-shadow-reattach-probe-reattached">shadow-reattach-updated</span></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var reattachedFrameHost = shadowRoot is null
            ? null
            : await shadowRoot.WaitForElementAsync("#shadow-reattach-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var reattachedHostChildFrames = reattachedFrameHost is null
            ? []
            : (await reattachedFrameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var reattachedChildFrame = reattachedHostChildFrames.SingleOrDefault();
        var frameByName = await page.GetFrameAsync("live-shadow-reattach-child-frame").ConfigureAwait(false);
        var reattachedProbe = reattachedChildFrame is null
            ? null
            : await reattachedChildFrame.WaitForElementAsync("#child-shadow-reattach-probe-reattached", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var reattachedText = reattachedChildFrame is null
            ? null
            : await reattachedChildFrame.EvaluateAsync<string>("return document.getElementById('child-shadow-reattach-probe-reattached')?.textContent ?? null").ConfigureAwait(false);
        var reattachedDetached = reattachedChildFrame is not null && await reattachedChildFrame.IsDetachedAsync().ConfigureAwait(false);
        var postReattachMainChildFrames = (await page.MainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var postReattachPageFrames = page.Frames.ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(shadowRoot, Is.Not.Null, "Open shadow root must be discoverable for the remove-reattach scenario.");
            Assert.That(initialFrameHost, Is.Not.Null, "Initial iframe host inside the open shadow root must be discoverable before removal.");
            Assert.That(initialHostChildFrames, Has.Length.EqualTo(1), "Initial shadow-hosted iframe must materialize exactly one child frame before removal.");
            Assert.That(initialChildFrame, Is.Not.Null, "Initial shadow-hosted iframe must materialize a child frame runtime object before removal.");
            Assert.That(initialProbe, Is.Not.Null, "Initial iframe-local probe must resolve before the iframe host is removed from the shadow root.");
            Assert.That(removalState, Does.Contain("\"wrapperPresent\":true"), $"Shadow host wrapper must remain attached while removing the initial iframe from the shadow root. state={removalState ?? "<null>"}");
            Assert.That(removalState, Does.Contain("\"framePresent\":false"), $"Initial iframe host must be removed from the open shadow root before reattach. state={removalState ?? "<null>"}");
            Assert.That(initialDetached, Is.True, $"Initial materialized shadow-hosted child frame must become detached after removal. state={removalState ?? "<null>"}");
            Assert.That(reattachedFrameHost, Is.Not.Null, "Reattached iframe host must become discoverable through the same open shadow root.");
            Assert.That(reattachedHostChildFrames, Has.Length.EqualTo(1), "Reattached iframe host must materialize exactly one fresh child frame.");
            Assert.That(reattachedChildFrame, Is.Not.Null, "Reattached shadow-hosted iframe must materialize a fresh child frame runtime object.");
            Assert.That(reattachedChildFrame, Is.Not.SameAs(initialChildFrame), "Reattached iframe must materialize a new frame instance instead of reusing the detached predecessor.");
            Assert.That(frameByName, Is.SameAs(reattachedChildFrame), "Name-based lookup must resolve the reattached live frame instead of the detached predecessor.");
            Assert.That(reattachedProbe, Is.Not.Null, "Reattached iframe-local probe must resolve inside the fresh frame context.");
            Assert.That(reattachedText, Is.EqualTo("shadow-reattach-updated"), "Reattached frame evaluation must resolve the updated iframe document content.");
            Assert.That(reattachedDetached, Is.False, "Freshly reattached frame must remain attached.");
            Assert.That(postReattachMainChildFrames, Has.Length.EqualTo(1), "Main-frame child snapshot must drop the detached predecessor and keep only the reattached child frame.");
            Assert.That(postReattachMainChildFrames[0], Is.SameAs(reattachedChildFrame));
            Assert.That(postReattachPageFrames, Has.Length.EqualTo(2), "Page frame snapshot must contain only the main frame and the reattached child frame after detached history is pruned.");
            Assert.That(postReattachPageFrames[0], Is.SameAs(page.MainFrame));
            Assert.That(postReattachPageFrames[1], Is.SameAs(reattachedChildFrame));
            Assert.That(await reattachedProbe!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("shadow-reattach-updated"));
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageChildIframeObjectModelTreatsCrossOriginFrameAsOpaqueContext()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Cross-origin child iframe object model requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            return new Promise((resolve) => {
                document.title = 'Real Browser Cross Origin Child Iframe';
                document.body.replaceChildren();

                const frame = document.createElement('iframe');
                frame.id = 'cross-origin-frame-host';
                frame.name = 'live-cross-origin-frame';
                frame.addEventListener('load', () => resolve('loaded'), { once: true });
                frame.src = 'data:text/html;charset=utf-8,' + encodeURIComponent('<html><head><title>Cross Origin Child</title></head><body><span id="cross-origin-probe">cross-origin-ready</span></body></html>');
                document.body.appendChild(frame);
            });
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#cross-origin-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var hostChildFrames = frameHost is null
            ? []
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var childFrame = hostChildFrames.SingleOrDefault();
        var mainChildFrames = (await page.MainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var pageFrames = page.Frames.ToArray();
        var frameByName = await page.GetFrameAsync("live-cross-origin-frame").ConfigureAwait(false);
        var frameElement = childFrame is null ? null : await childFrame.GetFrameElementAsync().ConfigureAwait(false);
        var parentFrame = childFrame is null ? null : await childFrame.GetParentFrameAsync().ConfigureAwait(false);
        var contentFrame = childFrame is null ? null : await childFrame.GetContentFrameAsync().ConfigureAwait(false);
        var frameUrl = childFrame is null ? null : await childFrame.GetUrlAsync().ConfigureAwait(false);
        var frameTitle = childFrame is null ? null : await childFrame.GetTitleAsync().ConfigureAwait(false);
        var frameContent = childFrame is null ? null : await childFrame.GetContentAsync().ConfigureAwait(false);
        var frameValue = childFrame is null ? null : await childFrame.EvaluateAsync<string>("return document.title ?? null").ConfigureAwait(false);
        var frameElementLookup = childFrame is null ? null : await childFrame.GetElementAsync("#cross-origin-probe").ConfigureAwait(false);
        var frameElements = childFrame is null ? [] : (await childFrame.GetElementsAsync("body").ConfigureAwait(false)).ToArray();
        var frameWait = childFrame is null
            ? null
            : await childFrame.WaitForElementAsync("#cross-origin-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var nestedChildFrames = childFrame is null ? [] : (await childFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frameHost, Is.Not.Null, "Cross-origin iframe host must still be discoverable on the live page.");
            Assert.That(hostChildFrames, Has.Length.EqualTo(1), "Cross-origin iframe host must still materialize a child frame runtime object.");
            Assert.That(childFrame, Is.Not.Null, "Cross-origin iframe host must surface a child frame instance even when its DOM is opaque.");
            Assert.That(mainChildFrames, Has.Length.EqualTo(1), "Main frame must expose the same cross-origin child frame instance.");
            Assert.That(mainChildFrames[0], Is.SameAs(childFrame));
            Assert.That(pageFrames, Has.Length.EqualTo(2), "Page frame snapshot must still include the cross-origin child frame once materialized.");
            Assert.That(pageFrames[0], Is.SameAs(page.MainFrame));
            Assert.That(pageFrames[1], Is.SameAs(childFrame));
            Assert.That(frameByName, Is.SameAs(childFrame), "Name-based lookup must still resolve the cross-origin child frame by its host name.");
            Assert.That(frameElement, Is.SameAs(frameHost), "Cross-origin child frame must still point back to its iframe host.");
            Assert.That(parentFrame, Is.SameAs(page.MainFrame), "Cross-origin child frame parent lookup must still resolve the main frame.");
            Assert.That(contentFrame, Is.SameAs(childFrame), "Cross-origin child frame content-frame lookup must still return the same child frame instance.");
            Assert.That(frameUrl, Is.Null, "Cross-origin child frame URL stays opaque on the current frameHostElementId path.");
            Assert.That(frameTitle, Is.EqualTo("null"), "Cross-origin child frame title lookup must fail closed and currently surfaces the bridge null sentinel string.");
            Assert.That(frameContent, Is.EqualTo("null"), "Cross-origin child frame content lookup must fail closed and currently surfaces the bridge null sentinel string.");
            Assert.That(frameValue, Is.EqualTo("null"), "Cross-origin child frame evaluation must fail closed and currently surfaces the bridge null sentinel string.");
            Assert.That(frameElementLookup, Is.Null, "Cross-origin child frame DOM lookup must return null rather than throwing a bridge error.");
            Assert.That(frameElements, Is.Empty, "Cross-origin child frame DOM enumeration must stay empty on an opaque frame root.");
            Assert.That(frameWait, Is.Null, "Cross-origin child frame waits must fail closed rather than throwing a bridge error.");
            Assert.That(nestedChildFrames, Is.Empty, "Cross-origin child frame enumeration must not throw when the nested DOM root is opaque.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageScreenshotReturnsNonEmptyPngPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Screenshot surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Screenshot';
                document.body.innerHTML = '<main id="screenshot-probe">live screenshot probe</main>';

                const style = document.createElement('style');
                style.textContent = `
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                    }

                    body {
                        display: grid;
                        place-items: center;
                        background: linear-gradient(135deg, #14324a 0%, #1f6f8b 45%, #f0c06d 100%);
                        color: white;
                        font: 700 28px/1.2 sans-serif;
                    }

                    main {
                        padding: 24px 32px;
                        border-radius: 18px;
                        background: rgba(0, 0, 0, 0.22);
                        box-shadow: 0 20px 40px rgba(0, 0, 0, 0.18);
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var probe = await page.WaitForElementAsync("#screenshot-probe", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        byte[] screenshot;

        try
        {
            screenshot = (await page.GetScreenshotAsync().ConfigureAwait(false)).ToArray();
        }
        catch (Exception error)
        {
            var diagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
            Assert.Fail($"Live page screenshot failed: {error.Message}. {diagnostics}");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(probe, Is.Not.Null);
            Assert.That(screenshot.Length, Is.GreaterThan(8), "Live page screenshot must return a non-empty PNG payload.");
            Assert.That(IsPngPayload(screenshot), Is.True, "Live page screenshot must start with PNG signature bytes.");
        });
    }

    [Test]
    public async Task RealBrowserChildFrameScreenshotReturnsCroppedNonEmptyPngPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Child-frame screenshot surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Child Frame Screenshot';
                document.body.innerHTML = '<iframe id="screenshot-child-frame" name="screenshot-child-frame" style="width: 320px; height: 180px; border: 0; border-radius: 20px; margin: 48px auto; display: block; background: white;" src="about:blank#screenshot-child-frame"></iframe>';

                const style = document.createElement('style');
                style.textContent = `
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                    }

                    body {
                        background: linear-gradient(135deg, #13324a 0%, #3b7a57 55%, #f0b869 100%);
                    }
                `;

                document.head.appendChild(style);

                const frame = document.getElementById('screenshot-child-frame');
                const childDocument = frame?.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><style>html,body{margin:0;width:100%;height:100%}body{display:grid;place-items:center;background:linear-gradient(135deg,#502d63 0%,#2f84a1 100%);color:#fff;font:700 22px/1.2 sans-serif}main{padding:20px 24px;border-radius:18px;background:rgba(255,255,255,.12)}</style><title>Child Screenshot Frame</title></head><body><main id="child-screenshot-probe">child screenshot</main></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#screenshot-child-frame", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var childFrame = frameHost is null
            ? null
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).SingleOrDefault();

        Assert.That(childFrame, Is.Not.Null, "Child screenshot frame must materialize before capturing its screenshot.");

        var pageScreenshot = (await page.GetScreenshotAsync().ConfigureAwait(false)).ToArray();
        var childScreenshot = (await childFrame!.GetScreenshotAsync().ConfigureAwait(false)).ToArray();
        var pageSize = ReadPngSize(pageScreenshot);
        var childSize = ReadPngSize(childScreenshot);

        Assert.Multiple(() =>
        {
            Assert.That(pageScreenshot.Length, Is.GreaterThan(8), "Live page screenshot must remain non-empty for child-frame screenshot comparisons.");
            Assert.That(childScreenshot.Length, Is.GreaterThan(8), "Child frame screenshot must return a non-empty PNG payload.");
            Assert.That(IsPngPayload(childScreenshot), Is.True, "Child frame screenshot must start with PNG signature bytes.");
            Assert.That(childSize.Width, Is.GreaterThan(0), "Child frame screenshot must expose a positive PNG width.");
            Assert.That(childSize.Height, Is.GreaterThan(0), "Child frame screenshot must expose a positive PNG height.");
            Assert.That(childSize.Width, Is.LessThan(pageSize.Width), "Child frame screenshot must be cropped relative to the full page screenshot width.");
            Assert.That(childSize.Height, Is.LessThan(pageSize.Height), "Child frame screenshot must be cropped relative to the full page screenshot height.");
        });
    }

    [Test]
    public async Task RealBrowserChildFrameElementScreenshotReturnsCroppedNonEmptyPngPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Child-frame element screenshot surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Child Frame Element Screenshot';
                document.body.innerHTML = '<iframe id="screenshot-child-element-frame" name="screenshot-child-element-frame" style="width: 360px; height: 220px; border: 0; border-radius: 22px; margin: 48px auto; display: block; background: white;" src="about:blank#screenshot-child-element-frame"></iframe>';

                const style = document.createElement('style');
                style.textContent = `
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                    }

                    body {
                        background: linear-gradient(135deg, #241f45 0%, #296e8a 50%, #eabf76 100%);
                    }
                `;

                document.head.appendChild(style);

                const frame = document.getElementById('screenshot-child-element-frame');
                const childDocument = frame?.contentWindow?.document;
                if (!childDocument) {
                    return;
                }

                childDocument.open();
                childDocument.write('<html><head><style>html,body{margin:0;width:100%;height:100%}body{display:grid;place-items:center;background:linear-gradient(135deg,#1d5a6a 0%,#55a28d 100%)}button{width:180px;height:76px;border:0;border-radius:18px;background:#f6e2a8;color:#213;font:700 20px/1.2 sans-serif;box-shadow:0 16px 36px rgba(0,0,0,.18)}</style><title>Child Element Screenshot</title></head><body><button id="child-screenshot-button" type="button">frame element</button></body></html>');
                childDocument.close();
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#screenshot-child-element-frame", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var childFrame = frameHost is null
            ? null
            : (await frameHost.GetChildFramesAsync().ConfigureAwait(false)).SingleOrDefault();

        Assert.That(childFrame, Is.Not.Null, "Child frame for element screenshot must materialize before capturing its element screenshot.");

        var childElement = await childFrame!.WaitForElementAsync("#child-screenshot-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(childElement, Is.Not.Null, "Child-frame element screenshot probe must be discoverable before capture.");

        var frameScreenshot = (await childFrame.GetScreenshotAsync().ConfigureAwait(false)).ToArray();
        var elementScreenshot = (await childElement!.GetScreenshotAsync().ConfigureAwait(false)).ToArray();
        var frameSize = ReadPngSize(frameScreenshot);
        var elementSize = ReadPngSize(elementScreenshot);

        Assert.Multiple(() =>
        {
            Assert.That(frameScreenshot.Length, Is.GreaterThan(8), "Child frame screenshot must remain non-empty for nested element screenshot comparisons.");
            Assert.That(elementScreenshot.Length, Is.GreaterThan(8), "Child-frame element screenshot must return a non-empty PNG payload.");
            Assert.That(IsPngPayload(elementScreenshot), Is.True, "Child-frame element screenshot must start with PNG signature bytes.");
            Assert.That(elementSize.Width, Is.GreaterThan(0), "Child-frame element screenshot must expose a positive PNG width.");
            Assert.That(elementSize.Height, Is.GreaterThan(0), "Child-frame element screenshot must expose a positive PNG height.");
            Assert.That(elementSize.Width, Is.LessThan(frameSize.Width), "Child-frame element screenshot must be cropped relative to its frame screenshot width.");
            Assert.That(elementSize.Height, Is.LessThan(frameSize.Height), "Child-frame element screenshot must be cropped relative to its frame screenshot height.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementClickProducesTrustedEvent()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Trusted click surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Trusted Click';
                document.body.innerHTML = '<button id="trusted-button" type="button">Trusted click</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #f6efe4;
                        font: 600 18px/1.4 sans-serif;
                    }

                    button {
                        min-width: 220px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #0f5f4b;
                        color: white;
                        font: inherit;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
                document.documentElement.dataset.atomTrustedClickCount = '0';
                document.documentElement.dataset.atomTrustedClickTrusted = 'false';
                globalThis.__atomTrustedClickEvents = [];
                globalThis.__atomTrustedClickEventSequence = 0;

                const recordTrustedClickEvent = (type, event) => {
                    globalThis.__atomTrustedClickEvents.push({
                        sequence: ++globalThis.__atomTrustedClickEventSequence,
                        type,
                        trusted: !!event.isTrusted,
                        targetId: event.target?.id ?? null,
                        targetTag: event.target?.tagName ?? null,
                        clientX: typeof event.clientX === 'number' ? event.clientX : null,
                        clientY: typeof event.clientY === 'number' ? event.clientY : null,
                        screenX: typeof event.screenX === 'number' ? event.screenX : null,
                        screenY: typeof event.screenY === 'number' ? event.screenY : null,
                    });

                    if (globalThis.__atomTrustedClickEvents.length > 32) {
                        globalThis.__atomTrustedClickEvents.splice(0, globalThis.__atomTrustedClickEvents.length - 32);
                    }
                };

                for (const type of ['pointermove', 'mousemove', 'pointerover', 'pointerenter', 'mouseover', 'mouseenter', 'pointerdown', 'mousedown', 'mouseup', 'click']) {
                    document.addEventListener(type, (event) => recordTrustedClickEvent(type, event), true);
                }

                document.getElementById('trusted-button')?.addEventListener('click', (event) => {
                    document.documentElement.dataset.atomTrustedClickCount = String(Number(document.documentElement.dataset.atomTrustedClickCount || '0') + 1);
                    document.documentElement.dataset.atomTrustedClickTrusted = event.isTrusted ? 'true' : 'false';
                });
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#trusted-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Trusted click probe button must be discoverable before interaction.");

        var preClickWindowBounds = await ((WebWindow)browser.CurrentWindow).GetBoundingBoxAsync().ConfigureAwait(false);
        var preClickViewportSize = await page.GetViewportSizeAsync().ConfigureAwait(false);
        var preClickButtonBounds = await button!.GetBoundingBoxAsync().ConfigureAwait(false);
        var preClickInteractionPoint = await InvokePrivateValueTaskAsync<Point>(button, "ResolveInteractionPointAsync").ConfigureAwait(false);
        var preClickEstimatedScreenPoint = EstimateScreenPoint(preClickWindowBounds, preClickViewportSize, preClickButtonBounds);

        await button.ClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var clickCount = "0";
        var trusted = "false";

        while (DateTime.UtcNow < deadline)
        {
            clickCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomTrustedClickCount ?? '0')").ConfigureAwait(false) ?? "0";
            trusted = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomTrustedClickTrusted ?? 'false')").ConfigureAwait(false) ?? "false";

            if (clickCount == "1")
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var preClickDiagnostics = string.Concat(
            "preClickWindowBounds=", preClickWindowBounds?.ToString() ?? "<null>",
            ";preClickViewport=", preClickViewportSize?.ToString() ?? "<null>",
            ";preClickButtonBounds=", preClickButtonBounds?.ToString() ?? "<null>",
            ";preClickInteractionPoint=", preClickInteractionPoint.ToString(),
            ";preClickEstimatedScreenPoint=", preClickEstimatedScreenPoint?.ToString() ?? "<null>");

        var clickDiagnostics = clickCount == "1" && trusted == "true"
            ? string.Empty
            : string.Concat(preClickDiagnostics, ";", await DescribeTrustedClickFailureAsync(browser, page, "trusted-button", "atomTrustedClickCount", "atomTrustedClickTrusted", "__atomTrustedClickEvents").ConfigureAwait(false));

        Assert.Multiple(() =>
        {
            Assert.That(clickCount, Is.EqualTo("1"), $"Trusted element click must dispatch exactly one browser click event. {clickDiagnostics}");
            Assert.That(trusted, Is.EqualTo("true"), $"Trusted element click must produce a trusted DOM click event. {clickDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementHumanityClickProducesTrustedEvent()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Humanity click surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Humanity Click';
                document.body.innerHTML = '<button id="humanity-click-button" type="button">Humanity click</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #ede3d2;
                        font: 600 18px/1.4 sans-serif;
                    }

                    button {
                        min-width: 240px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #15543d;
                        color: white;
                        font: inherit;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
                document.documentElement.dataset.atomHumanityClickCount = '0';
                document.documentElement.dataset.atomHumanityClickTrusted = 'false';
                globalThis.__atomHumanityClickEvents = [];
                globalThis.__atomHumanityClickEventSequence = 0;

                const recordHumanityClickEvent = (type, event) => {
                    globalThis.__atomHumanityClickEvents.push({
                        sequence: ++globalThis.__atomHumanityClickEventSequence,
                        type,
                        trusted: !!event.isTrusted,
                        targetId: event.target?.id ?? null,
                        targetTag: event.target?.tagName ?? null,
                        clientX: typeof event.clientX === 'number' ? event.clientX : null,
                        clientY: typeof event.clientY === 'number' ? event.clientY : null,
                        screenX: typeof event.screenX === 'number' ? event.screenX : null,
                        screenY: typeof event.screenY === 'number' ? event.screenY : null,
                    });

                    if (globalThis.__atomHumanityClickEvents.length > 32) {
                        globalThis.__atomHumanityClickEvents.splice(0, globalThis.__atomHumanityClickEvents.length - 32);
                    }
                };

                for (const type of ['pointermove', 'mousemove', 'pointerover', 'pointerenter', 'mouseover', 'mouseenter', 'pointerdown', 'mousedown', 'mouseup', 'click']) {
                    document.addEventListener(type, (event) => recordHumanityClickEvent(type, event), true);
                }

                document.getElementById('humanity-click-button')?.addEventListener('click', (event) => {
                    document.documentElement.dataset.atomHumanityClickCount = String(Number(document.documentElement.dataset.atomHumanityClickCount || '0') + 1);
                    document.documentElement.dataset.atomHumanityClickTrusted = event.isTrusted ? 'true' : 'false';
                });
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#humanity-click-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Humanity click probe button must be discoverable before interaction.");

        var preClickWindowBounds = await ((WebWindow)browser.CurrentWindow).GetBoundingBoxAsync().ConfigureAwait(false);
        var preClickViewportSize = await page.GetViewportSizeAsync().ConfigureAwait(false);
        var preClickButtonBounds = await button!.GetBoundingBoxAsync().ConfigureAwait(false);
        var preClickInteractionPoint = await InvokePrivateValueTaskAsync<Point>(button, "ResolveInteractionPointAsync").ConfigureAwait(false);
        var preClickEstimatedScreenPoint = EstimateScreenPoint(preClickWindowBounds, preClickViewportSize, preClickButtonBounds);

        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var clickCount = "0";
        var trusted = "false";

        while (DateTime.UtcNow < deadline)
        {
            clickCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityClickCount ?? '0')").ConfigureAwait(false) ?? "0";
            trusted = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityClickTrusted ?? 'false')").ConfigureAwait(false) ?? "false";

            if (clickCount == "1")
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var preClickDiagnostics = string.Concat(
            "preClickWindowBounds=", preClickWindowBounds?.ToString() ?? "<null>",
            ";preClickViewport=", preClickViewportSize?.ToString() ?? "<null>",
            ";preClickButtonBounds=", preClickButtonBounds?.ToString() ?? "<null>",
            ";preClickInteractionPoint=", preClickInteractionPoint.ToString(),
            ";preClickEstimatedScreenPoint=", preClickEstimatedScreenPoint?.ToString() ?? "<null>");

        var clickDiagnostics = clickCount == "1" && trusted == "true"
            ? string.Empty
            : string.Concat(preClickDiagnostics, ";", await DescribeTrustedClickFailureAsync(browser, page, "humanity-click-button", "atomHumanityClickCount", "atomHumanityClickTrusted", "__atomHumanityClickEvents").ConfigureAwait(false));

        Assert.Multiple(() =>
        {
            Assert.That(clickCount, Is.EqualTo("1"), $"HumanityClickAsync must dispatch exactly one browser click event. {clickDiagnostics}");
            Assert.That(trusted, Is.EqualTo("true"), $"HumanityClickAsync must produce a trusted DOM click event. {clickDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementHumanityTypeUsesTrustedKeyboardInput()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Humanity typing surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Humanity Type';
                document.body.innerHTML = '<input id="humanity-type-input" value="" autocomplete="off" />';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #f5eee5;
                        font: 600 18px/1.4 sans-serif;
                    }

                    input {
                        width: 360px;
                        height: 64px;
                        padding: 0 18px;
                        border: 2px solid #9f8f7c;
                        border-radius: 18px;
                        font: inherit;
                        background: white;
                        color: #1d1d1d;
                    }
                `;

                document.head.appendChild(style);
                document.documentElement.dataset.atomHumanityTypeKeydownCount = '0';
                document.documentElement.dataset.atomHumanityTypeKeyupCount = '0';
                document.documentElement.dataset.atomHumanityTypeInputCount = '0';
                document.documentElement.dataset.atomHumanityTypeTrustedKeydown = 'true';
                document.documentElement.dataset.atomHumanityTypeTrustedKeyup = 'true';
                document.documentElement.dataset.atomHumanityTypeTrustedInput = 'true';

                const input = document.getElementById('humanity-type-input');
                input?.addEventListener('keydown', (event) => {
                    document.documentElement.dataset.atomHumanityTypeKeydownCount = String(Number(document.documentElement.dataset.atomHumanityTypeKeydownCount || '0') + 1);
                    if (!event.isTrusted) {
                        document.documentElement.dataset.atomHumanityTypeTrustedKeydown = 'false';
                    }
                });
                input?.addEventListener('keyup', (event) => {
                    document.documentElement.dataset.atomHumanityTypeKeyupCount = String(Number(document.documentElement.dataset.atomHumanityTypeKeyupCount || '0') + 1);
                    if (!event.isTrusted) {
                        document.documentElement.dataset.atomHumanityTypeTrustedKeyup = 'false';
                    }
                });
                input?.addEventListener('input', (event) => {
                    document.documentElement.dataset.atomHumanityTypeInputCount = String(Number(document.documentElement.dataset.atomHumanityTypeInputCount || '0') + 1);
                    if (!event.isTrusted) {
                        document.documentElement.dataset.atomHumanityTypeTrustedInput = 'false';
                    }
                });
            })();
            """).ConfigureAwait(false);

        var input = await page.WaitForElementAsync("#humanity-type-input", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(input, Is.Not.Null, "Humanity typing probe input must be discoverable before interaction.");

        await page.EvaluateAsync(
            """
            (() => {
                const input = document.getElementById('humanity-type-input');
                input?.focus();
                return document.activeElement?.id ?? null;
            })();
            """).ConfigureAwait(false);

        await input!.HumanityTypeAsync("abc").ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var value = string.Empty;
        var keydownCount = "0";
        var keyupCount = "0";
        var inputCount = "0";
        var trustedKeydown = "false";
        var trustedKeyup = "false";
        var trustedInput = "false";

        while (DateTime.UtcNow < deadline)
        {
            value = await page.EvaluateAsync<string>("String(document.getElementById('humanity-type-input')?.value ?? '')").ConfigureAwait(false) ?? string.Empty;
            keydownCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeKeydownCount ?? '0')").ConfigureAwait(false) ?? "0";
            keyupCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeKeyupCount ?? '0')").ConfigureAwait(false) ?? "0";
            inputCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeInputCount ?? '0')").ConfigureAwait(false) ?? "0";
            trustedKeydown = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedKeydown ?? 'false')").ConfigureAwait(false) ?? "false";
            trustedKeyup = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedKeyup ?? 'false')").ConfigureAwait(false) ?? "false";
            trustedInput = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedInput ?? 'false')").ConfigureAwait(false) ?? "false";

            if (value == "abc" && inputCount == "3")
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var typingDiagnostics = value == "abc"
            && keydownCount == "3"
            && keyupCount == "3"
            && inputCount == "3"
            && trustedKeydown == "true"
            && trustedKeyup == "true"
            && trustedInput == "true"
            ? string.Empty
            : await DescribeTrustedTypingFailureAsync(browser, page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo("abc"), $"HumanityTypeAsync must type the requested text into the focused input. {typingDiagnostics}");
            Assert.That(keydownCount, Is.EqualTo("3"), $"HumanityTypeAsync must dispatch one trusted keydown per typed character. {typingDiagnostics}");
            Assert.That(keyupCount, Is.EqualTo("3"), $"HumanityTypeAsync must dispatch one trusted keyup per typed character. {typingDiagnostics}");
            Assert.That(inputCount, Is.EqualTo("3"), $"HumanityTypeAsync must dispatch one trusted input event per typed character. {typingDiagnostics}");
            Assert.That(trustedKeydown, Is.EqualTo("true"), $"HumanityTypeAsync keydown events must stay trusted. {typingDiagnostics}");
            Assert.That(trustedKeyup, Is.EqualTo("true"), $"HumanityTypeAsync keyup events must stay trusted. {typingDiagnostics}");
            Assert.That(trustedInput, Is.EqualTo("true"), $"HumanityTypeAsync input events must stay trusted. {typingDiagnostics}");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerApisRelayAndUnsubscribeDomEvents()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Element event-listener surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Element Event Listener';
                document.body.innerHTML = '<button id="listener-button" type="button">Listener relay</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #efe6d8;
                    }

                    button {
                        min-width: 260px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #294f8a;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Listener relay button must be discoverable before interaction.");

        List<string> payloads = [];
        Action<string> onClick = payload => payloads.Add(payload);

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && payloads.Count == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.That(payloads, Has.Count.EqualTo(1), "RemoveEventListenerAsync must stop relaying further DOM events after unsubscription.");

        using var payloadDocument = JsonDocument.Parse(payloads[0]);
        var payload = payloadDocument.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("type").GetString(), Is.EqualTo("click"), "Managed element listener must receive the DOM event type.");
            Assert.That(payload.GetProperty("isTrusted").GetBoolean(), Is.True, "Managed element listener must observe trusted browser click events.");
            Assert.That(payload.GetProperty("currentTargetId").GetString(), Is.EqualTo("listener-button"), "Managed element listener must stay bound to the target element.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsEventHandlerElementEventArgsPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Typed element event-listener payload requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Element Event Args';
                document.body.innerHTML = '<button id="listener-eventargs-button" type="button">Listener event args</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #e8efe6;
                    }

                    button {
                        min-width: 260px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #2f5a3d;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-eventargs-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Typed listener relay button must be discoverable before interaction.");

        ElementEventArgs? deliveredArgs = null;
        var hitCount = 0;
        EventHandler<ElementEventArgs> onClick = (_, args) =>
        {
            Interlocked.Increment(ref hitCount);
            deliveredArgs = args;
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "EventHandler<ElementEventArgs> must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredArgs, Is.Not.Null, "Typed element listener must materialize ElementEventArgs payload.");
            Assert.That(deliveredArgs!.Type, Is.EqualTo("click"), "Typed element listener must expose the DOM event type.");
            Assert.That(deliveredArgs.IsTrusted, Is.True, "Typed element listener must preserve the trusted-event flag.");
            Assert.That(deliveredArgs.TargetId, Is.EqualTo("listener-eventargs-button"), "Typed element listener must expose the originating target id.");
            Assert.That(deliveredArgs.CurrentTargetId, Is.EqualTo("listener-eventargs-button"), "Typed element listener must stay bound to the target element.");
            Assert.That(deliveredArgs.Payload.ValueKind, Is.EqualTo(JsonValueKind.Object), "Typed element listener must retain the raw JSON payload for future inspection.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsJsonObjectPayloadAndAsyncSenderHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "JsonObject listener payload mapping requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser JsonObject Listener Payload';
                document.body.innerHTML = '<button id="listener-jsonobject-button" type="button">Listener JsonObject payload</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #f0e8da;
                    }

                    button {
                        min-width: 280px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #7a3f2f;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-jsonobject-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "JsonObject payload listener button must be discoverable before interaction.");

        JsonObject? deliveredPayload = null;
        IElement? deliveredSender = null;
        var hitCount = 0;

        Func<IElement, JsonObject, ValueTask> onClick = (sender, payload) =>
        {
            deliveredSender = sender;
            deliveredPayload = payload;
            Interlocked.Increment(ref hitCount);
            return ValueTask.CompletedTask;
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "JsonObject async listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "JsonObject async listener must receive the element sender instance.");
            Assert.That(deliveredPayload, Is.Not.Null, "JsonObject async listener must materialize a typed JSON object payload.");
            Assert.That(deliveredPayload!["type"]?.GetValue<string>(), Is.EqualTo("click"), "JsonObject async listener must expose the DOM event type.");
            Assert.That(deliveredPayload["isTrusted"]?.GetValue<bool>(), Is.True, "JsonObject async listener must expose the trusted-event flag.");
            Assert.That(deliveredPayload["targetId"]?.GetValue<string>(), Is.EqualTo("listener-jsonobject-button"), "JsonObject async listener must expose the originating target id.");
            Assert.That(deliveredPayload["currentTargetId"]?.GetValue<string>(), Is.EqualTo("listener-jsonobject-button"), "JsonObject async listener must expose the current target id.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsDictionaryPayloadAndAsyncSenderHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Dictionary listener payload mapping requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Dictionary Listener Payload';
                document.body.innerHTML = '<button id="listener-dictionary-button" type="button">Listener dictionary payload</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #ebe4f0;
                    }

                    button {
                        min-width: 300px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #4f3b79;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-dictionary-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Dictionary payload listener button must be discoverable before interaction.");

        IReadOnlyDictionary<string, object>? deliveredPayload = null;
        IElement? deliveredSender = null;
        var hitCount = 0;

        Func<IElement, IReadOnlyDictionary<string, object>, ValueTask> onClick = (sender, payload) =>
        {
            deliveredSender = sender;
            deliveredPayload = payload;
            Interlocked.Increment(ref hitCount);
            return ValueTask.CompletedTask;
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Dictionary async listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "Dictionary async listener must receive the element sender instance.");
            Assert.That(deliveredPayload, Is.Not.Null, "Dictionary async listener must materialize a dictionary payload.");
            Assert.That(deliveredPayload!["type"], Is.EqualTo("click"), "Dictionary async listener must expose the DOM event type.");
            Assert.That(deliveredPayload["isTrusted"], Is.True, "Dictionary async listener must expose the trusted-event flag.");
            Assert.That(deliveredPayload["targetId"], Is.EqualTo("listener-dictionary-button"), "Dictionary async listener must expose the originating target id.");
            Assert.That(deliveredPayload["currentTargetId"], Is.EqualTo("listener-dictionary-button"), "Dictionary async listener must expose the current target id.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsMutableObjectDictionaryPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Mutable object dictionary payload mapping requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Mutable Object Dictionary Listener Payload';
                document.body.innerHTML = '<button id="listener-mutable-object-dictionary-button" type="button">Listener mutable object dictionary payload</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #e8e2ef;
                    }

                    button {
                        min-width: 380px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #5c447d;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-mutable-object-dictionary-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Mutable object dictionary payload listener button must be discoverable before interaction.");

        IDictionary<string, object>? deliveredPayload = null;
        var hitCount = 0;

        Action<IDictionary<string, object>> onClick = payload =>
        {
            deliveredPayload = payload;
            Interlocked.Increment(ref hitCount);
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Mutable object dictionary listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredPayload, Is.Not.Null, "Mutable object dictionary listener must materialize a mutable dictionary payload.");
            Assert.That(deliveredPayload!["type"], Is.EqualTo("click"), "Mutable object dictionary listener must expose the DOM event type.");
            Assert.That(deliveredPayload["isTrusted"], Is.True, "Mutable object dictionary listener must expose the trusted-event flag.");
            Assert.That(deliveredPayload["targetId"], Is.EqualTo("listener-mutable-object-dictionary-button"), "Mutable object dictionary listener must expose the originating target id.");
            Assert.That(deliveredPayload["currentTargetId"], Is.EqualTo("listener-mutable-object-dictionary-button"), "Mutable object dictionary listener must expose the current target id.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsJsonElementDictionaryPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "JsonElement dictionary payload mapping requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser JsonElement Dictionary Listener Payload';
                document.body.innerHTML = '<button id="listener-json-element-dictionary-button" type="button">Listener JsonElement dictionary payload</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #e4edf0;
                    }

                    button {
                        min-width: 340px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #2f6771;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-json-element-dictionary-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "JsonElement dictionary payload listener button must be discoverable before interaction.");

        IReadOnlyDictionary<string, JsonElement>? deliveredPayload = null;
        var hitCount = 0;

        Action<IReadOnlyDictionary<string, JsonElement>> onClick = payload =>
        {
            deliveredPayload = payload;
            Interlocked.Increment(ref hitCount);
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "JsonElement dictionary listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredPayload, Is.Not.Null, "JsonElement dictionary listener must materialize a raw JsonElement payload dictionary.");
            Assert.That(deliveredPayload!["type"].GetString(), Is.EqualTo("click"), "JsonElement dictionary listener must expose the DOM event type.");
            Assert.That(deliveredPayload["isTrusted"].GetBoolean(), Is.True, "JsonElement dictionary listener must expose the trusted-event flag.");
            Assert.That(deliveredPayload["targetId"].GetString(), Is.EqualTo("listener-json-element-dictionary-button"), "JsonElement dictionary listener must expose the originating target id.");
            Assert.That(deliveredPayload["currentTargetId"].GetString(), Is.EqualTo("listener-json-element-dictionary-button"), "JsonElement dictionary listener must expose the current target id.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsMutableJsonElementDictionaryPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Mutable JsonElement dictionary payload mapping requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Mutable JsonElement Dictionary Listener Payload';
                document.body.innerHTML = '<button id="listener-mutable-json-element-dictionary-button" type="button">Listener mutable JsonElement dictionary payload</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #f0ece1;
                    }

                    button {
                        min-width: 380px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #6b5a2f;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-mutable-json-element-dictionary-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Mutable JsonElement dictionary payload listener button must be discoverable before interaction.");

        IDictionary<string, JsonElement>? deliveredPayload = null;
        var hitCount = 0;

        Action<IDictionary<string, JsonElement>> onClick = payload =>
        {
            deliveredPayload = payload;
            Interlocked.Increment(ref hitCount);
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Mutable JsonElement dictionary listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredPayload, Is.Not.Null, "Mutable JsonElement dictionary listener must materialize a mutable dictionary payload.");
            Assert.That(deliveredPayload!["type"].GetString(), Is.EqualTo("click"), "Mutable JsonElement dictionary listener must expose the DOM event type.");
            Assert.That(deliveredPayload["isTrusted"].GetBoolean(), Is.True, "Mutable JsonElement dictionary listener must expose the trusted-event flag.");
            Assert.That(deliveredPayload["targetId"].GetString(), Is.EqualTo("listener-mutable-json-element-dictionary-button"), "Mutable JsonElement dictionary listener must expose the originating target id.");
            Assert.That(deliveredPayload["currentTargetId"].GetString(), Is.EqualTo("listener-mutable-json-element-dictionary-button"), "Mutable JsonElement dictionary listener must expose the current target id.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsSenderOnlyAsyncHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Sender-only listener handler requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Sender Only Listener Handler';
                document.body.innerHTML = '<button id="listener-sender-only-button" type="button">Listener sender only</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #efe3e0;
                    }

                    button {
                        min-width: 280px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #8a3d3d;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-sender-only-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Sender-only listener button must be discoverable before interaction.");

        IElement? deliveredSender = null;
        var hitCount = 0;

        Func<IElement, ValueTask> onClick = sender =>
        {
            deliveredSender = sender;
            Interlocked.Increment(ref hitCount);
            return ValueTask.CompletedTask;
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Sender-only async listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "Sender-only async listener must receive the element sender instance.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsSenderOnlySyncElementHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Sender-only sync Element handler requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Sender Only Sync Element Handler';
                document.body.innerHTML = '<button id="listener-sender-only-sync-button" type="button">Listener sender only sync</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #e6efe8;
                    }

                    button {
                        min-width: 320px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #3e6d50;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-sender-only-sync-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Sender-only sync listener button must be discoverable before interaction.");

        Element? deliveredSender = null;
        var hitCount = 0;

        Action<Element> onClick = sender =>
        {
            deliveredSender = sender;
            Interlocked.Increment(ref hitCount);
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Sender-only sync Element listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "Sender-only sync Element listener must receive the concrete Element sender instance.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsSenderOnlySyncInterfaceHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Sender-only sync IElement handler requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Sender Only Sync IElement Handler';
                document.body.innerHTML = '<button id="listener-sender-only-sync-interface-button" type="button">Listener sender only sync interface</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #e3edf1;
                    }

                    button {
                        min-width: 380px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #376272;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-sender-only-sync-interface-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Sender-only sync IElement listener button must be discoverable before interaction.");

        IElement? deliveredSender = null;
        var hitCount = 0;

        Action<IElement> onClick = sender =>
        {
            deliveredSender = sender;
            Interlocked.Increment(ref hitCount);
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Sender-only sync IElement listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "Sender-only sync IElement listener must receive the sender instance.");
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementEventListenerSupportsSenderOnlyAsyncConcreteElementHandler()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var trustedInputSession = await RealBrowserTrustedInputSession.CreateAsync().ConfigureAwait(false);
        var browser = trustedInputSession.Browser;
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Sender-only async Element handler requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Sender Only Async Element Handler';
                document.body.innerHTML = '<button id="listener-sender-only-async-element-button" type="button">Listener sender only async element</button>';

                const style = document.createElement('style');
                style.textContent = `
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #f1ece3;
                    }

                    button {
                        min-width: 380px;
                        min-height: 72px;
                        border: 0;
                        border-radius: 999px;
                        background: #7a5f32;
                        color: white;
                        font: 600 18px/1.4 sans-serif;
                        cursor: pointer;
                    }
                `;

                document.head.appendChild(style);
            })();
            """).ConfigureAwait(false);

        var button = await page.WaitForElementAsync("#listener-sender-only-async-element-button", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(button, Is.Not.Null, "Sender-only async Element listener button must be discoverable before interaction.");

        Element? deliveredSender = null;
        var hitCount = 0;

        Func<Element, ValueTask> onClick = sender =>
        {
            deliveredSender = sender;
            Interlocked.Increment(ref hitCount);
            return ValueTask.CompletedTask;
        };

        await button!.AddEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref hitCount) == 0)
            await Task.Delay(50).ConfigureAwait(false);

        await button.RemoveEventListenerAsync("click", onClick).ConfigureAwait(false);
        await button.HumanityClickAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref hitCount), Is.EqualTo(1), "Sender-only async Element listener must receive exactly one relayed DOM event before unsubscription.");
            Assert.That(deliveredSender, Is.SameAs(button), "Sender-only async Element listener must receive the concrete Element sender instance.");
        });
    }

    [Test]
    public async Task RealBrowserPageCallbackSurfaceRelaysSubscribedFunctionAndRemovesHookOnUnsubscribe()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback surface requires a bootstrapped current page.").ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += (_, args) =>
        {
            callbacks.Add(args);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, args) => finalized.Add(args);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        _ = await page.EvaluateAsync("return app.ready('alpha', 7, true)").ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && (callbacks.Count == 0 || finalized.Count == 0))
            await Task.Delay(50).ConfigureAwait(false);

        await page.UnSubscribeAsync("app.ready").ConfigureAwait(false);
        var callbackTypeAfterUnsubscribe = await page.EvaluateAsync<string>("return typeof globalThis.app?.ready").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1), "Subscribed page callback must relay exactly one callback event.");
            Assert.That(finalized, Has.Count.EqualTo(1), "Subscribed page callback must relay exactly one callback finalized event.");
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(callbacks[0].Code, Does.Contain("app.ready("));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbackTypeAfterUnsubscribe, Is.EqualTo("undefined"), "UnSubscribeAsync must remove the injected callback hook from the page.");
        });
    }

    [Test]
    public async Task RealBrowserPageCallbackSurfaceContinuesOriginalFunctionByDefault()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback control requires a bootstrapped current page.").ConfigureAwait(false);
        await PreparePageCallbackHarnessAsync(page).ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += (_, args) =>
        {
            callbacks.Add(args);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, args) => finalized.Add(args);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var result = await page.EvaluateAsync<string>("return app.ready('alpha', 7, true)").ConfigureAwait(false);
        await WaitForPageCallbackRelayAsync(callbacks, finalized).ConfigureAwait(false);
        var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("original:alpha|7|true"), "Default callback flow must keep executing the original page function.");
            Assert.That(snapshot.Calls, Is.EqualTo("1"), "Default callback flow must call the original function exactly once.");
            Assert.That(snapshot.ArgsJson, Is.EqualTo("[\"alpha\",7,true]"), "Default callback flow must preserve the original arguments.");
            Assert.That(snapshot.Replaced, Is.EqualTo("0"), "Default callback flow must not mark the replacement path.");
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
        });
    }

    [Test]
    public async Task RealBrowserMainWorldCanReplayOriginalFunctionWithParsedCallbackDecisionArguments()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback replay requires a bootstrapped current page.").ConfigureAwait(false);
        await PreparePageCallbackHarnessAsync(page).ConfigureAwait(false);

        var result = await page.EvaluateAsync<string>(
            """
            return (() => {
                const requestId = 'atom-callback-test';
                const responseNodeId = 'atom-callback-response-' + requestId;
                const root = document.documentElement ?? document.head ?? document.body;
                if (!root) {
                    return 'missing-root';
                }

                document.getElementById(responseNodeId)?.remove();

                const responseNode = document.createElement('script');
                responseNode.id = responseNodeId;
                responseNode.type = 'application/json';
                responseNode.textContent = JSON.stringify({
                    action: 'continue',
                    args: ['beta', 9, false],
                });
                root.appendChild(responseNode);

                const decision = JSON.parse(document.getElementById(responseNodeId)?.textContent ?? 'null') ?? { action: 'continue' };
                document.getElementById(responseNodeId)?.remove();

                const effectiveArgs = decision && Array.isArray(decision.args)
                    ? decision.args
                    : ['alpha', 7, true];

                return globalThis.app.ready.apply(globalThis.app, effectiveArgs);
            })();
            """).ConfigureAwait(false);
        var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("original:beta|9|false"), "Parsed callback-decision args must remain replayable through the original page function.");
            Assert.That(snapshot.Calls, Is.EqualTo("1"), "The original page function must be invoked exactly once when replaying parsed callback args.");
            Assert.That(snapshot.ArgsJson, Is.EqualTo("[\"beta\",9,false]"), "Parsed callback args must reach the original page function unchanged.");
            Assert.That(snapshot.Replaced, Is.EqualTo("0"));
        });
    }

    [Test]
    public async Task RealBrowserCallbackRequestBridgeWritesReplacementArgumentDecisionPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback bridge requires a bootstrapped current page.").ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];

        page.Callback += async (_, args) =>
        {
            callbacks.Add(args);
            await args.ContinueAsync(["beta", 9, false]).ConfigureAwait(false);
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var payload = await page.EvaluateAsync<string>(
            """
            return (() => {
                const requestId = 'atom-direct-request';
                const responseNodeId = 'atom-callback-response-' + requestId;
                const root = document.documentElement ?? document.head ?? document.body;
                if (!root) {
                    return 'missing-root';
                }

                document.getElementById(responseNodeId)?.remove();

                const node = document.createElement('script');
                node.type = 'application/json';
                node.dataset.atomCallbackRequest = '1';
                node.textContent = JSON.stringify({
                    requestId,
                    name: 'app.ready',
                    args: ['alpha', 7, true],
                    code: "app.ready('alpha', 7, true)",
                });
                root.appendChild(node);
                document.dispatchEvent(new Event('atom-webdriver-callback-request'));
                globalThis.dispatchEvent(new Event('atom-webdriver-callback-request'));

                const responseNode = document.getElementById(responseNodeId);
                const responseText = responseNode?.textContent ?? '<missing>';
                responseNode?.remove();
                return responseText;
            })();
            """).ConfigureAwait(false);

        Assert.That(
            callbacks,
            Has.Count.EqualTo(1),
            await DescribePageCallbackFailureAsync(browser, page, callbacks, payload: payload).ConfigureAwait(false));

        using var document = JsonDocument.Parse(payload ?? "{}");
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("action", out var action), Is.True);
            Assert.That(action.GetString(), Is.EqualTo("continue"));
            Assert.That(root.TryGetProperty("args", out var args), Is.True);
            Assert.That(args.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(args.GetArrayLength(), Is.EqualTo(3));
            Assert.That(args[0].GetString(), Is.EqualTo("beta"));
            Assert.That(args[1].GetInt32(), Is.EqualTo(9));
            Assert.That(args[2].GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task RealBrowserCallbackRequestBridgeWritesDefaultContinueDecisionPayload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback bridge requires a bootstrapped current page.").ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];

        page.Callback += (_, args) =>
        {
            callbacks.Add(args);
            return ValueTask.CompletedTask;
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        string? payload = null;
        try
        {
            payload = await page.EvaluateAsync<string>(
                """
                return (() => {
                    const requestId = 'atom-default-request';
                    const responseNodeId = 'atom-callback-response-' + requestId;
                    const root = document.documentElement ?? document.head ?? document.body;
                    if (!root) {
                        return 'missing-root';
                    }

                    document.getElementById(responseNodeId)?.remove();

                    const node = document.createElement('script');
                    node.type = 'application/json';
                    node.dataset.atomCallbackRequest = '1';
                    node.textContent = JSON.stringify({
                        requestId,
                        name: 'app.ready',
                        args: ['alpha', 7, true],
                        code: "app.ready('alpha', 7, true)",
                    });
                    root.appendChild(node);
                    document.dispatchEvent(new Event('atom-webdriver-callback-request'));
                    globalThis.dispatchEvent(new Event('atom-webdriver-callback-request'));

                    const responseNode = document.getElementById(responseNodeId);
                    const callbackDebug = typeof globalThis.__atomCallbackBridgeLastError === 'string'
                        ? globalThis.__atomCallbackBridgeLastError
                        : '';
                    const responseText = responseNode?.textContent ?? ('<missing>' + (callbackDebug ? '|debug=' + callbackDebug : ''));
                    responseNode?.remove();
                    return responseText;
                })();
                """).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Fail($"{ex.Message} | {await DescribePageCallbackFailureAsync(browser, page, callbacks).ConfigureAwait(false)}");
            throw;
        }

        Assert.That(
            callbacks,
            Has.Count.EqualTo(1),
            await DescribePageCallbackFailureAsync(browser, page, callbacks, payload: payload).ConfigureAwait(false));

        using var document = JsonDocument.Parse(payload ?? "{}");
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("action", out var action), Is.True);
            Assert.That(action.GetString(), Is.EqualTo("continue"));
            Assert.That(root.TryGetProperty("args", out _), Is.False);
            Assert.That(root.TryGetProperty("code", out _), Is.False);
        });
    }

    [Test]
    public async Task RealBrowserPageCallbackSurfaceCanContinueWithReplacementArguments()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback control requires a bootstrapped current page.").ConfigureAwait(false);
        await PreparePageCallbackHarnessAsync(page).ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += async (_, args) =>
        {
            callbacks.Add(args);
            await args.ContinueAsync(["beta", 9, false]).ConfigureAwait(false);
        };
        page.CallbackFinalized += (_, args) => finalized.Add(args);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var result = await page.EvaluateAsync<string>("return app.ready('alpha', 7, true)").ConfigureAwait(false);
        try
        {
            await WaitForPageCallbackRelayAsync(callbacks, finalized).ConfigureAwait(false);
        }
        catch (AssertionException)
        {
            Assert.Fail(await DescribePageCallbackFailureAsync(browser, page, callbacks, finalized, result, includeHarnessSnapshot: true).ConfigureAwait(false));
        }

        var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("original:beta|9|false"), "ContinueAsync(args) must re-invoke the original function with replacement arguments.");
            Assert.That(snapshot.Calls, Is.EqualTo("1"), "ContinueAsync(args) must keep the original function active.");
            Assert.That(snapshot.ArgsJson, Is.EqualTo("[\"beta\",9,false]"), "ContinueAsync(args) must reach the original function with the rewritten argument list.");
            Assert.That(snapshot.Replaced, Is.EqualTo("0"), "ContinueAsync(args) must not mark the replacement path.");
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
        });
    }

    [Test]
    public async Task RealBrowserPageCallbackSurfaceCanAbortOriginalFunction()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback control requires a bootstrapped current page.").ConfigureAwait(false);
        await PreparePageCallbackHarnessAsync(page).ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += async (_, args) =>
        {
            callbacks.Add(args);
            await args.AbortAsync().ConfigureAwait(false);
        };
        page.CallbackFinalized += (_, args) => finalized.Add(args);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var result = await page.EvaluateAsync<string>("return typeof app.ready('alpha', 7, true)").ConfigureAwait(false);
        await WaitForPageCallbackRelayAsync(callbacks, finalized).ConfigureAwait(false);
        var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("undefined"), "AbortAsync must suppress the original callback return value.");
            Assert.That(snapshot.Calls, Is.EqualTo("0"), "AbortAsync must prevent the original function from executing.");
            Assert.That(snapshot.ArgsJson, Is.EqualTo("[]"), "AbortAsync must leave the original function state untouched.");
            Assert.That(snapshot.Replaced, Is.EqualTo("0"), "AbortAsync must not mark the replacement path.");
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].IsCancelled, Is.True);
        });
    }

    [Test]
    public async Task RealBrowserPageCallbackSurfaceCanReplaceOriginalFunctionCode()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Page callback control requires a bootstrapped current page.").ConfigureAwait(false);
        await PreparePageCallbackHarnessAsync(page).ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += async (_, args) =>
        {
            callbacks.Add(args);
            await args.ReplaceAsync("document.documentElement.dataset.atomReadyReplaced = '1'; return 'patched:' + args.join('|');").ConfigureAwait(false);
        };
        page.CallbackFinalized += (_, args) => finalized.Add(args);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var result = await page.EvaluateAsync<string>("return app.ready('alpha', 7, true)").ConfigureAwait(false);
        await WaitForPageCallbackRelayAsync(callbacks, finalized).ConfigureAwait(false);
        var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("patched:alpha|7|true"), "ReplaceAsync must swap the original callback body with the supplied JS code.");
            Assert.That(snapshot.Calls, Is.EqualTo("0"), "ReplaceAsync must skip the original function body.");
            Assert.That(snapshot.ArgsJson, Is.EqualTo("[]"), "ReplaceAsync must not execute the original function state updates.");
            Assert.That(snapshot.Replaced, Is.EqualTo("1"), "ReplaceAsync must execute the replacement body in page context.");
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
        });
    }

    [Test]
    public async Task RealBrowserCurrentPageElementMutationApisModifyDomState()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Element mutation surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Element Mutations';
                document.body.innerHTML = `
                    <main id="mutation-root">
                        <input id="mutation-input" value="initial" />
                        <div id="mutation-target" class="seed removable"><span class="before">before-text</span></div>
                    </main>`;

                document.documentElement.dataset.atomMutationInputEvents = '0';
                document.documentElement.dataset.atomMutationChangeEvents = '0';

                const input = document.getElementById('mutation-input');
                input?.addEventListener('input', () => {
                    document.documentElement.dataset.atomMutationInputEvents = String(Number(document.documentElement.dataset.atomMutationInputEvents || '0') + 1);
                });
                input?.addEventListener('change', () => {
                    document.documentElement.dataset.atomMutationChangeEvents = String(Number(document.documentElement.dataset.atomMutationChangeEvents || '0') + 1);
                });
            })();
            """).ConfigureAwait(false);

        var input = await page.WaitForElementAsync("#mutation-input", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var target = await page.WaitForElementAsync("#mutation-target", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(input, Is.Not.Null, "Mutation input must be discoverable before interaction.");
            Assert.That(target, Is.Not.Null, "Mutation target must be discoverable before interaction.");
        });

        var inputElement = input!;
        var targetElement = target!;

        var evaluatedIdBefore = await targetElement.EvaluateAsync<string>("element.id").ConfigureAwait(false);

        await inputElement.SetValueAsync("bridge-value").ConfigureAwait(false);
        await targetElement.SetAttributeAsync("data-bridge", "attr-value").ConfigureAwait(false);
        await targetElement.SetStyleAsync("color", "rgb(12, 34, 56)").ConfigureAwait(false);
        await targetElement.AddClassAsync("added-class").ConfigureAwait(false);
        await targetElement.RemoveClassAsync("removable").ConfigureAwait(false);
        await targetElement.ToggleClassAsync("toggled-class").ConfigureAwait(false);
        await targetElement.SetCustomPropertyAsync("atomCustomBridgeValue", "custom-prop").ConfigureAwait(false);
        await targetElement.SetDataAsync("payload", "data-value").ConfigureAwait(false);
        await targetElement.SetContentAsync("<span class=\"after\">after-text</span>").ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        var inputEventCount = "0";
        var changeEventCount = "0";

        while (DateTime.UtcNow < deadline)
        {
            inputEventCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomMutationInputEvents ?? '0')").ConfigureAwait(false) ?? "0";
            changeEventCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomMutationChangeEvents ?? '0')").ConfigureAwait(false) ?? "0";

            if (inputEventCount == "1" && changeEventCount == "1")
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var inputValue = await inputElement.GetValueAsync().ConfigureAwait(false);
        var bridgeAttribute = await targetElement.GetAttributeAsync("data-bridge").ConfigureAwait(false);
        var customData = await targetElement.GetCustomDataAsync("payload").ConfigureAwait(false);
        var classList = (await targetElement.GetClassListAsync().ConfigureAwait(false)).ToArray();
        var innerText = await targetElement.GetInnerTextAsync().ConfigureAwait(false);
        var evaluatedIdAfter = await targetElement.EvaluateAsync<string>("element.id").ConfigureAwait(false);
        var customProperty = await targetElement.EvaluateAsync<string>("String(element.atomCustomBridgeValue ?? '')").ConfigureAwait(false);
        var computedColor = await targetElement.EvaluateAsync<string>("getComputedStyle(element).color").ConfigureAwait(false);
        var afterText = await targetElement.EvaluateAsync<string>("element.querySelector('.after')?.textContent ?? ''").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(evaluatedIdBefore, Is.EqualTo("mutation-target"), "Element.EvaluateAsync must expose the live target element to the execution context before mutation.");
            Assert.That(evaluatedIdAfter, Is.EqualTo("mutation-target"), "Element.EvaluateAsync must keep exposing the live target element after mutation.");
            Assert.That(inputValue, Is.EqualTo("bridge-value"), "SetValueAsync must update the live input value.");
            Assert.That(inputEventCount, Is.EqualTo("1"), "SetValueAsync must dispatch a bubbling input event exactly once.");
            Assert.That(changeEventCount, Is.EqualTo("1"), "SetValueAsync must dispatch a bubbling change event exactly once.");
            Assert.That(bridgeAttribute, Is.EqualTo("attr-value"), "SetAttributeAsync must persist the target attribute.");
            Assert.That(customData, Is.EqualTo("data-value"), "SetDataAsync must remain observable through GetCustomDataAsync.");
            Assert.That(classList, Does.Contain("seed"), "Add/Remove/Toggle class flow must preserve existing classes.");
            Assert.That(classList, Does.Contain("added-class"), "AddClassAsync must add the requested class.");
            Assert.That(classList, Does.Contain("toggled-class"), "ToggleClassAsync must add the requested class when it is absent.");
            Assert.That(classList, Does.Not.Contain("removable"), "RemoveClassAsync must remove the requested class.");
            Assert.That(innerText, Is.EqualTo("after-text"), "SetContentAsync must replace the element HTML content.");
            Assert.That(afterText, Is.EqualTo("after-text"), "Element.EvaluateAsync must observe the mutated descendant content.");
            Assert.That(customProperty, Is.EqualTo("custom-prop"), "SetCustomPropertyAsync must persist a live custom JS property on the element.");
            Assert.That(computedColor, Is.EqualTo("rgb(12, 34, 56)"), "SetStyleAsync must update the live computed style.");
        });
    }

    [Test]
    public async Task RealBrowserChildFrameElementMutationApisModifyFrameDomState()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Frame element mutation surface requires a bootstrapped current page.").ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            (() => {
                document.title = 'Real Browser Frame Element Mutations';
                document.body.innerHTML = `
                    <iframe
                        id="mutation-frame-host"
                        srcdoc="<!doctype html><html><body><div id='frame-target' class='frame-seed'>frame-before</div></body></html>">
                    </iframe>`;
            })();
            """).ConfigureAwait(false);

        var frameHost = await page.WaitForElementAsync("#mutation-frame-host", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(frameHost, Is.Not.Null, "Mutation frame host must be discoverable before frame traversal.");

        var childFrame = (await frameHost!.GetChildFramesAsync().ConfigureAwait(false)).SingleOrDefault();
        Assert.That(childFrame, Is.Not.Null, "Mutation frame host must expose a single child frame.");

        var frameTarget = await childFrame!.WaitForElementAsync("#frame-target", WaitForElementKind.Attached, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.That(frameTarget, Is.Not.Null, "Mutation frame target must be discoverable inside the child frame.");

        await frameTarget!.SetAttributeAsync("data-frame", "frame-attr").ConfigureAwait(false);
        await frameTarget.AddClassAsync("frame-added").ConfigureAwait(false);
        await frameTarget.RemoveClassAsync("frame-seed").ConfigureAwait(false);
        await frameTarget.SetContentAsync("<span class=\"frame-after\">frame-after</span>").ConfigureAwait(false);

        var frameAttribute = await frameTarget.GetAttributeAsync("data-frame").ConfigureAwait(false);
        var frameClassList = (await frameTarget.GetClassListAsync().ConfigureAwait(false)).ToArray();
        var frameText = await frameTarget.GetInnerTextAsync().ConfigureAwait(false);
        var frameAfterText = await frameTarget.EvaluateAsync<string>("element.querySelector('.frame-after')?.textContent ?? ''").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(frameAttribute, Is.EqualTo("frame-attr"), "SetAttributeAsync must mutate the live iframe element state.");
            Assert.That(frameClassList, Does.Contain("frame-added"), "AddClassAsync must work for elements discovered inside child frames.");
            Assert.That(frameClassList, Does.Not.Contain("frame-seed"), "RemoveClassAsync must work for elements discovered inside child frames.");
            Assert.That(frameText, Is.EqualTo("frame-after"), "SetContentAsync must update the live iframe element content.");
            Assert.That(frameAfterText, Is.EqualTo("frame-after"), "Element.EvaluateAsync must stay bound to iframe elements too.");
        });
    }

    [Test]
    public async Task RealBrowserBrowserAndWindowDelegationRemainBoundToCurrentSnapshotsAcrossWindows()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowNonCurrentPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync(new WebPageSettings()).ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings()).ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, firstWindowCurrentPage, "Delegation surface requires a bootstrapped first-window current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "Delegation surface requires a bootstrapped second-window current page.").ConfigureAwait(false);

        var browserCurrentTarget = new LookupTarget("Delegation Browser Current", new Uri("https://127.0.0.1/real-browser-delegation/browser-current"), "delegation-browser-current", "browser");
        var firstWindowCurrentTarget = new LookupTarget("Delegation First Window Current", new Uri("https://127.0.0.1/real-browser-delegation/window-current"), "delegation-window-current", "window");
        var firstWindowNonCurrentTarget = new LookupTarget("Delegation First Window Hidden", new Uri("https://127.0.0.1/real-browser-delegation/window-hidden"), "delegation-window-hidden", "page");

        var browserResponse = await browser.NavigateAsync(browserCurrentTarget.Url, new NavigationSettings
        {
            Html = CreateLookupHtml(browserCurrentTarget.Title, browserCurrentTarget.MarkerId, browserCurrentTarget.MarkerValue),
        }).ConfigureAwait(false);
        var windowResponse = await firstWindow.NavigateAsync(firstWindowCurrentTarget.Url, new NavigationSettings
        {
            Html = CreateLookupHtml(firstWindowCurrentTarget.Title, firstWindowCurrentTarget.MarkerId, firstWindowCurrentTarget.MarkerValue),
        }).ConfigureAwait(false);
        var pageResponse = await firstWindowNonCurrentPage.NavigateAsync(firstWindowNonCurrentTarget.Url, CreateLookupHtml(firstWindowNonCurrentTarget.Title, firstWindowNonCurrentTarget.MarkerId, firstWindowNonCurrentTarget.MarkerValue)).ConfigureAwait(false);
        var browserReloadResponse = await browser.ReloadAsync().ConfigureAwait(false);
        var windowReloadResponse = await firstWindow.ReloadAsync().ConfigureAwait(false);
        var pageReloadResponse = await firstWindowNonCurrentPage.ReloadAsync().ConfigureAwait(false);

        var firstWindowTitle = await firstWindow.GetTitleAsync().ConfigureAwait(false);
        var firstWindowUrl = await firstWindow.GetUrlAsync().ConfigureAwait(false);
        var secondWindowTitle = await secondWindow.GetTitleAsync().ConfigureAwait(false);
        var secondWindowUrl = await secondWindow.GetUrlAsync().ConfigureAwait(false);
        var firstWindowBounds = await firstWindow.GetBoundingBoxAsync().ConfigureAwait(false);
        var secondWindowBounds = await secondWindow.GetBoundingBoxAsync().ConfigureAwait(false);
        var currentBrowserTitle = await secondWindowCurrentPage.GetTitleAsync().ConfigureAwait(false);
        var currentBrowserUrl = await secondWindowCurrentPage.GetUrlAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browserResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(windowResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pageResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(browserReloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(windowReloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pageReloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));

            Assert.That(firstWindowCurrentPage.CurrentTitle, Is.EqualTo(firstWindowCurrentTarget.Title));
            Assert.That(firstWindowCurrentPage.CurrentUrl, Is.EqualTo(firstWindowCurrentTarget.Url));
            Assert.That(firstWindowNonCurrentPage.CurrentTitle, Is.EqualTo(firstWindowNonCurrentTarget.Title));
            Assert.That(firstWindowNonCurrentPage.CurrentUrl, Is.EqualTo(firstWindowNonCurrentTarget.Url));
            Assert.That(secondWindowCurrentPage.CurrentTitle, Is.EqualTo(browserCurrentTarget.Title));
            Assert.That(secondWindowCurrentPage.CurrentUrl, Is.EqualTo(browserCurrentTarget.Url));
            Assert.That(firstWindowTitle, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(firstWindowUrl, Is.Not.Null);
            Assert.That(firstWindowUrl!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
            Assert.That(secondWindowTitle, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(secondWindowUrl, Is.Not.Null);
            Assert.That(secondWindowUrl!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
            Assert.That(currentBrowserTitle, Is.EqualTo("Atom Bridge Discovery"));
            Assert.That(currentBrowserUrl, Is.Not.Null);
            Assert.That(currentBrowserUrl!.AbsoluteUri, Does.StartWith("http://127.0.0.1:"));
            Assert.That(firstWindowBounds, Is.Not.Null);
            Assert.That(secondWindowBounds, Is.Not.Null);
        });
    }

    [Test]
    public async Task RealBrowserPageBridgeSmokeUsesLiveTransport()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var currentPage = (WebPage)browser.CurrentPage;

        await currentPage.NavigateAsync(new Uri("https://127.0.0.1/live-bridge-local"), new NavigationSettings
        {
            Html = "<html><head><title>Local Bridge Title</title></head><body>ok</body></html>",
        }).ConfigureAwait(false);

        await using var server = await StartBoundBridgeAsync(currentPage).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, currentPage.TabId).ConfigureAwait(false);

        var titleTask = currentPage.GetTitleAsync().AsTask();
        var titleRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, currentPage.TabId, "\"Live Bridge Title\"").ConfigureAwait(false);
        var title = await titleTask.ConfigureAwait(false);

        var urlTask = currentPage.GetUrlAsync().AsTask();
        var urlRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, currentPage.TabId, "\"https://127.0.0.1/live-transport\"").ConfigureAwait(false);
        var url = await urlTask.ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1 &&
            snapshot.GetProperty("tabs").GetInt32() == 1 &&
            snapshot.GetProperty("completedRequests").GetInt64() == 2).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(titleRequest.Payload.HasValue, Is.False);
            Assert.That(urlRequest.Payload.HasValue, Is.False);
            Assert.That(currentPage.CurrentTitle, Is.EqualTo("Local Bridge Title"));
            Assert.That(currentPage.CurrentUrl, Is.EqualTo(new Uri("https://127.0.0.1/live-bridge-local")));
            Assert.That(title, Is.EqualTo("Live Bridge Title"));
            Assert.That(url, Is.EqualTo(new Uri("https://127.0.0.1/live-transport")));
            Assert.That(health.GetProperty("connections").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public async Task RealBrowserCookieSurfacePersistsAcrossNavigationAndClear()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var currentPage = (WebPage)browser.CurrentPage;
        var bootstrapped = IsBridgeCommandsBound(currentPage);
        var diagnostics = bootstrapped ? null : await DescribeBootstrapFailureAsync(browser, currentPage).ConfigureAwait(false);

        Assert.That(bootstrapped, Is.True, $"Real-browser cookie smoke requires a bootstrapped discovery page. {diagnostics}");

        await currentPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]).ConfigureAwait(false);

        var cookiesAfterSet = (await currentPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var documentCookieAfterSet = (await currentPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;

        await currentPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-surface"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Surface</title></head><body>cookies</body></html>",
        }).ConfigureAwait(false);

        var cookiesAfterNavigate = (await currentPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var documentCookieAfterNavigate = (await currentPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;

        await currentPage.ClearAllCookiesAsync().ConfigureAwait(false);

        var cookiesAfterClear = (await currentPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var documentCookieAfterClear = (await currentPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var documentCookieAfterSetDiagnostics = documentCookieAfterSet.Contains("session=alpha", StringComparison.Ordinal)
            ? null
            : await DescribeDocumentCookieFailureAsync(browser, currentPage, documentCookieAfterSet).ConfigureAwait(false);
        var documentCookieAfterNavigateDiagnostics = documentCookieAfterNavigate.Contains("session=alpha", StringComparison.Ordinal)
            ? null
            : await DescribeDocumentCookieFailureAsync(browser, currentPage, documentCookieAfterNavigate).ConfigureAwait(false);
        var documentCookieAfterClearDiagnostics = string.IsNullOrEmpty(documentCookieAfterClear)
            ? null
            : await DescribeDocumentCookieFailureAsync(browser, currentPage, documentCookieAfterClear).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(cookiesAfterSet, Has.Length.EqualTo(1));
            Assert.That(cookiesAfterSet[0].Name, Is.EqualTo("session"));
            Assert.That(cookiesAfterSet[0].Value, Is.EqualTo("alpha"));
            Assert.That(documentCookieAfterSet, Does.Contain("session=alpha"), documentCookieAfterSetDiagnostics);
            Assert.That(currentPage.CurrentUrl, Is.EqualTo(new Uri("https://127.0.0.1/real-browser-cookie-surface")));
            Assert.That(currentPage.CurrentTitle, Is.EqualTo("Real Browser Cookie Surface"));
            Assert.That(cookiesAfterNavigate, Has.Length.EqualTo(1));
            Assert.That(cookiesAfterNavigate[0].Name, Is.EqualTo("session"));
            Assert.That(cookiesAfterNavigate[0].Value, Is.EqualTo("alpha"));
            Assert.That(documentCookieAfterNavigate, Does.Contain("session=alpha"), documentCookieAfterNavigateDiagnostics);
            Assert.That(cookiesAfterClear, Is.Empty);
            Assert.That(documentCookieAfterClear, Is.Empty, documentCookieAfterClearDiagnostics);
        });
    }

    [Test]
    public async Task RealBrowserSameSiteTabsKeepCookieIsolation()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var secondBootstrapped = IsBridgeCommandsBound(secondPage);
        var secondDiagnostics = secondBootstrapped ? null : await DescribeBootstrapFailureAsync(browser, secondPage).ConfigureAwait(false);

        Assert.That(secondBootstrapped, Is.True, $"Real-browser same-site cookie isolation requires a bootstrapped second page. {secondDiagnostics}");

        await firstPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-isolation/first"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Isolation A</title></head><body>first</body></html>",
        }).ConfigureAwait(false);
        await secondPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-isolation/second"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Isolation B</title></head><body>second</body></html>",
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstPage.TabId, Is.Not.EqualTo(secondPage.TabId));
            Assert.That(firstPage.BoundBridgeTabId, Is.Not.EqualTo(secondPage.BoundBridgeTabId));
            Assert.That(firstPage.GetOrCreateBridgeContextId(), Is.Not.EqualTo(secondPage.GetOrCreateBridgeContextId()));
        });

        await firstPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]).ConfigureAwait(false);
        await secondPage.SetCookiesAsync([new Cookie("session", "beta", "/", LocalCookieDomain)]).ConfigureAwait(false);

        await WaitForDocumentCookieAsync(browser, firstPage, "session=alpha").ConfigureAwait(false);
        await WaitForDocumentCookieAsync(browser, secondPage, "session=beta").ConfigureAwait(false);

        var firstDocumentCookie = (await firstPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var secondDocumentCookie = (await secondPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var firstCookies = (await firstPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var secondCookies = (await secondPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstDocumentCookie, Does.Contain("session=alpha"));
            Assert.That(firstDocumentCookie, Does.Not.Contain("session=beta"));
            Assert.That(secondDocumentCookie, Does.Contain("session=beta"));
            Assert.That(secondDocumentCookie, Does.Not.Contain("session=alpha"));
            Assert.That(firstCookies, Has.Length.EqualTo(1));
            Assert.That(firstCookies[0].Name, Is.EqualTo("session"));
            Assert.That(firstCookies[0].Value, Is.EqualTo("alpha"));
            Assert.That(secondCookies, Has.Length.EqualTo(1));
            Assert.That(secondCookies[0].Name, Is.EqualTo("session"));
            Assert.That(secondCookies[0].Value, Is.EqualTo("beta"));
        });
    }

    [Test]
    public async Task RealBrowserSameSiteWindowAndBrowserCookieClearRemainContextLocal()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var thirdPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, secondPage, "Real-browser same-site clear requires a bootstrapped second page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, thirdPage, "Real-browser same-site clear requires a bootstrapped third page.").ConfigureAwait(false);

        await firstPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-clear/first"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Clear A</title></head><body>first</body></html>",
        }).ConfigureAwait(false);
        await secondPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-clear/second"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Clear B</title></head><body>second</body></html>",
        }).ConfigureAwait(false);
        await thirdPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-cookie-clear/third"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Cookie Clear C</title></head><body>third</body></html>",
        }).ConfigureAwait(false);

        await firstPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]).ConfigureAwait(false);
        await secondPage.SetCookiesAsync([new Cookie("session", "beta", "/", LocalCookieDomain)]).ConfigureAwait(false);
        await thirdPage.SetCookiesAsync([new Cookie("session", "gamma", "/", LocalCookieDomain)]).ConfigureAwait(false);

        await WaitForDocumentCookieAsync(browser, firstPage, "session=alpha").ConfigureAwait(false);
        await WaitForDocumentCookieAsync(browser, secondPage, "session=beta").ConfigureAwait(false);
        await WaitForDocumentCookieAsync(browser, thirdPage, "session=gamma").ConfigureAwait(false);

        await firstWindow.ClearAllCookiesAsync().ConfigureAwait(false);

        await WaitForDocumentCookieAsync(browser, firstPage, static cookie => string.IsNullOrEmpty(cookie), "Timed out waiting for first same-site page cookies to clear.").ConfigureAwait(false);
        await WaitForDocumentCookieAsync(browser, secondPage, static cookie => string.IsNullOrEmpty(cookie), "Timed out waiting for second same-site page cookies to clear.").ConfigureAwait(false);
        await WaitForDocumentCookieAsync(browser, thirdPage, static cookie => cookie.Contains("session=gamma", StringComparison.Ordinal), "Timed out waiting for third same-site page cookie to remain intact after window clear.").ConfigureAwait(false);

        var firstCookiesAfterWindowClear = (await firstPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var secondCookiesAfterWindowClear = (await secondPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var thirdCookiesAfterWindowClear = (await thirdPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var firstDocumentCookieAfterWindowClear = (await firstPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var secondDocumentCookieAfterWindowClear = (await secondPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
        var thirdDocumentCookieAfterWindowClear = (await thirdPage.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;

        await browser.ClearAllCookiesAsync().ConfigureAwait(false);

        await WaitForDocumentCookieAsync(browser, thirdPage, static cookie => string.IsNullOrEmpty(cookie), "Timed out waiting for browser-wide clear to remove the remaining same-site cookie.").ConfigureAwait(false);

        var firstCookiesAfterBrowserClear = (await firstPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var secondCookiesAfterBrowserClear = (await secondPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var thirdCookiesAfterBrowserClear = (await thirdPage.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(thirdPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(secondPage));

            Assert.That(firstCookiesAfterWindowClear, Is.Empty);
            Assert.That(secondCookiesAfterWindowClear, Is.Empty);
            Assert.That(thirdCookiesAfterWindowClear, Has.Length.EqualTo(1));
            Assert.That(thirdCookiesAfterWindowClear[0].Name, Is.EqualTo("session"));
            Assert.That(thirdCookiesAfterWindowClear[0].Value, Is.EqualTo("gamma"));
            Assert.That(firstDocumentCookieAfterWindowClear, Is.Empty);
            Assert.That(secondDocumentCookieAfterWindowClear, Is.Empty);
            Assert.That(thirdDocumentCookieAfterWindowClear, Does.Contain("session=gamma"));

            Assert.That(firstCookiesAfterBrowserClear, Is.Empty);
            Assert.That(secondCookiesAfterBrowserClear, Is.Empty);
            Assert.That(thirdCookiesAfterBrowserClear, Is.Empty);
        });
    }

    [Test]
    public async Task RealBrowserSameSiteTabsKeepLocalAndSessionStorageIsolationAcrossNavigation()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var firstUrl = new Uri("https://127.0.0.1/real-browser-storage-isolation/first");
        var secondUrl = new Uri("https://127.0.0.1/real-browser-storage-isolation/second");
        var firstNavigationUrl = new Uri("https://127.0.0.1/real-browser-storage-isolation/first-after-navigation");

        await AssertPageBootstrappedAsync(browser, firstPage, "Real-browser storage isolation requires a bootstrapped first page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondPage, "Real-browser storage isolation requires a bootstrapped second page.").ConfigureAwait(false);

        await firstPage.NavigateAsync(firstUrl, new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Storage Isolation A</title></head><body>first-storage</body></html>",
        }).ConfigureAwait(false);
        await secondPage.NavigateAsync(secondUrl, new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Storage Isolation B</title></head><body>second-storage</body></html>",
        }).ConfigureAwait(false);

        var firstAfterSet = await CaptureStorageSurfaceSnapshotAsync(firstPage, localValue: "alpha", sessionValue: "one").ConfigureAwait(false);
        var secondBeforeSet = await CaptureStorageSurfaceSnapshotAsync(secondPage).ConfigureAwait(false);

        await firstPage.NavigateAsync(firstNavigationUrl, new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Storage Isolation C</title></head><body>first-storage-after-navigation</body></html>",
        }).ConfigureAwait(false);

        var firstAfterNavigate = await CaptureStorageSurfaceSnapshotAsync(firstPage).ConfigureAwait(false);
        var secondAfterSet = await CaptureStorageSurfaceSnapshotAsync(secondPage, localValue: "beta", sessionValue: "two").ConfigureAwait(false);
        var firstAfterSecondSet = await CaptureStorageSurfaceSnapshotAsync(firstPage).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstPage.GetOrCreateBridgeContextId(), Is.Not.EqualTo(secondPage.GetOrCreateBridgeContextId()));
            Assert.That(firstAfterSet.ContextId, Is.Not.EqualTo(secondBeforeSet.ContextId));

            Assert.That(firstAfterSet.Error, Is.Null, $"First storage snapshot failed. snapshot={firstAfterSet}");
            Assert.That(secondBeforeSet.Error, Is.Null, $"Second pre-write storage snapshot failed. snapshot={secondBeforeSet}");
            Assert.That(firstAfterNavigate.Error, Is.Null, $"First post-navigation storage snapshot failed. snapshot={firstAfterNavigate}");
            Assert.That(secondAfterSet.Error, Is.Null, $"Second post-write storage snapshot failed. snapshot={secondAfterSet}");
            Assert.That(firstAfterSecondSet.Error, Is.Null, $"First post-second-write storage snapshot failed. snapshot={firstAfterSecondSet}");

            Assert.That(firstAfterSet.HasLocalStorage, Is.True, $"First page localStorage must be available. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.HasSessionStorage, Is.True, $"First page sessionStorage must be available. snapshot={firstAfterSet}");
            Assert.That(secondBeforeSet.HasLocalStorage, Is.True, $"Second page localStorage must be available. snapshot={secondBeforeSet}");
            Assert.That(secondBeforeSet.HasSessionStorage, Is.True, $"Second page sessionStorage must be available. snapshot={secondBeforeSet}");

            Assert.That(firstAfterSet.LocalValue, Is.EqualTo("alpha"), $"First page localStorage must keep its own value. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.SessionValue, Is.EqualTo("one"), $"First page sessionStorage must keep its own value. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.LocalLength, Is.GreaterThanOrEqualTo(1), $"First page localStorage must expose at least the probe key. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.SessionLength, Is.GreaterThanOrEqualTo(1), $"First page sessionStorage must expose at least the probe key. snapshot={firstAfterSet}");

            Assert.That(secondBeforeSet.LocalValue, Is.Null, $"Second page localStorage must stay isolated from the first page. snapshot={secondBeforeSet}");
            Assert.That(secondBeforeSet.SessionValue, Is.Null, $"Second page sessionStorage must stay isolated from the first page. snapshot={secondBeforeSet}");

            Assert.That(firstAfterNavigate.LocalValue, Is.EqualTo("alpha"), $"Same-tab same-origin navigation must preserve isolated localStorage. snapshot={firstAfterNavigate}");
            Assert.That(firstAfterNavigate.SessionValue, Is.EqualTo("one"), $"Same-tab same-origin navigation must preserve isolated sessionStorage. snapshot={firstAfterNavigate}");
            Assert.That(firstPage.CurrentUrl, Is.EqualTo(firstNavigationUrl));
            Assert.That(secondPage.CurrentUrl, Is.EqualTo(secondUrl));

            Assert.That(secondAfterSet.LocalValue, Is.EqualTo("beta"), $"Second page localStorage must keep its own value. snapshot={secondAfterSet}");
            Assert.That(secondAfterSet.SessionValue, Is.EqualTo("two"), $"Second page sessionStorage must keep its own value. snapshot={secondAfterSet}");

            Assert.That(firstAfterSecondSet.LocalValue, Is.EqualTo("alpha"), $"First page localStorage must remain isolated after second-page writes. snapshot={firstAfterSecondSet}");
            Assert.That(firstAfterSecondSet.SessionValue, Is.EqualTo("one"), $"First page sessionStorage must remain isolated after second-page writes. snapshot={firstAfterSecondSet}");
        });
    }

    [Test]
    public async Task RealBrowserSameSiteTabsKeepIndexedDbAndCacheIsolation()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);

        await AssertPageBootstrappedAsync(browser, firstPage, "Real-browser IndexedDB/Cache isolation requires a bootstrapped first page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondPage, "Real-browser IndexedDB/Cache isolation requires a bootstrapped second page.").ConfigureAwait(false);

        await firstPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-offline-storage/first"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Offline Storage A</title></head><body>first-offline-storage</body></html>",
        }).ConfigureAwait(false);
        await secondPage.NavigateAsync(new Uri("https://127.0.0.1/real-browser-offline-storage/second"), new NavigationSettings
        {
            Html = "<html><head><title>Real Browser Offline Storage B</title></head><body>second-offline-storage</body></html>",
        }).ConfigureAwait(false);

        var firstAfterSet = await CaptureIndexedDbAndCacheSurfaceSnapshotAsync(firstPage, value: "alpha").ConfigureAwait(false);
        var secondBeforeSet = await CaptureIndexedDbAndCacheSurfaceSnapshotAsync(secondPage).ConfigureAwait(false);
        var secondAfterSet = await CaptureIndexedDbAndCacheSurfaceSnapshotAsync(secondPage, value: "beta").ConfigureAwait(false);
        var firstAfterSecondSet = await CaptureIndexedDbAndCacheSurfaceSnapshotAsync(firstPage).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstPage.GetOrCreateBridgeContextId(), Is.Not.EqualTo(secondPage.GetOrCreateBridgeContextId()));
            Assert.That(firstAfterSet.ContextId, Is.Not.EqualTo(secondBeforeSet.ContextId));

            Assert.That(firstAfterSet.Error, Is.Null, $"First IndexedDB/Cache snapshot failed. snapshot={firstAfterSet}");
            Assert.That(secondBeforeSet.Error, Is.Null, $"Second pre-write IndexedDB/Cache snapshot failed. snapshot={secondBeforeSet}");
            Assert.That(secondAfterSet.Error, Is.Null, $"Second post-write IndexedDB/Cache snapshot failed. snapshot={secondAfterSet}");
            Assert.That(firstAfterSecondSet.Error, Is.Null, $"First post-second-write IndexedDB/Cache snapshot failed. snapshot={firstAfterSecondSet}");

            Assert.That(firstAfterSet.HasIndexedDb, Is.True, $"First page IndexedDB must be available. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.HasCaches, Is.True, $"First page Cache API must be available. snapshot={firstAfterSet}");
            Assert.That(secondBeforeSet.HasIndexedDb, Is.True, $"Second page IndexedDB must be available. snapshot={secondBeforeSet}");
            Assert.That(secondBeforeSet.HasCaches, Is.True, $"Second page Cache API must be available. snapshot={secondBeforeSet}");

            Assert.That(firstAfterSet.IndexedDbValue, Is.EqualTo("alpha"), $"First page IndexedDB must keep its own value. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.CacheValue, Is.EqualTo("alpha"), $"First page Cache API must keep its own value. snapshot={firstAfterSet}");
            Assert.That(firstAfterSet.CacheKeys, Does.Contain("atom-live-cache"), $"First page must expose its isolated cache namespace. snapshot={firstAfterSet}");

            Assert.That(secondBeforeSet.IndexedDbValue, Is.Null, $"Second page IndexedDB must stay isolated from first-page writes. snapshot={secondBeforeSet}");
            Assert.That(secondBeforeSet.CacheValue, Is.Null, $"Second page Cache API must stay isolated from first-page writes. snapshot={secondBeforeSet}");
            Assert.That(secondBeforeSet.CacheKeys, Does.Not.Contain("atom-live-cache"), $"Second page must not see the first page cache namespace. snapshot={secondBeforeSet}");

            Assert.That(secondAfterSet.IndexedDbValue, Is.EqualTo("beta"), $"Second page IndexedDB must keep its own value. snapshot={secondAfterSet}");
            Assert.That(secondAfterSet.CacheValue, Is.EqualTo("beta"), $"Second page Cache API must keep its own value. snapshot={secondAfterSet}");
            Assert.That(secondAfterSet.CacheKeys, Does.Contain("atom-live-cache"), $"Second page must expose its own isolated cache namespace. snapshot={secondAfterSet}");

            Assert.That(firstAfterSecondSet.IndexedDbValue, Is.EqualTo("alpha"), $"First page IndexedDB must remain isolated after second-page writes. snapshot={firstAfterSecondSet}");
            Assert.That(firstAfterSecondSet.CacheValue, Is.EqualTo("alpha"), $"First page Cache API must remain isolated after second-page writes. snapshot={firstAfterSecondSet}");
            Assert.That(firstAfterSecondSet.CacheKeys, Does.Contain("atom-live-cache"), $"First page cache namespace must remain visible only in its own context. snapshot={firstAfterSecondSet}");
        });
    }

    [Test]
    public async Task RealBrowserLoopbackServiceWorkerRegistersAndServesOfflineFallback()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserServiceWorkerLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var page = (WebPage)browser.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Real-browser service worker fallback requires a bootstrapped current page.").ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, page, server.PageUrl).ConfigureAwait(false);

        var initialSnapshot = await CaptureServiceWorkerSurfaceSnapshotAsync(page).ConfigureAwait(false);
        var registrationResult = await ExecuteMainWorldStringAsync(page, """
        (async () => {
            try {
                await navigator.serviceWorker.register('/real-browser-service-worker/sw.js');
                await navigator.serviceWorker.ready;
                return 'ok';
            } catch (error) {
                return error instanceof Error
                    ? `${error.name}: ${error.message}`
                    : String(error);
            }
            })();
        """).ConfigureAwait(false);

        for (var attempt = 0; attempt < 100 && !server.HasObservedRequest(RealBrowserServiceWorkerLoopbackServer.ServiceWorkerPath); attempt++)
            await Task.Delay(50).ConfigureAwait(false);

        var controlledPage = (WebPage)await ((WebWindow)browser.CurrentWindow).OpenPageAsync().ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, controlledPage, "Real-browser service worker fallback requires a bootstrapped controlled page.").ConfigureAwait(false);
        await NavigateToDeviceFingerprintPageAsync(browser, controlledPage, server.PageUrl).ConfigureAwait(false);

        var serviceWorkerRequestDiagnostics = server.GetLastObservedRequestHead(RealBrowserServiceWorkerLoopbackServer.ServiceWorkerPath)?.Replace("\r\n", " | ", StringComparison.Ordinal) ?? "<null>";
        var observedPathDiagnostics = string.Join(",", server.GetObservedPaths());
        var controlledSnapshot = await WaitForServiceWorkerControllerAsync(
            controlledPage,
            $"Timed out waiting for service worker-controlled loopback page to publish the offline fallback probe. registration={registrationResult}; swRequested={server.HasObservedRequest(RealBrowserServiceWorkerLoopbackServer.ServiceWorkerPath)}; observedPaths=[{observedPathDiagnostics}]; swRequest={serviceWorkerRequestDiagnostics}")
            .ConfigureAwait(false);
        var offlineFetchResult = await ExecuteMainWorldStringAsync(controlledPage, """
        (async () => {
            try {
                const response = await fetch('/real-browser-service-worker/network-data', {
                    cache: 'no-store'
                });
                const body = await response.text();
                return `${response.status}:${response.headers.get('x-atom-source') ?? '<missing>'}:${body}`;
            } catch (error) {
                return error instanceof Error
                    ? `${error.name}: ${error.message}`
                    : String(error);
            }
            })();
        """).ConfigureAwait(false);

        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await controlledPage.EvaluateAsync<string>("""
            return await navigator.serviceWorker.getRegistrations()
                .then(registrations => Promise.all(registrations.map(registration => registration.unregister())).then(() => 'cleanup'))
                .catch(() => 'cleanup');
            """, cleanupCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        var serviceWorkerRequestHead = server.GetLastObservedRequestHead(RealBrowserServiceWorkerLoopbackServer.ServiceWorkerPath);

        Assert.Multiple(() =>
        {
            Assert.That(initialSnapshot.IsSecureContext, Is.True, $"Loopback page must be a secure context for service worker registration. snapshot={initialSnapshot}");
            Assert.That(initialSnapshot.HasServiceWorkerApi, Is.True, $"Loopback page must expose navigator.serviceWorker. snapshot={initialSnapshot}");

            Assert.That(registrationResult, Is.EqualTo("ok"), "Loopback service worker registration must succeed in the real browser.");
            Assert.That(serviceWorkerRequestHead, Is.Not.Null.And.Not.Empty, "Browser must request the loopback service worker script after registration.");
            Assert.That(serviceWorkerRequestHead, Does.Contain("Service-Worker: script\r\n"), "Service worker bootstrap request must carry the Service-Worker header.");
            Assert.That(serviceWorkerRequestHead, Does.Contain("Sec-Fetch-Dest: serviceworker\r\n"), "Service worker bootstrap request must keep the serviceworker destination.");

            Assert.That(controlledSnapshot.IsSecureContext, Is.True, $"Controlled loopback page must remain a secure context. snapshot={controlledSnapshot}");
            Assert.That(controlledSnapshot.HasController, Is.True, $"Loopback page must become service-worker controlled after re-navigation. snapshot={controlledSnapshot}");
            Assert.That(controlledSnapshot.ControllerScriptUrl, Is.EqualTo(server.ServiceWorkerUrl.AbsoluteUri), $"Controlled page must point at the expected loopback service worker script. snapshot={controlledSnapshot}");

            Assert.That(offlineFetchResult, Is.EqualTo("200:service-worker-offline:offline-fallback"), $"Service worker must serve the offline fallback when the loopback network request fails. snapshot={controlledSnapshot}");
            Assert.That(server.GetObservedRequestCount(RealBrowserServiceWorkerLoopbackServer.NetworkDataPath), Is.GreaterThanOrEqualTo(1), "Service worker fallback must still attempt the loopback network request before serving the offline response.");
        });
    }

    [Test]
    public async Task RealBrowserNonCurrentPageEventsRemainScopedAcrossWindows()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, firstWindowCurrentPage, "Real-browser event scoping requires a bootstrapped first-window current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "Real-browser event scoping requires a bootstrapped second-window current page.").ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        List<InterceptedRequestEventArgs> firstWindowRequests = [];
        List<InterceptedRequestEventArgs> browserRequests = [];
        List<InterceptedRequestEventArgs> secondWindowRequests = [];
        List<InterceptedRequestEventArgs> secondWindowCurrentPageRequests = [];
        List<InterceptedResponseEventArgs> targetPageResponses = [];
        List<InterceptedResponseEventArgs> firstWindowResponses = [];
        List<InterceptedResponseEventArgs> browserResponses = [];
        List<InterceptedResponseEventArgs> secondWindowResponses = [];
        List<InterceptedResponseEventArgs> secondWindowCurrentPageResponses = [];
        List<string> targetPageLifecycle = [];
        List<string> firstWindowLifecycle = [];
        List<string> browserLifecycle = [];
        List<string> secondWindowLifecycle = [];
        List<string> secondWindowCurrentPageLifecycle = [];

        targetPage.Request += (_, args) =>
        {
            targetPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Request += (_, args) =>
        {
            firstWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Request += (_, args) =>
        {
            browserRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindow.Request += (_, args) =>
        {
            secondWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Request += (_, args) =>
        {
            secondWindowCurrentPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };

        targetPage.Response += (_, args) =>
        {
            targetPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Response += (_, args) =>
        {
            firstWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Response += (_, args) =>
        {
            browserResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindow.Response += (_, args) =>
        {
            secondWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Response += (_, args) =>
        {
            secondWindowCurrentPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        SubscribeLifecycle(targetPage, targetPageLifecycle);
        SubscribeLifecycle(firstWindow, firstWindowLifecycle);
        SubscribeLifecycle(browser, browserLifecycle);
        SubscribeLifecycle(secondWindow, secondWindowLifecycle);
        SubscribeLifecycle(secondWindowCurrentPage, secondWindowCurrentPageLifecycle);

        await targetPage.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var targetUrl = new Uri("https://127.0.0.1/real-browser-events/non-current");
        var response = await targetPage.NavigateAsync(targetUrl, new NavigationSettings
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Real-Browser-Events"] = "non-current",
            },
            Html = "<html><head><title>Real Browser Events NonCurrent</title></head><body>events</body></html>",
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(targetPage.CurrentUrl, Is.EqualTo(targetUrl));
            Assert.That(targetPage.CurrentTitle, Is.EqualTo("Real Browser Events NonCurrent"));

            Assert.That(targetPageRequests, Has.Count.EqualTo(1));
            Assert.That(firstWindowRequests, Has.Count.EqualTo(1));
            Assert.That(browserRequests, Has.Count.EqualTo(1));
            Assert.That(secondWindowRequests, Is.Empty);
            Assert.That(secondWindowCurrentPageRequests, Is.Empty);
            Assert.That(targetPageResponses, Has.Count.EqualTo(1));
            Assert.That(firstWindowResponses, Has.Count.EqualTo(1));
            Assert.That(browserResponses, Has.Count.EqualTo(1));
            Assert.That(secondWindowResponses, Is.Empty);
            Assert.That(secondWindowCurrentPageResponses, Is.Empty);

            Assert.That(targetPageRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(firstWindowRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(browserRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(targetPageResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(firstWindowResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(browserResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));

            Assert.That(targetPageLifecycle, Is.EqualTo(StandardLifecycleSequence));
            Assert.That(firstWindowLifecycle, Is.EqualTo(targetPageLifecycle));
            Assert.That(browserLifecycle, Is.EqualTo(targetPageLifecycle));
            Assert.That(secondWindowLifecycle, Is.Empty);
            Assert.That(secondWindowCurrentPageLifecycle, Is.Empty);
        });
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionRemainsScopedToEnabledPageAndPattern()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Target page interception test requires a bootstrapped first page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "Target page interception test requires a bootstrapped second-window page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);
        await secondWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, secondWindowCurrentPage, server.PageScopeOtherPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        List<InterceptedRequestEventArgs> firstWindowRequests = [];
        List<InterceptedRequestEventArgs> browserRequests = [];
        List<InterceptedRequestEventArgs> secondWindowPageRequests = [];
        List<InterceptedResponseEventArgs> targetPageResponses = [];
        List<InterceptedResponseEventArgs> firstWindowResponses = [];
        List<InterceptedResponseEventArgs> browserResponses = [];
        List<InterceptedResponseEventArgs> secondWindowPageResponses = [];

        targetPage.Request += (_, args) =>
        {
            targetPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Request += (_, args) =>
        {
            firstWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Request += (_, args) =>
        {
            browserRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Request += (_, args) =>
        {
            secondWindowPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };

        targetPage.Response += (_, args) =>
        {
            targetPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Response += (_, args) =>
        {
            firstWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Response += (_, args) =>
        {
            browserResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Response += (_, args) =>
        {
            secondWindowPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        var matchingUrl = server.CreatePageScopeFetchTargetUrl("target");
        var nonMatchingUrl = server.CreatePageScopeFetchOtherUrl("target");

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/fetch-target*"]).ConfigureAwait(false);

        try
        {
            await firstWindow.ActivateAsync().ConfigureAwait(false);
            _ = await ExecuteFetchAsync(targetPage, matchingUrl, "target-page").ConfigureAwait(false);

            await WaitForCollectionCountAsync(targetPageRequests, 1, "Timed out waiting for target page request interception event.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(targetPageResponses, 1, "Timed out waiting for target page response interception event.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(firstWindowRequests, 1, "Timed out waiting for first-window request interception event.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(firstWindowResponses, 1, "Timed out waiting for first-window response interception event.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(browserRequests, 1, "Timed out waiting for browser request interception event.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(browserResponses, 1, "Timed out waiting for browser response interception event.").ConfigureAwait(false);

            await secondWindow.ActivateAsync().ConfigureAwait(false);
            _ = await ExecuteFetchAsync(secondWindowCurrentPage, matchingUrl, "other-window").ConfigureAwait(false);
            _ = await ExecuteFetchAsync(targetPage, nonMatchingUrl, "target-page-non-matching").ConfigureAwait(false);

            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
                Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
                Assert.That(firstWindow.CurrentPage, Is.SameAs(targetPage));

                Assert.That(targetPageRequests, Has.Count.EqualTo(1));
                Assert.That(targetPageResponses, Has.Count.EqualTo(1));
                Assert.That(firstWindowRequests, Has.Count.EqualTo(1));
                Assert.That(firstWindowResponses, Has.Count.EqualTo(1));
                Assert.That(browserRequests, Has.Count.EqualTo(1));
                Assert.That(browserResponses, Has.Count.EqualTo(1));
                Assert.That(secondWindowPageRequests, Is.Empty);
                Assert.That(secondWindowPageResponses, Is.Empty);

                Assert.That(targetPageRequests[0].Request.RequestUri, Is.EqualTo(matchingUrl));
                Assert.That(firstWindowRequests[0].Request.RequestUri, Is.EqualTo(matchingUrl));
                Assert.That(browserRequests[0].Request.RequestUri, Is.EqualTo(matchingUrl));
                Assert.That(targetPageResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(matchingUrl));
                Assert.That(firstWindowResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(matchingUrl));
                Assert.That(browserResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(matchingUrl));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionAbortBlocksMatchingFetchBeforeLoopbackDelivery()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser request abort test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        List<InterceptedResponseEventArgs> targetPageResponses = [];
        var abortUrl = server.CreateDecisionFetchUrl("abort");

        targetPage.Request += async (_, args) =>
        {
            if (args.Request.RequestUri == abortUrl)
            {
                targetPageRequests.Add(args);
                await args.AbortAsync(HttpStatusCode.Gone, "blocked-live").ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        targetPage.Response += (_, args) =>
        {
            targetPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, abortUrl, "decision-abort").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, abortUrl, "Timed out waiting for the live abort interception request.").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(abortUrl), Is.False);
                Assert.That(targetPageResponses.All(response => response.Response.RequestMessage!.RequestUri != abortUrl), Is.True);
                Assert.That(result, Does.Not.Contain($"ok:{abortUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionRedirectRewritesMatchingFetchToRedirectTarget()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser request redirect test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        var sourceUrl = server.CreateDecisionFetchUrl("redirect-source");
        var redirectedUrl = server.CreateDecisionFetchUrl("redirect-target");

        targetPage.Request += async (_, args) =>
        {
            if (args.Request.RequestUri == sourceUrl)
            {
                targetPageRequests.Add(args);
                await args.RedirectAsync(redirectedUrl).ConfigureAwait(false);
                return;
            }

            if (args.Request.RequestUri == redirectedUrl)
                targetPageRequests.Add(args);

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, sourceUrl, "decision-redirect").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, sourceUrl, "Timed out waiting for the live redirect interception request.").ConfigureAwait(false);
            await WaitForLoopbackRequestAsync(server, redirectedUrl, "Timed out waiting for the redirected live request to reach the loopback server.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.HasObservedRequest(sourceUrl), Is.False);
                Assert.That(server.HasObservedRequest(redirectedUrl), Is.True);
                Assert.That(targetPageRequests.Any(request => request.Request.RequestUri == sourceUrl), Is.True);
                Assert.That(result, Is.EqualTo($"ok:{redirectedUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionFulfillReplacesFetchResponseWithoutLoopbackDelivery()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser request fulfill test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        const string fulfilledBody = "fulfilled-live-body";
        var fulfillUrl = server.CreateDecisionFetchUrl("fulfill");

        targetPage.Request += async (_, args) =>
        {
            if (args.Request.RequestUri == fulfillUrl)
            {
                targetPageRequests.Add(args);

                var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "fulfilled-live",
                    Content = new StringContent(fulfilledBody, Encoding.UTF8, "text/plain"),
                };
                fulfilled.Headers.Add("X-Intercepted", "fulfilled-live");

                await args.FulfillAsync(fulfilled).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, fulfillUrl, "decision-fulfill").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, fulfillUrl, "Timed out waiting for the live fulfill interception request.").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(fulfillUrl), Is.False);
                Assert.That(result, Is.EqualTo(fulfilledBody));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionMainFrameFulfillFailsClosedWithoutOriginRequest()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser main_frame fail-closed test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);

        const string bodyTextScript = "return document.body?.innerText?.trim() ?? document.body?.textContent?.trim() ?? '';";
        var baselineUrl = server.PageScopeTargetPageUrl;
        await NavigateToRealBrowserPageAsync(browser, targetPage, baselineUrl).ConfigureAwait(false);

        var baselineSnapshotUrl = targetPage.CurrentUrl;
        var baselineSnapshotTitle = targetPage.CurrentTitle;
        var baselineLiveUrl = await targetPage.GetUrlAsync().ConfigureAwait(false);
        var baselineBodyText = await targetPage.EvaluateAsync<string>(bodyTextScript).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        var blockedUrl = server.PageScopeOtherPageUrl;

        targetPage.Request += async (_, args) =>
        {
            if (args.IsNavigate && args.Request.RequestUri == blockedUrl)
            {
                targetPageRequests.Add(args);

                var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "main-frame-fulfilled-live",
                    Content = new StringContent("<html><head><title>Main Frame Fulfilled</title></head><body><main id='fulfilled-marker'>fulfilled</main></body></html>", Encoding.UTF8, "text/html"),
                };
                fulfilled.Headers.Add("X-Intercepted", "main-frame-fulfilled-live");

                await args.FulfillAsync(fulfilled).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/page-scope/other*"]).ConfigureAwait(false);

        try
        {
            using var navCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await targetPage.BridgeCommands!.NavigateAsync(blockedUrl, navCts.Token).ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, blockedUrl, "Timed out waiting for the live main_frame fulfill interception request.").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            var liveUrlAfterBlockedNavigate = await targetPage.GetUrlAsync().ConfigureAwait(false);
            var liveBodyTextAfterBlockedNavigate = await targetPage.EvaluateAsync<string>(bodyTextScript).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(blockedUrl), Is.False);
                Assert.That(baselineSnapshotUrl, Is.EqualTo(baselineUrl));
                Assert.That(baselineLiveUrl, Is.EqualTo(baselineUrl.AbsoluteUri));
                Assert.That(targetPage.CurrentUrl, Is.EqualTo(baselineSnapshotUrl));
                Assert.That(targetPage.CurrentTitle, Is.EqualTo(baselineSnapshotTitle));
                Assert.That(liveUrlAfterBlockedNavigate, Is.EqualTo(baselineLiveUrl));
                Assert.That(liveBodyTextAfterBlockedNavigate, Is.EqualTo(baselineBodyText));
                Assert.That(targetPage.IsDisposed, Is.False);
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserDeviceClientHintsReachLoopbackRequestHeaders()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
            Logger = new ConsoleLogger(nameof(WebDriverRealBrowserIntegrationTests)),
        }).ConfigureAwait(false);
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request-header observation");

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser Client Hints request-header test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        var probeUrl = server.CreateHeaderEchoUrl("Sec-CH-UA-Platform-Version", "client-hints-loopback");

        var echoedHeader = await ExecuteFetchAsync(targetPage, probeUrl, "client-hints-loopback").ConfigureAwait(false);

        await WaitForLoopbackRequestAsync(server, probeUrl, "Timed out waiting for the live Client Hints request to reach the loopback server.").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(server.HasObservedRequest(probeUrl), Is.True);
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA"), Is.EqualTo("\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Full-Version-List"), Is.EqualTo("\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Platform"), Is.EqualTo("\"Android\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Platform-Version"), Is.EqualTo("\"14.0.0\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Mobile"), Is.EqualTo("?1"));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Arch"), Is.EqualTo("\"arm\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Model"), Is.EqualTo("\"Pixel 7\""));
            Assert.That(server.GetObservedRequestHeader(probeUrl, "Sec-CH-UA-Bitness"), Is.EqualTo("\"64\""));
            Assert.That(echoedHeader, Is.EqualTo("\"14.0.0\""));
        });
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionContinueCanAddRequestHeadersBeforeLoopbackDelivery()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser request continue mutation test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        const string headerName = "X-Atom-Intercepted";
        const string headerValue = "live-continue";
        var continueUrl = server.CreateHeaderEchoUrl(headerName, "continue-headers");

        targetPage.Request += async (_, args) =>
        {
            if (args.Request.RequestUri == continueUrl)
            {
                targetPageRequests.Add(args);

                var replacement = new HttpsRequestMessage(args.Request.Method, args.Request.RequestUri);
                replacement.Headers.Add(headerName, headerValue);
                await args.ContinueAsync(replacement).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/header-echo/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, continueUrl, "decision-continue").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, continueUrl, "Timed out waiting for the live continue interception request.").ConfigureAwait(false);
            await WaitForLoopbackRequestAsync(server, continueUrl, "Timed out waiting for the live continue-mutated request to reach the loopback server.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(continueUrl), Is.True);
                Assert.That(server.GetObservedRequestHeader(continueUrl, headerName), Is.EqualTo(headerValue));
                Assert.That(result, Is.EqualTo(headerValue));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterRequestInterceptionScope.Window, TestName = "RealBrowser window.Request AbortAsync blocks matching fetch before loopback delivery")]
    [TestCase(OuterRequestInterceptionScope.Browser, TestName = "RealBrowser browser.Request AbortAsync blocks matching fetch before loopback delivery")]
    public async Task RealBrowserOuterScopeRequestInterceptionAbortBlocksMatchingFetchBeforeLoopbackDelivery(OuterRequestInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope request abort test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> interceptedRequests = [];
        List<InterceptedResponseEventArgs> interceptedResponses = [];
        var abortUrl = server.CreateDecisionFetchUrl($"outer-request-abort-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedRequestHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Request.RequestUri == abortUrl)
            {
                interceptedRequests.Add(args);
                await args.AbortAsync(HttpStatusCode.Gone, "outer-request-blocked-live").ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        using var responseSubscription = SubscribeScopedResponseHandler(browser, firstWindow, scope is OuterRequestInterceptionScope.Window ? OuterResponseInterceptionScope.Window : OuterResponseInterceptionScope.Browser, args =>
        {
            interceptedResponses.Add(args);
            return ValueTask.CompletedTask;
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, abortUrl, "outer-request-abort").ConfigureAwait(false);

            await WaitForRequestUriAsync(interceptedRequests, abortUrl, "Timed out waiting for the outer-scope live abort interception request.").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(abortUrl), Is.False);
                Assert.That(interceptedResponses.All(response => response.Response.RequestMessage!.RequestUri != abortUrl), Is.True);
                Assert.That(result, Does.Not.Contain($"ok:{abortUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterRequestInterceptionScope.Window, TestName = "RealBrowser window.Request RedirectAsync rewrites matching fetch to redirect target")]
    [TestCase(OuterRequestInterceptionScope.Browser, TestName = "RealBrowser browser.Request RedirectAsync rewrites matching fetch to redirect target")]
    public async Task RealBrowserOuterScopeRequestInterceptionRedirectRewritesMatchingFetchToRedirectTarget(OuterRequestInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope request redirect test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> interceptedRequests = [];
        var sourceUrl = server.CreateDecisionFetchUrl($"outer-request-redirect-source-{scope.ToString().ToLowerInvariant()}");
        var redirectedUrl = server.CreateDecisionFetchUrl($"outer-request-redirect-target-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedRequestHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Request.RequestUri == sourceUrl)
            {
                interceptedRequests.Add(args);
                await args.RedirectAsync(redirectedUrl).ConfigureAwait(false);
                return;
            }

            if (args.Request.RequestUri == redirectedUrl)
                interceptedRequests.Add(args);

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, sourceUrl, "outer-request-redirect").ConfigureAwait(false);

            await WaitForRequestUriAsync(interceptedRequests, sourceUrl, "Timed out waiting for the outer-scope live redirect interception request.").ConfigureAwait(false);
            await WaitForLoopbackRequestAsync(server, redirectedUrl, "Timed out waiting for the outer-scope redirected request to reach the loopback server.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.HasObservedRequest(sourceUrl), Is.False);
                Assert.That(server.HasObservedRequest(redirectedUrl), Is.True);
                Assert.That(interceptedRequests.Any(request => request.Request.RequestUri == sourceUrl), Is.True);
                Assert.That(result, Is.EqualTo($"ok:{redirectedUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterRequestInterceptionScope.Window, TestName = "RealBrowser window.Request FulfillAsync replaces fetch response without loopback delivery")]
    [TestCase(OuterRequestInterceptionScope.Browser, TestName = "RealBrowser browser.Request FulfillAsync replaces fetch response without loopback delivery")]
    public async Task RealBrowserOuterScopeRequestInterceptionFulfillReplacesFetchResponseWithoutLoopbackDelivery(OuterRequestInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope request fulfill test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> interceptedRequests = [];
        var fulfilledBody = $"outer-request-fulfilled-{scope.ToString().ToLowerInvariant()}";
        var fulfillUrl = server.CreateDecisionFetchUrl($"outer-request-fulfill-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedRequestHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Request.RequestUri == fulfillUrl)
            {
                interceptedRequests.Add(args);

                var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "outer-request-fulfilled-live",
                    Content = new StringContent(fulfilledBody, Encoding.UTF8, "text/plain"),
                };
                fulfilled.Headers.Add("X-Intercepted", $"outer-request-{scope.ToString().ToLowerInvariant()}");

                await args.FulfillAsync(fulfilled).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, fulfillUrl, "outer-request-fulfill").ConfigureAwait(false);

            await WaitForRequestUriAsync(interceptedRequests, fulfillUrl, "Timed out waiting for the outer-scope live fulfill interception request.").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(fulfillUrl), Is.False);
                Assert.That(result, Is.EqualTo(fulfilledBody));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterRequestInterceptionScope.Window, TestName = "RealBrowser window.Request ContinueAsync can add request headers before loopback delivery")]
    [TestCase(OuterRequestInterceptionScope.Browser, TestName = "RealBrowser browser.Request ContinueAsync can add request headers before loopback delivery")]
    public async Task RealBrowserOuterScopeRequestInterceptionContinueCanAddRequestHeadersBeforeLoopbackDelivery(OuterRequestInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope request continue mutation test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> interceptedRequests = [];
        const string headerName = "X-Atom-Outer-Intercepted";
        var headerValue = $"live-{scope.ToString().ToLowerInvariant()}-continue";
        var continueUrl = server.CreateHeaderEchoUrl(headerName, $"outer-request-continue-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedRequestHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Request.RequestUri == continueUrl)
            {
                interceptedRequests.Add(args);

                var replacement = new HttpsRequestMessage(args.Request.Method, args.Request.RequestUri);
                replacement.Headers.Add(headerName, headerValue);
                await args.ContinueAsync(replacement).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/header-echo/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, continueUrl, "outer-request-continue").ConfigureAwait(false);

            await WaitForRequestUriAsync(interceptedRequests, continueUrl, "Timed out waiting for the outer-scope live continue interception request.").ConfigureAwait(false);
            await WaitForLoopbackRequestAsync(server, continueUrl, "Timed out waiting for the outer-scope continue-mutated request to reach the loopback server.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedRequests, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(continueUrl), Is.True);
                Assert.That(server.GetObservedRequestHeader(continueUrl, headerName), Is.EqualTo(headerValue));
                Assert.That(result, Is.EqualTo(headerValue));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageResponseInterceptionAbortBlocksDeliveredFetchResponse()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser response abort test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> targetPageResponses = [];
        var abortUrl = server.CreateDecisionFetchUrl("response-abort");

        targetPage.Response += async (_, args) =>
        {
            if (args.Response.RequestMessage?.RequestUri == abortUrl)
            {
                targetPageResponses.Add(args);
                await args.AbortAsync(HttpStatusCode.GatewayTimeout, "response-blocked-live").ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, abortUrl, "response-abort").ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, abortUrl, "Timed out waiting for the upstream response-abort request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, abortUrl, "Timed out waiting for the live response-abort interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(abortUrl), Is.True);
                Assert.That(result, Does.Not.Contain($"ok:{abortUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageResponseInterceptionFulfillReplacesDeliveredFetchResponse()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser response fulfill test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> targetPageResponses = [];
        const string fulfilledBody = "response-fulfilled-live";
        var fulfillUrl = server.CreateDecisionFetchUrl("response-fulfill");

        targetPage.Response += async (_, args) =>
        {
            if (args.Response.RequestMessage?.RequestUri == fulfillUrl)
            {
                targetPageResponses.Add(args);

                var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "response-fulfilled-live",
                    Content = new StringContent(fulfilledBody, Encoding.UTF8, "text/plain"),
                };
                fulfilled.Headers.TryAddWithoutValidation("X-Intercepted-Response", "fulfilled-live");

                await args.FulfillAsync(fulfilled).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, fulfillUrl, "response-fulfill").ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, fulfillUrl, "Timed out waiting for the upstream response-fulfill request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, fulfillUrl, "Timed out waiting for the live response-fulfill interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(fulfillUrl), Is.True);
                Assert.That(result, Is.EqualTo(fulfilledBody));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageResponseInterceptionContinueCanAddResponseHeadersBeforeDelivery()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser response continue mutation test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> targetPageResponses = [];
        const string headerName = "X-Atom-Response-Intercepted";
        const string headerValue = "live-response-continue";
        var continueUrl = server.CreateDecisionFetchUrl("response-continue-headers");

        targetPage.Response += async (_, args) =>
        {
            if (args.Response.RequestMessage?.RequestUri == continueUrl)
            {
                targetPageResponses.Add(args);
                args.Response.Headers.Remove(headerName);
                args.Response.Headers.Remove("Access-Control-Expose-Headers");
                args.Response.Headers.TryAddWithoutValidation(headerName, headerValue);
                args.Response.Headers.TryAddWithoutValidation("Access-Control-Expose-Headers", headerName);
                await args.ContinueAsync().ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchHeaderAsync(targetPage, continueUrl, headerName).ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, continueUrl, "Timed out waiting for the upstream response-continue request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, continueUrl, "Timed out waiting for the live response-continue interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targetPageResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(continueUrl), Is.True);
                Assert.That(result, Is.EqualTo(headerValue));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterResponseInterceptionScope.Window, TestName = "RealBrowser window.Response AbortAsync blocks delivered fetch response")]
    [TestCase(OuterResponseInterceptionScope.Browser, TestName = "RealBrowser browser.Response AbortAsync blocks delivered fetch response")]
    public async Task RealBrowserOuterScopeResponseInterceptionAbortBlocksDeliveredFetchResponse(OuterResponseInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope response abort test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> interceptedResponses = [];
        var abortUrl = server.CreateDecisionFetchUrl($"response-abort-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedResponseHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Response.RequestMessage?.RequestUri == abortUrl)
            {
                interceptedResponses.Add(args);
                await args.AbortAsync(HttpStatusCode.GatewayTimeout, "outer-response-blocked-live").ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, abortUrl, "outer-response-abort").ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, abortUrl, "Timed out waiting for the outer-scope response-abort request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(interceptedResponses, abortUrl, "Timed out waiting for the outer-scope response-abort interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(abortUrl), Is.True);
                Assert.That(result, Does.Not.Contain($"ok:{abortUrl.PathAndQuery}"));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterResponseInterceptionScope.Window, TestName = "RealBrowser window.Response FulfillAsync replaces delivered fetch response")]
    [TestCase(OuterResponseInterceptionScope.Browser, TestName = "RealBrowser browser.Response FulfillAsync replaces delivered fetch response")]
    public async Task RealBrowserOuterScopeResponseInterceptionFulfillReplacesDeliveredFetchResponse(OuterResponseInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope response fulfill test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> interceptedResponses = [];
        var fulfillUrl = server.CreateDecisionFetchUrl($"response-fulfill-{scope.ToString().ToLowerInvariant()}");
        var fulfilledBody = $"outer-response-fulfilled-{scope.ToString().ToLowerInvariant()}";
        using var subscription = SubscribeScopedResponseHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Response.RequestMessage?.RequestUri == fulfillUrl)
            {
                interceptedResponses.Add(args);

                var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
                {
                    ReasonPhrase = "outer-response-fulfilled-live",
                    Content = new StringContent(fulfilledBody, Encoding.UTF8, "text/plain"),
                };
                fulfilled.Headers.TryAddWithoutValidation("X-Intercepted-Response", scope.ToString().ToLowerInvariant());

                await args.FulfillAsync(fulfilled).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchAsync(targetPage, fulfillUrl, "outer-response-fulfill").ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, fulfillUrl, "Timed out waiting for the outer-scope response-fulfill request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(interceptedResponses, fulfillUrl, "Timed out waiting for the outer-scope response-fulfill interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(fulfillUrl), Is.True);
                Assert.That(result, Is.EqualTo(fulfilledBody));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [TestCase(OuterResponseInterceptionScope.Window, TestName = "RealBrowser window.Response ContinueAsync can add response headers before delivery")]
    [TestCase(OuterResponseInterceptionScope.Browser, TestName = "RealBrowser browser.Response ContinueAsync can add response headers before delivery")]
    public async Task RealBrowserOuterScopeResponseInterceptionContinueCanAddResponseHeadersBeforeDelivery(OuterResponseInterceptionScope scope)
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, targetPage, "Real-browser outer-scope response continue mutation test requires a bootstrapped target page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);

        List<InterceptedResponseEventArgs> interceptedResponses = [];
        const string headerName = "X-Atom-Outer-Response-Intercepted";
        var headerValue = $"live-{scope.ToString().ToLowerInvariant()}-response-continue";
        var continueUrl = server.CreateDecisionFetchUrl($"response-continue-{scope.ToString().ToLowerInvariant()}");
        using var subscription = SubscribeScopedResponseHandler(browser, firstWindow, scope, async args =>
        {
            if (args.Response.RequestMessage?.RequestUri == continueUrl)
            {
                interceptedResponses.Add(args);
                args.Response.Headers.Remove(headerName);
                args.Response.Headers.Remove("Access-Control-Expose-Headers");
                args.Response.Headers.TryAddWithoutValidation(headerName, headerValue);
                args.Response.Headers.TryAddWithoutValidation("Access-Control-Expose-Headers", headerName);
                await args.ContinueAsync().ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        });

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/decision/*"]).ConfigureAwait(false);

        try
        {
            var result = await ExecuteFetchHeaderAsync(targetPage, continueUrl, headerName).ConfigureAwait(false);

            await WaitForLoopbackRequestAsync(server, continueUrl, "Timed out waiting for the outer-scope response-continue request to reach the loopback server.").ConfigureAwait(false);
            await WaitForResponseUriAsync(interceptedResponses, continueUrl, "Timed out waiting for the outer-scope response-continue interception event.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(interceptedResponses, Has.Count.EqualTo(1));
                Assert.That(server.HasObservedRequest(continueUrl), Is.True);
                Assert.That(result, Is.EqualTo(headerValue));
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserPageRequestInterceptionCanUpdatePatternsDuringNavigationAndReloadWithoutLeakingToSiblingPage()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var targetPage = (WebPage)firstWindow.CurrentPage;
        var siblingPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);

        await AssertPageBootstrappedAsync(browser, targetPage, "Page interception lifecycle test requires a bootstrapped target page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, siblingPage, "Page interception lifecycle test requires a bootstrapped sibling page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeTargetPageUrl).ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, siblingPage, server.PageScopeOtherPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> targetPageRequests = [];
        List<InterceptedResponseEventArgs> targetPageResponses = [];
        List<InterceptedRequestEventArgs> siblingPageRequests = [];
        List<InterceptedResponseEventArgs> siblingPageResponses = [];
        List<InterceptedRequestEventArgs> firstWindowRequests = [];
        List<InterceptedResponseEventArgs> firstWindowResponses = [];

        targetPage.Request += (_, args) =>
        {
            targetPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        targetPage.Response += (_, args) =>
        {
            targetPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        siblingPage.Request += (_, args) =>
        {
            siblingPageRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        siblingPage.Response += (_, args) =>
        {
            siblingPageResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Request += (_, args) =>
        {
            firstWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Response += (_, args) =>
        {
            firstWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        var alphaUrl = server.CreatePageScopeFetchTargetUrl("page-alpha-initial");
        var alphaAfterUpdateUrl = server.CreatePageScopeFetchTargetUrl("page-alpha-after-update");
        var betaUrl = server.CreatePageScopeFetchOtherUrl("page-beta-after-update");
        var betaSiblingUrl = server.CreatePageScopeFetchOtherUrl("page-beta-sibling");
        var betaAfterReloadUrl = server.CreatePageScopeFetchOtherUrl("page-beta-after-reload");
        var betaAfterDisableUrl = server.CreatePageScopeFetchOtherUrl("page-beta-after-disable");

        await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/fetch-target*"]).ConfigureAwait(false);

        try
        {
            _ = await ExecuteFetchAsync(targetPage, alphaUrl, "page-alpha-initial").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, alphaUrl, "Timed out waiting for initial page-level alpha interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, alphaUrl, "Timed out waiting for initial page-level alpha interception response.").ConfigureAwait(false);
            await WaitForRequestUriAsync(firstWindowRequests, alphaUrl, "Timed out waiting for initial window-bubbled alpha interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(firstWindowResponses, alphaUrl, "Timed out waiting for initial window-bubbled alpha interception response.").ConfigureAwait(false);

            _ = await ExecuteFetchAsync(siblingPage, alphaUrl, "page-alpha-sibling").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            await targetPage.SetRequestInterceptionAsync(true, ["*real-browser-interception/fetch-other*"]).ConfigureAwait(false);

            _ = await ExecuteFetchAsync(targetPage, alphaAfterUpdateUrl, "page-alpha-after-update").ConfigureAwait(false);
            _ = await ExecuteFetchAsync(targetPage, betaUrl, "page-beta-after-update").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, betaUrl, "Timed out waiting for updated page-level beta interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, betaUrl, "Timed out waiting for updated page-level beta interception response.").ConfigureAwait(false);
            await WaitForRequestUriAsync(firstWindowRequests, betaUrl, "Timed out waiting for updated window-bubbled beta interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(firstWindowResponses, betaUrl, "Timed out waiting for updated window-bubbled beta interception response.").ConfigureAwait(false);

            _ = await ExecuteFetchAsync(siblingPage, betaSiblingUrl, "page-beta-sibling").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            await NavigateToRealBrowserPageAsync(browser, targetPage, server.PageScopeOtherPageUrl).ConfigureAwait(false);
            await ReloadRealBrowserPageAsync(browser, targetPage).ConfigureAwait(false);

            _ = await ExecuteFetchAsync(targetPage, betaAfterReloadUrl, "page-beta-after-reload").ConfigureAwait(false);

            await WaitForRequestUriAsync(targetPageRequests, betaAfterReloadUrl, "Timed out waiting for page-level interception after navigation and reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(targetPageResponses, betaAfterReloadUrl, "Timed out waiting for page-level response after navigation and reload.").ConfigureAwait(false);
            await WaitForRequestUriAsync(firstWindowRequests, betaAfterReloadUrl, "Timed out waiting for window-bubbled interception after navigation and reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(firstWindowResponses, betaAfterReloadUrl, "Timed out waiting for window-bubbled response after navigation and reload.").ConfigureAwait(false);

            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);

            _ = await ExecuteFetchAsync(targetPage, betaAfterDisableUrl, "page-beta-after-disable").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstWindow.CurrentPage, Is.SameAs(siblingPage));

                Assert.That(targetPageRequests.Any(request => request.Request.RequestUri == alphaUrl), Is.True);
                Assert.That(targetPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == alphaUrl), Is.True);
                Assert.That(firstWindowRequests.Any(request => request.Request.RequestUri == alphaUrl), Is.True);
                Assert.That(firstWindowResponses.Any(response => response.Response.RequestMessage!.RequestUri == alphaUrl), Is.True);

                Assert.That(targetPageRequests.Any(request => request.Request.RequestUri == betaUrl), Is.True);
                Assert.That(targetPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaUrl), Is.True);
                Assert.That(firstWindowRequests.Any(request => request.Request.RequestUri == betaUrl), Is.True);
                Assert.That(firstWindowResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaUrl), Is.True);

                Assert.That(targetPageRequests.Any(request => request.Request.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(targetPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(firstWindowRequests.Any(request => request.Request.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(firstWindowResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaAfterReloadUrl), Is.True);

                Assert.That(targetPageRequests.All(request => request.Request.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(targetPageResponses.All(response => response.Response.RequestMessage!.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(firstWindowRequests.All(request => request.Request.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(firstWindowResponses.All(response => response.Response.RequestMessage!.RequestUri != alphaAfterUpdateUrl), Is.True);

                Assert.That(targetPageRequests.All(request => request.Request.RequestUri != betaAfterDisableUrl), Is.True);
                Assert.That(targetPageResponses.All(response => response.Response.RequestMessage!.RequestUri != betaAfterDisableUrl), Is.True);
                Assert.That(firstWindowRequests.All(request => request.Request.RequestUri != betaAfterDisableUrl), Is.True);
                Assert.That(firstWindowResponses.All(response => response.Response.RequestMessage!.RequestUri != betaAfterDisableUrl), Is.True);

                Assert.That(siblingPageRequests.All(request => request.Request.RequestUri != alphaUrl), Is.True);
                Assert.That(siblingPageResponses.All(response => response.Response.RequestMessage!.RequestUri != alphaUrl), Is.True);
                Assert.That(siblingPageRequests.All(request => request.Request.RequestUri != betaSiblingUrl), Is.True);
                Assert.That(siblingPageResponses.All(response => response.Response.RequestMessage!.RequestUri != betaSiblingUrl), Is.True);
            });
        }
        finally
        {
            await targetPage.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserWindowRequestInterceptionFansOutAcrossWindowPagesWithoutCrossWindowLeak()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowNonCurrentPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, firstWindowNonCurrentPage, "Window interception fan-out test requires a bootstrapped non-current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, firstWindowCurrentPage, "Window interception fan-out test requires a bootstrapped current page.").ConfigureAwait(false);
        await AssertPageBootstrappedAsync(browser, secondWindowCurrentPage, "Window interception fan-out test requires a bootstrapped second-window page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, firstWindowNonCurrentPage, server.WindowFanoutNonCurrentPageUrl).ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, firstWindowCurrentPage, server.WindowFanoutCurrentPageUrl).ConfigureAwait(false);
        await secondWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, secondWindowCurrentPage, server.WindowFanoutOtherWindowPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> nonCurrentRequests = [];
        List<InterceptedRequestEventArgs> currentRequests = [];
        List<InterceptedRequestEventArgs> firstWindowRequests = [];
        List<InterceptedRequestEventArgs> browserRequests = [];
        List<InterceptedRequestEventArgs> secondWindowRequests = [];
        List<InterceptedResponseEventArgs> nonCurrentResponses = [];
        List<InterceptedResponseEventArgs> currentResponses = [];
        List<InterceptedResponseEventArgs> firstWindowResponses = [];
        List<InterceptedResponseEventArgs> browserResponses = [];
        List<InterceptedResponseEventArgs> secondWindowResponses = [];

        firstWindowNonCurrentPage.Request += (_, args) =>
        {
            nonCurrentRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindowCurrentPage.Request += (_, args) =>
        {
            currentRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Request += (_, args) =>
        {
            firstWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Request += (_, args) =>
        {
            browserRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Request += (_, args) =>
        {
            secondWindowRequests.Add(args);
            return ValueTask.CompletedTask;
        };

        firstWindowNonCurrentPage.Response += (_, args) =>
        {
            nonCurrentResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindowCurrentPage.Response += (_, args) =>
        {
            currentResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Response += (_, args) =>
        {
            firstWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        browser.Response += (_, args) =>
        {
            browserResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        secondWindowCurrentPage.Response += (_, args) =>
        {
            secondWindowResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        var nonCurrentUrl = server.CreateWindowFanoutFetchUrl("non-current");
        var currentUrl = server.CreateWindowFanoutFetchUrl("current");
        var otherWindowUrl = server.CreateWindowFanoutFetchUrl("other-window");

        await firstWindow.SetRequestInterceptionAsync(true, ["*real-browser-interception/window-fanout/fetch/*"]).ConfigureAwait(false);

        try
        {
            await firstWindow.ActivateAsync().ConfigureAwait(false);
            _ = await ExecuteFetchAsync(firstWindowNonCurrentPage, nonCurrentUrl, "non-current-page").ConfigureAwait(false);
            _ = await ExecuteFetchAsync(firstWindowCurrentPage, currentUrl, "current-page").ConfigureAwait(false);

            await WaitForCollectionCountAsync(nonCurrentRequests, 1, "Timed out waiting for non-current page interception request.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(nonCurrentResponses, 1, "Timed out waiting for non-current page interception response.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(currentRequests, 1, "Timed out waiting for current page interception request.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(currentResponses, 1, "Timed out waiting for current page interception response.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(firstWindowRequests, 2, "Timed out waiting for window fan-out request events.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(firstWindowResponses, 2, "Timed out waiting for window fan-out response events.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(browserRequests, 2, "Timed out waiting for browser fan-out request events.").ConfigureAwait(false);
            await WaitForCollectionCountAsync(browserResponses, 2, "Timed out waiting for browser fan-out response events.").ConfigureAwait(false);

            await secondWindow.ActivateAsync().ConfigureAwait(false);
            _ = await ExecuteFetchAsync(secondWindowCurrentPage, otherWindowUrl, "other-window-page").ConfigureAwait(false);

            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
                Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
                Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));

                Assert.That(nonCurrentRequests, Has.Count.EqualTo(1));
                Assert.That(nonCurrentResponses, Has.Count.EqualTo(1));
                Assert.That(currentRequests, Has.Count.EqualTo(1));
                Assert.That(currentResponses, Has.Count.EqualTo(1));
                Assert.That(firstWindowRequests, Has.Count.EqualTo(2));
                Assert.That(firstWindowResponses, Has.Count.EqualTo(2));
                Assert.That(browserRequests, Has.Count.EqualTo(2));
                Assert.That(browserResponses, Has.Count.EqualTo(2));
                Assert.That(secondWindowRequests, Is.Empty);
                Assert.That(secondWindowResponses, Is.Empty);

                Assert.That(nonCurrentRequests[0].Request.RequestUri, Is.EqualTo(nonCurrentUrl));
                Assert.That(currentRequests[0].Request.RequestUri, Is.EqualTo(currentUrl));
                Assert.That(nonCurrentResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(nonCurrentUrl));
                Assert.That(currentResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(currentUrl));
            });
        }
        finally
        {
            await firstWindow.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserBrowserRequestInterceptionPersistsForNewPageAcrossNavigationAndReloadUntilDisabled()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var initialPage = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, initialPage, "Browser interception persistence test requires a bootstrapped initial page.").ConfigureAwait(false);

        List<InterceptedRequestEventArgs> openedPageRequests = [];
        List<InterceptedResponseEventArgs> openedPageResponses = [];
        List<InterceptedRequestEventArgs> browserRequests = [];
        List<InterceptedResponseEventArgs> browserResponses = [];

        await browser.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        try
        {
            var openedPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
            await AssertPageBootstrappedAsync(browser, openedPage, "Browser interception should flow into pages opened after enablement.").ConfigureAwait(false);

            openedPage.Request += (_, args) =>
            {
                openedPageRequests.Add(args);
                return ValueTask.CompletedTask;
            };
            openedPage.Response += (_, args) =>
            {
                openedPageResponses.Add(args);
                return ValueTask.CompletedTask;
            };
            browser.Request += (_, args) =>
            {
                browserRequests.Add(args);
                return ValueTask.CompletedTask;
            };
            browser.Response += (_, args) =>
            {
                browserResponses.Add(args);
                return ValueTask.CompletedTask;
            };

            var firstMatchingUrl = server.CreatePageScopeFetchTargetUrl("browser-opened-page-initial");
            await firstWindow.ActivateAsync().ConfigureAwait(false);
            _ = await ExecuteFetchAsync(openedPage, firstMatchingUrl, "browser-opened-page-initial").ConfigureAwait(false);

            await WaitForRequestUriAsync(openedPageRequests, firstMatchingUrl, "Timed out waiting for the first inherited browser-level interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(openedPageResponses, firstMatchingUrl, "Timed out waiting for the first inherited browser-level interception response.").ConfigureAwait(false);
            await WaitForRequestUriAsync(browserRequests, firstMatchingUrl, "Timed out waiting for the browser-level request after opening a new page.").ConfigureAwait(false);
            await WaitForResponseUriAsync(browserResponses, firstMatchingUrl, "Timed out waiting for the browser-level response after opening a new page.").ConfigureAwait(false);

            await NavigateToRealBrowserPageAsync(browser, openedPage, server.PageScopeOtherPageUrl).ConfigureAwait(false);
            await ReloadRealBrowserPageAsync(browser, openedPage).ConfigureAwait(false);

            var secondMatchingUrl = server.CreatePageScopeFetchTargetUrl("browser-opened-page-after-reload");
            _ = await ExecuteFetchAsync(openedPage, secondMatchingUrl, "browser-opened-page-after-reload").ConfigureAwait(false);

            await WaitForRequestUriAsync(openedPageRequests, secondMatchingUrl, "Timed out waiting for browser-level interception after navigation and reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(openedPageResponses, secondMatchingUrl, "Timed out waiting for browser-level response after navigation and reload.").ConfigureAwait(false);
            await WaitForRequestUriAsync(browserRequests, secondMatchingUrl, "Timed out waiting for the second browser-level request after navigation and reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(browserResponses, secondMatchingUrl, "Timed out waiting for the second browser-level response after navigation and reload.").ConfigureAwait(false);

            await browser.SetRequestInterceptionAsync(false).ConfigureAwait(false);

            var thirdMatchingUrl = server.CreatePageScopeFetchTargetUrl("browser-opened-page-disabled");
            _ = await ExecuteFetchAsync(openedPage, thirdMatchingUrl, "browser-opened-page-disabled").ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstWindow.CurrentPage, Is.SameAs(openedPage));
                Assert.That(openedPageRequests.Any(request => request.Request.RequestUri == firstMatchingUrl), Is.True);
                Assert.That(openedPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == firstMatchingUrl), Is.True);
                Assert.That(browserRequests.Any(request => request.Request.RequestUri == firstMatchingUrl), Is.True);
                Assert.That(browserResponses.Any(response => response.Response.RequestMessage!.RequestUri == firstMatchingUrl), Is.True);
                Assert.That(openedPageRequests.Any(request => request.Request.RequestUri == secondMatchingUrl), Is.True);
                Assert.That(openedPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == secondMatchingUrl), Is.True);
                Assert.That(browserRequests.Any(request => request.Request.RequestUri == secondMatchingUrl), Is.True);
                Assert.That(browserResponses.Any(response => response.Response.RequestMessage!.RequestUri == secondMatchingUrl), Is.True);
                Assert.That(openedPageRequests.Any(request => request.Request.RequestUri == thirdMatchingUrl), Is.False);
                Assert.That(openedPageResponses.Any(response => response.Response.RequestMessage!.RequestUri == thirdMatchingUrl), Is.False);
                Assert.That(browserRequests.Any(request => request.Request.RequestUri == thirdMatchingUrl), Is.False);
                Assert.That(browserResponses.Any(response => response.Response.RequestMessage!.RequestUri == thirdMatchingUrl), Is.False);
            });
        }
        finally
        {
            await browser.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RealBrowserWindowRequestInterceptionCanUpdatePatternsDuringPageLifetimeAndSurviveReload()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        using var server = new RealBrowserInterceptionLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var browser = await WebDriverTestEnvironment.LaunchAsync();
        IgnoreIfChromiumMv3BlockingWebRequestUnsupported(browser, "Live request/response interception");
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)firstWindow.CurrentPage;

        await AssertPageBootstrappedAsync(browser, page, "Window interception update test requires a bootstrapped current page.").ConfigureAwait(false);

        await firstWindow.ActivateAsync().ConfigureAwait(false);
        await NavigateToRealBrowserPageAsync(browser, page, server.WindowFanoutCurrentPageUrl).ConfigureAwait(false);

        List<InterceptedRequestEventArgs> pageRequests = [];
        List<InterceptedResponseEventArgs> pageResponses = [];
        List<InterceptedRequestEventArgs> windowRequests = [];
        List<InterceptedResponseEventArgs> windowResponses = [];

        page.Request += (_, args) =>
        {
            pageRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        page.Response += (_, args) =>
        {
            pageResponses.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Request += (_, args) =>
        {
            windowRequests.Add(args);
            return ValueTask.CompletedTask;
        };
        firstWindow.Response += (_, args) =>
        {
            windowResponses.Add(args);
            return ValueTask.CompletedTask;
        };

        var alphaUrl = server.CreateWindowFanoutFetchUrl("alpha");
        var alphaAfterUpdateUrl = server.CreateWindowFanoutFetchUrl("alpha-after-update");
        var betaUrl = server.CreateWindowFanoutFetchUrl("beta");
        var betaAfterReloadUrl = server.CreateWindowFanoutFetchUrl("beta-after-reload");

        await firstWindow.SetRequestInterceptionAsync(true, ["*real-browser-interception/window-fanout/fetch/alpha*"]).ConfigureAwait(false);

        try
        {
            _ = await ExecuteFetchAsync(page, alphaUrl, "window-alpha-initial").ConfigureAwait(false);

            await WaitForRequestUriAsync(pageRequests, alphaUrl, "Timed out waiting for initial alpha interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(pageResponses, alphaUrl, "Timed out waiting for initial alpha interception response.").ConfigureAwait(false);
            await WaitForRequestUriAsync(windowRequests, alphaUrl, "Timed out waiting for initial window alpha interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(windowResponses, alphaUrl, "Timed out waiting for initial window alpha interception response.").ConfigureAwait(false);

            await firstWindow.SetRequestInterceptionAsync(true, ["*real-browser-interception/window-fanout/fetch/beta*"]).ConfigureAwait(false);

            _ = await ExecuteFetchAsync(page, alphaAfterUpdateUrl, "window-alpha-after-update").ConfigureAwait(false);
            _ = await ExecuteFetchAsync(page, betaUrl, "window-beta-after-update").ConfigureAwait(false);

            await WaitForRequestUriAsync(pageRequests, betaUrl, "Timed out waiting for updated beta interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(pageResponses, betaUrl, "Timed out waiting for updated beta interception response.").ConfigureAwait(false);
            await WaitForRequestUriAsync(windowRequests, betaUrl, "Timed out waiting for updated window beta interception request.").ConfigureAwait(false);
            await WaitForResponseUriAsync(windowResponses, betaUrl, "Timed out waiting for updated window beta interception response.").ConfigureAwait(false);

            await ReloadRealBrowserPageAsync(browser, page).ConfigureAwait(false);

            _ = await ExecuteFetchAsync(page, betaAfterReloadUrl, "window-beta-after-reload").ConfigureAwait(false);

            await WaitForRequestUriAsync(pageRequests, betaAfterReloadUrl, "Timed out waiting for beta interception after reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(pageResponses, betaAfterReloadUrl, "Timed out waiting for beta response after reload.").ConfigureAwait(false);
            await WaitForRequestUriAsync(windowRequests, betaAfterReloadUrl, "Timed out waiting for window beta interception after reload.").ConfigureAwait(false);
            await WaitForResponseUriAsync(windowResponses, betaAfterReloadUrl, "Timed out waiting for window beta response after reload.").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pageRequests.Any(request => request.Request.RequestUri == alphaUrl), Is.True);
                Assert.That(pageResponses.Any(response => response.Response.RequestMessage!.RequestUri == alphaUrl), Is.True);
                Assert.That(windowRequests.Any(request => request.Request.RequestUri == alphaUrl), Is.True);
                Assert.That(windowResponses.Any(response => response.Response.RequestMessage!.RequestUri == alphaUrl), Is.True);
                Assert.That(pageRequests.Any(request => request.Request.RequestUri == betaUrl), Is.True);
                Assert.That(pageResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaUrl), Is.True);
                Assert.That(windowRequests.Any(request => request.Request.RequestUri == betaUrl), Is.True);
                Assert.That(windowResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaUrl), Is.True);
                Assert.That(pageRequests.Any(request => request.Request.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(pageResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(windowRequests.Any(request => request.Request.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(windowResponses.Any(response => response.Response.RequestMessage!.RequestUri == betaAfterReloadUrl), Is.True);
                Assert.That(pageRequests.All(request => request.Request.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(pageResponses.All(response => response.Response.RequestMessage!.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(windowRequests.All(request => request.Request.RequestUri != alphaAfterUpdateUrl), Is.True);
                Assert.That(windowResponses.All(response => response.Response.RequestMessage!.RequestUri != alphaAfterUpdateUrl), Is.True);
            });
        }
        finally
        {
            await firstWindow.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DisposeAsyncCleansTemporaryRealBrowserProfile()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser integration test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        var browser = await WebDriverTestEnvironment.LaunchAsync();
        var process = WebDriverTestEnvironment.GetLaunchedBrowserProcess(browser);
        var launchSettings = WebDriverTestEnvironment.GetLaunchSettings(browser);
        var profile = launchSettings.Profile;

        Assert.That(profile, Is.Not.Null);
        Assert.That(process, Is.Not.Null);

        var profilePath = profile!.Path;
        var processId = process!.Id;

        await browser.DisposeAsync().ConfigureAwait(false);

        await WaitForConditionAsync(id => !IsProcessAlive(id), processId, BrowserShutdownTimeout).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Path, Is.Empty);
            Assert.That(Directory.Exists(profilePath), Is.False);
            Assert.That(IsProcessAlive(processId), Is.False);
        });
    }

    private static async Task WaitForConditionAsync<TState>(Func<TState, bool> predicate, TState state, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!predicate(state))
        {
            if (stopwatch.Elapsed >= timeout)
                Assert.Fail($"Condition was not satisfied within {timeout}.");

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private static Task DelayForHeadfulObservationAsync()
        => Task.CompletedTask;

    private static async Task WaitForDocumentCookieAsync(WebBrowser browser, WebPage page, string expectedCookie)
        => await WaitForDocumentCookieAsync(
            browser,
            page,
            cookie => cookie.Contains(expectedCookie, StringComparison.Ordinal),
            $"Timed out waiting for document.cookie to contain '{expectedCookie}'").ConfigureAwait(false);

    private static async Task WaitForDocumentCookieAsync(WebBrowser browser, WebPage page, Func<string, bool> predicate, string failureMessage)
    {
        var deadline = DateTime.UtcNow + CookieSyncTimeout;
        var observedCookie = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            observedCookie = (await page.EvaluateAsync("document.cookie").ConfigureAwait(false))?.GetString() ?? string.Empty;
            if (predicate(observedCookie))
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        var diagnostics = await DescribeDocumentCookieFailureAsync(browser, page, observedCookie).ConfigureAwait(false);
        Assert.Fail($"{failureMessage} | {diagnostics}");
    }

    private static async Task<string> DescribeDocumentCookieFailureAsync(WebBrowser browser, WebPage page, string observedCookie)
    {
        var bootstrapDiagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);

        try
        {
            var probeJson = await page.EvaluateAsync<string>("""
            return JSON.stringify((() => {
                const describeDescriptor = (target) => {
                    try {
                        const descriptor = target ? Object.getOwnPropertyDescriptor(target, 'cookie') : null;
                        if (!descriptor) {
                            return null;
                        }

                        return {
                            configurable: descriptor.configurable === true,
                            enumerable: descriptor.enumerable === true,
                            hasGetter: typeof descriptor.get === 'function',
                            hasSetter: typeof descriptor.set === 'function',
                        };
                    } catch (error) {
                        return {
                            error: error instanceof Error
                                ? `${error.name}: ${error.message}`
                                : String(error),
                        };
                    }
                };

                const snapshot = {
                    locationHref: null,
                    readyState: null,
                    visibilityState: null,
                    hasFocus: null,
                    contextId: globalThis.__atomTabContext?.contextId ?? null,
                    syncFunctionType: typeof globalThis.__atomSyncDocumentCookieHeader,
                    cookieValue: null,
                    cookieReadError: null,
                    documentCookieDescriptor: describeDescriptor(document),
                    htmlDocumentCookieDescriptor: describeDescriptor(globalThis.HTMLDocument?.prototype),
                    documentPrototypeCookieDescriptor: describeDescriptor(globalThis.Document?.prototype),
                };

                try {
                    snapshot.locationHref = String(location.href);
                } catch (error) {
                    snapshot.locationHref = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                }

                try {
                    snapshot.readyState = String(document.readyState);
                } catch (error) {
                    snapshot.readyState = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                }

                try {
                    snapshot.visibilityState = String(document.visibilityState);
                } catch (error) {
                    snapshot.visibilityState = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                }

                try {
                    snapshot.hasFocus = String(document.hasFocus());
                } catch (error) {
                    snapshot.hasFocus = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                }

                try {
                    snapshot.cookieValue = String(document.cookie);
                } catch (error) {
                    snapshot.cookieReadError = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                }

                return snapshot;
            })());
            """).ConfigureAwait(false) ?? "<null>";
            var liveCookies = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
            var liveCookieDiagnostics = liveCookies.Length == 0
                ? "<empty>"
                : string.Join(
                    ";",
                    liveCookies.Select(static cookie => $"{cookie.Name}={cookie.Value};domain={cookie.Domain};path={cookie.Path}"));

            return string.Concat(
                bootstrapDiagnostics,
                ", observedDocumentCookie=",
                observedCookie,
                ", cookieProbe=",
                probeJson,
                ", directCookies=[",
                liveCookieDiagnostics,
                "]");
        }
        catch (Exception ex)
        {
            return string.Concat(
                bootstrapDiagnostics,
                ", observedDocumentCookie=",
                observedCookie,
                ", cookieProbeError=",
                ex.GetType().Name,
                ":",
                ex.Message);
        }
    }

    private static async Task WaitForCollectionCountAsync<T>(IReadOnlyCollection<T> collection, int expectedCount, string failureMessage)
    {
        var deadline = DateTime.UtcNow + CookieSyncTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (collection.Count >= expectedCount)
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task WaitForRequestUriAsync(IReadOnlyCollection<InterceptedRequestEventArgs> collection, Uri expectedUri, string failureMessage)
    {
        var deadline = DateTime.UtcNow + CookieSyncTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (collection.Any(request => request.Request.RequestUri == expectedUri))
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task WaitForResponseUriAsync(IReadOnlyCollection<InterceptedResponseEventArgs> collection, Uri expectedUri, string failureMessage)
    {
        var deadline = DateTime.UtcNow + CookieSyncTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (collection.Any(response => response.Response.RequestMessage!.RequestUri == expectedUri))
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task WaitForLoopbackRequestAsync(RealBrowserInterceptionLoopbackServer server, Uri url, string failureMessage)
    {
        var deadline = DateTime.UtcNow + CookieSyncTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (server.HasObservedRequest(url))
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task<string?> ExecuteFetchAsync(WebPage page, Uri url, string marker)
        => await page.EvaluateAsync<string>($$"""
        return await fetch({{ToJavaScriptStringLiteral(url.AbsoluteUri)}}, {
            cache: 'no-store'
        }).then(response => response.text()).catch(error => String(error));
        """).ConfigureAwait(false);

    private static async Task<string?> ExecuteFetchHeaderAsync(WebPage page, Uri url, string headerName)
        => await page.EvaluateAsync<string>($$"""
        return await fetch({{ToJavaScriptStringLiteral(url.AbsoluteUri)}}, {
            cache: 'no-store'
        }).then(response => response.headers.get({{ToJavaScriptStringLiteral(headerName)}}) ?? 'not-found').catch(error => String(error));
        """).ConfigureAwait(false);

    private static string ToJavaScriptStringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return string.Concat(
            '"',
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal),
            '"');
    }

    private static bool IsPngPayload(byte[] payload)
        => payload.Length >= 8
            && payload[0] == 0x89
            && payload[1] == 0x50
            && payload[2] == 0x4E
            && payload[3] == 0x47
            && payload[4] == 0x0D
            && payload[5] == 0x0A
            && payload[6] == 0x1A
            && payload[7] == 0x0A;

    private static Size ReadPngSize(byte[] payload)
    {
        if (!IsPngPayload(payload) || payload.Length < 24)
            return Size.Empty;

        var width = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(20, 4));
        return width > 0 && height > 0 ? new Size(width, height) : Size.Empty;
    }

    private static async Task AssertPageBootstrappedAsync(WebBrowser browser, WebPage page, string message)
    {
        const string readyStateScript = "return document.readyState;";
        var deadline = DateTime.UtcNow + BridgeBootstrapTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (page.BridgeCommands is { } bridge)
            {
                try
                {
                    var status = await bridge.GetDebugPortStatusAsync().ConfigureAwait(false);
                    if (status.HasPort && status.HasSocket)
                    {
                        var readyState = await page.EvaluateAsync<string>(readyStateScript).ConfigureAwait(false);
                        if (string.Equals(readyState, "interactive", StringComparison.Ordinal)
                            || string.Equals(readyState, "complete", StringComparison.Ordinal))
                        {
                            return;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        var diagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
        Assert.Fail($"{message} {diagnostics}");
    }

    private static async Task NavigateToLookupTargetAsync(WebPage page, LookupTarget target)
        => await page.NavigateAsync(target.Url, new NavigationSettings
        {
            Html = CreateLookupHtml(target.Title, target.MarkerId, target.MarkerValue),
        }).ConfigureAwait(false);

    private static string CreateLookupHtml(string title, string markerId, string markerValue)
        => $"""
        <html>
          <head>
            <title>{title}</title>
          </head>
          <body>
            <main id=\"{markerId}\" class=\"scope-marker\" data-page=\"{markerValue}\">{markerValue}</main>
            <section class=\"scope-marker\">{title}</section>
          </body>
        </html>
        """;

    private static async Task NavigateToDeviceFingerprintPageAsync(WebBrowser browser, WebPage page, Uri url)
        => await NavigateToRealBrowserPageAsync(browser, page, url).ConfigureAwait(false);

    private static async Task NavigateToRealBrowserPageAsync(WebBrowser browser, WebPage page, Uri url)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(url);

        const string readyStateScript = "return document.readyState;";
        var previousLiveUrl = page.CurrentUrl;

        if (previousLiveUrl is null)
        {
            try
            {
                previousLiveUrl = await page.GetUrlAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }

        Uri? lastObservedLiveUrl = previousLiveUrl;

        for (var navigationAttempt = 0; navigationAttempt < 2; navigationAttempt++)
        {
            var stableLiveUrlAttempts = 0;

            await page.BridgeCommands!.NavigateAsync(url).ConfigureAwait(false);

            for (var attempt = 0; attempt < 200; attempt++)
            {
                try
                {
                    var status = await page.BridgeCommands.GetDebugPortStatusAsync().ConfigureAwait(false);
                    if (status.HasPort && status.HasSocket)
                    {
                        var readyState = await page.EvaluateAsync<string>(readyStateScript).ConfigureAwait(false);
                        var liveUrl = await page.GetUrlAsync().ConfigureAwait(false);
                        lastObservedLiveUrl = liveUrl;

                        if ((string.Equals(readyState, "interactive", StringComparison.Ordinal)
                            || string.Equals(readyState, "complete", StringComparison.Ordinal))
                            && liveUrl == url)
                        {
                            stableLiveUrlAttempts++;
                            if (page.CurrentUrl == url || stableLiveUrlAttempts >= 3)
                            {
                                return;
                            }
                        }
                        else
                        {
                            stableLiveUrlAttempts = 0;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    stableLiveUrlAttempts = 0;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            var observedLiveUrl = lastObservedLiveUrl ?? page.CurrentUrl;
            if (navigationAttempt == 0
                && (observedLiveUrl is null || observedLiveUrl == previousLiveUrl))
            {
                continue;
            }

            previousLiveUrl = observedLiveUrl ?? previousLiveUrl;
            break;
        }

        var diagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
        Assert.Fail(
            $"Timed out waiting for bridge port rebootstrap after real navigation to {url.AbsoluteUri}. previousLiveUrl={previousLiveUrl?.AbsoluteUri ?? "<null>"}, lastObservedLiveUrl={lastObservedLiveUrl?.AbsoluteUri ?? "<null>"}. {diagnostics}");
    }

    private static async Task ReloadRealBrowserPageAsync(WebBrowser browser, WebPage page)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(page);

        const string readyStateScript = "return document.readyState;";
        const string timeOriginScript = "return String(globalThis.performance?.timeOrigin ?? '');";
        var stableLiveUrlAttempts = 0;

        var expectedUrl = await page.GetUrlAsync().ConfigureAwait(false) ?? page.CurrentUrl;
        if (expectedUrl is null)
            throw new InvalidOperationException("Real browser reload requires a materialized page URL before reload.");

        var previousTimeOrigin = await page.EvaluateAsync<string>(timeOriginScript).ConfigureAwait(false);
        await page.BridgeCommands!.ReloadAsync().ConfigureAwait(false);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                var status = await page.BridgeCommands.GetDebugPortStatusAsync().ConfigureAwait(false);
                if (status.HasPort && status.HasSocket)
                {
                    var readyState = await page.EvaluateAsync<string>(readyStateScript).ConfigureAwait(false);
                    var currentTimeOrigin = await page.EvaluateAsync<string>(timeOriginScript).ConfigureAwait(false);
                    var liveUrl = await page.GetUrlAsync().ConfigureAwait(false);
                    if ((string.Equals(readyState, "interactive", StringComparison.Ordinal)
                        || string.Equals(readyState, "complete", StringComparison.Ordinal))
                        && liveUrl == expectedUrl
                        && !string.IsNullOrWhiteSpace(currentTimeOrigin)
                        && !string.Equals(currentTimeOrigin, previousTimeOrigin, StringComparison.Ordinal))
                    {
                        stableLiveUrlAttempts++;
                        if (page.CurrentUrl == expectedUrl || stableLiveUrlAttempts >= 3)
                        {
                            return;
                        }
                    }
                    else
                    {
                        stableLiveUrlAttempts = 0;
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        var diagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
        Assert.Fail($"Timed out waiting for bridge port rebootstrap after real reload of {expectedUrl.AbsoluteUri}. {diagnostics}");
    }

    private static async Task<JsonDocument> CaptureDeviceFingerprintSnapshotAsync(WebPage page)
    {
        const string snapshotScript = """
            return JSON.stringify((() => {
                const targetWindow = window;
                const pageContext = targetWindow.__atomTabContext ?? null;
                const contentContext = globalThis.__atomContentRuntimeContext ?? null;
                return {
                    pageHasContext: pageContext !== null,
                    pageContextId: pageContext?.contextId ?? null,
                    pageContextUserAgent: pageContext?.userAgent ?? null,
                    contentHasContext: contentContext !== null,
                    contentContextId: contentContext?.contextId ?? null,
                    contentContextUserAgent: contentContext?.userAgent ?? null,
                    userAgent: targetWindow.navigator.userAgent ?? null,
                    platform: targetWindow.navigator.platform ?? null,
                    language: targetWindow.navigator.language ?? null,
                    languages: Array.from(targetWindow.navigator.languages ?? []),
                    timezone: new targetWindow.Intl.DateTimeFormat().resolvedOptions().timeZone ?? null,
                    hardwareConcurrency: targetWindow.navigator.hardwareConcurrency ?? null,
                    deviceMemory: targetWindow.navigator.deviceMemory ?? null,
                    maxTouchPoints: targetWindow.navigator.maxTouchPoints ?? null,
                    devicePixelRatio: targetWindow.devicePixelRatio ?? null,
                    innerWidth: targetWindow.innerWidth ?? null,
                    innerHeight: targetWindow.innerHeight ?? null,
                };
            })());
        """;

        string? snapshotJson = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                snapshotJson = await page.EvaluateAsync<string>(snapshotScript).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(snapshotJson))
                {
                    using var snapshot = JsonDocument.Parse(snapshotJson);
                    var root = snapshot.RootElement;
                    if ((root.TryGetProperty("pageHasContext", out var pageHasContext) && pageHasContext.ValueKind == JsonValueKind.True)
                        || (root.TryGetProperty("contentHasContext", out var contentHasContext) && contentHasContext.ValueKind == JsonValueKind.True))
                    {
                        return JsonDocument.Parse(snapshotJson);
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            const string diagnosticsScript = """
                return JSON.stringify((() => {
                    const targetWindow = window;
                    const pageContext = targetWindow.__atomTabContext ?? null;
                    const contentContext = globalThis.__atomContentRuntimeContext ?? null;
                    return {
                        pageHasContext: pageContext !== null,
                        pageContextId: pageContext?.contextId ?? null,
                        pageContextUserAgent: pageContext?.userAgent ?? null,
                        contentHasContext: contentContext !== null,
                        contentContextId: contentContext?.contextId ?? null,
                        contentContextUserAgent: contentContext?.userAgent ?? null,
                        missingSnapshot: true,
                    };
                })());
            """;

            snapshotJson = (await page.EvaluateAsync<string>(diagnosticsScript).ConfigureAwait(false)) ?? "{}";
        }

        return JsonDocument.Parse(snapshotJson);
    }

    private static async Task<GeolocationSurfaceSnapshot> CaptureGeolocationSurfaceSnapshotAsync(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        const string snapshotScript = """
            return await new Promise(resolve => {
                const snapshot = {
                    latitude: null,
                    longitude: null,
                    accuracy: null,
                    error: null,
                };

                const finish = () => resolve(JSON.stringify(snapshot));

                try {
                    const timeoutHandle = setTimeout(() => {
                        snapshot.error = 'timeout';
                        finish();
                    }, 1500);

                    navigator.geolocation.getCurrentPosition(
                        position => {
                            clearTimeout(timeoutHandle);
                            snapshot.latitude = position?.coords?.latitude ?? null;
                            snapshot.longitude = position?.coords?.longitude ?? null;
                            snapshot.accuracy = position?.coords?.accuracy ?? null;
                            finish();
                        },
                        error => {
                            clearTimeout(timeoutHandle);
                            snapshot.error = `${error?.code ?? -1}:${error?.message ?? 'error'}`;
                            finish();
                        },
                        {
                            enableHighAccuracy: true,
                            maximumAge: 0,
                            timeout: 1000,
                        });
                } catch (error) {
                    snapshot.error = error instanceof Error
                        ? `${error.name}: ${error.message}`
                        : String(error);
                    finish();
                }
            });
        """;

        string? snapshotJson = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                snapshotJson = await page.EvaluateAsync<string>(snapshotScript).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(snapshotJson))
                {
                    using var document = JsonDocument.Parse(snapshotJson);
                    var root = document.RootElement;
                    var latitude = ReadOptionalDoubleProperty(root, "latitude");
                    var longitude = ReadOptionalDoubleProperty(root, "longitude");
                    var accuracy = ReadOptionalDoubleProperty(root, "accuracy");
                    var error = ReadOptionalStringProperty(root, "error");
                    if (latitude.HasValue || longitude.HasValue || !string.IsNullOrWhiteSpace(error))
                        return new GeolocationSurfaceSnapshot(latitude, longitude, accuracy, error);
                }
            }
            catch (InvalidOperationException)
            {
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live geolocation snapshot must produce a JSON payload.");

        using var fallbackDocument = JsonDocument.Parse(snapshotJson!);
        var fallbackRoot = fallbackDocument.RootElement;
        return new GeolocationSurfaceSnapshot(
            ReadOptionalDoubleProperty(fallbackRoot, "latitude"),
            ReadOptionalDoubleProperty(fallbackRoot, "longitude"),
            ReadOptionalDoubleProperty(fallbackRoot, "accuracy"),
            ReadOptionalStringProperty(fallbackRoot, "error"));
    }

    private static async Task<PrivacySignalSnapshot> CapturePrivacySignalSnapshotAsync(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        const string snapshotScript = """
            return JSON.stringify({
                doNotTrack: navigator.doNotTrack ?? null,
                globalPrivacyControl: typeof navigator.globalPrivacyControl === 'boolean'
                    ? navigator.globalPrivacyControl
                    : null,
            });
        """;

        var snapshotJson = await page.EvaluateAsync<string>(snapshotScript).ConfigureAwait(false);
        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live privacy signal snapshot must produce a JSON payload.");

        using var document = JsonDocument.Parse(snapshotJson!);
        var root = document.RootElement;
        return new PrivacySignalSnapshot(
            ReadOptionalStringProperty(root, "doNotTrack"),
            ReadOptionalBooleanProperty(root, "globalPrivacyControl"));
    }

    private static async Task<MediaDevicesSurfaceSnapshot> CaptureMediaDevicesSurfaceSnapshotAsync(WebPage page, bool requestVideo)
    {
        ArgumentNullException.ThrowIfNull(page);

        var requestVideoLiteral = requestVideo ? "true" : "false";
        var snapshotScript = $$"""
            return JSON.stringify(await (async () => {
                const shouldRequestVideo = {{requestVideoLiteral}};
                const pageContext = window.__atomTabContext ?? null;
                const contentContext = globalThis.__atomContentRuntimeContext ?? null;
                const virtualMediaContext = pageContext?.virtualMediaDevices ?? contentContext?.virtualMediaDevices ?? null;
                const snapshot = {
                    isSecureContext: window.isSecureContext === true,
                    hasMediaDevicesApi: typeof navigator.mediaDevices !== 'undefined' && navigator.mediaDevices !== null,
                    hasVirtualMediaContext: virtualMediaContext !== null,
                    enumerateError: null,
                    devices: [],
                    videoRequest: null,
                };

                const describeError = (error) => error instanceof Error
                    ? { name: error.name, message: error.message }
                    : { name: 'Error', message: String(error) };
                const createTimeoutError = () => typeof DOMException === 'function'
                    ? new DOMException('Timed out waiting for getUserMedia result.', 'TimeoutError')
                    : new Error('Timed out waiting for getUserMedia result.');
                const getUserMediaWithTimeout = async (constraints, timeoutMs) => {
                    let timeoutHandle = null;

                    try {
                        return await Promise.race([
                            navigator.mediaDevices.getUserMedia(constraints),
                            new Promise((_, reject) => {
                                timeoutHandle = setTimeout(() => reject(createTimeoutError()), timeoutMs);
                            }),
                        ]);
                    } finally {
                        if (timeoutHandle !== null) {
                            clearTimeout(timeoutHandle);
                        }
                    }
                };

                if (!snapshot.hasMediaDevicesApi) {
                    return snapshot;
                }

                try {
                    if (typeof navigator.mediaDevices.enumerateDevices === 'function') {
                        const devices = await navigator.mediaDevices.enumerateDevices();
                        snapshot.devices = Array.from(devices ?? []).map(device => ({
                            kind: device?.kind ?? null,
                            label: device?.label ?? null,
                            deviceId: device?.deviceId ?? null,
                            groupId: device?.groupId ?? null,
                        }));
                    }
                } catch (error) {
                    const describedError = describeError(error);
                    snapshot.enumerateError = `${describedError.name}: ${describedError.message}`;
                }

                if (!shouldRequestVideo || typeof navigator.mediaDevices.getUserMedia !== 'function') {
                    return snapshot;
                }

                const hasAliasedAudioInput = snapshot.devices.some(device => device?.kind === 'audioinput'
                    && typeof device?.label === 'string'
                    && device.label.length > 0
                    && typeof device?.deviceId === 'string'
                    && device.deviceId.length > 0);
                const hasAliasedVideoInput = snapshot.devices.some(device => device?.kind === 'videoinput'
                    && typeof device?.label === 'string'
                    && device.label.length > 0
                    && typeof device?.deviceId === 'string'
                    && device.deviceId.length > 0);

                if (!snapshot.hasVirtualMediaContext || !hasAliasedAudioInput || !hasAliasedVideoInput) {
                    return snapshot;
                }

                const requestedVideoDeviceId = snapshot.devices.find(device => device?.kind === 'videoinput')?.deviceId ?? null;

                try {
                    const stream = await getUserMediaWithTimeout({
                        video: requestedVideoDeviceId
                            ? {
                                deviceId: { exact: requestedVideoDeviceId },
                                width: { exact: 320 },
                                height: { exact: 180 },
                                frameRate: { ideal: 12 },
                            }
                            : {
                                width: { exact: 320 },
                                height: { exact: 180 },
                                frameRate: { ideal: 12 },
                            },
                    }, 1500);

                    const tracks = typeof stream.getVideoTracks === 'function'
                        ? stream.getVideoTracks()
                        : [];

                    snapshot.videoRequest = {
                        ok: true,
                        requestedDeviceId: requestedVideoDeviceId,
                        active: stream.active === true,
                        trackCount: tracks.length,
                        tracks: Array.from(tracks).map(track => ({
                            kind: track?.kind ?? null,
                            readyState: track?.readyState ?? null,
                            enabled: track?.enabled === true,
                            muted: track?.muted === true,
                            label: track?.label ?? null,
                        })),
                    };

                    if (typeof stream.getTracks === 'function') {
                        stream.getTracks().forEach(track => {
                            try {
                                track.stop();
                            } catch {
                            }
                        });
                    }
                } catch (error) {
                    const describedError = describeError(error);
                    snapshot.videoRequest = {
                        ok: false,
                        requestedDeviceId: requestedVideoDeviceId,
                        active: false,
                        trackCount: 0,
                        tracks: [],
                        errorName: describedError.name,
                        errorMessage: describedError.message,
                    };
                }

                return snapshot;
            })());
        """;

        string? snapshotJson = null;
        var stableMediaAttempts = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                snapshotJson = await page.EvaluateAsync<string>(snapshotScript).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(snapshotJson))
                {
                    using var attemptDocument = JsonDocument.Parse(snapshotJson);
                    var attemptRoot = attemptDocument.RootElement;

                    if (!requestVideo)
                        break;

                    var hasVirtualMediaContext = ReadBooleanProperty(attemptRoot, "hasVirtualMediaContext");
                    var devices = ReadMediaDeviceEntries(attemptRoot);
                    var videoRequestSnapshot = ReadMediaVideoRequest(attemptRoot);
                    var hasAliasedAudioInput = devices.Any(device => string.Equals(device.Kind, "audioinput", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(device.Label)
                        && !string.IsNullOrWhiteSpace(device.DeviceId));
                    var hasAliasedVideoInput = devices.Any(device => string.Equals(device.Kind, "videoinput", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(device.Label)
                        && !string.IsNullOrWhiteSpace(device.DeviceId));
                    var videoReady = videoRequestSnapshot is { Ok: true, Active: true, TrackCount: >= 1 };
                    var virtualMediaStateReady = hasVirtualMediaContext && hasAliasedAudioInput && hasAliasedVideoInput;

                    if (virtualMediaStateReady && videoReady)
                        break;

                    if (virtualMediaStateReady)
                    {
                        stableMediaAttempts++;

                        if (videoRequestSnapshot is not null || stableMediaAttempts >= 10)
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live mediaDevices snapshot must produce a JSON payload.");

        using var document = JsonDocument.Parse(snapshotJson!);
        var root = document.RootElement;
        return new MediaDevicesSurfaceSnapshot(
            ReadBooleanProperty(root, "isSecureContext"),
            root.TryGetProperty("hasMediaDevicesApi", out var hasMediaDevicesApi)
                && hasMediaDevicesApi.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ReadOptionalStringProperty(root, "enumerateError"),
            ReadMediaDeviceEntries(root),
            ReadMediaVideoRequest(root));
    }

    private static void AssertDeviceFingerprintSnapshot(JsonElement snapshot, Device device)
        => AssertFingerprintMatches(snapshot, device, "page");

    private static async Task<StorageSurfaceSnapshot> CaptureStorageSurfaceSnapshotAsync(WebPage page, string? localValue = null, string? sessionValue = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var localValueLiteral = localValue is null ? "null" : ToJavaScriptStringLiteral(localValue);
        var sessionValueLiteral = sessionValue is null ? "null" : ToJavaScriptStringLiteral(sessionValue);
        var snapshotJson = await page.EvaluateAsync<string>($$"""
        return JSON.stringify((() => {
            const requestedLocalValue = {{localValueLiteral}};
            const requestedSessionValue = {{sessionValueLiteral}};
            const snapshot = {
                contextId: globalThis.__atomTabContext?.contextId ?? null,
                hasLocalStorage: typeof localStorage !== 'undefined',
                hasSessionStorage: typeof sessionStorage !== 'undefined',
                localValue: null,
                sessionValue: null,
                localLength: -1,
                sessionLength: -1,
                error: null,
            };

            try {
                if (snapshot.hasLocalStorage) {
                    if (requestedLocalValue !== null) {
                        localStorage.setItem('atom-live-storage-key', requestedLocalValue);
                    }

                    snapshot.localValue = localStorage.getItem('atom-live-storage-key');
                    snapshot.localLength = localStorage.length;
                }

                if (snapshot.hasSessionStorage) {
                    if (requestedSessionValue !== null) {
                        sessionStorage.setItem('atom-live-storage-key', requestedSessionValue);
                    }

                    snapshot.sessionValue = sessionStorage.getItem('atom-live-storage-key');
                    snapshot.sessionLength = sessionStorage.length;
                }
            } catch (error) {
                snapshot.error = error instanceof Error
                    ? `${error.name}: ${error.message}`
                    : String(error);
            }

            return snapshot;
        })());
        """).ConfigureAwait(false);

        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live storage snapshot must produce a JSON payload.");

        using var document = JsonDocument.Parse(snapshotJson!);
        var root = document.RootElement;
        return new StorageSurfaceSnapshot(
            ReadOptionalStringProperty(root, "contextId"),
            ReadBooleanProperty(root, "hasLocalStorage"),
            ReadBooleanProperty(root, "hasSessionStorage"),
            ReadOptionalStringProperty(root, "localValue"),
            ReadOptionalStringProperty(root, "sessionValue"),
            ReadInt32Property(root, "localLength"),
            ReadInt32Property(root, "sessionLength"),
            ReadOptionalStringProperty(root, "error"));
    }

    private static async Task<IndexedDbAndCacheSurfaceSnapshot> CaptureIndexedDbAndCacheSurfaceSnapshotAsync(WebPage page, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var valueLiteral = value is null ? "null" : ToJavaScriptStringLiteral(value);
        var snapshotJson = await page.EvaluateAsync<string>($$"""
        return JSON.stringify(await (async () => {
            const requestedValue = {{valueLiteral}};
            const snapshot = {
                contextId: globalThis.__atomTabContext?.contextId ?? null,
                hasIndexedDb: typeof indexedDB !== 'undefined' && indexedDB !== null,
                hasCaches: typeof caches !== 'undefined' && caches !== null,
                indexedDbValue: null,
                cacheValue: null,
                cacheKeys: [],
                error: null,
            };

            try {
                const publicDatabaseName = 'atom-live-db';
                const publicStoreName = 'entries';
                const publicCacheName = 'atom-live-cache';
                const publicCacheRequestUrl = location.origin + '/atom-live-cache-entry';

                if (snapshot.hasIndexedDb) {
                    const database = await new Promise((resolve, reject) => {
                        const request = indexedDB.open(publicDatabaseName, 1);
                        request.onupgradeneeded = () => {
                            const upgradedDatabase = request.result;
                            if (!upgradedDatabase.objectStoreNames.contains(publicStoreName)) {
                                upgradedDatabase.createObjectStore(publicStoreName);
                            }
                        };
                        request.onsuccess = () => resolve(request.result);
                        request.onerror = () => reject(request.error ?? new Error('IndexedDB open failed.'));
                    });

                    try {
                        if (requestedValue !== null) {
                            await new Promise((resolve, reject) => {
                                const transaction = database.transaction(publicStoreName, 'readwrite');
                                transaction.objectStore(publicStoreName).put(requestedValue, 'atom-live-entry');
                                transaction.oncomplete = () => resolve(null);
                                transaction.onerror = () => reject(transaction.error ?? new Error('IndexedDB write failed.'));
                                transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB write aborted.'));
                            });
                        }

                        snapshot.indexedDbValue = await new Promise((resolve, reject) => {
                            const transaction = database.transaction(publicStoreName, 'readonly');
                            const request = transaction.objectStore(publicStoreName).get('atom-live-entry');
                            request.onsuccess = () => resolve(request.result ?? null);
                            request.onerror = () => reject(request.error ?? new Error('IndexedDB read failed.'));
                        });
                    } finally {
                        database.close();
                    }
                }

                if (snapshot.hasCaches) {
                    if (requestedValue !== null) {
                        const writableCache = await caches.open(publicCacheName);
                        await writableCache.put(publicCacheRequestUrl, new Response(requestedValue, {
                            headers: { 'content-type': 'text/plain' },
                        }));
                    }

                    snapshot.cacheKeys = Array.from(await caches.keys());
                    if (await caches.has(publicCacheName)) {
                        const readableCache = await caches.open(publicCacheName);
                        const response = await readableCache.match(publicCacheRequestUrl);
                        snapshot.cacheValue = response ? await response.text() : null;
                    }
                }
            } catch (error) {
                snapshot.error = error instanceof Error
                    ? `${error.name}: ${error.message}`
                    : String(error);
            }

            return snapshot;
        })());
        """).ConfigureAwait(false);

        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live IndexedDB/Cache snapshot must produce a JSON payload.");

        using var document = JsonDocument.Parse(snapshotJson!);
        var root = document.RootElement;
        return new IndexedDbAndCacheSurfaceSnapshot(
            ReadOptionalStringProperty(root, "contextId"),
            ReadBooleanProperty(root, "hasIndexedDb"),
            ReadBooleanProperty(root, "hasCaches"),
            ReadOptionalStringProperty(root, "indexedDbValue"),
            ReadOptionalStringProperty(root, "cacheValue"),
            ReadStringArrayProperty(root, "cacheKeys"),
            ReadOptionalStringProperty(root, "error"));
    }

    private static async Task<ServiceWorkerSurfaceSnapshot> CaptureServiceWorkerSurfaceSnapshotAsync(WebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var snapshotJson = await ExecuteMainWorldStringAsync(page, """
        JSON.stringify((() => {
            const snapshot = {
                isSecureContext: window.isSecureContext === true,
                hasServiceWorkerApi: typeof navigator.serviceWorker !== 'undefined',
                hasController: false,
                controllerScriptUrl: null,
                controllerState: null,
                error: null,
            };

            try {
                const controller = navigator.serviceWorker?.controller ?? null;
                snapshot.hasController = controller !== null;
                snapshot.controllerScriptUrl = controller?.scriptURL ?? null;
                snapshot.controllerState = controller?.state ?? null;
            } catch (error) {
                snapshot.error = error instanceof Error
                    ? `${error.name}: ${error.message}`
                    : String(error);
            }

            return snapshot;
        })());
        """).ConfigureAwait(false);

        Assert.That(snapshotJson, Is.Not.Null.And.Not.Empty, "Live service worker snapshot must produce a JSON payload.");

        using var document = JsonDocument.Parse(snapshotJson!);
        var root = document.RootElement;
        return new ServiceWorkerSurfaceSnapshot(
            ReadBooleanProperty(root, "isSecureContext"),
            root.TryGetProperty("hasServiceWorkerApi", out var hasServiceWorkerApi)
                && hasServiceWorkerApi.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ReadBooleanProperty(root, "hasController"),
            ReadOptionalStringProperty(root, "controllerScriptUrl"),
            ReadOptionalStringProperty(root, "controllerState"),
            ReadOptionalStringProperty(root, "error"));
    }

    private static async Task<ServiceWorkerSurfaceSnapshot> WaitForServiceWorkerControllerAsync(WebPage page, string message)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ServiceWorkerSurfaceSnapshot snapshot = new(false, false, false, null, null, null);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            snapshot = await CaptureServiceWorkerSurfaceSnapshotAsync(page).ConfigureAwait(false);
            if (snapshot.HasController)
                return snapshot;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail($"{message} snapshot={snapshot}");
        return snapshot;
    }

    private static async Task<string?> ExecuteMainWorldStringAsync(WebPage page, string script)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var results = await page.ExecuteInAllFramesAsync(script).ConfigureAwait(false);
                if (results is not JsonElement element)
                    return null;

                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in element.EnumerateArray())
                    {
                        return entry.ValueKind == JsonValueKind.String
                            ? entry.GetString()
                            : entry.ToString();
                    }

                    return null;
                }

                return element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString();
            }
            catch (InvalidOperationException) when (attempt < 99)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static string? ReadOptionalStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? ReadOptionalDoubleProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var value)
                ? value
                : null;

    private static bool? ReadOptionalBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static bool ReadRequiredBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new InvalidOperationException($"JSON payload does not contain required boolean property '{propertyName}'.");

    private static bool ReadBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();

    private static int ReadInt32Property(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : -1;

    private static string[] ReadStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static void AssertResolvedDevice(WebPage page, Device expected, string message)
    {
        Assert.Multiple(() =>
        {
            Assert.That(page.ResolvedDevice, Is.Not.Null, $"{message}; resolvedDevice=<null>");
            Assert.That(page.ResolvedDevice?.UserAgent, Is.EqualTo(expected.UserAgent), $"{message}; resolvedUserAgent={page.ResolvedDevice?.UserAgent ?? "<null>"}");
            Assert.That(page.ResolvedDevice?.Locale, Is.EqualTo(expected.Locale), $"{message}; resolvedLocale={page.ResolvedDevice?.Locale ?? "<null>"}");
        });
    }

    private static void AssertGeolocationSnapshot(GeolocationSurfaceSnapshot snapshot, GeolocationSettings expected, string scope)
    {
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Error, Is.Null, $"{scope} geolocation snapshot failed. snapshot={snapshot}");
            Assert.That(snapshot.Latitude, Is.Not.Null, $"{scope} latitude missing. snapshot={snapshot}");
            Assert.That(snapshot.Longitude, Is.Not.Null, $"{scope} longitude missing. snapshot={snapshot}");
            Assert.That(snapshot.Accuracy, Is.Not.Null, $"{scope} accuracy missing. snapshot={snapshot}");
            Assert.That(snapshot.Latitude!.Value, Is.EqualTo(expected.Latitude).Within(0.0001d), $"{scope} latitude mismatch. snapshot={snapshot}");
            Assert.That(snapshot.Longitude!.Value, Is.EqualTo(expected.Longitude).Within(0.0001d), $"{scope} longitude mismatch. snapshot={snapshot}");

            if (expected.Accuracy.HasValue)
            {
                Assert.That(snapshot.Accuracy!.Value, Is.EqualTo(expected.Accuracy.Value).Within(0.1d), $"{scope} accuracy mismatch. snapshot={snapshot}");
            }
        });
    }

    private static void AssertPrivacySignalSnapshot(PrivacySignalSnapshot snapshot, Device expected, string scope)
    {
        Assert.Multiple(() =>
        {
            if (expected.DoNotTrack.HasValue)
            {
                Assert.That(snapshot.DoNotTrack, Is.EqualTo(expected.DoNotTrack.Value ? "1" : "0"), $"{scope} doNotTrack mismatch. snapshot={snapshot}");
            }

            if (expected.GlobalPrivacyControl.HasValue)
            {
                Assert.That(snapshot.GlobalPrivacyControl, Is.EqualTo(expected.GlobalPrivacyControl.Value), $"{scope} globalPrivacyControl mismatch. snapshot={snapshot}");
            }
        });
    }

    private static void AssertMediaDevicesSnapshot(MediaDevicesSurfaceSnapshot snapshot, VirtualMediaDevicesSettings expected, string scope)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var expectedAudioInputDeviceId = ResolveVirtualMediaDeviceId("audioinput", expected.GroupId, expected.AudioInputBrowserDeviceId);
        var expectedVideoInputDeviceId = ResolveVirtualMediaDeviceId("videoinput", expected.GroupId, expected.VideoInputBrowserDeviceId);
        var expectedAudioOutputDeviceId = ResolveVirtualMediaDeviceId("audiooutput", expected.GroupId, null);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsSecureContext, Is.True, $"{scope} mediaDevices page must stay in a secure context. snapshot={snapshot}");
            Assert.That(snapshot.HasMediaDevicesApi, Is.True, $"{scope} mediaDevices API missing. snapshot={snapshot}");
            Assert.That(snapshot.EnumerateError, Is.Null, $"{scope} enumerateDevices failed. snapshot={snapshot}");
            Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "audioinput" && device.Label == expected.AudioInputLabel), $"{scope} audioinput alias missing. snapshot={snapshot}");
            Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "videoinput" && device.Label == expected.VideoInputLabel), $"{scope} videoinput alias missing. snapshot={snapshot}");

            if (!string.IsNullOrWhiteSpace(expected.GroupId))
            {
                Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "audioinput" && device.GroupId == expected.GroupId), $"{scope} audioinput groupId mismatch. snapshot={snapshot}");
                Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "videoinput" && device.GroupId == expected.GroupId), $"{scope} videoinput groupId mismatch. snapshot={snapshot}");
            }

            Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "audioinput" && device.DeviceId == expectedAudioInputDeviceId), $"{scope} audioinput deviceId mismatch. snapshot={snapshot}");
            Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "videoinput" && device.DeviceId == expectedVideoInputDeviceId), $"{scope} videoinput deviceId mismatch. snapshot={snapshot}");

            if (expected.AudioOutputEnabled)
            {
                Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "audiooutput" && device.Label == expected.AudioOutputLabel), $"{scope} audiooutput alias missing. snapshot={snapshot}");
                Assert.That(snapshot.Devices, Has.Some.Matches<MediaDeviceEntry>(device => device.Kind == "audiooutput" && device.DeviceId == expectedAudioOutputDeviceId), $"{scope} audiooutput deviceId mismatch. snapshot={snapshot}");
            }

            Assert.That(snapshot.VideoRequest, Is.Not.Null, $"{scope} video getUserMedia snapshot missing. snapshot={snapshot}");
            Assert.That(snapshot.VideoRequest!.Ok, Is.True, $"{scope} video getUserMedia should succeed. snapshot={snapshot}");
            Assert.That(snapshot.VideoRequest.RequestedDeviceId, Is.EqualTo(expectedVideoInputDeviceId), $"{scope} video request should target alias deviceId. snapshot={snapshot}");
            Assert.That(snapshot.VideoRequest.Active, Is.True, $"{scope} video stream must be active. snapshot={snapshot}");
            Assert.That(snapshot.VideoRequest.TrackCount, Is.GreaterThanOrEqualTo(1), $"{scope} video stream must expose at least one track. snapshot={snapshot}");
            Assert.That(snapshot.VideoRequest.Tracks, Has.Some.Matches<MediaTrackSnapshot>(track => track.Kind == "video" && track.ReadyState == "live"), $"{scope} video stream must expose a live video track. snapshot={snapshot}");
        });
    }

    private static void AssertMediaDevicesSnapshotDoesNotLeak(MediaDevicesSurfaceSnapshot snapshot, VirtualMediaDevicesSettings unexpected, string scope)
    {
        ArgumentNullException.ThrowIfNull(unexpected);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsSecureContext, Is.True, $"{scope} mediaDevices page must stay in a secure context. snapshot={snapshot}");
            Assert.That(snapshot.HasMediaDevicesApi, Is.True, $"{scope} mediaDevices API missing. snapshot={snapshot}");
            Assert.That(snapshot.EnumerateError, Is.Null, $"{scope} enumerateDevices failed. snapshot={snapshot}");
            Assert.That(snapshot.Devices.Any(device => string.Equals(device.Label, unexpected.AudioInputLabel, StringComparison.Ordinal)), Is.False, $"{scope} leaked audioinput alias. snapshot={snapshot}");
            Assert.That(snapshot.Devices.Any(device => string.Equals(device.Label, unexpected.VideoInputLabel, StringComparison.Ordinal)), Is.False, $"{scope} leaked videoinput alias. snapshot={snapshot}");

            if (unexpected.AudioOutputEnabled)
                Assert.That(snapshot.Devices.Any(device => string.Equals(device.Label, unexpected.AudioOutputLabel, StringComparison.Ordinal)), Is.False, $"{scope} leaked audiooutput alias. snapshot={snapshot}");

            if (!string.IsNullOrWhiteSpace(unexpected.GroupId))
                Assert.That(snapshot.Devices.Any(device => string.Equals(device.GroupId, unexpected.GroupId, StringComparison.Ordinal)), Is.False, $"{scope} leaked media groupId. snapshot={snapshot}");
        });
    }

    private static Device CreateVirtualMediaDevice(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var device = Device.DesktopFullHd;
        device.VirtualMediaDevices = new VirtualMediaDevicesSettings
        {
            AudioInputEnabled = true,
            AudioInputLabel = "Atom Live Microphone " + id,
            VideoInputEnabled = true,
            VideoInputLabel = "Atom Live Camera " + id,
            AudioOutputEnabled = true,
            AudioOutputLabel = "Atom Live Speakers " + id,
            GroupId = "atom-live-media-" + id,
        };
        return device;
    }

    private static string ResolveVirtualMediaDeviceId(string kind, string? groupId, string? configuredDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(configuredDeviceId))
            return configuredDeviceId;

        var resolvedGroupId = string.IsNullOrWhiteSpace(groupId)
            ? "atom-virtual-media"
            : groupId;
        return kind + "-" + resolvedGroupId;
    }

    private sealed class FakeVirtualCameraBackend(string deviceIdentifier = "fake-camera") : IVirtualCameraBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(CameraControlType control, float value)
            => ControlChanged?.Invoke(this, new CameraControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(CameraControlType control) => 0.0f;

        public CameraControlRange? GetControlRange(CameraControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeVirtualMicrophoneBackend(string deviceIdentifier = "fake-microphone") : IVirtualMicrophoneBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteSamples(ReadOnlySpan<byte> sampleData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(MicrophoneControlType control, float value)
            => ControlChanged?.Invoke(this, new MicrophoneControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(MicrophoneControlType control) => 0.0f;

        public MicrophoneControlRange? GetControlRange(MicrophoneControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static MediaDeviceEntry[] ReadMediaDeviceEntries(JsonElement root)
    {
        if (!root.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
            return [];

        return devices.EnumerateArray()
            .Select(static device => new MediaDeviceEntry(
                ReadOptionalStringProperty(device, "kind"),
                ReadOptionalStringProperty(device, "label"),
                ReadOptionalStringProperty(device, "deviceId"),
                ReadOptionalStringProperty(device, "groupId")))
            .ToArray();
    }

    private static MediaVideoRequestSnapshot? ReadMediaVideoRequest(JsonElement root)
    {
        if (!root.TryGetProperty("videoRequest", out var videoRequest)
            || videoRequest.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new MediaVideoRequestSnapshot(
            ReadBooleanProperty(videoRequest, "ok"),
            ReadOptionalStringProperty(videoRequest, "requestedDeviceId"),
            ReadBooleanProperty(videoRequest, "active"),
            ReadInt32Property(videoRequest, "trackCount"),
            ReadMediaTracks(videoRequest),
            ReadOptionalStringProperty(videoRequest, "errorName"),
            ReadOptionalStringProperty(videoRequest, "errorMessage"));
    }

    private static MediaTrackSnapshot[] ReadMediaTracks(JsonElement root)
    {
        if (!root.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            return [];

        return tracks.EnumerateArray()
            .Select(static track => new MediaTrackSnapshot(
                ReadOptionalStringProperty(track, "kind"),
                ReadOptionalStringProperty(track, "readyState"),
                ReadBooleanProperty(track, "enabled"),
                ReadBooleanProperty(track, "muted"),
                ReadOptionalStringProperty(track, "label")))
            .ToArray();
    }

    private static void AssertFingerprintMatches(JsonElement snapshot, Device device, string scope)
    {
        if (!snapshot.TryGetProperty("languages", out var languagesElement)
            || !snapshot.TryGetProperty("userAgent", out _)
            || !snapshot.TryGetProperty("language", out _)
            || !snapshot.TryGetProperty("platform", out _)
            || !snapshot.TryGetProperty("timezone", out _)
            || !snapshot.TryGetProperty("maxTouchPoints", out _)
            || !snapshot.TryGetProperty("devicePixelRatio", out _)
            || !snapshot.TryGetProperty("innerWidth", out _)
            || !snapshot.TryGetProperty("innerHeight", out _))
        {
            Assert.Fail($"{scope} fingerprint snapshot was not populated; snapshot={snapshot.GetRawText()}");
        }

        var expectedLanguages = device.Languages?.ToArray() ?? [];
        var actualLanguages = languagesElement.EnumerateArray().Select(static value => value.GetString()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                snapshot.GetProperty("userAgent").GetString(),
                Is.EqualTo(device.UserAgent),
                $"{scope} userAgent mismatch; pageHasContext={snapshot.GetProperty("pageHasContext").GetBoolean()}; pageContextId={snapshot.GetProperty("pageContextId").GetString()}; pageContextUserAgent={snapshot.GetProperty("pageContextUserAgent").GetString()}; contentHasContext={snapshot.GetProperty("contentHasContext").GetBoolean()}; contentContextId={snapshot.GetProperty("contentContextId").GetString()}; contentContextUserAgent={snapshot.GetProperty("contentContextUserAgent").GetString()}; snapshot={snapshot.GetRawText()}");
            if (!string.IsNullOrWhiteSpace(device.Platform))
                Assert.That(snapshot.GetProperty("platform").GetString(), Is.EqualTo(device.Platform), $"{scope} platform mismatch");

            Assert.That(snapshot.GetProperty("language").GetString(), Is.EqualTo(device.Locale ?? expectedLanguages.FirstOrDefault()), $"{scope} language mismatch");
            Assert.That(actualLanguages, Is.EqualTo(expectedLanguages), $"{scope} languages mismatch");

            if (!string.IsNullOrWhiteSpace(device.Timezone))
                Assert.That(snapshot.GetProperty("timezone").GetString(), Is.EqualTo(device.Timezone), $"{scope} timezone mismatch");

            if (device.HardwareConcurrency.HasValue)
                Assert.That(snapshot.GetProperty("hardwareConcurrency").GetInt32(), Is.EqualTo(device.HardwareConcurrency.Value), $"{scope} hardwareConcurrency mismatch");

            if (device.DeviceMemory.HasValue)
                Assert.That(snapshot.GetProperty("deviceMemory").GetDouble(), Is.EqualTo(device.DeviceMemory.Value).Within(0.001d), $"{scope} deviceMemory mismatch");

            Assert.That(snapshot.GetProperty("maxTouchPoints").GetInt32(), Is.EqualTo(device.MaxTouchPoints), $"{scope} maxTouchPoints mismatch");

            if (device.DeviceScaleFactor > 0)
                Assert.That(snapshot.GetProperty("devicePixelRatio").GetDouble(), Is.EqualTo(device.DeviceScaleFactor).Within(0.001d), $"{scope} devicePixelRatio mismatch");

            if (!device.ViewportSize.IsEmpty)
            {
                Assert.That(snapshot.GetProperty("innerWidth").GetInt32(), Is.EqualTo(device.ViewportSize.Width), $"{scope} innerWidth mismatch");
                Assert.That(snapshot.GetProperty("innerHeight").GetInt32(), Is.EqualTo(device.ViewportSize.Height), $"{scope} innerHeight mismatch");
            }
        });
    }

    private readonly record struct PageCallbackHarnessSnapshot(string Calls, string ArgsJson, string Replaced);

    private static async Task PreparePageCallbackHarnessAsync(WebPage page)
    {
        _ = await page.EvaluateAsync(
            """
            (() => {
                document.documentElement.dataset.atomReadyCalls = '0';
                document.documentElement.dataset.atomReadyArgs = '[]';
                document.documentElement.dataset.atomReadyReplaced = '0';

                globalThis.app = globalThis.app ?? {};
                globalThis.app.ready = (...args) => {
                    document.documentElement.dataset.atomReadyCalls = String(Number(document.documentElement.dataset.atomReadyCalls || '0') + 1);
                    document.documentElement.dataset.atomReadyArgs = JSON.stringify(args);
                    return 'original:' + args.join('|');
                };
            })();
            """).ConfigureAwait(false);
    }

    private static async Task<PageCallbackHarnessSnapshot> CapturePageCallbackHarnessSnapshotAsync(WebPage page)
    {
        var payload = await page.EvaluateAsync<string>(
            """
            return JSON.stringify({
                calls: String(document.documentElement.dataset.atomReadyCalls ?? '0'),
                args: String(document.documentElement.dataset.atomReadyArgs ?? '[]'),
                replaced: String(document.documentElement.dataset.atomReadyReplaced ?? '0'),
            });
            """).ConfigureAwait(false) ?? "{}";

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return new PageCallbackHarnessSnapshot(
            root.TryGetProperty("calls", out var calls) ? calls.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("args", out var args) ? args.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("replaced", out var replaced) ? replaced.GetString() ?? string.Empty : string.Empty);
    }

    private static async Task WaitForPageCallbackRelayAsync(IReadOnlyCollection<CallbackEventArgs> callbacks, IReadOnlyCollection<CallbackFinalizedEventArgs> finalized)
        => await WaitForConditionAsync(
            static state => state.Callbacks.Count > 0 && state.Finalized.Count > 0,
            (Callbacks: callbacks, Finalized: finalized),
            TimeSpan.FromSeconds(3)).ConfigureAwait(false);

    private static async Task<string> DescribePageCallbackFailureAsync(
        WebBrowser browser,
        WebPage page,
        List<CallbackEventArgs> callbacks,
        List<CallbackFinalizedEventArgs>? finalized = null,
        string? payload = null,
        bool includeHarnessSnapshot = false)
    {
        var details = new List<string>
        {
            await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false),
            $"callbacks={callbacks.Count}"
        };

        if (finalized is not null)
            details.Add($"finalized={finalized.Count}");

        if (payload is not null)
            details.Add($"payload={payload}");

        if (includeHarnessSnapshot)
        {
            try
            {
                var snapshot = await CapturePageCallbackHarnessSnapshotAsync(page).ConfigureAwait(false);
                details.Add($"harnessCalls={snapshot.Calls}");
                details.Add($"harnessArgs={snapshot.ArgsJson}");
                details.Add($"harnessReplaced={snapshot.Replaced}");
            }
            catch (Exception error)
            {
                details.Add($"harnessSnapshotError={error.Message}");
            }
        }

        return string.Join(", ", details);
    }

    private static bool HasCallbackSubscription(WebPage page, string callbackPath)
    {
        var field = typeof(WebPage).GetField("callbackSubscriptions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(WebPage).FullName, "callbackSubscriptions");
        var subscriptions = field.GetValue(page) as System.Collections.IDictionary
            ?? throw new InvalidOperationException("Не удалось получить callbackSubscriptions для test inspection.");
        return subscriptions.Contains(callbackPath);
    }

    private static DelegateDisposable SubscribeScopedResponseHandler(
        WebBrowser browser,
        WebWindow window,
        OuterResponseInterceptionScope scope,
        Func<InterceptedResponseEventArgs, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(handler);

        if (scope is OuterResponseInterceptionScope.Window)
        {
            AsyncEventHandler<IWebWindow, InterceptedResponseEventArgs> windowHandler = (_, args) => handler(args);
            window.Response += windowHandler;
            return new DelegateDisposable(() => window.Response -= windowHandler);
        }

        AsyncEventHandler<IWebBrowser, InterceptedResponseEventArgs> browserHandler = (_, args) => handler(args);
        browser.Response += browserHandler;
        return new DelegateDisposable(() => browser.Response -= browserHandler);
    }

    private static DelegateDisposable SubscribeScopedRequestHandler(
        WebBrowser browser,
        WebWindow window,
        OuterRequestInterceptionScope scope,
        Func<InterceptedRequestEventArgs, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(handler);

        if (scope is OuterRequestInterceptionScope.Window)
        {
            AsyncEventHandler<IWebWindow, InterceptedRequestEventArgs> windowHandler = (_, args) => handler(args);
            window.Request += windowHandler;
            return new DelegateDisposable(() => window.Request -= windowHandler);
        }

        AsyncEventHandler<IWebBrowser, InterceptedRequestEventArgs> browserHandler = (_, args) => handler(args);
        browser.Request += browserHandler;
        return new DelegateDisposable(() => browser.Request -= browserHandler);
    }

    private readonly record struct LookupTarget(string Title, Uri Url, string MarkerId, string MarkerValue);

    public enum OuterResponseInterceptionScope
    {
        Window,
        Browser,
    }

    public enum OuterRequestInterceptionScope
    {
        Window,
        Browser,
    }

    private sealed record StorageSurfaceSnapshot(
        string? ContextId,
        bool HasLocalStorage,
        bool HasSessionStorage,
        string? LocalValue,
        string? SessionValue,
        int LocalLength,
        int SessionLength,
        string? Error);

    private sealed record IndexedDbAndCacheSurfaceSnapshot(
        string? ContextId,
        bool HasIndexedDb,
        bool HasCaches,
        string? IndexedDbValue,
        string? CacheValue,
        string[] CacheKeys,
        string? Error);

    private sealed record ServiceWorkerSurfaceSnapshot(
        bool IsSecureContext,
        bool HasServiceWorkerApi,
        bool HasController,
        string? ControllerScriptUrl,
        string? ControllerState,
        string? Error);

    private sealed record GeolocationSurfaceSnapshot(
        double? Latitude,
        double? Longitude,
        double? Accuracy,
        string? Error);

    private sealed record PrivacySignalSnapshot(
        string? DoNotTrack,
        bool? GlobalPrivacyControl);

    private sealed record MediaDeviceEntry(
        string? Kind,
        string? Label,
        string? DeviceId,
        string? GroupId);

    private sealed record MediaTrackSnapshot(
        string? Kind,
        string? ReadyState,
        bool Enabled,
        bool Muted,
        string? Label);

    private sealed record MediaVideoRequestSnapshot(
        bool Ok,
        string? RequestedDeviceId,
        bool Active,
        int TrackCount,
        MediaTrackSnapshot[] Tracks,
        string? ErrorName,
        string? ErrorMessage);

    private sealed record MediaDevicesSurfaceSnapshot(
        bool IsSecureContext,
        bool HasMediaDevicesApi,
        string? EnumerateError,
        MediaDeviceEntry[] Devices,
        MediaVideoRequestSnapshot? VideoRequest);

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class RealBrowserDeviceFingerprintLoopbackServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private Task? serverTask;

        public Task StartAsync()
        {
            listener.Start();
            serverTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public Uri CreatePageUrl(string marker)
                => CreateUrl($"/real-browser-device/{Uri.EscapeDataString(marker)}");

        public void Dispose()
        {
            cancellationTokenSource.Cancel();

            try { listener.Stop(); }
            catch { }

            try { serverTask?.GetAwaiter().GetResult(); }
            catch { }

            cancellationTokenSource.Dispose();
        }

        private Uri CreateUrl(string path)
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new Uri($"http://127.0.0.1:{port}{path}");
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    var requestHead = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                    var path = ReadRequestPath(requestHead);

                    if (path.Contains("/real-browser-device/", StringComparison.Ordinal))
                    {
                        var marker = path[(path.LastIndexOf('/') + 1)..];
                        await WriteResponseAsync(
                                stream,
                                "200 OK",
                                "text/html; charset=utf-8",
                                Encoding.UTF8.GetBytes(CreateDeviceFingerprintResponseBody(Uri.UnescapeDataString(marker))),
                                cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await WriteResponseAsync(
                            stream,
                            "404 Not Found",
                            "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes("not-found"),
                            cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static string CreateDeviceFingerprintResponseBody(string marker)
                => $$"""
                        <!doctype html>
                        <html>
                            <head>
                                <meta charset="utf-8">
                                <title>Device Fingerprint {{WebUtility.HtmlEncode(marker)}}</title>
                            </head>
                            <body>
                                <main id="marker">{{WebUtility.HtmlEncode(marker)}}</main>
                                <script id="fingerprint-snapshot" type="application/json"></script>
                                <script>
                                    (() => {
                                        const writeSnapshot = () => {
                                            const targetWindow = window;
                                            const pageContext = targetWindow.__atomTabContext ?? null;
                                            const contentContext = globalThis.__atomContentRuntimeContext ?? null;
                                            const payload = {
                                                pageHasContext: pageContext !== null,
                                                pageContextId: pageContext?.contextId ?? null,
                                                pageContextUserAgent: pageContext?.userAgent ?? null,
                                                contentHasContext: contentContext !== null,
                                                contentContextId: contentContext?.contextId ?? null,
                                                contentContextUserAgent: contentContext?.userAgent ?? null,
                                                userAgent: targetWindow.navigator.userAgent ?? null,
                                                platform: targetWindow.navigator.platform ?? null,
                                                language: targetWindow.navigator.language ?? null,
                                                languages: Array.from(targetWindow.navigator.languages ?? []),
                                                timezone: new targetWindow.Intl.DateTimeFormat().resolvedOptions().timeZone ?? null,
                                                hardwareConcurrency: targetWindow.navigator.hardwareConcurrency ?? null,
                                                deviceMemory: targetWindow.navigator.deviceMemory ?? null,
                                                maxTouchPoints: targetWindow.navigator.maxTouchPoints ?? null,
                                                devicePixelRatio: targetWindow.devicePixelRatio ?? null,
                                                innerWidth: targetWindow.innerWidth ?? null,
                                                innerHeight: targetWindow.innerHeight ?? null,
                                            };

                                            document.getElementById('fingerprint-snapshot').textContent = JSON.stringify(payload);
                                        };

                                        writeSnapshot();
                                        window.addEventListener('load', writeSnapshot, { once: true });
                                        setTimeout(writeSnapshot, 50);
                                        setTimeout(writeSnapshot, 150);
                                        setTimeout(writeSnapshot, 300);
                                    })();
                                </script>
                            </body>
                        </html>
                        """;

        private static string ReadRequestPath(string requestHead)
        {
            var firstLineEnd = requestHead.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? requestHead[..firstLineEnd] : requestHead;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1] : "/";
        }

        private static async Task<string> ReadRequestHeadAsync(System.Net.Sockets.NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                memory.Write(buffer, 0, read);
                var snapshot = Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
                if (snapshot.Contains("\r\n\r\n", StringComparison.Ordinal))
                    return snapshot;
            }

            return Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
        }

        private static async Task WriteResponseAsync(System.Net.Sockets.NetworkStream stream, string status, string contentType, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(headBytes, cancellationToken).ConfigureAwait(false);

            if (!body.IsEmpty)
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RealBrowserServiceWorkerLoopbackServer : IDisposable
    {
        private const string BasePath = "/real-browser-service-worker";
        private readonly object observedRequestHeadsSync = new();
        private readonly Dictionary<string, List<string>> observedRequestHeads = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private Task? serverTask;

        public Uri PageUrl { get; private set; } = null!;
        public Uri ServiceWorkerUrl { get; private set; } = null!;
        public Uri NetworkDataUrl { get; private set; } = null!;
        public static string ServiceWorkerPath => BasePath + "/sw.js";
        public static string NetworkDataPath => BasePath + "/network-data";

        public Task StartAsync()
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            PageUrl = new Uri($"http://127.0.0.1:{port}{BasePath}/page");
            ServiceWorkerUrl = new Uri($"http://127.0.0.1:{port}{ServiceWorkerPath}");
            NetworkDataUrl = new Uri($"http://127.0.0.1:{port}{NetworkDataPath}");
            serverTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public bool HasObservedRequest(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (observedRequestHeadsSync)
            {
                return observedRequestHeads.TryGetValue(path, out var requests)
                    && requests.Count > 0;
            }
        }

        public int GetObservedRequestCount(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (observedRequestHeadsSync)
            {
                return observedRequestHeads.TryGetValue(path, out var requests)
                    ? requests.Count
                    : 0;
            }
        }

        public string? GetLastObservedRequestHead(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (observedRequestHeadsSync)
            {
                return observedRequestHeads.TryGetValue(path, out var requests)
                    && requests.Count > 0
                        ? requests[^1]
                        : null;
            }
        }

        public string[] GetObservedPaths()
        {
            lock (observedRequestHeadsSync)
            {
                return observedRequestHeads.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();

            try { listener.Stop(); }
            catch { }

            try { serverTask?.GetAwaiter().GetResult(); }
            catch { }

            cancellationTokenSource.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    var requestHead = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                    var path = NormalizeRequestPath(ReadRequestPath(requestHead));
                    RecordObservedRequest(path, requestHead);

                    if (string.Equals(path, PageUrl.AbsolutePath, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(
                            stream,
                            "200 OK",
                            "text/html; charset=utf-8",
                            Encoding.UTF8.GetBytes(CreatePageResponseBody()),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(path, ServiceWorkerPath, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(
                            stream,
                            "200 OK",
                            "application/javascript; charset=utf-8",
                            Encoding.UTF8.GetBytes(CreateServiceWorkerResponseBody()),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(path, NetworkDataPath, StringComparison.Ordinal))
                    {
                        client.Client.LingerState = new LingerOption(true, 0);
                        client.Close();
                        continue;
                    }

                    await WriteResponseAsync(
                        stream,
                        "404 Not Found",
                        "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("not-found"),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RecordObservedRequest(string path, string requestHead)
        {
            lock (observedRequestHeadsSync)
            {
                if (!observedRequestHeads.TryGetValue(path, out var requests))
                {
                    requests = [];
                    observedRequestHeads[path] = requests;
                }

                requests.Add(requestHead);
            }
        }

        private static string CreatePageResponseBody()
            => """
                <!doctype html>
                <html>
                    <head>
                        <meta charset="utf-8">
                        <title>Real Browser Service Worker Loopback</title>
                    </head>
                    <body>
                        <main id="status">service-worker-loopback</main>
                    </body>
                </html>
                """;

        private static string CreateServiceWorkerResponseBody()
            => """
                self.addEventListener('install', event => {
                    event.waitUntil(self.skipWaiting());
                });

                self.addEventListener('activate', event => {
                    event.waitUntil(self.clients.claim());
                });

                self.addEventListener('fetch', event => {
                    const url = new URL(event.request.url);
                    if (url.pathname !== '/real-browser-service-worker/network-data') {
                        return;
                    }

                    event.respondWith((async () => {
                        try {
                            return await fetch(event.request);
                        } catch {
                            return new Response('offline-fallback', {
                                status: 200,
                                headers: {
                                    'content-type': 'text/plain; charset=utf-8',
                                    'x-atom-source': 'service-worker-offline',
                                },
                            });
                        }
                    })());
                });
                """;

        private static string ReadRequestPath(string requestHead)
        {
            var firstLineEnd = requestHead.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? requestHead[..firstLineEnd] : requestHead;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return "/";

            var rawPath = parts[1];
            return Uri.TryCreate(rawPath, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.PathAndQuery
                : rawPath;
        }

        private static string NormalizeRequestPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
            return queryIndex >= 0
                ? path[..queryIndex]
                : path;
        }

        private static async Task<string> ReadRequestHeadAsync(System.Net.Sockets.NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                memory.Write(buffer, 0, read);
                var snapshot = Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
                if (snapshot.Contains("\r\n\r\n", StringComparison.Ordinal))
                    return snapshot;
            }

            return Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
        }

        private static async Task WriteResponseAsync(System.Net.Sockets.NetworkStream stream, string status, string contentType, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(headBytes, cancellationToken).ConfigureAwait(false);

            if (!body.IsEmpty)
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RealBrowserInterceptionLoopbackServer : IDisposable
    {
        private readonly object observedRequestsSync = new();
        private readonly Dictionary<string, ObservedLoopbackRequest> observedRequests = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private Task? serverTask;

        public Uri PageScopeTargetPageUrl { get; private set; } = null!;
        public Uri PageScopeOtherPageUrl { get; private set; } = null!;
        public Uri WindowFanoutNonCurrentPageUrl { get; private set; } = null!;
        public Uri WindowFanoutCurrentPageUrl { get; private set; } = null!;
        public Uri WindowFanoutOtherWindowPageUrl { get; private set; } = null!;

        public Task StartAsync()
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            PageScopeTargetPageUrl = new Uri($"http://127.0.0.1:{port}/real-browser-interception/page-scope/target");
            PageScopeOtherPageUrl = new Uri($"http://127.0.0.1:{port}/real-browser-interception/page-scope/other");
            WindowFanoutNonCurrentPageUrl = new Uri($"http://127.0.0.1:{port}/real-browser-interception/window-fanout/non-current");
            WindowFanoutCurrentPageUrl = new Uri($"http://127.0.0.1:{port}/real-browser-interception/window-fanout/current");
            WindowFanoutOtherWindowPageUrl = new Uri($"http://127.0.0.1:{port}/real-browser-interception/window-fanout/other-window");
            serverTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public Uri CreatePageScopeFetchTargetUrl(string page)
            => CreateUrl($"/real-browser-interception/fetch-target?page={Uri.EscapeDataString(page)}&tick={Guid.NewGuid():N}");

        public Uri CreatePageScopeFetchOtherUrl(string page)
            => CreateUrl($"/real-browser-interception/fetch-other?page={Uri.EscapeDataString(page)}&tick={Guid.NewGuid():N}");

        public Uri CreateWindowFanoutFetchUrl(string page)
            => CreateUrl($"/real-browser-interception/window-fanout/fetch/{Uri.EscapeDataString(page)}?tick={Guid.NewGuid():N}");

        public Uri CreateDecisionFetchUrl(string scenario)
            => CreateUrl($"/real-browser-interception/decision/{Uri.EscapeDataString(scenario)}?tick={Guid.NewGuid():N}");

        public Uri CreateHeaderEchoUrl(string headerName, string scenario)
            => CreateUrl($"/real-browser-interception/header-echo/{Uri.EscapeDataString(scenario)}?header={Uri.EscapeDataString(headerName)}&tick={Guid.NewGuid():N}");

        public bool HasObservedRequest(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);

            lock (observedRequestsSync)
            {
                return observedRequests.ContainsKey(url.PathAndQuery);
            }
        }

        public string? GetObservedRequestHeader(Uri url, string headerName)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(headerName);

            lock (observedRequestsSync)
            {
                if (!observedRequests.TryGetValue(url.PathAndQuery, out var observedRequest))
                    return null;

                return observedRequest.Headers.TryGetValue(headerName, out var value)
                    ? value
                    : null;
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();

            try { listener.Stop(); }
            catch { }

            try { serverTask?.GetAwaiter().GetResult(); }
            catch { }

            cancellationTokenSource.Dispose();
        }

        private Uri CreateUrl(string path)
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new Uri($"http://127.0.0.1:{port}{path}");
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    var requestHead = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                    var path = ReadRequestPath(requestHead);
                    var requestHeaders = ReadRequestHeaders(requestHead);
                    RecordObservedRequest(path, requestHeaders);

                    if (path.Contains("/page-scope/", StringComparison.Ordinal)
                        || path.Contains("/window-fanout/", StringComparison.Ordinal) && !path.Contains("/fetch/", StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(
                            stream,
                            "200 OK",
                            "text/html; charset=utf-8",
                            Encoding.UTF8.GetBytes($"<html><body><main>{WebUtility.HtmlEncode(path)}</main></body></html>"),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (path.Contains("/real-browser-interception/header-echo/", StringComparison.Ordinal))
                    {
                        var headerName = ReadQueryParameter(path, "header");
                        var headerValue = headerName is null
                            ? string.Empty
                            : requestHeaders.TryGetValue(headerName, out var value)
                                ? value
                                : string.Empty;

                        await WriteResponseAsync(
                            stream,
                            "200 OK",
                            "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes(headerValue),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (path.Contains("/real-browser-interception/", StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(
                            stream,
                            "200 OK",
                            "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes($"ok:{path}"),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await WriteResponseAsync(
                        stream,
                        "404 Not Found",
                        "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("not-found"),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RecordObservedRequest(string path, IReadOnlyDictionary<string, string> headers)
        {
            lock (observedRequestsSync)
            {
                observedRequests[path] = new ObservedLoopbackRequest(path, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
            }
        }

        private static string ReadRequestPath(string requestHead)
        {
            var firstLineEnd = requestHead.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? requestHead[..firstLineEnd] : requestHead;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1] : "/";
        }

        private static Dictionary<string, string> ReadRequestHeaders(string requestHead)
        {
            var lines = requestHead.Split(["\r\n"], StringSplitOptions.None);
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrEmpty(line))
                    break;

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                var name = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (name.Length == 0)
                    continue;

                headers[name] = value;
            }

            return headers;
        }

        private static string? ReadQueryParameter(string path, string parameterName)
        {
            var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex < 0 || queryIndex == path.Length - 1)
                return null;

            var query = path[(queryIndex + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
                var key = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
                if (!string.Equals(Uri.UnescapeDataString(key), parameterName, StringComparison.Ordinal))
                    continue;

                var value = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
                return Uri.UnescapeDataString(value);
            }

            return null;
        }

        private static async Task<string> ReadRequestHeadAsync(System.Net.Sockets.NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                memory.Write(buffer, 0, read);
                var snapshot = Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
                if (snapshot.Contains("\r\n\r\n", StringComparison.Ordinal))
                    return snapshot;
            }

            return Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
        }

        private static async Task WriteResponseAsync(System.Net.Sockets.NetworkStream stream, string status, string contentType, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(headBytes, cancellationToken).ConfigureAwait(false);

            if (!body.IsEmpty)
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private sealed record ObservedLoopbackRequest(string PathAndQuery, IReadOnlyDictionary<string, string> Headers);
    }

    private static void SubscribeLifecycle(WebPage page, List<string> events)
    {
        page.DomContentLoaded += (_, args) => RecordLifecycle(events, "DomContentLoaded", args);
        page.NavigationCompleted += (_, args) => RecordLifecycle(events, "NavigationCompleted", args);
        page.PageLoaded += (_, args) => RecordLifecycle(events, "PageLoaded", args);
    }

    private static void SubscribeLifecycle(WebWindow window, List<string> events)
    {
        window.DomContentLoaded += (_, args) => RecordLifecycle(events, "DomContentLoaded", args);
        window.NavigationCompleted += (_, args) => RecordLifecycle(events, "NavigationCompleted", args);
        window.PageLoaded += (_, args) => RecordLifecycle(events, "PageLoaded", args);
    }

    private static void SubscribeLifecycle(WebBrowser browser, List<string> events)
    {
        browser.DomContentLoaded += (_, args) => RecordLifecycle(events, "DomContentLoaded", args);
        browser.NavigationCompleted += (_, args) => RecordLifecycle(events, "NavigationCompleted", args);
        browser.PageLoaded += (_, args) => RecordLifecycle(events, "PageLoaded", args);
    }

    private static void RecordLifecycle(List<string> events, string name, WebLifecycleEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        events.Add(name);
    }

    private static async Task<string> DescribeBootstrapFailureAsync(WebBrowser browser, WebPage currentPage)
    {
        var details = new List<string>();
        var launchSettings = WebDriverTestEnvironment.GetLaunchSettings(browser);
        var manifestPath = launchSettings.Profile is { Path.Length: > 0 } profile
            ? Path.Combine(profile.Path, "profile.json")
            : null;
        AppendProfileManifestDiagnostics(details, manifestPath);
        var sessionId = manifestPath is null ? null : TryReadBridgeSessionId(manifestPath);

        details.Add($"bridgeCommandsBound={IsBridgeCommandsBound(currentPage)}");
        details.Add($"snapshotUrl={currentPage.CurrentUrl?.AbsoluteUri ?? "<null>"}");

        try
        {
            var liveUrl = await currentPage.GetUrlAsync().ConfigureAwait(false);
            details.Add($"liveUrl={liveUrl?.AbsoluteUri ?? "<null>"}");
        }
        catch (Exception ex)
        {
            details.Add($"liveUrlError={ex.GetType().Name}");
        }

        if (typeof(WebBrowser).GetField("bridgeBootstrapTask", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(browser) is Task<bool> bootstrapTask)
            details.Add($"bootstrapTaskStatus={bootstrapTask.Status}");

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            details.Add($"profileManifest={(manifestPath is null ? "missing-profile" : "missing-session-id")}");
            return string.Join(", ", details);
        }

        details.Add($"sessionId={sessionId}");

        var bridgeServer = typeof(WebBrowser).GetField("bridgeServer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(browser);
        if (bridgeServer is null)
        {
            details.Add("bridgeServer=missing");
            return string.Join(", ", details);
        }

        AppendExtensionDebugEventDiagnostics(details, bridgeServer);

        var sessionSnapshot = await InvokeInternalValueTaskAsync(bridgeServer, "CreateSessionSnapshotAsync", sessionId).ConfigureAwait(false);
        if (sessionSnapshot is null)
        {
            details.Add("sessionSnapshot=null");
            return string.Join(", ", details);
        }

        details.Add($"sessionConnected={GetPropertyValue<bool>(sessionSnapshot, "IsConnected")}");
        details.Add($"sessionBrowserFamily={GetPropertyValue<string>(sessionSnapshot, "BrowserFamily")}");
        details.Add($"sessionExtensionVersion={GetPropertyValue<string>(sessionSnapshot, "ExtensionVersion")}");

        if (await InvokeInternalValueTaskAsync(bridgeServer, "GetTabsForSessionAsync", sessionId).ConfigureAwait(false) is not Array tabs)
        {
            details.Add("tabs=null");
            return string.Join(", ", details);
        }

        details.Add($"tabCount={tabs.Length}");

        var tabDiagnostics = new List<string>(tabs.Length);
        foreach (var tab in tabs)
        {
            if (tab is null)
                continue;

            tabDiagnostics.Add(string.Concat(
                GetPropertyValue<string>(tab, "TabId"),
                ":registered=",
                GetPropertyValue<bool>(tab, "IsRegistered").ToString(),
                ":window=",
                GetNullablePropertyValue(tab, "WindowId") ?? "<null>"));
        }

        details.Add($"tabs=[{string.Join(";", tabDiagnostics)}]");
        return string.Join(", ", details);
    }

    private static async Task<string> DescribeTrustedClickFailureAsync(WebBrowser browser, WebPage page, string buttonId, string clickCountDatasetName, string clickTrustedDatasetName, string eventBufferName)
    {
        var bootstrapDiagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
        var mouseDiagnostics = DescribeCurrentMouseDiagnostics(browser);
        var nativeWindowDiagnostics = ReadPrivateMemberValue(browser, "LastLinuxNativeWindowBoundsDiagnostics");
        var escapedButtonId = EscapeJavaScriptString(buttonId);
        var escapedClickCountDatasetName = EscapeJavaScriptString(clickCountDatasetName);
        var escapedClickTrustedDatasetName = EscapeJavaScriptString(clickTrustedDatasetName);
        var escapedEventBufferName = EscapeJavaScriptString(eventBufferName);

        try
        {
            var url = await page.EvaluateAsync<string>("String(location.href)").ConfigureAwait(false) ?? "<null>";
            var title = await page.EvaluateAsync<string>("String(document.title)").ConfigureAwait(false) ?? "<null>";
            var readyState = await page.EvaluateAsync<string>("String(document.readyState)").ConfigureAwait(false) ?? "<null>";
            var hasFocus = await page.EvaluateAsync<string>("String(document.hasFocus())").ConfigureAwait(false) ?? "<null>";
            var visibilityState = await page.EvaluateAsync<string>("String(document.visibilityState)").ConfigureAwait(false) ?? "<null>";
            var activeElementTag = await page.EvaluateAsync<string>("String(document.activeElement?.tagName ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var activeElementId = await page.EvaluateAsync<string>("String(document.activeElement?.id ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var innerWidth = await page.EvaluateAsync<string>("String(window.innerWidth)").ConfigureAwait(false) ?? "<null>";
            var innerHeight = await page.EvaluateAsync<string>("String(window.innerHeight)").ConfigureAwait(false) ?? "<null>";
            var devicePixelRatio = await page.EvaluateAsync<string>("String(window.devicePixelRatio)").ConfigureAwait(false) ?? "<null>";
            var screenX = await page.EvaluateAsync<string>("String(window.screenX)").ConfigureAwait(false) ?? "<null>";
            var screenY = await page.EvaluateAsync<string>("String(window.screenY)").ConfigureAwait(false) ?? "<null>";
            var mozInnerScreenX = await page.EvaluateAsync<string>("String(typeof window.mozInnerScreenX === 'number' ? window.mozInnerScreenX : null)").ConfigureAwait(false) ?? "<null>";
            var mozInnerScreenY = await page.EvaluateAsync<string>("String(typeof window.mozInnerScreenY === 'number' ? window.mozInnerScreenY : null)").ConfigureAwait(false) ?? "<null>";
            var buttonPresent = await page.EvaluateAsync<string>($"String(document.getElementById('{escapedButtonId}') !== null)").ConfigureAwait(false) ?? "<null>";
            var buttonConnected = await page.EvaluateAsync<string>($"String(document.getElementById('{escapedButtonId}')?.isConnected ?? false)").ConfigureAwait(false) ?? "<null>";
            var buttonDisabled = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); return String(element instanceof HTMLButtonElement ? element.disabled : null); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var buttonRect = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; return rect ? [rect.left, rect.top, rect.width, rect.height].join(',') : '<null>'; }})()")
                .ConfigureAwait(false) ?? "<null>";
            var buttonCenter = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; return rect ? [rect.left + (rect.width / 2), rect.top + (rect.height / 2)].join(',') : '<null>'; }})()")
                .ConfigureAwait(false) ?? "<null>";
            var hitTag = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; if (!rect) {{ return '<null>'; }} return String(document.elementFromPoint(rect.left + (rect.width / 2), rect.top + (rect.height / 2))?.tagName ?? '<null>'); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var hitId = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; if (!rect) {{ return '<null>'; }} return String(document.elementFromPoint(rect.left + (rect.width / 2), rect.top + (rect.height / 2))?.id ?? '<null>'); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var display = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); return String(element instanceof HTMLElement ? getComputedStyle(element).display : '<null>'); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var visibility = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); return String(element instanceof HTMLElement ? getComputedStyle(element).visibility : '<null>'); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var pointerEvents = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); return String(element instanceof HTMLElement ? getComputedStyle(element).pointerEvents : '<null>'); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var hovered = await page.EvaluateAsync<string>($"(() => {{ const element = document.getElementById('{escapedButtonId}'); return String(element instanceof HTMLElement ? element.matches(':hover') : false); }})()")
                .ConfigureAwait(false) ?? "<null>";
            var clickCount = await page.EvaluateAsync<string>($"String(document.documentElement.dataset['{escapedClickCountDatasetName}'] ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var trusted = await page.EvaluateAsync<string>($"String(document.documentElement.dataset['{escapedClickTrustedDatasetName}'] ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var events = await page.EvaluateAsync<string>($"JSON.stringify(Array.isArray(globalThis['{escapedEventBufferName}']) ? globalThis['{escapedEventBufferName}'].slice(-12) : [])")
                .ConfigureAwait(false) ?? "<null>";

            var domDiagnostics = string.Join(
                ";",
                $"url={url}",
                $"title={title}",
                $"readyState={readyState}",
                $"hasFocus={hasFocus}",
                $"visibilityState={visibilityState}",
                $"activeElementTag={activeElementTag}",
                $"activeElementId={activeElementId}",
                $"innerWidth={innerWidth}",
                $"innerHeight={innerHeight}",
                $"devicePixelRatio={devicePixelRatio}",
                $"screenX={screenX}",
                $"screenY={screenY}",
                $"mozInnerScreenX={mozInnerScreenX}",
                $"mozInnerScreenY={mozInnerScreenY}",
                $"buttonPresent={buttonPresent}",
                $"buttonConnected={buttonConnected}",
                $"buttonDisabled={buttonDisabled}",
                $"buttonRect={buttonRect}",
                $"buttonCenter={buttonCenter}",
                $"hitTag={hitTag}",
                $"hitId={hitId}",
                $"display={display}",
                $"visibility={visibility}",
                $"pointerEvents={pointerEvents}",
                $"hovered={hovered}",
                $"clickCount={clickCount}",
                $"trusted={trusted}",
                $"events={events}");

            return $"{bootstrapDiagnostics}, {mouseDiagnostics}, nativeWindowDiscovery={nativeWindowDiagnostics}, trustedClickState={domDiagnostics ?? "<null>"}";
        }
        catch (Exception error)
        {
            return $"{bootstrapDiagnostics}, {mouseDiagnostics}, nativeWindowDiscovery={nativeWindowDiagnostics}, trustedClickStateError={error.Message}";
        }
    }

    private static string EscapeJavaScriptString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static async Task<string> DescribeTrustedTypingFailureAsync(WebBrowser browser, WebPage page)
    {
        var bootstrapDiagnostics = await DescribeBootstrapFailureAsync(browser, page).ConfigureAwait(false);
        var mouseDiagnostics = DescribeCurrentMouseDiagnostics(browser);
        var keyboardDiagnostics = DescribeCurrentKeyboardDiagnostics(browser);

        try
        {
            var url = await page.EvaluateAsync<string>("String(location.href)").ConfigureAwait(false) ?? "<null>";
            var title = await page.EvaluateAsync<string>("String(document.title)").ConfigureAwait(false) ?? "<null>";
            var readyState = await page.EvaluateAsync<string>("String(document.readyState)").ConfigureAwait(false) ?? "<null>";
            var hasFocus = await page.EvaluateAsync<string>("String(document.hasFocus())").ConfigureAwait(false) ?? "<null>";
            var visibilityState = await page.EvaluateAsync<string>("String(document.visibilityState)").ConfigureAwait(false) ?? "<null>";
            var activeElementTag = await page.EvaluateAsync<string>("String(document.activeElement?.tagName ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var activeElementId = await page.EvaluateAsync<string>("String(document.activeElement?.id ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var innerWidth = await page.EvaluateAsync<string>("String(window.innerWidth)").ConfigureAwait(false) ?? "<null>";
            var innerHeight = await page.EvaluateAsync<string>("String(window.innerHeight)").ConfigureAwait(false) ?? "<null>";
            var screenX = await page.EvaluateAsync<string>("String(window.screenX)").ConfigureAwait(false) ?? "<null>";
            var screenY = await page.EvaluateAsync<string>("String(window.screenY)").ConfigureAwait(false) ?? "<null>";
            var inputPresent = await page.EvaluateAsync<string>("String(document.getElementById('humanity-type-input') !== null)").ConfigureAwait(false) ?? "<null>";
            var inputConnected = await page.EvaluateAsync<string>("String(document.getElementById('humanity-type-input')?.isConnected ?? false)").ConfigureAwait(false) ?? "<null>";
            var inputValue = await page.EvaluateAsync<string>("String(document.getElementById('humanity-type-input')?.value ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var inputRect = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; return rect ? [rect.left, rect.top, rect.width, rect.height].join(',') : '<null>'; })()")
                .ConfigureAwait(false) ?? "<null>";
            var inputCenter = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; return rect ? [rect.left + (rect.width / 2), rect.top + (rect.height / 2)].join(',') : '<null>'; })()")
                .ConfigureAwait(false) ?? "<null>";
            var hitTag = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; if (!rect) { return '<null>'; } return String(document.elementFromPoint(rect.left + (rect.width / 2), rect.top + (rect.height / 2))?.tagName ?? '<null>'); })()")
                .ConfigureAwait(false) ?? "<null>";
            var hitId = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); const rect = element instanceof HTMLElement ? element.getBoundingClientRect() : null; if (!rect) { return '<null>'; } return String(document.elementFromPoint(rect.left + (rect.width / 2), rect.top + (rect.height / 2))?.id ?? '<null>'); })()")
                .ConfigureAwait(false) ?? "<null>";
            var hovered = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); return String(element instanceof HTMLElement ? element.matches(':hover') : false); })()")
                .ConfigureAwait(false) ?? "<null>";
            var selectionStart = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); return String(element instanceof HTMLInputElement ? element.selectionStart : null); })()")
                .ConfigureAwait(false) ?? "<null>";
            var selectionEnd = await page.EvaluateAsync<string>("(() => { const element = document.getElementById('humanity-type-input'); return String(element instanceof HTMLInputElement ? element.selectionEnd : null); })()")
                .ConfigureAwait(false) ?? "<null>";
            var keydownCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeKeydownCount ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var keyupCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeKeyupCount ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var inputCount = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeInputCount ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var trustedKeydown = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedKeydown ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var trustedKeyup = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedKeyup ?? '<null>')").ConfigureAwait(false) ?? "<null>";
            var trustedInput = await page.EvaluateAsync<string>("String(document.documentElement.dataset.atomHumanityTypeTrustedInput ?? '<null>')").ConfigureAwait(false) ?? "<null>";

            var domDiagnostics = string.Join(
                ";",
                $"url={url}",
                $"title={title}",
                $"readyState={readyState}",
                $"hasFocus={hasFocus}",
                $"visibilityState={visibilityState}",
                $"activeElementTag={activeElementTag}",
                $"activeElementId={activeElementId}",
                $"innerWidth={innerWidth}",
                $"innerHeight={innerHeight}",
                $"screenX={screenX}",
                $"screenY={screenY}",
                $"inputPresent={inputPresent}",
                $"inputConnected={inputConnected}",
                $"inputValue={inputValue}",
                $"inputRect={inputRect}",
                $"inputCenter={inputCenter}",
                $"hitTag={hitTag}",
                $"hitId={hitId}",
                $"hovered={hovered}",
                $"selectionStart={selectionStart}",
                $"selectionEnd={selectionEnd}",
                $"keydownCount={keydownCount}",
                $"keyupCount={keyupCount}",
                $"inputCount={inputCount}",
                $"trustedKeydown={trustedKeydown}",
                $"trustedKeyup={trustedKeyup}",
                $"trustedInput={trustedInput}");

            return $"{bootstrapDiagnostics}, {mouseDiagnostics}, {keyboardDiagnostics}, trustedTypingState={domDiagnostics}";
        }
        catch (Exception error)
        {
            return $"{bootstrapDiagnostics}, {mouseDiagnostics}, {keyboardDiagnostics}, trustedTypingStateError={error.Message}";
        }
    }

    private static string DescribeCurrentMouseDiagnostics(WebBrowser browser)
    {
        var currentMouse = typeof(WebBrowser)
            .GetProperty("CurrentMouse", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(browser);

        if (currentMouse is null)
            return "mouse=<null>";

        var backend = currentMouse.GetType()
            .GetField("backend", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(currentMouse);

        if (backend is null)
            return $"mouseType={currentMouse.GetType().Name};mouseBackend=<null>";

        return string.Join(
            ";",
            $"mouseType={currentMouse.GetType().Name}",
            $"mouseBackend={backend.GetType().Name}",
            $"mouseLastMove={ReadPrivateMemberValue(backend, "LastAbsoluteMovePosition")}",
            $"mouseBeforeButtonDown={ReadPrivateMemberValue(backend, "LastPointerPositionBeforeButtonDown")}",
            $"mouseChildWindowBeforeButtonDown={ReadPrivateMemberValue(backend, "LastPointerChildWindowBeforeButtonDown")}",
            $"mouseChildPositionBeforeButtonDown={ReadPrivateMemberValue(backend, "LastPointerPositionInChildWindowBeforeButtonDown")}",
            $"mouseAfterButtonUp={ReadPrivateMemberValue(backend, "LastPointerPositionAfterButtonUp")}");
    }

    private static string DescribeCurrentKeyboardDiagnostics(WebBrowser browser)
    {
        var currentKeyboard = typeof(WebBrowser)
            .GetProperty("CurrentKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(browser);

        if (currentKeyboard is null)
            return "keyboard=<null>";

        var backend = currentKeyboard.GetType()
            .GetField("backend", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(currentKeyboard);

        if (backend is null)
            return $"keyboardType={currentKeyboard.GetType().Name};keyboardBackend=<null>";

        return string.Join(
            ";",
            $"keyboardType={currentKeyboard.GetType().Name}",
            $"keyboardBackend={backend.GetType().Name}",
            $"keyboardLastFocusStrategy={ReadPrivateMemberValue(backend, "LastFocusStrategy")}",
            $"keyboardLastPointerChildWindow={ReadPrivateMemberValue(backend, "LastPointerChildWindow")}",
            $"keyboardLastExistingFocusWindow={ReadPrivateMemberValue(backend, "LastExistingFocusWindow")}",
            $"keyboardLastAssignedFocusWindow={ReadPrivateMemberValue(backend, "LastAssignedFocusWindow")}");
    }

    private static string ReadPrivateMemberValue(object target, string memberName)
    {
        var type = target.GetType();
        var value = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
            ?? type.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);
        return value?.ToString() ?? "<null>";
    }

    private static Point? EstimateScreenPoint(Rectangle? windowBounds, Size? viewportSize, Rectangle? buttonBounds)
    {
        if (windowBounds is not Rectangle bounds || buttonBounds is not Rectangle elementBounds)
            return null;

        var chromeLeft = viewportSize is { Width: > 0 }
            ? Math.Max(0, (bounds.Width - viewportSize.Value.Width) / 2)
            : 0;
        var chromeTop = viewportSize is { Height: > 0 }
            ? Math.Max(0, bounds.Height - viewportSize.Value.Height)
            : 0;
        var centerX = elementBounds.Left + (elementBounds.Width / 2.0);
        var centerY = elementBounds.Top + (elementBounds.Height / 2.0);

        return new Point(
            (int)Math.Round(bounds.X + chromeLeft + centerX),
            (int)Math.Round(bounds.Y + chromeTop + centerY));
    }

    private static async ValueTask<T> InvokePrivateValueTaskAsync<T>(object target, string methodName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CancellationToken)]);
        Assert.That(method, Is.Not.Null, $"Не найден private/internal method {methodName}(CancellationToken) on {target.GetType().Name}.");

        var result = method!.Invoke(target, [CancellationToken.None]);
        return result switch
        {
            ValueTask<T> valueTask => await valueTask.ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Method {target.GetType().Name}.{methodName} did not return ValueTask<{typeof(T).Name}>."),
        };
    }


    private static void IgnoreIfChromiumMv3BlockingWebRequestUnsupported(WebBrowser browser, string scenario)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        var launchSettings = WebDriverTestEnvironment.GetLaunchSettings(browser);
        var profile = launchSettings.Profile;
        if (profile is not ChromeProfile || profile is FirefoxProfile)
            return;

        var manifestPath = profile.Path is { Length: > 0 } profilePath
            ? Path.Combine(profilePath, "profile.json")
            : null;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("bridge", out var bridgeElement))
            return;

        string? installMode = null;
        if (bridgeElement.TryGetProperty("strategy", out var strategyElement)
            && strategyElement.TryGetProperty("installMode", out var installModeElement))
        {
            installMode = installModeElement.GetString();
        }

        string? managedPolicyStatus = null;
        string? managedPolicyDetail = null;
        if (bridgeElement.TryGetProperty("managedPolicyDiagnostics", out var managedPolicyElement))
        {
            if (managedPolicyElement.TryGetProperty("status", out var statusElement))
                managedPolicyStatus = statusElement.GetString();

            if (managedPolicyElement.TryGetProperty("detail", out var detailElement))
                managedPolicyDetail = detailElement.GetString();
        }

        var profileSeeded = string.Equals(installMode, ChromiumBootstrapInstallMode.ProfileSeeded.ToString(), StringComparison.Ordinal);
        var missingManagedPolicy = string.Equals(managedPolicyStatus, "profile-local", StringComparison.Ordinal)
            || string.Equals(managedPolicyStatus, "system-publish-required", StringComparison.Ordinal);
        if (!profileSeeded && !missingManagedPolicy)
            return;

        var browserName = profile.GetType().Name.Replace("Profile", string.Empty, StringComparison.Ordinal);
        var message = $"{scenario} requires a managed-policy Chromium extension install on Linux for MV3 webRequest parity; browser={browserName}, bridgeInstallMode={installMode ?? "<unknown>"}, managedPolicyStatus={managedPolicyStatus ?? "<unknown>"}";
        if (!string.IsNullOrWhiteSpace(managedPolicyDetail))
            message = string.Concat(message, ", managedPolicyDetail=", managedPolicyDetail);

        Assert.Ignore(message);
    }
    private sealed class RealBrowserTrustedInputSession(WebBrowser browser, VirtualDisplay? display) : IAsyncDisposable
    {
        internal WebBrowser Browser { get; } = browser;

        internal static async ValueTask<RealBrowserTrustedInputSession> CreateAsync()
        {
            VirtualDisplay? display = null;

            try
            {
                if (OperatingSystem.IsLinux())
                {
                    display = await CreateHiddenVirtualDisplayOrIgnoreAsync().ConfigureAwait(false);
                }

                var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
                {
                    Display = display,
                    UseHeadlessMode = OperatingSystem.IsLinux(),
                }).ConfigureAwait(false);

                return new RealBrowserTrustedInputSession(browser, display);
            }
            catch
            {
                if (OperatingSystem.IsLinux() && display is not null)
                    await display.DisposeAsync().ConfigureAwait(false);

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync().ConfigureAwait(false);

            if (OperatingSystem.IsLinux() && display is not null)
                await display.DisposeAsync().ConfigureAwait(false);
        }
    }

    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualDisplay> CreateHiddenVirtualDisplayOrIgnoreAsync()
    {
        try
        {
            return await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
            {
                IsVisible = false,
            }).ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex)
        {
            Assert.Ignore("Virtual display backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    private static void AppendExtensionDebugEventDiagnostics(List<string> details, object bridgeServer)
    {
        var method = bridgeServer.GetType().GetMethod("GetExtensionDebugEventsSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.Invoke(bridgeServer, null) is not string[] events || events.Length == 0)
            return;

        var recentEvents = events.Length <= 12 ? events : events[^12..];
        details.Add($"extensionDebugEvents=[{string.Join(" || ", recentEvents)}]");
    }

    private static void AppendProfileManifestDiagnostics(List<string> details, string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        if (document.RootElement.TryGetProperty("browser", out var browserElement)
            && browserElement.TryGetProperty("channel", out var channelElement)
            && channelElement.GetString() is { Length: > 0 } channel)
        {
            details.Add($"browserChannel={channel}");
        }

        if (!document.RootElement.TryGetProperty("bridge", out var bridgeElement))
            return;

        if (bridgeElement.TryGetProperty("strategy", out var strategyElement))
        {
            if (strategyElement.TryGetProperty("installMode", out var installModeElement)
                && installModeElement.GetString() is { Length: > 0 } installMode)
            {
                details.Add($"bridgeInstallMode={installMode}");
            }

            if (strategyElement.TryGetProperty("transportMode", out var transportModeElement)
                && transportModeElement.GetString() is { Length: > 0 } transportMode)
            {
                details.Add($"bridgeTransportMode={transportMode}");
            }
        }

        if (bridgeElement.TryGetProperty("transportUrl", out var transportUrlElement)
            && transportUrlElement.GetString() is { Length: > 0 } transportUrl)
        {
            details.Add($"transportUrlScheme={(Uri.TryCreate(transportUrl, UriKind.Absolute, out var transportUri) ? transportUri.Scheme : "invalid")}");
        }

        if (bridgeElement.TryGetProperty("managedPolicyDiagnostics", out var managedPolicyElement))
        {
            if (managedPolicyElement.TryGetProperty("status", out var policyStatusElement)
                && policyStatusElement.GetString() is { Length: > 0 } policyStatus)
            {
                details.Add($"managedPolicyStatus={policyStatus}");
            }

            if (managedPolicyElement.TryGetProperty("detail", out var policyDetailElement)
                && policyDetailElement.GetString() is { Length: > 0 } policyDetail)
            {
                details.Add($"managedPolicyDetail={policyDetail}");
            }
        }

        if (bridgeElement.TryGetProperty("managedDeliveryTrust", out var trustElement))
        {
            if (trustElement.TryGetProperty("status", out var trustStatusElement)
                && trustStatusElement.GetString() is { Length: > 0 } trustStatus)
            {
                details.Add($"managedDeliveryTrustStatus={trustStatus}");
            }

            if (trustElement.TryGetProperty("detail", out var trustDetailElement)
                && trustDetailElement.GetString() is { Length: > 0 } trustDetail)
            {
                details.Add($"managedDeliveryTrustDetail={trustDetail}");
            }
        }
    }

    private static bool IsBridgeCommandsBound(WebPage currentPage)
        => currentPage.GetType().GetProperty("BridgeCommands", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(currentPage) is not null;

    private static string? TryReadBridgeSessionId(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("bridge", out var bridgeElement))
            return null;

        return bridgeElement.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
    }

    private static async Task<object?> InvokeInternalValueTaskAsync(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);

        dynamic valueTask = method.Invoke(target, arguments)
            ?? throw new InvalidOperationException($"Вызов '{methodName}' не вернул ValueTask результата");

        return await valueTask.AsTask().ConfigureAwait(false);
    }

    private static T GetPropertyValue<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);

        return (T)(property.GetValue(target)
            ?? throw new InvalidOperationException($"Свойство '{propertyName}' вернуло null"));
    }

    private static string? GetNullablePropertyValue(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);

        return property.GetValue(target) as string;
    }

    private static List<RealBrowserFrameExecutionSnapshot> ReadFrameExecutionSnapshots(JsonElement? payload)
    {
        if (payload is not JsonElement array || array.ValueKind != JsonValueKind.Array)
            return [];

        var snapshots = new List<RealBrowserFrameExecutionSnapshot>();
        foreach (var item in array.EnumerateArray())
        {
            var ordinal = item.TryGetProperty("ordinal", out var ordinalElement) && ordinalElement.TryGetInt32(out var ordinalValue)
                ? ordinalValue
                : snapshots.Count;
            var frameId = item.TryGetProperty("frameId", out var frameIdElement) && frameIdElement.TryGetInt32(out var frameIdValue)
                ? frameIdValue
                : (int?)null;
            var parentFrameId = item.TryGetProperty("parentFrameId", out var parentFrameIdElement) && parentFrameIdElement.TryGetInt32(out var parentFrameIdValue)
                ? parentFrameIdValue
                : (int?)null;
            var url = item.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString()
                : null;
            var status = item.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString()
                : null;
            var error = item.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            var value = item.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString()
                : null;

            string? frameElementId = null;
            string? frameKind = null;
            string? marker = null;
            string? title = null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    using var document = JsonDocument.Parse(value);
                    var root = document.RootElement;
                    if (root.TryGetProperty("frameElementId", out var frameElementIdElement) && frameElementIdElement.ValueKind == JsonValueKind.String)
                        frameElementId = frameElementIdElement.GetString();

                    if (root.TryGetProperty("frameKind", out var frameKindElement) && frameKindElement.ValueKind == JsonValueKind.String)
                        frameKind = frameKindElement.GetString();

                    if (root.TryGetProperty("marker", out var markerElement) && markerElement.ValueKind == JsonValueKind.String)
                        marker = markerElement.GetString();

                    if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                        title = titleElement.GetString();
                }
                catch (JsonException)
                {
                    // Ignore non-JSON frame return values; the test only inspects structured snapshots.
                }
            }

            snapshots.Add(new RealBrowserFrameExecutionSnapshot(ordinal, frameId, parentFrameId, url, status, error, frameElementId, frameKind, marker, title));
        }

        return snapshots;
    }

    private static async Task<BridgeServer> StartBoundBridgeAsync(WebPage page)
    {
        var server = new BridgeServer(BridgeTestHelpers.CreateSettings());

        try
        {
            await server.StartAsync().ConfigureAwait(false);
            page.BindBridgeCommands("session-a", server.Commands);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<ClientWebSocket> ConnectBridgeSocketAsync(BridgeServer server, string tabId)
    {
        var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
            await BridgeTestHelpers.SendHandshakeAsync(socket, BridgeTestHelpers.CreateClientPayload()).ConfigureAwait(false);

            var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(response.Status, Is.EqualTo(BridgeStatus.Ok));

            var acceptPayload = response.Payload?.Deserialize(BridgeJsonContext.Default.BridgeHandshakeAcceptPayload);

            Assert.That(acceptPayload, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(acceptPayload!.SessionId, Is.EqualTo("session-a"));
                Assert.That(acceptPayload.NegotiatedProtocolVersion, Is.EqualTo(BridgeHandshakeValidator.CurrentProtocolVersion));
                Assert.That(acceptPayload.RequestTimeoutMs, Is.EqualTo((int)BridgeTestHelpers.CreateSettings().RequestTimeout.TotalMilliseconds));
            });

            await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);
            await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: tabId, windowId: "window-1")).ConfigureAwait(false);
            _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<BridgeMessage> RespondToBridgeCommandAsync(ClientWebSocket socket, BridgeCommand command, string tabId, string payloadJson)
    {
        var request = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Command, Is.EqualTo(command));
        Assert.That(request.TabId, Is.EqualTo(tabId));

        using var payload = JsonDocument.Parse(payloadJson);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = request.Id,
            Type = BridgeMessageType.Response,
            TabId = tabId,
            Status = BridgeStatus.Ok,
            Payload = payload.RootElement.Clone(),
        }).ConfigureAwait(false);

        return request;
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record RealBrowserFrameExecutionSnapshot(
        int Ordinal,
        int? FrameId,
        int? ParentFrameId,
        string? Url,
        string? Status,
        string? Error,
        string? FrameElementId,
        string? FrameKind,
        string? Marker,
        string? Title);
}