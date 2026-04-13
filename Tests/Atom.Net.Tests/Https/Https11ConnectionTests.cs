using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Https;

[TestFixture]
public sealed class Https11ConnectionTests
{
    private const int TestTimeoutMs = 10_000;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    [Test]
    public async Task SendAsyncWritesRequestLineAndParsesFixedLengthResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("GET /probe?x=1 HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: 127.0.0.1"));
            Assert.That(request.Head, Does.Not.Contain("Connection: close"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Type: text/plain\r\nX-Test: fixed\r\n\r\nhello"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/probe?x=1"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Version, Is.EqualTo(HttpVersion.Version11));
        Assert.That(body, Is.EqualTo("hello"));
        Assert.That(response.Headers.TryGetValues("X-Test", out var values), Is.True);
        Assert.That(values, Contains.Item("fixed"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);
        Assert.That(connection.Traffic.Sended, Is.GreaterThan(0UL));
        Assert.That(connection.Traffic.Received, Is.GreaterThan(0UL));

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWritesRequestBodyAndParsesChunkedResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("POST /submit HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Content-Length: 7"));
            Assert.That(Encoding.UTF8.GetString(request.Body), Is.EqualTo("payload"));

            var response = "HTTP/1.1 201 Created\r\nTransfer-Encoding: chunked\r\nContent-Type: text/plain\r\n\r\n5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{GetPort(listener)}/submit"))
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
        };

        var response = await connection.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body, Is.EqualTo("hello world"));

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncCanReuseConnectionForSequentialFramedResponses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /first HTTP/1.1\r\n"));

            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /second HTTP/1.1\r\n"));

            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var firstResponseMessage = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/first"))).ConfigureAwait(false);
        Assert.That(await firstResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);

        var secondResponseMessage = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/second"))).ConfigureAwait(false);
        Assert.That(await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(connection.IsDraining, Is.True);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWrapsMalformedStatusLineAsResponseException()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "BROKEN\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/broken"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.IsCompleted, Is.False);
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncCanUseCustomTls12Transport()
    {
        using var certificate = CreateLoopbackCertificate();
        using var listener = CreateLocalhostListener();

        var serverTask = RunTlsServerAsync(listener, certificate, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("GET /secure HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: localhost"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\nsecure"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        var options = CreateOptions(listener) with
        {
            Host = "localhost",
            IsHttps = true,
            SslProtocols = SslProtocols.Tls12,
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
        };

        await Within(connection.OpenAsync(options)).ConfigureAwait(false);

        var response = await Within(connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"https://localhost:{GetPort(listener)}/secure")))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("secure"));
        Assert.That(connection.IsSecure, Is.True);

        await Within(serverTask).ConfigureAwait(false);
    }

    [Test]
    public async Task BuildRequestHeadIncludesTrimmedRefererAndOriginForSecureCrossOriginFetch()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.StartWith("GET /fetch HTTP/1.1\r\n"));
            Assert.That(requestHead, Does.Contain("Host: localhost\r\n"));
            Assert.That(requestHead, Does.Contain("Referer: https://example.net/\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://example.net\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: cross-site\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsFullRefererAndOmitsOriginForSecureSameOriginFetch()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesSameSiteMetadataForSecureFetchWithinRegistrableDomain()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://api.example.co.uk/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri("https://www.example.co.uk/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Host: api.example.co.uk\r\n"));
            Assert.That(requestHead, Does.Contain("Referer: https://www.example.co.uk/\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://www.example.co.uk\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-site\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsOriginWhenNoReferrerPolicySuppressesCrossSiteFetchReferer()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
            Content = new StringContent("payload", Encoding.UTF8, "application/json"),
        };
        request.Headers.Referrer = new Uri("https://example.net/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Not.Contain("\r\nReferer:"));
            Assert.That(requestHead, Does.Contain("Origin: https://example.net\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: cross-site\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadSuppressesRefererAndOriginForSameOriginSafeFetchWithNoReferrerPolicy()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Not.Contain("\r\nReferer:"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsOriginWhenNoReferrerPolicySuppressesCrossSchemeSameHostFetchReferer()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("http://localhost/fetch"))
        {
            Kind = RequestKind.Fetch,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Not.Contain("\r\nReferer:"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: cross-site\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesExplicitIframeContextForNestedNavigation()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsUserActivated = false,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadEmitsSecFetchUserForExplicitIframeFormSubmission()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsFormSubmission = true,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-user: ?1\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadAlignsChromiumMultipartNavigationDefaultsToCapture()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        var boundary = "----atom-multipart-boundary";
        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/navigation"))
        {
            Kind = RequestKind.Navigation,
        };
        using var content = new MultipartFormDataContent(boundary);
        content.Add(new StringContent("hello-form", Encoding.UTF8), "message");
        request.Content = content;
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);
        var bodyBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: bodyBytes.Length));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain($"Content-Type: multipart/form-data; boundary=\"{boundary}\"\r\n"));
            Assert.That(requestHead, Does.Contain("Upgrade-Insecure-Requests: 1\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-user: ?1\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsSecFetchUserSuppressedWhenIframeFormSubmissionExplicitlySetsIsUserActivatedFalse()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/frame"))
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
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesExplicitManifestContextWithoutUriExtensionSniffing()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/resource"))
        {
            Kind = RequestKind.Preload,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Manifest,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: manifest\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesModulePreloadDefaults()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/modulepreload-entry.js"))
        {
            Kind = RequestKind.ModulePreload,
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: script\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesExplicitWorkerContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Worker,
                FetchMode = HttpsFetchMode.SameOrigin,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: worker\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesModuleScriptDefaults()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/document-module-entry.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: script\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesPrefetchDefaults()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/prefetch-entry.js"))
        {
            Kind = RequestKind.Prefetch,
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-purpose: prefetch\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesStyleAcceptForExplicitStyleFetchContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.css"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Style,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: text/css,*/*;q=0.1\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: style\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesFirefoxImageAcceptForExplicitImageFetchContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: image/avif,image/webp,image/*,*/*;q=0.8\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: image\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesChromiumImageAcceptForExplicitImageFetchContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: image\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsSafariStyleFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.css"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Style,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: style\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsSafariImageFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/logo.png"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Image,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: image\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsChromiumScriptFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: script\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsSafariScriptFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateSafariDesktopMacOs(),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: script\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsFirefoxScriptFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Script,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: script\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsFontFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/app.woff2"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Font,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: font\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsAudioFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/audio.mp3"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Audio,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
            Assert.That(requestHead, Does.Contain("Range: bytes=0-\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: audio\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsVideoFetchAcceptConservative()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/video.mp4"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Video,
                FetchMode = HttpsFetchMode.NoCors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: identity;q=1, *;q=0\r\n"));
            Assert.That(requestHead, Does.Contain("Range: bytes=0-\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: no-cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: video\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesExplicitSharedWorkerContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/shared-worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.SharedWorker,
                FetchMode = HttpsFetchMode.SameOrigin,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: sharedworker\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadInfersIframeContextFromNonTopLevelNavigation()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsTopLevelNavigation = false,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesSameOriginSiteForIframeNavigationWithoutReferrer()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsTopLevelNavigation = false,
            },
        };

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadInfersWorkerContextFromInitiatorType()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.Worker,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: worker\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadInfersServiceWorkerContextFromInitiatorType()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/service-worker.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                InitiatorType = HttpsRequestInitiatorType.ServiceWorker,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
            Assert.That(requestHead, Does.Contain("Service-Worker: script\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadUsesOriginForExplicitServiceWorkerCorsImportContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/sw-module-dep.js"))
        {
            Kind = RequestKind.Fetch,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.ServiceWorker,
                FetchMode = HttpsFetchMode.Cors,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/sw-module.js");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: */*\r\n"));
            Assert.That(requestHead, Does.Contain("Accept-Encoding: gzip, deflate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: serviceworker\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/sw-module.js\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\npriority:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadTreatsCrossSchemeFetchWithinRegistrableDomainAsCrossSite()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://api.example.co.uk/fetch"))
        {
            Kind = RequestKind.Fetch,
        };
        request.Headers.Referrer = new Uri("http://www.example.co.uk/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: http://www.example.co.uk/\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: http://www.example.co.uk\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: cross-site\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: cors\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: empty\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsNavigationAcceptForExplicitIframeContext()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/frame"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                Destination = HttpsRequestDestination.Iframe,
                IsUserActivated = false,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: iframe\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
        });
    }

    [Test]
    public async Task BuildRequestHeadEmitsOriginForSecureUnsafeNavigation()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/navigation"))
        {
            Kind = RequestKind.Navigation,
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Contain("Origin: https://localhost\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadSuppressesOriginForUnsafeNavigationWhenFormSubmissionIsExplicitlyFalse()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/navigation"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = false,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("Referer: https://localhost/origin-page\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-user: ?1\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadSuppressesRefererAndOriginForUnsafeNavigationWhenFormSubmissionIsFalseAndPolicyIsNoReferrer()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("https://localhost/navigation"))
        {
            Kind = RequestKind.Navigation,
            ReferrerPolicy = ReferrerPolicyMode.NoReferrer,
            Context = new HttpsBrowserRequestContext
            {
                IsFormSubmission = false,
            },
            Content = new StringContent("payload", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Not.Contain("\r\nReferer:"));
            Assert.That(requestHead, Does.Not.Contain("\r\nOrigin:"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: same-origin\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-user: ?1\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadKeepsOriginForUnsafeNavigationWhenStrictOriginWhenCrossOriginSuppressesDowngradeReferer()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Post, new Uri("http://localhost/navigation"))
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

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Not.Contain("\r\nReferer:"));
            Assert.That(requestHead, Does.Contain("Origin: https://example.net\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-site: cross-site\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-user: ?1\r\n"));
        });
    }

    [Test]
    public async Task BuildRequestHeadSuppressesSecFetchUserForReloadNavigation()
    {
        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"),
        };

        using var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://localhost/reload"))
        {
            Kind = RequestKind.Navigation,
            Context = new HttpsBrowserRequestContext
            {
                IsReload = true,
            },
        };
        request.Headers.Referrer = new Uri("https://localhost/origin-page");

        InvokeApplyBrowserProfileDefaults(handler, request);

        await using var connection = new Https11Connection();
        var requestHead = Encoding.ASCII.GetString(InvokeBuildRequestHead(connection, request, bodyLength: 0));

        Assert.Multiple(() =>
        {
            Assert.That(requestHead, Does.Contain("sec-fetch-mode: navigate\r\n"));
            Assert.That(requestHead, Does.Contain("sec-fetch-dest: document\r\n"));
            Assert.That(requestHead, Does.Not.Contain("\r\nsec-fetch-user:"));
            Assert.That(requestHead, Does.Contain("Upgrade-Insecure-Requests: 1\r\n"));
        });
    }

    [Test]
    public async Task SendAsyncWrapsResponseWhenBodyReadTimeoutExpires()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);

            var head = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(head).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);

            await Task.Delay(250).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        var options = CreateOptions(listener) with { ResponseBodyTimeout = TimeSpan.FromMilliseconds(100) };
        await connection.OpenAsync(options).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/slow-body"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.TypeOf<TimeoutException>());
        Assert.That(response.Exception!.Message, Does.Contain("response body"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncForHeadResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Head, new Uri($"http://127.0.0.1:{GetPort(listener)}/head"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncFor204ResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/nocontent"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncFor304ResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 304 Not Modified\r\nETag: abc\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/notmodified"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncParsesChunkTrailersAndKeepsConnectionReusable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var firstResponse = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\nX-Trailer: done\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var first = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/trailers"))).ConfigureAwait(false);
        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("hello"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);

        var second = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/after-trailers"))).ConfigureAwait(false);
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("ok"));
        Assert.That(connection.IsDraining, Is.True);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncSkipsInterim100ContinueAndReturnsFinalResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 100 Continue\r\nX-Interim: yes\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nfinal"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/continue"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("final"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWrapsResponseWhenHeadersExceedConfiguredLimit()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nX-Large: 1234567890\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        var options = CreateOptions(listener) with { MaxResponseHeadersBytes = 16 };
        await connection.OpenAsync(options).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/large-headers"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.Exception!.Message, Does.Contain("MaxResponseHeadersLength"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWraps101UpgradeAsUnsupportedResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/upgrade"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.Exception, Is.TypeOf<NotSupportedException>());
        Assert.That(response.Exception!.Message, Does.Contain("101 Switching Protocols"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public void CreateTcpSettingsUsesProfileSnapshotAndConnectTimeoutOverride()
    {
        var options = CreateOptions() with
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ProfileTcpSettings = new TcpSettings
            {
                IsNagleDisabled = false,
                UseHappyEyeballsAlternating = false,
                AttemptTimeout = TimeSpan.FromMilliseconds(150),
                ConnectTimeout = TimeSpan.FromSeconds(7),
            },
        };

        var settings = InvokeCreateTcpSettings(options);

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsNagleDisabled, Is.False);
            Assert.That(settings.UseHappyEyeballsAlternating, Is.False);
            Assert.That(settings.AttemptTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(150)));
            Assert.That(settings.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(settings.LocalEndPoint, Is.EqualTo(options.LocalEndPoint));
        });
    }

    [Test]
    public void CreateTlsSettingsUsesProfileSnapshotAndAppliesExplicitOverrides()
    {
        static bool Validate(X509Certificate2? _, X509Chain? __, SslPolicyErrors ___) => true;

        var options = CreateOptions() with
        {
            CheckCertificateRevocationList = true,
            ServerCertificateValidationCallback = Validate,
            ProfileTlsSettings = new TlsSettings
            {
                HandshakeTimeout = TimeSpan.FromMilliseconds(240),
                Delay = TimeSpan.FromMilliseconds(20),
                CheckCertificateRevocationList = false,
                CipherSuites = [CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256],
                Extensions = [new SessionTicketExtension()],
            },
        };

        var settings = InvokeCreateTlsSettings(options);

        Assert.Multiple(() =>
        {
            Assert.That(settings.HandshakeTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(240)));
            Assert.That(settings.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
            Assert.That(settings.CheckCertificateRevocationList, Is.True);
            Assert.That(settings.ServerCertificateValidationCallback, Is.Not.Null);
            Assert.That(settings.CipherSuites, Is.EqualTo(new[] { CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256 }));
            Assert.That(settings.Extensions.OfType<SessionTicketExtension>().Any(), Is.True);
            Assert.That(settings.Extensions.OfType<ServerNameTlsExtension>().Any(), Is.True);
            Assert.That(settings.Extensions.OfType<AlpnTlsExtension>().Any(), Is.True);
        });
    }

    [Test]
    public void CreateTlsSettingsMaterializesProfileExtensionOrderPerConnection()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindows();
        var options = CreateOptions() with
        {
            Host = "orders.example",
            ProfileTlsSettings = profile.Tls,
        };

        var settings = InvokeCreateTlsSettings(options);
        var extensionIds = settings.Extensions.Select(static extension => extension.Id).ToArray();
        var serverName = settings.Extensions.OfType<ServerNameTlsExtension>().Single();
        var profileServerName = profile.Tls.Extensions.OfType<ServerNameTlsExtension>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(extensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x0017, 0xff01, 0x000A, 0x000B, 0x0023, 0x0010, 0x000D, 0x002B }));
            Assert.That(serverName.HostName, Is.EqualTo("orders.example"));
            Assert.That(ReferenceEquals(serverName, profileServerName), Is.False);
            Assert.That(profileServerName.HostName, Is.Empty);
        });
    }

    private static HttpsConnectionOptions CreateOptions(TcpListener listener) => new()
    {
        Host = "127.0.0.1",
        Port = GetPort(listener),
        IsHttps = false,
        PreferredVersion = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        ResponseHeadersTimeout = TimeSpan.FromSeconds(3),
        RequestSendTimeout = TimeSpan.FromSeconds(3),
        ResponseBodyTimeout = TimeSpan.FromSeconds(3),
        SslProtocols = SslProtocols.None,
        CheckCertificateRevocationList = true,
        ServerCertificateValidationCallback = null,
        MaxResponseHeadersBytes = 64 * 1024,
        IdleTimeout = TimeSpan.FromSeconds(30),
        MaxConcurrentStreams = 1,
        AutoDecompression = false,
    };

    private static HttpsConnectionOptions CreateOptions() => new()
    {
        Host = "example.com",
        Port = 443,
        IsHttps = true,
        PreferredVersion = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        ResponseHeadersTimeout = TimeSpan.FromSeconds(3),
        RequestSendTimeout = TimeSpan.FromSeconds(3),
        ResponseBodyTimeout = TimeSpan.FromSeconds(3),
        SslProtocols = SslProtocols.None,
        CheckCertificateRevocationList = true,
        ServerCertificateValidationCallback = null,
        MaxResponseHeadersBytes = 64 * 1024,
        IdleTimeout = TimeSpan.FromSeconds(30),
        MaxConcurrentStreams = 1,
        AutoDecompression = false,
    };

    private static int GetPort(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static TcpListener CreateLocalhostListener()
    {
        var listener = new TcpListener(IPAddress.IPv6Any, 0);

        try { listener.Server.DualMode = true; } catch { }

        listener.Start();
        return listener;
    }

    private static async Task RunServerAsync(TcpListener listener, Func<System.Net.Sockets.NetworkStream, Task> handler)
    {
        using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
        using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
        await handler(stream).ConfigureAwait(false);
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

            var value = line[(line.IndexOf(':') + 1)..].Trim();
            return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static TcpSettings InvokeCreateTcpSettings(HttpsConnectionOptions options)
    {
        var method = typeof(Https11Connection).GetMethod("CreateTcpSettings", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateTcpSettings not found.");

        return (TcpSettings)(method.Invoke(obj: null, [options])
            ?? throw new InvalidOperationException("CreateTcpSettings invocation returned null."));
    }

    private static TlsSettings InvokeCreateTlsSettings(HttpsConnectionOptions options)
    {
        var method = typeof(Https11Connection).GetMethod("CreateTlsSettings", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateTlsSettings not found.");

        return (TlsSettings)(method.Invoke(obj: null, [options])
            ?? throw new InvalidOperationException("CreateTlsSettings invocation returned null."));
    }

    private static void InvokeApplyBrowserProfileDefaults(HttpsClientHandler handler, HttpsRequestMessage request)
    {
        var method = typeof(HttpsClientHandler).GetMethod("ApplyBrowserProfileDefaults", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ApplyBrowserProfileDefaults not found.");

        _ = method.Invoke(handler, [request]);
    }

    private static byte[] InvokeBuildRequestHead(Https11Connection connection, HttpsRequestMessage request, int bodyLength)
    {
        var method = typeof(Https11Connection).GetMethod("BuildRequestHead", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildRequestHead not found.");

        return (byte[])(method.Invoke(connection, [request, bodyLength])
            ?? throw new InvalidOperationException("BuildRequestHead invocation returned null."));
    }
}