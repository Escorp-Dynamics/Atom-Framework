using System.Collections.Concurrent;
using Atom.Architect.Components;
using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Представляет фабрику временных почтовых аккаунтов.
/// </summary>
[ComponentOwner(typeof(ITemporaryEmailProvider))]
public partial class TemporaryEmailFactory : ITemporaryEmailFactory<ITemporaryEmailProvider, TemporaryEmailFactory>
{
    private readonly ConcurrentDictionary<ITemporaryEmailAccount, ITemporaryEmailProvider> leasedAccounts = [];

    private int disposeState;
    private int nextProviderIndex = -1;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public int Count => leasedAccounts.Count;

    /// <inheritdoc/>
    public IEnumerable<ITemporaryEmailProvider> Providers => TryGetAll<ITemporaryEmailProvider>(out var providers) ? providers : [];

    /// <inheritdoc/>
    public IEnumerable<string> AvailableDomains
        => Providers
            .SelectMany(static provider => provider.AvailableDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IEnumerable<ITemporaryEmailAccount> Accounts => leasedAccounts.Keys;

    /// <inheritdoc/>
    public TemporaryEmailProviderSelectionStrategy ProviderSelectionStrategy { get; set; } = TemporaryEmailProviderSelectionStrategy.RoundRobin;

    /// <inheritdoc/>
    public ValueTask<ITemporaryEmailAccount> GetAsync()
        => GetAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<ITemporaryEmailAccount> GetAsync(CancellationToken cancellationToken)
        => GetAsync(TemporaryEmailAccountCreateSettings.Empty, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ITemporaryEmailAccount> GetAsync(TemporaryEmailAccountCreateSettings request)
        => GetAsync(request, CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask<ITemporaryEmailAccount> GetAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        var candidates = GetOrderedProviders(request);
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("Фабрика не содержит провайдеров, способных создать временный почтовый аккаунт.");
        }

        Exception? lastError = null;
        foreach (var provider in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyLoggerFallback(provider);

            try
            {
                var account = await provider.CreateAccountAsync(request, cancellationToken).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(account);

                account.Provider ??= provider;
                account.Logger ??= provider.Logger ?? Logger;
                leasedAccounts[account] = provider;
                return account;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw new InvalidOperationException(
            "Ни один провайдер временной почты не смог создать аккаунт по текущему запросу.",
            lastError);
    }

    /// <inheritdoc/>
    public async ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(
        int count,
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var accounts = new List<ITemporaryEmailAccount>(count);
        for (var index = 0; index < count; index++)
        {
            accounts.Add(await GetAsync(request, cancellationToken).ConfigureAwait(false));
        }

        return accounts;
    }

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count)
        => GetAsync(count, TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count, CancellationToken cancellationToken)
        => GetAsync(count, TemporaryEmailAccountCreateSettings.Empty, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count, TemporaryEmailAccountCreateSettings request)
        => GetAsync(count, request, CancellationToken.None);

    /// <inheritdoc/>
    public void Return(ITemporaryEmailAccount item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!leasedAccounts.TryRemove(item, out _))
        {
            return;
        }

        item.Dispose();
    }

    /// <summary>
    /// Высвобождает ресурсы фабрики и выданных аккаунтов.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeState, value: 1) != 0)
        {
            return;
        }

        foreach (var account in leasedAccounts.Keys.ToArray())
        {
            Return(account);
        }

        if (TryGetAll<ITemporaryEmailProvider>(out var providers))
        {
            foreach (var provider in providers.ToArray())
            {
                if (ReferenceEquals(provider.Owner, this))
                {
                    provider.Detach();
                }

                provider.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private void ApplyLoggerFallback(ITemporaryEmailProvider provider)
    {
        if (provider.Logger is null)
        {
            provider.Logger = Logger;
        }
    }

    private ITemporaryEmailProvider[] GetOrderedProviders(TemporaryEmailAccountCreateSettings request)
    {
        var candidates = Providers.Where(provider => provider.CanCreate(request)).ToArray();
        if (candidates.Length <= 1)
        {
            return candidates;
        }

        if (ProviderSelectionStrategy is TemporaryEmailProviderSelectionStrategy.Random)
        {
            for (var index = candidates.Length - 1; index > 0; index--)
            {
                var swapIndex = Random.Shared.Next(index + 1);
                (candidates[index], candidates[swapIndex]) = (candidates[swapIndex], candidates[index]);
            }

            return candidates;
        }

        var start = (Interlocked.Increment(ref nextProviderIndex) & int.MaxValue) % candidates.Length;
        if (start == 0)
        {
            return candidates;
        }

        var ordered = new ITemporaryEmailProvider[candidates.Length];
        Array.Copy(candidates, sourceIndex: start, ordered, destinationIndex: 0, length: candidates.Length - start);
        Array.Copy(candidates, sourceIndex: 0, ordered, destinationIndex: candidates.Length - start, length: start);
        return ordered;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, GetType().Name);
}