#pragma warning disable MA0182

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет HTTP/1.1 соединение.
/// </summary>
[SuppressMessage("Major Code Smell", "S3459:Unassigned fields should be removed", Justification = "Traffic is a mutable metrics struct updated after connection activity begins.")]
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "transport and socketTransport are released through Dispose(bool), DisposeAsyncCore, CloseAsync, and Abort via Interlocked.Exchange.")]
internal sealed partial class Https11Connection : HttpsConnection
{
    private static readonly SearchValues<char> tokenSeparators = SearchValues.Create(" ;");
    private Traffic traffic;
    private Stream? transport;
    private TcpStream? socketTransport;
    private HttpsConnectionOptions options;
    private readonly byte[] receiveBuffer = new byte[4096];
    private int receiveOffset;
    private int receiveCount;
    private int activeStreams;
    private int isConnected;
    private int isDraining;
    private long createdTimestamp;
    private long lastActivityTimestamp;

    /// <inheritdoc/>
    public override Version Version => HttpVersion.Version11;

    /// <inheritdoc/>
    public override bool IsConnected => Volatile.Read(ref isConnected) is not 0;

    /// <inheritdoc/>
    public override bool IsSecure => options.IsHttps;

    /// <inheritdoc/>
    public override bool IsMultiplexing => false;

    /// <inheritdoc/>
    public override int ActiveStreams => Volatile.Read(ref activeStreams);

    /// <inheritdoc/>
    public override int MaxConcurrentStreams => 1;

    /// <inheritdoc/>
    public override bool IsDraining => Volatile.Read(ref isDraining) is not 0;

    /// <inheritdoc/>
    public override IPEndPoint? LocalEndPoint => socketTransport?.Socket.LocalEndPoint as IPEndPoint;

    /// <inheritdoc/>
    public override IPEndPoint? RemoteEndPoint => socketTransport?.Socket.RemoteEndPoint as IPEndPoint;

    /// <inheritdoc/>
    internal long CreatedTimestamp => Volatile.Read(ref createdTimestamp);

    /// <inheritdoc/>
    public override long LastActivityTimestamp => Volatile.Read(ref lastActivityTimestamp);

    /// <inheritdoc/>
    public override Traffic Traffic => traffic;

    /// <inheritdoc/>
    public override bool HasCapacity => IsConnected && !IsDraining && ActiveStreams is 0;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Abort([AllowNull] Exception ex)
    {
        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);
        receiveOffset = 0;
        receiveCount = 0;

        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocketTransport = Interlocked.Exchange(ref socketTransport, value: null);
        if (currentTransport is null && currentSocketTransport is null) return;

