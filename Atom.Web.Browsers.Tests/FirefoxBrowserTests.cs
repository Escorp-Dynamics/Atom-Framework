namespace Atom.Web.Browsers.Firefox.Tests;

public class FirefoxBrowserTests
{
    [Fact]
    public async Task BasicTest()
    {
        await using var browser = new FirefoxBrowser();
        var window = await browser.OpenWindowAsync();

        Assert.NotNull(window);
    }
}