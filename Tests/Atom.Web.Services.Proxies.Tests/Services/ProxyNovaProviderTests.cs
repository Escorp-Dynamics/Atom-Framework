namespace Atom.Web.Proxies.Services.Tests;

using System.Net;
using System.Net.Http;
using Atom.Net.Proxies;

public class ProxyNovaProviderTests(ILogger logger) : BenchmarkTests<ProxyNovaProviderTests>(logger)
{
    public ProxyNovaProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "ProxyNova JSON API парсится в нормализованный набор proxy"), Benchmark]
    public void ParseJsonPayloadTest()
    {
        const string payload = """
            {
              "data": [
                {
                  "ip": "atob(\"MTYzLjE3Mi41My4xNA==\").concat(\"22\".substring(1+0, 4-2))",
                  "port": 80,
                  "countryCode": "FR",
                  "countryName": "France",
                  "cityName": "Paris",
                  "latitude": 48.8566,
                  "longitude": 2.3522,
                  "hostname": "163-172-53-142.rev.poneytelecom.eu",
                  "asn": "Scaleway S.a.s.",
                  "aliveSecondsAgo": 120,
                  "uptime": 62
                },
                {
                  "ip": "\"613163613\".substring(2+1, 9-3).concat(\"241.35.271.\".split(\"\").reverse().join(\"\"))",
                  "port": 8080,
                  "countryCode": "EC",
                  "countryName": "Ecuador",
                  "cityName": "Quito",
                  "latitude": -0.1807,
                  "longitude": -78.4678,
                  "hostname": "",
                  "asn": "Example ASN",
                  "aliveSecondsAgo": 15,
                  "uptime": 15
                }
              ]
            }
            """;

        var before = DateTime.UtcNow;
        var proxies = ProxyNovaProvider.Parse(payload).ToArray();
        var after = DateTime.UtcNow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies[0].Provider, Is.EqualTo(nameof(ProxyNovaProvider)));
            Assert.That(proxies[0].Host, Is.EqualTo("163.172.53.142"));
            Assert.That(proxies[0].Port, Is.EqualTo(80));
            Assert.That(proxies[0].Type, Is.EqualTo(ProxyType.Http));
            Assert.That(proxies[0].Anonymity, Is.EqualTo(AnonymityLevel.Low));
            Assert.That(proxies[0].Geolocation?.Country?.IsoCode2, Is.EqualTo("FR"));
            Assert.That(proxies[0].Geolocation?.City, Is.EqualTo("Paris"));
            Assert.That(proxies[0].Uptime, Is.EqualTo((byte)62));
            Assert.That(proxies[0].Alive, Is.InRange(before.AddSeconds(-121), after.AddSeconds(-119)));

            Assert.That(proxies[1].Host, Is.EqualTo("163.172.53.142"));
            Assert.That(proxies[1].Port, Is.EqualTo(8080));
            Assert.That(proxies[1].Geolocation?.Country?.IsoCode2, Is.EqualTo("EC"));
            Assert.That(proxies[1].Geolocation?.City, Is.EqualTo("Quito"));
            Assert.That(proxies[1].Uptime, Is.EqualTo((byte)15));
            Assert.That(proxies[1].Alive, Is.InRange(before.AddSeconds(-16), after.AddSeconds(-14)));
        }
    }

    [TestCase(TestName = "Пустой payload даёт пустой набор proxy"), Benchmark]
    public void ParseEmptyPayloadTest()
    {
        Assert.That(ProxyNovaProvider.Parse(string.Empty), Is.Empty);
    }

    [TestCase(TestName = "ProxyNova CreateEndpoint из options нормализует query"), Benchmark]
    public void CreateEndpointFromOptionsTest()
    {
        var endpoint = ProxyNovaProvider.CreateEndpoint(new ProxyNovaProviderOptions
        {
            Country = "de",
            Near = new(48.8566, 2.3522),
            Limit = 50000,
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint, Does.StartWith(ProxyNovaProvider.DefaultEndpoint));
            Assert.That(endpoint, Does.Contain("limit=1000"));
            Assert.That(endpoint, Does.Contain("near=48.8566%2C2.3522"));
            Assert.That(endpoint, Does.Not.Contain("country="));
        }
    }

    [TestCase(TestName = "ProxyNova clamp limit и убирает country при near"), Benchmark]
    public async Task RefreshNormalizesApiQueryTest()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"ip\":\"atob(\\\"MTI3LjAuMC4x\\\")\",\"port\":8080,\"countryCode\":\"FR\",\"aliveSecondsAgo\":1,\"uptime\":100}]}")
                {
                    Headers = { ContentType = new("application/json") },
                },
            };
        }));

        using var provider = new ProxyNovaProvider(new ProxyNovaProviderOptions
        {
            Country = "DE",
            Near = new(48.8566, 2.3522),
            Limit = 50000,
        }, httpClient);
        await provider.RefreshAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestedUri, Is.Not.Null);
            Assert.That(requestedUri!.Query, Does.Contain("limit=1000"));
            Assert.That(requestedUri!.Query, Does.Contain("near=48.8566%2C2.3522"));
            Assert.That(requestedUri!.Query, Does.Not.Contain("country="));
            Assert.That(provider.PoolCount, Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "ProxyNova plain-text ошибка invalid near поднимает FormatException"), Benchmark]
    public void RefreshThrowsOnPlainTextErrorPayloadTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("invalid ?near= specified")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new ProxyNovaProvider($"{ProxyNovaProvider.DefaultEndpoint}?near=abc", httpClient);

        Assert.That(async () => await provider.RefreshAsync(), Throws.TypeOf<FormatException>());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}