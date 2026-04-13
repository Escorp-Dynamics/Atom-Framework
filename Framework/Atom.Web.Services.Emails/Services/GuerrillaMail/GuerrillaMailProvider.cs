using Atom.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе session-oriented API GuerrillaMail.
/// </summary>
public sealed class GuerrillaMailProvider : FixedDomainTemporaryEmailProvider<GuerrillaMailProviderOptions>
{
    /// <summary>
    /// Базовый URL API GuerrillaMail.
    /// </summary>
    public const string DefaultApiUrl = "https://api.guerrillamail.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера GuerrillaMail с настройками по умолчанию.
    /// </summary>
    public GuerrillaMailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new GuerrillaMailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера GuerrillaMail с явными настройками.
    /// </summary>
    public GuerrillaMailProvider(GuerrillaMailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("GuerrillaMail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        var login = TemporaryEmailAddressUtility.ExtractUserName(address);
        var domain = TemporaryEmailAddressUtility.ExtractDomain(address);

        var payload = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(
                ("f", "set_email_user"),
                ("email_user", login),
                ("domain", domain)), acceptJsonLd: false),
            GuerrillaMailJsonContext.Default.GuerrillaMailSetEmailAddressResponse,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload?.SessionToken))
        {
            throw new InvalidOperationException("GuerrillaMail не вернул session token для созданного mailbox.");
        }

        var resolvedAddress = string.IsNullOrWhiteSpace(payload.EmailAddress) ? address : payload.EmailAddress;
        return new GuerrillaMailAccount(this, TemporaryEmailMailMapper.CreateStableId(resolvedAddress), resolvedAddress, payload.SessionToken, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(GuerrillaMailAccount account, CancellationToken cancellationToken)
    {
        var inbox = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(
                ("f", "check_email"),
                ("seq", "0"),
                ("sid_token", account.SessionToken)), acceptJsonLd: false),
            GuerrillaMailJsonContext.Default.GuerrillaMailInboxResponse,
            cancellationToken).ConfigureAwait(false);

        var summaries = inbox?.Messages;
        if (summaries is not { Length: > 0 })
        {
            return [];
        }

        var mails = new List<Mail>(summaries.Length);
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(summary.MailId))
            {
                continue;
            }

            var detail = await GetMessageAsync(account, summary.MailId, cancellationToken).ConfigureAwait(false);
            mails.Add(detail is null ? CreateMail(account, summary) : CreateMail(account, detail, summary.MailRead != 0));
        }

        return mails;
    }

    internal async ValueTask DeleteMailAsync(GuerrillaMailAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, BuildQueryPath(
            ("f", "del_email"),
            ("sid_token", account.SessionToken),
            ("email_ids[]", upstreamMessageId)), acceptJsonLd: false);

        await SendAndEnsureSuccessAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GuerrillaMailMessageDetailResponse?> GetMessageAsync(GuerrillaMailAccount account, string mailId, CancellationToken cancellationToken)
        => await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(
                ("f", "fetch_email"),
                ("sid_token", account.SessionToken),
                ("email_id", mailId)), acceptJsonLd: false),
            GuerrillaMailJsonContext.Default.GuerrillaMailMessageDetailResponse,
            cancellationToken).ConfigureAwait(false);

    private static GuerrillaMailMail CreateMail(GuerrillaMailAccount account, GuerrillaMailMessageSummaryResponse summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary.MailId);

        return new GuerrillaMailMail(
            account,
            summary.MailId,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{summary.MailId}"),
            summary.From ?? string.Empty,
            account.Address,
            summary.Subject ?? string.Empty,
            summary.Excerpt ?? string.Empty,
            isRead: summary.MailRead != 0);
    }

    private static GuerrillaMailMail CreateMail(GuerrillaMailAccount account, GuerrillaMailMessageDetailResponse detail, bool isRead)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.MailId);

        return new GuerrillaMailMail(
            account,
            detail.MailId,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{detail.MailId}"),
            detail.From ?? string.Empty,
            account.Address,
            detail.Subject ?? string.Empty,
            detail.Body ?? string.Empty,
            isRead);
    }

    private static string BuildQueryPath(params (string Key, string Value)[] parameters)
    {
        var estimatedLength = "ajax.php?".Length;
        for (var index = 0; index < parameters.Length; index++)
        {
            estimatedLength += parameters[index].Key.Length + parameters[index].Value.Length + 2;
        }

        using var builder = new ValueStringBuilder(estimatedLength);
        builder.Append("ajax.php?");
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(parameters[index].Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameters[index].Value));
        }

        return builder.ToString();
    }
}