using System.Linq;
using System.Net;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverCookieWindowSurfaceTests
{
    private const string LocalCookieDomain = "127.0.0.1";
    private static readonly string[] SessionCookieName = ["session"];
    private static readonly string[] PreferencesCookieName = ["prefs"];
    private static readonly string[] ModeCookieName = ["mode"];

    [Test]
    public async Task OpenWindowAsyncPublishesNewCurrentWindowAndPageSnapshots()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = browser.CurrentWindow;
        var firstPage = browser.CurrentPage;

        var secondWindow = await browser.OpenWindowAsync();
        var secondPage = secondWindow.CurrentPage;

        Assert.Multiple(() =>
        {
            Assert.That(secondWindow, Is.Not.SameAs(firstWindow));
            Assert.That(secondPage, Is.Not.SameAs(firstPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondPage));
            Assert.That(browser.Windows, Has.Some.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(secondPage));
        });
    }

    [Test]
    public async Task ActivateAsyncPublishesWindowAsCurrentSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = firstWindow.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondPage = secondWindow.CurrentPage;

        await firstWindow.ActivateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(firstPage));
            Assert.That(browser.CurrentWindow, Is.Not.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.Not.SameAs(secondPage));
            Assert.That(browser.Windows, Has.Some.SameAs(firstWindow));
            Assert.That(browser.Windows, Has.Some.SameAs(secondWindow));
        });
    }

    [Test]
    public async Task ActivateAsyncRepublishesEachWindowCurrentPageIndependentlyAcrossWindows()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowInitialPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync();
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondWindowInitialPage = (WebPage)secondWindow.CurrentPage;
        var secondWindowCurrentPage = (WebPage)await secondWindow.OpenPageAsync();

        await firstWindow.ActivateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.Not.SameAs(firstWindowInitialPage));
            Assert.That(secondWindow.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(secondWindow.CurrentPage, Is.Not.SameAs(secondWindowInitialPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(firstWindowCurrentPage));
        });

        await secondWindow.ActivateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(secondWindow.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(browser.Pages, Has.Some.SameAs(firstWindowCurrentPage));
            Assert.That(browser.Pages, Has.Some.SameAs(secondWindowCurrentPage));
        });
    }

    [Test]
    public async Task CloseAsyncRepublishesPreviousLiveWindowAndPageSnapshots()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = browser.CurrentWindow;
        var firstPage = browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondPage = secondWindow.CurrentPage;

        await secondWindow.CloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(secondWindow.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(firstWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(firstPage));
            Assert.That(browser.Windows, Has.None.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.None.SameAs(secondPage));
            Assert.That(browser.Windows, Has.Some.SameAs(firstWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(firstPage));
        });
    }

    [Test]
    public async Task PromotedWindowSupportsOpenPageAsyncAfterCurrentWindowClose()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var promotedWindow = (WebWindow)browser.CurrentWindow;
        var promotedPage = (WebPage)browser.CurrentPage;
        var disposedCurrentWindow = (WebWindow)await browser.OpenWindowAsync();
        var disposedCurrentPage = (WebPage)disposedCurrentWindow.CurrentPage;

        await disposedCurrentWindow.CloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(promotedWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(promotedPage));
            Assert.That(browser.Windows, Has.None.SameAs(disposedCurrentWindow));
            Assert.That(browser.Pages, Has.None.SameAs(disposedCurrentPage));
        });

        var reopenedPage = (WebPage)await promotedWindow.OpenPageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reopenedPage, Is.Not.SameAs(promotedPage));
            Assert.That(reopenedPage, Is.Not.SameAs(disposedCurrentPage));
            Assert.That(reopenedPage.Window, Is.SameAs(promotedWindow));
            Assert.That(promotedWindow.CurrentPage, Is.SameAs(reopenedPage));
            Assert.That(browser.CurrentWindow, Is.SameAs(promotedWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(reopenedPage));
            Assert.That(browser.Pages, Has.Some.SameAs(promotedPage));
            Assert.That(browser.Pages, Has.Some.SameAs(reopenedPage));
            Assert.That(browser.Pages, Has.None.SameAs(disposedCurrentPage));
        });
    }

    [Test]
    public async Task CloseAsyncOnNonCurrentWindowKeepsCurrentWindowAndPageSnapshotsStable()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondPage = (WebPage)secondWindow.CurrentPage;

        await firstWindow.CloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.IsDisposed, Is.True);
            Assert.That(firstPage.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondPage));
            Assert.That(browser.Windows, Has.None.SameAs(firstWindow));
            Assert.That(browser.Pages, Has.None.SameAs(firstPage));
            Assert.That(browser.Windows, Has.Some.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(secondPage));
        });
    }

    [Test]
    public async Task DisposingNonCurrentWindowKeepsCurrentWindowAndPageSnapshotsStable()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = browser.CurrentWindow;
        var firstPage = browser.CurrentPage;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondPage = secondWindow.CurrentPage;

        await firstWindow.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondPage));
            Assert.That(browser.Windows, Has.None.SameAs(firstWindow));
            Assert.That(browser.Pages, Has.None.SameAs(firstPage));
            Assert.That(browser.Windows, Has.Some.SameAs(secondWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(secondPage));
        });
    }

    [Test]
    public async Task PageClearAllCookiesRemainsLocalToTargetPageAcrossWindowTabs()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var clearedPage = (WebPage)window.CurrentPage;
        var retainedPage = (WebPage)await window.OpenPageAsync();

        await clearedPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]);
        await retainedPage.SetCookiesAsync([new Cookie("prefs", "beta", "/", LocalCookieDomain)]);

        await clearedPage.ClearAllCookiesAsync();

        var clearedPageCookies = (await clearedPage.GetAllCookiesAsync()).ToArray();
        var retainedPageCookies = (await retainedPage.GetAllCookiesAsync()).ToArray();
        var clearedPageDocumentCookie = (await clearedPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;
        var retainedPageDocumentCookie = (await retainedPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(retainedPage));
            Assert.That(browser.CurrentPage, Is.SameAs(retainedPage));
            Assert.That(clearedPageCookies, Is.Empty);
            Assert.That(retainedPageCookies, Has.Length.EqualTo(1));
            Assert.That(retainedPageCookies[0].Name, Is.EqualTo("prefs"));
            Assert.That(retainedPageCookies[0].Value, Is.EqualTo("beta"));
            Assert.That(clearedPageDocumentCookie, Is.Empty);
            Assert.That(retainedPageDocumentCookie, Does.Contain("prefs=beta"));
            Assert.That(retainedPageDocumentCookie, Does.Not.Contain("session=alpha"));
        });
    }

    [Test]
    public async Task WindowClearAllCookiesFansOutAcrossCurrentAndNonCurrentPagesWithoutChangingBrowserCurrentWindow()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowInitialPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync();
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondWindowPage = (WebPage)secondWindow.CurrentPage;

        await firstWindowInitialPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]);
        await firstWindowCurrentPage.SetCookiesAsync([new Cookie("prefs", "beta", "/", LocalCookieDomain)]);
        await secondWindowPage.SetCookiesAsync([new Cookie("mode", "gamma", "/", LocalCookieDomain)]);

        await firstWindow.ClearAllCookiesAsync();

        var firstWindowInitialPageCookies = (await firstWindowInitialPage.GetAllCookiesAsync()).ToArray();
        var firstWindowCurrentPageCookies = (await firstWindowCurrentPage.GetAllCookiesAsync()).ToArray();
        var secondWindowPageCookies = (await secondWindowPage.GetAllCookiesAsync()).ToArray();
        var firstWindowCurrentPageDocumentCookie = (await firstWindowCurrentPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;
        var secondWindowPageDocumentCookie = (await secondWindowPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(firstWindowInitialPageCookies, Is.Empty);
            Assert.That(firstWindowCurrentPageCookies, Is.Empty);
            Assert.That(secondWindowPageCookies, Has.Length.EqualTo(1));
            Assert.That(secondWindowPageCookies[0].Name, Is.EqualTo("mode"));
            Assert.That(secondWindowPageCookies[0].Value, Is.EqualTo("gamma"));
            Assert.That(firstWindowCurrentPageDocumentCookie, Is.Empty);
            Assert.That(secondWindowPageDocumentCookie, Does.Contain("mode=gamma"));
        });
    }

    [Test]
    public async Task SetAndGetCookiesOnNonCurrentPageInNonCurrentWindowRemainIsolatedFromCurrentBrowserPage()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowNonCurrentPage = (WebPage)firstWindow.CurrentPage;
        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync();
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;

        await firstWindowNonCurrentPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]);
        await firstWindowCurrentPage.SetCookiesAsync([new Cookie("prefs", "beta", "/", LocalCookieDomain)]);
        await secondWindowCurrentPage.SetCookiesAsync([new Cookie("mode", "gamma", "/", LocalCookieDomain)]);

        var firstWindowNonCurrentPageCookies = (await firstWindowNonCurrentPage.GetAllCookiesAsync()).ToArray();
        var firstWindowCurrentPageCookies = (await firstWindowCurrentPage.GetAllCookiesAsync()).ToArray();
        var secondWindowCurrentPageCookies = (await secondWindowCurrentPage.GetAllCookiesAsync()).ToArray();
        var firstWindowNonCurrentDocumentCookie = (await firstWindowNonCurrentPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;
        var firstWindowCurrentDocumentCookie = (await firstWindowCurrentPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;
        var secondWindowCurrentDocumentCookie = (await secondWindowCurrentPage.EvaluateAsync("document.cookie"))?.GetString() ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));

            Assert.That(firstWindowNonCurrentPageCookies.Select(static cookie => cookie.Name), Is.EqualTo(SessionCookieName));
            Assert.That(firstWindowCurrentPageCookies.Select(static cookie => cookie.Name), Is.EqualTo(PreferencesCookieName));
            Assert.That(secondWindowCurrentPageCookies.Select(static cookie => cookie.Name), Is.EqualTo(ModeCookieName));

            Assert.That(firstWindowNonCurrentDocumentCookie, Does.Contain("session=alpha"));
            Assert.That(firstWindowNonCurrentDocumentCookie, Does.Not.Contain("prefs=beta"));
            Assert.That(firstWindowCurrentDocumentCookie, Does.Contain("prefs=beta"));
            Assert.That(firstWindowCurrentDocumentCookie, Does.Not.Contain("session=alpha"));
            Assert.That(secondWindowCurrentDocumentCookie, Does.Contain("mode=gamma"));
            Assert.That(secondWindowCurrentDocumentCookie, Does.Not.Contain("session=alpha"));
        });
    }

    [Test]
    public async Task CookieSurfacePersistsAcrossPageWindowAndBrowserOperations()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)firstWindow.CurrentPage;
        var secondPage = (WebPage)await firstWindow.OpenPageAsync();
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var thirdPage = (WebPage)secondWindow.CurrentPage;

        await firstPage.SetCookiesAsync([new Cookie("session", "alpha", "/", LocalCookieDomain)]);
        await secondPage.SetCookiesAsync([new Cookie("prefs", "beta", "/", LocalCookieDomain)]);
        await thirdPage.SetCookiesAsync([new Cookie("mode", "gamma", "/", LocalCookieDomain)]);

        var firstCookiesBeforeClear = (await firstPage.GetAllCookiesAsync()).ToArray();
        var secondCookiesBeforeClear = (await secondPage.GetAllCookiesAsync()).ToArray();
        var thirdCookiesBeforeClear = (await thirdPage.GetAllCookiesAsync()).ToArray();
        var firstDocumentCookie = (await firstPage.EvaluateAsync("document.cookie"))?.GetString();

        await secondWindow.ClearAllCookiesAsync();

        var firstCookiesAfterWindowClear = (await firstPage.GetAllCookiesAsync()).ToArray();
        var secondCookiesAfterWindowClear = (await secondPage.GetAllCookiesAsync()).ToArray();
        var thirdCookiesAfterWindowClear = (await thirdPage.GetAllCookiesAsync()).ToArray();

        await browser.ClearAllCookiesAsync();

        var firstCookiesAfterBrowserClear = (await firstPage.GetAllCookiesAsync()).ToArray();
        var secondCookiesAfterBrowserClear = (await secondPage.GetAllCookiesAsync()).ToArray();
        var thirdCookiesAfterBrowserClear = (await thirdPage.GetAllCookiesAsync()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstCookiesBeforeClear, Has.Length.EqualTo(1));
            Assert.That(firstCookiesBeforeClear[0].Name, Is.EqualTo("session"));
            Assert.That(firstCookiesBeforeClear[0].Value, Is.EqualTo("alpha"));
            Assert.That(secondCookiesBeforeClear, Has.Length.EqualTo(1));
            Assert.That(secondCookiesBeforeClear[0].Name, Is.EqualTo("prefs"));
            Assert.That(thirdCookiesBeforeClear, Has.Length.EqualTo(1));
            Assert.That(thirdCookiesBeforeClear[0].Name, Is.EqualTo("mode"));
            Assert.That(firstDocumentCookie, Does.Contain("session=alpha"));

            Assert.That(firstCookiesAfterWindowClear, Has.Length.EqualTo(1));
            Assert.That(secondCookiesAfterWindowClear, Has.Length.EqualTo(1));
            Assert.That(thirdCookiesAfterWindowClear, Is.Empty);

            Assert.That(firstCookiesAfterBrowserClear, Is.Empty);
            Assert.That(secondCookiesAfterBrowserClear, Is.Empty);
            Assert.That(thirdCookiesAfterBrowserClear, Is.Empty);
        });
    }
}