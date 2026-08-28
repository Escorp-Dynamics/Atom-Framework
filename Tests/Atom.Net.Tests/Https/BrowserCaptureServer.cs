using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Запрошенная клиентом или браузером голова HTTP-запроса.
/// </summary>
/// <param name="Method">Метод запроса.</param>
/// <param name="Path">Путь запроса без query.</param>
/// <param name="Headers">Заголовки в порядке прихода, имена — в точном регистре.</param>
internal sealed record CapturedRequest(string Method, string Path, (string Name, string Value)[] Headers);

/// <summary>
/// Многозапросный capture-сервер HTTP/1.1: записывает головы всех приходящих запросов
/// и отвечает по фиксированной таблице путей сценария.
/// </summary>
/// <remarks>
/// Сценарная страница порождает запросы всех типов (subresources, fetch, redirect, iframe,
/// cross-origin preflight), и браузер шлёт их по нескольким соединениям — сервер обязан
/// переживать последовательность запросов на одном соединении и параллельные соединения.
/// Ответы уходят с <c>Connection: close</c>: для capture-эталона это неважно, а серверу
/// избавляет от состояния keep-alive.
/// </remarks>
internal sealed class BrowserCaptureServer : IAsyncDisposable
{
    private const int WaitForRequestsAttempts = 300;

    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, CapturedRequest> captured = new(StringComparer.Ordinal);
    private readonly string? rootPageHtml;

    public BrowserCaptureServer(string? rootPageHtml = null)
    {
        this.rootPageHtml = rootPageHtml;
        listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoopAsync(lifetime.Token));
    }

    public int Port { get; }

    /// <summary>Источник сценария, который обслуживает этот сервер.</summary>
    public string Origin => $"http://127.0.0.1:{Port}";

    /// <summary>Мгновенный снимок всех записанных запросов.</summary>
    public IReadOnlyDictionary<string, CapturedRequest> Snapshot() => new Dictionary<string, CapturedRequest>(captured);

    /// <summary>Стирает записи: между браузерной и клиентской фазами одного теста.</summary>
    public void Reset() => captured.Clear();

    /// <summary>Ждёт, пока придут все перечисленные запросы (метод, путь).</summary>
    public async Task WaitAsync(IReadOnlyList<(string Method, string Path)> keys, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < WaitForRequestsAttempts; attempt++)
        {
            if (keys.All(key => captured.ContainsKey(Key(key.Method, key.Path)))) return;
            await Task.Delay(100, cancellationToken);
        }

        var missing = keys.Where(key => !captured.ContainsKey(Key(key.Method, key.Path))).Select(key => Key(key.Method, key.Path));
        throw new TimeoutException("capture-сервер не дождался запросов: " + string.Join(", ", missing));
    }

    private static string Key(string method, string path) => method + " " + path;

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => ServeConnectionAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task ServeConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        var buffer = new byte[16384];

        while (true)
        {
            var total = 0;
            var headEnd = -1;

            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
                if (read is 0) return;

                total += read;
                headEnd = IndexOfHeadEnd(buffer, total);
                if (headEnd >= 0) break;
            }

            if (headEnd < 0) return;

            var head = Encoding.ASCII.GetString(buffer, 0, headEnd);
            var request = ParseHead(head);
            var bodyLength = ParseContentLength(head);

            // Тело запроса (fetch POST) дочитывается и выбрасывается: capture интересуют головы.
            var bodyAvailable = total - headEnd - 4;

            while (bodyAvailable < bodyLength)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read is 0) return;
                bodyAvailable += read;
            }

            if (request is not null)
            {
                captured[Key(request.Method, request.Path)] = request;
            }

            await stream.WriteAsync(BuildResponse(request?.Path ?? "/"), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }

    private static int IndexOfHeadEnd(byte[] buffer, int length)
    {
        for (var index = 0; index + 3 < length; index++)
        {
            if (buffer[index] is 0x0D && buffer[index + 1] is 0x0A && buffer[index + 2] is 0x0D && buffer[index + 3] is 0x0A)
            {
                return index;
            }
        }

        return -1;
    }

    private static CapturedRequest? ParseHead(string head)
    {
        var lines = head.Replace("\r\n", "\n").Split('\n');
        if (lines.Length is 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var headers = new List<(string, string)>();

        for (var index = 1; index < lines.Length; index++)
        {
            var separator = lines[index].IndexOf(':', StringComparison.Ordinal);
            if (separator < 0) continue;

            headers.Add((lines[index][..separator], lines[index][(separator + 1)..].Trim()));
        }

        var path = requestLine[1];
        var query = path.IndexOf('?', StringComparison.Ordinal);

        return new CapturedRequest(requestLine[0], query < 0 ? path : path[..query], [.. headers]);
    }

    private static int ParseContentLength(string head)
    {
        foreach (var line in head.Replace("\r\n", "\n").Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0) continue;

            if (line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(separator + 1)..].Trim(), out var length))
            {
                return length;
            }
        }

        return 0;
    }

    private byte[] BuildResponse(string path)
    {
        const string json = """{"ok":true}""";
        const string corsAllow = "Access-Control-Allow-Origin: *";

        return path switch
        {
            "/" => Text(200, "text/html", rootPageHtml ?? "<html><body>capture</body></html>"),
            "/style" => Text(200, "text/css", "body{margin:0}"),
            "/script" => Text(200, "application/javascript", "/* capture */"),
            "/img" => Binary(200, "image/png", Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")),
            "/iframe" => Text(200, "text/html", "<html><body>iframe</body></html>"),
            "/fetch-get" or "/fetch-post" or "/final" => Text(200, "application/json", json, corsAllow),
            "/redirect" => Text(302, "text/html", string.Empty, "Location: /final"),
            "/preflight" => Text(200, "application/json", json,
                corsAllow, "Access-Control-Allow-Methods: PUT", "Access-Control-Allow-Headers: X-Probe"),
            _ => Text(404, "text/plain", "not found"),
        };
    }

    private static byte[] Text(int status, string contentType, string body, params string[] extraHeaders)
        => Build(status, contentType, Encoding.UTF8.GetBytes(body), extraHeaders);

    private static byte[] Binary(int status, string contentType, byte[] body, params string[] extraHeaders)
        => Build(status, contentType, body, extraHeaders);

    private static byte[] Build(int status, string contentType, byte[] body, string[] extraHeaders)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ')
            .Append(status is 200 ? "OK" : status is 302 ? "Found" : "Not Found").Append("\r\n");
        builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");

        foreach (var header in extraHeaders)
        {
            builder.Append(header).Append("\r\n");
        }

        builder.Append("Connection: close\r\n\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString()).Concat(body).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        listener.Stop();
        lifetime.Dispose();
    }
}
