using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе resource-oriented REST API temp-mail.io.
/// </summary>
public sealed class TempMailIoProvider : FixedDomainTemporaryEmailProvider<TempMailIoProviderOptions>
{
    /// <summary>
    /// Базовый URL API temp-mail.io.
    /// </summary>
    public const string DefaultApiUrl = "https://api.internal.temp-mail.io/api/v3/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера temp-mail.io с настройками по умолчанию.
    /// </summary>
    public TempMailIoProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new TempMailIoProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера temp-mail.io с явными настройками.
    /// </summary>
    public TempMailIoProvider(TempMailIoProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("temp-mail.io", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        var login = TemporaryEmailAddressUtility.ExtractUserName(address);
        var domain = TemporaryEmailAddressUtility.ExtractDomain(address);

        using var httpRequest = CreateRequest(HttpMethod.Post, "email/new", acceptJsonLd: false);
        httpRequest.Content = CreateJsonContent(
            new TempMailIoCreateMailboxRequest
            {
                Name = login,
                Domain = domain,
            },
            TempMailIoJsonContext.Default.TempMailIoCreateMailboxRequest,
            "application/json");

        var payload = await SendAndReadJsonAsync(
            httpRequest,
            TempMailIoJsonContext.Default.TempMailIoCreateMailboxResponse,
            cancellationToken).ConfigureAwait(false);

        var resolvedAddress = string.IsNullOrWhiteSpace(payload?.Email) ? address : payload.Email;
        return new TempMailIoAccount(this, TemporaryEmailMailMapper.CreateStableId(resolvedAddress), resolvedAddress, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(TempMailIoAccount account, CancellationToken cancellationToken)
    {
        var path = $"email/{Uri.EscapeDataString(account.Address)}/messages";
        var payload = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, path, acceptJsonLd: false),
            TempMailIoJsonContext.Default.TempMailIoMessageResponseArray,
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

    internal async ValueTask DeleteMailAsync(TempMailIoAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        var path = $"email/{Uri.EscapeDataString(account.Address)}/messages/{Uri.EscapeDataString(upstreamMessageId)}";
        await SendAndEnsureSuccessAsync(CreateRequest(HttpMethod.Delete, path, acceptJsonLd: false), cancellationToken).ConfigureAwait(false);
    }

    private static TempMailIoMail CreateMail(TempMailIoAccount account, TempMailIoMessageResponse payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Id);

        var body = !string.IsNullOrWhiteSpace(payload.TextBody)
            ? payload.TextBody
            : payload.HtmlBody ?? string.Empty;

        return new TempMailIoMail(
            account,
            payload.Id,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{payload.Id}"),
            payload.From ?? string.Empty,
            string.IsNullOrWhiteSpace(payload.To) ? account.Address : payload.To,
            payload.Subject ?? string.Empty,
            body,
            payload.Seen);
    }
}