namespace Atom.Web.Proxies.Services;

internal interface IProxyPoolSnapshotSource
{
    ValueTask<ServiceProxy[]> GetPoolSnapshotAsync(CancellationToken cancellationToken);
}