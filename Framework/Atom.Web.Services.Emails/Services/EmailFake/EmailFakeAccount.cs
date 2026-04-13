using Atom.Web.Emails;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного HTML inbox emailfake.com.
/// </summary>
public sealed class EmailFakeAccount : ProviderTemporaryEmailAccount<EmailFakeProvider>
{
    internal EmailFakeAccount(EmailFakeProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "emailfake.com не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Локальная часть адреса.
    /// </summary>
    public string Login { get; }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}