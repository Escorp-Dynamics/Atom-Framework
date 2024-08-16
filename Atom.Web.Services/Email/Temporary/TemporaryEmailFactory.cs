
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Фабрика сервисов временных почт.
/// </summary>
public class TemporaryEmailFactory : WebServiceFactory<TemporaryEmailFactory, ITemporaryEmailService>, ITemporaryEmailFactory
{
    private readonly ConcurrentQueue<KeyValuePair<string, string>> domains = [];
    private readonly SemaphoreSlim locker = new(1, 1);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) locker.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> GetNextDomainAsync(CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (domains.IsEmpty)
        {
            foreach (var service in Services)
                await foreach (var d in service.GetDomainsAsync(cancellationToken))
                {
                    var kv = new KeyValuePair<string, string>(service.GetType().FullName!, d);
                    if (!domains.Contains(kv)) domains.Enqueue(kv);
                }
        }

        _ = domains.TryDequeue(out var domain);
        locker.Release();

        return domain.Value;
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetNextDomainAsync() => GetNextDomainAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask<TemporaryEmailAccount?> CreateAccountAsync([NotNull] string login, string password, CancellationToken cancellationToken)
    {
        if (!login.Contains('@')) throw new AggregateException("В логине должно содержаться имя домена");

        var domain = login.Split('@', 2)[1];
        var service = Services.FirstOrDefault(x => x.Domains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase))) ?? throw new AggregateException("Домен не поддерживается ни одним из доступных сервисов");

        return await service.CreateAccountAsync(login, password, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<TemporaryEmailAccount?> CreateAccountAsync(string login, string password) => CreateAccountAsync(login, password, CancellationToken.None);
}