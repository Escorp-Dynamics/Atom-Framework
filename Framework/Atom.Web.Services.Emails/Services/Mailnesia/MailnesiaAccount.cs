using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе XML mailbox feed Mailnesia.
/// </summary>
public sealed class MailnesiaAccount : ProviderTemporaryEmailAccount<MailnesiaProvider>
{

    internal MailnesiaAccount(MailnesiaProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Mailnesia не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Логин части адреса для XML mailbox endpoint.
    /// </summary>
    public string Login { get; }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}