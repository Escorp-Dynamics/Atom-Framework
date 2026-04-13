using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox Tempail.
/// </summary>
public sealed partial class TempailProvider : FixedDomainTemporaryEmailProvider<TempailProviderOptions>
{
    /// <summary>
    /// Базовый URL Tempail.
    /// </summary>
    public const string DefaultApiUrl = "https://tempail.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Tempail с настройками по умолчанию.
    /// </summary>
    public TempailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new TempailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Tempail с явными настройками.
    /// </summary>
    public TempailProvider(TempailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Tempail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new TempailAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(TempailAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"en/inbox/{Uri.EscapeDataString(account.Address)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<TempailMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new TempailMail(
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