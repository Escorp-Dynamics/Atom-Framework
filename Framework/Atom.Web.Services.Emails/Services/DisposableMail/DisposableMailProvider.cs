using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox DisposableMail.
/// </summary>
public sealed partial class DisposableMailProvider : FixedDomainTemporaryEmailProvider<DisposableMailProviderOptions>
{
    /// <summary>
    /// Базовый URL DisposableMail.
    /// </summary>
    public const string DefaultApiUrl = "https://www.disposablemail.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера DisposableMail с настройками по умолчанию.
    /// </summary>
    public DisposableMailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new DisposableMailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера DisposableMail с явными настройками.
    /// </summary>
    public DisposableMailProvider(DisposableMailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("DisposableMail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new DisposableMailAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(DisposableMailAccount account, CancellationToken cancellationToken)
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

    private static IEnumerable<DisposableMailMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new DisposableMailMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                WebUtility.HtmlDecode(match.Groups["from"].Value),
                address,
                WebUtility.HtmlDecode(match.Groups["subject"].Value),
                WebUtility.HtmlDecode(match.Groups["body"].Value));
        }
    }

    [GeneratedRegex("<tr\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>.*?<td\\s+class=\"body\">(?<body>.*?)</td>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}