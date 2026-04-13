namespace Atom.Web.Emails.Services;

/// <summary>
/// Общий контракт операций над письмами HTTP-ориентированного upstream-провайдера.
/// </summary>
public interface IHttpTemporaryEmailMailOperations
{
    /// <summary>
    /// Помечает upstream-письмо как прочитанное.
    /// </summary>
    ValueTask MarkUpstreamMailAsReadAsync(string upstreamMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет upstream-письмо.
    /// </summary>
    ValueTask DeleteUpstreamMailAsync(string upstreamMessageId, CancellationToken cancellationToken);
}