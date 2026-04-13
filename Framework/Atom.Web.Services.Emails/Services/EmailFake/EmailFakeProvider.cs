using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox emailfake.com.
/// </summary>
public sealed partial class EmailFakeProvider : FixedDomainTemporaryEmailProvider<EmailFakeProviderOptions>
{
    /// <summary>
    /// Базовый URL emailfake.com.
    /// </summary>
    public const string DefaultApiUrl = "https://emailfake.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера emailfake.com с настройками по умолчанию.
    /// </summary>
    public EmailFakeProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new EmailFakeProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера emailfake.com с явными настройками.
    /// </summary>
    public EmailFakeProvider(EmailFakeProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("EmailFake", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new EmailFakeAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(EmailFakeAccount account, CancellationToken cancellationToken)
    {
        var domain = TemporaryEmailAddressUtility.ExtractDomain(account.Address);
        using var request = CreateRequest(HttpMethod.Get, $"{Uri.EscapeDataString(domain)}/{Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<EmailFakeMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new EmailFakeMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                WebUtility.HtmlDecode(match.Groups["from"].Value),
                address,
                WebUtility.HtmlDecode(match.Groups["subject"].Value),
                WebUtility.HtmlDecode(match.Groups["body"].Value));
        }
    }

    [GeneratedRegex("<article\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>\\s*<div\\s+class=\"body\">(?<body>.*?)</div>\\s*</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}