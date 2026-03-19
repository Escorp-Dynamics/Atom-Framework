using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Канонизирует IP-литералы и пытается разрешать hostname-значения через DNS с кэшем.
/// </summary>
public sealed class DnsAwareProxyDedupKeyResolver : IProxyDedupKeyResolver
{
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<CacheEntry>> inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> addressResolver;

    /// <summary>
    /// Создаёт resolver с системным DNS resolver по умолчанию.
    /// </summary>
    public DnsAwareProxyDedupKeyResolver()
        : this(static (host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    /// <summary>
    /// Создаёт resolver с пользовательским async address resolver.
    /// </summary>
    /// <param name="addressResolver">Функция разрешения hostname в IP-адреса.</param>
    public DnsAwareProxyDedupKeyResolver(Func<string, CancellationToken, Task<IPAddress[]>> addressResolver)
    {
        ArgumentNullException.ThrowIfNull(addressResolver);
        this.addressResolver = addressResolver;
    }

    /// <summary>
    /// Время жизни кэша DNS-разрешения.
    /// </summary>
    public TimeSpan EntryLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Максимальное время ожидания DNS-разрешения.
    /// </summary>
    public TimeSpan ResolutionTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Разрешает использовать set-based dedup key для hostname, вернувших несколько адресов.
    /// По умолчанию multi-address hostnames остаются на raw host key, чтобы избежать нестабильного dedup при rotating DNS.
    /// </summary>
    public bool UseMultiAddressKeys { get; set; }

    /// <summary>
    /// Мягкий лимит числа кэшированных hostname-записей до запуска opportunistic cleanup.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 4096;

    /// <inheritdoc/>
    public async ValueTask<string> GetKeyAsync(ServiceProxy proxy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var host = proxy.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var candidate = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        if (IPAddress.TryParse(candidate, out var address))
        {
            return address.ToString();
        }

        var now = DateTime.UtcNow;
        if (cache.Count > MaxCacheEntries)
        {
            CleanupExpiredEntries(now);
        }

        if (cache.TryGetValue(host, out var cached) && cached.Cacheable && cached.ExpiresUtc > now)
        {
            return cached.Key;
        }

        var resolutionTask = GetOrCreateInflightTask(host);
        var resolved = await resolutionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (resolved.Cacheable)
        {
            cache[host] = resolved;
        }
        else
        {
            cache.TryRemove(host, out _);
        }

        return resolved.Key;
    }

    private Task<CacheEntry> GetOrCreateInflightTask(string host)
    {
        while (true)
        {
            if (inflight.TryGetValue(host, out var existingTask))
            {
                return existingTask;
            }

            var completion = new TaskCompletionSource<CacheEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!inflight.TryAdd(host, completion.Task))
            {
                continue;
            }

            CompleteInflightTaskAsync(host, completion);
            return completion.Task;
        }
    }

    private async void CompleteInflightTaskAsync(string host, TaskCompletionSource<CacheEntry> completion)
    {
        try
        {
            var entry = await ResolveEntryAsync(host).ConfigureAwait(false);
            completion.SetResult(entry);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Task<CacheEntry>>>)inflight).Remove(new KeyValuePair<string, Task<CacheEntry>>(host, completion.Task));
        }
    }

    private async Task<CacheEntry> ResolveEntryAsync(string host)
    {
        using var timeoutSource = ResolutionTimeout > TimeSpan.Zero
            ? new CancellationTokenSource(ResolutionTimeout)
            : new CancellationTokenSource();

        try
        {
            var addresses = await addressResolver(host, timeoutSource.Token).ConfigureAwait(false);
            if (addresses is null || addresses.Length == 0)
            {
                return CreateCacheEntry(host, cacheable: false);
            }

            var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < addresses.Length; index++)
            {
                uniqueKeys.Add(addresses[index].ToString());
            }

            string[] keys = [.. uniqueKeys];
            Array.Sort(keys, StringComparer.Ordinal);
            if (keys.Length == 1)
            {
                return CreateCacheEntry(keys[0]);
            }

            if (!UseMultiAddressKeys)
            {
                return CreateCacheEntry(host);
            }

            return CreateCacheEntry(string.Join('|', keys));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return CreateCacheEntry(host, cacheable: false);
        }
    }

    private CacheEntry CreateCacheEntry(string key, bool cacheable = true)
    {
        var shouldCache = cacheable && EntryLifetime > TimeSpan.Zero;
        return new CacheEntry(key, shouldCache ? DateTime.UtcNow + EntryLifetime : DateTime.MinValue, shouldCache);
    }

    private void CleanupExpiredEntries(DateTime now)
    {
        foreach (var entry in cache)
        {
            if (!entry.Value.Cacheable || entry.Value.ExpiresUtc <= now)
            {
                cache.TryRemove(entry.Key, out _);
            }
        }
    }

    private readonly record struct CacheEntry(string Key, DateTime ExpiresUtc, bool Cacheable);
}