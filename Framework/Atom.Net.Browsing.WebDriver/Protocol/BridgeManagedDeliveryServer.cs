using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

[SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Certificate lifetime is owned by the shared certificate manager.")]
internal sealed class BridgeManagedDeliveryServer(string host, int port, BridgeManagedExtensionDelivery? delivery, ILogger? diagnosticsLogger = null) : IAsyncDisposable
{
    private readonly TcpListener listener = new(ResolveBindableAddress(host), port);
    private readonly CancellationTokenSource cts = new();
    private readonly X509Certificate2 certificate = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(host);
    private readonly ILogger? logger = diagnosticsLogger;
    private BridgeManagedExtensionDelivery? managedExtensionDelivery = delivery;
    private Task? acceptLoop;
    private bool isDisposed;

    public int Port { get; private set; } = port;

    public bool RequiresCertificateBypass { get; private set; } = true;

    public BridgeManagedDeliveryTrustDiagnostics TrustDiagnostics { get; private set; } = BridgeManagedDeliveryTrustDiagnostics.BypassRequired("not-started");

    internal void Configure(BridgeManagedExtensionDelivery? nextDelivery)
    {
        managedExtensionDelivery = nextDelivery;

        if (nextDelivery is null)
        {
            logger?.LogManagedDeliveryPayloadCleared(Port);
            return;
        }

        logger?.LogManagedDeliveryPayloadConfigured(Port, nextDelivery.ExtensionId, nextDelivery.UpdateUrl, nextDelivery.PackageBytes.Length);
    }

    internal ValueTask StartAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        TrustDiagnostics = BridgeManagedDeliveryCertificateTrustInstaller.EnsureTrusted(BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateAuthorityCertificate());
        RequiresCertificateBypass = TrustDiagnostics.RequiresCertificateBypass;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);
        logger?.LogManagedDeliveryListenerStarted(host, Port);
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
        logger?.LogManagedDeliveryListenerStopped(host, Port);
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
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                }, cancellationToken).ConfigureAwait(false);

                var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request is null)
                    return;

                await WriteRouteResponseAsync(stream, request.Value.Method, request.Value.Path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Server shutdown.
            }
            catch (IOException ex)
            {
                logger?.LogManagedDeliveryClientDisconnected(Port, ex);

                // Client disconnected before request completed.
            }
            catch (AuthenticationException ex)
            {
                logger?.LogManagedDeliveryTlsHandshakeFailed(Port, ex);
                // TLS negotiation failed.
            }
        }
    }

    private async Task WriteRouteResponseAsync(SslStream stream, string method, string path, CancellationToken cancellationToken)
    {
        logger?.LogManagedDeliveryRequestReceived(Port, method, path);

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLoggedResponseAsync(stream, method, path, HttpStatusCode.MethodNotAllowed, "method-not-allowed", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        var nextDelivery = managedExtensionDelivery;
        if (nextDelivery is null)
        {
            await WriteLoggedResponseAsync(stream, method, path, HttpStatusCode.NotFound, "payload-missing", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryMatchManagedExtensionRoute(path, out var extensionId, out var routeKind))
        {
            await WriteLoggedResponseAsync(stream, method, path, HttpStatusCode.NotFound, "route-not-matched", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(nextDelivery.ExtensionId, extensionId, StringComparison.Ordinal))
        {
            await WriteLoggedResponseAsync(stream, method, path, HttpStatusCode.NotFound, "extension-id-mismatch", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(routeKind, "manifest", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogManagedDeliveryManifestServed(Port, extensionId, path);
            await WriteResponseAsync(stream, HttpStatusCode.OK, "text/xml; charset=utf-8", BuildManagedManifestPayload(nextDelivery), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(routeKind, "extension.crx", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogManagedDeliveryPackageServed(Port, extensionId, path, nextDelivery.PackageBytes.Length);
            await WriteResponseAsync(stream, HttpStatusCode.OK, "application/x-chrome-extension", nextDelivery.PackageBytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteLoggedResponseAsync(stream, method, path, HttpStatusCode.NotFound, "route-kind-unsupported", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLoggedResponseAsync(
        SslStream stream,
        string method,
        string path,
        HttpStatusCode statusCode,
        string reason,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        logger?.LogManagedDeliveryRequestRejected(Port, method, path, (int)statusCode, reason);
        await WriteResponseAsync(stream, statusCode, "text/plain; charset=utf-8", body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(SslStream stream, HttpStatusCode statusCode, string contentType, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var reasonPhrase = statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            _ => "Error",
        };

        var headers = Encoding.ASCII.GetBytes(
            string.Concat(
                "HTTP/1.1 ",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                " ",
                reasonPhrase,
                "\r\nContent-Type: ",
                contentType,
                "\r\nContent-Length: ",
                body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "\r\nConnection: close\r\n\r\n"));

        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        if (!body.IsEmpty)
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Method, string Path)?> ReadRequestAsync(SslStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
            return null;

        while (true)
        {
            var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headerLine))
                break;
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        if (!Uri.TryCreate(string.Concat("https://bridge.local", parts[1]), UriKind.Absolute, out var requestUri))
            return null;

        return (parts[0], requestUri.AbsolutePath);
    }

    private static bool TryMatchManagedExtensionRoute(string path, out string extensionId, out string routeKind)
    {
        extensionId = string.Empty;
        routeKind = string.Empty;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length is not 3 || !string.Equals(segments[0], "chromium", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(segments[1]) || string.IsNullOrWhiteSpace(segments[2]))
            return false;

        extensionId = segments[1];
        routeKind = segments[2];
        return true;
    }

    private static byte[] BuildManagedManifestPayload(BridgeManagedExtensionDelivery delivery)
        => Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gupdate xmlns="http://www.google.com/update2/response" protocol="2.0">
              <app appid="{{WebUtility.HtmlEncode(delivery.ExtensionId)}}">
                <updatecheck codebase="{{WebUtility.HtmlEncode(delivery.PackageUrl)}}" version="{{WebUtility.HtmlEncode(delivery.ExtensionVersion)}}" />
              </app>
            </gupdate>
            """);

    private static IPAddress ResolveBindableAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
            return address;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        throw new InvalidOperationException($"TLS delivery endpoint не умеет привязываться к хосту '{host}'");
    }

}