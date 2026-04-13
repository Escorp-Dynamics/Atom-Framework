using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного inbox API Mailinator.
/// </summary>
public sealed class MailinatorPublicAccount : ProviderTemporaryEmailAccount<MailinatorPublicProvider>
{
    internal MailinatorPublicAccount(MailinatorPublicProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Mailinator public inbox не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Локальная часть адреса для публичного inbox endpoint.
    /// </summary>
    public string Login { get; }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}