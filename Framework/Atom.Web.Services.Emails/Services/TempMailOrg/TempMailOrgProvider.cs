using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе mailbox-keyed API temp-mail.org.
/// </summary>
public sealed class TempMailOrgProvider : FixedDomainTemporaryEmailProvider<TempMailOrgProviderOptions>
{
    /// <summary>
    /// Базовый URL API temp-mail.org.
    /// </summary>
    public const string DefaultApiUrl = "https://api.temp-mail.org/request/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера temp-mail.org с настройками по умолчанию.
    /// </summary>
    public TempMailOrgProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new TempMailOrgProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера temp-mail.org с явными настройками.
    /// </summary>
    public TempMailOrgProvider(TempMailOrgProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("temp-mail.org", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new TempMailOrgAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(TempMailOrgAccount account, CancellationToken cancellationToken)
    {
        var path = $"mail/id/{Uri.EscapeDataString(account.Address)}";
        var payload = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, path, acceptJsonLd: false),
            TempMailOrgJsonContext.Default.TempMailOrgMessageResponseArray,
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

    internal async ValueTask DeleteMailAsync(TempMailOrgAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        var path = $"delete/id/{Uri.EscapeDataString(account.Address)}/{Uri.EscapeDataString(upstreamMessageId)}";
        await SendAndEnsureSuccessAsync(CreateRequest(HttpMethod.Delete, path, acceptJsonLd: false), cancellationToken).ConfigureAwait(false);
    }

    private static TempMailOrgMail CreateMail(TempMailOrgAccount account, TempMailOrgMessageResponse payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Id);

        var body = !string.IsNullOrWhiteSpace(payload.Text)
            ? payload.Text
            : payload.Html ?? string.Empty;

        return new TempMailOrgMail(
            account,
            payload.Id,
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{payload.Id}"),
            payload.From ?? string.Empty,
            account.Address,
            payload.Subject ?? string.Empty,
            body);
    }
}