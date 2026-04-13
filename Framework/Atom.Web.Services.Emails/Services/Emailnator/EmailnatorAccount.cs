using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе JSON API Emailnator.
/// </summary>
public sealed class EmailnatorAccount : SessionTemporaryEmailAccount<EmailnatorProvider>
{

    internal EmailnatorAccount(EmailnatorProvider provider, Guid id, string address, string sessionId, ILogger? logger = null)
        : base(provider, id, address, sessionId, "Emailnator не поддерживает исходящую отправку через текущий провайдер.", logger) { }

    /// <summary>
    /// Идентификатор upstream session.
    /// </summary>
    public string SessionId => SessionKey;

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}