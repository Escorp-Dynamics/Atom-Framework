using Atom.Net.Proxies;

namespace Atom.Web.Proxies.Services.Tests;

public class FreeProxyProviderParsingTests(ILogger logger) : BenchmarkTests<FreeProxyProviderParsingTests>(logger)
{
    public FreeProxyProviderParsingTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "GeoNode JSON парсится в нормализованный набор proxy"), Benchmark]
    public void GeoNodeParseTest()
    {
        const string payload = /*lang=json,strict*/
            """
            {
              "data": [
                {
                  "ip": "185.12.34.56",
                  "port": "8080",
                  "anonymityLevel": "elite",
                  "country": "BR",
                  "city": "Sao Paulo",
                  "lastChecked": "2026-03-18T09:45:00.000Z",
                  "upTime": "93",
                  "protocols": ["http", "https"]
                }
              ]
            }
            """;

        var proxies = GeoNodeProxyProvider.Parse(payload).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies[0].Provider, Is.EqualTo(nameof(GeoNodeProxyProvider)));
            Assert.That(proxies[0].Host, Is.EqualTo("185.12.34.56"));
            Assert.That(proxies[0].Port, Is.EqualTo(8080));
            Assert.That(proxies[0].Anonymity, Is.EqualTo(AnonymityLevel.High));
            Assert.That(proxies[0].Geolocation?.City, Is.EqualTo("Sao Paulo"));
            Assert.That(proxies[0].Uptime, Is.EqualTo(93));
            Assert.That(proxies.Select(proxy => proxy.Type), Is.EquivalentTo(new[] { ProxyType.Http, ProxyType.Https }));
        }
    }

    [TestCase(TestName = "GeoNode CreateEndpoint из options нормализует query"), Benchmark]
    public void GeoNodeCreateEndpointTest()
    {
        var endpoint = GeoNodeProxyProvider.CreateEndpoint(new GeoNodeProxyProviderOptions
        {
            Limit = 250,
            Page = 3,
            SortBy = "speed",
            SortType = "ASC",
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint, Does.Contain("limit=250"));
            Assert.That(endpoint, Does.Contain("page=3"));
            Assert.That(endpoint, Does.Contain("sort_by=speed"));
            Assert.That(endpoint, Does.Contain("sort_type=asc"));
        }
    }

    [TestCase(TestName = "ProxyScrape plain text парсится в HTTP proxy"), Benchmark]
    public void ProxyScrapeParseTest()
    {
        const string payload = "198.51.100.10:8080\n203.0.113.5:3128\ninvalid\n";

        var proxies = ProxyScrapeProvider.Parse(payload).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Http), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(ProxyScrapeProvider)), Is.True);
            Assert.That(proxies[1].Host, Is.EqualTo("203.0.113.5"));
            Assert.That(proxies[1].Port, Is.EqualTo(3128));
        }
    }

    [TestCase(TestName = "ProxyScrape CreateEndpoint из options нормализует query"), Benchmark]
    public void ProxyScrapeCreateEndpointTest()
    {
        var endpoint = ProxyScrapeProvider.CreateEndpoint(new ProxyScrapeProviderOptions
        {
            Protocol = "SOCKS5",
            TimeoutMilliseconds = 20000,
            Country = "DE",
            Ssl = "YES",
            Anonymity = "elite",
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint, Does.Contain("protocol=socks5"));
            Assert.That(endpoint, Does.Contain("timeout=20000"));
            Assert.That(endpoint, Does.Contain("country=de"));
            Assert.That(endpoint, Does.Contain("ssl=yes"));
            Assert.That(endpoint, Does.Contain("anonymity=elite"));
        }
    }
}