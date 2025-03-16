
namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет сервис прокси luminati.io.
/// </summary>
public class LuminatiService : ProxyService
{
    /// <inheritdoc/>
    public override ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}