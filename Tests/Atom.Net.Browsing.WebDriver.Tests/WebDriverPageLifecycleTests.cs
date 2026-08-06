using System.Drawing;
using System.Net;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverPageLifecycleTests
{
    [Test]
    public async Task CloseAsyncMarksPageDisposedAndRemovesItFromWindow()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var window = (WebWindow)browser.CurrentWindow;
        var openedPage = (WebPage)await window.OpenPageAsync();
        var initialPage = (WebPage)window.CurrentPage;

        await openedPage.CloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(openedPage.IsDisposed, Is.True);
            Assert.That(window.Pages, Does.Not.Contain(openedPage));
            Assert.That(window.Pages, Does.Contain(initialPage));
        });
    }

    [Test]
    public async Task CloseAsyncOnCurrentPageSwitchesWindowToAnotherLivePage()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var window = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)window.CurrentPage;
        var secondPage = (WebPage)await window.OpenPageAsync();

        await secondPage.CloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(secondPage.IsDisposed, Is.True);
            Assert.That(window.CurrentPage, Is.EqualTo(firstPage));
        });
    }

    [Test]
    public async Task ReconfigureAsyncRefreshesResolvedDeviceWithoutReopeningTab()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.MacBookPro14,
        });

        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var originalTabId = page.TabId;

        await page.ReconfigureAsync(new WebPageSettings
        {
            Device = Device.Pixel2,
        });

        var viewport = await page.GetViewportSizeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.IsDisposed, Is.False);
            Assert.That(page.TabId, Is.EqualTo(originalTabId));
            Assert.That(page.ResolvedDevice, Is.Not.Null);
            Assert.That(page.ResolvedDevice!.Locale, Is.EqualTo(Device.Pixel2.Locale));
            Assert.That(page.ResolvedDevice.ViewportSize, Is.EqualTo(Device.Pixel2.ViewportSize));
            Assert.That(viewport, Is.EqualTo(Device.Pixel2.ViewportSize));
        });
    }

    [Test]
    public async Task ReconfigureAsyncSnapshotsProvidedSettingsAndIgnoresExternalMutations()
    {
        var scopedDevice = Device.Pixel2;

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var page = (WebPage)browser.CurrentWindow.CurrentPage;

        await page.ReconfigureAsync(new WebPageSettings
        {
            Device = scopedDevice,
        });

        scopedDevice.Locale = "fr-FR";
        scopedDevice.ViewportSize = new Size(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(page.ResolvedDevice, Is.Not.Null);
            Assert.That(page.ResolvedDevice!.Locale, Is.EqualTo(Device.Pixel2.Locale));
            Assert.That(page.ResolvedDevice.ViewportSize, Is.EqualTo(Device.Pixel2.ViewportSize));
        });
    }

    [Test]
    public async Task ReconfigureAsyncRefreshesProxyAndFingerprintSettings()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var page = (WebPage)browser.CurrentWindow.CurrentPage;
        var proxyUri = new Uri("http://127.0.0.1:3128");

        await page.ReconfigureAsync(new WebPageSettings
        {
            Proxy = new WebProxy(proxyUri),
            UseProxy = true,
            Device = Device.iPhone14Pro,
        });

        Assert.Multiple(() =>
        {
            // Прокси: новое значение подхватывается из обновлённых настроек.
            Assert.That(page.Settings, Is.Not.Null);
            Assert.That(page.Settings!.Proxy, Is.Not.Null);
            Assert.That(((WebProxy)page.Settings.Proxy!).Address, Is.EqualTo(proxyUri));
            Assert.That(page.Settings.UseProxy, Is.True);

            // Фингерпринт: пересчитывается из новых настроек без переоткрытия вкладки.
            Assert.That(page.IsDisposed, Is.False);
            Assert.That(page.ResolvedDevice, Is.Not.Null);
            Assert.That(page.ResolvedDevice!.UserAgent, Is.EqualTo(Device.iPhone14Pro.UserAgent));
            Assert.That(page.ResolvedDevice.ViewportSize, Is.EqualTo(Device.iPhone14Pro.ViewportSize));
        });
    }

    [Test]
    public async Task ReconfigureAsyncAfterCloseThrowsObjectDisposed()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var page = (WebPage)browser.CurrentWindow.CurrentPage;

        await page.CloseAsync();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await page.ReconfigureAsync(new WebPageSettings { Device = Device.Pixel2 }));
    }
}
