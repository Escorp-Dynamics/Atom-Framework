namespace Atom.Net.Proxies.Tests;

public class ProxyTests(ILogger logger) : BenchmarkTests<ProxyTests>(logger)
{
    public ProxyTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var proxy = Proxy.Rent();

        proxy.Host = "localhost";
        proxy.Port = 80;

        var json = proxy.Serialize();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"host\":\"localhost\",\"port\":80}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        var proxy2 = Proxy.Deserialize(json);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(proxy2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(proxy2?.Host, Is.EqualTo(proxy.Host));
                Assert.That(proxy2?.Port, Is.EqualTo(proxy.Port));
            }
        }

        if (proxy2 is not null) Proxy.Return(proxy2);

        proxy.UserName = "login";
        proxy.Password = "password";

        json = proxy.Serialize();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(json, Is.Not.Null);
            Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "{\"host\":\"localhost\",\"port\":80,\"userName\":\"login\",\"password\":\"password\"}"));
        }

        if (string.IsNullOrEmpty(json)) return;
        proxy2 = Proxy.Deserialize(json);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(proxy2, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(proxy2?.Host, Is.EqualTo(proxy.Host));
                Assert.That(proxy2?.Port, Is.EqualTo(proxy.Port));
                Assert.That(proxy2?.UserName, Is.EqualTo(proxy.UserName));
                Assert.That(proxy2?.Password, Is.EqualTo(proxy.Password));
            }
        }

        if (proxy2 is not null) Proxy.Return(proxy2);

        Proxy.Return(proxy);
    }
}