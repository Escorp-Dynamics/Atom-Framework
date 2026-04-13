using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе resource-path API temp-mail.org.
/// </summary>
public sealed class TempMailOrgAccount : ProviderTemporaryEmailAccount<TempMailOrgProvider>
{

    internal TempMailOrgAccount(TempMailOrgProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "temp-mail.org не поддерживает исходящую отправку через текущий провайдер.", logger) { }

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}