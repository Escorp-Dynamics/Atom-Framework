using System.Net;

namespace Atom.Web.Browsers.Tests;

[TestFixture]
public class WebBrowserTests
{
    [Test]
    public async Task BaseTest()
    {
        await using var browser = new WebBrowser();
        var statusCode = await browser.GoToAsync(new Uri("link"));

        Assert.Multiple(() =>
        {
            Assert.That(statusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(browser.Source, Is.Not.Empty);
        });
    }
}