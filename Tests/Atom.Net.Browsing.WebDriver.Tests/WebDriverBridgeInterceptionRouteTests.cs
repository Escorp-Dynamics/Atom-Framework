using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverBridgeInterceptionRouteTests
{
    [Test]
    public async Task BridgeServerCallbackRouteInvokesHandlerAndReturnsContinueDecisionWithArgs()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        BridgeCallbackRequestPayload? captured = null;
        server.CallbackRequested += (request, _) =>
        {
            captured = request;
            return ValueTask.FromResult(BridgeCallbackHttpResponse.Continue(["beta", 9, false]));
        };

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/callback?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "callback-1",
                ["tabId"] = "tab-1",
                ["name"] = "app.ready",
                ["args"] = new JsonArray("alpha", 7, true),
                ["code"] = "app.ready('alpha', 7, true)",
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var args = payload["args"]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(args.Count, Is.EqualTo(3));
            Assert.That(args[0]?.GetValue<string>(), Is.EqualTo("beta"));
            Assert.That(args[1]?.GetValue<int>(), Is.EqualTo(9));
            Assert.That(args[2]?.GetValue<bool>(), Is.False);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.RequestId, Is.EqualTo("callback-1"));
            Assert.That(captured.TabId, Is.EqualTo("tab-1"));
            Assert.That(captured.Name, Is.EqualTo("app.ready"));
            Assert.That(captured.Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(captured.Code, Is.EqualTo("app.ready('alpha', 7, true)"));
        });
    }

    [Test]
    public async Task BridgeServerCallbackRouteDispatchesIntoWebBrowserHandlersAndReturnsContinueDecisionWithArgs()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        CallbackEventArgs? captured = null;

        page.Callback += async (_, args) =>
        {
            captured = args;
            await args.ContinueAsync(["beta", 9L, false]).ConfigureAwait(false);
        };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/callback?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "callback-browser-1",
                ["tabId"] = page.TabId,
                ["name"] = "app.ready",
                ["args"] = new JsonArray("alpha", 7, true),
                ["code"] = "app.ready('alpha', 7, true)",
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var args = payload["args"]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(args.Count, Is.EqualTo(3));
            Assert.That(args[0]?.GetValue<string>(), Is.EqualTo("beta"));
            Assert.That(args[1]?.GetValue<int>(), Is.EqualTo(9));
            Assert.That(args[2]?.GetValue<bool>(), Is.False);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Name, Is.EqualTo("app.ready"));
            Assert.That(captured.Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(captured.Code, Is.EqualTo("app.ready('alpha', 7, true)"));
        });
    }

    [Test]
    public async Task BridgeServerInterceptRouteReturnsContinueByDefault()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "req-1",
                ["tabId"] = "tab-1",
                ["url"] = "https://example.test/request",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(payload["statusCode"], Is.Null);
        });
    }

    [Test]
    public async Task BridgeServerInterceptRouteInvokesRequestHandlerAndReturnsAbortDecision()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        BridgeInterceptedRequestPayload? captured = null;
        server.RequestInterceptionRequested += (request, _) =>
        {
            captured = request;
            return ValueTask.FromResult(BridgeInterceptHttpResponse.Abort((int)HttpStatusCode.Gone, "blocked"));
        };

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "req-2",
                ["tabId"] = "tab-2",
                ["url"] = "https://example.test/blocked",
                ["method"] = "POST",
                ["type"] = "xmlhttprequest",
                ["headers"] = new JsonObject
                {
                    ["Test"] = "alpha",
                },
                ["requestBodyBase64"] = Convert.ToBase64String("payload"u8.ToArray()),
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("abort"));
            Assert.That(payload["statusCode"]?.GetValue<int>(), Is.EqualTo((int)HttpStatusCode.Gone));
            Assert.That(payload["reasonPhrase"]?.GetValue<string>(), Is.EqualTo("blocked"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.TabId, Is.EqualTo("tab-2"));
            Assert.That(captured.Method, Is.EqualTo("POST"));
            Assert.That(captured.RequestBodyBase64, Is.Not.Null);
            Assert.That(captured.Headers!["Test"], Is.EqualTo("alpha"));
        });
    }

    [Test]
    public async Task BridgeServerInterceptResponseRouteInvokesHandlerAndReturnsFulfillPayload()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        BridgeInterceptedResponsePayload? captured = null;
        server.ResponseInterceptionRequested += (responsePayload, _) =>
        {
            captured = responsePayload;
            return ValueTask.FromResult(BridgeInterceptHttpResponse.Fulfill(
                bodyBase64: Convert.ToBase64String("fulfilled"u8.ToArray()),
                responseHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = "text/plain",
                    ["X-Intercepted"] = "true",
                },
                statusCode: (int)HttpStatusCode.Accepted,
                reasonPhrase: "fulfilled"));
        };

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept-response?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "resp-1",
                ["tabId"] = "tab-9",
                ["url"] = "https://example.test/response",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["statusCode"] = 200,
                ["reasonPhrase"] = "OK",
                ["headers"] = new JsonObject
                {
                    ["Content-Type"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var responseHeaders = payload["responseHeaders"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("fulfill"));
            Assert.That(payload["statusCode"]?.GetValue<int>(), Is.EqualTo((int)HttpStatusCode.Accepted));
            Assert.That(payload["reasonPhrase"]?.GetValue<string>(), Is.EqualTo("fulfilled"));
            Assert.That(payload["bodyBase64"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
            Assert.That(responseHeaders["X-Intercepted"]?.GetValue<string>(), Is.EqualTo("true"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.StatusCode, Is.EqualTo(200));
            Assert.That(captured.ReasonPhrase, Is.EqualTo("OK"));
        });
    }

    [Test]
    public async Task BridgeServerObservedRequestHeadersRouteInvokesHandlerAndReturnsNoContent()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        ObservedRequestHeadersEventArgs? captured = null;
        server.RequestHeadersObserved += (request, _) =>
        {
            captured = request;
            return ValueTask.CompletedTask;
        };

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/observed-request-headers?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "obs-1",
                ["tabId"] = "tab-7",
                ["url"] = "https://example.test/headers",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["headers"] = new JsonObject
                {
                    ["Accept"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.TabId, Is.EqualTo("tab-7"));
            Assert.That(captured.Headers["Accept"], Is.EqualTo("text/html"));
            Assert.That(captured.Url, Is.EqualTo(new Uri("https://example.test/headers")));
        });
    }

    [Test]
    public async Task BridgeServerInterceptionRoutesRejectInvalidSecret()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=wrong",
            new JsonObject
            {
                ["requestId"] = "req-denied",
                ["tabId"] = "tab-denied",
                ["url"] = "https://example.test/denied",
            }).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task BridgeServerInterceptRouteDispatchesIntoWebBrowserRequestHandlers()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        InterceptedRequestEventArgs? captured = null;

        page.Request += (_, args) =>
        {
            captured = args;
            args.Request.Headers.TryAddWithoutValidation("X-Bridge-Request", "true");
            return args.ContinueAsync();
        };

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "browser-req-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/live-request",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["headers"] = new JsonObject
                {
                    ["Accept"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var headers = payload["headers"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(headers["X-Bridge-Request"]?.GetValue<string>(), Is.EqualTo("true"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Request.RequestUri, Is.EqualTo(new Uri("https://example.test/live-request")));
            Assert.That(captured.Request.Headers.Accept.ToString(), Does.Contain("text/html"));
        });
    }

    [Test]
    public async Task BridgeServerInterceptRouteFailsClosedWhenWebBrowserRequestHandlerCallsFulfillOnMainFrame()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        InterceptedRequestEventArgs? captured = null;

        page.Request += async (_, args) =>
        {
            captured = args;
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "browser-fulfilled",
                Content = new StringContent("patched", Encoding.UTF8, "text/plain"),
            };
            fulfilled.Headers.TryAddWithoutValidation("X-Bridge-Request-Fulfill", "true");

            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "browser-req-fulfill-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/live-request-fulfill",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["headers"] = new JsonObject
                {
                    ["Accept"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("abort"));
            Assert.That(payload["statusCode"]?.GetValue<int>(), Is.EqualTo((int)HttpStatusCode.NotImplemented));
            Assert.That(payload["reasonPhrase"]?.GetValue<string>(), Does.Contain("main_frame fulfill"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.IsNavigate, Is.True);
            Assert.That(captured.Request.RequestUri, Is.EqualTo(new Uri("https://example.test/live-request-fulfill")));
        });
    }

    [Test]
    public async Task BridgeServerInterceptRouteEnqueuesProxyCapableMainFrameFulfillmentAndReturnsContinue()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        var routeToken = "proxy-token-1";

        browser.ProxyNavigationDecisions.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = "session-1",
            TabId = page.TabId,
            ContextId = page.GetOrCreateBridgeContextId(),
            RouteToken = routeToken,
            Revision = 1,
        });

        page.Request += async (_, args) =>
        {
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "browser-fulfilled-proxy",
                Content = new StringContent("patched-proxy", Encoding.UTF8, "text/plain"),
            };
            fulfilled.Headers.TryAddWithoutValidation("X-Bridge-Proxy-Fulfill", "true");

            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "browser-req-proxy-fulfill-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/live-request-proxy-fulfill",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["supportsNavigationFulfillment"] = true,
                ["headers"] = new JsonObject
                {
                    ["Accept"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var consumed = browser.ProxyNavigationDecisions.TryConsumeDecision(routeToken, "GET", "https://example.test/live-request-proxy-fulfill", DateTimeOffset.UtcNow, out var pendingDecision);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(consumed, Is.True);
            Assert.That(pendingDecision, Is.Not.Null);
            Assert.That(pendingDecision!.Action, Is.EqualTo(ProxyNavigationDecisionAction.Fulfill));
            Assert.That(pendingDecision.StatusCode, Is.EqualTo((int)HttpStatusCode.Accepted));
            Assert.That(pendingDecision.ReasonPhrase, Is.EqualTo("browser-fulfilled-proxy"));
            Assert.That(pendingDecision.ResponseHeaders, Is.Not.Null);
            Assert.That(pendingDecision.ResponseHeaders!["X-Bridge-Proxy-Fulfill"], Is.EqualTo("true"));
        });
    }

    [Test]
    public async Task BridgeServerInterceptResponseRouteDispatchesIntoWebBrowserResponseHandlers()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        InterceptedResponseEventArgs? captured = null;

        page.Response += async (_, args) =>
        {
            captured = args;
            var fulfilled = new HttpsResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "browser-fulfilled",
                Content = new StringContent("patched", Encoding.UTF8, "text/plain"),
            };
            fulfilled.Headers.TryAddWithoutValidation("X-Bridge-Response", "true");

            await args.FulfillAsync(fulfilled).ConfigureAwait(false);
        };

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept-response?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "browser-resp-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/live-response",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["statusCode"] = 200,
                ["reasonPhrase"] = "OK",
                ["headers"] = new JsonObject
                {
                    ["Content-Type"] = "text/html",
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var responseHeaders = payload["responseHeaders"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("fulfill"));
            Assert.That(payload["statusCode"]?.GetValue<int>(), Is.EqualTo((int)HttpStatusCode.Accepted));
            Assert.That(payload["reasonPhrase"]?.GetValue<string>(), Is.EqualTo("browser-fulfilled"));
            Assert.That(payload["bodyBase64"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
            Assert.That(responseHeaders["X-Bridge-Response"]?.GetValue<string>(), Is.EqualTo("true"));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(captured.Response.Content?.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        });
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, JsonObject payload)
        => client.PostAsync(url, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
}