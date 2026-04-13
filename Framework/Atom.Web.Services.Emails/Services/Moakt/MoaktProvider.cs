using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox Moakt.
/// </summary>
public sealed partial class MoaktProvider : FixedDomainTemporaryEmailProvider<MoaktProviderOptions>
{
    /// <summary>
    /// Базовый URL Moakt.
    /// </summary>
    public const string DefaultApiUrl = "https://moakt.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Moakt с настройками по умолчанию.
    /// </summary>
    public MoaktProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MoaktProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Moakt с явными настройками.
    /// </summary>
    public MoaktProvider(MoaktProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Moakt", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MoaktAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MoaktAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"ru/inbox/{Uri.EscapeDataString(account.Address)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<MoaktMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new MoaktMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                WebUtility.HtmlDecode(match.Groups["from"].Value),
                address,
                WebUtility.HtmlDecode(match.Groups["subject"].Value),
                WebUtility.HtmlDecode(match.Groups["body"].Value));
        }
    }

    [GeneratedRegex("<section\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>\\s*<div\\s+class=\"body\">(?<body>.*?)</div>\\s*</section>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}