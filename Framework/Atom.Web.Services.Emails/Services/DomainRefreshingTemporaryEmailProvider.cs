using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация временного почтового HTTP-провайдера с обновляемым списком доменов.
/// </summary>
public abstract class DomainRefreshingTemporaryEmailProvider<TOptions> : HttpTemporaryEmailProvider<TOptions>
    where TOptions : HttpTemporaryEmailProviderOptions
{
    private readonly SemaphoreSlim domainRefreshGate = new(initialCount: 1, maxCount: 1);

    private string[] availableDomains = [];

    /// <summary>
    /// Инициализирует провайдер с обновляемым списком доменов.
    /// </summary>
    protected DomainRefreshingTemporaryEmailProvider(TOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger) { }

    /// <inheritdoc/>
    public override IEnumerable<string> AvailableDomains => availableDomains;

    /// <inheritdoc/>
    public override bool CanCreate(TemporaryEmailAccountCreateSettings request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Domain))
        {
            return true;
        }

        return availableDomains.Length == 0 || availableDomains.Contains(request.Domain, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Обновляет snapshot доступных доменов из upstream API.
    /// </summary>
    public async ValueTask<IEnumerable<string>> RefreshAvailableDomainsAsync(CancellationToken cancellationToken = default)
        => await RefreshDomainCacheAsync(domainRefreshGate, LoadAvailableDomainsCoreAsync, domains => availableDomains = domains, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Вычисляет итоговый email address по запросу и актуальному списку доменов.
    /// </summary>
    protected async ValueTask<string> ResolveAddressAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var domains = Options.DomainRefreshMode is TemporaryEmailDomainRefreshMode.Always || availableDomains.Length == 0
            ? [.. await RefreshAvailableDomainsAsync(cancellationToken).ConfigureAwait(false)]
            : availableDomains;

        if (domains.Length == 0)
        {
            throw new InvalidOperationException(CreateNoDomainsMessage());
        }

        var selectedDomain = SelectDomain(domains, request.Domain);
        var alias = TemporaryEmailCredentialGenerator.NormalizeAlias(request.Alias, Options);
        return TemporaryEmailAddressUtility.Compose(alias, selectedDomain);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            domainRefreshGate.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Загружает актуальный набор доменов из upstream API.
    /// </summary>
    protected abstract ValueTask<string[]> LoadAvailableDomainsCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Создаёт сообщение об отсутствии доступных доменов.
    /// </summary>
    protected abstract string CreateNoDomainsMessage();

    /// <summary>
    /// Создаёт сообщение о неподдерживаемом домене.
    /// </summary>
    protected abstract string CreateUnsupportedDomainMessage(string requestedDomain);

    private string SelectDomain(IEnumerable<string> domains, string? requestedDomain)
    {
        if (string.IsNullOrWhiteSpace(requestedDomain))
        {
            return domains.First();
        }

        var matchedDomain = domains.FirstOrDefault(domain => string.Equals(domain, requestedDomain, StringComparison.OrdinalIgnoreCase));
        return matchedDomain ?? throw new InvalidOperationException(CreateUnsupportedDomainMessage(requestedDomain));
    }
}