using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Profiles;
using Atom.Net.Https.Http;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Дифференциальный capture-тест: навигационный запрос настоящего Firefox против запроса
/// нашего клиента на ОДНОМ и том же capture-сервере.
/// </summary>
/// <remarks>
/// Форма сравнения — структура, а не значения: порядок и регистр имён заголовков, Presence
/// матрица и значения детерминированных заголовков (sec-fetch-*, accept-encoding,
/// upgrade-insecure-requests). Значения версии (User-Agent, sec-ch-ua) различаются по построению:
/// локальный Chromium — не Google Chrome 131 из профиля.
/// </remarks>
[CancelAfter(60000)]
public sealed class FirefoxDifferentialCaptureTests
{
    private static readonly string[] ExpectedFirefoxOrder =
    [
        "Host", "User-Agent", "Accept", "Accept-Language", "Accept-Encoding", "Connection",
        "Upgrade-Insecure-Requests", "Sec-Fetch-Dest", "Sec-Fetch-Mode", "Sec-Fetch-Site", "Priority",
    ];
    private static readonly string[] FirefoxPaths =
    [
        "/usr/bin/firefox",
        "/usr/sbin/firefox",
    ];

    private static string? FindFirefox() => FirefoxPaths.FirstOrDefault(File.Exists);

    [Test]
    public async Task FirefoxNavigationHeadersMatchRealFirefoxShape()
    {
        var firefox = FindFirefox();
        if (firefox is null) Assert.Ignore("Firefox не найден в системе — capture-тест пропущен");

        var browserHead = await CaptureAsync(firefox, runClient: false);
        var clientHead = await CaptureAsync(firefox, runClient: true);

        var browserHeaders = ParseHeaders(browserHead);
        var clientHeaders = ParseHeaders(clientHead);

        // 1. Порядок имён заголовков обязан совпадать с живым Firefox.
        var browserNames = browserHeaders.Select(static h => h.Name).ToArray();
        var clientNames = clientHeaders.Select(static h => h.Name).ToArray();
        Assert.That(clientNames, Is.EqualTo(ExpectedFirefoxOrder).AsCollection,
            "порядок заголовков не совпал с эталоном Firefox 154: " +
            $"клиент: [{string.Join(", ", clientNames)}]");
        Assert.That(clientNames, Is.EqualTo(browserNames).AsCollection,
            "порядок заголовков навигации расходится с настоящим Chromium: " +
            $"браузер: [{string.Join(", ", browserNames)}], клиент: [{string.Join(", ", clientNames)}]");

        // 2. Детерминированные значения — точные.
        AssertValue(clientHeaders, "priority", GetBrowserValue(browserHeaders, "priority"));
        AssertValue(clientHeaders, "upgrade-insecure-requests", GetBrowserValue(browserHeaders, "upgrade-insecure-requests"));
        AssertValue(clientHeaders, "sec-fetch-site", GetBrowserValue(browserHeaders, "sec-fetch-site"));
        AssertValue(clientHeaders, "sec-fetch-mode", GetBrowserValue(browserHeaders, "sec-fetch-mode"));
        // Обе стороны (CLI-навигация без user activation) не отправляют Sec-Fetch-User.
        Assert.That(GetBrowserValue(clientHeaders, "sec-fetch-user"), Is.Empty,
            "клиент не должен отправлять Sec-Fetch-User без user activation");
        AssertValue(clientHeaders, "sec-fetch-dest", GetBrowserValue(browserHeaders, "sec-fetch-dest"));

        // 3. Accept-Encoding: состав кодеков совпадает.
        var browserAe = GetBrowserValue(browserHeaders, "accept-encoding");
        var clientAe = GetBrowserValue(clientHeaders, "accept-encoding");
        Assert.That(NormalizeCodecs(clientAe), Is.EqualTo(NormalizeCodecs(browserAe)),
            $"состав Accept-Encoding расходится: браузер [{browserAe}], клиент [{clientAe}]");

        // 4. Регистр имён: браузерный casing воспроизводится посимвольно.
        for (var index = 0; index < browserNames.Length; index++)
        {
            Assert.That(clientNames[index], Is.EqualTo(browserNames[index]),
                $"регистр заголовка #{index} расходится: браузер [{browserNames[index]}], клиент [{clientNames[index]}]");
        }
    }

