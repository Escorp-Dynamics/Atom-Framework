using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация для Hydra-compatible HTTP-провайдеров временной почты.
/// </summary>
public abstract class HydraTemporaryEmailProvider<TProvider, TOptions, TAccount, TMail> : DomainRefreshingTemporaryEmailProvider<TOptions>
    , IHydraTemporaryEmailProviderOperations
    where TProvider : HydraTemporaryEmailProvider<TProvider, TOptions, TAccount, TMail>
    where TOptions : HttpTemporaryEmailProviderOptions
    where TAccount : HttpTemporaryEmailAccount<TProvider>
    where TMail : Mail
{
    /// <summary>
    /// Инициализирует Hydra-compatible провайдер временной почты.
    /// </summary>
    protected HydraTemporaryEmailProvider(TOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger) { }

    /// <summary>
    /// Отображаемое имя upstream-провайдера для диагностических сообщений.
    /// </summary>
    protected abstract string ProviderDisplayName { get; }

    /// <inheritdoc/>
    protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
        var password = TemporaryEmailCredentialGenerator.CreatePassword(Options);

        var created = await CreateAccountAsync(address, password, cancellationToken).ConfigureAwait(false);
        var token = await AuthenticateAsync(address, password, cancellationToken).ConfigureAwait(false);
        var upstreamId = created.Id!;
        var resolvedAddress = string.IsNullOrWhiteSpace(created.Address) ? address : created.Address;

        return CreateHydraAccount(
            Self,
            upstreamId,
            TemporaryEmailMailMapper.CreateStableId(upstreamId),
            resolvedAddress,
            password,
            token,
            Logger);
    }

    internal async ValueTask<string> AuthenticateAsync(string address, string password, CancellationToken cancellationToken)
    {
        var requestPayload = new HydraTokenRequest
        {
            Address = address,
            Password = password,
        };

        using var request = CreateRequest(HttpMethod.Post, "token", acceptJsonLd: false);
        request.Content = CreateJsonContent(requestPayload, HydraTemporaryEmailJsonContext.Default.HydraTokenRequest, "application/json");

        var tokenResponse = await SendAndReadJsonAsync(
            request,
            HydraTemporaryEmailJsonContext.Default.HydraTokenResponse,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(tokenResponse?.Token))
        {
            throw new InvalidOperationException($"{ProviderDisplayName} не вернул access token для созданного аккаунта.");
        }

        return tokenResponse.Token;
    }

    internal async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(TAccount account, string token, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "messages", token, acceptJsonLd: true);
        var messageCollection = await SendAndReadJsonAsync(
            request,
            HydraTemporaryEmailJsonContext.Default.HydraMessageCollectionResponse,
            cancellationToken).ConfigureAwait(false);

        var summaries = messageCollection?.Members;
        if (summaries is not { Length: > 0 })
        {
            return [];
        }

        var mails = new List<Mail>(summaries.Length);
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(summary.Id))
            {
                continue;
            }

            var detail = await GetMessageAsync(summary.Id, token, cancellationToken).ConfigureAwait(false);
            mails.Add(detail is null ? CreateMail(account, summary) : CreateMail(account, detail));
        }

        return mails;
    }

    internal async ValueTask MarkAsReadAsync(string token, string upstreamMessageId, CancellationToken cancellationToken)
    {
        var requestPayload = new HydraMarkSeenRequest
        {
            Seen = true,
        };

        using var request = CreateAuthorizedRequest(new HttpMethod("PATCH"), $"messages/{upstreamMessageId}", token, acceptJsonLd: true);
        request.Content = CreateJsonContent(
            requestPayload,
            HydraTemporaryEmailJsonContext.Default.HydraMarkSeenRequest,
            "application/merge-patch+json");

        await SendAndEnsureSuccessAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask DeleteMailAsync(string token, string upstreamMessageId, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"messages/{upstreamMessageId}", token, acceptJsonLd: true);
        await SendAndEnsureSuccessAsync(request, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string> IHydraTemporaryEmailProviderOperations.AuthenticateAsync(string address, string password, CancellationToken cancellationToken)
        => AuthenticateAsync(address, password, cancellationToken);

    async ValueTask<IEnumerable<Mail>> IHydraTemporaryEmailProviderOperations.LoadMessagesAsync(TemporaryEmailAccount account, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return await LoadMessagesAsync((TAccount)account, token, cancellationToken).ConfigureAwait(false);
    }

    ValueTask IHydraTemporaryEmailProviderOperations.MarkAsReadAsync(string token, string upstreamMessageId, CancellationToken cancellationToken)
        => MarkAsReadAsync(token, upstreamMessageId, cancellationToken);

    ValueTask IHydraTemporaryEmailProviderOperations.DeleteMailAsync(string token, string upstreamMessageId, CancellationToken cancellationToken)
        => DeleteMailAsync(token, upstreamMessageId, cancellationToken);

    private TProvider Self => (TProvider)this;

    private async ValueTask<HydraAccountResponse> CreateAccountAsync(string address, string password, CancellationToken cancellationToken)
    {
        var requestPayload = new HydraCreateAccountRequest
        {
            Address = address,
            Password = password,
        };

        using var request = CreateRequest(HttpMethod.Post, "accounts", acceptJsonLd: true);
        request.Content = CreateJsonContent(requestPayload, HydraTemporaryEmailJsonContext.Default.HydraCreateAccountRequest, "application/ld+json");

        var created = await SendAndReadJsonAsync(
            request,
            HydraTemporaryEmailJsonContext.Default.HydraAccountResponse,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(created?.Id))
        {
            throw new InvalidOperationException($"{ProviderDisplayName} вернул аккаунт без upstream id.");
        }

        return created;
    }

    /// <inheritdoc/>
    protected override async ValueTask<string[]> LoadAvailableDomainsCoreAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "domains", acceptJsonLd: true);
        var payload = await SendAndReadJsonAsync(
            request,
            HydraTemporaryEmailJsonContext.Default.HydraDomainCollectionResponse,
            cancellationToken).ConfigureAwait(false);

        return payload?.Members?
            .Where(static item => item.IsActive && !item.IsPrivate && !string.IsNullOrWhiteSpace(item.Domain))
            .Select(static item => item.Domain!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    /// <inheritdoc/>
    protected override string CreateNoDomainsMessage()
        => $"{ProviderDisplayName} не вернул ни одного активного публичного домена.";

    /// <inheritdoc/>
    protected override string CreateUnsupportedDomainMessage(string requestedDomain)
        => $"{ProviderDisplayName} не поддерживает домен '{requestedDomain}'.";

    private async ValueTask<HydraMessageDetailResponse?> GetMessageAsync(string upstreamMessageId, string token, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"messages/{upstreamMessageId}", token, acceptJsonLd: true);
        return await SendAndReadJsonAsync(
            request,
            HydraTemporaryEmailJsonContext.Default.HydraMessageDetailResponse,
            cancellationToken).ConfigureAwait(false);
    }

    private TMail CreateMail(TAccount account, HydraMessageSummaryResponse summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary.Id);

        return TemporaryEmailMailMapper.Map(
            summary.Id,
            summary.From?.Address,
            summary.From?.Name,
            summary.To,
            static recipient => recipient.Address,
            static recipient => recipient.Name,
            summary.Subject,
            summary.Intro,
            summary.Seen,
            (upstreamId, id, from, to, subject, body, isRead) => CreateHydraMail(account, upstreamId, id, from, to, subject, body, isRead));
    }

    private TMail CreateMail(TAccount account, HydraMessageDetailResponse detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.Id);

        var body = !string.IsNullOrWhiteSpace(detail.Text)
            ? detail.Text
            : detail.Html?.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
                ?? detail.Intro
                ?? string.Empty;

        return TemporaryEmailMailMapper.Map(
            detail.Id,
            detail.From?.Address,
            detail.From?.Name,
            detail.To,
            static recipient => recipient.Address,
            static recipient => recipient.Name,
            detail.Subject,
            body,
            detail.Seen,
            (upstreamId, id, from, to, subject, resolvedBody, isRead) => CreateHydraMail(account, upstreamId, id, from, to, subject, resolvedBody, isRead));
    }

    /// <summary>
    /// Создаёт concrete account для текущего провайдера.
    /// </summary>
    protected abstract TAccount CreateHydraAccount(
        TProvider provider,
        string upstreamAccountId,
        Guid id,
        string address,
        string password,
        string token,
        ILogger? logger);

    /// <summary>
    /// Создаёт concrete mail для текущего провайдера.
    /// </summary>
    protected abstract TMail CreateHydraMail(
        TAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead);
}