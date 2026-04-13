using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация для провайдеров с фиксированным локальным списком доменов.
/// </summary>
public abstract class FixedDomainTemporaryEmailProvider<TOptions> : DomainRefreshingTemporaryEmailProvider<TOptions>
    where TOptions : FixedDomainTemporaryEmailProviderOptions
{
    private readonly string providerDisplayName;

    /// <summary>
    /// Инициализирует fixed-domain провайдер.
    /// </summary>
    protected FixedDomainTemporaryEmailProvider(string providerDisplayName, TOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options, httpClient, logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);
        this.providerDisplayName = providerDisplayName;
    }

    /// <inheritdoc/>
    protected override ValueTask<string[]> LoadAvailableDomainsCoreAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(
            Options.SupportedDomains
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
                .ToArray());

    /// <inheritdoc/>
    protected override string CreateNoDomainsMessage()
        => $"{providerDisplayName} не вернул ни одного доступного домена.";

    /// <inheritdoc/>
    protected override string CreateUnsupportedDomainMessage(string requestedDomain)
        => $"{providerDisplayName} не поддерживает домен '{requestedDomain}'.";
}