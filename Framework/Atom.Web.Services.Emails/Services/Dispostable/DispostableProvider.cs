using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Провайдер временной почты на базе plain-text mailbox feed Dispostable.
/// </summary>
public sealed class DispostableProvider : FixedDomainTemporaryEmailProvider<DispostableProviderOptions>
{
    /// <summary>
    /// Базовый URL API Dispostable.
    /// </summary>
    public const string DefaultApiUrl = "https://api.dispostable.com/";

    /// <summary>
    /// Создаёт новый экземпляр провайдера Dispostable с настройками по умолчанию.
    /// </summary>
    public DispostableProvider(HttpClient? httpClient = null, ILogger? logger = null)
        : this(new DispostableProviderOptions(), httpClient, logger) { }

    /// <summary>
    /// Создаёт новый экземпляр провайдера Dispostable с явными настройками.
    /// </summary>
    public DispostableProvider(DispostableProviderOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base("Dispostable", options, httpClient, logger) { }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        return new DispostableAccount(this, TemporaryEmailMailMapper.CreateStableId(address), address, Logger);
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(DispostableAccount account, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"mailbox/{Uri.EscapeDataString(account.Login)}.txt", acceptJsonLd: false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return ParseMessages(content, account).ToArray();
    }

    internal async ValueTask DeleteMailAsync(DispostableAccount account, string upstreamMessageId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"mailbox/{Uri.EscapeDataString(account.Login)}/{Uri.EscapeDataString(upstreamMessageId)}", acceptJsonLd: false);
        await SendAndEnsureSuccessAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<DispostableMail> ParseMessages(string content, DispostableAccount account)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var upstreamId = parts[0];
            var from = parts[1];
            var subject = parts[2];
            var body = parts[3];

            yield return new DispostableMail(
                account,
                upstreamId,
                TemporaryEmailMailMapper.CreateStableId($"{account.Address}|{upstreamId}"),
                from,
                account.Address,
                subject,
                body);
        }
    }
}