using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverFrameShadowRootApiSurfaceTests
{
    [Test]
    public void FrameSurfaceSpecTest()
    {
        var frame = typeof(Frame);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireEvent(frame, nameof(Frame.DomContentLoaded));
            PublicApiAssert.RequireEvent(frame, nameof(Frame.NavigationCompleted));
            PublicApiAssert.RequireEvent(frame, nameof(Frame.PageLoaded));

            PublicApiAssert.RequireProperty(frame, nameof(Frame.IsDisposed));
            PublicApiAssert.RequireProperty(frame, nameof(Frame.Frames));
            PublicApiAssert.RequireProperty(frame, nameof(Frame.Page));
            PublicApiAssert.RequireProperty(frame, nameof(Frame.Host));

            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetUrlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetTitleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetContentAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireGenericMethod(frame, nameof(Frame.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(String), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(WaitForElementSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(ElementSelector), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetElementsAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetElementsAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetShadowRootAsync), nameof(String), nameof(CancellationToken));

            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetChildFramesAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetParentFrameAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetFrameElementAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetNameAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetFrameElementHandleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.IsDetachedAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.IsVisibleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(frame, nameof(Frame.GetContentFrameAsync), nameof(CancellationToken));

            var screenshot = PublicApiAssert.RequireMethod(frame, nameof(Frame.GetScreenshotAsync), nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(screenshot, "Memory");

            var bounds = PublicApiAssert.RequireMethod(frame, nameof(Frame.GetBoundingBoxAsync), nameof(CancellationToken));
            PublicApiAssert.AssertReturnTypeContains(bounds, "Rectangle");
        });
    }

    [Test]
    public void ShadowRootSurfaceSpecTest()
    {
        var shadowRoot = typeof(ShadowRoot);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(shadowRoot, nameof(ShadowRoot.IsDisposed));
            PublicApiAssert.RequireProperty(shadowRoot, nameof(ShadowRoot.Frames));
            PublicApiAssert.RequireProperty(shadowRoot, nameof(ShadowRoot.Host));
            PublicApiAssert.RequireProperty(shadowRoot, nameof(ShadowRoot.Page));
            PublicApiAssert.RequireProperty(shadowRoot, nameof(ShadowRoot.Frame));

            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetUrlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetTitleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetContentAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireGenericMethod(shadowRoot, nameof(ShadowRoot.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(String), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(String), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(WaitForElementSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(ElementSelector), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.WaitForElementAsync), nameof(ElementSelector), nameof(WaitForElementKind), nameof(TimeSpan), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetElementAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetElementAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetElementsAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetElementsAsync), nameof(ElementSelector), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(ShadowRoot.GetShadowRootAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(shadowRoot, nameof(IAsyncDisposable.DisposeAsync));
        });
    }
}