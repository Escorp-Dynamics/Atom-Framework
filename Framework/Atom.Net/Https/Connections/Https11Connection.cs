#pragma warning disable MA0182

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Atom.Net.Https.Headers;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;
using Atom.Text;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет HTTP/1.1 соединение.
/// </summary>
[SuppressMessage("Major Code Smell", "S3459:Unassigned fields should be removed", Justification = "Traffic is a mutable metrics struct updated after connection activity begins.")]
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "transport and socketTransport are released through Dispose(bool), DisposeAsyncCore, CloseAsync, and Abort via Interlocked.Exchange.")]
internal sealed partial class Https11Connection : HttpsConnection
{
    private static readonly NamedGroup[] defaultSupportedGroups = CreateSupportedGroups();
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

        var settings = CreateTcpSettings(options);

        var tcpStream = new TcpStream(settings);
        Stream applicationTransport = tcpStream;
        var openToken = CreateOpenToken(options.ConnectTimeout, cancellationToken, out var openTimeoutCts);

        try
        {
            await tcpStream.ConnectAsync(options.Host, options.Port, openToken).ConfigureAwait(false);

            if (options.IsHttps)
            {
                var tlsStream = new Tls12Stream(tcpStream, CreateTlsSettings(options));

                try
                {
                    await tlsStream.HandshakeAsync(openToken).ConfigureAwait(false);
                }
                catch
                {
                    await tlsStream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                applicationTransport = tlsStream;
            }
        }
        catch (OperationCanceledException exception) when (openTimeoutCts is not null && openTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Не удалось открыть HTTP/1.1 соединение с {options.Host}:{options.Port} за {options.ConnectTimeout}.", exception);
        }
        catch
        {
            if (!ReferenceEquals(applicationTransport, tcpStream))
                await applicationTransport.DisposeAsync().ConfigureAwait(false);

            await tcpStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            openTimeoutCts?.Dispose();
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

    private static CancellationToken CreateOpenToken(TimeSpan connectTimeout, CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (connectTimeout <= TimeSpan.Zero || connectTimeout == Timeout.InfiniteTimeSpan)
            return cancellationToken;

        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(connectTimeout);
        return timeoutCts.Token;
    }

    private CancellationToken CreateSendToken(CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (options.RequestSendTimeout <= TimeSpan.Zero || options.RequestSendTimeout == Timeout.InfiniteTimeSpan)
            return cancellationToken;

        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.RequestSendTimeout);
        return timeoutCts.Token;
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
                var sendToken = CreateSendToken(cancellationToken, out var sendTimeoutCts);

                try
                {
                    var requestBody = await ReadRequestBodyAsync(request.Content, sendToken).ConfigureAwait(false);
                    var requestHead = BuildRequestHead(request, requestBody.Length);

                    await transport.WriteAsync(requestHead, sendToken).ConfigureAwait(false);
                    TrackSent(requestHead.Length);

                    if (requestBody.Length > 0)
                    {
                        await transport.WriteAsync(requestBody, sendToken).ConfigureAwait(false);
                        TrackSent(requestBody.Length);
                    }

                    var response = await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false);
                    return new HttpsResponseMessage(response, Stopwatch.GetElapsedTime(started), exception: null);
                }
                catch (OperationCanceledException exception) when (sendTimeoutCts is not null && sendTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Не удалось отправить request за {options.RequestSendTimeout}.", exception);
                }
                finally
                {
                    sendTimeoutCts?.Dispose();
                }
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
    private static TcpSettings CreateTcpSettings(HttpsConnectionOptions options)
    {
        var profileSettings = options.ProfileTcpSettings;
        if (profileSettings is null)
        {
            return new TcpSettings
            {
                IsNagleDisabled = true,
                ConnectTimeout = options.ConnectTimeout,
                AttemptTimeout = options.ConnectTimeout > TimeSpan.Zero ? options.ConnectTimeout : TimeSpan.FromSeconds(3),
                LocalEndPoint = options.LocalEndPoint,
            };
        }

        return profileSettings.Value with
        {
            ConnectTimeout = options.ConnectTimeout,
            LocalEndPoint = options.LocalEndPoint,
        };
    }

    [SuppressMessage("Security", "CA5398:Do not hardcode SslProtocols values", Justification = "The first custom TLS seam intentionally pins the only supported protocol version.")]
    private static TlsSettings CreateTlsSettings(HttpsConnectionOptions options)
    {
        var profileSettings = options.ProfileTlsSettings;
        var requestedProtocols = options.SslProtocols;
        if (requestedProtocols is not SslProtocols.None && (requestedProtocols & SslProtocols.Tls12) == SslProtocols.None)
            throw new NotSupportedException("Минимальный custom TLS path пока поддерживает только TLS 1.2.");

        var defaultCipherSuites = new[]
        {
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        };

        var defaultExtensions = new ITlsExtension[]
        {
            new ServerNameTlsExtension { HostName = options.Host },
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
            new SupportedGroupsTlsExtension { Groups = defaultSupportedGroups },
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
        };

        var cipherSuites = profileSettings is { } profileTls && profileTls.CipherSuites.Any()
            ? profileTls.CipherSuites
            : defaultCipherSuites;

        var extensions = MergeTlsExtensions(options.Host, defaultExtensions, profileSettings?.Extensions);

        var handshakeTimeout = profileSettings?.HandshakeTimeout ?? options.ConnectTimeout;
        if (handshakeTimeout <= TimeSpan.Zero || handshakeTimeout == Timeout.InfiniteTimeSpan)
        {
            handshakeTimeout = options.ConnectTimeout;
        }

        return new TlsSettings
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites = cipherSuites,
            Extensions = extensions,
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            CheckCertificateRevocationList = options.CheckCertificateRevocationList,
            ServerCertificateValidationCallback = options.ServerCertificateValidationCallback,
            Delay = profileSettings?.Delay ?? TimeSpan.Zero,
            HandshakeTimeout = handshakeTimeout,
        };
    }

