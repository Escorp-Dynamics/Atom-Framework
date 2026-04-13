namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebBrowser
{
    private ValueTask<IWebWindow?> GetWindowByNameAsync(string name, CancellationToken cancellationToken)
        => GetWindowByNameCoreAsync(name, cancellationToken);

    private async ValueTask<IWebWindow?> GetWindowByNameCoreAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        LaunchSettings.Logger?.LogWebBrowserLookupStarting("окна", "имени", name);

        if (string.Equals(name, "current", StringComparison.Ordinal))
        {
            LaunchSettings.Logger?.LogWebBrowserLookupCompleted("окна", "имени", name, DescribeWindowLookupResult(currentWindow));
            return currentWindow;
        }

        foreach (var window in Windows)
        {
            if (window is not WebWindow candidateWindow || candidateWindow.IsDisposed)
                continue;

            if (candidateWindow.CurrentPage is not WebPage currentPage || currentPage.IsDisposed)
                continue;

            var currentTitle = await currentPage.GetLookupTitleAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(currentTitle, name, StringComparison.Ordinal))
            {
                LaunchSettings.Logger?.LogWebBrowserLookupCompleted("окна", "имени", name, DescribeWindowLookupResult(candidateWindow));
                return candidateWindow;
            }
        }

        LaunchSettings.Logger?.LogWebBrowserLookupCompleted("окна", "имени", name, "<none>");
        return null;
    }

    private ValueTask<IWebWindow?> GetWindowByUrlAsync(Uri url, CancellationToken cancellationToken)
        => GetWindowByUrlCoreAsync(url, cancellationToken);

    private async ValueTask<IWebWindow?> GetWindowByUrlCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        LaunchSettings.Logger?.LogWebBrowserLookupStarting("окна", "адресу", url.ToString());

        foreach (var window in Windows)
        {
            if (window is not WebWindow candidateWindow || candidateWindow.IsDisposed)
                continue;

            foreach (var page in candidateWindow.Pages)
            {
                if (page is not WebPage candidatePage || candidatePage.IsDisposed)
                    continue;

                var currentUrl = await candidatePage.GetLookupUrlAsync(cancellationToken).ConfigureAwait(false);
                if (currentUrl == url)
                {
                    LaunchSettings.Logger?.LogWebBrowserLookupCompleted("окна", "адресу", url.ToString(), DescribeWindowLookupResult(candidateWindow));
                    return candidateWindow;
                }
            }
        }

        LaunchSettings.Logger?.LogWebBrowserLookupCompleted("окна", "адресу", url.ToString(), "<none>");
        return null;
    }

    private ValueTask<IWebPage?> GetPageByNameAsync(string name, CancellationToken cancellationToken)
        => GetPageByNameCoreAsync(name, cancellationToken);

    private async ValueTask<IWebPage?> GetPageByNameCoreAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        LaunchSettings.Logger?.LogWebBrowserLookupStarting("вкладки", "имени", name);

        if (string.Equals(name, "current", StringComparison.Ordinal))
        {
            LaunchSettings.Logger?.LogWebBrowserLookupCompleted("вкладки", "имени", name, DescribePageLookupResult(CurrentPage));
            return CurrentPage;
        }

        foreach (var window in Windows)
        {
            if (window is not WebWindow candidateWindow || candidateWindow.IsDisposed)
                continue;

            foreach (var page in candidateWindow.Pages)
            {
                if (page is not WebPage candidatePage || candidatePage.IsDisposed)
                    continue;

                var currentTitle = await candidatePage.GetLookupTitleAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(currentTitle, name, StringComparison.Ordinal))
                {
                    LaunchSettings.Logger?.LogWebBrowserLookupCompleted("вкладки", "имени", name, DescribePageLookupResult(candidatePage));
                    return candidatePage;
                }
            }
        }

        LaunchSettings.Logger?.LogWebBrowserLookupCompleted("вкладки", "имени", name, "<none>");
        return null;
    }

    private ValueTask<IWebPage?> GetPageByUrlAsync(Uri url, CancellationToken cancellationToken)
        => GetPageByUrlCoreAsync(url, cancellationToken);

    private async ValueTask<IWebPage?> GetPageByUrlCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        LaunchSettings.Logger?.LogWebBrowserLookupStarting("вкладки", "адресу", url.ToString());

        foreach (var window in Windows)
        {
            if (window is not WebWindow candidateWindow || candidateWindow.IsDisposed)
                continue;

            foreach (var page in candidateWindow.Pages)
            {
                if (page is not WebPage candidatePage || candidatePage.IsDisposed)
                    continue;

                var currentUrl = await candidatePage.GetLookupUrlAsync(cancellationToken).ConfigureAwait(false);
                if (currentUrl == url)
                {
                    LaunchSettings.Logger?.LogWebBrowserLookupCompleted("вкладки", "адресу", url.ToString(), DescribePageLookupResult(candidatePage));
                    return candidatePage;
                }
            }
        }

        LaunchSettings.Logger?.LogWebBrowserLookupCompleted("вкладки", "адресу", url.ToString(), "<none>");
        return null;
    }

    private static string DescribeWindowLookupResult(IWebWindow? window)
        => window is WebWindow runtimeWindow ? runtimeWindow.WindowId : "<none>";

    private static string DescribePageLookupResult(IWebPage? page)
        => page is WebPage runtimePage ? runtimePage.TabId : "<none>";
}