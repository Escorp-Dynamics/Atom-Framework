namespace Atom.Web.Proxies.Services;

/// <summary>
/// Предоставляет стандартные стратегии вычисления dedup key для прокси.
/// </summary>
public static class ProxyDedupKeyResolvers
{
    /// <summary>
    /// Resolver, канонизирующий только IP-литералы.
    /// </summary>
    public static IProxyDedupKeyResolver Literal { get; } = new LiteralProxyDedupKeyResolver();
}