    private static List<ITlsExtension> MergeTlsExtensions(string host, IEnumerable<ITlsExtension> defaultExtensions, IEnumerable<ITlsExtension>? profileExtensions)
    {
        if (profileExtensions is null)
        {
            return [.. defaultExtensions.Select(extension => MaterializeTlsExtension(extension, host))];
        }

        var merged = new List<ITlsExtension>();
        var overriddenExtensionIds = new HashSet<ushort>();

        foreach (var extension in profileExtensions)
        {
            merged.Add(MaterializeTlsExtension(extension, host));
            overriddenExtensionIds.Add(extension.Id);
        }

        foreach (var extension in defaultExtensions)
        {
            if (overriddenExtensionIds.Contains(extension.Id))
            {
                continue;
            }

            merged.Add(MaterializeTlsExtension(extension, host));
        }

        return merged;
    }

    private static ITlsExtension MaterializeTlsExtension(ITlsExtension extension, string host)
        => extension switch
        {
            ServerNameTlsExtension serverName => new ServerNameTlsExtension
            {
                Id = serverName.Id,
                HostName = string.IsNullOrWhiteSpace(serverName.HostName) ? host : serverName.HostName,
            },
            AlpnTlsExtension alpn => new AlpnTlsExtension
            {
                Id = alpn.Id,
                Protocols = alpn.Protocols.Any() ? [.. alpn.Protocols] : [AlpnTlsExtension.Http11],
            },
            SupportedVersionsTlsExtension supportedVersions => new SupportedVersionsTlsExtension
            {
                Id = supportedVersions.Id,
                Versions = supportedVersions.Versions.Any() ? [.. supportedVersions.Versions] : [SslProtocols.Tls12],
            },
            SupportedGroupsTlsExtension supportedGroups => new SupportedGroupsTlsExtension
            {
                Id = supportedGroups.Id,
                Groups = [.. supportedGroups.Groups],
            },
            EcPointFormatsTlsExtension ecPointFormats => new EcPointFormatsTlsExtension
            {
                Id = ecPointFormats.Id,
                Formats = [.. ecPointFormats.Formats],
            },
            SignatureAlgorithmsTlsExtension signatureAlgorithms => new SignatureAlgorithmsTlsExtension
            {
                Id = signatureAlgorithms.Id,
                Algorithms = [.. signatureAlgorithms.Algorithms],
            },
            ExtendedMasterSecretTlsExtension extendedMasterSecret => new ExtendedMasterSecretTlsExtension
            {
                Id = extendedMasterSecret.Id,
                IsEnabled = extendedMasterSecret.IsEnabled,
            },
            RenegotiationInfoTlsExtension renegotiationInfo => new RenegotiationInfoTlsExtension
            {
                Id = renegotiationInfo.Id,
                RenegotiatedConnection = renegotiationInfo.RenegotiatedConnection.ToArray(),
            },
            SessionTicketExtension sessionTicket => new SessionTicketExtension
            {
                Id = sessionTicket.Id,
                Ticket = sessionTicket.Ticket.ToArray(),
            },
            _ => extension,
        };

