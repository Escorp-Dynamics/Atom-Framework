using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе JSON POST API Emailnator.
/// </summary>
public sealed class EmailnatorProvider : FixedDomainTemporaryEmailProvider<EmailnatorProviderOptions>
{
    /// <summary>
    /// Базовый URL API Emailnator.
    /// </summary>
    public const string DefaultApiUrl = "https://www.emailnator.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Emailnator с настройками по умолчанию.
    /// </summary>
    public EmailnatorProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new EmailnatorProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Emailnator с явными настройками.
    /// </summary>
    public EmailnatorProvider(EmailnatorProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Emailnator", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        var login = TemporaryEmailAddressUtility.ExtractUserName(address);
        var domain = TemporaryEmailAddressUtility.ExtractDomain(address);

        var payload = await SendAndReadJsonAsync(
            CreateJsonPostRequest(
                "generate-email",
                new EmailnatorCreateMailboxRequest
                {
                    Name = login,
                    Domain = domain,
                },
                EmailnatorJsonContext.Default.EmailnatorCreateMailboxRequest),
            EmailnatorJsonContext.Default.EmailnatorCreateMailboxResponse,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload?.SessionId))
        {
            throw new InvalidOperationException("Emailnator не вернул session identifier для созданного mailbox.");
        }

        var resolvedAddress = string.IsNullOrWhiteSpace(payload.Email) ? address : payload.Email;
        return new EmailnatorAccount(this, TemporaryEmailMailMapper.CreateStableId(resolvedAddress), resolvedAddress, payload.SessionId, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(EmailnatorAccount account, CancellationToken cancellationToken)
    {
        var list = await SendAndReadJsonAsync(
            CreateJsonPostRequest(
                "message-list",
                new EmailnatorMessageListRequest
                {
                    Email = account.Address,
                    SessionId = account.SessionId,
                },
                EmailnatorJsonContext.Default.EmailnatorMessageListRequest),
            EmailnatorJsonContext.Default.EmailnatorMessageListResponse,
            cancellationToken).ConfigureAwait(false);

        var messages = list?.Messages;
        if (messages is not { Length: > 0 })
        {
            return [];
        }

        var mails = new List<Mail>(messages.Length);
        foreach (var summary in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(summary.MessageId))
            {
                continue;
            }

            var detail = await GetMessageAsync(account, summary.MessageId, cancellationToken).ConfigureAwait(false);
            mails.Add(detail is null ? CreateMail(account, summary) : CreateMail(account, detail));
        }

        return mails;
    }

    internal async ValueTask DeleteMailAsync(EmailnatorAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        await SendAndEnsureSuccessAsync(
            CreateJsonPostRequest(
                "delete-message",
                new EmailnatorDeleteMessageRequest
                {
                    Email = account.Address,
                    SessionId = account.SessionId,
                    MessageId = upstreamMessageId,
                },
                EmailnatorJsonContext.Default.EmailnatorDeleteMessageRequest),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EmailnatorMessageDetailResponse?> GetMessageAsync(EmailnatorAccount account, string messageId, CancellationToken cancellationToken)
        => await SendAndReadJsonAsync(
            CreateJsonPostRequest(
                "message-detail",
                new EmailnatorMessageDetailRequest
                {
                    Email = account.Address,
                    SessionId = account.SessionId,
                    MessageId = messageId,
                },
                EmailnatorJsonContext.Default.EmailnatorMessageDetailRequest),
            EmailnatorJsonContext.Default.EmailnatorMessageDetailResponse,
            cancellationToken).ConfigureAwait(false);

    private static EmailnatorMail CreateMail(EmailnatorAccount account, EmailnatorMessageSummaryResponse summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary.MessageId);

        return new EmailnatorMail(
            account,
            summary.MessageId,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{summary.MessageId}"),
            summary.From ?? string.Empty,
            account.Address,
            summary.Subject ?? string.Empty,
            string.Empty,
            isRead: false);
    }

    private static EmailnatorMail CreateMail(EmailnatorAccount account, EmailnatorMessageDetailResponse detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.MessageId);

        return new EmailnatorMail(
            account,
            detail.MessageId,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{detail.MessageId}"),
            detail.From ?? string.Empty,
            account.Address,
            detail.Subject ?? string.Empty,
            detail.Content ?? string.Empty,
            isRead: false);
    }

    private static HttpRequestMessage CreateJsonPostRequest<TPayload>(string path, TPayload payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TPayload> typeInfo)
    {
        var request = CreateRequest(HttpMethod.Post, path, acceptJsonLd: false);
        request.Content = CreateJsonContent(payload, typeInfo, "application/json");
        return request;
    }
}