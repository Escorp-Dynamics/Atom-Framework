using System.Drawing;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;
using Atom.Media.Video;
using Atom.Media.Video.Backends;
using Atom.Net.Browsing.WebDriver.Protocol;
using RuntimeWebBrowser = Atom.Net.Browsing.WebDriver.WebBrowser;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeLifecycleTests
{
    private static readonly string[] ExpectedLifecycleOrder = ["DomContentLoaded", "NavigationCompleted", "PageLoaded"];
    private static readonly string[] ExpectedShadowItemHandles = ["shadow-item-1", "shadow-item-2"];
    private static readonly BridgeEvent[] ExpectedBridgeOrder =
    [
        BridgeEvent.RequestIntercepted,
        BridgeEvent.ResponseReceived,
        BridgeEvent.DomContentLoaded,
        BridgeEvent.NavigationCompleted,
        BridgeEvent.PageLoaded,
    ];

    [Test]
    public async Task OpenPageAsyncPropagatesLifecycleWithDistinctTabContext()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)window.CurrentPage;
        var secondPage = (WebPage)await window.OpenPageAsync();
        List<string> pageLifecycleEvents = [];
        List<string> browserLifecycleEvents = [];

        SubscribeLifecycle(secondPage, pageLifecycleEvents);
        SubscribeLifecycle(browser, browserLifecycleEvents);

        await secondPage.NavigateAsync(new Uri("https://127.0.0.1/secondary"), new NavigationSettings
        {
            Html = "<html><head><title>Secondary</title></head><body>page</body></html>",
        });

        var pageHasEvent = secondPage.TryDequeueBridgeEvent(out var pageEvent);
        var windowHasEvent = window.TryDequeueBridgeEvent(out var windowEvent);
        var browserHasEvent = browser.TryDequeueBridgeEvent(out var browserEvent);

        Assert.Multiple(() =>
        {
            Assert.That(secondPage.TabId, Is.Not.EqualTo(firstPage.TabId));
            Assert.That(secondPage.WindowId, Is.EqualTo(window.WindowId));
            Assert.That(pageHasEvent, Is.True);
            Assert.That(pageEvent, Is.Not.Null);
            Assert.That(pageEvent!.TabId, Is.EqualTo(secondPage.TabId));
            Assert.That(pageEvent.WindowId, Is.EqualTo(window.WindowId));
            Assert.That(windowHasEvent, Is.True);
            Assert.That(windowEvent, Is.Not.Null);
            Assert.That(windowEvent!.TabId, Is.EqualTo(secondPage.TabId));
            Assert.That(browserHasEvent, Is.True);
            Assert.That(browserEvent, Is.Not.Null);
            Assert.That(browserEvent!.TabId, Is.EqualTo(secondPage.TabId));
            Assert.That(browserEvent.WindowId, Is.EqualTo(window.WindowId));
            Assert.That(pageLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(browserLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
        });
    }

    [Test]
    public async Task OpenWindowAsyncPropagatesLifecycleWithDistinctWindowContext()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var firstWindow = (WebWindow)browser.CurrentWindow;
        var secondWindow = (WebWindow)await browser.OpenWindowAsync();
        var secondPage = (WebPage)secondWindow.CurrentPage;
        List<string> windowLifecycleEvents = [];
        List<string> browserLifecycleEvents = [];

        SubscribeLifecycle(secondWindow, windowLifecycleEvents);
        SubscribeLifecycle(browser, browserLifecycleEvents);

        await secondPage.NavigateAsync(new Uri("https://127.0.0.1/window-two"), new NavigationSettings
        {
            Html = "<html><head><title>Window Two</title></head><body>window</body></html>",
        });

        var pageHasEvent = secondPage.TryDequeueBridgeEvent(out var pageEvent);
        var windowHasEvent = secondWindow.TryDequeueBridgeEvent(out var windowEvent);
        var browserHasEvent = browser.TryDequeueBridgeEvent(out var browserEvent);

        Assert.Multiple(() =>
        {
            Assert.That(secondWindow.WindowId, Is.Not.EqualTo(firstWindow.WindowId));
            Assert.That(secondPage.WindowId, Is.EqualTo(secondWindow.WindowId));
            Assert.That(pageHasEvent, Is.True);
            Assert.That(pageEvent, Is.Not.Null);
            Assert.That(pageEvent!.WindowId, Is.EqualTo(secondWindow.WindowId));
            Assert.That(pageEvent.TabId, Is.EqualTo(secondPage.TabId));
            Assert.That(windowHasEvent, Is.True);
            Assert.That(windowEvent, Is.Not.Null);
            Assert.That(windowEvent!.WindowId, Is.EqualTo(secondWindow.WindowId));
            Assert.That(browserHasEvent, Is.True);
            Assert.That(browserEvent, Is.Not.Null);
            Assert.That(browserEvent!.WindowId, Is.EqualTo(secondWindow.WindowId));
            Assert.That(browserEvent.TabId, Is.EqualTo(secondPage.TabId));
            Assert.That(windowLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(browserLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
        });
    }

    [Test]
    public void PageNavigationStateDispatchesLocalBridgeEnvelope()
    {
        const string windowId = "window-local";
        const string tabId = "tab-local";
        var state = new PageNavigationState(windowId, tabId);
        var navigatePayload = JsonDocument.Parse("""
            {
              "url": "https://127.0.0.1/dispatch",
              "kind": "Default",
              "html": "<html><head><title>Dispatch</title></head><body>bridge</body></html>",
              "headers": {
                "X-Test": "protocol-envelope"
              }
            }
            """).RootElement.Clone();

        var navigateResponse = state.Send(new BridgeMessage
        {
            Id = "msg-nav",
            Type = BridgeMessageType.Request,
            WindowId = windowId,
            TabId = tabId,
            Command = BridgeCommand.Navigate,
            Payload = navigatePayload,
        });

        var titleResponse = state.Send(new BridgeMessage
        {
            Id = "msg-title",
            Type = BridgeMessageType.Request,
            WindowId = windowId,
            TabId = tabId,
            Command = BridgeCommand.GetTitle,
        });

        var evaluateResponse = state.Send(new BridgeMessage
        {
            Id = "msg-script",
            Type = BridgeMessageType.Request,
            WindowId = windowId,
            TabId = tabId,
            Command = BridgeCommand.ExecuteScript,
            Payload = JsonDocument.Parse("{\"script\":\"document.title\"}").RootElement.Clone(),
        });
        var hasRequestEvent = state.TryDequeueEvent(out var requestEvent);
        var hasResponseEvent = state.TryDequeueEvent(out var responseEvent);
        var hasDomContentLoadedEvent = state.TryDequeueEvent(out var domContentLoadedEvent);
        var hasNavigationEvent = state.TryDequeueEvent(out var navigationEvent);
        var hasPageLoadedEvent = state.TryDequeueEvent(out var pageLoadedEvent);

        Assert.Multiple(() =>
        {
            Assert.That(navigateResponse.Type, Is.EqualTo(BridgeMessageType.Response));
            Assert.That(navigateResponse.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(navigateResponse.WindowId, Is.EqualTo(windowId));
            Assert.That(navigateResponse.TabId, Is.EqualTo(tabId));
            Assert.That(navigateResponse.Payload?.GetProperty("statusCode").GetInt32(), Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(navigateResponse.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://127.0.0.1/dispatch"));
            Assert.That(navigateResponse.Payload?.GetProperty("title").GetString(), Is.EqualTo("Dispatch"));
            Assert.That(navigateResponse.Payload?.GetProperty("content").GetString(), Does.Contain("bridge"));
            Assert.That(titleResponse.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(titleResponse.Payload?.GetString(), Is.EqualTo("Dispatch"));
            Assert.That(evaluateResponse.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(evaluateResponse.Payload?.GetString(), Is.EqualTo("Dispatch"));
            Assert.That(hasRequestEvent, Is.True);
            Assert.That(requestEvent, Is.Not.Null);
            Assert.That(requestEvent!.Type, Is.EqualTo(BridgeMessageType.Event));
            Assert.That(requestEvent.Event, Is.EqualTo(BridgeEvent.RequestIntercepted));
            Assert.That(requestEvent.WindowId, Is.EqualTo(windowId));
            Assert.That(requestEvent.TabId, Is.EqualTo(tabId));
            Assert.That(requestEvent.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://127.0.0.1/dispatch"));
            Assert.That(requestEvent.Payload?.GetProperty("method").GetString(), Is.EqualTo("GET"));
            Assert.That(hasResponseEvent, Is.True);
            Assert.That(responseEvent, Is.Not.Null);
            Assert.That(responseEvent!.Type, Is.EqualTo(BridgeMessageType.Event));
            Assert.That(responseEvent.Event, Is.EqualTo(BridgeEvent.ResponseReceived));
            Assert.That(responseEvent.WindowId, Is.EqualTo(windowId));
            Assert.That(responseEvent.TabId, Is.EqualTo(tabId));
            Assert.That(responseEvent.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://127.0.0.1/dispatch"));
            Assert.That(responseEvent.Payload?.GetProperty("statusCode").GetInt32(), Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(hasDomContentLoadedEvent, Is.True);
            Assert.That(domContentLoadedEvent, Is.Not.Null);
            Assert.That(domContentLoadedEvent!.Type, Is.EqualTo(BridgeMessageType.Event));
            Assert.That(domContentLoadedEvent.Event, Is.EqualTo(BridgeEvent.DomContentLoaded));
            Assert.That(domContentLoadedEvent.WindowId, Is.EqualTo(windowId));
            Assert.That(domContentLoadedEvent.TabId, Is.EqualTo(tabId));
            Assert.That(hasNavigationEvent, Is.True);
            Assert.That(navigationEvent, Is.Not.Null);
            Assert.That(navigationEvent!.Type, Is.EqualTo(BridgeMessageType.Event));
            Assert.That(navigationEvent.Event, Is.EqualTo(BridgeEvent.NavigationCompleted));
            Assert.That(navigationEvent.WindowId, Is.EqualTo(windowId));
            Assert.That(navigationEvent.TabId, Is.EqualTo(tabId));
            Assert.That(navigationEvent.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://127.0.0.1/dispatch"));
            Assert.That(hasPageLoadedEvent, Is.True);
            Assert.That(pageLoadedEvent, Is.Not.Null);
            Assert.That(pageLoadedEvent!.Event, Is.EqualTo(BridgeEvent.PageLoaded));
            Assert.That(pageLoadedEvent.WindowId, Is.EqualTo(windowId));
            Assert.That(pageLoadedEvent.TabId, Is.EqualTo(tabId));
        });
    }

    [Test]
    public async Task NavigateAsyncUpdatesPageAndFrameRuntimeState()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var window = (WebWindow)browser.CurrentWindow;
        var targetUrl = new Uri("https://127.0.0.1/login");
        const string html = "<html><head><title>Login</title></head><body>ok</body></html>";
        List<BridgeEvent> pageBridgeEvents = [];
        List<BridgeEvent> windowBridgeEvents = [];
        List<BridgeEvent> browserBridgeEvents = [];
        List<string> pageLifecycleEvents = [];
        List<string> windowLifecycleEvents = [];
        List<string> browserLifecycleEvents = [];
        List<string> frameLifecycleEvents = [];
        List<WebLifecycleEventArgs> pageLifecycleArgs = [];
        List<WebLifecycleEventArgs> frameLifecycleArgs = [];

        page.BridgeEventReceived += message =>
        {
            if (message.Event is BridgeEvent @event)
            {
                pageBridgeEvents.Add(@event);
            }
        };
        window.BridgeEventReceived += message =>
        {
            if (message.Event is BridgeEvent @event)
            {
                windowBridgeEvents.Add(@event);
            }
        };
        browser.BridgeEventReceived += message =>
        {
            if (message.Event is BridgeEvent @event)
            {
                browserBridgeEvents.Add(@event);
            }
        };
        SubscribeLifecycle(page, pageLifecycleEvents, pageLifecycleArgs);
        SubscribeLifecycle(window, windowLifecycleEvents);
        SubscribeLifecycle(browser, browserLifecycleEvents);
        SubscribeLifecycle((Frame)page.MainFrame, frameLifecycleEvents, frameLifecycleArgs);

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(targetUrl, new NavigationSettings
        {
            Html = html,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Test"] = "bridge-state",
            },
        });

        var pageUrl = await page.GetUrlAsync();
        var pageTitle = await page.GetTitleAsync();
        var pageContent = await page.GetContentAsync();
        var frameUrl = await page.MainFrame.GetUrlAsync();
        var frameTitle = await page.MainFrame.GetTitleAsync();
        var frameContent = await page.MainFrame.GetContentAsync();
        var frameEvaluatedTitle = await page.MainFrame.EvaluateAsync<string>("document.title");
        var frameEvaluatedUrl = await page.MainFrame.EvaluateAsync<string>("window.location.href");
        var element = new Element(page);
        var elementEvaluatedTitle = await element.EvaluateAsync<string>("document.title");
        var pageHasEvent = page.TryDequeueBridgeEvent(out var pageEvent);
        var windowHasEvent = window.TryDequeueBridgeEvent(out var windowEvent);
        var browserHasEvent = browser.TryDequeueBridgeEvent(out var browserEvent);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.RequestMessage, Is.Not.Null);
            Assert.That(response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(response.RequestMessage.Headers.GetValues("X-Test").Single(), Is.EqualTo("bridge-state"));
            Assert.That(pageUrl, Is.EqualTo(targetUrl));
            Assert.That(pageTitle, Is.EqualTo("Login"));
            Assert.That(pageContent, Is.EqualTo(html));
            Assert.That(frameUrl, Is.EqualTo(targetUrl));
            Assert.That(frameTitle, Is.EqualTo("Login"));
            Assert.That(frameContent, Is.EqualTo(html));
            Assert.That(frameEvaluatedTitle, Is.EqualTo("Login"));
            Assert.That(frameEvaluatedUrl, Is.EqualTo(targetUrl.ToString()));
            Assert.That(elementEvaluatedTitle, Is.EqualTo("Login"));
            Assert.That(pageHasEvent, Is.True);
            Assert.That(pageEvent, Is.Not.Null);
            Assert.That(pageEvent!.WindowId, Is.EqualTo(page.WindowId));
            Assert.That(pageEvent.TabId, Is.EqualTo(page.TabId));
            Assert.That(pageBridgeEvents, Is.EqualTo(ExpectedBridgeOrder));
            Assert.That(pageLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(pageLifecycleArgs, Has.Count.EqualTo(3));
            Assert.That(pageLifecycleArgs.All(args => ReferenceEquals(args.Window, window)), Is.True);
            Assert.That(pageLifecycleArgs.All(args => ReferenceEquals(args.Page, page)), Is.True);
            Assert.That(pageLifecycleArgs.All(args => ReferenceEquals(args.Frame, page.MainFrame)), Is.True);
            Assert.That(windowHasEvent, Is.True);
            Assert.That(windowEvent, Is.Not.Null);
            Assert.That(windowEvent!.WindowId, Is.EqualTo(window.WindowId));
            Assert.That(windowEvent.TabId, Is.EqualTo(page.TabId));
            Assert.That(windowBridgeEvents, Is.EqualTo(ExpectedBridgeOrder));
            Assert.That(windowLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(browserHasEvent, Is.True);
            Assert.That(browserEvent, Is.Not.Null);
            Assert.That(browserEvent!.WindowId, Is.EqualTo(window.WindowId));
            Assert.That(browserEvent.TabId, Is.EqualTo(page.TabId));
            Assert.That(browserBridgeEvents, Is.EqualTo(ExpectedBridgeOrder));
            Assert.That(browserLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(frameLifecycleEvents, Is.EqualTo(ExpectedLifecycleOrder));
            Assert.That(frameLifecycleArgs, Has.Count.EqualTo(3));
            Assert.That(frameLifecycleArgs.All(args => ReferenceEquals(args.Window, window)), Is.True);
            Assert.That(frameLifecycleArgs.All(args => ReferenceEquals(args.Page, page)), Is.True);
            Assert.That(frameLifecycleArgs.All(args => ReferenceEquals(args.Frame, page.MainFrame)), Is.True);
        });
    }

    [Test]
    public async Task NavigateAsyncSupportsReloadBackAndForwardHistoryState()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = browser.CurrentPage;
        var firstUrl = new Uri("https://127.0.0.1/first");
        var secondUrl = new Uri("https://127.0.0.1/second");

        await page.NavigateAsync(firstUrl, new NavigationSettings
        {
            Html = "<html><head><title>First</title></head><body>1</body></html>",
        });

        await page.NavigateAsync(secondUrl, new NavigationSettings
        {
            Html = "<html><head><title>Second</title></head><body>2</body></html>",
        });

        var backResponse = await page.NavigateAsync(new Uri("https://ignored.local/back"), NavigationKind.Back);
        var backUrl = await page.GetUrlAsync();
        var backTitle = await page.GetTitleAsync();

        var forwardResponse = await page.NavigateAsync(new Uri("https://ignored.local/forward"), NavigationKind.Forward);
        var forwardUrl = await page.GetUrlAsync();
        var forwardTitle = await page.GetTitleAsync();

        var reloadResponse = await page.ReloadAsync();
        var reloadedUrl = await page.GetUrlAsync();
        var reloadedTitle = await page.GetTitleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(backResponse.RequestMessage!.RequestUri, Is.EqualTo(firstUrl));
            Assert.That(backUrl, Is.EqualTo(firstUrl));
            Assert.That(backTitle, Is.EqualTo("First"));
            Assert.That(forwardResponse.RequestMessage!.RequestUri, Is.EqualTo(secondUrl));
            Assert.That(forwardUrl, Is.EqualTo(secondUrl));
            Assert.That(forwardTitle, Is.EqualTo("Second"));
            Assert.That(reloadResponse.RequestMessage!.RequestUri, Is.EqualTo(secondUrl));
            Assert.That(reloadedUrl, Is.EqualTo(secondUrl));
            Assert.That(reloadedTitle, Is.EqualTo("Second"));
        });
    }

    [Test]
    public async Task ElementReadFallbacksUseTransportBackedRootMarkupSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = browser.CurrentPage;
        const string html = "<input id=\"login\" class=\"primary secondary\" value=\"alice\" data-test=\"42\" role=\"textbox\" tabindex=\"5\" contenteditable=\"true\" draggable=\"true\" checked />";

        await page.NavigateAsync(new Uri("https://127.0.0.1/form"), new NavigationSettings
        {
            Html = html,
        });

        var element = new Element((WebPage)page);
        var classList = (await element.GetClassListAsync()).ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(await element.GetInnerHtmlAsync(), Is.EqualTo(string.Empty));
            Assert.That(await element.GetInnerTextAsync(), Is.EqualTo(string.Empty));
            Assert.That(await element.GetValueAsync(), Is.EqualTo("alice"));
            Assert.That(await element.GetAttributeAsync("id"), Is.EqualTo("login"));
            Assert.That(await element.GetPropertyAsync("value"), Is.EqualTo("alice"));
            Assert.That(await element.GetCustomDataAsync("test"), Is.EqualTo("42"));
            Assert.That(await element.GetRoleAsync(), Is.EqualTo("textbox"));
            Assert.That(await element.GetTabIndexAsync(), Is.EqualTo(5));
            Assert.That(await element.IsCheckedAsync(), Is.True);
            Assert.That(await element.IsContentEditableAsync(), Is.True);
            Assert.That(await element.IsDraggableAsync(), Is.True);
            Assert.That(await element.IsEditableAsync(), Is.True);
            Assert.That(classList, Has.Length.EqualTo(2));
            Assert.That(classList, Does.Contain("primary"));
            Assert.That(classList, Does.Contain("secondary"));
            Assert.That(await element.GetElementHandleAsync(), Is.Not.Null.And.Not.Empty);
            Assert.That(await element.GetElementPathAsync(), Is.EqualTo("/input[1]"));
        });
    }

    [Test]
    public async Task PageBoundBridgeCommandsOverrideFrameMetadataQueries()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var titleTask = page.GetTitleAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, page.TabId, "\"Bridge Title\"").ConfigureAwait(false);
        var title = await titleTask.ConfigureAwait(false);

        var urlTask = page.GetUrlAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, "\"https://bridge.test/runtime\"").ConfigureAwait(false);
        var url = await urlTask.ConfigureAwait(false);

        var contentTask = page.GetContentAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetContent, page.TabId, "\"<html>bridge-content</html>\"").ConfigureAwait(false);
        var content = await contentTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("Bridge Title"));
            Assert.That(url, Is.EqualTo(new Uri("https://bridge.test/runtime")));
            Assert.That(content, Is.EqualTo("<html>bridge-content</html>"));
        });
    }

    [Test]
    public async Task PageBoundBridgeCommandsCanDispatchReload()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var reloadTask = page.BridgeCommands!.ReloadAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.Reload, page.TabId, "null").ConfigureAwait(false);
        await reloadTask.ConfigureAwait(false);
    }

    [Test]
    public async Task PageReloadAsyncUsesBridgeCommandWhenCurrentSnapshotCameFromLiveLifecycle()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var liveUrl = new Uri("https://bridge.test/live-reload");

        using (var payload = JsonDocument.Parse($$"""
            {
              "href": "{{liveUrl.AbsoluteUri}}",
              "title": "Live Reload"
            }
            """))
        {
            await page.ReceiveBridgeEventAsync(new BridgeMessage
            {
                Id = "live-reload-snapshot",
                Type = BridgeMessageType.Event,
                WindowId = page.WindowId,
                TabId = page.TabId,
                Event = BridgeEvent.NavigationCompleted,
                Payload = payload.RootElement.Clone(),
            }).ConfigureAwait(false);
        }

        var reloadTask = page.ReloadAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, $"\"{liveUrl.AbsoluteUri}\"").ConfigureAwait(false);
        await RespondToBridgeCommandAsync(socket, BridgeCommand.Reload, page.TabId, "null").ConfigureAwait(false);
        var response = await reloadTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.RequestMessage, Is.Not.Null);
            Assert.That(response.RequestMessage!.RequestUri, Is.EqualTo(liveUrl));
            Assert.That(response.ReasonPhrase, Is.EqualTo("Synthetic WebDriver navigation acknowledgement"));
            Assert.That(response.Headers.TryGetValues("x-atom-webdriver-navigation", out var values), Is.True);
            Assert.That(values, Is.Not.Null);
            Assert.That(values!, Does.Contain("synthetic"));
            Assert.That(page.CurrentUrl, Is.EqualTo(liveUrl));
            Assert.That(page.CurrentTitle, Is.EqualTo("Live Reload"));
        });
    }

    [Test]
    public async Task PageReloadAsyncFallsBackToLocalTransportWhenSnapshotDivergedFromLiveLifecycle()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var liveUrl = new Uri("https://bridge.test/diverged-reload");

        using (var payload = JsonDocument.Parse($$"""
            {
              "href": "{{liveUrl.AbsoluteUri}}",
              "title": "Live Title"
            }
            """))
        {
            await page.ReceiveBridgeEventAsync(new BridgeMessage
            {
                Id = "diverged-live-snapshot",
                Type = BridgeMessageType.Event,
                WindowId = page.WindowId,
                TabId = page.TabId,
                Event = BridgeEvent.NavigationCompleted,
                Payload = payload.RootElement.Clone(),
            }).ConfigureAwait(false);
        }

        _ = await page.NavigateAsync(liveUrl, new NavigationSettings
        {
            Html = "<html><head><title>Local Diverged Title</title></head><body>local</body></html>",
        }).ConfigureAwait(false);

        var reloadResponse = await page.ReloadAsync().ConfigureAwait(false);

        using var receiveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        BridgeMessage? bridgeRequest;
        try
        {
            bridgeRequest = await ReceiveBridgeMessageAsync(socket, receiveCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bridgeRequest = null;
        }

        var reloadedUrl = page.CurrentUrl;
        var reloadedTitle = page.CurrentTitle;

        Assert.Multiple(() =>
        {
            Assert.That(bridgeRequest, Is.Null, "Diverged synthetic snapshot must not trigger a bridge reload command.");
            Assert.That(reloadResponse.RequestMessage, Is.Not.Null);
            Assert.That(reloadResponse.RequestMessage!.RequestUri, Is.EqualTo(liveUrl));
            Assert.That(reloadedUrl, Is.EqualTo(liveUrl));
            Assert.That(reloadedTitle, Is.EqualTo("Local Diverged Title"));
            Assert.That(page.CurrentTitle, Is.EqualTo("Local Diverged Title"));
        });
    }

    [Test]
    public async Task PageNavigateAsyncRemainsSyntheticOnBoundLivePage()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var liveUrl = new Uri("https://bridge.test/live-before-public-navigate");
        var targetUrl = new Uri("https://bridge.test/public-navigate-local");

        using (var payload = JsonDocument.Parse($$"""
            {
              "href": "{{liveUrl.AbsoluteUri}}",
              "title": "Live Before Public Navigate"
            }
            """))
        {
            await page.ReceiveBridgeEventAsync(new BridgeMessage
            {
                Id = "public-navigate-live-snapshot",
                Type = BridgeMessageType.Event,
                WindowId = page.WindowId,
                TabId = page.TabId,
                Event = BridgeEvent.NavigationCompleted,
                Payload = payload.RootElement.Clone(),
            }).ConfigureAwait(false);
        }

        var response = await page.NavigateAsync(targetUrl, new NavigationSettings
        {
            Html = "<html><head><title>Local Public Navigate</title></head><body><main id='navigate-marker'>local</main></body></html>",
        }).ConfigureAwait(false);

        using var receiveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        BridgeMessage? bridgeRequest;
        try
        {
            bridgeRequest = await ReceiveBridgeMessageAsync(socket, receiveCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bridgeRequest = null;
        }

        Assert.Multiple(() =>
        {
            Assert.That(bridgeRequest, Is.Null, "Public NavigateAsync must stay on the synthetic/local contract even for a bridge-bound live page.");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.RequestMessage, Is.Not.Null);
            Assert.That(response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(page.CurrentUrl, Is.EqualTo(targetUrl));
            Assert.That(page.CurrentTitle, Is.EqualTo("Local Public Navigate"));
            Assert.That(page.CurrentContent, Does.Contain("navigate-marker"));
        });
    }

    [Test]
    public async Task LateBridgeDiscoveryLifecycleDoesNotOverwriteNavigatedBoundPageUrl()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        var targetUrl = new Uri("https://bridge.test/storage-isolation-page");
        var discoveryUrl = new Uri($"http://127.0.0.1:{server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/");

        _ = await page.NavigateAsync(targetUrl, new NavigationSettings
        {
            Html = "<html><head><title>Storage Isolation Page</title></head><body>storage</body></html>",
        }).ConfigureAwait(false);

        using var payload = JsonDocument.Parse($$"""
            {
              "href": "{{discoveryUrl.AbsoluteUri}}",
              "title": "Atom Bridge Discovery"
            }
            """);
        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "late-discovery-snapshot",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.NavigationCompleted,
            Payload = payload.RootElement.Clone(),
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(page.CurrentUrl, Is.EqualTo(targetUrl));
            Assert.That(page.CurrentTitle, Is.EqualTo("Storage Isolation Page"));
        });
    }

    [Test]
    public async Task PageBoundBridgeCommandsExposeRichDescribeElementAndWindowBounds()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var boundsTask = window.GetBoundingBoxAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetWindowBounds, page.TabId, "{\"left\":11,\"top\":22,\"width\":333,\"height\":444}").ConfigureAwait(false);
        var bounds = await boundsTask.ConfigureAwait(false);

        var describeTask = page.BridgeCommands!.DescribeElementAsync("element-42").AsTask();
        var describeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"SELECT\",\"checked\":false,\"selectedIndex\":1,\"isActive\":true,\"isVisible\":true,\"associatedControlId\":\"country\",\"boundingBox\":{\"left\":10.5,\"top\":20.25,\"width\":30.75,\"height\":40.5},\"computedStyle\":{\"display\":\"block\",\"opacity\":\"1\"},\"options\":[{\"value\":\"us\",\"text\":\"United States\"},{\"value\":\"ca\",\"text\":\"Canada\"}]}").ConfigureAwait(false);
        var description = await describeTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.Not.Null);
            Assert.That(bounds!.Value.X, Is.EqualTo(11));
            Assert.That(bounds.Value.Y, Is.EqualTo(22));
            Assert.That(bounds.Value.Width, Is.EqualTo(333));
            Assert.That(bounds.Value.Height, Is.EqualTo(444));
            Assert.That(describeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("element-42"));
            Assert.That(description.TagName, Is.EqualTo("SELECT"));
            Assert.That(description.Checked, Is.False);
            Assert.That(description.SelectedIndex, Is.EqualTo(1));
            Assert.That(description.IsActive, Is.True);
            Assert.That(description.IsVisible, Is.True);
            Assert.That(description.AssociatedControlId, Is.EqualTo("country"));
            Assert.That(description.BoundingBox.X, Is.EqualTo(10.5f));
            Assert.That(description.BoundingBox.Y, Is.EqualTo(20.25f));
            Assert.That(description.BoundingBox.Width, Is.EqualTo(30.75f));
            Assert.That(description.BoundingBox.Height, Is.EqualTo(40.5f));
            Assert.That(description.ComputedStyle["display"], Is.EqualTo("block"));
            Assert.That(description.ComputedStyle["opacity"], Is.EqualTo("1"));
            Assert.That(description.Options, Has.Length.EqualTo(2));
            Assert.That(description.Options[0].Value, Is.EqualTo("us"));
            Assert.That(description.Options[0].Text, Is.EqualTo("United States"));
            Assert.That(description.Options[1].Value, Is.EqualTo("ca"));
            Assert.That(description.Options[1].Text, Is.EqualTo("Canada"));
        });
    }

    [Test]
    public async Task PageAndShadowRootBridgeQueriesUseScopedShadowContext()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var outerShadowTask = page.GetShadowRootAsync("#outer-host").AsTask();
        var outerFindRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElement, page.TabId, "\"outer-host-element\"").ConfigureAwait(false);
        var outerCheckRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.CheckShadowRoot, page.TabId, "\"open\"").ConfigureAwait(false);
        var outerShadow = await outerShadowTask.ConfigureAwait(false);

        Assert.That(outerShadow, Is.Not.Null);

        var outerTextTask = outerShadow!.EvaluateAsync<string>("return shadowRoot.querySelector('b').textContent").AsTask();
        var outerExecuteRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.ExecuteScript, page.TabId, "\"outer-text\"").ConfigureAwait(false);
        var outerText = await outerTextTask.ConfigureAwait(false);

        var outerContentTask = outerShadow.GetContentAsync().AsTask();
        var outerContentRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.ExecuteScript, page.TabId, "\"<b>outer-text</b><div id='nested-host'></div>\"").ConfigureAwait(false);
        var outerContent = await outerContentTask.ConfigureAwait(false);

        var nestedShadowTask = outerShadow.GetShadowRootAsync("#nested-host").AsTask();
        var nestedFindRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElement, page.TabId, "\"nested-host-element\"").ConfigureAwait(false);
        var nestedCheckRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.CheckShadowRoot, page.TabId, "\"open\"").ConfigureAwait(false);
        var nestedShadow = await nestedShadowTask.ConfigureAwait(false);

        Assert.That(nestedShadow, Is.Not.Null);

        var nestedContentTask = nestedShadow!.GetContentAsync().AsTask();
        var nestedContentRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.ExecuteScript, page.TabId, "\"<i>inner-text</i>\"").ConfigureAwait(false);
        var nestedContent = await nestedContentTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(outerFindRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(outerFindRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("#outer-host"));
            Assert.That(outerFindRequest.Payload?.TryGetProperty("shadowHostElementId", out _), Is.False);
            Assert.That(outerCheckRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("outer-host-element"));

            Assert.That(outerExecuteRequest.Payload?.GetProperty("script").GetString(), Is.EqualTo("return shadowRoot.querySelector('b').textContent"));
            Assert.That(outerExecuteRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("outer-host-element"));
            Assert.That(outerText, Is.EqualTo("outer-text"));

            Assert.That(outerContentRequest.Payload?.GetProperty("script").GetString(), Is.EqualTo("return shadowRoot.innerHTML"));
            Assert.That(outerContentRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("outer-host-element"));
            Assert.That(outerContent, Is.EqualTo("<b>outer-text</b><div id='nested-host'></div>"));

            Assert.That(nestedFindRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(nestedFindRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("#nested-host"));
            Assert.That(nestedFindRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("outer-host-element"));
            Assert.That(nestedCheckRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("nested-host-element"));

            Assert.That(nestedContentRequest.Payload?.GetProperty("script").GetString(), Is.EqualTo("return shadowRoot.innerHTML"));
            Assert.That(nestedContentRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("nested-host-element"));
            Assert.That(nestedContent, Is.EqualTo("<i>inner-text</i>"));
        });
    }

    [Test]
    public async Task ShadowRootMetadataQueriesReuseFrameBridgeContext()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var shadowTask = page.GetShadowRootAsync("#meta-host").AsTask();
        var findRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElement, page.TabId, "\"meta-host-element\"").ConfigureAwait(false);
        var checkRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.CheckShadowRoot, page.TabId, "\"open\"").ConfigureAwait(false);
        var shadow = await shadowTask.ConfigureAwait(false);

        Assert.That(shadow, Is.Not.Null);

        var titleTask = shadow!.GetTitleAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, page.TabId, "\"Shadow Runtime Title\"").ConfigureAwait(false);
        var title = await titleTask.ConfigureAwait(false);

        var urlTask = shadow.GetUrlAsync().AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, "\"https://shadow.test/runtime\"").ConfigureAwait(false);
        var url = await urlTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(findRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(findRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("#meta-host"));
            Assert.That(checkRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("meta-host-element"));
            Assert.That(title, Is.EqualTo("Shadow Runtime Title"));
            Assert.That(url, Is.EqualTo(new Uri("https://shadow.test/runtime")));
            Assert.That(shadow.Page, Is.SameAs(page));
            Assert.That(shadow.Frame, Is.SameAs(page.MainFrame));
        });
    }

    [Test]
    public async Task ShadowRootScopedCollectionAndWaitQueriesReuseResolvedHostContext()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var shadowTask = page.GetShadowRootAsync("#items-host").AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElement, page.TabId, "\"items-host-element\"").ConfigureAwait(false);
        await RespondToBridgeCommandAsync(socket, BridgeCommand.CheckShadowRoot, page.TabId, "\"open\"").ConfigureAwait(false);
        var shadow = await shadowTask.ConfigureAwait(false);

        Assert.That(shadow, Is.Not.Null);

        var elementsTask = shadow!.GetElementsAsync(".item").AsTask();
        var elementsRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[\"shadow-item-1\",\"shadow-item-2\"]").ConfigureAwait(false);
        var elements = (await elementsTask.ConfigureAwait(false)).ToArray();
        var elementHandles = await Task.WhenAll(elements.Select(static element => element.GetElementHandleAsync().AsTask())).ConfigureAwait(false);

        var waitedTask = shadow.WaitForElementAsync("#delayed", WaitForElementKind.Visible | WaitForElementKind.Stable, TimeSpan.FromSeconds(2)).AsTask();
        var waitRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.WaitForElement, page.TabId, "\"shadow-waited-item\"").ConfigureAwait(false);
        var waited = await waitedTask.ConfigureAwait(false);
        var waitedHandle = waited is null ? null : await waited.GetElementHandleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(elementsRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(elementsRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo(".item"));
            Assert.That(elementsRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("items-host-element"));
            Assert.That(elementHandles, Is.EqualTo(ExpectedShadowItemHandles));

            Assert.That(waitRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(waitRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("#delayed"));
            Assert.That(waitRequest.Payload?.GetProperty("kind").GetString(), Is.EqualTo("Visible, Stable"));
            Assert.That(waitRequest.Payload?.GetProperty("timeoutMs").GetDouble(), Is.EqualTo(2000d));
            Assert.That(waitRequest.Payload?.GetProperty("shadowHostElementId").GetString(), Is.EqualTo("items-host-element"));
            Assert.That(waitedHandle, Is.EqualTo("shadow-waited-item"));
        });
    }

    [Test]
    public async Task FrameChildDiscoveryRequestsShadowRootTraversalOnBridgeLookup()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var childFramesTask = page.MainFrame.GetChildFramesAsync().AsTask();
        var discoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[\"shadow-frame-host-element\"]").ConfigureAwait(false);
        var childFrames = (await childFramesTask.ConfigureAwait(false)).ToArray();
        var childFrame = childFrames.SingleOrDefault();
        var pageFrames = page.Frames.ToArray();
        var frameElementHandle = childFrame is null ? null : await childFrame.GetFrameElementHandleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(discoveryRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(discoveryRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("iframe,frame"));
            Assert.That(discoveryRequest.Payload?.GetProperty("allowShadowRootDiscovery").GetBoolean(), Is.True);
            Assert.That(childFrames, Has.Length.EqualTo(1));
            Assert.That(childFrame, Is.Not.Null);
            Assert.That(frameElementHandle, Is.EqualTo("shadow-frame-host-element"));
            Assert.That(pageFrames, Has.Length.EqualTo(2));
            Assert.That(pageFrames[0], Is.SameAs(page.MainFrame));
            Assert.That(pageFrames[1], Is.SameAs(childFrame));
        });
    }

    [Test]
    public async Task PageGetShadowRootReturnsNullWhenBridgeReportsNonOpenMode()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var shadowTask = page.GetShadowRootAsync("#closed-host").AsTask();
        var findRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElement, page.TabId, "\"closed-host-element\"").ConfigureAwait(false);
        var checkRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.CheckShadowRoot, page.TabId, "\"false\"").ConfigureAwait(false);
        var shadow = await shadowTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(findRequest.Payload?.GetProperty("strategy").GetString(), Is.EqualTo("Css"));
            Assert.That(findRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("#closed-host"));
            Assert.That(checkRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("closed-host-element"));
            Assert.That(shadow, Is.Null);
        });
    }

    [Test]
    public async Task BridgeBackedElementStateQueriesUseBridgeProperties()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var interactiveElement = new Element(page, bridgeElementId: "element-interactive");
        var disabledElement = new Element(page, bridgeElementId: "element-disabled");

        var contentEditableTask = interactiveElement.IsContentEditableAsync().AsTask();
        var contentEditableRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"true\"").ConfigureAwait(false);
        var contentEditable = await contentEditableTask.ConfigureAwait(false);

        var draggableTask = interactiveElement.IsDraggableAsync().AsTask();
        var draggableRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"true\"").ConfigureAwait(false);
        var draggable = await draggableTask.ConfigureAwait(false);

        var selectedTask = interactiveElement.IsSelectedAsync().AsTask();
        var selectedRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"true\"").ConfigureAwait(false);
        var selected = await selectedTask.ConfigureAwait(false);

        var disabledTask = disabledElement.IsDisabledAsync().AsTask();
        var disabledRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"true\"").ConfigureAwait(false);
        var disabled = await disabledTask.ConfigureAwait(false);

        var editableTask = interactiveElement.IsEditableAsync().AsTask();
        var editableTagRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"INPUT\"").ConfigureAwait(false);
        var editableDisabledRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"false\"").ConfigureAwait(false);
        var editable = await editableTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(contentEditableRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("element-interactive"));
            Assert.That(contentEditableRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("isContentEditable"));
            Assert.That(contentEditable, Is.True);
            Assert.That(draggableRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("draggable"));
            Assert.That(draggable, Is.True);
            Assert.That(selectedRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("selected"));
            Assert.That(selected, Is.True);
            Assert.That(disabledRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("element-disabled"));
            Assert.That(disabledRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("disabled"));
            Assert.That(disabled, Is.True);
            Assert.That(editableTagRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("tagName"));
            Assert.That(editableDisabledRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("disabled"));
            Assert.That(editable, Is.True);
        });
    }

    [Test]
    public async Task BridgeBackedChildFrameUsesHostElementVisibilityAndBounds()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var boundsTask = childFrame.GetBoundingBoxAsync().AsTask();
        var boundsRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var bounds = await boundsTask.ConfigureAwait(false);

        var visibleTask = childFrame.IsVisibleAsync().AsTask();
        var visibleRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var visible = await visibleTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(boundsRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(visibleRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(bounds, Is.Not.Null);
            Assert.That(bounds!.Value.X, Is.EqualTo(40));
            Assert.That(bounds.Value.Y, Is.EqualTo(50));
            Assert.That(bounds.Value.Width, Is.EqualTo(320));
            Assert.That(bounds.Value.Height, Is.EqualTo(180));
            Assert.That(visible, Is.True);
        });
    }

    [Test]
    public async Task BridgeBackedChildFrameReadsNameFromHostBridgeProperty()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var nameTask = childFrame.GetNameAsync().AsTask();
        var nameRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"bridge-child-frame\"").ConfigureAwait(false);
        var name = await nameTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(nameRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(nameRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("name"));
            Assert.That(name, Is.EqualTo("bridge-child-frame"));
        });
    }

    [Test]
    public async Task BridgeBackedChildFrameUsesHiddenHostVisibilityState()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var visibleTask = childFrame.IsVisibleAsync().AsTask();
        var visibleRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isVisible\":false,\"boundingBox\":{\"left\":0,\"top\":0,\"width\":0,\"height\":0},\"computedStyle\":{\"display\":\"none\"},\"options\":[]}").ConfigureAwait(false);
        var visible = await visibleTask.ConfigureAwait(false);

        var boundsTask = childFrame.GetBoundingBoxAsync().AsTask();
        var boundsRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isVisible\":false,\"boundingBox\":{\"left\":0,\"top\":0,\"width\":0,\"height\":0},\"computedStyle\":{\"display\":\"none\"},\"options\":[]}").ConfigureAwait(false);
        var bounds = await boundsTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(visibleRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(boundsRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(visible, Is.False);
            Assert.That(bounds, Is.Not.Null);
            Assert.That(bounds!.Value, Is.EqualTo(new Rectangle(0, 0, 0, 0)));
        });
    }

    [Test]
    public async Task FrameDetachedEventMarksMatchingChildFrameDetached()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);

        using var payload = JsonDocument.Parse("""
            {
              "frameElementId": "iframe-host-element"
            }
            """);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "frame-detached-event",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.FrameDetached,
            Payload = payload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var childDetached = await childFrame.IsDetachedAsync().ConfigureAwait(false);
        var mainDetached = await mainFrame.IsDetachedAsync().ConfigureAwait(false);
        var remainingChildFrames = (await mainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var pageFrames = page.Frames.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(childDetached, Is.True);
            Assert.That(mainDetached, Is.False);
            Assert.That(remainingChildFrames, Is.Empty, "Detached child frames must be pruned from the main-frame snapshot after a FrameDetached event.");
            Assert.That(pageFrames, Has.Length.EqualTo(1), "Page frame snapshot must drop detached child frames after a FrameDetached event.");
            Assert.That(pageFrames[0], Is.SameAs(mainFrame));
        });
    }

    [Test]
    public async Task BridgeBackedChildFrameTreatsDisconnectedHostDescriptionAsDetached()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var detachedTask = childFrame.IsDetachedAsync().AsTask();
        var describeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isConnected\":false,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var detached = await detachedTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(describeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(detached, Is.True);
        });
    }

    [Test]
    public async Task BridgeBackedChildFrameTreatsMissingHostDescriptionAsDetached()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element");
        var childFrame = new Frame(page, mainFrame, hostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var detachedTask = childFrame.IsDetachedAsync().AsTask();
        var describeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "null", BridgeStatus.NotFound).ConfigureAwait(false);
        var detached = await detachedTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(describeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element"));
            Assert.That(detached, Is.True);
        });
    }

    [Test]
    public async Task PageFrameLookupByNameSkipsDetachedBridgeChildFrameAndResolvesFreshSuccessor()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var detachedHostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element-detached");
        var detachedFrame = new Frame(page, mainFrame, detachedHostElement);
        var attachedHostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element-attached");
        var attachedFrame = new Frame(page, mainFrame, attachedHostElement);
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var lookupTask = page.GetFrameAsync("bridge-reattached-frame").AsTask();
        var rootDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var detachedChildDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var attachedChildDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var detachedDescribeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isConnected\":false,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var attachedDescribeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isConnected\":true,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var nameRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetElementProperty, page.TabId, "\"bridge-reattached-frame\"").ConfigureAwait(false);
        var resolvedFrame = await lookupTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(detachedFrame, Is.Not.SameAs(attachedFrame));
            Assert.That(rootDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False, "Root frame discovery must start without a frameHostElementId payload.");
            Assert.That(detachedChildDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False, "Bridge discovery currently uses the shared frame discovery payload shape for existing child frames as well.");
            Assert.That(attachedChildDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False, "Bridge discovery currently uses the shared frame discovery payload shape for existing child frames as well.");
            Assert.That(detachedDescribeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element-detached"));
            Assert.That(attachedDescribeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element-attached"));
            Assert.That(nameRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element-attached"));
            Assert.That(nameRequest.Payload?.GetProperty("propertyName").GetString(), Is.EqualTo("name"));
            Assert.That(resolvedFrame, Is.SameAs(attachedFrame));
        });
    }

    [Test]
    public async Task PageFrameLookupByUrlSkipsDetachedBridgeChildFrameAndResolvesFreshSuccessor()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var detachedHostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element-detached");
        var detachedFrame = new Frame(page, mainFrame, detachedHostElement);
        var attachedHostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element-attached");
        var attachedFrame = new Frame(page, mainFrame, attachedHostElement);
        var attachedFrameUrl = new Uri("https://bridge.test/reattached-frame");
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var lookupTask = page.GetFrameAsync(attachedFrameUrl).AsTask();
        var rootDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var detachedChildDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var attachedChildDiscoveryRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.FindElements, page.TabId, "[]").ConfigureAwait(false);
        var mainFrameUrlRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, "\"https://bridge.test/main-frame\"").ConfigureAwait(false);
        var detachedDescribeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isConnected\":false,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var attachedDescribeRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.DescribeElement, page.TabId, "{\"tagName\":\"IFRAME\",\"checked\":false,\"selectedIndex\":-1,\"isActive\":false,\"isConnected\":true,\"isVisible\":true,\"boundingBox\":{\"left\":40,\"top\":50,\"width\":320,\"height\":180},\"computedStyle\":{\"display\":\"block\"},\"options\":[]}").ConfigureAwait(false);
        var urlRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.ExecuteScript, page.TabId, "\"https://bridge.test/reattached-frame\"").ConfigureAwait(false);
        var resolvedFrame = await lookupTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(detachedFrame, Is.Not.SameAs(attachedFrame));
            Assert.That(rootDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False);
            Assert.That(detachedChildDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False);
            Assert.That(attachedChildDiscoveryRequest.Payload?.TryGetProperty("frameHostElementId", out _), Is.False);
            Assert.That(mainFrameUrlRequest.Payload, Is.Null, "Main frame URL lookup should use the page-level GetUrl command without an element payload.");
            Assert.That(detachedDescribeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element-detached"));
            Assert.That(attachedDescribeRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("iframe-host-element-attached"));
            Assert.That(urlRequest.Payload?.GetProperty("frameHostElementId").GetString(), Is.EqualTo("iframe-host-element-attached"));
            Assert.That(urlRequest.Payload?.GetProperty("script").GetString(), Does.Contain("globalThis.location?.href"));
            Assert.That(resolvedFrame, Is.SameAs(attachedFrame));
        });
    }

    [Test]
    public async Task PageBoundBridgeAttachVirtualMediaSendsSetTabContextPayload()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-native"));
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-native"));
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
            Name = "Bridge Camera",
            DeviceId = "bridge-bundle",
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Bridge Microphone",
            DeviceId = "bridge-bundle",
        }).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var attachCameraTask = page.AttachVirtualCameraAsync(camera).AsTask();
        var cameraRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.SetTabContext, page.TabId, "null").ConfigureAwait(false);
        await attachCameraTask.ConfigureAwait(false);

        var attachMicrophoneTask = page.AttachVirtualMicrophoneAsync(microphone).AsTask();
        var microphoneRequest = await RespondToBridgeCommandAsync(socket, BridgeCommand.SetTabContext, page.TabId, "null").ConfigureAwait(false);
        await attachMicrophoneTask.ConfigureAwait(false);

        var cameraMediaDevices = cameraRequest.Payload?.GetProperty("virtualMediaDevices");
        var microphoneMediaDevices = microphoneRequest.Payload?.GetProperty("virtualMediaDevices");
        var cameraContextId = cameraRequest.Payload?.GetProperty("contextId").GetString();
        var microphoneContextId = microphoneRequest.Payload?.GetProperty("contextId").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(cameraRequest.Payload?.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
            Assert.That(cameraRequest.Payload?.GetProperty("tabId").GetString(), Is.EqualTo(page.TabId));
            Assert.That(cameraContextId, Is.Not.Null.And.Not.Empty);
            Assert.That(cameraMediaDevices.HasValue, Is.True);
            Assert.That(cameraMediaDevices?.GetProperty("videoInputLabel").GetString(), Is.EqualTo("Bridge Camera"));
            Assert.That(cameraMediaDevices?.GetProperty("videoInputBrowserDeviceId").GetString(), Is.EqualTo("camera-native"));
            Assert.That(microphoneRequest.Payload?.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
            Assert.That(microphoneRequest.Payload?.GetProperty("tabId").GetString(), Is.EqualTo(page.TabId));
            Assert.That(microphoneContextId, Is.EqualTo(cameraContextId));
            Assert.That(microphoneMediaDevices.HasValue, Is.True);
            Assert.That(microphoneMediaDevices?.GetProperty("videoInputLabel").GetString(), Is.EqualTo("Bridge Camera"));
            Assert.That(microphoneMediaDevices?.GetProperty("videoInputBrowserDeviceId").GetString(), Is.EqualTo("camera-native"));
            Assert.That(microphoneMediaDevices?.GetProperty("audioInputLabel").GetString(), Is.EqualTo("Bridge Microphone"));
            Assert.That(microphoneMediaDevices?.GetProperty("audioInputBrowserDeviceId").GetString(), Is.EqualTo("microphone-native"));
            Assert.That(microphoneMediaDevices?.GetProperty("groupId").GetString(), Is.EqualTo("bridge-bundle"));
        });
    }

    [Test]
    public async Task BrowserAndWindowLookupUseBridgeTitleAndUrlMetadata()
    {
        const string targetTitle = "Bridge Lookup Title";
        const string targetUrl = "https://bridge.lookup/runtime";

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var titlePayload = '"' + targetTitle + '"';
        var urlPayload = '"' + targetUrl + '"';

        var browserWindowByTitleTask = browser.GetWindowAsync(targetTitle).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, page.TabId, titlePayload).ConfigureAwait(false);
        var browserWindowByTitle = await browserWindowByTitleTask.ConfigureAwait(false);

        var browserPageByTitleTask = browser.GetPageAsync(targetTitle).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, page.TabId, titlePayload).ConfigureAwait(false);
        var browserPageByTitle = await browserPageByTitleTask.ConfigureAwait(false);

        var windowPageByTitleTask = window.GetPageAsync(targetTitle).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetTitle, page.TabId, titlePayload).ConfigureAwait(false);
        var windowPageByTitle = await windowPageByTitleTask.ConfigureAwait(false);

        var targetUri = new Uri(targetUrl);

        var browserWindowByUrlTask = browser.GetWindowAsync(targetUri).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, urlPayload).ConfigureAwait(false);
        var browserWindowByUrl = await browserWindowByUrlTask.ConfigureAwait(false);

        var browserPageByUrlTask = browser.GetPageAsync(targetUri).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, urlPayload).ConfigureAwait(false);
        var browserPageByUrl = await browserPageByUrlTask.ConfigureAwait(false);

        var windowPageByUrlTask = window.GetPageAsync(targetUri).AsTask();
        await RespondToBridgeCommandAsync(socket, BridgeCommand.GetUrl, page.TabId, urlPayload).ConfigureAwait(false);
        var windowPageByUrl = await windowPageByUrlTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browserWindowByTitle, Is.SameAs(window));
            Assert.That(browserPageByTitle, Is.SameAs(page));
            Assert.That(windowPageByTitle, Is.SameAs(page));
            Assert.That(browserWindowByUrl, Is.SameAs(window));
            Assert.That(browserPageByUrl, Is.SameAs(page));
            Assert.That(windowPageByUrl, Is.SameAs(page));
        });
    }

    [Test]
    public async Task BrowserAndWindowLookupPreferLocalSnapshotsForBoundNonCurrentPages()
    {
        const string hiddenTitle = "Hidden Snapshot Title";
        const string currentTitle = "Current Snapshot Title";
        const string bridgeTitle = "Bridge Live Title";
        const string bridgeUrl = "https://bridge.lookup/live";
        var hiddenUrl = new Uri("https://snapshot.lookup/hidden");
        var currentUrl = new Uri("https://snapshot.lookup/current");

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var window = (WebWindow)browser.CurrentWindow;
        var hiddenPage = (WebPage)window.CurrentPage;
        var currentPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);

        await hiddenPage.NavigateAsync(hiddenUrl, new NavigationSettings
        {
            Html = $"<html><head><title>{hiddenTitle}</title></head><body>hidden</body></html>",
        }).ConfigureAwait(false);
        await currentPage.NavigateAsync(currentUrl, new NavigationSettings
        {
            Html = $"<html><head><title>{currentTitle}</title></head><body>current</body></html>",
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentPage, Is.SameAs(currentPage));
            Assert.That(hiddenPage.CurrentTitle, Is.EqualTo(hiddenTitle));
            Assert.That(hiddenPage.CurrentUrl, Is.EqualTo(hiddenUrl));
            Assert.That(currentPage.CurrentTitle, Is.EqualTo(currentTitle));
            Assert.That(currentPage.CurrentUrl, Is.EqualTo(currentUrl));
        });

        await using var server = await StartBoundBridgeAsync(hiddenPage).ConfigureAwait(false);

        async Task<(T Result, BridgeMessage? Request)> CompleteBoundLookupAsync<T>(Func<Task<T>> lookupFactory, BridgeCommand command, string fallbackPayload)
        {
            using var lookupSocket = await ConnectBridgeSocketAsync(server, hiddenPage.TabId).ConfigureAwait(false);

            try
            {
                return await CompleteLookupWithOptionalBridgeFallbackAsync(
                    lookupFactory(),
                    lookupSocket,
                    hiddenPage.TabId,
                    command,
                    fallbackPayload).ConfigureAwait(false);
            }
            finally
            {
                lookupSocket.Dispose();
                await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
            }
        }

        var (browserWindowByHiddenTitle, browserWindowTitleRequest) = await CompleteBoundLookupAsync(
            () => browser.GetWindowAsync(hiddenTitle).AsTask(),
            BridgeCommand.GetTitle,
            '"' + bridgeTitle + '"').ConfigureAwait(false);

        var (browserWindowByHiddenUrl, browserWindowUrlRequest) = await CompleteBoundLookupAsync(
            () => browser.GetWindowAsync(hiddenUrl).AsTask(),
            BridgeCommand.GetUrl,
            '"' + bridgeUrl + '"').ConfigureAwait(false);

        var (browserPageByHiddenTitle, browserPageTitleRequest) = await CompleteBoundLookupAsync(
            () => browser.GetPageAsync(hiddenTitle).AsTask(),
            BridgeCommand.GetTitle,
            '"' + bridgeTitle + '"').ConfigureAwait(false);

        var (browserPageByHiddenUrl, browserPageUrlRequest) = await CompleteBoundLookupAsync(
            () => browser.GetPageAsync(hiddenUrl).AsTask(),
            BridgeCommand.GetUrl,
            '"' + bridgeUrl + '"').ConfigureAwait(false);

        var (windowPageByHiddenTitle, windowPageTitleRequest) = await CompleteBoundLookupAsync(
            () => window.GetPageAsync(hiddenTitle).AsTask(),
            BridgeCommand.GetTitle,
            '"' + bridgeTitle + '"').ConfigureAwait(false);

        var (windowPageByHiddenUrl, windowPageUrlRequest) = await CompleteBoundLookupAsync(
            () => window.GetPageAsync(hiddenUrl).AsTask(),
            BridgeCommand.GetUrl,
            '"' + bridgeUrl + '"').ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browserWindowByHiddenTitle, Is.Null, "Window lookup by title must remain scoped to each window's current page.");
            Assert.That(browserWindowTitleRequest, Is.Null, "Non-current page title lookup must not fall through to the bridge for window resolution.");
            Assert.That(browserWindowByHiddenUrl, Is.SameAs(window));
            Assert.That(browserWindowUrlRequest, Is.Null, "Window lookup by URL must use the page snapshot before any live bridge fallback.");
            Assert.That(browserPageByHiddenTitle, Is.SameAs(hiddenPage));
            Assert.That(browserPageTitleRequest, Is.Null, "Browser page lookup by title must use the page snapshot before any live bridge fallback.");
            Assert.That(browserPageByHiddenUrl, Is.SameAs(hiddenPage));
            Assert.That(browserPageUrlRequest, Is.Null, "Browser page lookup by URL must use the page snapshot before any live bridge fallback.");
            Assert.That(windowPageByHiddenTitle, Is.SameAs(hiddenPage));
            Assert.That(windowPageTitleRequest, Is.Null, "Window page lookup by title must use the page snapshot before any live bridge fallback.");
            Assert.That(windowPageByHiddenUrl, Is.SameAs(hiddenPage));
            Assert.That(windowPageUrlRequest, Is.Null, "Window page lookup by URL must use the page snapshot before any live bridge fallback.");
        });
    }

    [Test]
    public async Task PageBoundBridgeSetCookiesAsyncSendsCookieAttributes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);
        var expiresAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cookie = new Cookie("session", "alpha", "/account", "example.com")
        {
            Secure = true,
            HttpOnly = true,
            Expires = expiresAt,
        };

        var setCookieTask = page.SetCookiesAsync([cookie]).AsTask();
        var request = await RespondToBridgeCommandAsync(socket, BridgeCommand.SetCookie, page.TabId, "null").ConfigureAwait(false);
        await setCookieTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(request.Payload?.GetProperty("contextId").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(request.Payload?.GetProperty("name").GetString(), Is.EqualTo("session"));
            Assert.That(request.Payload?.GetProperty("value").GetString(), Is.EqualTo("alpha"));
            Assert.That(request.Payload?.GetProperty("domain").GetString(), Is.EqualTo("example.com"));
            Assert.That(request.Payload?.GetProperty("path").GetString(), Is.EqualTo("/account"));
            Assert.That(request.Payload?.GetProperty("secure").GetBoolean(), Is.True);
            Assert.That(request.Payload?.GetProperty("httpOnly").GetBoolean(), Is.True);
            Assert.That(request.Payload?.GetProperty("expires").GetInt64(), Is.EqualTo(new DateTimeOffset(expiresAt).ToUnixTimeSeconds()));
        });
    }

    [Test]
    public async Task PageBoundBridgeGetAllCookiesAsyncReadsCookieAttributes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await using var server = await StartBoundBridgeAsync(page).ConfigureAwait(false);
        using var socket = await ConnectBridgeSocketAsync(server, page.TabId).ConfigureAwait(false);

        var readCookiesTask = page.GetAllCookiesAsync().AsTask();
        const string payload = "[{\"name\":\"session\",\"value\":\"alpha\",\"domain\":\"example.com\",\"path\":\"/account\",\"secure\":true,\"httpOnly\":true,\"expires\":1735689600}]";
        var request = await RespondToBridgeCommandAsync(socket, BridgeCommand.GetCookies, page.TabId, payload).ConfigureAwait(false);
        var cookies = (await readCookiesTask.ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(request.Payload?.GetProperty("contextId").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(cookies, Has.Length.EqualTo(1));
            Assert.That(cookies[0].Name, Is.EqualTo("session"));
            Assert.That(cookies[0].Value, Is.EqualTo("alpha"));
            Assert.That(cookies[0].Domain, Is.EqualTo("example.com"));
            Assert.That(cookies[0].Path, Is.EqualTo("/account"));
            Assert.That(cookies[0].Secure, Is.True);
            Assert.That(cookies[0].HttpOnly, Is.True);
            Assert.That(cookies[0].Expires, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1735689600).UtcDateTime));
        });
    }

    private static async Task<BridgeServer> StartBoundBridgeAsync(WebPage page)
    {
        var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        try
        {
            await server.StartAsync().ConfigureAwait(false);
            page.BindBridgeCommands("session-a", server.Commands);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<ClientWebSocket> ConnectBridgeSocketAsync(BridgeServer server, string tabId)
    {
        var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
            await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "test-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0")).ConfigureAwait(false);
            _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
            await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);
            await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: tabId, windowId: "window-1")).ConfigureAwait(false);
            _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static Task<BridgeMessage> RespondToBridgeCommandAsync(ClientWebSocket socket, BridgeCommand command, string tabId, string payloadJson)
        => RespondToBridgeCommandAsync(socket, command, tabId, payloadJson, BridgeStatus.Ok);

    private static async Task<BridgeMessage> RespondToBridgeCommandAsync(ClientWebSocket socket, BridgeCommand command, string tabId, string payloadJson, BridgeStatus status)
    {
        var request = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Command, Is.EqualTo(command));
        Assert.That(request.TabId, Is.EqualTo(tabId));

        using var payload = JsonDocument.Parse(payloadJson);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = request.Id,
            Type = BridgeMessageType.Response,
            TabId = tabId,
            Status = status,
            Payload = payload.RootElement.Clone(),
        }).ConfigureAwait(false);

        return request;
    }

    private static async Task<(T Result, BridgeMessage? Request)> CompleteLookupWithOptionalBridgeFallbackAsync<T>(
        Task<T> lookupTask,
        ClientWebSocket socket,
        string tabId,
        BridgeCommand expectedCommand,
        string fallbackPayloadJson)
    {
        using var receiveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var receiveTask = ReceiveBridgeMessageAsync(socket, receiveCts.Token);
        var firstCompleted = await Task.WhenAny(lookupTask, receiveTask).ConfigureAwait(false);
        BridgeMessage? request = null;

        if (ReferenceEquals(firstCompleted, receiveTask))
        {
            request = await receiveTask.ConfigureAwait(false);
            if (request is not null)
            {
                Assert.That(request.Command, Is.EqualTo(expectedCommand));
                Assert.That(request.TabId, Is.EqualTo(tabId));

                using var payload = JsonDocument.Parse(fallbackPayloadJson);
                await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
                {
                    Id = request.Id,
                    Type = BridgeMessageType.Response,
                    TabId = tabId,
                    Status = BridgeStatus.Ok,
                    Payload = payload.RootElement.Clone(),
                }).ConfigureAwait(false);
            }
        }
        else
        {
            receiveCts.Cancel();

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        var lookupCompleted = await Task.WhenAny(lookupTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        Assert.That(lookupCompleted, Is.SameAs(lookupTask), "Lookup should complete without waiting for an extra bridge roundtrip.");
        return (await lookupTask.ConfigureAwait(false), request);
    }

    private static async Task<BridgeMessage?> ReceiveBridgeMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return result.MessageType is not WebSocketMessageType.Text
            ? null
            : JsonSerializer.Deserialize(buffer.AsSpan(0, result.Count), BridgeJsonContext.Default.BridgeMessage);
    }

    private static void SubscribeLifecycle(WebPage page, List<string> events, List<WebLifecycleEventArgs>? argsSink = null)
    {
        page.DomContentLoaded += (_, args) => RecordLifecycle(events, argsSink, "DomContentLoaded", args);
        page.NavigationCompleted += (_, args) => RecordLifecycle(events, argsSink, "NavigationCompleted", args);
        page.PageLoaded += (_, args) => RecordLifecycle(events, argsSink, "PageLoaded", args);
    }

    private static void SubscribeLifecycle(WebWindow window, List<string> events, List<WebLifecycleEventArgs>? argsSink = null)
    {
        window.DomContentLoaded += (_, args) => RecordLifecycle(events, argsSink, "DomContentLoaded", args);
        window.NavigationCompleted += (_, args) => RecordLifecycle(events, argsSink, "NavigationCompleted", args);
        window.PageLoaded += (_, args) => RecordLifecycle(events, argsSink, "PageLoaded", args);
    }

    private static void SubscribeLifecycle(RuntimeWebBrowser browser, List<string> events, List<WebLifecycleEventArgs>? argsSink = null)
    {
        browser.DomContentLoaded += (_, args) => RecordLifecycle(events, argsSink, "DomContentLoaded", args);
        browser.NavigationCompleted += (_, args) => RecordLifecycle(events, argsSink, "NavigationCompleted", args);
        browser.PageLoaded += (_, args) => RecordLifecycle(events, argsSink, "PageLoaded", args);
    }

    private static void SubscribeLifecycle(Frame frame, List<string> events, List<WebLifecycleEventArgs>? argsSink = null)
    {
        frame.DomContentLoaded += (_, args) => RecordLifecycle(events, argsSink, "DomContentLoaded", args);
        frame.NavigationCompleted += (_, args) => RecordLifecycle(events, argsSink, "NavigationCompleted", args);
        frame.PageLoaded += (_, args) => RecordLifecycle(events, argsSink, "PageLoaded", args);
    }

    private static void RecordLifecycle(List<string> events, List<WebLifecycleEventArgs>? argsSink, string name, WebLifecycleEventArgs args)
    {
        events.Add(name);
        argsSink?.Add(args);
    }

    private sealed class FakeVirtualCameraBackend(string deviceIdentifier = "fake-camera") : IVirtualCameraBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(CameraControlType control, float value)
            => ControlChanged?.Invoke(this, new CameraControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(CameraControlType control) => 0.0f;

        public CameraControlRange? GetControlRange(CameraControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeVirtualMicrophoneBackend(string deviceIdentifier = "fake-microphone") : IVirtualMicrophoneBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteSamples(ReadOnlySpan<byte> sampleData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(MicrophoneControlType control, float value)
            => ControlChanged?.Invoke(this, new MicrophoneControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(MicrophoneControlType control) => 0.0f;

        public MicrophoneControlRange? GetControlRange(MicrophoneControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}