    private static async Task<string> CaptureAsync(string firefox, bool runClient)
    {
        var capture = new CaptureServer();
        var url = $"http://127.0.0.1:{capture.Port}/browser-capture";

        try
        {
            if (runClient)
            {
                var profile = BrowserProfileResolver.Resolve(
                    "Mozilla/5.0 (X11; Linux x86_64; rv:154.0) Gecko/20100101 Firefox/154.0");
                using var handler = new HttpsClientHandler { BrowserProfile = profile };
                using var client = new System.Net.Http.HttpClient(handler, disposeHandler: false);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, new Uri(url))
                    .WithHttpsRequestKind(RequestKind.Navigation)
                    .WithHttpsBrowserContext(new HttpsBrowserRequestContext
                    {
                        // CLI-навигация screenshot-режима не является user-activated —
                        // живой Firefox в этом случае Sec-Fetch-User не отправляет.
                        IsUserActivated = false,
                    });
                using var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);
                _ = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);
            }
            else
            {
                var start = new ProcessStartInfo(firefox)
                {
                    ArgumentList =
                    {
                        "--headless", "--screenshot", "/tmp/atom-net-ff-shot.png", "--window-size=800,600", url,
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(start)!;
                _ = await process.StandardOutput.ReadToEndAsync(TestContext.CurrentContext.CancellationToken);
                process.WaitForExit(30000);
            }

            await capture.WaitHeadAsync(TestContext.CurrentContext.CancellationToken);
            return capture.Head!;
        }
        finally
        {
            await capture.DisposeAsync();
        }
    }

    private static (string Name, string Value)[] ParseHeaders(string head)
    {
        var lines = head.Replace("\r\n", "\n").Split('\n');
        var headers = new List<(string, string)>();

        // Строка запроса пропускается; заголовки до пустой строки.
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length is 0) break;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0) continue;
            headers.Add((line[..separator], line[(separator + 1)..].Trim()));
        }

        return [.. headers];
    }

    private static string GetBrowserValue((string Name, string Value)[] headers, string name)
    {
        var match = Array.Find(headers, h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
        return match.Name is null ? string.Empty : match.Value;
    }

    private static void AssertValue((string Name, string Value)[] headers, string name, string expected)
    {
        var match = Array.Find(headers, h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
        Assert.That(match.Name is not null, Is.True, "заголовок " + name + " отсутствует у клиента");
        Assert.That(match.Value, Is.EqualTo(expected), "значение " + name + " расходится с Chromium");
    }

    private static string NormalizeCodecs(string value)
        => string.Join(",", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static codec => codec.ToLowerInvariant()));

    private sealed class CaptureServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource lifetime = new();
        private readonly TaskCompletionSource<string> headSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CaptureServer()
        {
            listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(() => AcceptAsync(lifetime.Token));
        }

        public int Port { get; }

        public string? Head => headSource.Task.IsCompleted ? headSource.Task.Result : null;

        public async ValueTask WaitHeadAsync(CancellationToken cancellationToken)
        {
            await headSource.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        private async Task AcceptAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            var buffer = new byte[8192];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
                if (read is 0) break;
                total += read;
                if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
            }

            var head = Encoding.ASCII.GetString(buffer, 0, total);
            headSource.TrySetResult(head.Substring(0, head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));

            var body = "<html><head><title>capture</title></head><body>ok</body></html>"u8.ToArray();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n"), cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await lifetime.CancelAsync();
            listener.Stop();
            lifetime.Dispose();
        }
    }
}
