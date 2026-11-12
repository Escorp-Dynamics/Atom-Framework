using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Сквозные проверки публичного API драйвера на настоящем движке Obscura: запуск, навигация,
/// DOM, ввод, cookie, перехват, callback'и, окна и вкладки. Без сети тесты пропускаются.
/// </summary>
[TestFixture]
[Category("Obscura")]
[NonParallelizable]
public sealed class WebDriverObscuraBrowserLiveTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);
    private LoopbackSite? site;

    [OneTimeSetUp]
    public async Task ProvisionAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ObscuraProfile.BinaryPathVariableName)))
            return;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await ObscuraBinaryProvisioner.EnsureAsync(ObscuraBuildVariant.Default, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or PlatformNotSupportedException)
        {
            Assert.Ignore("Obscura недоступна (нет сети или платформа не поддерживается): " + exception.Message);
        }
    }

    [SetUp]
    public void StartSite() => site = LoopbackSite.Start();

    [TearDown]
    public void StopSite() => site?.Dispose();

    [Test]
    public async Task LaunchNavigatesAndReadsDocument()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var url = site!.Url("/doc");

        List<string> lifecycle = [];
        page.DomContentLoaded += (_, _) => { lock (lifecycle) lifecycle.Add("dom"); };
        page.PageLoaded += (_, _) => { lock (lifecycle) lifecycle.Add("load"); };

        await page.NavigateAsync(url).ConfigureAwait(false);

        var title = await page.GetTitleAsync().ConfigureAwait(false);
        var currentUrl = await page.GetUrlAsync().ConfigureAwait(false);
        var content = await page.GetContentAsync().ConfigureAwait(false);
        var sum = await page.EvaluateAsync<string>("return String(1 + 2)").ConfigureAwait(false);
        var awaited = await page.EvaluateAsync<string>("return await new Promise(r => setTimeout(() => r('later'), 20))").ConfigureAwait(false);
        await WaitUntilAsync(() => { lock (lifecycle) return lifecycle.Contains("load"); }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("Obscura Doc"));
            Assert.That(currentUrl, Is.EqualTo(url));
            Assert.That(content, Does.Contain("id=\"marker\""));
            Assert.That(sum, Is.EqualTo("3"));
            Assert.That(awaited, Is.EqualTo("later"));
            Assert.That(lifecycle, Does.Contain("dom"));
            Assert.That(lifecycle, Does.Contain("load"));
        });
    }

    [Test]
    public async Task ScriptErrorSurfacesAsException()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var exception = await AsyncAssert.CatchAsync(async () => await page.EvaluateAsync("throw new Error('boom-obscura')").ConfigureAwait(false)).ConfigureAwait(false);
        Assert.That(exception.ToString(), Does.Contain("boom-obscura"));
    }

    [Test]
    public async Task FindsElementsWaitsAndReadsProperties()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var marker = await page.GetElementAsync("#marker").ConfigureAwait(false);
        var items = (await page.GetElementsAsync("li").ConfigureAwait(false)).ToArray();
        var missing = await page.GetElementAsync("#nope").ConfigureAwait(false);

        await page.EvaluateAsync("setTimeout(() => { const d = document.createElement('div'); d.id = 'late'; d.textContent = 'late-text'; document.body.appendChild(d); }, 200)").ConfigureAwait(false);
        var late = await page.WaitForElementAsync("#late", WaitForElementKind.Visible, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var hidden = await page.WaitForElementAsync("#hidden", WaitForElementKind.Visible, TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(marker, Is.Not.Null);
            Assert.That(await marker!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("obscura-marker"));
            Assert.That(await marker.GetPropertyAsync("id").ConfigureAwait(false), Is.EqualTo("marker"));
            Assert.That(items, Has.Length.EqualTo(3));
            Assert.That(missing, Is.Null);
            Assert.That(late, Is.Not.Null);
            Assert.That(await late!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("late-text"));
            Assert.That(hidden, Is.Null);
        });
    }

    [Test]
    public async Task ShadowRootAndFrameQueriesWork()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        await page.EvaluateAsync(
            """
            const host = document.createElement('div');
            host.id = 'shadow-host';
            document.body.appendChild(host);
            host.attachShadow({ mode: 'open' }).innerHTML = '<span id="inner">inner-text</span>';
            """).ConfigureAwait(false);

        var shadowRoot = await page.GetShadowRootAsync("#shadow-host").ConfigureAwait(false);
        Assert.That(shadowRoot, Is.Not.Null);
        var inner = await shadowRoot!.GetElementAsync("#inner").ConfigureAwait(false);

        Assert.That(inner, Is.Not.Null);
        Assert.That(await inner!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("inner-text"));
    }

    [Test]
    public async Task ClickAndTypeProduceTrustedInput()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/form")).ConfigureAwait(false);

        var button = await page.GetElementAsync("#button").ConfigureAwait(false);
        var input = await page.GetElementAsync("#input").ConfigureAwait(false);
        Assert.That(button, Is.Not.Null);
        Assert.That(input, Is.Not.Null);

        await button!.ClickAsync().ConfigureAwait(false);
        await input!.TypeAsync("Привет, obscura 1").ConfigureAwait(false);
        await input.PressAsync(ConsoleKey.Backspace).ConfigureAwait(false);

        var clicks = await page.EvaluateAsync<string>("return String(window.__clicks)").ConfigureAwait(false);
        var trusted = await page.EvaluateAsync<string>("return String(window.__trusted)").ConfigureAwait(false);
        var value = await input.GetValueAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo("1"));
            Assert.That(trusted, Is.EqualTo("true"));
            Assert.That(value, Is.EqualTo("Привет, obscura "));
        });
    }

    [Test]
    public async Task ScreenshotReturnsPng()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var screenshot = (await page.GetScreenshotAsync().ConfigureAwait(false)).ToArray();

        Assert.That(screenshot.Length, Is.GreaterThan(8));
        Assert.That(screenshot.AsSpan(1, 3).SequenceEqual("PNG"u8), Is.True);
    }

    [Test]
    public async Task CookiesRoundTripAndStayIsolatedPerPage()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        await page.SetCookiesAsync([new Cookie("session", "alpha", "/", "127.0.0.1")]).ConfigureAwait(false);
        var cookies = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var documentCookie = await page.EvaluateAsync<string>("return document.cookie").ConfigureAwait(false);

        var sibling = (WebPage)await ((WebWindow)browser.CurrentWindow).OpenPageAsync().ConfigureAwait(false);
        await sibling.NavigateAsync(site.Url("/doc")).ConfigureAwait(false);
        var siblingCookies = (await sibling.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();

        await page.ClearAllCookiesAsync().ConfigureAwait(false);
        var cleared = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(cookies.Any(static cookie => cookie.Name == "session" && cookie.Value == "alpha"), Is.True);
            Assert.That(documentCookie, Does.Contain("session=alpha"));
            Assert.That(siblingCookies.Any(static cookie => cookie.Name == "session"), Is.False);
            Assert.That(cleared, Is.Empty);
        });
    }

    [Test]
    public async Task ConsoleMessagesAreRelayed()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        List<ConsoleMessageEventArgs> messages = [];
        page.Console += (_, args) => { lock (messages) messages.Add(args); };

        await page.EvaluateAsync("console.warn('obscura-console', 42)").ConfigureAwait(false);
        await WaitUntilAsync(() => { lock (messages) return messages.Count > 0; }).ConfigureAwait(false);

        ConsoleMessageEventArgs message;
        lock (messages)
            message = messages[0];

        Assert.Multiple(() =>
        {
            Assert.That(message.Level, Is.EqualTo(ConsoleMessageLevel.Warn));
            Assert.That(message.Message, Does.Contain("obscura-console"));
        });
    }

    [Test]
    public async Task RequestInterceptionContinuesAbortsAndFulfills()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var continueUrl = site.Url("/api/continue");
        var abortUrl = site.Url("/api/abort");
        var fulfillUrl = site.Url("/api/fulfill");
        List<Uri> seen = [];

        page.Request += async (_, args) =>
        {
            var uri = args.Request.RequestUri!;
            lock (seen) seen.Add(uri);

            if (uri == abortUrl)
            {
                await args.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (uri == fulfillUrl)
            {
                await args.FulfillAsync(new HttpsResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("fulfilled-by-driver", Encoding.UTF8, "text/plain"),
                }).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await page.SetRequestInterceptionAsync(true, ["*/api/*"]).ConfigureAwait(false);

        var continued = await FetchAsync(page, continueUrl).ConfigureAwait(false);
        var aborted = await FetchAsync(page, abortUrl).ConfigureAwait(false);
        var fulfilled = await FetchAsync(page, fulfillUrl).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(continued, Is.EqualTo("ok:/api/continue"));
            Assert.That(aborted, Does.StartWith("error:"));
            Assert.That(fulfilled, Is.EqualTo("fulfilled-by-driver"));
            Assert.That(site.HasSeen("/api/abort"), Is.False);
            Assert.That(site.HasSeen("/api/fulfill"), Is.False);
            Assert.That(seen, Does.Contain(continueUrl));
        });
    }

    [Test]
    public async Task HtmlNavigationServesDocumentUnderTargetOrigin()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var url = site!.Url("/virtual");

        await page.NavigateAsync(url, "<html><head><title>Injected</title></head><body><p id='v'>virtual-body</p></body></html>").ConfigureAwait(false);

        var title = await page.GetTitleAsync().ConfigureAwait(false);
        var origin = await page.EvaluateAsync<string>("return location.href").ConfigureAwait(false);
        var element = await page.GetElementAsync("#v").ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(title, Is.EqualTo("Injected"));
            Assert.That(origin, Is.EqualTo(url.AbsoluteUri));
            Assert.That(element, Is.Not.Null);
            Assert.That(await element!.GetInnerTextAsync().ConfigureAwait(false), Is.EqualTo("virtual-body"));
            Assert.That(site.HasSeen("/virtual"), Is.False);
        });
    }

    [Test]
    public async Task CallbackSubscriptionRelaysCalls()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        List<CallbackEventArgs> callbacks = [];
        List<CallbackFinalizedEventArgs> finalized = [];
        page.Callback += (_, args) =>
        {
            lock (callbacks) callbacks.Add(args);
            return ValueTask.CompletedTask;
        };
        page.CallbackFinalized += (_, args) => { lock (finalized) finalized.Add(args); };

        await page.SubscribeAsync("app.ready").ConfigureAwait(false);
        _ = await page.EvaluateAsync("return app.ready('alpha', 7, true)").ConfigureAwait(false);
        await WaitUntilAsync(() => { lock (callbacks) lock (finalized) return callbacks.Count > 0 && finalized.Count > 0; }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(callbacks[0].Name, Is.EqualTo("app.ready"));
            Assert.That(callbacks[0].Args, Is.EqualTo(new object?[] { "alpha", 7L, true }));
            Assert.That(finalized[0].Name, Is.EqualTo("app.ready"));
        });
    }

    [Test]
    public async Task WindowsAndPagesOpenAndCloseIndependently()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var firstPage = (WebPage)browser.CurrentPage;
        await firstPage.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondPage = (WebPage)secondWindow.CurrentPage;
        await secondPage.NavigateAsync(site.Url("/form")).ConfigureAwait(false);
        var thirdPage = (WebPage)await secondWindow.OpenPageAsync().ConfigureAwait(false);
        await thirdPage.NavigateAsync(site.Url("/doc?third")).ConfigureAwait(false);

        var secondTitle = await secondPage.GetTitleAsync().ConfigureAwait(false);
        var thirdUrl = await thirdPage.GetUrlAsync().ConfigureAwait(false);

        await thirdPage.CloseAsync().ConfigureAwait(false);
        await secondWindow.CloseAsync().ConfigureAwait(false);

        var firstTitle = await firstPage.GetTitleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(secondTitle, Is.EqualTo("Obscura Form"));
            Assert.That(thirdUrl, Is.EqualTo(site.Url("/doc?third")));
            Assert.That(firstTitle, Is.EqualTo("Obscura Doc"));
        });
    }

    [Test]
    public async Task WindowProxyRunsInSeparateProcessWithoutLeakingToOtherWindows()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var firstPage = (WebPage)browser.CurrentPage;

        // Прокси на заведомо закрытый порт: если окно его получило, навигация обязана упасть.
        var deadProxy = new WebProxy(new Uri($"http://127.0.0.1:{ReserveClosedPort()}"));
        var proxiedWindow = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings { Proxy = deadProxy }).ConfigureAwait(false);
        var proxiedPage = (WebPage)proxiedWindow.CurrentPage;

        _ = await AsyncAssert.CatchAsync(async () => await proxiedPage.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false)).ConfigureAwait(false);

        await firstPage.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);
        Assert.That(await firstPage.GetTitleAsync().ConfigureAwait(false), Is.EqualTo("Obscura Doc"));
    }

    [Test]
    public async Task ProxiedWindowProcessStopsWhenWindowCloses()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var baseline = CountObscuraServers();

        var proxy = new WebProxy(new Uri($"http://127.0.0.1:{ReserveClosedPort()}"));
        var window = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings { Proxy = proxy }).ConfigureAwait(false);
        var withWindow = CountObscuraServers();

        await window.CloseAsync().ConfigureAwait(false);
        var deadline = DateTime.UtcNow + EventTimeout;
        while (CountObscuraServers() > baseline && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(withWindow, Is.EqualTo(baseline + 1));
            Assert.That(CountObscuraServers(), Is.EqualTo(baseline));
        });
    }

    [Test]
    public async Task PageCannotSeeDriverBindingOrForgeCallbacks()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);

        var exposed = await page.EvaluateAsync<string>(
            "return String(Object.getOwnPropertyNames(globalThis).filter(n => /^__[0-9a-f]{32}$/.test(n) || n.includes('atomWebDriver')).length)").ConfigureAwait(false);

        // Ещё одна навигация и вычисление — драйвер продолжает работать.
        await page.NavigateAsync(site.Url("/form")).ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exposed, Is.EqualTo("0"));
            Assert.That(title, Is.EqualTo("Obscura Form"));
        });
    }

    [Test]
    public async Task ActivateExclusiveIsNoOpWithoutDisplay()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;

        // Без дисплея и ОС-мыши обычный путь бросил бы InvalidOperationException.
        await page.ActivateExclusiveAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
        Assert.Pass();
    }

    [Test]
    public async Task HtmlNavigationWithFragmentStillServesDocument()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;
        var url = site!.Url("/virtual-fragment#section");

        await page.NavigateAsync(url, "<html><head><title>Fragment</title></head><body>f</body></html>").ConfigureAwait(false);

        Assert.Multiple(async () =>
        {
            Assert.That(await page.GetTitleAsync().ConfigureAwait(false), Is.EqualTo("Fragment"));
            Assert.That(site.HasSeen("/virtual-fragment"), Is.False);
        });
    }

    [Test]
    public async Task RequestInterceptionCoversIframeDocumentAndItsRequests()
    {
        await using var browser = await LaunchAsync().ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;

        var childUrl = site!.Url("/child");
        var childFetchUrl = site.Url("/api/fromchild");
        List<(Uri Url, bool IsNavigate)> seen = [];

        page.Request += async (_, args) =>
        {
            var uri = args.Request.RequestUri!;
            lock (seen) seen.Add((uri, args.IsNavigate));

            if (uri == childUrl)
            {
                await args.FulfillAsync(new HttpsResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<html><head><title>FC</title></head><body><p id='c'>fulfilled-child</p><script>fetch('/api/fromchild').then(r => r.text()).then(t => document.body.dataset.r = t)</script></body></html>",
                        Encoding.UTF8,
                        "text/html"),
                }).ConfigureAwait(false);
                return;
            }

            if (uri == childFetchUrl)
            {
                await args.FulfillAsync(new HttpsResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("intercepted-in-frame", Encoding.UTF8, "text/plain"),
                }).ConfigureAwait(false);
                return;
            }

            await args.ContinueAsync().ConfigureAwait(false);
        };

        await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);
        await page.NavigateAsync(site.Url("/parent")).ConfigureAwait(false);

        string? state = null;
        var deadline = DateTime.UtcNow + EventTimeout;
        while (DateTime.UtcNow < deadline)
        {
            state = await page.EvaluateAsync<string>(
                "const d = document.getElementById('f')?.contentDocument; return JSON.stringify({ c: d?.getElementById('c')?.textContent ?? null, r: d?.body?.dataset?.r ?? null })").ConfigureAwait(false);
            if (state?.Contains("intercepted-in-frame", StringComparison.Ordinal) == true)
                break;

            await Task.Delay(50).ConfigureAwait(false);
        }

        (Uri Url, bool IsNavigate)[] snapshot;
        lock (seen)
            snapshot = [.. seen];

        Assert.Multiple(() =>
        {
            Assert.That(state, Does.Contain("fulfilled-child"));
            Assert.That(state, Does.Contain("intercepted-in-frame"));
            Assert.That(site.HasSeen("/child"), Is.False);
            Assert.That(site.HasSeen("/api/fromchild"), Is.False);
            Assert.That(snapshot.Single(item => item.Url == site.Url("/parent")).IsNavigate, Is.True);
            Assert.That(snapshot.Single(item => item.Url == childUrl).IsNavigate, Is.False);
        });
    }

    [Test]
    public async Task AuthenticatedProxyReceivesCredentials()
    {
        using var proxy = AuthProxy.Start("user", "p@ss:word");
        var credentials = new NetworkCredential("user", "p@ss:word");

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Profile = new ObscuraProfile { AllowPrivateNetwork = true },
            Proxy = new WebProxy(proxy.Address) { Credentials = credentials },
        }).ConfigureAwait(false);
        var page = (WebPage)browser.CurrentPage;

        await page.NavigateAsync(site!.Url("/doc")).ConfigureAwait(false);
        var title = await page.GetTitleAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("Obscura Doc"));
            Assert.That(proxy.AuthorizedRequests, Is.GreaterThan(0));
            Assert.That(proxy.RejectedRequests, Is.Zero);
        });
    }

    private static int CountObscuraServers()
        => System.Diagnostics.Process.GetProcesses().Count(static process =>
        {
            try
            {
                return process.ProcessName.Equals("obscura", StringComparison.Ordinal);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            finally
            {
                process.Dispose();
            }
        });

    private static int ReserveClosedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static ValueTask<WebBrowser> LaunchAsync()
        => WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Profile = new ObscuraProfile { AllowPrivateNetwork = true },
        });

    private static async Task<string?> FetchAsync(WebPage page, Uri url)
        => await page.EvaluateAsync<string>(
            "return await fetch('" + url.AbsoluteUri + "').then(r => r.text()).catch(e => 'error:' + e)").ConfigureAwait(false);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + EventTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Ожидаемое событие не пришло вовремя.");

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// HTTP-прокси с Basic-авторизацией: пересылает обычные (не CONNECT) запросы и считает,
    /// сколько пришло с верными учётными данными и сколько отклонено.
    /// </summary>
    private sealed class AuthProxy : IDisposable
    {
        private readonly TcpListener listener;
        private readonly string expectedAuthorization;
        private int authorized;
        private int rejected;

        private AuthProxy(TcpListener listener, string expectedAuthorization)
        {
            this.listener = listener;
            this.expectedAuthorization = expectedAuthorization;
        }

        public Uri Address => new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");

        public int AuthorizedRequests => Volatile.Read(ref authorized);

        public int RejectedRequests => Volatile.Read(ref rejected);

        public static AuthProxy Start(string user, string password)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var proxy = new AuthProxy(listener, "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
            _ = Task.Run(proxy.AcceptLoopAsync);
            return proxy;
        }

        public void Dispose() => listener.Stop();

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream).ConfigureAwait(false);
                if (head is null)
                    return;

                var lines = head.Split("\r\n");
                var target = lines[0].Split(' ')[1];
                var authorization = lines.Skip(1)
                    .Where(static line => line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                    .Select(static line => line["Proxy-Authorization:".Length..].Trim())
                    .FirstOrDefault();

                if (!string.Equals(authorization, expectedAuthorization, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref rejected);
                    await stream.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"atom\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray()).ConfigureAwait(false);
                    return;
                }

                Interlocked.Increment(ref authorized);
                using var http = new HttpClient();
                using var response = await http.GetAsync(new Uri(target)).ConfigureAwait(false);
                var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
                var header = $"HTTP/1.1 {(int)response.StatusCode} OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
        }

        private static async Task<string?> ReadHeadAsync(Stream stream)
        {
            var buffer = new List<byte>();
            var one = new byte[1];
            while (buffer.Count < 65536)
            {
                if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
                    return null;

                buffer.Add(one[0]);
                if (buffer.Count >= 4 && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n')
                    return Encoding.ASCII.GetString([.. buffer]);
            }

            return null;
        }
    }

    /// <summary>
    /// Минимальный HTTP-сайт на loopback с несколькими страницами и журналом обращений.
    /// </summary>
    private sealed class LoopbackSite : IDisposable
    {
        private readonly HttpListener listener;
        private readonly HashSet<string> seenPaths = new(StringComparer.Ordinal);
        private readonly int port;

        private LoopbackSite(HttpListener listener, int port)
        {
            this.listener = listener;
            this.port = port;
        }

        public static LoopbackSite Start()
        {
            var port = ReserveFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var site = new LoopbackSite(listener, port);
            _ = Task.Run(site.AcceptLoopAsync);
            return site;
        }

        public Uri Url(string pathAndQuery) => new($"http://127.0.0.1:{port}{pathAndQuery}");

        public bool HasSeen(string path)
        {
            lock (seenPaths)
                return seenPaths.Contains(path);
        }

        public void Dispose() => listener.Close();

        private static int ReserveFreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        private async Task AcceptLoopAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var path = context.Request.Url!.AbsolutePath;
                lock (seenPaths)
                    seenPaths.Add(path);

                var (contentType, body) = Render(path);
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = contentType;
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        private static (string ContentType, string Body) Render(string path) => path switch
        {
            "/doc" => ("text/html; charset=utf-8", """
                <html><head><title>Obscura Doc</title></head><body>
                <p id="marker">obscura-marker</p>
                <ul><li>a</li><li>b</li><li>c</li></ul>
                <div id="hidden" style="display:none">hidden</div>
                </body></html>
                """),
            "/form" => ("text/html; charset=utf-8", """
                <html><head><title>Obscura Form</title></head><body>
                <button id="button" style="width:200px;height:60px">Go</button>
                <input id="input" style="width:300px" />
                <script>
                window.__clicks = 0; window.__trusted = false;
                document.getElementById('button').addEventListener('click', e => { window.__clicks++; window.__trusted = e.isTrusted; });
                </script>
                </body></html>
                """),
            "/parent" => ("text/html; charset=utf-8", "<html><head><title>Parent</title></head><body><iframe id=\"f\" src=\"/child\"></iframe></body></html>"),
            "/child" => ("text/html; charset=utf-8", "<html><head><title>Child</title></head><body><p id=\"c\">origin-child</p></body></html>"),
            _ when path.StartsWith("/api/", StringComparison.Ordinal) => ("text/plain", "ok:" + path),
            _ => ("text/html; charset=utf-8", "<html><head><title>Other</title></head><body>other</body></html>"),
        };
    }
}