    private static NamedGroup[] CreateSupportedGroups()
    {
        if (!IsX25519Supported())
            return [NamedGroup.Secp256r1, NamedGroup.Secp384r1];

        return [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1];
    }

    private static bool IsX25519Supported()
    {
        try
        {
            using var _ = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("X25519"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<byte[]> ReadRequestBodyAsync(HttpContent? content, CancellationToken cancellationToken)
        => content is null ? [] : await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

    private byte[] BuildRequestHead(HttpsRequestMessage request, int bodyLength)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("RequestUri не задан.");
        var target = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        var hostHeader = BuildHostHeader(uri, options.IsHttps);
        var hasBody = bodyLength > 0;
        var builder = new ValueStringBuilder(256);
        try
        {
            var formattingPolicy = request.HeadersFormattingPolicy;

            builder.Append(request.Method.Method)
                .Append(' ')
                .Append(target)
                .Append(" HTTP/1.1\r\n");

            if (formattingPolicy is null)
            {
                AppendHostHeader(ref builder, request.Headers, hostHeader);
                AppendFormattedHeaders(ref builder, request, hasBody);

                if (IsDraining && !ContainsHeader(request.Headers, nameof(HttpRequestHeader.Connection)))
                    builder.Append("Connection: close\r\n");
            }
            else
            {
                AppendFormattedHeaders(ref builder, request, hasBody, hostHeader);
            }

            if (hasBody)
                builder.Append("Content-Length: ").Append(bodyLength.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");

            builder.Append("\r\n");
            return Encoding.ASCII.GetBytes(builder.ToString());
        }
        finally
        {
            builder.Dispose();
        }
    }

    private void AppendFormattedHeaders(ref ValueStringBuilder builder, HttpsRequestMessage request, bool hasBody, string? hostHeader = null)
    {
        var formattingPolicy = request.HeadersFormattingPolicy;
        if (formattingPolicy is null)
        {
            AppendRequestHeaders(ref builder, request.Headers);
            AppendContentHeaders(ref builder, request.Content, hasBody);
            return;
        }

        var headers = BuildHeaderMap(request, hasBody, hostHeader, IsDraining);
        foreach (var header in formattingPolicy.Format(headers, HttpVersion.Version11, request.EffectiveKind, request.UseCookieCrumbling))
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }
    }

    private static Dictionary<string, string> BuildHeaderMap(HttpsRequestMessage request, bool hasBody, string? hostHeader, bool isDraining)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(hostHeader))
        {
            headers[nameof(HttpRequestHeader.Host)] = request.Headers.Host ?? hostHeader;
        }

        foreach (var header in request.Headers)
        {
            headers[header.Key] = SerializeRequestHeader(request.Headers, header.Key, header.Value);
        }

        if (isDraining && !headers.ContainsKey(nameof(HttpRequestHeader.Connection)))
        {
            headers[nameof(HttpRequestHeader.Connection)] = "close";
        }

        if (request.Content is null)
        {
            return headers;
        }

        foreach (var header in request.Content.Headers)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) && hasBody)
            {
                continue;
            }

            headers[header.Key] = string.Join(", ", header.Value);
        }

        return headers;
    }

    private static string SerializeRequestHeader(HttpRequestHeaders headers, string name, IEnumerable<string> fallbackValues)
    {
        if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase) && headers.UserAgent.Count > 0)
        {
            return string.Join(' ', headers.UserAgent.Select(static value => value.ToString()));
        }

        if (string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase) && headers.Accept.Count > 0)
        {
            return string.Join(',', headers.Accept.Select(SerializeAcceptValue));
        }

        if (string.Equals(name, "Accept-Language", StringComparison.OrdinalIgnoreCase) && headers.AcceptLanguage.Count > 0)
        {
            return string.Join(',', headers.AcceptLanguage.Select(SerializeAcceptLanguageValue));
        }

        if (string.Equals(name, "Accept-Encoding", StringComparison.OrdinalIgnoreCase) && headers.AcceptEncoding.Count > 0)
        {
            return string.Join(", ", headers.AcceptEncoding.Select(SerializeAcceptEncodingValue));
        }

        if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) && headers.Connection.Count > 0)
        {
            return string.Join(", ", headers.Connection);
        }

        if (string.Equals(name, "Referer", StringComparison.OrdinalIgnoreCase) && headers.Referrer is not null)
        {
            return headers.Referrer.OriginalString;
        }

        return string.Join(", ", fallbackValues);
    }

    private static string SerializeAcceptLanguageValue(StringWithQualityHeaderValue value)
        => value.Quality.HasValue
            ? string.Concat(value.Value, ";q=", value.Quality.Value.ToString("0.0###", CultureInfo.InvariantCulture))
            : value.Value;

    private static string SerializeAcceptEncodingValue(StringWithQualityHeaderValue value)
        => value.Quality.HasValue
            ? string.Concat(value.Value, ";q=", value.Quality.Value.ToString("0.###", CultureInfo.InvariantCulture))
            : value.Value;

    private static string SerializeAcceptValue(MediaTypeWithQualityHeaderValue value)
    {
        using var builder = new ValueStringBuilder(value.MediaType ?? "*/*");

        foreach (var parameter in value.Parameters)
        {
            if (string.Equals(parameter.Name, "q", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(';').Append(parameter.Name);
            if (!string.IsNullOrEmpty(parameter.Value))
            {
                builder.Append('=').Append(parameter.Value);
            }
        }

        if (value.Quality.HasValue)
        {
            builder.Append(";q=").Append(value.Quality.Value.ToString("0.0###", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendHostHeader(ref ValueStringBuilder builder, HttpHeaders headers, string hostHeader)
    {
        if (!ContainsHeader(headers, nameof(HttpRequestHeader.Host)))
            builder.Append("Host: ").Append(hostHeader).Append("\r\n");
    }

    private static void AppendRequestHeaders(ref ValueStringBuilder builder, HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, nameof(HttpRequestHeader.Host), StringComparison.OrdinalIgnoreCase)) continue;
            AppendHeader(ref builder, header.Key, header.Value);
        }
    }

    private static void AppendContentHeaders(ref ValueStringBuilder builder, HttpContent? content, bool hasBody)
    {
        if (content is null) return;

        foreach (var header in content.Headers)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) && hasBody) continue;
            AppendHeader(ref builder, header.Key, header.Value);
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

    private static void AppendHeader(ref ValueStringBuilder builder, string name, IEnumerable<string> values)
    {
        builder.Append(name).Append(": ");
        var isFirst = true;
        foreach (var value in values)
        {
            if (!isFirst)
            {
                builder.Append(", ");
            }

            builder.Append(value);
            isFirst = false;
        }

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