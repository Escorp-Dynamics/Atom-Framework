namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebWindow
{
    public ValueTask<IWebPage?> GetPageAsync(string name, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetPageByNameAsync(name, cancellationToken);
    }

    public ValueTask<IWebPage?> GetPageAsync(string name)
        => GetPageAsync(name, CancellationToken.None);

    public ValueTask<IWebPage?> GetPageAsync(Uri url, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetPageByUrlAsync(url, cancellationToken);
    }

    public ValueTask<IWebPage?> GetPageAsync(Uri url)
        => GetPageAsync(url, CancellationToken.None);

    public ValueTask<IWebPage?> GetPageAsync(IElement element, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sourcePage = element.Page as WebPage;
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupStarting(WindowId, "элементу", sourcePage?.TabId ?? "<external>");

        if (ReferenceEquals(element.Page.Window, this))
        {
            OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "элементу", sourcePage?.TabId ?? "<external>", DescribePageLookupResult(element.Page));
            return ValueTask.FromResult<IWebPage?>(element.Page);
        }

        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "элементу", sourcePage?.TabId ?? "<external>", "<none>");
        return ValueTask.FromResult<IWebPage?>(null);
    }

    public ValueTask<IWebPage?> GetPageAsync(IElement element)
        => GetPageAsync(element, CancellationToken.None);

    private ValueTask<IWebPage?> GetPageByNameAsync(string name, CancellationToken cancellationToken)
        => GetPageByNameCoreAsync(name, cancellationToken);

    private async ValueTask<IWebPage?> GetPageByNameCoreAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupStarting(WindowId, "имени", name);

        if (string.Equals(name, "current", StringComparison.Ordinal))
        {
            OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "имени", name, DescribePageLookupResult(currentPage));
            return currentPage;
        }

        foreach (var page in Pages)
        {
            if (page is not WebPage candidate || candidate.IsDisposed)
                continue;

            var currentTitle = await candidate.GetLookupTitleAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(currentTitle, name, StringComparison.Ordinal))
            {
                OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "имени", name, DescribePageLookupResult(candidate));
                return candidate;
            }
        }

        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "имени", name, "<none>");
        return null;
    }

    private ValueTask<IWebPage?> GetPageByUrlAsync(Uri url, CancellationToken cancellationToken)
        => GetPageByUrlCoreAsync(url, cancellationToken);

    private async ValueTask<IWebPage?> GetPageByUrlCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupStarting(WindowId, "адресу", url.ToString());

        foreach (var page in Pages)
        {
            if (page is not WebPage candidate || candidate.IsDisposed)
                continue;

            var currentUrl = await candidate.GetLookupUrlAsync(cancellationToken).ConfigureAwait(false);
            if (currentUrl == url)
            {
                OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "адресу", url.ToString(), DescribePageLookupResult(candidate));
                return candidate;
            }
        }

        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowLookupCompleted(WindowId, "адресу", url.ToString(), "<none>");
        return null;
    }

    private static string DescribePageLookupResult(IWebPage? page)
        => page is WebPage runtimePage ? runtimePage.TabId : "<none>";
}