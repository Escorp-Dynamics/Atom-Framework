using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverBrowserApiSurfaceTests
{
    [Test]
    public void BrowserCoreSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.IsDisposed));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.Windows));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.Pages));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.CurrentWindow));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.CurrentPage));
        });
    }

    [Test]
    public void BrowserInterceptionAndEventSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.Console));
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.Request));
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.Response));
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.DomContentLoaded));
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.NavigationCompleted));
            PublicApiAssert.RequireEvent(browser, nameof(WebBrowser.PageLoaded));

            PublicApiAssert.RequireMethod(
                browser,
                nameof(WebBrowser.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>),
                nameof(CancellationToken));
            PublicApiAssert.RequireMethod(
                browser,
                nameof(WebBrowser.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>));
            PublicApiAssert.RequireMethod(
                browser,
                nameof(WebBrowser.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(CancellationToken));
            PublicApiAssert.RequireMethod(
                browser,
                nameof(WebBrowser.SetRequestInterceptionAsync),
                nameof(Boolean));
        });
    }

    [Test]
    public void BrowserMediaSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.AttachVirtualCameraAsync), nameof(VirtualCamera), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.AttachVirtualCameraAsync), nameof(VirtualCamera));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.AttachVirtualMicrophoneAsync), nameof(VirtualMicrophone), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.AttachVirtualMicrophoneAsync), nameof(VirtualMicrophone));
        });
    }

    [Test]
    public void BrowserNavigationAndCookieSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.ClearAllCookiesAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.ClearAllCookiesAsync));

            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(NavigationKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(NavigationSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(IReadOnlyDictionary<string, string>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(ReadOnlyMemory<byte>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.NavigateAsync), nameof(Uri), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.ReloadAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.ReloadAsync));
        });
    }
}