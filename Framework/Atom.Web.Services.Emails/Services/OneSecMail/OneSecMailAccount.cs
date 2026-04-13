using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе query API 1SecMail.
/// </summary>
public sealed class OneSecMailAccount : ProviderTemporaryEmailAccount<OneSecMailProvider>
{

    internal OneSecMailAccount(OneSecMailProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "1secmail не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Логин части адреса для upstream query API.
    /// </summary>
    public string Login { get; }

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}