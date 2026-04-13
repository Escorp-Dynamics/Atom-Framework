using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Временный почтовый аккаунт на базе upstream API mail.gw.
/// </summary>
public sealed class MailGwAccount : HydraTemporaryEmailAccount<MailGwProvider>
{
    private readonly string upstreamAccountId;

    internal MailGwAccount(
        MailGwProvider provider,
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
    /// Идентификатор аккаунта в upstream API mail.gw.
    /// </summary>
    public string UpstreamAccountId => upstreamAccountId;

    /// <inheritdoc/>
    protected override string ProviderDisplayName => "mail.gw";
}