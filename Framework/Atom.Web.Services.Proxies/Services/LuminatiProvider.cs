namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет провайдер прокси luminati.io.
/// </summary>
public class LuminatiProvider : ProxyProvider
{
    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
    => ValueTask.FromException<IEnumerable<ServiceProxy>>(new NotSupportedException("LuminatiProvider is not implemented."));
}