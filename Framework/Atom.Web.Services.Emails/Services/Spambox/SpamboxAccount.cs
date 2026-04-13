using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного inbox API Spambox.
/// </summary>
public sealed class SpamboxAccount : ProviderTemporaryEmailAccount<SpamboxProvider>
{
    internal SpamboxAccount(SpamboxProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Spambox не поддерживает исходящую отправку через текущий провайдер.", logger)
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