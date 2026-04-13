using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Https;

[TestFixture]
public sealed class HttpsClientHandlerTests
{
    private const int TestTimeoutMs = 10_000;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    [Test]
    public async Task HttpClientWithHttpsClientHandlerCanSendHttp11Request()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(request.Head, Does.StartWith("GET /handler HTTP/1.1\r\n"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 7\r\n\r\nhandler"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var client = new HttpClient(new HttpsClientHandler(), disposeHandler: true);
        var responseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/handler")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(responseMessage, Is.TypeOf<HttpsResponseMessage>());
        Assert.That(body, Is.EqualTo("handler"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpClientWithHttpsClientHandlerCanSendHttps11RequestOverCustomTls()
    {
        using var certificate = CreateLoopbackCertificate();
        using var listener = CreateLocalhostListener();

        var serverTask = RunTlsServerAsync(listener, certificate, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(request.Head, Does.StartWith("GET /secure-handler HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: localhost"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\n\r\nsecure-body"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            SslProtocols = SslProtocols.Tls12,
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = HttpsClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        var responseMessage = await Within(client.GetAsync(new Uri($"https://localhost:{GetPort(listener)}/secure-handler"))).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(responseMessage, Is.TypeOf<HttpsResponseMessage>());
        Assert.That(body, Is.EqualTo("secure-body"));
        await Within(serverTask).ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerReusesCookieContainerAcrossRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var seenFirstCookie = false;
        var seenSecondCookie = false;

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            seenFirstCookie = first.Head.Contains("Cookie:", StringComparison.OrdinalIgnoreCase);
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nSet-Cookie: sid=abc; Path=/\r\nConnection: close\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);
        }, async stream =>
        {
            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            seenSecondCookie = second.Head.Contains("Cookie: sid=abc", StringComparison.OrdinalIgnoreCase);
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler();
        using var client = new HttpClient(handler, disposeHandler: false);

        _ = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/first")).ConfigureAwait(false);
        var secondResponseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/second")).ConfigureAwait(false);
        var secondBody = await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(seenFirstCookie, Is.False);
        Assert.That(seenSecondCookie, Is.True);
        Assert.That(secondBody, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesBrowserProfileDefaultsWhenHeadersAreMissing()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0\r\n"));
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("priority: u=1, i\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate, br, zstd\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Language: en-US,en;q=0.9\r\n"));
                Assert.That(request.Head, Does.Contain("Connection: keep-alive\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Contain("sec-ch-ua-mobile: ?0\r\n"));
                Assert.That(request.Head, Does.Contain("sec-ch-ua-platform: \"Windows\"\r\n"));
                Assert.That(request.Head, Does.Contain("sec-ch-ua: \"Not_A Brand\";v=\"24\", \"Chromium\";v=\"131\", \"Microsoft Edge\";v=\"131\"\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        var adapter = new UserAgentAdapter();
        using var handler = adapter.CreateHandler("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");
        using var client = new HttpClient(handler, disposeHandler: false);

        var responseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/profile-defaults")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesChromiumFetchPriorityDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("priority: u=1, i\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Contain("sec-ch-ua-mobile: ?0\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateEdgeDesktopWindows(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/chromium-fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        await client.SendAsync(request).ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesFirefoxAcceptEncodingDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate, br, zstd\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Language: en-US,en;q=0.5\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-ch-ua:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateFirefoxDesktop(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        var responseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/firefox-encoding")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariAcceptEncodingConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate, br\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Language: en-US\r\n"));
                Assert.That(request.Head, Does.Not.Contain("Accept-Language: en-US,en;q=0.9\r\n"));
                Assert.That(request.Head, Does.Not.Contain("zstd"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-ch-ua:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        var responseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/safari-encoding")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesNavigationDefaultsForNavigationRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7\r\n"));
                Assert.That(request.Head, Does.Contain("priority: u=0, i\r\n"));
                Assert.That(request.Head, Does.Contain("Upgrade-Insecure-Requests: 1\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: none\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head.IndexOf("Host:", StringComparison.Ordinal), Is.GreaterThan(0));
                Assert.That(request.Head.IndexOf("Connection:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("Host:", StringComparison.Ordinal)));
                Assert.That(request.Head.IndexOf("sec-ch-ua:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("Connection:", StringComparison.Ordinal)));
                Assert.That(request.Head.IndexOf("sec-ch-ua:", StringComparison.Ordinal), Is.GreaterThan(0));
                Assert.That(request.Head.IndexOf("User-Agent:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("sec-ch-ua:", StringComparison.Ordinal)));
                Assert.That(request.Head.IndexOf("Accept:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("User-Agent:", StringComparison.Ordinal)));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/navigation"))
        {
            Kind = RequestKind.Navigation,
        };

        await client.SendAsync(request).ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesFirefoxNavigationAcceptDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Contain("priority: u=0, i\r\n"));
                Assert.That(request.Head, Does.Not.Contain("image/avif"));
                Assert.That(request.Head, Does.Not.Contain("image/webp"));
                Assert.That(request.Head, Does.Not.Contain("image/apng"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-ch-ua:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateFirefoxDesktop(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/firefox-navigation"))
        {
            Kind = RequestKind.Navigation,
        };

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesFirefoxFetchPriorityDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("priority: u=4\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-ch-ua:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateFirefoxDesktop(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/firefox-fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariNavigationAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Not.Contain("image/avif"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-ch-ua:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/safari-navigation"))
        {
            Kind = RequestKind.Navigation,
        };

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerEmitsOriginForUnsafeNavigationRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/navigation"))
        {
            Kind = RequestKind.Navigation,
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerSuppressesOriginForUnsafeNavigationWhenFormSubmissionIsExplicitlyFalse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/navigation"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = false,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerSuppressesRefererAndOriginForUnsafeNavigationWhenFormSubmissionIsFalseAndPolicyIsNoReferrer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/navigation"))
        {
            Kind = RequestKind.Navigation,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = false,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsOriginForUnsafeNavigationWhenStrictOriginWhenCrossOriginSuppressesDowngradeReferer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Contain("Origin: https://example.net\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: cross-site\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/navigation"))
        {
            Kind = RequestKind.Navigation,
            ReferrerPolicy = ReferrerPolicyMode.StrictOriginWhenCrossOrigin,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = true,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAlignsChromiumMultipartNavigationDefaultsToCapture()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var boundary = "----atom-multipart-boundary";
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            var bodyText = Encoding.UTF8.GetString(request.Body);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.StartWith("POST /navigation HTTP/1.1\r\n"));
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain($"Content-Type: multipart/form-data; boundary=\"{boundary}\"\r\n"));
                Assert.That(request.Head, Does.Contain("Upgrade-Insecure-Requests: 1\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(bodyText, Does.Contain($"--{boundary}"));
                Assert.That(bodyText, Does.Contain("Content-Disposition: form-data; name=message"));
                Assert.That(bodyText, Does.Contain("hello-form"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/navigation"))
        {
            Kind = RequestKind.Navigation,
        };
        using var content = new MultipartFormDataContent(boundary);
        content.Add(new StringContent("hello-form", Encoding.UTF8), "message");
        request.Content = content;
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesExplicitIframeContextForNestedNavigation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: iframe\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-fetch-user:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsUserActivated = false,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerEmitsSecFetchUserForExplicitIframeFormSubmission()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: iframe\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-user: ?1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsFormSubmission = true,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSecFetchUserSuppressedWhenIframeFormSubmissionExplicitlySetsIsUserActivatedFalse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: iframe\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-fetch-user:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsFormSubmission = true,
                IsUserActivated = false,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerInfersIframeContextFromNonTopLevelNavigation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: iframe\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-fetch-user:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsTopLevelNavigation = false,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesSameOriginSiteForIframeNavigationWithoutReferrer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: iframe\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-fetch-user:"));
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsTopLevelNavigation = false,
            },
        };

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotInferIframeDestinationForNonNavigationRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-script.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/frame");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesHttpRequestOptionsForExplicitIframeScriptContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-script.js"))
            .WithHttpsRequestKind(RequestKind.Fetch)
            .WithHttpsBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            });
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/frame");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesExplicitIframeScriptContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-script.js"))
            .WithRequestKind(RequestKind.Fetch)
            .WithBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            })
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/frame"))
            .BuildHttpsRequest();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerSuppressesSecFetchUserForReloadNavigation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: navigate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: document\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nsec-fetch-user:"));
                Assert.That(request.Head, Does.Contain("Upgrade-Insecure-Requests: 1\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/reload"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsReload = true,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDerivesRefererOriginAndSiteContextFromReferrer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerEmitsOriginForSameOriginUnsafeFetch()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
            Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerTreatsCrossSchemeLoopbackFetchAsCrossSiteAndSuppressesReferer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Contain($"Origin: https://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: cross-site\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri($"https://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsOriginWhenStrictOriginWhenCrossOriginSuppressesDowngradeReferer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Contain("Origin: https://example.net\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: cross-site\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.StrictOriginWhenCrossOrigin,
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesPreloadDefaultsWithoutOriginForSameOriginSafeRequest()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/preload.js"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesModulePreloadDefaultsForSameOriginScript()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/modulepreload-entry.js"))
        {
            Kind = RequestKind.ModulePreload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesHttpRequestOptionsModulePreloadHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/modulepreload-helper.js"))
            .WithHttpsModulePreload();
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesModulePreloadHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/modulepreload-builder.js"))
            .WithModulePreload()
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/origin-page"))
            .Build();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesPrefetchDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-purpose: prefetch\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/prefetch-entry.js"))
        {
            Kind = RequestKind.Prefetch,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesHttpRequestOptionsPrefetchHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("sec-purpose: prefetch\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/prefetch-helper.js"))
            .WithHttpsPrefetch();
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesPrefetchHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("sec-purpose: prefetch\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/prefetch-builder.js"))
            .WithPrefetch()
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/origin-page"))
            .Build();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesModuleScriptDefaultsForSameOriginScript()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/module-entry.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesHttpRequestOptionsModuleScriptHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/module-script-helper.js"))
            .WithHttpsModuleScript();
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesModuleScriptHelper()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/module-script-builder.js"))
            .WithModuleScript()
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/origin-page"))
            .Build();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesStyleDestinationForCssPreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/css,*/*;q=0.1\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: style\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/site.css"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesImageDestinationForImagePreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/hero.webp"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesFirefoxImagePreloadAcceptDefaults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: image/avif,image/webp,image/*,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Not.Contain("image/apng"));
                Assert.That(request.Head, Does.Not.Contain("image/svg+xml"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateFirefoxDesktop(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/image.webp"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{GetPort(listener)}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariImagePreloadAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("image/avif"));
                Assert.That(request.Head, Does.Not.Contain("image/webp"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/image.webp"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{GetPort(listener)}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesCorsModeForFontPreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: font\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/font.woff2"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesAudioDestinationForAudioPreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
                Assert.That(request.Head, Does.Contain("Range: bytes=0-\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: audio\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/intro.mp3"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesVideoDestinationForVideoPreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
                Assert.That(request.Head, Does.Contain("Range: bytes=0-\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: video\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/trailer.webm"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesTrackDestinationForSubtitlePreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: track\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/captions.vtt"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesManifestDestinationForManifestPreload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: manifest\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/site.webmanifest"))
        {
            Kind = RequestKind.Preload,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesExplicitWorkerContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: worker\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Worker,
                FetchMode = HttpsFetchMode.SameOrigin,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesStyleAcceptForExplicitStyleFetchContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/css,*/*;q=0.1\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: style\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.css"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Style,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesFirefoxImageAcceptForExplicitImageFetchContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: image/avif,image/webp,image/*,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesChromiumImageAcceptForExplicitImageFetchContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariStyleFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: style\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.css"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Style,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariImageFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsChromiumScriptFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsSafariScriptFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsFirefoxScriptFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsFontFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: font\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/app.woff2"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Font,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesHttpRequestOptionsForExplicitIframeFontPreloadContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: font\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-font.woff2"))
            .WithHttpsRequestKind(RequestKind.Preload)
            .WithHttpsBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Font,
                FetchMode = HttpsFetchMode.Cors,
            });
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/frame");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesExplicitIframeFontPreloadContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: font\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-font.woff2"))
            .WithRequestKind(RequestKind.Preload)
            .WithBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Font,
                FetchMode = HttpsFetchMode.Cors,
            })
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/frame"))
            .BuildHttpsRequest();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesExplicitIframeStyleContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: text/css,*/*;q=0.1\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: style\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-style.css"))
            .WithRequestKind(RequestKind.Fetch)
            .WithBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Style,
                FetchMode = HttpsFetchMode.NoCors,
            })
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/frame"))
            .BuildHttpsRequest();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsRequestBuilderUsesExplicitIframeImageContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: image\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = HttpsRequestBuilder.Create(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/frame-image.png"))
            .WithRequestKind(RequestKind.Fetch)
            .WithBrowserContext(new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Iframe,
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            })
            .WithReferrer(new Uri($"http://127.0.0.1:{port}/frame"))
            .BuildHttpsRequest();

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsAudioFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
                Assert.That(request.Head, Does.Contain("Range: bytes=0-\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: audio\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/audio.mp3"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Audio,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsVideoFetchAcceptConservative()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
                Assert.That(request.Head, Does.Contain("Range: bytes=0-\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: no-cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: video\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/video.mp4"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Video,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerInfersWorkerContextFromInitiatorType()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: worker\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Worker,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesExplicitSharedWorkerContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: sharedworker\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/shared-worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.SharedWorker,
                FetchMode = HttpsFetchMode.SameOrigin,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerInfersSharedWorkerContextFromInitiatorType()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: sharedworker\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/shared-worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.SharedWorker,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesCorsWorkerContextForModuleImport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: worker\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/worker-module.js\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/worker-dep.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Worker,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/worker-module.js");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesCorsSharedWorkerContextForModuleImport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: sharedworker\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/shared-worker-module.js\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/shared-worker-dep.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.SharedWorker,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/shared-worker-module.js");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerInfersServiceWorkerContextFromInitiatorType()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
                Assert.That(request.Head, Does.Contain("Service-Worker: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/service-worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.ServiceWorker,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerAppliesServiceWorkerDefaultsWithoutOriginForSameOriginSafeRequest()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/origin-page\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
                Assert.That(request.Head, Does.Contain("Service-Worker: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head.IndexOf("Service-Worker:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("sec-fetch-dest:", StringComparison.Ordinal)));
                Assert.That(request.Head.IndexOf("Referer:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("Service-Worker:", StringComparison.Ordinal)));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/service-worker.js"))
        {
            Kind = RequestKind.ServiceWorker,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsServiceWorkerMetadataSameOriginEvenWithCrossSiteReferrer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
                Assert.That(request.Head, Does.Contain("Service-Worker: script\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
                Assert.That(request.Head.IndexOf("Service-Worker:", StringComparison.Ordinal), Is.GreaterThan(request.Head.IndexOf("sec-fetch-dest:", StringComparison.Ordinal)));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/service-worker.js"))
        {
            Kind = RequestKind.ServiceWorker,
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerUsesOriginForExplicitServiceWorkerCorsImportContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("Accept: */*\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
                Assert.That(request.Head, Does.Contain($"Origin: http://127.0.0.1:{port}\r\n"));
                Assert.That(request.Head, Does.Contain($"Referer: http://127.0.0.1:{port}/sw-module.js\r\n"));
                Assert.That(request.Head, Does.Not.Contain("\r\npriority:"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/sw-module-dep.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.ServiceWorker,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/sw-module.js");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDerivesCrossSiteFetchContextFromReferrer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Contain("Origin: https://example.net\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: cross-site\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerSuppressesRefererAndOriginForSameOriginSafeFetchWithNoReferrerPolicy()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Not.Contain("\r\nOrigin:"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: same-origin\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-mode: cors\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-dest: empty\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
        };
        request.Headers.Referrer = new Uri($"http://127.0.0.1:{port}/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerKeepsOriginForCrossSiteFetchWhenNoReferrerPolicySuppressesReferer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = GetPort(listener);
        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Not.Contain("\r\nReferer:"));
                Assert.That(request.Head, Does.Contain("Origin: https://example.net\r\n"));
                Assert.That(request.Head, Does.Contain("sec-fetch-site: cross-site\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public void HttpsClientHandlerUsesRequestReferrerPolicyOverrideOverProfileDefault()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindows();
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://example.com/resource"))
        {
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
        };

        var effectivePolicy = HttpsClientHandler.ResolveEffectiveReferrerPolicy(request, profile.Headers);

        Assert.That(effectivePolicy, Is.EqualTo(ReferrerPolicyMode.NoReferrer));
    }

    [Test]
    public void HttpsClientHandlerFallsBackToProfileReferrerPolicyWhenRequestOverrideIsMissing()
    {
        var profile = BrowserProfileCatalog.CreateFirefoxDesktop();
        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://example.com/resource"));

        var effectivePolicy = HttpsClientHandler.ResolveEffectiveReferrerPolicy(request, profile.Headers);

        Assert.That(effectivePolicy, Is.EqualTo(ReferrerPolicyMode.StrictOriginWhenCrossOrigin));
    }

    [Test]
    public void DeriveRefererKeepsFullReferrerForSameOriginStrictOriginWhenCrossOrigin()
    {
        var requestUri = new Uri("https://example.com/resource");
        var sourceReferrer = new Uri("https://example.com/origin-page");

        var derivedReferrer = HttpsClientHandler.DeriveReferer(requestUri, sourceReferrer, ReferrerPolicyMode.StrictOriginWhenCrossOrigin);

        Assert.That(derivedReferrer, Is.EqualTo(sourceReferrer));
    }

    [Test]
    public void DeriveRefererTrimsCrossOriginToOriginForStrictOriginWhenCrossOrigin()
    {
        var requestUri = new Uri("https://api.example.net/resource");
        var sourceReferrer = new Uri("https://example.com/origin-page");

        var derivedReferrer = HttpsClientHandler.DeriveReferer(requestUri, sourceReferrer, ReferrerPolicyMode.StrictOriginWhenCrossOrigin);

        Assert.That(derivedReferrer, Is.Not.Null);
        Assert.That(derivedReferrer!.OriginalString, Is.EqualTo("https://example.com/"));
    }

    [Test]
    public void DeriveRefererSuppressesDowngradeForStrictOriginWhenCrossOrigin()
    {
        var requestUri = new Uri("http://example.com/resource");
        var sourceReferrer = new Uri("https://example.com/origin-page");

        var derivedReferrer = HttpsClientHandler.DeriveReferer(requestUri, sourceReferrer, ReferrerPolicyMode.StrictOriginWhenCrossOrigin);

        Assert.That(derivedReferrer, Is.Null);
    }

    [Test]
    public void DeriveRefererSuppressesCrossOriginTargetsForSameOriginPolicy()
    {
        var requestUri = new Uri("https://example.net/resource");
        var sourceReferrer = new Uri("https://example.com/origin-page");

        var derivedReferrer = HttpsClientHandler.DeriveReferer(requestUri, sourceReferrer, ReferrerPolicyMode.SameOrigin);

        Assert.That(derivedReferrer, Is.Null);
    }

    [Test]
    public void GetSecFetchSiteTreatsRegistrableDomainSubdomainsAsSameSite()
    {
        var requestUri = new Uri("https://api.example.co.uk/data");
        var referrer = new Uri("https://www.example.co.uk/page");

        var site = InvokeGetSecFetchSite(requestUri, referrer, RequestKind.Fetch);

        Assert.That(site, Is.EqualTo("same-site"));
    }

    [Test]
    public void GetSecFetchSiteTreatsDifferentRegistrableDomainsAsCrossSite()
    {
        var requestUri = new Uri("https://api.example.co.uk/data");
        var referrer = new Uri("https://www.example.net/page");

        var site = InvokeGetSecFetchSite(requestUri, referrer, RequestKind.Fetch);

        Assert.That(site, Is.EqualTo("cross-site"));
    }

    [Test]
    public void GetSecFetchSiteTreatsDifferentSchemesAsCrossSite()
    {
        var requestUri = new Uri("https://api.example.co.uk/data");
        var referrer = new Uri("http://www.example.co.uk/page");

        var site = InvokeGetSecFetchSite(requestUri, referrer, RequestKind.Fetch);

        Assert.That(site, Is.EqualTo("cross-site"));
    }

    [Test]
    public void CreateRequestContextSnapshotCarriesExplicitFormSubmissionHint()
    {
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://example.com/submit"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = true,
            },
        };
        request.Headers.Referrer = new Uri("https://example.com/form");

        var profile = BrowserProfileCatalog.CreateEdgeDesktopWindows();
        var snapshot = InvokeCreateRequestContextSnapshot(request, profile, RequestKind.Navigation);

        Assert.Multiple(() =>
        {
            Assert.That(GetPropertyValue<bool>(snapshot, "IsFormSubmission"), Is.True);
            Assert.That(GetPropertyValue<bool>(snapshot, "IsReload"), Is.False);
            Assert.That(GetPropertyValue<string>(snapshot, "SecFetchMode"), Is.EqualTo("navigate"));
            Assert.That(GetPropertyValue<bool>(snapshot, "IsUserActivated"), Is.True);
        });
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotOverrideExplicitHeadersWhenBrowserProfileIsSet()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(request.Head, Does.Contain("User-Agent: CustomAgent/1.0\r\n"));
                Assert.That(request.Head, Does.Not.Contain("Edg/131.0.0.0"));
                Assert.That(request.Head, Does.Contain("Accept: application/json\r\n"));
                Assert.That(request.Head, Does.Contain("Accept-Language: ru-RU,ru;q=0.9\r\n"));
                Assert.That(request.Head, Does.Contain("Connection: upgrade\r\n"));
            });

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = Atom.Net.Https.Profiles.BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/explicit-headers"));
        request.Headers.TryAddWithoutValidation("User-Agent", "CustomAgent/1.0");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9");
        request.Headers.TryAddWithoutValidation("Connection", "upgrade");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var responseMessage = await client.SendAsync(request).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(body, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public void BuildConnectionOptionsCarriesProfileTransportSettings()
    {
        static bool Validate(HttpRequestMessage _, X509Certificate2? __, X509Chain? ___, SslPolicyErrors ____) => true;

        var profile = new BrowserProfile
        {
            DisplayName = "Custom Transport Profile",
            UserAgent = "CustomAgent/1.0",
            Tcp = new TcpSettings
            {
                IsNagleDisabled = false,
                UseHappyEyeballsAlternating = false,
                AttemptTimeout = TimeSpan.FromMilliseconds(125),
                ConnectTimeout = TimeSpan.FromSeconds(9),
            },
            Tls = new TlsSettings
            {
                HandshakeTimeout = TimeSpan.FromMilliseconds(275),
                Delay = TimeSpan.FromMilliseconds(15),
            },
            Headers = new BrowserHeaderProfile(),
        };

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = profile,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            CheckCertificateRevocationList = true,
            ServerCertificateCustomValidationCallback = Validate,
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://example.com/test"));
        var options = InvokeBuildConnectionOptions(handler, request);

        var profileTcp = GetPropertyValue<TcpSettings?>(options, "ProfileTcpSettings");
        var profileTls = GetPropertyValue<TlsSettings?>(options, "ProfileTlsSettings");
        var connectTimeout = GetPropertyValue<TimeSpan>(options, "ConnectTimeout");

        Assert.Multiple(() =>
        {
            Assert.That(connectTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(profileTcp.HasValue, Is.True);
            Assert.That(profileTcp!.Value.IsNagleDisabled, Is.False);
            Assert.That(profileTcp.Value.UseHappyEyeballsAlternating, Is.False);
            Assert.That(profileTcp.Value.AttemptTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
            Assert.That(profileTcp.Value.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(profileTls.HasValue, Is.True);
            Assert.That(profileTls!.Value.HandshakeTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(275)));
            Assert.That(profileTls.Value.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(15)));
            Assert.That(profileTls.Value.CheckCertificateRevocationList, Is.True);
            Assert.That(profileTls.Value.ServerCertificateValidationCallback, Is.Not.Null);
        });
    }

    [Test]
    public async Task HttpsClientHandlerReusesSingleTcpConnectionForSequentialRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = Task.Run(async () =>
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /one HTTP/1.1\r\n"));
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /two HTTP/1.1\r\n"));
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler();
        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotReuseConnectionWhenIdleTimeoutElapsed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = RunServerAsync(listener, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        }, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.Zero,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(2));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotReuseConnectionWhenLifetimeElapsed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = RunServerAsync(listener, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        }, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            PooledConnectionLifetime = TimeSpan.Zero,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(2));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDirectSendHonorsMaxConnectionsPerServerForConcurrentRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var firstRequestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /one HTTP/1.1\r\n"));
            firstRequestSeen.SetResult();

            await releaseFirstResponse.Task.ConfigureAwait(false);
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /two HTTP/1.1\r\n"));
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        var firstTask = InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/one")), CancellationToken.None);

        await firstRequestSeen.Task.ConfigureAwait(false);

        var secondTask = InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/two")), CancellationToken.None);
        await Within(Task.Delay(150)).ConfigureAwait(false);

        Assert.That(listener.Pending(), Is.False);

        releaseFirstResponse.SetResult();

        var firstResponseMessage = await firstTask.ConfigureAwait(false);
        var secondResponseMessage = await secondTask.ConfigureAwait(false);

        Assert.That(await firstResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerReusesSingleConnectionAcrossLongSequentialSeries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        const int requestCount = 12;
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            for (var i = 0; i < requestCount; i++)
            {
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                Assert.That(request.Head, Does.StartWith($"GET /series/{i} HTTP/1.1\r\n"));

                var close = i == requestCount - 1 ? "Connection: close\r\n" : string.Empty;
                var body = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{close}\r\n{body}");
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        for (var i = 0; i < requestCount; i++)
        {
            var response = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/series/{i}")).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(body, Is.EqualTo(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDirectSendProcessesBurstThroughSingleLeasedConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        const int requestCount = 6;
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            for (var i = 0; i < requestCount; i++)
            {
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                Assert.That(request.Head, Does.StartWith($"GET /burst/{i} HTTP/1.1\r\n"));

                var close = i == requestCount - 1 ? "Connection: close\r\n" : string.Empty;
                var body = $"b{i}";
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{close}\r\n{body}");
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        var tasks = Enumerable.Range(0, requestCount)
            .Select(index => InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/burst/{index}")), CancellationToken.None))
            .ToArray();

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

        for (var i = 0; i < responses.Length; i++)
        {
            var body = await responses[i].Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(body, Is.EqualTo($"b{i}"));
        }

        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    private static string InvokeGetSecFetchSite(Uri? requestUri, Uri? referrer, RequestKind requestKind, HttpsRequestDestination destination = HttpsRequestDestination.Empty)
    {
        var method = typeof(HttpsClientHandler).GetMethod("GetSecFetchSite", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GetSecFetchSite not found.");
        var destinationType = typeof(HttpsClientHandler).GetNestedType("RequestDestination", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RequestDestination enum not found.");
        var destinationValue = Enum.Parse(destinationType, destination.ToString(), ignoreCase: false);

        return (string)(method.Invoke(obj: null, new object?[] { requestUri, referrer, requestKind, destinationValue })
            ?? throw new InvalidOperationException("GetSecFetchSite invocation returned null."));
    }

    private static int GetPort(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static TcpListener CreateLocalhostListener()
    {
        var listener = new TcpListener(IPAddress.IPv6Any, 0);

        try { listener.Server.DualMode = true; } catch { }

        listener.Start();
        return listener;
    }

    private static async Task RunTlsServerAsync(TcpListener listener, X509Certificate2 certificate, Func<SslStream, Task> handler)
    {
        using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
        using var networkStream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
        using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);

        await Within(sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12,
            ClientCertificateRequired = false,
        })).ConfigureAwait(false);

        await handler(sslStream).ConfigureAwait(false);
    }

    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static async Task<HttpsResponseMessage> InvokeSendInternalAsync(HttpsClientHandler handler, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = typeof(HttpsClientHandler).GetMethod("SendInternalAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SendInternalAsync not found.");

        var task = (Task<HttpsResponseMessage>?)method.Invoke(handler, [request, cancellationToken])
            ?? throw new InvalidOperationException("SendInternalAsync invocation returned null.");

        return await task.ConfigureAwait(false);
    }

    private static object InvokeBuildConnectionOptions(HttpsClientHandler handler, HttpsRequestMessage request)
    {
        var method = typeof(HttpsClientHandler).GetMethod("BuildConnectionOptions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildConnectionOptions not found.");

        return method.Invoke(handler, [request])
            ?? throw new InvalidOperationException("BuildConnectionOptions invocation returned null.");
    }

    private static object InvokeCreateRequestContextSnapshot(HttpsRequestMessage request, BrowserProfile profile, RequestKind requestKind)
    {
        var method = typeof(HttpsClientHandler).GetMethod("CreateRequestContextSnapshot", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateRequestContextSnapshot not found.");

        return method.Invoke(obj: null, [request, profile, requestKind])
            ?? throw new InvalidOperationException("CreateRequestContextSnapshot invocation returned null.");
    }

    private static T GetPropertyValue<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} not found.");

        return (T)(property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property {propertyName} returned null."));
    }

    private static async Task RunServerAsync(TcpListener listener, params Func<System.Net.Sockets.NetworkStream, Task>[] handlers)
    {
        foreach (var handler in handlers)
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
            await handler(stream).ConfigureAwait(false);
        }
    }

    private static async Task<(string Head, byte[] Body)> ReadRequestAsync(Stream stream)
    {
        var headerBytes = await ReadUntilAsync(stream, HeaderTerminator).ConfigureAwait(false);
        var head = Encoding.ASCII.GetString(headerBytes);
        var contentLength = ParseContentLength(head);
        var body = contentLength > 0 ? await ReadExactAsync(stream, contentLength).ConfigureAwait(false) : [];
        return (head, body);
    }

    private static int ParseContentLength(string head)
    {
        foreach (var line in head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
            return int.Parse(line[(line.IndexOf(':') + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        return 0;
    }

    private static async Task<byte[]> ReadUntilAsync(Stream stream, byte[] marker)
    {
        using var buffer = new MemoryStream();
        var matched = 0;
        var one = new byte[1];

        while (matched < marker.Length)
        {
            var read = await Within(stream.ReadAsync(one)).ConfigureAwait(false);
            if (read is 0) throw new InvalidOperationException("Peer closed before header terminator.");

            buffer.WriteByte(one[0]);
            matched = one[0] == marker[matched] ? matched + 1 : one[0] == marker[0] ? 1 : 0;
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await Within(stream.ReadAsync(buffer.AsMemory(offset, length - offset))).ConfigureAwait(false);
            if (read is 0) throw new InvalidOperationException("Peer closed before full body.");
            offset += read;
        }

        return buffer;
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}