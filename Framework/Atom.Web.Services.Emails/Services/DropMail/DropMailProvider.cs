using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе GraphQL API DropMail.
/// </summary>
public sealed class DropMailProvider : FixedDomainTemporaryEmailProvider<DropMailProviderOptions>
{
    private const string IntroduceSessionQuery = "mutation IntroduceSession($address:String!){introduceSession(input:{address:$address}){id addresses{address}}}";
    private const string SessionInboxQuery = "query Session($id:String!){session(id:$id){id mails{id fromAddr toAddr headerSubject text html seen}}}";

    /// <summary>
    /// Базовый URL GraphQL API DropMail.
    /// </summary>
    public const string DefaultApiUrl = "https://dropmail.me/api/graphql/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера DropMail с настройками по умолчанию.
    /// </summary>
    public DropMailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new DropMailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера DropMail с явными настройками.
    /// </summary>
    public DropMailProvider(DropMailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("DropMail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);

        using var httpRequest = CreateGraphQLRequest(new DropMailGraphQLRequest
        {
            Query = IntroduceSessionQuery,
            Variables = new DropMailGraphQLVariables
            {
                Address = address,
            },
        });

        var payload = await SendAndReadJsonAsync(
            httpRequest,
            DropMailJsonContext.Default.DropMailIntroduceSessionEnvelope,
            cancellationToken).ConfigureAwait(false);

        var session = payload?.Data?.Session;
        if (string.IsNullOrWhiteSpace(session?.Id))
        {
            throw new InvalidOperationException("DropMail не вернул session identifier для созданного mailbox.");
        }

        var resolvedAddress = session.Addresses?
            .Select(static item => item.Address)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? address;

        return new DropMailAccount(this, TemporaryEmailMailMapper.CreateStableId(resolvedAddress), resolvedAddress, session.Id, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(DropMailAccount account, CancellationToken cancellationToken)
    {
        using var httpRequest = CreateGraphQLRequest(new DropMailGraphQLRequest
        {
            Query = SessionInboxQuery,
            Variables = new DropMailGraphQLVariables
            {
                SessionId = account.SessionId,
            },
        });

        var payload = await SendAndReadJsonAsync(
            httpRequest,
            DropMailJsonContext.Default.DropMailSessionEnvelope,
            cancellationToken).ConfigureAwait(false);

        var mails = payload?.Data?.Session?.Mails;
        if (mails is not { Length: > 0 })
        {
            return [];
        }

        return mails
            .Where(static mail => !string.IsNullOrWhiteSpace(mail.Id))
            .Select(mail => CreateMail(account, mail))
            .ToArray();
    }

    private static DropMailMail CreateMail(DropMailAccount account, DropMailMailResponse payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Id);

        var body = !string.IsNullOrWhiteSpace(payload.Text)
            ? payload.Text
            : payload.Html ?? string.Empty;

        return new DropMailMail(
            payload.Id,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{payload.Id}"),
            payload.FromAddress ?? string.Empty,
            string.IsNullOrWhiteSpace(payload.ToAddress) ? account.Address : payload.ToAddress,
            payload.Subject ?? string.Empty,
            body,
            payload.Seen);
    }

    private static HttpRequestMessage CreateGraphQLRequest(DropMailGraphQLRequest payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var request = CreateRequest(HttpMethod.Post, string.Empty, acceptJsonLd: false);
        request.Content = CreateJsonContent(payload, DropMailJsonContext.Default.DropMailGraphQLRequest, "application/json");
        return request;
    }
}