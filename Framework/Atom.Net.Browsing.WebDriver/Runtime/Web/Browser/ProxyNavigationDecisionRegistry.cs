using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Browsing.WebDriver;

internal enum ProxyNavigationDecisionAction
{
    Continue,
    Abort,
    Redirect,
    Fulfill,
}

internal sealed record ProxyNavigationRoute
{
    public required string SessionId { get; init; }

    public required string TabId { get; init; }

    public required string ContextId { get; init; }

    public required string RouteToken { get; init; }

    public string? UpstreamProxy { get; init; }

    public long Revision { get; init; }
}

internal sealed record ProxyNavigationPendingDecision
{
    public required string RequestId { get; init; }

    public required string Method { get; init; }

    public required string AbsoluteUrl { get; init; }

    public required DateTimeOffset IssuedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required ProxyNavigationDecisionAction Action { get; init; }

    public string? RedirectUrl { get; init; }

    public string? ForwardUrl { get; init; }

    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }

    public byte[]? RequestBody { get; init; }

    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }

    public byte[]? ResponseBody { get; init; }

    public int? StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }
}

internal sealed class ProxyNavigationDecisionRegistry
{
    private readonly ConcurrentDictionary<string, ProxyNavigationRouteState> routesByToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> tokensByContextId = new(StringComparer.Ordinal);

    public void UpsertRoute(ProxyNavigationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (string.IsNullOrWhiteSpace(route.SessionId))
            throw new ArgumentException("Route SessionId cannot be null or whitespace", nameof(route));

        if (string.IsNullOrWhiteSpace(route.TabId))
            throw new ArgumentException("Route TabId cannot be null or whitespace", nameof(route));

        if (string.IsNullOrWhiteSpace(route.ContextId))
            throw new ArgumentException("Route ContextId cannot be null or whitespace", nameof(route));

        if (string.IsNullOrWhiteSpace(route.RouteToken))
            throw new ArgumentException("Route RouteToken cannot be null or whitespace", nameof(route));

        if (tokensByContextId.TryGetValue(route.ContextId, out var existingToken)
            && !string.Equals(existingToken, route.RouteToken, StringComparison.Ordinal)
            && routesByToken.TryRemove(existingToken, out var removedState))
        {
            removedState.Clear();
        }

        var state = routesByToken.AddOrUpdate(
            route.RouteToken,
            static (_, nextRoute) => new ProxyNavigationRouteState(nextRoute),
            static (_, existingState, nextRoute) =>
            {
                existingState.Update(nextRoute);
                return existingState;
            },
            route);

        tokensByContextId[route.ContextId] = state.Route.RouteToken;
    }

    public bool TryResolveRoute(string routeToken, [NotNullWhen(true)] out ProxyNavigationRoute? route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeToken);

        if (routesByToken.TryGetValue(routeToken, out var state))
        {
            route = state.Route;
            return true;
        }

        route = null;
        return false;
    }

    public bool TryResolveToken(string contextId, [NotNullWhen(true)] out string? routeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        return tokensByContextId.TryGetValue(contextId, out routeToken);
    }

    public bool EnqueueDecision(string contextId, ProxyNavigationPendingDecision decision, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentNullException.ThrowIfNull(decision);
        ValidateDecision(decision);

        if (!tokensByContextId.TryGetValue(contextId, out var routeToken)
            || !routesByToken.TryGetValue(routeToken, out var state))
        {
            return false;
        }

        state.Enqueue(decision, nowUtc);
        return true;
    }

    public bool TryConsumeDecision(
        string routeToken,
        string method,
        string absoluteUrl,
        DateTimeOffset nowUtc,
        [NotNullWhen(true)] out ProxyNavigationPendingDecision? decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUrl);

        if (routesByToken.TryGetValue(routeToken, out var state))
            return state.TryConsume(method, absoluteUrl, nowUtc, out decision);

        decision = null;
        return false;
    }

    public bool RemoveRouteByContextId(string contextId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);

        if (!tokensByContextId.TryRemove(contextId, out var routeToken))
            return false;

        if (routesByToken.TryRemove(routeToken, out var state))
            state.Clear();

        return true;
    }

    private static void ValidateDecision(ProxyNavigationPendingDecision decision)
    {

        if (string.IsNullOrWhiteSpace(decision.RequestId))
            throw new ArgumentException("Pending decision RequestId cannot be null or whitespace", nameof(decision));

        if (string.IsNullOrWhiteSpace(decision.Method))
            throw new ArgumentException("Pending decision Method cannot be null or whitespace", nameof(decision));

        if (string.IsNullOrWhiteSpace(decision.AbsoluteUrl))
            throw new ArgumentException("Pending decision AbsoluteUrl cannot be null or whitespace", nameof(decision));

        if (!Uri.TryCreate(decision.AbsoluteUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Pending proxy navigation decision must target an absolute URL", nameof(decision));
    }

    private sealed class ProxyNavigationRouteState(ProxyNavigationRoute route)
    {
        private readonly Lock gate = new();
        private readonly List<ProxyNavigationPendingDecision> pendingDecisions = [];

        public ProxyNavigationRoute Route { get; private set; } = route;

        public void Update(ProxyNavigationRoute route)
        {
            lock (gate)
            {
                Route = route;
                pendingDecisions.Clear();
            }
        }

        public void Enqueue(ProxyNavigationPendingDecision decision, DateTimeOffset nowUtc)
        {
            lock (gate)
            {
                PurgeExpired(nowUtc);
                pendingDecisions.Add(decision);
            }
        }

        public bool TryConsume(string method, string absoluteUrl, DateTimeOffset nowUtc, [NotNullWhen(true)] out ProxyNavigationPendingDecision? decision)
        {
            lock (gate)
            {
                PurgeExpired(nowUtc);

                for (var index = 0; index < pendingDecisions.Count; index++)
                {
                    var candidate = pendingDecisions[index];
                    if (!string.Equals(candidate.Method, method, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(candidate.AbsoluteUrl, absoluteUrl, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    pendingDecisions.RemoveAt(index);
                    decision = candidate;
                    return true;
                }
            }

            decision = null;
            return false;
        }

        public void Clear()
        {
            lock (gate)
            {
                pendingDecisions.Clear();
            }
        }

        private void PurgeExpired(DateTimeOffset nowUtc)
        {
            for (var index = pendingDecisions.Count - 1; index >= 0; index--)
            {
                if (pendingDecisions[index].ExpiresAtUtc <= nowUtc)
                    pendingDecisions.RemoveAt(index);
            }
        }
    }
}