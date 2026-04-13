using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе REST API temp-mail.io.
/// </summary>
public sealed class TempMailIoAccount : ProviderTemporaryEmailAccount<TempMailIoProvider>
{

    internal TempMailIoAccount(TempMailIoProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "temp-mail.io не поддерживает исходящую отправку через текущий провайдер.", logger) { }

    internal ValueTask DeleteMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        => ProviderCore.DeleteMailAsync(this, upstreamMessageId, cancellationToken);

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}