using System.Diagnostics;
using System.Linq;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Http;
using Atom.Net.Https.Profiles;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Сценарный дифференциальный capture-тест: одна страница порождает запросы ВСЕХ основных
/// типов, и каждый запрос настоящего браузера сравнивается с соответствующим запросом
/// нашего клиента на тех же capture-серверах.
/// </summary>
/// <remarks>
/// Типы в сценарии: навигация, subresources (style/script/img), iframe-навигация,
/// fetch GET и POST, redirect-цепочка (оба хопа), cross-origin CORS preflight и сам PUT.
/// Форма сравнения — структура: порядок и регистр имён заголовков плюс значения. Значения
/// версий (user-agent, sec-ch-ua-*) и accept-language агностичны: локальный браузер — не
/// версия из профиля, а его системная локаль не детерминирована.
/// </remarks>
[CancelAfter(180000)]
public sealed class BrowserCaptureScenarioTests
{
    private static readonly string[] ChromiumPaths =
    [
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/sbin/chromium",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable",
    ];

    private static readonly string[] FirefoxPaths =
    [
        "/usr/bin/firefox",
        "/usr/sbin/firefox",
    ];

    /// <summary>Заголовки, значения которых агностичны окружению и не сравниваются.</summary>
    private static readonly HashSet<string> ValueAgnosticHeaders = new(StringComparer.Ordinal)
    {
        "user-agent",
        "sec-ch-ua",
        "sec-ch-ua-mobile",
        "sec-ch-ua-platform",
        "accept-language",
    };

    private static readonly (string Method, string Path, string Label)[] SceneRequests =
    [
        ("GET", "/", "навигация"),
        ("GET", "/style", "subresource style"),
        ("GET", "/script", "subresource script"),
        ("GET", "/img", "subresource image"),
        ("GET", "/iframe", "iframe-навигация"),
        ("GET", "/fetch-get", "fetch GET"),
        ("POST", "/fetch-post", "fetch POST"),
        ("GET", "/redirect", "redirect: первый хоп"),
        ("GET", "/final", "redirect: второй хоп"),
    ];

    private static readonly (string Method, string Path, string Label)[] PreflightRequests =
    [
        ("OPTIONS", "/preflight", "CORS preflight"),
        ("PUT", "/preflight", "CORS: сам PUT"),
    ];

    [Test]
    public async Task AllRequestTypesMatchRealChromiumShape()
    {
        var chromium = FindExecutable(ChromiumPaths);
        if (chromium is null) Assert.Ignore("Chromium не найден в системе — capture-тест пропущен");

        await RunScenarioAsync(chromium, BrowserKind.Chromium);
    }

    [Test]
    public async Task AllRequestTypesMatchRealFirefoxShape()
    {
        var firefox = FindExecutable(FirefoxPaths);
        if (firefox is null) Assert.Ignore("Firefox не найден в системе — capture-тест пропущен");

        await RunScenarioAsync(firefox, BrowserKind.Firefox);
    }

    private enum BrowserKind
    {
        Chromium,
        Firefox,
    }

    private static (string Method, string Path)[] ToKeys((string Method, string Path, string Label)[] requests)
        => [.. requests.Select(static request => (request.Method, request.Path))];

    private static string? FindExecutable(string[] paths) => paths.FirstOrDefault(File.Exists);

    private static async Task RunScenarioAsync(string browserPath, BrowserKind kind)
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var userAgent = kind is BrowserKind.Chromium
            ? "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            : "Mozilla/5.0 (X11; Linux x86_64; rv:154.0) Gecko/20100101 Firefox/154.0";

        await using var preflightServer = new BrowserCaptureServer();
        var page = BuildScenarioPage(preflightServer.Origin);
        await using var sceneServer = new BrowserCaptureServer(page);

        // 1. Браузерная фаза: живой браузер грузит сценарную страницу.
        await LaunchBrowserAsync(browserPath, kind, sceneServer.Origin + "/", cancellationToken);
        await sceneServer.WaitAsync(ToKeys(SceneRequests), cancellationToken);
        await preflightServer.WaitAsync(ToKeys(PreflightRequests), cancellationToken);

