using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе form API EmailOnDeck.
/// </summary>
public sealed class EmailOnDeckAccount : SessionTemporaryEmailAccount<EmailOnDeckProvider>
{

    internal EmailOnDeckAccount(EmailOnDeckProvider provider, Guid id, string address, string sessionToken, ILogger? logger = null)
        : base(provider, id, address, sessionToken, "EmailOnDeck не поддерживает исходящую отправку через текущий провайдер.", logger) { }

    /// <summary>
    /// Сессионный токен upstream API.
    /// </summary>
    public string SessionToken => SessionKey;

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}