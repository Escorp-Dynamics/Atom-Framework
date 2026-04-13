using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverPageDomBehaviorTests
{
    [Test]
    public async Task PageDomQueriesResolveCurrentDocumentAcrossOverloads()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(new Uri("https://127.0.0.1/page-dom-query"), new NavigationSettings
        {
            Html = """
                <html>
                <head><title>DOM Query</title></head>
                <body>
                  <div id="marker" class="item primary">alpha</div>
                  <div id="secondary" class="item">beta</div>
                </body>
                </html>
                """,
        }).ConfigureAwait(false);

        var waitedByString = await page.WaitForElementAsync("#marker").ConfigureAwait(false);
        var waitedByTimeout = await page.WaitForElementAsync("#marker", TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var waitedByKind = await page.WaitForElementAsync("#marker", WaitForElementKind.Attached).ConfigureAwait(false);
        var waitedBySettings = await page.WaitForElementAsync(new WaitForElementSettings
        {
            Selector = ElementSelector.Css("#marker"),
            Timeout = TimeSpan.FromSeconds(1),
        }).ConfigureAwait(false);
        var waitedBySelector = await page.WaitForElementAsync(
            ElementSelector.Css("#marker"),
            WaitForElementKind.Attached,
            TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var missing = await page.WaitForElementAsync("#missing", TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);

        var elementByString = await page.GetElementAsync("#marker").ConfigureAwait(false);
        var elementBySelector = await page.GetElementAsync(ElementSelector.Css("#marker")).ConfigureAwait(false);
        var elementByCssSelector = await page.GetElementAsync(new CssSelector("#marker")).ConfigureAwait(false);
        var elementsByString = (await page.GetElementsAsync(".item").ConfigureAwait(false)).ToArray();
        var elementsBySelector = (await page.GetElementsAsync(ElementSelector.Css(".item")).ConfigureAwait(false)).ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(waitedByString, Is.Not.Null);
            Assert.That(waitedByTimeout, Is.Not.Null);
            Assert.That(waitedByKind, Is.Not.Null);
            Assert.That(waitedBySettings, Is.Not.Null);
            Assert.That(waitedBySelector, Is.Not.Null);
            Assert.That(missing, Is.Null);

            Assert.That(elementByString, Is.Not.Null);
            Assert.That(elementBySelector, Is.Not.Null);
            Assert.That(elementByCssSelector, Is.Not.Null);
            Assert.That(elementsByString, Has.Length.EqualTo(2));
            Assert.That(elementsBySelector, Has.Length.EqualTo(2));

            Assert.That(await waitedByString!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(await waitedByTimeout!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(await waitedByKind!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(await waitedBySettings!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(await waitedBySelector!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(await elementByString!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("alpha"));
            Assert.That(await elementBySelector!.GetAttributeAsync("class").ConfigureAwait(false), Does.Contain("item"));
            Assert.That(await elementByCssSelector!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
        });
    }

    [Test]
    public async Task PageDomQueriesResolveNonCssStrategiesAgainstTransportSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(new Uri("https://127.0.0.1/page-dom-query-strategies"), new NavigationSettings
        {
            Html = """
                <html>
                <head><title>DOM Query Strategies</title></head>
                <body>
                  <form id="login-form">
                    <input id="login" name="username" class="field primary" value="alice" />
                    <input id="password" name="password" class="field" value="secret" />
                    <span class="status">ready</span>
                  </form>
                </body>
                </html>
                """,
        }).ConfigureAwait(false);

        var elementById = await page.GetElementAsync(new ElementSelector(ElementSelectorStrategy.Id, "login")).ConfigureAwait(false);
        var elementByName = await page.GetElementAsync(new ElementSelector(ElementSelectorStrategy.Name, "username")).ConfigureAwait(false);
        var elementByTag = await page.GetElementAsync(new ElementSelector(ElementSelectorStrategy.TagName, "form")).ConfigureAwait(false);
        var elementByText = await page.GetElementAsync(new ElementSelector(ElementSelectorStrategy.Text, "ready")).ConfigureAwait(false);
        var waitedByTag = await page.WaitForElementAsync(new ElementSelector(ElementSelectorStrategy.TagName, "form"), TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var inputsByTag = (await page.GetElementsAsync(new ElementSelector(ElementSelectorStrategy.TagName, "input")).ConfigureAwait(false)).ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(elementById, Is.Not.Null);
            Assert.That(elementByName, Is.Not.Null);
            Assert.That(elementByTag, Is.Not.Null);
            Assert.That(elementByText, Is.Not.Null);
            Assert.That(waitedByTag, Is.Not.Null);
            Assert.That(inputsByTag, Has.Length.EqualTo(2));

            Assert.That(await elementById!.GetValueAsync().ConfigureAwait(false), Is.EqualTo("alice"));
            Assert.That((await elementByName!.GetClassListAsync().ConfigureAwait(false)).ToArray(), Does.Contain("primary"));
            Assert.That(await elementByTag!.GetElementPathAsync().ConfigureAwait(false), Is.EqualTo("/html[1]/body[1]/form[1]"));
            Assert.That(await elementByText!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("ready"));
            Assert.That(await waitedByTag!.GetAttributeAsync("id").ConfigureAwait(false), Is.EqualTo("login-form"));
        });
    }

    [Test]
    public async Task SyntheticTransportElementTraversalResolvesParentChildrenAndSiblings()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(new Uri("https://127.0.0.1/page-dom-traversal"), new NavigationSettings
        {
            Html = """
                                <html>
                                <head><title>DOM Traversal</title></head>
                                <body>
                                    <section id="root">
                                        <div id="row">
                                            <span id="first" data-role="primary">alpha</span>
                                            <span id="second">beta</span>
                                        </div>
                                        <p id="tail">tail</p>
                                    </section>
                                </body>
                                </html>
                                """,
        }).ConfigureAwait(false);

        var row = await page.GetElementAsync("#row").ConfigureAwait(false);
        var first = await page.GetElementAsync("#first").ConfigureAwait(false);
        var second = await page.GetElementAsync("#second").ConfigureAwait(false);

        Assert.That(row, Is.Not.Null);
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);

        var parent = await first!.GetParentElementAsync().ConfigureAwait(false);
        var rowChildren = (await row!.GetChildElementsAsync().ConfigureAwait(false)).ToArray();
        var firstSiblings = (await first.GetSiblingElementsAsync().ConfigureAwait(false)).ToArray();
        var secondSiblings = (await second!.GetSiblingElementsAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(parent, Is.Not.Null);
            Assert.That(await parent!.GetIdAsync().ConfigureAwait(false), Is.EqualTo("row"));
            Assert.That(await first.GetElementPathAsync().ConfigureAwait(false), Is.EqualTo("/html[1]/body[1]/section[1]/div[1]/span[1]"));
            Assert.That(await row.GetElementPathAsync().ConfigureAwait(false), Is.EqualTo("/html[1]/body[1]/section[1]/div[1]"));

            Assert.That(rowChildren, Has.Length.EqualTo(2));
            Assert.That(await rowChildren[0].GetIdAsync().ConfigureAwait(false), Is.EqualTo("first"));
            Assert.That(await rowChildren[1].GetIdAsync().ConfigureAwait(false), Is.EqualTo("second"));

            Assert.That(firstSiblings, Has.Length.EqualTo(1));
            Assert.That(await firstSiblings[0].GetIdAsync().ConfigureAwait(false), Is.EqualTo("second"));

            Assert.That(secondSiblings, Has.Length.EqualTo(1));
            Assert.That(await secondSiblings[0].GetIdAsync().ConfigureAwait(false), Is.EqualTo("first"));
            Assert.That(await first.GetCustomDataAsync("role").ConfigureAwait(false), Is.EqualTo("primary"));
        });
    }

    [Test]
    public async Task SyntheticTransportDoesNotSynthesizeShadowRootFromMarkupSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(new Uri("https://127.0.0.1/page-dom-shadow-gap"), new NavigationSettings
        {
            Html = """
                <html>
                <head><title>Shadow Gap</title></head>
                <body>
                  <div id="host">shadow host placeholder</div>
                </body>
                </html>
                """,
        }).ConfigureAwait(false);

        var host = await page.GetElementAsync("#host").ConfigureAwait(false);
        var pageShadowRoot = await page.GetShadowRootAsync("#host").ConfigureAwait(false);
        var elementShadowRoot = await host!.GetShadowRootAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(host, Is.Not.Null);
            Assert.That(pageShadowRoot, Is.Null);
            Assert.That(elementShadowRoot, Is.Null);
        });
    }
}