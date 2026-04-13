using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе HTML inbox Yopmail.
/// </summary>
public sealed partial class YopmailProvider : FixedDomainTemporaryEmailProvider<YopmailProviderOptions>
{
    /// <summary>
    /// Базовый URL Yopmail.
    /// </summary>
    public const string DefaultApiUrl = "https://yopmail.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Yopmail с настройками по умолчанию.
    /// </summary>
    public YopmailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new YopmailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Yopmail с явными настройками.
    /// </summary>
    public YopmailProvider(YopmailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Yopmail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new YopmailAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(YopmailAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"en/inbox?login={Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<YopmailMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new YopmailMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{id}"),
                WebUtility.HtmlDecode(match.Groups["from"].Value),
                address,
                WebUtility.HtmlDecode(match.Groups["subject"].Value),
                WebUtility.HtmlDecode(match.Groups["body"].Value));
        }
    }

    [GeneratedRegex("<li\\s+data-id=\"(?<id>[^\"]+)\"\\s+data-from=\"(?<from>[^\"]*)\"\\s+data-subject=\"(?<subject>[^\"]*)\"[^>]*>\\s*<p\\s+class=\"body\">(?<body>.*?)</p>\\s*</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MessagePattern();
}