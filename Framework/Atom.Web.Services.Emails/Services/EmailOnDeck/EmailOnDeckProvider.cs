using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе form-url-encoded API EmailOnDeck.
/// </summary>
public sealed class EmailOnDeckProvider : FixedDomainTemporaryEmailProvider<EmailOnDeckProviderOptions>
{
    /// <summary>
    /// Базовый URL API EmailOnDeck.
    /// </summary>
    public const string DefaultApiUrl = "https://www.emailondeck.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера EmailOnDeck с настройками по умолчанию.
    /// </summary>
    public EmailOnDeckProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new EmailOnDeckProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера EmailOnDeck с явными настройками.
    /// </summary>
    public EmailOnDeckProvider(EmailOnDeckProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("EmailOnDeck", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        var login = TemporaryEmailAddressUtility.ExtractUserName(address);
        var domain = TemporaryEmailAddressUtility.ExtractDomain(address);

        var payload = await SendAndReadJsonAsync(
            CreateFormRequest("api/create", ("name", login), ("domain", domain)),
            EmailOnDeckJsonContext.Default.EmailOnDeckCreateMailboxResponse,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload?.SessionToken))
        {
            throw new InvalidOperationException("EmailOnDeck не вернул session token для созданного mailbox.");
        }

        var resolvedAddress = string.IsNullOrWhiteSpace(payload.Email) ? address : payload.Email;
        return new EmailOnDeckAccount(this, TemporaryEmailMailMapper.CreateStableId(resolvedAddress), resolvedAddress, payload.SessionToken, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(EmailOnDeckAccount account, CancellationToken cancellationToken)
    {
        var payload = await SendAndReadJsonAsync(
            CreateFormRequest("api/inbox", ("email", account.Address), ("sessionToken", account.SessionToken)),
            EmailOnDeckJsonContext.Default.EmailOnDeckMessageResponseArray,
            cancellationToken).ConfigureAwait(false);

        if (payload is not { Length: > 0 })
        {
            return [];
        }

        return payload
            .Where(static message => !string.IsNullOrWhiteSpace(message.Id))
            .Select(message => CreateMail(account, message))
            .ToArray();
    }

    internal async ValueTask DeleteMailAsync(EmailOnDeckAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        await SendAndEnsureSuccessAsync(
            CreateFormRequest(
                "api/delete",
                ("email", account.Address),
                ("sessionToken", account.SessionToken),
                ("messageId", upstreamMessageId)),
            cancellationToken).ConfigureAwait(false);
    }

    private static EmailOnDeckMail CreateMail(EmailOnDeckAccount account, EmailOnDeckMessageResponse payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Id);

        return new EmailOnDeckMail(
            account,
            payload.Id,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{payload.Id}"),
            payload.From ?? string.Empty,
            account.Address,
            payload.Subject ?? string.Empty,
            payload.Body ?? string.Empty,
            payload.Read);
    }

    private static HttpRequestMessage CreateFormRequest(string path, params (string Key, string Value)[] values)
    {
        var request = CreateRequest(HttpMethod.Post, path, acceptJsonLd: false);
        request.Content = new FormUrlEncodedContent(values.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
        return request;
    }
}