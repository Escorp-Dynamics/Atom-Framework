using Atom.Web.Emails;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного HTML inbox Tempail.
/// </summary>
public sealed class TempailAccount : ProviderTemporaryEmailAccount<TempailProvider>
{
    internal TempailAccount(TempailProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Tempail не поддерживает исходящую отправку через текущий провайдер.", logger)
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