using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration inbox API Mailinator.
/// </summary>
public sealed class MailinatorPublicProvider : FixedDomainTemporaryEmailProvider<MailinatorPublicProviderOptions>
{
    /// <summary>
    /// Базовый URL публичного Mailinator.
    /// </summary>
    public const string DefaultApiUrl = "https://www.mailinator.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера MailinatorPublic с настройками по умолчанию.
    /// </summary>
    public MailinatorPublicProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new MailinatorPublicProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера MailinatorPublic с явными настройками.
    /// </summary>
    public MailinatorPublicProvider(MailinatorPublicProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("MailinatorPublic", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new MailinatorPublicAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(MailinatorPublicAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/v1/inbox?to={Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var mails = new List<Mail>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var from = item.TryGetProperty("from", out var fromProperty) ? fromProperty.GetString() : string.Empty;
            var subject = item.TryGetProperty("subject", out var subjectProperty) ? subjectProperty.GetString() : string.Empty;
            var text = item.TryGetProperty("body", out var textProperty) ? textProperty.GetString() : string.Empty;

            mails.Add(new MailinatorPublicMail(
                id,
                TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{id}"),
                from ?? string.Empty,
                account.Address,
                subject ?? string.Empty,
                text ?? string.Empty));
        }

        return mails;
    }
}