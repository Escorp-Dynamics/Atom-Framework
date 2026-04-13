using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

[SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Certificate lifetime is owned by the shared certificate manager.")]
internal sealed class BridgeSecureTransportServer(
    string host,
    int port,
    string secret,
    Func<WebSocket, CancellationToken, Task> handleAcceptedConnectionAsync,
    ILogger? diagnosticsLogger = null) : IAsyncDisposable
{
    private const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener listener = new(ResolveBindableAddress(host), port);
    private readonly CancellationTokenSource cts = new();
    private readonly X509Certificate2 certificate = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(host);
    private readonly string expectedSecret = secret;
    private readonly Func<WebSocket, CancellationToken, Task> connectionHandler = handleAcceptedConnectionAsync;
    private readonly ILogger? logger = diagnosticsLogger;
    private Task? acceptLoop;
    private bool isDisposed;

    public int Port { get; private set; } = port;

    internal ValueTask StartAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);
        logger?.LogSecureTransportListenerStarted(host, Port);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        await cts.CancelAsync().ConfigureAwait(false);

        try
        {
            listener.Stop();
        }
        catch (SocketException)
        {
            // Listener already stopped.
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (TimeoutException)
            {
                // Best-effort teardown.
            }
        }

        listener.Dispose();
        listener.Server.Dispose();
        cts.Dispose();
        logger?.LogSecureTransportListenerStopped(host, Port);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                logger?.LogSecureTransportAcceptFailed(ex, host, Port);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        SslStream? stream = null;
        WebSocket? socket = null;

        try
        {
            stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, cancellationToken).ConfigureAwait(false);

            var request = await ReadUpgradeRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            if (request is null)
                return;

            if (!TryValidateUpgradeRequest(request.Value, out var webSocketKey, out var statusCode, out var reason))
            {
                logger?.LogSecureTransportRequestRejected(request.Value.Method, request.Value.Path, (int)statusCode, reason);
                await WriteResponseAsync(stream, statusCode, reason, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteUpgradeAcceptedResponseAsync(stream, webSocketKey, cancellationToken).ConfigureAwait(false);
            socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
            stream = null;
            await connectionHandler(socket, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Server shutdown.
        }
        catch (IOException ex)
        {
            logger?.LogSecureTransportClientDisconnected(Port, ex);
        }
        catch (AuthenticationException ex)
        {
            logger?.LogSecureTransportTlsHandshakeFailed(Port, ex);
            logger?.LogSecureTransportTlsHandshakeDetail(Port, DescribeTlsAuthenticationException(ex), ex);
        }
        finally
        {
            socket?.Dispose();

            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);

            client.Dispose();
        }
    }

    private static async Task<UpgradeRequest?> ReadUpgradeRequestAsync(SslStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
            return null;

        var requestLineParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requestLineParts.Length < 2)
            return null;

        if (!Uri.TryCreate(string.Concat("https://bridge.local", requestLineParts[1]), UriKind.Absolute, out var requestUri))
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headerLine))
                break;

            var separatorIndex = headerLine.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
                continue;

            var name = headerLine[..separatorIndex].Trim();
            var value = headerLine[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(name))
                headers[name] = value;
        }

        return new UpgradeRequest(requestLineParts[0], requestUri.AbsolutePath, requestUri.Query, headers);
    }

    private bool TryValidateUpgradeRequest(
        UpgradeRequest request,
        [NotNullWhen(true)] out string? webSocketKey,
        out HttpStatusCode statusCode,
        [NotNullWhen(false)] out string? reason)
    {
        webSocketKey = null;
        statusCode = HttpStatusCode.BadRequest;
        reason = "invalid-request";

        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = HttpStatusCode.MethodNotAllowed;
            reason = "method-not-allowed";
            return false;
        }

        if (!HasMatchingSecret(request.Query))
        {
            statusCode = HttpStatusCode.Forbidden;
            reason = "invalid-secret";
            return false;
        }

        if (!request.Headers.TryGetValue("Upgrade", out var upgradeValue)
            || !string.Equals(upgradeValue, "websocket", StringComparison.OrdinalIgnoreCase))
        {
            reason = "missing-upgrade-header";
            return false;
        }

        if (!request.Headers.TryGetValue("Connection", out var connectionValue)
            || !HeaderContainsToken(connectionValue, "Upgrade"))
        {
            reason = "missing-connection-upgrade";
            return false;
        }

        if (!request.Headers.TryGetValue("Sec-WebSocket-Version", out var versionValue)
            || !string.Equals(versionValue, "13", StringComparison.Ordinal))
        {
            statusCode = HttpStatusCode.UpgradeRequired;
            reason = "unsupported-websocket-version";
            return false;
        }

        if (!request.Headers.TryGetValue("Sec-WebSocket-Key", out var keyValue)
            || string.IsNullOrWhiteSpace(keyValue))
        {
            reason = "missing-websocket-key";
            return false;
        }

        webSocketKey = keyValue;
        return true;
    }

    private static bool HeaderContainsToken(string headerValue, string token)
    {
        foreach (var part in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string DescribeTlsAuthenticationException(AuthenticationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder(exception.Message);
        var current = exception.InnerException;

        while (current is not null)
        {
            builder.Append(" | inner: ");
            builder.Append(current.GetType().Name);
            builder.Append(": ");
            builder.Append(current.Message);
            current = current.InnerException;
        }

        return builder.ToString();
    }

    private bool HasMatchingSecret(string query)
    {
        var actualSecret = GetQueryParameter(query, "secret");
        if (string.IsNullOrWhiteSpace(actualSecret))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualSecret),
            Encoding.UTF8.GetBytes(expectedSecret));
    }

    private static string? GetQueryParameter(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var remaining = query.AsSpan();
        if (!remaining.IsEmpty && remaining[0] == '?')
            remaining = remaining[1..];

        while (!remaining.IsEmpty)
        {
            var separatorIndex = remaining.IndexOf('&');
            var segment = separatorIndex < 0 ? remaining : remaining[..separatorIndex];
            remaining = separatorIndex < 0 ? [] : remaining[(separatorIndex + 1)..];

            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            if (!segment[..equalsIndex].Equals(key, StringComparison.Ordinal))
                continue;

            return Uri.UnescapeDataString(segment[(equalsIndex + 1)..].ToString());
        }

        return null;
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "WebSocket accept hash is fixed by RFC 6455 and must use SHA1.")]
    private static async Task WriteUpgradeAcceptedResponseAsync(SslStream stream, string webSocketKey, CancellationToken cancellationToken)
    {
        var acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(string.Concat(webSocketKey, WebSocketAcceptGuid))));
        var response = Encoding.ASCII.GetBytes(
            string.Concat(
                "HTTP/1.1 101 Switching Protocols\r\n",
                "Upgrade: websocket\r\n",
                "Connection: Upgrade\r\n",
                "Sec-WebSocket-Accept: ",
                acceptKey,
                "\r\n\r\n"));

        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(
        SslStream stream,
        HttpStatusCode statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var reasonPhrase = statusCode switch
        {
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            HttpStatusCode.UpgradeRequired => "Upgrade Required",
            _ => "Error",
        };

        var body = Encoding.UTF8.GetBytes(reason);
        var headers = Encoding.ASCII.GetBytes(
            string.Concat(
                "HTTP/1.1 ",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                " ",
                reasonPhrase,
                "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: ",
                body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "\r\nConnection: close\r\n",
                statusCode == HttpStatusCode.UpgradeRequired ? "Sec-WebSocket-Version: 13\r\n" : string.Empty,
                "\r\n"));

        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IPAddress ResolveBindableAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
            return address;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        throw new InvalidOperationException($"WSS transport endpoint не умеет привязываться к хосту '{host}'");
    }

    private readonly record struct UpgradeRequest(string Method, string Path, string Query, Dictionary<string, string> Headers);
}