using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Profiles;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет работу через апстрим-прокси: туннель CONNECT и абсолютную форму адреса.
/// </summary>
/// <remarks>
/// Прокси здесь настоящий, но локальный: он записывает то, что мы отправили, и отвечает сам.
/// Так проверяется именно НАША сторона обмена — формат запроса, кодирование учётных данных,
/// разбор ответа, — и проверка не зависит ни от сети, ни от чужого сервера.
///
/// Различие двух режимов не косметическое. Туннель существует затем, чтобы прокси не видел
/// содержимого, и нужен только защищённому обмену; незащищённый запрос прокси обслуживает сам, и
/// адрес ему нужен целиком. Перепутать режимы — значит получить отказ прокси, а не ошибку в коде.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HttpsUpstreamProxyTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public async Task CleartextRequestThroughProxyUsesAbsoluteTarget()
    {
        await using var proxy = new RecordingProxy(tunnel: false);

        using var handler = CreateHandler(proxy.Address, credentials: null);
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(new Uri("http://example.test/path?query=1"), TestContext.CurrentContext.CancellationToken);

        var requestLine = proxy.FirstRequestLine;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(requestLine, Does.StartWith("GET http://example.test/path?query=1 HTTP/1.1"));
        });
    }

    [Test]
    public async Task SecureRequestThroughProxyOpensTunnelToTarget()
    {
        await using var proxy = new RecordingProxy(tunnel: true);

        using var handler = CreateHandler(proxy.Address, credentials: null);
        using var client = new HttpClient(handler);

        // Туннель прокси откроет, но за ним рукопожатия не будет — соединение оборвётся. Здесь
        // проверяется именно запрос на туннель: он уходит ДО всякого TLS.
        await Assert.ThatAsync(async () => await client.GetAsync(new Uri("https://example.test/"), TestContext.CurrentContext.CancellationToken), Throws.Exception);

        Assert.That(proxy.FirstRequestLine, Is.EqualTo("CONNECT example.test:443 HTTP/1.1"));
    }

    [Test]
    public async Task ProxyCredentialsSurviveSpecialCharacters()
    {
        // Пароль с двоеточием и собакой — обычное дело у прокси-сервисов. Адрес хранит их
        // процентно-кодированными, и отправить их без раскодирования значит отправить чужой пароль.
        const string User = "поль:зователь";
        const string Password = "па:роль@сложный";

        await using var proxy = new RecordingProxy(tunnel: true);

        using var handler = CreateHandler(proxy.Address, new NetworkCredential(User, Password));
        using var client = new HttpClient(handler);

        await Assert.ThatAsync(async () => await client.GetAsync(new Uri("https://example.test/"), TestContext.CurrentContext.CancellationToken), Throws.Exception);

        var authorization = proxy.FirstHeaderValue("Proxy-Authorization");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..]));

        Assert.That(decoded, Is.EqualTo(User + ":" + Password));
    }

    [Test]
    public async Task DirectRequestKeepsOriginFormTarget()
    {
        // Без прокси абсолютный адрес в строке запроса — нарушение формы.
        await using var origin = new RecordingProxy(tunnel: false);

        using var handler = new HttpsClientHandler
        {
            BrowserProfile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseProxy = false,
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{origin.Port}/path"), TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(origin.FirstRequestLine, Is.EqualTo("GET /path HTTP/1.1"));
        });
    }

    [Test]
    public async Task Http3RequestThroughProxyFallsBackInsteadOfLeakingTheRealAddress()
    {
        // ★ HTTP/3 работает поверх UDP, а обычный прокси умеет только туннель CONNECT поверх TCP.
        // Пойти по HTTP/3 при настроенном прокси значит уйти МИМО него — напрямую, с настоящего
        // адреса, молча и с полностью рабочим ответом. Для того, кто поставил прокси именно ради
        // подмены адреса, это худший исход: утечка, ничем себя не проявляющая.
        //
        // Проверяется поэтому не «нет ошибки», а то, что запрос ДОШЁЛ ДО ПРОКСИ: только это
        // отличает откат от утечки.
        await using var proxy = new RecordingProxy(tunnel: false);

        using var handler = CreateHandler(proxy.Address, credentials: null);
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.test/quic"))
        {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        using var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(proxy.FirstRequestLine, Does.Contain("example.test/quic"), "запрос обязан пройти через прокси, а не мимо него");
        });
    }

    private static HttpsClientHandler CreateHandler(Uri proxyAddress, NetworkCredential? credentials)
        => new()
        {
            BrowserProfile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseProxy = true,
            Proxy = new WebProxy(proxyAddress) { Credentials = credentials },
        };

    /// <summary>
    /// Локальный прокси, записывающий полученный запрос.
    /// </summary>
    /// <param name="tunnel">Отвечать ли на CONNECT согласием вместо обслуживания запроса.</param>
    private sealed class RecordingProxy : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource lifetime = new();
        private readonly TaskCompletionSource<string> firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task acceptLoop;

        public RecordingProxy(bool tunnel)
        {
            listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();

            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Address = new Uri($"http://127.0.0.1:{Port}");
            acceptLoop = Task.Run(() => AcceptAsync(tunnel, lifetime.Token));
        }

        public int Port { get; }

        public Uri Address { get; }

        public string FirstRequestLine => FirstRequest.Split("\r\n")[0];

        private string FirstRequest => firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        public string FirstHeaderValue(string name)
        {
            foreach (var line in FirstRequest.Split("\r\n"))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0) continue;
                if (!string.Equals(line[..separator], name, StringComparison.OrdinalIgnoreCase)) continue;

                return line[(separator + 1)..].Trim();
            }

            throw new InvalidOperationException($"Заголовок {name} не получен.");
        }

        public async ValueTask DisposeAsync()
        {
            await lifetime.CancelAsync();
            listener.Stop();

            try
            {
                await acceptLoop;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при остановке.
            }
            catch (SocketException)
            {
                // Слушатель закрыт — тоже ожидаемо.
            }

            lifetime.Dispose();
        }

        private async Task AcceptAsync(bool tunnel, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();

                var header = await ReadHeaderAsync(stream, cancellationToken);
                firstRequest.TrySetResult(header);

                // Согласие на туннель отправляем и обрываем связь: за ним начался бы TLS, а
                // изображать сервер TLS для проверки формы запроса не нужно.
                var answer = tunnel
                    ? "HTTP/1.1 200 Connection Established\r\n\r\n"
                    : "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

                await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }

        private static async Task<string> ReadHeaderAsync(System.Net.Sockets.NetworkStream stream, CancellationToken cancellationToken)
        {
            var builder = new StringBuilder(256);
            var buffer = new byte[1];
            var matched = 0;

            while (matched < 4)
            {
                if (await stream.ReadAsync(buffer, cancellationToken) <= 0) break;

                var current = (char)buffer[0];
                builder.Append(current);

                matched = current switch
                {
                    '\r' when matched is 0 or 2 => matched + 1,
                    '\n' when matched is 1 or 3 => matched + 1,
                    _ => 0,
                };
            }

            return builder.ToString();
        }
    }
}
