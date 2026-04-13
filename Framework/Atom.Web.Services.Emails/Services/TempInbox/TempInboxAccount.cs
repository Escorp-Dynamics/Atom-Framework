using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного inbox API TempInbox.
/// </summary>
public sealed class TempInboxAccount : ProviderTemporaryEmailAccount<TempInboxProvider>
{
    internal TempInboxAccount(TempInboxProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "TempInbox не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Локальная часть адреса для inbox endpoint.
    /// </summary>
    public string Login { get; }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}