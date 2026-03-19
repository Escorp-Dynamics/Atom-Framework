using System.Net;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Канонизирует только IP-литералы и оставляет hostname-значения без DNS-разрешения.
/// </summary>
public sealed class LiteralProxyDedupKeyResolver : IProxyDedupKeyResolver
{
    /// <inheritdoc/>
    public ValueTask<string> GetKeyAsync(ServiceProxy proxy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var host = proxy.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return ValueTask.FromResult(string.Empty);
        }

        var candidate = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        return ValueTask.FromResult(IPAddress.TryParse(candidate, out var address) ? address.ToString() : host);
    }
}