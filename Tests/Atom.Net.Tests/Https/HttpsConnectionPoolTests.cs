using System.Net;
using System.Net.Http;
using System.Reflection;
using Atom.Net.Https;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Profiles;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет выбор версии HTTP и учёт общего мультиплексируемого соединения в пуле.
/// </summary>
/// <remarks>
/// Пул держит два несовместимых вида соединений. Эксклюзивное HTTP/1.1 занимает ровно один запрос
/// и возвращается в очередь; общее HTTP/2 обслуживает много запросов сразу и не принадлежит
/// никому. Ошибка в этом различении не выглядит ошибкой: соединение просто закрывается посреди
/// чужих запросов либо, наоборот, не закрывается никогда.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HttpsConnectionPoolTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public void ProfileRaisesVersionCeilingForOrdinaryRequests()
    {
        // Обычный запрос приходит с версией 1.1 по умолчанию. Профиль браузера обязан поднять
        // планку: Chrome не обращается по HTTP/1.1 туда, где сервер предлагает h2.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.org/");

        var resolved = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindowsTls13());

        Assert.That(resolved, Is.EqualTo(HttpVersion.Version20));
    }

    [Test]
    public void ExactVersionPolicyOverridesProfile()
    {
        // Прямое требование вызывающей стороны перекрывает профиль в обе стороны.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.org/")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        var resolved = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindowsTls13());

        Assert.That(resolved, Is.EqualTo(HttpVersion.Version11));
    }

    [Test]
    public void RequestMayRaiseVersionAboveProfile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.org/") { Version = HttpVersion.Version30 };

        var resolved = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindows());

        Assert.That(resolved, Is.EqualTo(HttpVersion.Version30));
    }

    [Test]
    public void WithoutProfileRequestDecidesVersion()
    {
        // Поведение обычного клиента без профиля не меняется.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.org/");

        var resolved = InvokeResolvePreferredVersion(request, profile: null);

        Assert.That(resolved, Is.EqualTo(HttpVersion.Version11));
    }

    [Test]
    public void PoolPublishesSharedConnectionOnlyOnce()
    {
        var poolState = CreatePoolState(maxConnections: 6);
        var first = new FakeConnection();
        var second = new FakeConnection();

        var published = InvokePool<bool>(poolState, "TryPublishMultiplexed", first);
        var rejected = InvokePool<bool>(poolState, "TryPublishMultiplexed", second);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(rejected, Is.False, "второе соединение вытеснило бы первое из-под идущих по нему запросов");
            Assert.That(GetProperty<object?>(poolState, "Multiplexed"), Is.SameAs(first));
        });
    }

    [Test]
    public void PoolClearsOnlyTheConnectionItWasAskedAbout()
    {
        var poolState = CreatePoolState(maxConnections: 6);
        var current = new FakeConnection();
        var stale = new FakeConnection();

        _ = InvokePool<bool>(poolState, "TryPublishMultiplexed", current);

        var wrongCleared = InvokePool<bool>(poolState, "TryClearMultiplexed", stale);
        var rightCleared = InvokePool<bool>(poolState, "TryClearMultiplexed", current);

        Assert.Multiple(() =>
        {
            Assert.That(wrongCleared, Is.False, "снятие по чужой ссылке осиротило бы действующее соединение");
            Assert.That(rightCleared, Is.True);
            Assert.That(GetProperty<object?>(poolState, "Multiplexed"), Is.Null);
        });
    }

    [Test]
    public void RetiredConnectionSurvivesSweepWhileItsStreamsAreAlive()
    {
        // Снятое с должности соединение закрывать сразу нельзя: по нему идут чужие запросы.
        var poolState = CreatePoolState(maxConnections: 6);
        var busy = new FakeConnection { ActiveStreamCount = 3 };

        InvokePool(poolState, "Retire", busy);
        InvokePool(poolState, "SweepRetired");

        Assert.That(busy.DisposeCount, Is.Zero);

        busy.ActiveStreamCount = 0;
        InvokePool(poolState, "SweepRetired");

        Assert.That(busy.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void SweepDoesNotLoopOverPermanentlyBusyConnections()
    {
        // Проход по очереди фиксированной длины: иначе уборка крутилась бы, пока идёт долгий ответ.
        var poolState = CreatePoolState(maxConnections: 6);

        for (var index = 0; index < 4; index++)
            InvokePool(poolState, "Retire", new FakeConnection { ActiveStreamCount = 1 });

        Assert.DoesNotThrow(() => InvokePool(poolState, "SweepRetired"));
    }

    private static Version InvokeResolvePreferredVersion(HttpRequestMessage request, BrowserProfile? profile, Uri? upstreamProxy = null)
    {
        var method = typeof(HttpsClientHandler).GetMethod("ResolvePreferredVersion", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolvePreferredVersion not found.");

        return (Version)(method.Invoke(obj: null, [request, profile, upstreamProxy])
            ?? throw new InvalidOperationException("ResolvePreferredVersion invocation returned null."));
    }

    [Test]
    public void ProxyRulesOutHttp3BecauseQuicCannotBeTunnelled()
    {
        // HTTP/3 идёт поверх UDP, а обычный прокси умеет только туннель CONNECT поверх TCP.
        // Оставить HTTP/3 при заданном прокси значит уйти мимо него с настоящего адреса — молча
        // и с полностью рабочим ответом. Chrome в этом случае QUIC тоже отключает.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/") { Version = HttpVersion.Version30 };

        var direct = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindowsTls13());
        var throughProxy = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(), new Uri("http://proxy.test:8080"));

        Assert.Multiple(() =>
        {
            Assert.That(direct, Is.EqualTo(HttpVersion.Version30));
            Assert.That(throughProxy, Is.EqualTo(HttpVersion.Version20));
        });
    }

    [Test]
    public void ProxyDoesNotDisturbVersionsThatItCanCarry()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/") { Version = HttpVersion.Version20 };

        var throughProxy = InvokeResolvePreferredVersion(request, BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(), new Uri("http://proxy.test:8080"));

        Assert.That(throughProxy, Is.EqualTo(HttpVersion.Version20));
    }

    private static object CreatePoolState(int maxConnections)
    {
        var type = typeof(HttpsClientHandler).GetNestedType("ConnectionPoolState", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ConnectionPoolState not found.");

        return Activator.CreateInstance(type, maxConnections)
            ?? throw new InvalidOperationException("ConnectionPoolState instantiation returned null.");
    }

    private static void InvokePool(object poolState, string name, params object[] arguments)
        => _ = Invoke(poolState, name, arguments);

    private static T InvokePool<T>(object poolState, string name, params object[] arguments)
        => (T)(Invoke(poolState, name, arguments) ?? throw new InvalidOperationException($"{name} вернул null."));

    private static object? Invoke(object poolState, string name, params object[] arguments)
    {
        var method = poolState.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} not found.");

        return method.Invoke(poolState, arguments);
    }

    private static T GetProperty<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} not found.");

        return (T)property.GetValue(instance)!;
    }

    /// <summary>
    /// Соединение-заглушка: пулу от соединения нужны только его состояние и число потоков.
    /// </summary>
    private sealed class FakeConnection : HttpsConnection
    {
        public int ActiveStreamCount { get; set; }

        public int DisposeCount { get; private set; }

        public override Version Version => HttpVersion.Version20;

        public override bool IsConnected => true;

        public override bool IsSecure => true;

        public override bool IsMultiplexing => true;

        public override int ActiveStreams => ActiveStreamCount;

        public override int MaxConcurrentStreams => 100;

        public override bool IsDraining => false;

        public override System.Net.IPEndPoint? LocalEndPoint => null;

        public override System.Net.IPEndPoint? RemoteEndPoint => null;

        public override long LastActivityTimestamp => System.Diagnostics.Stopwatch.GetTimestamp();

        public override long CreatedTimestamp => System.Diagnostics.Stopwatch.GetTimestamp();

        public override Traffic Traffic => default;

        public override bool HasCapacity => ActiveStreamCount < MaxConcurrentStreams;

        public override void Abort(Exception? ex)
        {
        }

        public override ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override bool MatchesTarget(string host, int port, bool isHttps) => true;

        public override ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override ValueTask<bool> PingAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public override ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override void StartDrain()
        {
        }

        protected override void Dispose(bool disposing) => DisposeCount++;
    }
}
