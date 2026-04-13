using Atom.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе query API 1SecMail.
/// </summary>
public sealed class OneSecMailProvider : DomainRefreshingTemporaryEmailProvider<OneSecMailProviderOptions>
{
    /// <summary>
    /// Базовый URL query API 1SecMail.
    /// </summary>
    public const string DefaultApiUrl = "https://www.1secmail.com/api/v1/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера 1SecMail с настройками по умолчанию.
    /// </summary>
    public OneSecMailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new OneSecMailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера 1SecMail с явными настройками.
    /// </summary>
    public OneSecMailProvider(OneSecMailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);

        return new OneSecMailAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(OneSecMailAccount account, CancellationToken cancellationToken)
    {
        var messages = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(
                ("action", "getMessages"),
                ("login", account.Login),
                ("domain", account.Domain)), acceptJsonLd: false),
            OneSecMailJsonContext.Default.OneSecMailMessageSummaryResponseArray,
            cancellationToken).ConfigureAwait(false);

        if (messages is not { Length: > 0 })
        {
            return [];
        }

        var mails = new List<Mail>(messages.Length);
        foreach (var summary in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (summary.Id <= 0)
            {
                continue;
            }

            var detail = await GetMessageAsync(account, summary.Id, cancellationToken).ConfigureAwait(false);
            mails.Add(detail is null ? CreateMail(account, summary) : CreateMail(account, detail));
        }

        return mails;
    }

    internal async ValueTask DeleteMailAsync(OneSecMailAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, BuildQueryPath(
            ("action", "deleteMessage"),
            ("login", account.Login),
            ("domain", account.Domain),
            ("id", upstreamMessageId)), acceptJsonLd: false);

        await SendAndEnsureSuccessAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async ValueTask<string[]> LoadAvailableDomainsCoreAsync(CancellationToken cancellationToken)
    {
        var domains = await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(("action", "getDomainList")), acceptJsonLd: false),
            OneSecMailJsonContext.Default.StringArray,
            cancellationToken).ConfigureAwait(false);

        var resolved = domains is { Length: > 0 }
            ? domains
            : Options.FallbackDomains;

        return resolved
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc/>
    protected override string CreateNoDomainsMessage()
        => "1secmail не вернул ни одного доступного домена.";

    /// <inheritdoc/>
    protected override string CreateUnsupportedDomainMessage(string requestedDomain)
        => $"1secmail не поддерживает домен '{requestedDomain}'.";

    private async ValueTask<OneSecMailMessageDetailResponse?> GetMessageAsync(OneSecMailAccount account, int id, CancellationToken cancellationToken)
        => await SendAndReadJsonAsync(
            CreateRequest(HttpMethod.Get, BuildQueryPath(
                ("action", "readMessage"),
                ("login", account.Login),
                ("domain", account.Domain),
                ("id", id.ToString())), acceptJsonLd: false),
            OneSecMailJsonContext.Default.OneSecMailMessageDetailResponse,
            cancellationToken).ConfigureAwait(false);

    private static OneSecMailMail CreateMail(OneSecMailAccount account, OneSecMailMessageSummaryResponse summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(summary.Id, 0);

        return new OneSecMailMail(
            account,
            summary.Id.ToString(),
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{summary.Id}"),
            summary.From ?? string.Empty,
            account.Address,
            summary.Subject ?? string.Empty,
            string.Empty,
            isRead: false);
    }

    private static OneSecMailMail CreateMail(OneSecMailAccount account, OneSecMailMessageDetailResponse detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(detail.Id, 0);

        var body = !string.IsNullOrWhiteSpace(detail.TextBody)
            ? detail.TextBody
            : !string.IsNullOrWhiteSpace(detail.Body)
                ? detail.Body
                : detail.HtmlBody ?? string.Empty;

        return new OneSecMailMail(
            account,
            detail.Id.ToString(),
            TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{detail.Id}"),
            detail.From ?? string.Empty,
            account.Address,
            detail.Subject ?? string.Empty,
            body,
            isRead: false);
    }

    private static string BuildQueryPath(params (string Key, string Value)[] parameters)
    {
        var estimatedLength = 1;
        for (var index = 0; index < parameters.Length; index++)
        {
            estimatedLength += parameters[index].Key.Length + parameters[index].Value.Length + 2;
        }

        using var builder = new ValueStringBuilder(estimatedLength);
        builder.Append('?');
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