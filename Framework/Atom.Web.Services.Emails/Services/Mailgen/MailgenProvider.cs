using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox Mailgen.
/// </summary>
public sealed partial class MailgenProvider : FixedDomainTemporaryEmailProvider<MailgenProviderOptions>
{
    /// <summary>
    /// Базовый URL Mailgen.
    /// </summary>
    public const string DefaultApiUrl = "https://mailgen.biz/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mailgen с настройками по умолчанию.
    /// </summary>
    public MailgenProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MailgenProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mailgen с явными настройками.
    /// </summary>
    public MailgenProvider(MailgenProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Mailgen", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MailgenAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MailgenAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, Uri.EscapeDataString(account.Address), acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<MailgenMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new MailgenMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                WebUtility.HtmlDecode(match.Groups["from"].Value),
                address,
                WebUtility.HtmlDecode(match.Groups["subject"].Value),
                WebUtility.HtmlDecode(match.Groups["body"].Value));
        }
    }

    [GeneratedRegex("<li\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>\\s*<div\\s+class=\"body\">(?<body>.*?)</div>\\s*</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}