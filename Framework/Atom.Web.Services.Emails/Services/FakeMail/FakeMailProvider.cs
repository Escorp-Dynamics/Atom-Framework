using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе публичного no-registration inbox API FakeMail.
/// </summary>
public sealed class FakeMailProvider : FixedDomainTemporaryEmailProvider<FakeMailProviderOptions>
{
    /// <summary>
    /// Базовый URL FakeMail.
    /// </summary>
    public const string DefaultApiUrl = "https://www.fakemail.net/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера FakeMail с настройками по умолчанию.
    /// </summary>
    public FakeMailProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new FakeMailProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера FakeMail с явными настройками.
    /// </summary>
    public FakeMailProvider(FakeMailProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("FakeMail", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new FakeMailAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(FakeMailAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/inbox/{Uri.EscapeDataString(account.Login)}", acceptJsonLd: false);
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

            mails.Add(new FakeMailMail(
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