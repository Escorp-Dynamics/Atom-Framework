namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverProxyNavigationDecisionRegistryTests
{
    [Test]
    public void UpsertRouteReplacesTokenForSameContextAndClearsOldPendingDecisions()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.UpsertRoute(CreateRoute(contextId: "context-1", routeToken: "token-1", revision: 1));
        Assert.That(registry.EnqueueDecision("context-1", CreateDecision("https://example.test/first", now), now), Is.True);

        registry.UpsertRoute(CreateRoute(contextId: "context-1", routeToken: "token-2", revision: 2));

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryResolveToken("context-1", out var routeToken), Is.True);
            Assert.That(routeToken, Is.EqualTo("token-2"));
            Assert.That(registry.TryResolveRoute("token-1", out _), Is.False);
            Assert.That(registry.TryConsumeDecision("token-2", "GET", "https://example.test/first", now, out _), Is.False);
        });
    }

    [Test]
    public void TryConsumeDecisionRequiresExactMethodAndAbsoluteUrlMatch()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.UpsertRoute(CreateRoute(contextId: "context-1", routeToken: "token-1", revision: 1));
        registry.EnqueueDecision("context-1", CreateDecision("https://example.test/expected", now, method: "POST"), now);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryConsumeDecision("token-1", "GET", "https://example.test/expected", now, out _), Is.False);
            Assert.That(registry.TryConsumeDecision("token-1", "POST", "https://example.test/other", now, out _), Is.False);
            Assert.That(registry.TryConsumeDecision("token-1", "POST", "https://example.test/expected", now, out var consumed), Is.True);
            Assert.That(consumed, Is.Not.Null);
            Assert.That(consumed!.RequestId, Is.EqualTo("request-1"));
        });
    }

    [Test]
    public void TryConsumeDecisionPurgesExpiredEntriesBeforeMatching()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.UpsertRoute(CreateRoute(contextId: "context-1", routeToken: "token-1", revision: 1));
        registry.EnqueueDecision("context-1", CreateDecision("https://example.test/expired", now.AddSeconds(-10), expiresAtUtc: now.AddSeconds(-1), requestId: "expired"), now);
        registry.EnqueueDecision("context-1", CreateDecision("https://example.test/live", now, requestId: "live"), now);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryConsumeDecision("token-1", "GET", "https://example.test/expired", now, out _), Is.False);
            Assert.That(registry.TryConsumeDecision("token-1", "GET", "https://example.test/live", now, out var consumed), Is.True);
            Assert.That(consumed, Is.Not.Null);
            Assert.That(consumed!.RequestId, Is.EqualTo("live"));
        });
    }

    [Test]
    public void RemoveRouteByContextIdRemovesRouteAndPendingDecisions()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.UpsertRoute(CreateRoute(contextId: "context-1", routeToken: "token-1", revision: 1));
        registry.EnqueueDecision("context-1", CreateDecision("https://example.test/live", now), now);

        Assert.Multiple(() =>
        {
            Assert.That(registry.RemoveRouteByContextId("context-1"), Is.True);
            Assert.That(registry.TryResolveToken("context-1", out _), Is.False);
            Assert.That(registry.TryResolveRoute("token-1", out _), Is.False);
            Assert.That(registry.TryConsumeDecision("token-1", "GET", "https://example.test/live", now, out _), Is.False);
        });
    }

    private static ProxyNavigationRoute CreateRoute(string contextId, string routeToken, long revision)
        => new()
        {
            SessionId = "session-1",
            TabId = "tab-1",
            ContextId = contextId,
            RouteToken = routeToken,
            UpstreamProxy = "http://127.0.0.1:8181",
            Revision = revision,
        };

    private static ProxyNavigationPendingDecision CreateDecision(
        string absoluteUrl,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc = null,
        string method = "GET",
        string requestId = "request-1")
        => new()
        {
            RequestId = requestId,
            Method = method,
            AbsoluteUrl = absoluteUrl,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc ?? issuedAtUtc.AddSeconds(5),
            Action = ProxyNavigationDecisionAction.Fulfill,
            StatusCode = 200,
            ReasonPhrase = "OK",
            ResponseBody = [1, 2, 3],
        };
}