        var browserScene = sceneServer.Snapshot();
        var browserPreflight = preflightServer.Snapshot();
        sceneServer.Reset();
        preflightServer.Reset();

        // 2. Клиентская фаза: тот же сценарий через наш клиент.
        var profile = BrowserProfileResolver.Resolve(userAgent);
        using var handler = new HttpsClientHandler { BrowserProfile = profile };
        using var client = new System.Net.Http.HttpClient(handler, disposeHandler: false);
        var pageUri = new Uri(sceneServer.Origin + "/");

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/", request =>
            {
                // Под virtual-time навигация Chromium — user-activated (Sec-Fetch-User шлётся),
                // Firefox в том же CLI-режиме остаётся не-activated (capture: заголовка нет).
                _ = request.WithHttpsRequestKind(RequestKind.Navigation)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext { IsUserActivated = kind is BrowserKind.Chromium });
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/style", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Style,
                        FetchMode = HttpsFetchMode.NoCors,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/script", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Script,
                        FetchMode = HttpsFetchMode.NoCors,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/img", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Image,
                        FetchMode = HttpsFetchMode.NoCors,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/iframe", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Navigation)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        IsTopLevelNavigation = false,
                        IsUserActivated = false,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/fetch-get", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Empty,
                        FetchMode = HttpsFetchMode.Cors,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Post, sceneServer.Origin + "/fetch-post", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Empty,
                        FetchMode = HttpsFetchMode.Cors,
                    });
                request.Headers.Referrer = pageUri;

                // Content-Type выставляет fetch API: text/plain-тело получает строгую форму
                // без пробела и в верхнем регистре кодировки.
                var content = new StringContent("x=1");
                content.Headers.ContentType = null;
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                request.Content = content;
            }, sceneServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Get, sceneServer.Origin + "/redirect", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Empty,
                        FetchMode = HttpsFetchMode.Cors,
                    });
                request.Headers.Referrer = pageUri;
            }, sceneServer, cancellationToken, expectMethod: "GET", expectPath: "/final");

        var preflightOrigin = preflightServer.Origin;
        await SendAsync(client, System.Net.Http.HttpMethod.Options, preflightOrigin + "/preflight", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Empty,
                        FetchMode = HttpsFetchMode.Cors,
                    });
                request.Headers.Referrer = pageUri;
                request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "PUT");
                request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "x-probe");
            }, preflightServer, cancellationToken);

        await SendAsync(client, System.Net.Http.HttpMethod.Put, preflightOrigin + "/preflight", request =>
            {
                _ = request.WithHttpsRequestKind(RequestKind.Fetch)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        Destination = HttpsRequestDestination.Empty,
                        FetchMode = HttpsFetchMode.Cors,
                    });
                request.Headers.Referrer = pageUri;
                request.Headers.TryAddWithoutValidation("X-Probe", "1");
            }, preflightServer, cancellationToken);

        // 3. Сравнение попарно по всем типам; расхождения копятся, чтобы один прогон
        // показал весь срез, а не первое отличие.
        var mismatches = new List<string>();

        foreach (var (method, path, label) in SceneRequests)
        {
            CollectShapeMismatches(
                browserScene[method + " " + path],
                sceneServer.Snapshot()[method + " " + path],
                label,
                mismatches);
        }

        foreach (var (method, path, label) in PreflightRequests)
        {
            CollectShapeMismatches(
                browserPreflight[method + " " + path],
                preflightServer.Snapshot()[method + " " + path],
                label,
                mismatches);
        }

        Assert.That(mismatches, Is.Empty, string.Join(Environment.NewLine + Environment.NewLine, mismatches));
    }

    /// <summary>Сценарная страница: subresources в разметке, fetch-запросы встроенным скриптом.</summary>
    private static string BuildScenarioPage(string preflightOrigin) => $$"""
        <!DOCTYPE html><html><head><meta charset="utf-8">
        <link rel="stylesheet" href="/style">
        <script src="/script"></script>
        </head><body>
        <img src="/img">
        <iframe src="/iframe"></iframe>
        <script>
        const postInit = {method:'POST',body:'x=1'};
        const probeHeaders = {'X-Probe':'1'};
        const probeInit = {method:'PUT',headers:probeHeaders};
        fetch('/fetch-get');
        fetch('/fetch-post',postInit);
        fetch('/redirect');
        fetch('{{preflightOrigin}}/preflight',probeInit);
        </script>
        </body></html>
        """;

    private static async Task LaunchBrowserAsync(string browserPath, BrowserKind kind, string url, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(browserPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var profileDirectory = Path.Combine(Path.GetTempPath(), "atom-net-scenario-" + Path.GetRandomFileName());
        Directory.CreateDirectory(profileDirectory);

        if (kind is BrowserKind.Chromium)
        {
            start.ArgumentList.Add("--headless=new");
            start.ArgumentList.Add("--disable-gpu");
            start.ArgumentList.Add("--no-sandbox");
            start.ArgumentList.Add("--user-data-dir=" + profileDirectory);
            // Сценарий переживает load: fetch-цепочка с redirect и preflight должна ДОГОВОРИТЬСЯ
            // до выхода браузера, иначе capture теряет вторые хопы недетерминированно.
            start.ArgumentList.Add("--virtual-time-budget=15000");
            start.ArgumentList.Add("--dump-dom");
            start.ArgumentList.Add(url);
        }
        else
        {
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--no-remote");
            start.ArgumentList.Add("--profile");
            start.ArgumentList.Add(profileDirectory);
            start.ArgumentList.Add("--screenshot");
            start.ArgumentList.Add(Path.Combine(profileDirectory, "shot.png"));
            start.ArgumentList.Add("--window-size=800,600");
            start.ArgumentList.Add(url);
        }

        using var process = Process.Start(start)!;
        _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        process.WaitForExit(60000);
    }

    private static async Task SendAsync(
        System.Net.Http.HttpClient client,
        System.Net.Http.HttpMethod method,
        string url,
        Action<System.Net.Http.HttpRequestMessage> shape,
        BrowserCaptureServer server,
        CancellationToken cancellationToken,
        string expectMethod = "",
        string expectPath = "")
    {
        using var request = new System.Net.Http.HttpRequestMessage(method, new Uri(url));
        shape(request);

        using var response = await client.SendAsync(request, cancellationToken);
        _ = await response.Content.ReadAsStringAsync(cancellationToken);

        await server.WaitAsync([(expectMethod is "" ? method.Method : expectMethod, expectPath is "" ? new Uri(url).AbsolutePath : expectPath)], cancellationToken);
    }

    private static void CollectShapeMismatches(CapturedRequest browser, CapturedRequest client, string label, List<string> mismatches)
    {
        var browserHeaders = browser.Headers;
        var clientHeaders = client.Headers;
        var browserNames = browserHeaders.Select(static h => h.Name).ToArray();
        var clientNames = clientHeaders.Select(static h => h.Name).ToArray();

        if (browserNames.SequenceEqual(clientNames, StringComparer.Ordinal))
        {
            for (var index = 0; index < browserNames.Length; index++)
            {
                if (ValueAgnosticHeaders.Contains(browserNames[index].ToLowerInvariant())) continue;

                if (!string.Equals(clientHeaders[index].Value, browserHeaders[index].Value, StringComparison.Ordinal))
                {
                    mismatches.Add(
                        $"{label}: значение «{browserNames[index]}» расходится: " +
                        $"браузер [{browserHeaders[index].Value}], клиент [{clientHeaders[index].Value}]");
                }
            }

            return;
        }

        mismatches.Add(
            $"{label}: набор/порядок заголовков расходится с живым браузером." + Environment.NewLine +
            $"браузер: [{string.Join(", ", browserNames)}]" + Environment.NewLine +
            $"клиент:  [{string.Join(", ", clientNames)}]");
    }
}
