namespace Atom.Web.Emails.Services;

/// <summary>
/// Общий контракт Hydra-compatible операций провайдера, используемых shared account layer.
/// </summary>
public interface IHydraTemporaryEmailProviderOperations
{
    /// <summary>
    /// Аутентифицирует временный почтовый аккаунт и возвращает bearer token.
    /// </summary>
    ValueTask<string> AuthenticateAsync(string address, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Загружает актуальный snapshot inbox для указанного аккаунта.
    /// </summary>
    ValueTask<IEnumerable<Mail>> LoadMessagesAsync(TemporaryEmailAccount account, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Помечает upstream-письмо как прочитанное.
    /// </summary>
    ValueTask MarkAsReadAsync(string token, string upstreamMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет upstream-письмо.
    /// </summary>
    ValueTask DeleteMailAsync(string token, string upstreamMessageId, CancellationToken cancellationToken);
}