namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет сервис прокси proxynova.com.
/// </summary>
public class ProxyNovaService : ProxyService
{
    /// <inheritdoc/>
    public override ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}