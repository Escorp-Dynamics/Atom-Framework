using System.Net;
using System.Net.Sockets;

namespace Atom.Net.Tests.Network;

/// <summary>
/// Проверяет кэш разрешений DNS.
/// </summary>
/// <remarks>
/// Ни один тест не обращается в настоящую сеть: резолвер передаётся параметром именно для этого.
/// Проверяется поведение, ради которого кэш и заводится, — что повторный вопрос не уходит наружу,
/// что протухшая запись уходит, что отказ не запоминается и что кэш не растёт без предела.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class DnsCacheTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public async Task SecondLookupOfTheSameHostComesFromCache()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        var first = await cache.ResolveAsync("cached.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        var second = await cache.ResolveAsync("cached.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(new[] { IPAddress.Loopback }));
            Assert.That(second, Is.EqualTo(new[] { IPAddress.Loopback }));
            Assert.That(cache.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HostNameCaseDoesNotCreateSecondEntry()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        await cache.ResolveAsync("Mixed.Case.Test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        await cache.ResolveAsync("mixed.case.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task EntryExpiresAfterTimeToLive()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromMilliseconds(40), 16);

        await cache.ResolveAsync("expiring.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(250).ConfigureAwait(false);
        await cache.ResolveAsync("expiring.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);

        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task ZeroTimeToLiveDisablesCaching()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.Zero, 16);

        await cache.ResolveAsync("disabled.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        await cache.ResolveAsync("disabled.test", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(cache.Count, Is.Zero);
        });
    }

    [Test]
    public async Task LiteralAddressBypassesCacheEntirely()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        await cache.ResolveAsync("127.0.0.1", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        await cache.ResolveAsync("127.0.0.1", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);
        await cache.ResolveAsync("::1", Counting(() => calls++), CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(cache.Count, Is.Zero);
        });
    }

    [Test]
    public void FailedLookupIsNotRemembered()
    {
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        ValueTask<IPAddress[]> Resolve(string host, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) is 1) throw new SocketException((int)SocketError.HostNotFound);

            return ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]);
        }

        Assert.ThrowsAsync<SocketException>(async () => await cache.ResolveAsync("failing.test", Resolve, CancellationToken.None).ConfigureAwait(false));
        Assert.That(cache.Count, Is.Zero);

        var addresses = cache.ResolveAsync("failing.test", Resolve, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(addresses, Is.EqualTo(new[] { IPAddress.Loopback }));
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [Test]
    public void EmptyAnswerIsTreatedAsFailure()
    {
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        Assert.ThrowsAsync<SocketException>(async () => await cache.ResolveAsync("empty.test", static (_, _) => ValueTask.FromResult<IPAddress[]>([]), CancellationToken.None).ConfigureAwait(false));
        Assert.That(cache.Count, Is.Zero);
    }

    [Test]
    public async Task CacheNeverGrowsPastCapacity()
    {
        const int capacity = 4;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), capacity);

        for (var i = 0; i < 64; i++)
            await cache.ResolveAsync("host-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".test", static (_, _) => ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]), CancellationToken.None).ConfigureAwait(false);

        Assert.That(cache.Count, Is.LessThanOrEqualTo(capacity));
    }

    [Test]
    public async Task ConcurrentLookupsShareOneResolution()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        async ValueTask<IPAddress[]> Resolve(string host, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await gate.Task.ConfigureAwait(false);

            return [IPAddress.Loopback];
        }

        var pending = new Task<IPAddress[]>[8];
        for (var i = 0; i < pending.Length; i++) pending[i] = cache.ResolveAsync("stampede.test", Resolve, CancellationToken.None).AsTask();

        gate.SetResult();

        var results = await Task.WhenAll(pending).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results, Is.All.EqualTo(new[] { IPAddress.Loopback }));
        });
    }

    [Test]
    public async Task CallerGetsItsOwnArray()
    {
        var cache = new DnsCache(TimeSpan.FromSeconds(30), 16);

        var first = await cache.ResolveAsync("copy.test", static (_, _) => ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]), CancellationToken.None).ConfigureAwait(false);
        first[0] = IPAddress.Any;

        var second = await cache.ResolveAsync("copy.test", static (_, _) => ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]), CancellationToken.None).ConfigureAwait(false);

        Assert.That(second[0], Is.EqualTo(IPAddress.Loopback));
    }

    [Test]
    public void RejectsMeaninglessSettings()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DnsCache(TimeSpan.FromSeconds(-1), 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DnsCache(TimeSpan.FromSeconds(30), 0));
        });
    }

    /// <summary>Резолвер-заглушка, считающий обращения.</summary>
    private static Func<string, CancellationToken, ValueTask<IPAddress[]>> Counting(Action onCall) => (_, _) =>
    {
        onCall();

        return ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]);
    };
}
