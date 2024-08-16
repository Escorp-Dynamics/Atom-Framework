namespace Atom.Web.Browsers.Firefox.Tests;

public class FirefoxBrowserTests
{
    [Fact]
    public async Task BasicTest()
    {
        var settings = new FirefoxSettings
        {
            AdminPassword = "@1b2C3d4@",
            IsJavaScriptConsoleEnabled = true,
        };

        await using var browser = new FirefoxBrowser(settings);
        await using var window = await browser.OpenWindowAsync();
        Assert.NotNull(window);

        await Task.Delay(TimeSpan.FromMinutes(1));
    }
}