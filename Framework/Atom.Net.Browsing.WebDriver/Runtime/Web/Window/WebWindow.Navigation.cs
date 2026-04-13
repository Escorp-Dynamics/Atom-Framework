using System.Drawing;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebWindow
{
    public async ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerBrowser.GetBridgeCommandPage() is { } bridgePage
            && bridgePage.BridgeCommands is { } bridge)
        {
            await bridge.ActivateWindowAsync(EffectiveWindowId, cancellationToken).ConfigureAwait(false);
            await bridge.ActivateTabAsync(((WebPage)CurrentPage).TabId, cancellationToken).ConfigureAwait(false);
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
        }

        OwnerBrowser.ActivateWindow(this);
    }

    public ValueTask ActivateAsync()
        => ActivateAsync(CancellationToken.None);

    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerBrowser.GetBridgeCommandPage(this) is { } bridgePage
            && bridgePage.BridgeCommands is { } bridge)
        {
            try
            {
                await bridge.CloseWindowAsync(EffectiveWindowId, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (ReferenceEquals(bridgePage.OwnerWindow, this)
                && exception.Message.Contains("отключено", StringComparison.Ordinal))
            {
                // Closing the only bridge-bound window can disconnect the sender before a response arrives.
            }
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask CloseAsync()
        => CloseAsync(CancellationToken.None);

    public async ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebPage[] pagesSnapshot;
        lock (pageGate)
        {
            ThrowIfDisposed();
            pagesSnapshot = pages.Where(static page => !page.IsDisposed).ToArray();
        }

        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowCookiesClearing(WindowId, pagesSnapshot.Length);

        foreach (var page in pagesSnapshot)
        {
            await page.ClearAllCookiesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask ClearAllCookiesAsync()
        => ClearAllCookiesAsync(CancellationToken.None);

    public async ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebPage[] pagesSnapshot;
        lock (pageGate)
        {
            ThrowIfDisposed();
            requestInterceptionState = RequestInterceptionState.Create(enabled, urlPatterns);
            pagesSnapshot = pages.Where(static page => !page.IsDisposed).ToArray();
        }

        foreach (var page in pagesSnapshot)
        {
            await page.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns)
        => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    public ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken)
        => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(bool enabled)
        => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings(), cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url)
        => NavigateAsync(url, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Kind = kind }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind)
        => NavigateAsync(url, kind, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Headers = headers }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers)
        => NavigateAsync(url, headers, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Body = body }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body)
        => NavigateAsync(url, body, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Html = html }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html)
        => NavigateAsync(url, html, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        var currentPage = (WebPage)CurrentPage;
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowNavigationStarting(WindowId, currentPage.TabId, url.ToString(), settings.Kind.ToString());
        return currentPage.NavigateAsync(url, settings, cancellationToken);
    }

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings)
        => NavigateAsync(url, settings, CancellationToken.None);

    public async ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var currentPage = (WebPage)CurrentPage;
        var reloadUrl = await currentPage.GetUrlAsync(cancellationToken).ConfigureAwait(false) ?? currentPage.CurrentUrl ?? new Uri("about:blank");
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowReloadStarting(WindowId, currentPage.TabId, reloadUrl.ToString());
        return await currentPage.ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<HttpsResponseMessage> ReloadAsync()
        => ReloadAsync(CancellationToken.None);

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfDisposed();
        return CurrentPage.AttachVirtualCameraAsync(camera, cancellationToken);
    }

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera)
        => AttachVirtualCameraAsync(camera, CancellationToken.None);

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ThrowIfDisposed();
        return CurrentPage.AttachVirtualMicrophoneAsync(microphone, cancellationToken);
    }

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone)
        => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return CurrentPage.GetUrlAsync(cancellationToken);
    }

    public ValueTask<Uri?> GetUrlAsync()
        => GetUrlAsync(CancellationToken.None);

    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return CurrentPage.GetTitleAsync(cancellationToken);
    }

    public ValueTask<string?> GetTitleAsync()
        => GetTitleAsync(CancellationToken.None);

    public async ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentPage is WebPage page
            && page.BridgeCommands is { } bridge)
        {
            var bridgeBounds = await bridge.GetWindowBoundsAsync(cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsLinux()
                && OwnerBrowser.TryGetLinuxNativeWindowBounds(bridgeBounds.Size, page.CurrentTitle) is Rectangle nativeBounds)
            {
                return nativeBounds;
            }

            return bridgeBounds;
        }

        return new Rectangle(ResolvedWindowPosition, ResolvedWindowSize);
    }

    public ValueTask<Rectangle?> GetBoundingBoxAsync()
        => GetBoundingBoxAsync(CancellationToken.None);
}