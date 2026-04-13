using System.Net;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverPageApiSurfaceTests
{
    [Test]
    public void PageCoreSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(page, nameof(WebPage.Window));
            PublicApiAssert.RequireProperty(page, nameof(WebPage.MainFrame));
            PublicApiAssert.RequireProperty(page, nameof(WebPage.WaitingTimeout));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetUrlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetTitleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetContentAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetScreenshotAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.IsVisibleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetViewportSizeAsync), nameof(CancellationToken));
        });
    }

    [Test]
    public void PageDomQuerySurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(String), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(WaitForElementSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(ElementSelector), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));

            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementAsync), "CssSelector", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementsAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementsAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetElementsAsync), "CssSelector", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetShadowRootAsync), nameof(String), nameof(CancellationToken));
        });
    }

    [Test]
    public void PageScriptFrameAndCallbackSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(page, nameof(WebPage.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireGenericMethod(page, nameof(WebPage.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.InjectScriptAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.InjectScriptAsync), nameof(String), nameof(Boolean), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.InjectScriptLinkAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.SubscribeAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.UnSubscribeAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetFrameAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetFrameAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.GetFrameAsync), nameof(IElement), nameof(CancellationToken));
        });
    }

    [Test]
    public void PageMediaSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(page, nameof(WebPage.AttachVirtualCameraAsync), nameof(VirtualCamera), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.AttachVirtualMicrophoneAsync), nameof(VirtualMicrophone), nameof(CancellationToken));
        });
    }

    [Test]
    public void PageInterceptionSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            var full = PublicApiAssert.RequireMethod(
                page,
                nameof(WebPage.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>),
                nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(full, nameof(ValueTask));

            PublicApiAssert.RequireMethod(
                page,
                nameof(WebPage.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>));
            PublicApiAssert.RequireMethod(
                page,
                nameof(WebPage.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(CancellationToken));
            PublicApiAssert.RequireMethod(
                page,
                nameof(WebPage.SetRequestInterceptionAsync),
                nameof(Boolean));
        });
    }

    [Test]
    public void PageEventSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireEvent(page, nameof(WebPage.Console));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.Request));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.Response));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.Callback));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.DomContentLoaded));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.NavigationCompleted));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.PageLoaded));
            PublicApiAssert.RequireEvent(page, nameof(WebPage.CallbackFinalized));
        });
    }

    [Test]
    public void PageNavigationSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            var navigate = PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(navigate, nameof(HttpsResponseMessage));

            PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(NavigationKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(IReadOnlyDictionary<string, string>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(ReadOnlyMemory<byte>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.NavigateAsync), nameof(Uri), nameof(NavigationSettings), nameof(CancellationToken));

            var reload = PublicApiAssert.RequireMethod(page, nameof(WebPage.ReloadAsync), nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(reload, nameof(HttpsResponseMessage));
        });
    }

    [Test]
    public void PageCookieSurfaceSpecTest()
    {
        var page = typeof(WebPage);

        Assert.Multiple(() =>
        {
            var getAll = PublicApiAssert.RequireMethod(page, nameof(WebPage.GetAllCookiesAsync), nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(getAll, nameof(Cookie));

            PublicApiAssert.RequireMethod(page, nameof(WebPage.SetCookiesAsync), "Cookie", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(page, nameof(WebPage.ClearAllCookiesAsync), nameof(CancellationToken));
        });
    }
}