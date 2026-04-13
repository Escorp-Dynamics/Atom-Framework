using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе HTML inbox Mohmal.
/// </summary>
public sealed class MohmalAccount : ProviderTemporaryEmailAccount<MohmalProvider>
{
    internal MohmalAccount(MohmalProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Mohmal не поддерживает исходящую отправку через текущий провайдер.", logger)
    {
        Login = TemporaryEmailAddressUtility.ExtractUserName(address);
    }

    /// <summary>
    /// Локальная часть адреса для HTML mailbox page.
    /// </summary>
    public string Login { get; }

    /// <inheritdoc/>
    protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        => ProviderCore.LoadMessagesAsync(this, cancellationToken);
}