using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverWindowApiSurfaceTests
{
    [Test]
    public void WindowCoreSurfaceSpecTest()
    {
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.IsDisposed));
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.Browser));
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.Pages));
            PublicApiAssert.RequireProperty(window, nameof(WebWindow.CurrentPage));
        });
    }

    [Test]
    public void WindowInterceptionAndEventSurfaceSpecTest()
    {
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.Console));
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.Request));
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.Response));
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.DomContentLoaded));
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.NavigationCompleted));
            PublicApiAssert.RequireEvent(window, nameof(WebWindow.PageLoaded));

            PublicApiAssert.RequireMethod(
                window,
                nameof(WebWindow.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>),
                nameof(CancellationToken));
            PublicApiAssert.RequireMethod(
                window,
                nameof(WebWindow.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(IEnumerable<string>));
            PublicApiAssert.RequireMethod(
                window,
                nameof(WebWindow.SetRequestInterceptionAsync),
                nameof(Boolean),
                nameof(CancellationToken));
            PublicApiAssert.RequireMethod(
                window,
                nameof(WebWindow.SetRequestInterceptionAsync),
                nameof(Boolean));
        });
    }

    [Test]
    public void WindowLifecycleAndMediaSurfaceSpecTest()
    {
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ActivateAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ActivateAsync));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.CloseAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.CloseAsync));

            PublicApiAssert.RequireMethod(window, nameof(WebWindow.AttachVirtualCameraAsync), nameof(VirtualCamera), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.AttachVirtualCameraAsync), nameof(VirtualCamera));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.AttachVirtualMicrophoneAsync), nameof(VirtualMicrophone), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.AttachVirtualMicrophoneAsync), nameof(VirtualMicrophone));
        });
    }

    [Test]
    public void WindowNavigationAndCookieSurfaceSpecTest()
    {
        var window = typeof(WebWindow);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ClearAllCookiesAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ClearAllCookiesAsync));

            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(NavigationKind), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(NavigationSettings), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(IReadOnlyDictionary<string, string>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(ReadOnlyMemory<byte>), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.NavigateAsync), nameof(Uri), nameof(String), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ReloadAsync), nameof(CancellationToken));
            PublicApiAssert.RequireMethod(window, nameof(WebWindow.ReloadAsync));
        });
    }
}