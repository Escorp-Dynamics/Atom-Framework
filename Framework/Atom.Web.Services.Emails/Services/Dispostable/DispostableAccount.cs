using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе plain-text API Dispostable.
/// </summary>
public sealed class DispostableAccount : ProviderTemporaryEmailAccount<DispostableProvider>
{

    internal DispostableAccount(DispostableProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Dispostable не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Локальная часть адреса для upstream mailbox key.
    /// </summary>
    public string Login { get; }

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}