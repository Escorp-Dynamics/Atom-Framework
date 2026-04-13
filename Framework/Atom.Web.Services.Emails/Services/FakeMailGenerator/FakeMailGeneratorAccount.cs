using Atom.Web.Emails;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе публичного HTML inbox Fake Mail Generator.
/// </summary>
public sealed class FakeMailGeneratorAccount : ProviderTemporaryEmailAccount<FakeMailGeneratorProvider>
{
    internal FakeMailGeneratorAccount(FakeMailGeneratorProvider provider, Guid id, string address, ILogger? logger = null)
        : base(provider, id, address, "Fake Mail Generator не поддерживает исходящую отправку через текущий провайдер.", logger)
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