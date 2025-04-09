using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services.Tests;

public class ServiceProxyTests(ILogger logger) : BenchmarkTests<ServiceProxyTests>(logger)
{
    public ServiceProxyTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var proxy = Proxy.Rent<ServiceProxy>();

        proxy.Host = "localhost";
        proxy.Port = 80;

        var json = proxy.Serialize();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"host\":\"localhost\",\"port\":80}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        var proxy2 = ServiceProxy.Deserialize(json);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(proxy2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(proxy2.Host, Is.EqualTo(proxy.Host));
                Assert.That(proxy2.Port, Is.EqualTo(proxy.Port));
            }
        }

        if (proxy2 is not null) Proxy.Return(proxy2);

        proxy.Anonymity = AnonymityLevel.High;
        proxy.Geolocation = Geolocation.Rent();
        proxy.Geolocation.Continent = Continent.SA;
        proxy.Geolocation.Country = Country.BRA;
        proxy.Geolocation.Latitude = 45.65656;
        proxy.Geolocation.Longitude = 135.6334544;
        proxy.ASN = "Test ASN";
        proxy.Alive = new DateTime(2025, 2, 1, 21, 04, 35, DateTimeKind.Utc);
        proxy.Uptime = 40;

        json = proxy.Serialize();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"host\":\"localhost\",\"port\":80,\"anonymity\":3,\"geolocation\":{\"continent\":\"SA\",\"country\":\"BRA\",\"latitude\":45.65656,\"longitude\":135.6334544},\"asn\":\"Test ASN\",\"alive\":\"01.02.2025 21:04:35\",\"uptime\":40}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        proxy2 = ServiceProxy.Deserialize(json);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(proxy2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(proxy2.Host, Is.EqualTo(proxy.Host));
                Assert.That(proxy2.Port, Is.EqualTo(proxy.Port));
                Assert.That(proxy2.Geolocation, Is.EqualTo(proxy.Geolocation));
                Assert.That(proxy2.Anonymity, Is.EqualTo(proxy.Anonymity));
                Assert.That(proxy2.ASN, Is.EqualTo(proxy.ASN));
                Assert.That(proxy2.Alive, Is.EqualTo(proxy.Alive));
                Assert.That(proxy2.Uptime, Is.EqualTo(proxy.Uptime));
            }
        }

        if (proxy2 is not null) Proxy.Return(proxy2);

        Proxy.Return(proxy);
    }
}