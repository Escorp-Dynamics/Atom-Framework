using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration inbox API Maildrop.
/// </summary>
public sealed class MaildropProvider : FixedDomainTemporaryEmailProvider<MaildropProviderOptions>
{
    /// <summary>
    /// Базовый URL Maildrop.
    /// </summary>
    public const string DefaultApiUrl = "https://maildrop.cc/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Maildrop с настройками по умолчанию.
    /// </summary>
    public MaildropProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MaildropProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Maildrop с явными настройками.
    /// </summary>
    public MaildropProvider(MaildropProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Maildrop", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MaildropAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MaildropAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/inbox/{Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
        List<MaildropMail> mails = [];
        using (request)
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return [];
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var from = item.TryGetProperty("from", out var fromProperty) ? fromProperty.GetString() : string.Empty;
                var subject = item.TryGetProperty("subject", out var subjectProperty) ? subjectProperty.GetString() : string.Empty;
                var text = item.TryGetProperty("text", out var textProperty) ? textProperty.GetString() : string.Empty;

                mails.Add(new MaildropMail(
                    id,
                    TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{id}"),
                    from ?? string.Empty,
                    account.Address,
                    subject ?? string.Empty,
                    text ?? string.Empty));
            }
        }

        if (mails.Count == 0)
        {
            return [];
        }

        return mails;
    }
}