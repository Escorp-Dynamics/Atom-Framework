using System.Net;
using System.Text;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverInterceptionBehaviorTests
{
    private delegate bool TryDequeueBridgeMessage(out BridgeMessage? message);

    [Test]
    public async Task RequestInterceptionDefaultsToContinueAndKeepsFirstDecision()
    {
        var args = CreateRequestArgs();

        args.SetDefaultIfPending();
        await args.AbortAsync(HttpStatusCode.Gone, "too-late").ConfigureAwait(false);

        var decision = await args.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(InterceptAction.Continue));
            Assert.That(decision.Continuation, Is.Null);
            Assert.That(decision.StatusCode, Is.Null);
            Assert.That(decision.ReasonPhrase, Is.Null);
        });
    }

    [Test]
    public async Task RequestInterceptionCapturesRedirectAbortAndFulfillSemantics()
    {
        var redirectedUrl = new Uri("https://redirected.test/next");
        var redirectArgs = CreateRequestArgs();

        await redirectArgs.RedirectAsync(redirectedUrl).ConfigureAwait(false);

        var redirectDecision = await redirectArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(redirectDecision.Action, Is.EqualTo(InterceptAction.Continue));
            Assert.That(redirectDecision.Continuation, Is.Not.Null);
            Assert.That(redirectDecision.Continuation!.RedirectUrl, Is.EqualTo(redirectedUrl));
            Assert.That(redirectDecision.Continuation.Request, Is.Null);
        });

        var abortArgs = CreateRequestArgs();
        await abortArgs.AbortAsync(HttpStatusCode.BadGateway, "upstream").ConfigureAwait(false);
        var abortDecision = await abortArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(abortDecision.Action, Is.EqualTo(InterceptAction.Abort));
            Assert.That(abortDecision.StatusCode, Is.EqualTo((int)HttpStatusCode.BadGateway));
            Assert.That(abortDecision.ReasonPhrase, Is.EqualTo("upstream"));
        });

        var fulfillArgs = CreateRequestArgs();
        var response = new HttpsResponseMessage(HttpStatusCode.Accepted)
        {
            ReasonPhrase = "fulfilled",
            Content = new StringContent("intercepted-body", Encoding.UTF8, "text/plain"),
        };
        response.Headers.Add("X-Intercepted", "true");

        await fulfillArgs.FulfillAsync(response).ConfigureAwait(false);
        var fulfillDecision = await fulfillArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(fulfillDecision.Action, Is.EqualTo(InterceptAction.Fulfill));
            Assert.That(fulfillDecision.Fulfillment, Is.Not.Null);
            Assert.That(fulfillDecision.Fulfillment!.Response, Is.SameAs(response));
            Assert.That(Encoding.UTF8.GetString(fulfillDecision.Fulfillment.Body!), Is.EqualTo("intercepted-body"));
        });
    }

    [Test]
    public async Task RequestInterceptionContinueWithReplacementRequestCapturesRequestInstance()
    {
        var args = CreateRequestArgs();
        var replacement = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://example.test/replaced"));
        replacement.Headers.Add("X-Test", "alpha");
        replacement.Content = new StringContent("payload", Encoding.UTF8, "text/plain");

        await args.ContinueAsync(replacement).ConfigureAwait(false);

        var decision = await args.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(InterceptAction.Continue));
            Assert.That(decision.Continuation, Is.Not.Null);
            Assert.That(decision.Continuation!.Request, Is.SameAs(replacement));
            Assert.That(decision.Continuation.RedirectUrl, Is.Null);
        });
    }

    [Test]
    public void BridgeBackedNavigationRequestFulfillThrowsNotSupportedException()
    {
        var args = CreateRequestArgs(isNavigate: true, supportsNavigationFulfillment: false);
        var response = new HttpsResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("bridge-navigation", Encoding.UTF8, "text/plain"),
        };

        var exception = Assert.ThrowsAsync(Is.InstanceOf<NotSupportedException>(), async () => await args.FulfillAsync(response).ConfigureAwait(false)) as Exception;

        Assert.That(exception?.Message, Does.Contain("main_frame fulfill"));
    }

    [Test]
    public async Task BridgeBackedSubresourceRequestFulfillStillCapturesFulfillment()
    {
        var args = CreateRequestArgs(isNavigate: false, supportsNavigationFulfillment: false);
        var response = new HttpsResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("bridge-subresource", Encoding.UTF8, "text/plain"),
        };

        await args.FulfillAsync(response).ConfigureAwait(false);

        var decision = await args.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(InterceptAction.Fulfill));
            Assert.That(decision.Fulfillment, Is.Not.Null);
            Assert.That(Encoding.UTF8.GetString(decision.Fulfillment!.Body!), Is.EqualTo("bridge-subresource"));
        });
    }

    [Test]
    public async Task ResponseInterceptionCapturesContinueAbortAndFulfillDecisions()
    {
        var continueResponse = CreateResponse(statusCode: HttpStatusCode.Created, body: "created", contentType: "text/plain");
        continueResponse.Headers.Add("X-Flow", "continue");
        var continueArgs = CreateResponseArgs(continueResponse);

        await continueArgs.ContinueAsync().ConfigureAwait(false);

        var continueDecision = await continueArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(continueDecision.Action, Is.EqualTo(InterceptAction.Continue));
            Assert.That(continueDecision.Response, Is.SameAs(continueResponse));
            Assert.That(continueDecision.ResponseHeaders, Is.Not.Null);
            Assert.That(continueDecision.ResponseHeaders!["X-Flow"], Is.EqualTo("continue"));
            Assert.That(continueDecision.ResponseHeaders!["Content-Type"], Does.Contain("text/plain"));
        });

        var abortArgs = CreateResponseArgs(CreateResponse(HttpStatusCode.OK, "ignored", "text/plain"));
        await abortArgs.AbortAsync(HttpStatusCode.GatewayTimeout, "timeout").ConfigureAwait(false);
        var abortDecision = await abortArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(abortDecision.Action, Is.EqualTo(InterceptAction.Abort));
            Assert.That(abortDecision.StatusCode, Is.EqualTo((int)HttpStatusCode.GatewayTimeout));
            Assert.That(abortDecision.ReasonPhrase, Is.EqualTo("timeout"));
            Assert.That(abortArgs.IsCancelled, Is.True);
        });

        var fulfillResponse = CreateResponse(HttpStatusCode.Accepted, "fulfilled", "application/json");
        fulfillResponse.Headers.Add("X-Mode", "fulfill");
        var fulfillArgs = CreateResponseArgs(fulfillResponse);

        await fulfillArgs.FulfillAsync(fulfillResponse).ConfigureAwait(false);
        var fulfillDecision = await fulfillArgs.WaitForDecisionAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(fulfillDecision.Action, Is.EqualTo(InterceptAction.Fulfill));
            Assert.That(fulfillDecision.Fulfillment, Is.Not.Null);
            Assert.That(fulfillDecision.Fulfillment!.Response, Is.SameAs(fulfillResponse));
            Assert.That(Encoding.UTF8.GetString(fulfillDecision.Fulfillment.Body!), Is.EqualTo("fulfilled"));
            Assert.That(fulfillDecision.StatusCode, Is.EqualTo((int)HttpStatusCode.Accepted));
            Assert.That(fulfillDecision.ResponseHeaders!["X-Mode"], Is.EqualTo("fulfill"));
        });
    }

    [Test]
    public async Task SyntheticTransportRequestAbortReturnsAbortResponseWithoutNavigating()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/original"), "<html><head><title>Original</title></head><body>stable</body></html>").ConfigureAwait(false);
        _ = DrainEventKinds(page.TryDequeueBridgeEvent);

        page.Request += (_, args) => args.AbortAsync(HttpStatusCode.Gone, "blocked");
        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/blocked"), "<html><head><title>Blocked</title></head><body>blocked</body></html>").ConfigureAwait(false);
        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);
        var currentUrl = await page.GetUrlAsync().ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
            Assert.That(response.ReasonPhrase, Is.EqualTo("blocked"));
            Assert.That(currentUrl, Is.EqualTo(new Uri("https://127.0.0.1/intercept/original")));
            Assert.That(title, Is.EqualTo("Original"));
            Assert.That(pageEvents, Is.EqualTo(new[] { BridgeEvent.RequestIntercepted }));
        });
    }

    [Test]
    public async Task SyntheticTransportRequestFulfillOverridesNavigationContent()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        page.Request += async (_, args) =>
        {
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "fulfilled",
                Content = new StringContent("<html><head><title>Fulfilled</title></head><body><h1 id='marker'>ok</h1></body></html>", Encoding.UTF8, "text/html"),
            };
            fulfilled.Headers.Add("X-Fulfilled", "true");
            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/fulfilled"), new NavigationSettings()).ConfigureAwait(false);
        var content = await page.GetContentAsync().ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);
        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(response.ReasonPhrase, Is.EqualTo("fulfilled"));
            Assert.That(content, Does.Contain("marker"));
            Assert.That(title, Is.EqualTo("Fulfilled"));
            Assert.That(pageEvents, Is.EqualTo(new[]
            {
                BridgeEvent.RequestIntercepted,
                BridgeEvent.ResponseReceived,
                BridgeEvent.DomContentLoaded,
                BridgeEvent.NavigationCompleted,
                BridgeEvent.PageLoaded,
            }));
        });
    }

    [Test]
    public async Task SyntheticTransportUsesFirstRequestDecisionAcrossScopes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var replacementUrl = new Uri("https://127.0.0.1/intercept/replaced");

        window.Request += async (_, args) =>
        {
            var replacement = new HttpsRequestMessage(HttpMethod.Post, replacementUrl)
            {
                Content = new StringContent("window-body", Encoding.UTF8, "text/plain"),
            };
            replacement.Headers.Add("X-Window", "chosen");
            await args.ContinueAsync(replacement).ConfigureAwait(false);
        };
        browser.Request += (_, args) => args.AbortAsync(HttpStatusCode.BadGateway, "ignored");

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/original"), new NavigationSettings()).ConfigureAwait(false);
        var currentUrl = await page.GetUrlAsync().ConfigureAwait(false);
        var requestUri = response.RequestMessage?.RequestUri;
        var requestMethod = response.RequestMessage?.Method;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(currentUrl, Is.EqualTo(replacementUrl));
            Assert.That(requestUri, Is.EqualTo(replacementUrl));
            Assert.That(requestMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(response.RequestMessage!.Headers.GetValues("X-Window").Single(), Is.EqualTo("chosen"));
        });
    }

    [Test]
    public async Task SyntheticTransportResponseFulfillOverridesCommittedContent()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;

        page.Response += async (_, args) =>
        {
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Created)
            {
                ReasonPhrase = "response-fulfilled",
                Content = new StringContent("<html><head><title>Response Fulfilled</title></head><body><main id='response-marker'>ok</main></body></html>", Encoding.UTF8, "text/html"),
            };
            fulfilled.Headers.Add("X-Response", "fulfilled");
            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/response-fulfilled"), "<html><head><title>Original</title></head><body>original</body></html>").ConfigureAwait(false);
        var content = await page.GetContentAsync().ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);
        var pageEvents = DrainEventKinds(page.TryDequeueBridgeEvent);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.ReasonPhrase, Is.EqualTo("response-fulfilled"));
            Assert.That(content, Does.Contain("response-marker"));
            Assert.That(title, Is.EqualTo("Response Fulfilled"));
            Assert.That(response.Headers.GetValues("X-Response").Single(), Is.EqualTo("fulfilled"));
            Assert.That(pageEvents, Is.EqualTo(new[]
            {
                BridgeEvent.RequestIntercepted,
                BridgeEvent.ResponseReceived,
                BridgeEvent.DomContentLoaded,
                BridgeEvent.NavigationCompleted,
                BridgeEvent.PageLoaded,
            }));
        });
    }

    [Test]
    public async Task SyntheticTransportUsesFirstResponseDecisionAcrossScopes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;

        window.Response += async (_, args) =>
        {
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "window-response",
                Content = new StringContent("<html><head><title>Window Response</title></head><body>window</body></html>", Encoding.UTF8, "text/html"),
            };
            fulfilled.Headers.Add("X-Window-Response", "chosen");
            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };
        browser.Response += (_, args) => args.AbortAsync(HttpStatusCode.BadGateway, "ignored");

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        var response = await page.NavigateAsync(new Uri("https://127.0.0.1/intercept/response-window"), "<html><head><title>Original</title></head><body>original</body></html>").ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(response.ReasonPhrase, Is.EqualTo("window-response"));
            Assert.That(title, Is.EqualTo("Window Response"));
            Assert.That(response.Headers.GetValues("X-Window-Response").Single(), Is.EqualTo("chosen"));
        });
    }

    private static InterceptedRequestEventArgs CreateRequestArgs(bool isNavigate = true, bool supportsNavigationFulfillment = true)
        => new()
        {
            IsNavigate = isNavigate,
            SupportsNavigationFulfillment = supportsNavigationFulfillment,
            Request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://example.test/original")),
            Frame = null!,
        };

    private static InterceptedResponseEventArgs CreateResponseArgs(HttpsResponseMessage response)
        => new()
        {
            IsNavigate = true,
            Response = response,
            Frame = null!,
        };

    private static HttpsResponseMessage CreateResponse(HttpStatusCode statusCode, string body, string contentType)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };

    private static BridgeEvent[] DrainEventKinds(TryDequeueBridgeMessage tryDequeue)
    {
        List<BridgeEvent> events = [];
        while (tryDequeue(out var message))
        {
            if (message?.Event is { } bridgeEvent)
            {
                events.Add(bridgeEvent);
            }
        }

        return events.ToArray();
    }
}