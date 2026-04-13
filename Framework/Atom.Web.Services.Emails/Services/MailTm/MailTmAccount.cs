using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе upstream API mail.tm.
/// </summary>
public sealed class MailTmAccount : HydraTemporaryEmailAccount<MailTmProvider>
{
    private readonly string upstreamAccountId;

    internal MailTmAccount(
        MailTmProvider provider,
        string upstreamAccountId,
        Guid id,
        string address,
        string password,
        string? token,
        ILogger? logger = null)
        : base(provider, id, address, password, token, logger)
    {
        this.upstreamAccountId = upstreamAccountId;
    }

    /// <summary>
    /// Идентификатор аккаунта в upstream API mail.tm.
    /// </summary>
    public string UpstreamAccountId => upstreamAccountId;

    /// <inheritdoc/>
    protected override string ProviderDisplayName => "mail.tm";
}