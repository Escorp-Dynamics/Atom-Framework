namespace Atom.Web.Proxies.Services;

/// <summary>
/// Разрешает ключ дедупликации для прокси в общем пуле.
/// </summary>
public interface IProxyDedupKeyResolver
{
    /// <summary>
    /// Возвращает детерминированный ключ дедупликации для прокси.
    /// </summary>
    /// <param name="proxy">Прокси, для которого требуется ключ.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<string> GetKeyAsync(ServiceProxy proxy, CancellationToken cancellationToken = default);
}