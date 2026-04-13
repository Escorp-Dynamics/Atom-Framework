using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration HTML inbox Fake Mail Generator.
/// </summary>
public sealed partial class FakeMailGeneratorProvider : FixedDomainTemporaryEmailProvider<FakeMailGeneratorProviderOptions>
{
    /// <summary>
    /// Базовый URL Fake Mail Generator.
    /// </summary>
    public const string DefaultApiUrl = "https://www.fakemailgenerator.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Fake Mail Generator с настройками по умолчанию.
    /// </summary>
    public FakeMailGeneratorProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new FakeMailGeneratorProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Fake Mail Generator с явными настройками.
    /// </summary>
    public FakeMailGeneratorProvider(FakeMailGeneratorProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("FakeMailGenerator", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new FakeMailGeneratorAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(FakeMailGeneratorAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"inbox?email={Uri.EscapeDataString(account.Address)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return ParseMessages(html, account.Address).ToArray();
    }

    private static IEnumerable<FakeMailGeneratorMail> ParseMessages(string html, string address)
    {
        foreach (Match match in MessagePattern().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new FakeMailGeneratorMail(
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