using System.Net;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverElementInterceptionApiSurfaceTests
{
    [Test]
    public void SelectorAndWaitSettingsSurfaceSpecTest()
    {
        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(typeof(WaitForElementSettings), nameof(WaitForElementSettings.Selector));
            PublicApiAssert.RequireProperty(typeof(WaitForElementSettings), nameof(WaitForElementSettings.Timeout));
            PublicApiAssert.RequireProperty(typeof(WaitForElementSettings), nameof(WaitForElementSettings.Kind));

            Assert.That(Enum.GetNames<WaitForElementKind>(), Does.Contain(nameof(WaitForElementKind.Attached))
                .And.Contain(nameof(WaitForElementKind.Visible))
                .And.Contain(nameof(WaitForElementKind.Stable)));
        });
    }

    [Test]
    public void ElementInteractionSurfaceSpecTest()
    {
        var element = typeof(IElement);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(element, nameof(IElement.IsDisposed));
            PublicApiAssert.RequireProperty(element, nameof(IElement.Page));
            PublicApiAssert.RequireProperty(element, nameof(IElement.Frame));

            PublicApiAssert.RequireMethod(element, nameof(IElement.ClickAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.ClickAsync), nameof(ClickSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.HoverAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.ScrollIntoViewAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.FocusAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.TypeAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.PressAsync), nameof(ConsoleKey), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.HumanityClickAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.HumanityTypeAsync), nameof(String), nameof(CancellationToken));
        });
    }

    [Test]
    public void ElementInspectionTraversalAndMutationSurfaceSpecTest()
    {
        var element = typeof(IElement);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetInnerTextAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetInnerHtmlAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetValueAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetAttributeAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.IsVisibleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetBoundingBoxAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetComputedStyleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.IsCheckedAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.IsDisabledAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetClassListAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetScreenshotAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetElementHandleAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetIdAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetPropertyAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetParentElementAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetChildElementsAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetSiblingElementsAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetElementPathAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetCustomDataAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireGenericMethod(element, nameof(IElement.EvaluateAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetFrameAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetChildFramesAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetParentFrameAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.GetShadowRootAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetValueAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetAttributeAsync), nameof(String), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetStyleAsync), nameof(String), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.AddClassAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.RemoveClassAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.ToggleClassAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetContentAsync), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetCustomPropertyAsync), nameof(String), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.SetDataAsync), nameof(String), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.AddEventListenerAsync), nameof(String), nameof(Delegate), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(element, nameof(IElement.RemoveEventListenerAsync), nameof(String), nameof(Delegate), nameof(CancellationToken));
        });
    }

    [Test]
    public void InterceptionEventArgsSurfaceSpecTest()
    {
        var requestArgs = typeof(InterceptedRequestEventArgs);
        var responseArgs = typeof(InterceptedResponseEventArgs);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(requestArgs, nameof(InterceptedRequestEventArgs.IsNavigate));
            PublicApiAssert.RequireProperty(requestArgs, nameof(InterceptedRequestEventArgs.Request));
            PublicApiAssert.RequireProperty(requestArgs, nameof(InterceptedRequestEventArgs.Frame));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.FulfillAsync), "HttpsResponseMessage", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.FulfillAsync), "HttpsResponseMessage");
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.RedirectAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.RedirectAsync), nameof(Uri));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.ContinueAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.ContinueAsync));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.ContinueAsync), "HttpsRequestMessage", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.ContinueAsync), "HttpsRequestMessage");
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(HttpStatusCode));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(Int32), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(Int32));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(String));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(Int32), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(requestArgs, nameof(InterceptedRequestEventArgs.AbortAsync), nameof(Int32), nameof(String));

            PublicApiAssert.RequireProperty(responseArgs, nameof(InterceptedResponseEventArgs.IsNavigate));
            PublicApiAssert.RequireProperty(responseArgs, nameof(InterceptedResponseEventArgs.Response));
            PublicApiAssert.RequireProperty(responseArgs, nameof(InterceptedResponseEventArgs.Frame));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.FulfillAsync), "HttpsResponseMessage", nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.FulfillAsync), "HttpsResponseMessage");
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.ContinueAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.ContinueAsync));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(HttpStatusCode));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(Int32), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(Int32));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(HttpStatusCode), nameof(String));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(Int32), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(responseArgs, nameof(InterceptedResponseEventArgs.AbortAsync), nameof(Int32), nameof(String));
        });
    }
}