using Atom.Net.Proxies;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Описывает selection hints для targeted provider fetch.
/// </summary>
public sealed record ProxyProviderFetchRequest(
    int RequestedCount,
    IReadOnlyList<ProxyType> Protocols,
    IReadOnlyList<Country> Countries,
    IReadOnlyList<AnonymityLevel> AnonymityLevels,
    bool AllowPartial = true,
    bool RequireUniqueHosts = true);