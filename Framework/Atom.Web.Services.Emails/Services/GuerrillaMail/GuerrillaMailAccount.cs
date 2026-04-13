using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе session-oriented API GuerrillaMail.
/// </summary>
public sealed class GuerrillaMailAccount : SessionTemporaryEmailAccount<GuerrillaMailProvider>
{

    internal GuerrillaMailAccount(GuerrillaMailProvider provider, Guid id, string address, string sessionToken, ILogger? logger = null)
        : base(provider, id, address, sessionToken, "GuerrillaMail не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Session token текущего GuerrillaMail mailbox.
    /// </summary>
    public string SessionToken => SessionKey;

    /// <summary>
    /// Локальная часть адреса в upstream API.
    /// </summary>
    public string Login { get; }

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}