        DisposeTransports(currentTransport, currentSocketTransport);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);
        receiveOffset = 0;
        receiveCount = 0;

        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocketTransport = Interlocked.Exchange(ref socketTransport, value: null);
        if (currentTransport is null && currentSocketTransport is null) return;

        await DisposeTransportsAsync(currentTransport, currentSocketTransport).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);
        receiveOffset = 0;
        receiveCount = 0;

        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocketTransport = Interlocked.Exchange(ref socketTransport, value: null);
        if (currentTransport is null && currentSocketTransport is null) return;

        DisposeTransports(currentTransport, currentSocketTransport);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);

        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);
        receiveOffset = 0;
        receiveCount = 0;

        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocketTransport = Interlocked.Exchange(ref socketTransport, value: null);
        if (currentTransport is null && currentSocketTransport is null) return;

        await DisposeTransportsAsync(currentTransport, currentSocketTransport).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool MatchesTarget(string host, int port, bool isHttps)
        => IsConnected
        && !IsDraining
        && options.Port == port
        && options.IsHttps == isHttps
        && string.Equals(options.Host, host, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            if (MatchesTarget(options.Host, options.Port, options.IsHttps)) return;
            throw new InvalidOperationException("Соединение уже открыто для другой цели.");
        }

        var settings = new TcpSettings
        {
            IsNagleDisabled = true,
            AttemptTimeout = options.ConnectTimeout > TimeSpan.Zero ? options.ConnectTimeout : TimeSpan.FromSeconds(3),
        };

        var tcpStream = new TcpStream(settings);
        Stream applicationTransport = tcpStream;

        try
        {
            await tcpStream.ConnectAsync(options.Host, options.Port, cancellationToken).ConfigureAwait(false);

            if (options.IsHttps)
            {
                var tlsStream = new Tls12Stream(tcpStream, CreateTlsSettings(options));

                try
                {
                    await tlsStream.HandshakeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await tlsStream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                applicationTransport = tlsStream;
            }
        }
        catch
        {
            if (!ReferenceEquals(applicationTransport, tcpStream))
                await applicationTransport.DisposeAsync().ConfigureAwait(false);

            await tcpStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        transport = applicationTransport;
        socketTransport = tcpStream;
        this.options = options;
        receiveOffset = 0;
        receiveCount = 0;
        Volatile.Write(ref createdTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref isConnected, 1);
        Volatile.Write(ref isDraining, 0);
        TouchActivity();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<bool> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var socket = socketTransport?.Socket;
        var alive = socket is not null && IsConnected && !(socket.Poll(0, SelectMode.SelectRead) && socket.Available is 0);
        return ValueTask.FromResult(alive);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!IsConnected || transport is null)
                throw new InvalidOperationException("Соединение не открыто.");

            if (IsDraining)
                throw new InvalidOperationException("Соединение уже переведено в drain mode.");

            Interlocked.Increment(ref activeStreams);

            try
            {
                var requestBody = await ReadRequestBodyAsync(request.Content, cancellationToken).ConfigureAwait(false);
                var requestHead = BuildRequestHead(request, requestBody.Length);

                await transport.WriteAsync(requestHead, cancellationToken).ConfigureAwait(false);
                TrackSent(requestHead.Length);

                if (requestBody.Length > 0)
                {
                    await transport.WriteAsync(requestBody, cancellationToken).ConfigureAwait(false);
                    TrackSent(requestBody.Length);
                }

                var response = await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false);
                return new HttpsResponseMessage(response, Stopwatch.GetElapsedTime(started), exception: null);
            }
            finally
            {
                Interlocked.Decrement(ref activeStreams);
            }
        }
        catch (Exception exception)
        {
            Abort(exception);
            return HttpsResponseMessage.FromException(request, Stopwatch.GetElapsedTime(started), exception);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void StartDrain() => Volatile.Write(ref isDraining, 1);

    private void TouchActivity() => Volatile.Write(ref lastActivityTimestamp, Stopwatch.GetTimestamp());

    private void TrackSent(int length)
    {
        traffic.Add((ulong)length, 0);
        TouchActivity();
    }

    private void TrackReceived(int length)
    {
        traffic.Add(0, (ulong)length);
        TouchActivity();
    }

    [SuppressMessage("Security", "CA5398:Do not hardcode SslProtocols values", Justification = "The first custom TLS seam intentionally pins the only supported protocol version.")]
    private static TlsSettings CreateTlsSettings(HttpsConnectionOptions options)
    {
        var requestedProtocols = options.SslProtocols;
        if (requestedProtocols is not SslProtocols.None && (requestedProtocols & SslProtocols.Tls12) == SslProtocols.None)
            throw new NotSupportedException("Минимальный custom TLS path пока поддерживает только TLS 1.2.");

        return new TlsSettings
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites =
            [
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            ],
            Extensions =
            [
                new ServerNameTlsExtension { HostName = options.Host },
                new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
                new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
                new SupportedGroupsTlsExtension { Groups = [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1] },
                new EcPointFormatsTlsExtension { Formats = [0x00] },
                new SignatureAlgorithmsTlsExtension
                {
                    Algorithms =
                    [
                        SignatureAlgorithm.EcdsaSecp256r1Sha256,
                        SignatureAlgorithm.RsaPssRsaeSha256,
                        SignatureAlgorithm.RsaPkcs1Sha256,
                        SignatureAlgorithm.EcdsaSecp384r1Sha384,
                        SignatureAlgorithm.RsaPssRsaeSha384,
                        SignatureAlgorithm.RsaPkcs1Sha384,
                    ]
                },
                new ExtendedMasterSecretTlsExtension { IsEnabled = true },
                new RenegotiationInfoTlsExtension(),
                new SessionTicketExtension(),
            ],
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            CheckCertificateRevocationList = options.CheckCertificateRevocationList,
            ServerCertificateValidationCallback = options.ServerCertificateValidationCallback,
        };
    }

    private static async ValueTask<byte[]> ReadRequestBodyAsync(HttpContent? content, CancellationToken cancellationToken)
        => content is null ? [] : await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

    private byte[] BuildRequestHead(HttpsRequestMessage request, int bodyLength)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("RequestUri не задан.");
        var target = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        var hostHeader = BuildHostHeader(uri, options.IsHttps);
        var hasBody = bodyLength > 0;
        var builder = new StringBuilder(256);

        builder.Append(request.Method.Method)
            .Append(' ')
            .Append(target)
            .Append(" HTTP/1.1\r\n");

        AppendHostHeader(builder, request.Headers, hostHeader);
        AppendRequestHeaders(builder, request.Headers);
        AppendContentHeaders(builder, request.Content, hasBody);

        if (request.Headers.ConnectionClose == true || IsDraining)
            builder.Append("Connection: close\r\n");

        if (hasBody)
            builder.Append("Content-Length: ").Append(bodyLength.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");

        builder.Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void AppendHostHeader(StringBuilder builder, HttpHeaders headers, string hostHeader)
    {
        if (!ContainsHeader(headers, nameof(HttpRequestHeader.Host)))
            builder.Append("Host: ").Append(hostHeader).Append("\r\n");
    }

    private static void AppendRequestHeaders(StringBuilder builder, HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, nameof(HttpRequestHeader.Host), StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(header.Key, nameof(HttpRequestHeader.Connection), StringComparison.OrdinalIgnoreCase)) continue;
            AppendHeader(builder, header.Key, header.Value);
        }
    }

    private static void AppendContentHeaders(StringBuilder builder, HttpContent? content, bool hasBody)
    {
        if (content is null) return;

        foreach (var header in content.Headers)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) && hasBody) continue;
            AppendHeader(builder, header.Key, header.Value);
        }
    }

    private static bool ContainsHeader(HttpHeaders headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static void AppendHeader(StringBuilder builder, string name, IEnumerable<string> values)
    {
        builder.Append(name).Append(": ");
        builder.AppendJoin(", ", values);
        builder.Append("\r\n");
    }

    private static string BuildHostHeader(Uri uri, bool isHttps)
    {
        var defaultPort = isHttps ? 443 : 80;
        return uri.IsDefaultPort || uri.Port == defaultPort
            ? uri.IdnHost
            : string.Concat(uri.IdnHost, ":", uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [SuppressMessage("Reliability", "CA1849:Call async methods when in an async method", Justification = "IDisposable requires a synchronous release path.")]
    [SuppressMessage("Reliability", "S6966:Await DisposeAsync instead", Justification = "IDisposable requires a synchronous release path.")]
    [SuppressMessage("Usage", "VSTHRD103:Call async methods when in an async method", Justification = "IDisposable requires a synchronous release path.")]
    private static void DisposeTransports(Stream? currentTransport, TcpStream? currentSocketTransport)
    {
        currentTransport?.Dispose();

        if (currentSocketTransport is null || ReferenceEquals(currentTransport, currentSocketTransport))
            return;

        currentSocketTransport.Dispose();
    }

    private static async ValueTask DisposeTransportsAsync(Stream? currentTransport, TcpStream? currentSocketTransport)
    {
        if (currentTransport is not null)
            await currentTransport.DisposeAsync().ConfigureAwait(false);

        if (currentSocketTransport is null || ReferenceEquals(currentTransport, currentSocketTransport))
            return;

        await currentSocketTransport.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class ResponseHeadersState
    {
        public List<KeyValuePair<string, string>> ContentHeaders { get; } = [];

        public long? ContentLength { get; set; }

        public bool TransferEncodingChunked { get; set; }

        public bool ConnectionClose { get; set; }

        public ResponseBodyKind BodyKind { get; set; }
    }

    private enum ResponseBodyKind
    {
        None,
        ContentLength,
        Chunked,
        CloseDelimited,
    }
}

#pragma warning restore MA0182