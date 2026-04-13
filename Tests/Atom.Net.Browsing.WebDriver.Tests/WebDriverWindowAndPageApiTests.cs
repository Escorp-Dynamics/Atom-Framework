using System.Drawing;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverWindowAndPageApiTests
{
    [Test]
    public void BrowserWindowAndPageLookupSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.Windows));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.CurrentWindow));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.Pages));
            PublicApiAssert.RequireProperty(browser, nameof(WebBrowser.CurrentPage));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(IElement), nameof(CancellationToken));

            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(IElement), nameof(CancellationToken));
        });
    }

    [Test]
    public void BrowserOpenWindowSurfaceSpecTest()
    {
        var browser = typeof(WebBrowser);

        Assert.Multiple(() =>
        {
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync), nameof(WebWindowSettings)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync), nameof(CancellationToken)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync), nameof(WebWindowSettings), nameof(CancellationToken)), nameof(WebWindow));
        });
    }

    [Test]
    public void WindowSurfaceSpecTest()
    {
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.Pages));
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.CurrentPage));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(IElement), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetUrlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetTitleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetBoundingBoxAsync), nameof(CancellationToken));
        });
    }

    [Test]
    public void WindowAndBrowserLookupReturnTypeSpecTest()
    {
        var browser = typeof(WebBrowser);
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(String), nameof(CancellationToken)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(Uri), nameof(CancellationToken)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetWindowAsync), nameof(IElement), nameof(CancellationToken)), nameof(WebWindow));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(String), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(Uri), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.GetPageAsync), nameof(IElement), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(String), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(Uri), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetPageAsync), nameof(IElement), nameof(CancellationToken)), nameof(WebPage));
            PublicApiAssert.AssertReturnTypeContains(PublicApiAssert.RequireMethod(window, nameof(WebWindow.GetBoundingBoxAsync), nameof(CancellationToken)), nameof(Rectangle));
        });
    }

    [Test]
    public void PageInspectionSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetUrlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetTitleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetContentAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetScreenshotAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.IsVisibleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetViewportSizeAsync), nameof(CancellationToken));
        });
    }
}