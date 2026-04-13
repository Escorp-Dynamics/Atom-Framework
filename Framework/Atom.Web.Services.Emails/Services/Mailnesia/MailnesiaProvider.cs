using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе XML mailbox feed Mailnesia.
/// </summary>
public sealed class MailnesiaProvider : FixedDomainTemporaryEmailProvider<MailnesiaProviderOptions>
{
    /// <summary>
    /// Базовый URL Mailnesia.
    /// </summary>
    public const string DefaultApiUrl = "https://mailnesia.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mailnesia с настройками по умолчанию.
    /// </summary>
    public MailnesiaProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MailnesiaProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Mailnesia с явными настройками.
    /// </summary>
    public MailnesiaProvider(MailnesiaProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Mailnesia", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MailnesiaAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MailnesiaAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"mailbox/{Uri.EscapeDataString(account.Login)}.xml", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var document = XDocument.Parse(xml, LoadOptions.None);
        return document.Root is null
            ? []
            : ParseMessages(document, account.Address);
    }

    private static MailnesiaMail[] ParseMessages(XDocument document, string address)
    {
        var entries = document.Descendants().Where(static node => node.Name.LocalName is "entry" or "item");
        var mails = new List<MailnesiaMail>();

        foreach (var entry in entries)
        {
            var upstreamId = GetChildValue(entry, "id");
            if (string.IsNullOrWhiteSpace(upstreamId))
            {
                upstreamId = GetChildValue(entry, "guid");
            }

            if (string.IsNullOrWhiteSpace(upstreamId))
            {
                continue;
            }

            var from = GetChildValue(entry, "from");
            if (string.IsNullOrWhiteSpace(from))
            {
                from = entry.Descendants().FirstOrDefault(static node => node.Name.LocalName == "author")?.Value;
            }

            var subject = GetChildValue(entry, "subject");
            if (string.IsNullOrWhiteSpace(subject))
            {
                subject = GetChildValue(entry, "title");
            }

            var body = GetChildValue(entry, "body");
            if (string.IsNullOrWhiteSpace(body))
            {
                body = GetChildValue(entry, "content");
            }

            mails.Add(new MailnesiaMail(
                upstreamId,
                TemporaryEmailMailMapper.CreateStableId($"{address}|{upstreamId}"),
                from ?? string.Empty,
                address,
                subject ?? string.Empty,
                body ?? string.Empty,
                isRead: false));
        }

        return [.. mails];
    }

    private static string? GetChildValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(node => node.Name.LocalName == localName)?.Value;
}