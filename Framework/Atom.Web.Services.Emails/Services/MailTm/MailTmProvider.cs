using System.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Первый concrete provider временной почты на базе публичного API mail.tm.
/// </summary>
public sealed class MailTmProvider : HydraTemporaryEmailProvider<MailTmProvider, MailTmProviderOptions, MailTmAccount, MailTmMail>
{
    /// <summary>
    /// Базовый URL публичного API mail.tm.
    /// </summary>
    public const string DefaultApiUrl = "https://api.mail.tm/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера mail.tm.
    /// </summary>
    public MailTmProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MailTmProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера mail.tm с явными настройками.
    /// </summary>
    public MailTmProvider(MailTmProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override string ProviderDisplayName => "mail.tm";

    /// <inheritdoc/>
    protected override MailTmAccount CreateHydraAccount(
        MailTmProvider provider,
        string upstreamAccountId,
        Guid id,
        string address,
        string password,
        string token,
        ILogger? logger)
        => new(provider, upstreamAccountId, id, address, password, token, logger);

    /// <inheritdoc/>
    protected override MailTmMail CreateHydraMail(
        MailTmAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        => new(account, upstreamId, id, from, to, subject, body, isRead);
}