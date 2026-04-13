using System.Net;
using System.Text;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeEventDispatchTests
{
    private delegate bool TryDequeueBridgeMessage(out BridgeMessage? message);
    private static readonly string[] ExpectedSubscriberOrder = ["first", "second", "third"];
    private static readonly BridgeEvent[] ExpectedCallbackRelay = [BridgeEvent.Callback, BridgeEvent.CallbackFinalized];
    private static readonly BridgeEvent[] ExpectedLifecycleRelay = [BridgeEvent.DomContentLoaded, BridgeEvent.NavigationCompleted, BridgeEvent.PageLoaded];
    private static readonly BridgeEvent[] ExpectedInterceptedNavigationRelay = [BridgeEvent.RequestIntercepted, BridgeEvent.ResponseReceived, BridgeEvent.DomContentLoaded, BridgeEvent.NavigationCompleted, BridgeEvent.PageLoaded];

    [Test]
    public async Task NavigateAsyncDispatchesRequestAndResponseAcrossScopesWhenInterceptionEnabled()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var targetUrl = new Uri("https://127.0.0.1/dispatch");
        var body = "payload"u8.ToArray();
        var settings = new NavigationSettings
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Dispatch"] = "enabled",
            },
            Body = body,
            Html = "<html><head><title>Dispatch</title></head><body>ok</body></html>",
        };

        List<InterceptedRequestEventArgs> pageRequests = [];
        List<InterceptedRequestEventArgs> windowRequests = [];
        List<InterceptedRequestEventArgs> browserRequests = [];
        List<InterceptedResponseEventArgs> pageResponses = [];
        List<InterceptedResponseEventArgs> windowResponses = [];
        List<InterceptedResponseEventArgs> browserResponses = [];

        page.Request += (_, e) =>
        {
            pageRequests.Add(e);
            return ValueTask.CompletedTask;
        };
        window.Request += (_, e) =>
        {
            windowRequests.Add(e);
            return ValueTask.CompletedTask;
        };
        browser.Request += (_, e) =>
        {
            browserRequests.Add(e);
            return ValueTask.CompletedTask;
        };
        page.Response += (_, e) =>
        {
            pageResponses.Add(e);
            return ValueTask.CompletedTask;
        };
        window.Response += (_, e) =>
        {
            windowResponses.Add(e);
            return ValueTask.CompletedTask;
        };
        browser.Response += (_, e) =>
        {
            browserResponses.Add(e);
            return ValueTask.CompletedTask;
        };

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(targetUrl, settings).ConfigureAwait(false);
        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var windowEvents = DrainEventKinds(window.TryDequeueBridgeEvent);
        var browserEvents = DrainEventKinds(browser.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pageEvents, Is.EqualTo(ExpectedInterceptedNavigationRelay));
            Assert.That(windowEvents, Is.EqualTo(pageEvents));
            Assert.That(browserEvents, Is.EqualTo(pageEvents));

            Assert.That(pageRequests, Has.Count.EqualTo(1));
            Assert.That(windowRequests, Has.Count.EqualTo(1));
            Assert.That(browserRequests, Has.Count.EqualTo(1));
            Assert.That(pageResponses, Has.Count.EqualTo(1));
            Assert.That(windowResponses, Has.Count.EqualTo(1));
            Assert.That(browserResponses, Has.Count.EqualTo(1));

            Assert.That(pageRequests[0].IsNavigate, Is.True);
            Assert.That(pageRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(pageRequests[0].Request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(pageRequests[0].Request.Headers.GetValues("X-Dispatch").Single(), Is.EqualTo("enabled"));
            Assert.That(pageRequests[0].Frame, Is.SameAs(page.MainFrame));

            Assert.That(windowRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(windowRequests[0].Frame, Is.SameAs(page.MainFrame));
            Assert.That(browserRequests[0].Request.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(browserRequests[0].Frame, Is.SameAs(page.MainFrame));

            Assert.That(pageResponses[0].IsNavigate, Is.True);
            Assert.That(pageResponses[0].Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pageResponses[0].Response.RequestMessage, Is.Not.Null);
            Assert.That(pageResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(pageResponses[0].Frame, Is.SameAs(page.MainFrame));

            Assert.That(windowResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(windowResponses[0].Frame, Is.SameAs(page.MainFrame));
            Assert.That(browserResponses[0].Response.RequestMessage!.RequestUri, Is.EqualTo(targetUrl));
            Assert.That(browserResponses[0].Frame, Is.SameAs(page.MainFrame));
        });
    }

    [Test]
    public async Task NavigateAsyncUsesEffectiveInterceptionScopeAndPatternsForSyntheticNavigation()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        List<Uri?> pageRequests = [];
        List<Uri?> windowRequests = [];
        List<Uri?> browserRequests = [];
        List<Uri?> pageResponses = [];
        List<Uri?> windowResponses = [];
        List<Uri?> browserResponses = [];

        page.Request += (_, args) =>
        {
            pageRequests.Add(args.Request.RequestUri);
            return ValueTask.CompletedTask;
        };
        window.Request += (_, args) =>
        {
            windowRequests.Add(args.Request.RequestUri);
            return ValueTask.CompletedTask;
        };
        browser.Request += (_, args) =>
        {
            browserRequests.Add(args.Request.RequestUri);
            return ValueTask.CompletedTask;
        };
        page.Response += (_, args) =>
        {
            pageResponses.Add(args.Response.RequestMessage?.RequestUri);
            return ValueTask.CompletedTask;
        };
        window.Response += (_, args) =>
        {
            windowResponses.Add(args.Response.RequestMessage?.RequestUri);
            return ValueTask.CompletedTask;
        };
        browser.Response += (_, args) =>
        {
            browserResponses.Add(args.Response.RequestMessage?.RequestUri);
            return ValueTask.CompletedTask;
        };

        var disabledUrl = new Uri("https://127.0.0.1/interception/disabled");
        var browserUrl = new Uri("https://127.0.0.1/interception/browser-hit");
        var browserMissUrl = new Uri("https://127.0.0.1/interception/browser-miss");
        var windowUrl = new Uri("https://127.0.0.1/interception/window-hit");
        var windowMissUrl = new Uri("https://127.0.0.1/interception/window-miss");
        var pageUrl = new Uri("https://127.0.0.1/interception/page-hit");

        await NavigateAndAssertAsync(disabledUrl, ExpectedLifecycleRelay, expectedDispatchCount: 0).ConfigureAwait(false);

        await browser.SetRequestInterceptionAsync(true, ["*browser-hit*"]).ConfigureAwait(false);
        await NavigateAndAssertAsync(browserUrl, ExpectedInterceptedNavigationRelay, expectedDispatchCount: 1).ConfigureAwait(false);

        await window.SetRequestInterceptionAsync(true, ["*window-hit*"]).ConfigureAwait(false);
        await NavigateAndAssertAsync(browserMissUrl, ExpectedLifecycleRelay, expectedDispatchCount: 1).ConfigureAwait(false);
        await NavigateAndAssertAsync(windowUrl, ExpectedInterceptedNavigationRelay, expectedDispatchCount: 2).ConfigureAwait(false);

        await page.SetRequestInterceptionAsync(true, ["*page-hit*"]).ConfigureAwait(false);
        await NavigateAndAssertAsync(windowMissUrl, ExpectedLifecycleRelay, expectedDispatchCount: 2).ConfigureAwait(false);
        await NavigateAndAssertAsync(pageUrl, ExpectedInterceptedNavigationRelay, expectedDispatchCount: 3).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(pageRequests, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
            Assert.That(windowRequests, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
            Assert.That(browserRequests, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
            Assert.That(pageResponses, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
            Assert.That(windowResponses, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
            Assert.That(browserResponses, Is.EqualTo(new Uri?[] { browserUrl, windowUrl, pageUrl }));
        });

        async Task NavigateAndAssertAsync(Uri url, IReadOnlyList<BridgeEvent> expectedEvents, int expectedDispatchCount)
        {
            await page.NavigateAsync(url, new NavigationSettings
            {
                Html = $"<html><head><title>{url.AbsolutePath}</title></head><body>{url}</body></html>",
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(DrainEventKinds(page.TryDequeueBridgeEvent), Is.EqualTo(expectedEvents));
                Assert.That(DrainEventKinds(window.TryDequeueBridgeEvent), Is.EqualTo(expectedEvents));
                Assert.That(DrainEventKinds(browser.TryDequeueBridgeEvent), Is.EqualTo(expectedEvents));
                Assert.That(pageRequests, Has.Count.EqualTo(expectedDispatchCount));
                Assert.That(windowRequests, Has.Count.EqualTo(expectedDispatchCount));
                Assert.That(browserRequests, Has.Count.EqualTo(expectedDispatchCount));
                Assert.That(pageResponses, Has.Count.EqualTo(expectedDispatchCount));
                Assert.That(windowResponses, Has.Count.EqualTo(expectedDispatchCount));
                Assert.That(browserResponses, Has.Count.EqualTo(expectedDispatchCount));
            });
        }
    }

    [Test]
    public async Task ReceiveBridgeEventDispatchesConsoleAcrossScopes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        List<ConsoleMessageEventArgs> pageMessages = [];
        List<ConsoleMessageEventArgs> windowMessages = [];
        List<ConsoleMessageEventArgs> browserMessages = [];

        page.Console += (_, e) => pageMessages.Add(e);
        window.Console += (_, e) => windowMessages.Add(e);
        browser.Console += (_, e) => browserMessages.Add(e);

        using var payload = JsonDocument.Parse("""
            {
              "level": "warn",
              "args": ["alpha", 42, true],
              "message": "alpha 42",
              "ts": 1730000000123
            }
            """);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "console-1",
            Type = BridgeMessageType.Event,
            WindowId = window.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.ConsoleMessage,
            Payload = payload.RootElement.Clone(),
            Timestamp = 1730000000123,
        });

        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var windowEvents = DrainEventKinds(window.TryDequeueBridgeEvent);
        var browserEvents = DrainEventKinds(browser.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(pageEvents, Is.EqualTo(new[] { BridgeEvent.ConsoleMessage }));
            Assert.That(windowEvents, Is.EqualTo(pageEvents));
            Assert.That(browserEvents, Is.EqualTo(pageEvents));

            Assert.That(pageMessages, Has.Count.EqualTo(1));
            Assert.That(windowMessages, Has.Count.EqualTo(1));
            Assert.That(browserMessages, Has.Count.EqualTo(1));

            Assert.That(pageMessages[0].Level, Is.EqualTo(ConsoleMessageLevel.Warn));
            Assert.That(pageMessages[0].Message, Is.EqualTo("alpha 42"));
            Assert.That(pageMessages[0].Time, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1730000000123)));
            Assert.That(pageMessages[0].Frame, Is.SameAs(page.MainFrame));
            Assert.That(pageMessages[0].Args.Cast<object?>().ToArray(), Is.EqualTo(new object?[] { "alpha", 42L, true }));

            Assert.That(windowMessages[0].Level, Is.EqualTo(ConsoleMessageLevel.Warn));
            Assert.That(windowMessages[0].Frame, Is.SameAs(page.MainFrame));
            Assert.That(browserMessages[0].Level, Is.EqualTo(ConsoleMessageLevel.Warn));
            Assert.That(browserMessages[0].Frame, Is.SameAs(page.MainFrame));
        });
    }

    [Test]
    public async Task ReceiveBridgeEventLifecycleHrefPayloadUpdatesPageSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        using var payload = JsonDocument.Parse("""
            {
              "href": "https://example.com/live-lifecycle",
              "title": "Live Lifecycle"
            }
            """);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "lifecycle-href-1",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.NavigationCompleted,
            Payload = payload.RootElement.Clone(),
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(page.CurrentUrl, Is.EqualTo(new Uri("https://example.com/live-lifecycle")));
            Assert.That(page.CurrentTitle, Is.EqualTo("Live Lifecycle"));
        });
    }

    [Test]
    public async Task ReceiveBridgeEventDispatchesCallbackOnlyForSubscribedPath()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, e) => finalized.Add(e);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        using var callbackPayload = JsonDocument.Parse("""
            {
              "name": "app.ready",
              "args": ["alpha", 7],
              "code": "return window.app.ready()"
            }
            """);
        using var finalizedPayload = JsonDocument.Parse("""
            {
              "name": "app.ready"
            }
            """);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "callback-1",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.Callback,
            Payload = callbackPayload.RootElement.Clone(),
        }).ConfigureAwait(false);
        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "callback-finalized-1",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.CallbackFinalized,
            Payload = finalizedPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await page.UnSubscribeAsync("app.ready").ConfigureAwait(false);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "callback-2",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.Callback,
            Payload = callbackPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L }));
            Assert.That(callbacks[0].Code, Is.EqualTo("return window.app.ready()"));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [Test]
    public async Task DispatchSyntheticCallbackDefaultsToContinueWhenSubscriberDoesNotControlInvocation()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        List<CallbackEventArgs> callbacks = [];

        page.Callback += (_, args) =>
        {
            callbacks.Add(args);
            return ValueTask.CompletedTask;
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        var callbackArgs = new CallbackEventArgs
        {
            Name = "app.ready",
            Args = ["alpha", 7L, true],
            Code = "return app.ready('alpha', 7, true)",
        };

        var decision = await page.DispatchSyntheticCallbackAsync(callbackArgs, CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(callbacks[0], Is.SameAs(callbackArgs));
            Assert.That(decision.Action, Is.EqualTo(CallbackControlAction.Continue));
            Assert.That(decision.Args, Is.Null);
            Assert.That(decision.Code, Is.Null);
        });
    }

    [Test]
    public async Task DispatchSyntheticCallbackAllowsSubscriberToAbortInvocation()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        page.Callback += async (_, args) =>
        {
            await args.AbortAsync().ConfigureAwait(false);
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        var callbackArgs = new CallbackEventArgs
        {
            Name = "app.ready",
            Args = ["alpha"],
            Code = "return app.ready('alpha')",
        };

        var decision = await page.DispatchSyntheticCallbackAsync(callbackArgs, CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbackArgs.IsCancelled, Is.True);
            Assert.That(decision.Action, Is.EqualTo(CallbackControlAction.Abort));
            Assert.That(decision.Args, Is.Null);
            Assert.That(decision.Code, Is.Null);
        });
    }

    [Test]
    public async Task DispatchSyntheticCallbackAllowsSubscriberToContinueWithReplacementArguments()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        page.Callback += async (_, args) =>
        {
            await args.ContinueAsync(["beta", 9L, false]).ConfigureAwait(false);
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        var callbackArgs = new CallbackEventArgs
        {
            Name = "app.ready",
            Args = ["alpha", 7L, true],
            Code = "return app.ready('alpha', 7, true)",
        };

        var decision = await page.DispatchSyntheticCallbackAsync(callbackArgs, CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(CallbackControlAction.Continue));
            Assert.That(decision.Args, Is.EqualTo(new object?[] { "beta", 9L, false }));
            Assert.That(decision.Code, Is.Null);
        });
    }

    [Test]
    public async Task DispatchSyntheticCallbackAllowsSubscriberToReplaceInvocationCode()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        const string replacementCode = "return 'patched:' + args.join('|');";

        page.Callback += async (_, args) =>
        {
            await args.ReplaceAsync(replacementCode).ConfigureAwait(false);
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        var callbackArgs = new CallbackEventArgs
        {
            Name = "app.ready",
            Args = ["alpha", 7L, true],
            Code = "return app.ready('alpha', 7, true)",
        };

        var decision = await page.DispatchSyntheticCallbackAsync(callbackArgs, CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(CallbackControlAction.Replace));
            Assert.That(decision.Args, Is.Null);
            Assert.That(decision.Code, Is.EqualTo(replacementCode));
        });
    }

    [Test]
    public async Task RequestDispatchInvokesAllAsyncSubscribers()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var calls = new List<string>();

        page.Request += async (_, _) =>
        {
            await Task.Delay(5).ConfigureAwait(false);
            calls.Add("first");
        };
        page.Request += async (_, _) =>
        {
            await Task.Yield();
            calls.Add("second");
        };
        page.Request += (_, _) =>
        {
            calls.Add("third");
            return ValueTask.CompletedTask;
        };

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        await page.NavigateAsync(new Uri("https://127.0.0.1/multi-subscriber"), new NavigationSettings
        {
            Html = "<html><head><title>Multi</title></head><body>ok</body></html>",
        }).ConfigureAwait(false);

        Assert.That(calls, Is.EqualTo(ExpectedSubscriberOrder));
    }

    [Test]
    public async Task EvaluateAsyncProducesSubscribedCallbackEnvelope()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, e) => finalized.Add(e);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        var result = await page.EvaluateAsync<string>("return app.ready('alpha', 7, true)").ConfigureAwait(false);
        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var windowEvents = DrainEventKinds(window.TryDequeueBridgeEvent);
        var browserEvents = DrainEventKinds(browser.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(pageEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(windowEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(browserEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(callbacks[0].Code, Is.EqualTo("return app.ready('alpha', 7, true)"));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [TestCase(false, TestName = "InjectScriptAsync produces subscribed callback envelope for body injection")]
    [TestCase(true, TestName = "InjectScriptAsync produces subscribed callback envelope for head injection")]
    public async Task InjectScriptAsyncProducesSubscribedCallbackEnvelope(bool injectToHead)
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];
        const string script = "return app.ready('alpha', 7, true)";

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, e) => finalized.Add(e);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        await page.InjectScriptAsync(script, injectToHead).ConfigureAwait(false);

        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var windowEvents = DrainEventKinds(window.TryDequeueBridgeEvent);
        var browserEvents = DrainEventKinds(browser.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(pageEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(windowEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(browserEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(callbacks[0].Code, Is.EqualTo(script));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [Test]
    public async Task InjectScriptLinkAsyncProducesSubscribedCallbackEnvelopeForInlineDataUri()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];
        const string inlineScript = "app.ready('alpha', 7, true)";
        var inlineScriptUrl = new Uri($"data:text/javascript;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(inlineScript))}");

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, e) => finalized.Add(e);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        await page.InjectScriptLinkAsync(inlineScriptUrl).ConfigureAwait(false);

        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var windowEvents = DrainEventKinds(window.TryDequeueBridgeEvent);
        var browserEvents = DrainEventKinds(browser.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(pageEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(windowEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(browserEvents, Is.EqualTo(ExpectedCallbackRelay));
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(callbacks[0].Code, Is.EqualTo(inlineScript));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [Test]
    public async Task EvaluateAsyncStopsDispatchingCallbackAfterUnsubscribe()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, e) => finalized.Add(e);

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        _ = await page.EvaluateAsync<string>("return app.ready('alpha')").ConfigureAwait(false);

        await page.UnSubscribeAsync("app.ready").ConfigureAwait(false);
        _ = await page.EvaluateAsync<string>("return app.ready('beta')").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha" }));
            Assert.That(finalized, Has.Count.EqualTo(1));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [TestCase("return window.app.ready('alpha', 7)", new object?[] { "alpha", 7L }, TestName = "EvaluateAsync produces callback envelope for window-rooted invocation")]
    [TestCase("return await globalThis.app.ready('alpha', 7, true)", new object?[] { "alpha", 7L, true }, TestName = "EvaluateAsync produces callback envelope for awaited globalThis invocation")]
    [TestCase("(self.app.ready('alpha'))", new object?[] { "alpha" }, TestName = "EvaluateAsync produces callback envelope for wrapped self-rooted invocation")]
    public async Task EvaluateAsyncProducesCallbackEnvelopeForRootedInvocationForms(string script, object?[] expectedArgs)
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        List<CallbackEventArgs> callbacks = [];

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        _ = await page.EvaluateAsync<string>(script).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(expectedArgs));
            Assert.That(callbacks[0].Code, Is.EqualTo(script));
        });
    }

    [Test]
    public async Task EvaluateAsyncKeepsNestedArgumentsAsSingleValues()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        List<CallbackEventArgs> callbacks = [];

        page.Callback += (_, e) =>
        {
            callbacks.Add(e);
            return ValueTask.CompletedTask;
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        _ = await page.EvaluateAsync<string>("return app.ready({ value: 7, inner: [1, 2] }, [1, 2, 3], nested.call('x', 2))").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Has.Count.EqualTo(1));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[]
            {
                "{ value: 7, inner: [1, 2] }",
                "[1, 2, 3]",
                "nested.call('x', 2)",
            }));
        });
    }

    [Test]
    public async Task ReceiveBridgeEventWithMalformedCallbackPayloadSkipsDispatchButKeepsEnvelope()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var callbackCount = 0;
        var finalizedCount = 0;

        page.Callback += (_, _) =>
        {
            callbackCount++;
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, _) => finalizedCount++;
        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        using var malformedPayload = JsonDocument.Parse("""
            {
              "args": ["alpha"]
            }
            """);

        await page.ReceiveBridgeEventAsync(new BridgeMessage
        {
            Id = "callback-malformed",
            Type = BridgeMessageType.Event,
            WindowId = page.WindowId,
            TabId = page.TabId,
            Event = BridgeEvent.Callback,
            Payload = malformedPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbackCount, Is.Zero);
            Assert.That(finalizedCount, Is.Zero);
            Assert.That(DrainEventKinds(page.TryDequeueBridgeEvent), Is.EqualTo(new[] { BridgeEvent.Callback }));
        });
    }

    private static List<BridgeEvent> DrainEventKinds(TryDequeueBridgeMessage tryDequeue)
    {
        List<BridgeEvent> events = [];

        while (tryDequeue(out var message))
        {
            if (message?.Event is BridgeEvent @event)
            {
                events.Add(@event);
            }
        }

        return events;
    }
}