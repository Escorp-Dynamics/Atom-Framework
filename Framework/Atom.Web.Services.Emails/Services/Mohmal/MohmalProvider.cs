using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе HTML inbox Mohmal.
/// </summary>
public sealed partial class MohmalProvider : FixedDomainTemporaryEmailProvider<MohmalProviderOptions>
{
    /// <summary>
    /// Базовый URL Mohmal.
    /// </summary>
    public const string DefaultApiUrl = "https://mohmal.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mohmal с настройками по умолчанию.
    /// </summary>
    public MohmalProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MohmalProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mohmal с явными настройками.
    /// </summary>
    public MohmalProvider(MohmalProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Mohmal", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MohmalAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MohmalAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"en/inbox/{Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<MohmalMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var from = WebUtility.HtmlDecode(match.Groups["from"].Value);
            var subject = WebUtility.HtmlDecode(match.Groups["subject"].Value);
            var body = WebUtility.HtmlDecode(match.Groups["body"].Value);

            yield return new MohmalMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                from,
                address,
                subject,
                body);
        }
    }

    [GeneratedRegex("<article\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>\\s*<div\\s+class=\"body\">(?<body>.*?)</div>\\s*</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}