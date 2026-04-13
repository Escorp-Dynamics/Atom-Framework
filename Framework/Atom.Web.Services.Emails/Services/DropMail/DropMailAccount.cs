using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе GraphQL API DropMail.
/// </summary>
public sealed class DropMailAccount : SessionTemporaryEmailAccount<DropMailProvider>
{

    internal DropMailAccount(DropMailProvider provider, Guid id, string address, string sessionId, ILogger? logger = null)
        : base(provider, id, address, sessionId, "DropMail не поддерживает исходящую отправку через текущий провайдер.", logger) { }

    /// <summary>
    /// Идентификатор upstream session в DropMail.
    /// </summary>
    public string SessionId => SessionKey;

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}