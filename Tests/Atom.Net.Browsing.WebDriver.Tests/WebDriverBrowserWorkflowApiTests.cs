using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverBrowserWorkflowApiTests
{
    [Test]
    public void BrowserWorkflowSpecTest()
    {
        var browser = typeof(WebBrowser);
        var window = typeof(WebWindow);
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.LaunchAsync), nameof(WebBrowserSettings), nameof(CancellationToken));

            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(browser, nameof(WebBrowser.OpenWindowAsync), nameof(WebWindowSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.OpenPageAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.OpenPageAsync), nameof(WebPageSettings), nameof(CancellationToken));
        });
    }
}