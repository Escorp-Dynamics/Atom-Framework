using System.Net;
using System.Net.Http;
using Atom.Net.Proxies;
using Atom.Web.Analytics;

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

    [TestCase(TestName = "GeoNode может пройти все страницы и собрать суммарный пул"), Benchmark]
    public async Task GeoNodeFetchAllPagesTest()
    {
        var requestedPages = new List<int>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var page = GetPage(request.RequestUri);
            requestedPages.Add(page);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page switch
                {
                    1 => """
                        {"data":[
                          {"ip":"198.51.100.10","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]},
                          {"ip":"198.51.100.11","port":"8081","anonymityLevel":"anonymous","country":"DE","protocols":["http"]}
                        ],"total":5,"page":1,"limit":2}
                        """,
                    2 => """
                        {"data":[
                          {"ip":"198.51.100.12","port":"3128","anonymityLevel":"elite","country":"DE","protocols":["http","https"]},
                          {"ip":"198.51.100.13","port":"1080","anonymityLevel":"transparent","country":"DE","protocols":["socks5"]}
                        ],"total":5,"page":2,"limit":2}
                        """,
                    _ => """
                        {"data":[
                          {"ip":"198.51.100.14","port":"80","anonymityLevel":"elite","country":"DE","protocols":["http"]}
                        ],"total":5,"page":3,"limit":2}
                        """,
                })
                {
                    Headers = { ContentType = new("application/json") },
                },
            };
        }));

        using var provider = new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
        {
            Limit = 2,
            Page = 1,
            FetchAllPages = true,
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.FetchAllPages, Is.True);
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(requestedPages, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(proxies, Has.Length.EqualTo(5));
            Assert.That(proxies.Select(static proxy => proxy.Host), Is.SupersetOf(new[]
            {
                "198.51.100.10",
                "198.51.100.11",
                "198.51.100.12",
                "198.51.100.13",
                "198.51.100.14",
            }));
            Assert.That(proxies.Select(static proxy => proxy.Type), Does.Contain(ProxyType.Socks5));
        }
    }

    [TestCase(TestName = "GeoNode page-walk уважает RequestsPerSecondLimit"), Benchmark]
    public async Task GeoNodeFetchAllPagesHonorsRequestsPerSecondLimitTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var page = GetPage(request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page switch
                {
                    1 => """
                        {"data":[{"ip":"198.51.100.10","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]}],"total":4,"page":1,"limit":1}
                        """,
                    2 => """
                        {"data":[{"ip":"198.51.100.11","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]}],"total":4,"page":2,"limit":1}
                        """,
                    3 => """
                        {"data":[{"ip":"198.51.100.12","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]}],"total":4,"page":3,"limit":1}
                        """,
                    _ => """
                        {"data":[{"ip":"198.51.100.13","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]}],"total":4,"page":4,"limit":1}
                        """,
                })
                {
                    Headers = { ContentType = new("application/json") },
                },
            };
        }));

        using var provider = new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
        {
            Limit = 1,
            Page = 1,
            FetchAllPages = true,
            RequestsPerSecondLimit = 2,
        }, httpClient);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var proxies = (await provider.FetchAsync()).ToArray();
        watch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(4));
            Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900)));
        }
    }

    [TestCase(TestName = "GeoNode повторяет 429 с Retry-After и затем проходит"), Benchmark]
    public async Task GeoNodeRetriesTooManyRequestsWithRetryAfterTest()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(75));
                return throttled;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[{"ip":"198.51.100.10","port":"8080","anonymityLevel":"elite","country":"DE","protocols":["http"]}],"total":1,"page":1,"limit":1}
                    """)
                {
                    Headers = { ContentType = new("application/json") },
                },
            };
        }));

        using var provider = new GeoNodeProxyProvider(new GeoNodeProxyProviderOptions
        {
            RetryAttempts = 2,
            RetryDelayMilliseconds = 10,
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var proxies = (await provider.FetchAsync()).ToArray();
        watch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(proxies, Has.Length.EqualTo(1));
            Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60)));
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

    [TestCase(TestName = "ProxyScrape provider применяет protocol type из endpoint query"), Benchmark]
    public async Task ProxyScrapeRefreshUsesConfiguredProtocolTypeTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:1080\n203.0.113.5:2080\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new ProxyScrapeProvider(new ProxyScrapeProviderOptions
        {
            Protocol = "SOCKS5",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Socks5), Is.True);
        }
    }

    [TestCase(TestName = "ProxyScrape targeted fetch сужает query по country, anonymity и protocol"), Benchmark]
    public async Task ProxyScrapeTargetedFetchUsesRequestFiltersTest()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("198.51.100.10:1080\n")
                {
                    Headers = { ContentType = new("text/plain") },
                },
            };
        }));

        using var provider = new ProxyScrapeProvider(new ProxyScrapeProviderOptions
        {
            TimeoutMilliseconds = 20000,
            Ssl = "yes",
        }, httpClient);

        var result = await ((IProxyTargetedProvider)provider).FetchAsync(
            new ProxyProviderFetchRequest(3, [ProxyType.Socks4], [Country.DE], [AnonymityLevel.High]),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestedUri, Is.Not.Null);
            Assert.That(requestedUri!.Query, Does.Contain("protocol=socks4"));
            Assert.That(requestedUri!.Query, Does.Contain("country=de"));
            Assert.That(requestedUri!.Query, Does.Contain("anonymity=elite"));
            Assert.That(requestedUri!.Query, Does.Contain("timeout=20000"));
            Assert.That(requestedUri!.Query, Does.Contain("ssl=yes"));
            Assert.That(result.Proxies, Has.Count.EqualTo(1));
            Assert.That(result.Proxies[0].Type, Is.EqualTo(ProxyType.Socks4));
        }
    }

    [TestCase(TestName = "OpenProxyList plain text парсится в proxy заданного типа"), Benchmark]
    public void OpenProxyListParseTest()
    {
        const string payload = "198.51.100.10:8080\n203.0.113.5:1080\ninvalid\n";

        var proxies = OpenProxyListProvider.Parse(payload, ProxyType.Https).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Https), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(OpenProxyListProvider)), Is.True);
            Assert.That(proxies[1].Host, Is.EqualTo("203.0.113.5"));
            Assert.That(proxies[1].Port, Is.EqualTo(1080));
        }
    }

    [TestCase(TestName = "OpenProxyList CreateEndpoint из options нормализует protocol"), Benchmark]
    public void OpenProxyListCreateEndpointTest()
    {
        var endpoint = OpenProxyListProvider.CreateEndpoint(new OpenProxyListProviderOptions
        {
            Protocol = "SOCKS4",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/roosterkid/openproxylist/main/SOCKS4_RAW.txt"));
    }

    [TestCase(TestName = "OpenProxyList provider загружает plain text endpoint и применяет protocol type"), Benchmark]
    public async Task OpenProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:8080\n203.0.113.5:1080\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new OpenProxyListProvider(new OpenProxyListProviderOptions
        {
            Protocol = "https",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Https), Is.True);
        }
    }

    [TestCase(TestName = "Proxifly mixed-scheme plain text парсится в proxy разных типов"), Benchmark]
    public void ProxiflyProxyListParseTest()
    {
        const string payload = "http://198.51.100.10:8080\nhttps://203.0.113.5:8443\nsocks4://203.0.113.6:1080\nsocks5://203.0.113.7:1080\ninvalid\n";

        var proxies = ProxiflyProxyListProvider.Parse(payload).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(4));
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(ProxiflyProxyListProvider)), Is.True);
            Assert.That(proxies.Select(static proxy => proxy.Type), Is.EqualTo(new[]
            {
                ProxyType.Http,
                ProxyType.Https,
                ProxyType.Socks4,
                ProxyType.Socks5,
            }));
        }
    }

    [TestCase(TestName = "Proxifly CreateEndpoint из options возвращает единый raw endpoint"), Benchmark]
    public void ProxiflyProxyListCreateEndpointTest()
    {
        var endpoint = ProxiflyProxyListProvider.CreateEndpoint(new ProxiflyProxyListProviderOptions
        {
            Protocol = "SOCKS5",
        });

        Assert.That(endpoint, Is.EqualTo(ProxiflyProxyListProvider.DefaultEndpoint));
    }

    [TestCase(TestName = "Proxifly provider фильтрует configured protocol поверх mixed-scheme feed"), Benchmark]
    public async Task ProxiflyProxyListRefreshUsesConfiguredProtocolFilterTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("http://198.51.100.10:8080\nsocks5://203.0.113.5:1080\nsocks5://203.0.113.6:1081\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new ProxiflyProxyListProvider(new ProxiflyProxyListProviderOptions
        {
            Protocol = "socks5",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Socks5), Is.True);
        }
    }

        [TestCase(TestName = "ProxyMania HTML-таблица парсится в нормализованный набор proxy"), Benchmark]
        public void ProxymaniaProxyListParseTest()
        {
                const string payload = """
                        <table class="table_proxychecker">
                            <tbody id="resultTable">
                                <tr>
                                    <td class="proxy-cell">35.225.22.61:80</td>
                                    <td class="country-cell"><img src="/img/flags/us.svg" alt="United States"> United States </td>
                                    <td>HTTPS</td>
                                    <td>High</td>
                                    <td class="speed-fast">135 ms</td>
                                    <td class="last-checked" data-timestamp="1773984974"><span class="relative-time">1 min. ago</span></td>
                                </tr>
                                <tr>
                                    <td class="proxy-cell">203.0.113.5:1080</td>
                                    <td class="country-cell"><img src="/img/flags/de.svg" alt="Germany"> Germany </td>
                                    <td>SOCKS5</td>
                                    <td>Medium</td>
                                    <td class="speed-fast">220 ms</td>
                                    <td class="last-checked" data-timestamp="1773984900"><span class="relative-time">2 min. ago</span></td>
                                </tr>
                            </tbody>
                        </table>
                        """;

                var proxies = ProxymaniaProxyListProvider.Parse(payload).ToArray();
                Country.TryParse("US", System.Globalization.CultureInfo.InvariantCulture, out var usCountry);
                Country.TryParse("DE", System.Globalization.CultureInfo.InvariantCulture, out var deCountry);

                using (Assert.EnterMultipleScope())
                {
                        Assert.That(proxies, Has.Length.EqualTo(2));
                        Assert.That(proxies.All(static proxy => proxy.Provider == nameof(ProxymaniaProxyListProvider)), Is.True);
                        Assert.That(proxies.Select(static proxy => proxy.Type), Is.EqualTo(new[] { ProxyType.Https, ProxyType.Socks5 }));
                        Assert.That(proxies[0].Anonymity, Is.EqualTo(AnonymityLevel.High));
                        Assert.That(proxies[1].Anonymity, Is.EqualTo(AnonymityLevel.Medium));
                        Assert.That(proxies[0].Geolocation?.Country, Is.EqualTo(usCountry));
                        Assert.That(proxies[1].Geolocation?.Country, Is.EqualTo(deCountry));
                        Assert.That(proxies[0].Alive, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1773984974).UtcDateTime));
                }
        }

        [TestCase(TestName = "ProxyMania CreateEndpoint из options нормализует HTML query"), Benchmark]
        public void ProxymaniaProxyListCreateEndpointTest()
        {
                var endpoint = ProxymaniaProxyListProvider.CreateEndpoint(new ProxymaniaProxyListProviderOptions
                {
                        Protocol = "https",
                        Country = "us",
                        MaximumSpeedMilliseconds = 300,
                        Page = 2,
                });

                using (Assert.EnterMultipleScope())
                {
                        Assert.That(endpoint, Does.StartWith(ProxymaniaProxyListProvider.DefaultEndpoint));
                        Assert.That(endpoint, Does.Contain("type=HTTPS"));
                        Assert.That(endpoint, Does.Contain("country=US"));
                        Assert.That(endpoint, Does.Contain("speed=300"));
                        Assert.That(endpoint, Does.Contain("page=2"));
                }
        }

        [TestCase(TestName = "ProxyMania может пройти HTML-пагинацию и собрать суммарный пул"), Benchmark]
        public async Task ProxymaniaProxyListFetchAllPagesTest()
        {
                var requestedUris = new List<string>();
                using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
                {
                        var uri = request.RequestUri?.AbsoluteUri ?? throw new InvalidOperationException("Missing request uri.");
                        requestedUris.Add(uri);

                        var payload = uri.Contains("page=2", StringComparison.OrdinalIgnoreCase)
                                ? """
                                        <table class="table_proxychecker">
                                            <tbody id="resultTable">
                                                <tr>
                                                    <td class="proxy-cell">203.0.113.6:8443</td>
                                                    <td class="country-cell"><img src="/img/flags/us.svg" alt="United States"> United States </td>
                                                    <td>HTTPS</td>
                                                    <td>High</td>
                                                    <td class="speed-fast">205 ms</td>
                                                    <td class="last-checked" data-timestamp="1773984800"><span class="relative-time">3 min. ago</span></td>
                                                </tr>
                                            </tbody>
                                        </table>
                                        """
                                : """
                                        <div class="pagination-container">
                                            <ul class="pagination justify-content-center">
                                                <li class="page-item active"><a class="page-link">1</a></li>
                                                <li class="page-item"><a href="https://proxymania.su/en/free-proxy?type=HTTPS&country=US&speed=300&&page=2">2</a></li>
                                                <li class="page-item"><a href="https://proxymania.su/en/free-proxy?type=HTTPS&country=US&speed=300&&page=2">Next</a></li>
                                            </ul>
                                        </div>
                                        <table class="table_proxychecker">
                                            <tbody id="resultTable">
                                                <tr>
                                                    <td class="proxy-cell">198.51.100.10:8080</td>
                                                    <td class="country-cell"><img src="/img/flags/us.svg" alt="United States"> United States </td>
                                                    <td>HTTPS</td>
                                                    <td>High</td>
                                                    <td class="speed-fast">135 ms</td>
                                                    <td class="last-checked" data-timestamp="1773984974"><span class="relative-time">1 min. ago</span></td>
                                                </tr>
                                            </tbody>
                                        </table>
                                        """;

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                                Content = new StringContent(payload)
                                {
                                        Headers = { ContentType = new("text/html") },
                                },
                        };
                }));

                using var provider = new ProxymaniaProxyListProvider(new ProxymaniaProxyListProviderOptions
                {
                        Protocol = "https",
                        Country = "US",
                        MaximumSpeedMilliseconds = 300,
                        FetchAllPages = true,
                        RequestsPerSecondLimit = 10,
                }, httpClient);

                var proxies = (await provider.FetchAsync()).ToArray();

                using (Assert.EnterMultipleScope())
                {
                        Assert.That(provider.FetchAllPages, Is.True);
                        Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
                        Assert.That(requestedUris, Has.Count.EqualTo(2));
                        Assert.That(requestedUris[0], Does.Contain("type=HTTPS"));
                        Assert.That(requestedUris[0], Does.Contain("country=US"));
                        Assert.That(requestedUris[0], Does.Contain("speed=300"));
                        Assert.That(requestedUris[1], Does.Contain("page=2"));
                        Assert.That(proxies, Has.Length.EqualTo(2));
                        Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Https), Is.True);
                }
        }

        [TestCase(TestName = "hide-my-name HTML-таблица парсится в нормализованный набор proxy"), Benchmark]
        public void HideMyNameProxyListParseTest()
        {
                const string payload = """
                        <table>
                            <tbody>
                                <tr>
                                    <td>154.16.146.42</td>
                                    <td>80</td>
                                    <td><i class="flag-icon flag-icon-us"></i><span class="country">United States</span><span class="city">Denver</span></td>
                                    <td><div class="bar"><p>480 мс</p></div></td>
                                    <td>HTTP</td>
                                    <td>Высокая</td>
                                    <td>51 секунда</td>
                                </tr>
                                <tr>
                                    <td>184.178.172.5</td>
                                    <td>15303</td>
                                    <td><i class="flag-icon flag-icon-us"></i><span class="country">United States</span><span class="city"></span></td>
                                    <td><div class="bar"><p>1260 мс</p></div></td>
                                    <td>SOCKS4, SOCKS5</td>
                                    <td>Высокая</td>
                                    <td>2 минуты</td>
                                </tr>
                                <tr>
                                    <td>47.91.104.88</td>
                                    <td>3128</td>
                                    <td><i class="flag-icon flag-icon-ae"></i><span class="country">United Arab Emirates</span><span class="city">Dubai</span></td>
                                    <td><div class="bar"><p>1520 мс</p></div></td>
                                    <td>HTTP</td>
                                    <td>Нет</td>
                                    <td>1 час</td>
                                </tr>
                            </tbody>
                        </table>
                        """;

                var before = DateTime.UtcNow;
                var proxies = HideMyNameProxyListProvider.Parse(payload).ToArray();
                Country.TryParse("US", System.Globalization.CultureInfo.InvariantCulture, out var usCountry);
                Country.TryParse("AE", System.Globalization.CultureInfo.InvariantCulture, out var aeCountry);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(proxies, Has.Length.EqualTo(3));
                        Assert.That(proxies.All(static proxy => proxy.Provider == nameof(HideMyNameProxyListProvider)), Is.True);
                        Assert.That(proxies.Select(static proxy => proxy.Type), Is.EqualTo(new[]
                        {
                                ProxyType.Http,
                                ProxyType.Socks5,
                                ProxyType.Http,
                        }));
                        Assert.That(proxies[0].Geolocation?.Country, Is.EqualTo(usCountry));
                        Assert.That(proxies[0].Geolocation?.City, Is.EqualTo("Denver"));
                        Assert.That(proxies[1].Geolocation?.Country, Is.EqualTo(usCountry));
                    Assert.That(proxies[2].Geolocation?.Country, Is.EqualTo(aeCountry));
                    Assert.That(proxies[2].Geolocation?.City, Is.EqualTo("Dubai"));
                        Assert.That(proxies[0].Anonymity, Is.EqualTo(AnonymityLevel.High));
                    Assert.That(proxies[2].Anonymity, Is.EqualTo(AnonymityLevel.Transparent));
                        Assert.That(proxies[0].Alive, Is.LessThanOrEqualTo(before));
                    Assert.That(proxies[2].Alive, Is.LessThanOrEqualTo(before.AddMinutes(-50)));
                }
        }

        [TestCase(TestName = "hide-my-name CreateEndpoint из options нормализует HTML query"), Benchmark]
        public void HideMyNameProxyListCreateEndpointTest()
        {
                var endpoint = HideMyNameProxyListProvider.CreateEndpoint(new HideMyNameProxyListProviderOptions
                {
                        CountryFilter = "amau",
                        MaximumSpeedMilliseconds = 150,
                        TypeFilter = "s",
                        AnonymityFilter = "4",
                        Start = 64,
                });

                using (Assert.EnterMultipleScope())
                {
                        Assert.That(endpoint, Does.StartWith(HideMyNameProxyListProvider.DefaultEndpoint));
                        Assert.That(endpoint, Does.Contain("country=AMAU"));
                        Assert.That(endpoint, Does.Contain("maxtime=150"));
                        Assert.That(endpoint, Does.Contain("type=s"));
                        Assert.That(endpoint, Does.Contain("anon=4"));
                        Assert.That(endpoint, Does.Contain("start=64"));
                }
        }

        [TestCase(TestName = "hide-my-name может пройти start-пагинацию и собрать суммарный пул"), Benchmark]
        public async Task HideMyNameProxyListFetchAllPagesTest()
        {
                var requestedUris = new List<string>();
                using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
                {
                        var uri = request.RequestUri?.AbsoluteUri ?? throw new InvalidOperationException("Missing request uri.");
                        requestedUris.Add(uri);

                        var payload = uri.Contains("start=128", StringComparison.OrdinalIgnoreCase)
                                ? """
                                        <table>
                                            <tbody>
                                                <tr>
                                                    <td>203.0.113.12</td>
                                                    <td>1080</td>
                                                    <td><i class="flag-icon flag-icon-jp"></i><span class="country">Japan</span><span class="city">Tokyo</span></td>
                                                    <td><div class="bar"><p>500 мс</p></div></td>
                                                    <td>SOCKS5</td>
                                                    <td>Высокая</td>
                                                    <td>3 минуты</td>
                                                </tr>
                                            </tbody>
                                            <div class="pagination"><ul><li class="is-active"><a href="/proxy-list/?start=128#list">3</a></li></ul></div>
                                        </table>
                                        """
                                : uri.Contains("start=64", StringComparison.OrdinalIgnoreCase)
                                        ? """
                                                <table>
                                                    <tbody>
                                                        <tr>
                                                            <td>203.0.113.11</td>
                                                            <td>1080</td>
                                                            <td><i class="flag-icon flag-icon-us"></i><span class="country">United States</span><span class="city">Denver</span></td>
                                                            <td><div class="bar"><p>450 мс</p></div></td>
                                                            <td>SOCKS4, SOCKS5</td>
                                                            <td>Высокая</td>
                                                            <td>2 минуты</td>
                                                        </tr>
                                                    </tbody>
                                                    <div class="pagination"><ul>
                                                        <li><a href="/proxy-list/?start=64#list">2</a></li>
                                                        <li><a href="/proxy-list/?start=128#list">3</a></li>
                                                    </ul></div>
                                                </table>
                                                """
                                        : """
                                                <table>
                                                    <tbody>
                                                        <tr>
                                                            <td>203.0.113.10</td>
                                                            <td>8080</td>
                                                            <td><i class="flag-icon flag-icon-us"></i><span class="country">United States</span><span class="city">Denver</span></td>
                                                            <td><div class="bar"><p>400 мс</p></div></td>
                                                            <td>HTTP</td>
                                                            <td>Высокая</td>
                                                            <td>1 минута</td>
                                                        </tr>
                                                    </tbody>
                                                    <div class="pagination"><ul>
                                                        <li class="is-active"><a href="/proxy-list/">1</a></li>
                                                        <li><a href="/proxy-list/?start=64#list">2</a></li>
                                                        <li><a href="/proxy-list/?start=128#list">3</a></li>
                                                    </ul></div>
                                                </table>
                                                """;

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                                Content = new StringContent(payload)
                                {
                                        Headers = { ContentType = new("text/html") },
                                },
                        };
                }));

                using var provider = new HideMyNameProxyListProvider(new HideMyNameProxyListProviderOptions
                {
                        FetchAllPages = true,
                        RequestsPerSecondLimit = 10,
                }, httpClient);

                var proxies = (await provider.FetchAsync()).ToArray();

                using (Assert.EnterMultipleScope())
                {
                        Assert.That(provider.FetchAllPages, Is.True);
                        Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
                        Assert.That(requestedUris, Has.Count.EqualTo(3));
                        Assert.That(requestedUris[1], Does.Contain("start=64"));
                        Assert.That(requestedUris[2], Does.Contain("start=128"));
                    Assert.That(proxies, Has.Length.EqualTo(3));
                        Assert.That(proxies.Select(static proxy => proxy.Type), Is.EqualTo(new[]
                        {
                                ProxyType.Http,
                                ProxyType.Socks5,
                                ProxyType.Socks5,
                        }));
                }
        }

    [TestCase(TestName = "Iplocate plain text парсится в proxy заданного типа"), Benchmark]
    public void IplocateProxyListParseTest()
    {
        const string payload = "198.51.100.10:8080\n203.0.113.5:8443\ninvalid\n";

        var proxies = IplocateProxyListProvider.Parse(payload, ProxyType.Https).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Https), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(IplocateProxyListProvider)), Is.True);
            Assert.That(proxies[1].Port, Is.EqualTo(8443));
        }
    }

    [TestCase(TestName = "Iplocate CreateEndpoint из options нормализует protocol"), Benchmark]
    public void IplocateProxyListCreateEndpointTest()
    {
        var endpoint = IplocateProxyListProvider.CreateEndpoint(new IplocateProxyListProviderOptions
        {
            Protocol = "SOCKS5",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/iplocate/free-proxy-list/main/protocols/socks5.txt"));
    }

    [TestCase(TestName = "Iplocate provider загружает plain text endpoint и применяет protocol type"), Benchmark]
    public async Task IplocateProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:8080\n203.0.113.5:1080\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new IplocateProxyListProvider(new IplocateProxyListProviderOptions
        {
            Protocol = "http",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Http), Is.True);
        }
    }

    [TestCase(TestName = "r00tee plain text парсится в proxy заданного типа"), Benchmark]
    public void R00teeProxyListParseTest()
    {
        const string payload = "198.51.100.10:443\n203.0.113.5:8443\ninvalid\n";

        var proxies = R00teeProxyListProvider.Parse(payload, ProxyType.Https).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Https), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(R00teeProxyListProvider)), Is.True);
            Assert.That(proxies[0].Port, Is.EqualTo(443));
        }
    }

    [TestCase(TestName = "r00tee CreateEndpoint из options нормализует protocol"), Benchmark]
    public void R00teeProxyListCreateEndpointTest()
    {
        var endpoint = R00teeProxyListProvider.CreateEndpoint(new R00teeProxyListProviderOptions
        {
            Protocol = "SOCKS4",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/r00tee/Proxy-List/main/Socks4.txt"));
    }

    [TestCase(TestName = "r00tee provider загружает plain text endpoint и применяет protocol type"), Benchmark]
    public async Task R00teeProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:1080\n203.0.113.5:1081\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new R00teeProxyListProvider(new R00teeProxyListProviderOptions
        {
            Protocol = "socks5",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Socks5), Is.True);
        }
    }

    [TestCase(TestName = "vakhov plain text парсится в proxy заданного типа"), Benchmark]
    public void VakhovProxyListParseTest()
    {
        const string payload = "198.51.100.10:8080\n203.0.113.5:8443\ninvalid\n";

        var proxies = VakhovProxyListProvider.Parse(payload, ProxyType.Http).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Http), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(VakhovProxyListProvider)), Is.True);
            Assert.That(proxies[1].Port, Is.EqualTo(8443));
        }
    }

    [TestCase(TestName = "vakhov CreateEndpoint из options нормализует protocol"), Benchmark]
    public void VakhovProxyListCreateEndpointTest()
    {
        var endpoint = VakhovProxyListProvider.CreateEndpoint(new VakhovProxyListProviderOptions
        {
            Protocol = "SOCKS4",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/vakhov/fresh-proxy-list/master/socks4.txt"));
    }

    [TestCase(TestName = "vakhov provider загружает plain text endpoint и применяет protocol type"), Benchmark]
    public async Task VakhovProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:1080\n203.0.113.5:1081\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new VakhovProxyListProvider(new VakhovProxyListProviderOptions
        {
            Protocol = "socks5",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Socks5), Is.True);
        }
    }

    [TestCase(TestName = "gfpcom protocol-specific список парсит plain и scheme строки"), Benchmark]
    public void GfpcomProxyListParseTest()
    {
        const string payload = "198.51.100.10:8080\nhttp://user:pass@203.0.113.5:8443\ninvalid\n";

        var proxies = GfpcomProxyListProvider.Parse(payload, ProxyType.Http).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Http), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(GfpcomProxyListProvider)), Is.True);
            Assert.That(proxies[1].Host, Is.EqualTo("203.0.113.5"));
            Assert.That(proxies[1].Port, Is.EqualTo(8443));
        }
    }

    [TestCase(TestName = "gfpcom CreateEndpoint из options нормализует protocol"), Benchmark]
    public void GfpcomProxyListCreateEndpointTest()
    {
        var endpoint = GfpcomProxyListProvider.CreateEndpoint(new GfpcomProxyListProviderOptions
        {
            Protocol = "SOCKS5",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/wiki/gfpcom/free-proxy-list/lists/socks5.txt"));
    }

    [TestCase(TestName = "gfpcom provider загружает protocol-specific endpoint и применяет нормализацию"), Benchmark]
    public async Task GfpcomProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("http://198.51.100.10:8080\n203.0.113.5:8081\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new GfpcomProxyListProvider(new GfpcomProxyListProviderOptions
        {
            Protocol = "http",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Http), Is.True);
        }
    }

    [TestCase(TestName = "Zaeem plain text парсится в proxy заданного типа"), Benchmark]
    public void ZaeemProxyListParseTest()
    {
        const string payload = "198.51.100.10:8080\n203.0.113.5:8443\n999.999.999.999:70000\ninvalid\n";

        var proxies = ZaeemProxyListProvider.Parse(payload, ProxyType.Https).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(proxy => proxy.Type == ProxyType.Https), Is.True);
            Assert.That(proxies.All(proxy => proxy.Provider == nameof(ZaeemProxyListProvider)), Is.True);
            Assert.That(proxies[1].Port, Is.EqualTo(8443));
        }
    }

    [TestCase(TestName = "Zaeem CreateEndpoint из options нормализует protocol"), Benchmark]
    public void ZaeemProxyListCreateEndpointTest()
    {
        var endpoint = ZaeemProxyListProvider.CreateEndpoint(new ZaeemProxyListProviderOptions
        {
            Protocol = "SOCKS4",
        });

        Assert.That(endpoint, Is.EqualTo("https://raw.githubusercontent.com/Zaeem20/FREE_PROXIES_LIST/master/socks4.txt"));
    }

    [TestCase(TestName = "Zaeem provider загружает plain text endpoint и применяет protocol type"), Benchmark]
    public async Task ZaeemProxyListRefreshUsesConfiguredProtocolTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("198.51.100.10:1080\n203.0.113.5:1081\n")
            {
                Headers = { ContentType = new("text/plain") },
            },
        }));

        using var provider = new ZaeemProxyListProvider(new ZaeemProxyListProviderOptions
        {
            Protocol = "http",
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.RequestsPerSecondLimit, Is.EqualTo(10));
            Assert.That(proxies, Has.Length.EqualTo(2));
            Assert.That(proxies.All(static proxy => proxy.Type == ProxyType.Http), Is.True);
        }
    }

    [TestCase(TestName = "ProxyNova может пройти опубликованные country filters и собрать расширенный пул"), Benchmark]
    public async Task ProxyNovaFetchPublishedCountriesTest()
    {
        var requestedCountries = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request uri.");
            if (uri.AbsoluteUri == ProxyNovaProvider.DefaultCountryIndexEndpoint)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<a href=\"/proxy-server-list/country-id/\">ID</a><a href=\"/proxy-server-list/country-us/\">US</a>")
                    {
                        Headers = { ContentType = new("text/html") },
                    },
                };
            }

            var country = GetQueryValue(uri, "country")?.ToUpperInvariant() ?? string.Empty;
            requestedCountries.Add(country);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(country switch
                {
                    "ID" => """
                        {"data":[
                          {"ip":"198.51.100.10","port":8080,"countryCode":"ID","countryName":"Indonesia","aliveSecondsAgo":1,"uptime":10},
                          {"ip":"198.51.100.11","port":8081,"countryCode":"ID","countryName":"Indonesia","aliveSecondsAgo":1,"uptime":10}
                        ]}
                        """,
                    "US" => """
                        {"data":[
                          {"ip":"198.51.100.12","port":3128,"countryCode":"US","countryName":"United States","aliveSecondsAgo":1,"uptime":10}
                        ]}
                        """,
                    _ => "{\"data\":[]}",
                })
                {
                    Headers = { ContentType = new("application/json") },
                },
            };
        }));

        using var provider = new ProxyNovaProvider(new ProxyNovaProviderOptions
        {
            FetchPublishedCountries = true,
            Limit = ProxyNovaProvider.MaximumLimit,
            RequestsPerSecondLimit = 10,
        }, httpClient);

        var proxies = (await provider.FetchAsync()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestedCountries, Is.EqualTo(new[] { "ID", "US" }));
            Assert.That(proxies, Has.Length.EqualTo(3));
            Assert.That(proxies.Select(static proxy => proxy.Host), Is.EquivalentTo(new[]
            {
                "198.51.100.10",
                "198.51.100.11",
                "198.51.100.12",
            }));
        }
    }

    private static int GetPage(Uri? uri)
    {
        if (uri is null)
        {
            return 1;
        }

        var query = uri.Query.AsSpan();
        if (!query.IsEmpty && query[0] == '?')
        {
            query = query[1..];
        }

        while (!query.IsEmpty)
        {
            var separatorIndex = query.IndexOf('&');
            var segment = separatorIndex < 0 ? query : query[..separatorIndex];
            query = separatorIndex < 0 ? [] : query[(separatorIndex + 1)..];

            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            if (!segment[..equalsIndex].SequenceEqual("page"))
            {
                continue;
            }

            if (int.TryParse(Uri.UnescapeDataString(segment[(equalsIndex + 1)..].ToString()), out var page))
            {
                return page;
            }
        }

        return 1;
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query.AsSpan();
        if (!query.IsEmpty && query[0] == '?')
        {
            query = query[1..];
        }

        while (!query.IsEmpty)
        {
            var separatorIndex = query.IndexOf('&');
            var segment = separatorIndex < 0 ? query : query[..separatorIndex];
            query = separatorIndex < 0 ? [] : query[(separatorIndex + 1)..];

            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            if (!segment[..equalsIndex].SequenceEqual(key))
            {
                continue;
            }

            return Uri.UnescapeDataString(segment[(equalsIndex + 1)..].ToString());
        }

        return null;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}