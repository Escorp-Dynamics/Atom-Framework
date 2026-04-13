using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет провайдер прокси luminati.io.
/// </summary>
public class LuminatiProvider : ProxyProvider
{
    /// <summary>
    /// Создаёт провайдер luminati.io.
    /// </summary>
    public LuminatiProvider(ILogger? logger = null)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<ServiceProxy>> LoadPoolAsync(CancellationToken cancellationToken)
    => ValueTask.FromException<IEnumerable<ServiceProxy>>(new NotSupportedException("LuminatiProvider is not implemented."));
}