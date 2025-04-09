using System.Net;
using Atom.Web.Browsing.Drivers.Firefox;

namespace Atom.Web.Browsing.Tests;

public class WebBrowserTests(ILogger logger) : BenchmarkTests<WebBrowserTests>(logger)
{
    public WebBrowserTests() : this(ConsoleLogger.Unicode) { }

    [Test]
    public async Task BaseTest()
    {
        await using var browser = new WebBrowser();
        var statusCode = await browser.GoToAsync(new Uri("link"));

        if (!IsBenchmarkEnabled)
        {
            Assert.Multiple(() =>
            {
                Assert.That(statusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(browser.Source, Is.Not.Empty);
            });
        }
    }

    [Test]
    public async Task FirefoxBaseTest()
    {
        var settings = FirefoxDriverSettings.Default;
        settings.Logger = Debug.Logging.Logger.Factory.CreateLogger("firefox-driver");

        await using var browser = new FirefoxDriver(settings);
        var statusCode = await browser.GoToAsync(new Uri("https://www.google.com/"));

        if (!IsBenchmarkEnabled)
        {
            Assert.Multiple(() =>
            {
                Assert.That(statusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(browser.Source, Is.Not.Empty);
            });
        }
    }

    [Test]
    public async Task FirefoxBaseWithContextTest()
    {
        var settings = FirefoxDriverSettings.Default;
        settings.Logger = Debug.Logging.Logger.Factory.CreateLogger("firefox-driver");

        await using var browser = new FirefoxDriver(settings);
        await using var context = await browser.CreateContextAsync();

        var statusCode = await context.GoToAsync(new Uri("https://www.google.com/"));

        if (!IsBenchmarkEnabled)
        {
            Assert.Multiple(() =>
            {
                Assert.That(statusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(browser.Source, Is.Not.Empty);
            });
        }
    }
}