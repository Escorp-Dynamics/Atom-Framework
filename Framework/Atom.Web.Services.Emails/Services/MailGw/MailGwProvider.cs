using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного API mail.gw.
/// </summary>
public sealed class MailGwProvider : HydraTemporaryEmailProvider<MailGwProvider, MailGwProviderOptions, MailGwAccount, MailGwMail>
{
    /// <summary>
    /// Базовый URL публичного API mail.gw.
    /// </summary>
    public const string DefaultApiUrl = "https://api.mail.gw/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера mail.gw.
    /// </summary>
    public MailGwProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MailGwProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера mail.gw с явными настройками.
    /// </summary>
    public MailGwProvider(MailGwProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override string ProviderDisplayName => "mail.gw";

    /// <inheritdoc/>
    protected override MailGwAccount CreateHydraAccount(
        MailGwProvider provider,
        string upstreamAccountId,
        Guid id,
        string address,
        string password,
        string token,
        ILogger? logger)
        => new(provider, upstreamAccountId, id, address, password, token, logger);

    /// <inheritdoc/>
    protected override MailGwMail CreateHydraMail(
        MailGwAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        => new(account, upstreamId, id, from, to, subject, body, isRead);
}