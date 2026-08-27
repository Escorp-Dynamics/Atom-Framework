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
    private static readonly SearchValues<char> tokenSeparators = SearchValues.Create(" ;");
    private Traffic traffic;
    private Stream? transport;
    private TcpStream? socketTransport;
    private HttpsConnectionOptions options;
    private readonly byte[] receiveBuffer = new byte[4096];

    /// <summary>
    /// Словарь заголовков, переиспользуемый между запросами этого соединения.
    /// </summary>
    /// <remarks>
    /// Соединение HTTP/1.1 обслуживает запросы строго по очереди, поэтому словарь можно
    /// переиспользовать — и это заметно: на каждый запрос иначе создаётся словарь со своими
    /// внутренними массивами, а запросов на соединение проходят тысячи.
    /// </remarks>
    private readonly Dictionary<string, string> headerMap = new(StringComparer.OrdinalIgnoreCase);
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


#pragma warning disable MA0196 // Do not use inheritdoc on non-inheriting members
    /// <inheritdoc/>
    public override long CreatedTimestamp => Volatile.Read(ref createdTimestamp);
#pragma warning restore MA0196 // Do not use inheritdoc on non-inheriting members

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

        // Предлагаем в ALPN только http/1.1: это соединение умеет говорить лишь на нём, и позволить
        // серверу выбрать h2 значило бы продолжить обмен по 1.1 и развалиться на первом же кадре.
        // Когда протокол выбирает вызывающая сторона по результату согласования, транспорт приходит
        // готовым через Adopt, и этот путь не используется.
        var established = await HttpsTransportConnector.ConnectAsync(options, HttpsTransportConnector.Http11Only, cancellationToken).ConfigureAwait(false);

        if (established.IsSecure && established.NegotiatedProtocol is { Length: > 0 } protocol && !string.Equals(protocol, "http/1.1", StringComparison.Ordinal))
        {
            await DisposeTransportsAsync(established.Transport, established.Socket).ConfigureAwait(false);
            throw new NotSupportedException($"Сервер согласовал протокол '{protocol}', а это соединение поддерживает только http/1.1");
        }

        Adopt(established, options);
    }

    /// <summary>
    /// Принимает уже установленный транспорт.
    /// </summary>
    /// <param name="established">Установленный транспорт.</param>
    /// <param name="options">Параметры соединения.</param>
    /// <remarks>
    /// Нужен там, где протокол выбирается по ALPN: рукопожатие к этому моменту уже состоялось, и
    /// повторное подключение ради «правильного» класса соединения означало бы лишний оборот
    /// TCP и TLS на каждый первый запрос к узлу.
    /// </remarks>
    internal void Adopt(in HttpsTransport established, HttpsConnectionOptions options)
    {
        transport = established.Transport;
        socketTransport = established.Socket;
        this.options = options;
        receiveOffset = 0;
        receiveCount = 0;
        Volatile.Write(ref createdTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref isConnected, 1);
        Volatile.Write(ref isDraining, 0);
        TouchActivity();
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
    private static async ValueTask<byte[]> ReadRequestBodyAsync(HttpContent? content, CancellationToken cancellationToken)
        => content is null ? [] : await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

    private byte[] BuildRequestHead(HttpsRequestMessage request, int bodyLength)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("RequestUri не задан.");

        // Прокси без туннеля обслуживает запрос сам, и адрес ему нужен целиком: по одному пути он
        // не знает, к какому узлу обращаться. За туннелем и при прямом подключении, наоборот,
        // абсолютный адрес — нарушение формы запроса.
        var target = options.UpstreamProxy is not null && !options.IsHttps
            ? uri.AbsoluteUri
            : string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
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

            // ★ Нулевую длину объявлять ОБЯЗАТЕЛЬНО, если метод подразумевает тело. Запрос
            // POST без Content-Length и без Transfer-Encoding сервер вправе отвергнуть, и
            // отвергает: nginx, Apache и IIS отвечают на такой «411 Length Required».
            if (hasBody || MethodImpliesBody(request.Method))
                builder.Append("Content-Length: ").Append((hasBody ? bodyLength : 0).ToString(CultureInfo.InvariantCulture)).Append("\r\n");

            builder.Append("\r\n");
            // Latin1, а не ASCII: она переносит байты 1:1. ASCII молча заменяла всё старше
            // 0x7F на «?», то есть портила значение, которое вызывающая сторона задала сама —
            // например куку или referer с непроцентированным путём.
            return Encoding.Latin1.GetBytes(builder.ToString());
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

        var headers = BuildHeaderMap(headerMap, request, hasBody, hostHeader, IsDraining);
        foreach (var header in formattingPolicy.Format(headers, HttpVersion.Version11, request.EffectiveKind, request.UseCookieCrumbling))
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }
    }

    private static Dictionary<string, string> BuildHeaderMap(Dictionary<string, string> headers, HttpsRequestMessage request, bool hasBody, string? hostHeader, bool isDraining)
    {
        headers.Clear();

        if (!string.IsNullOrEmpty(hostHeader))
        {
            headers[nameof(HttpRequestHeader.Host)] = request.Headers.Host ?? hostHeader;
        }

        // Значения берутся НЕРАЗОБРАННЫМИ — теми, что были записаны. Прежде здесь стояла ручная
        // склейка, разбиравшая каждый типизированный заголовок особым случаем: она повторяла
        // работу платформы и расходилась с ней на всём, чего не предусмотрела.
        foreach (var header in request.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToString();
        }

        if (isDraining && !headers.ContainsKey(nameof(HttpRequestHeader.Connection)))
        {
            headers[nameof(HttpRequestHeader.Connection)] = "close";
        }

        if (request.Content is null)
        {
            return headers;
        }

        foreach (var header in request.Content.Headers.NonValidated)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) && hasBody)
            {
                continue;
            }

            headers[header.Key] = header.Value.ToString();
        }

        return headers;
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

    /// <summary>
    /// Подразумевает ли метод наличие тела.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <returns><see langword="true"/> для методов, у которых тело ожидается.</returns>
    /// <remarks>
    /// Для таких методов длину надо объявлять даже при пустом теле: отсутствие и длины, и
    /// признака кусочной передачи сервер вправе счесть ошибкой запроса.
    /// </remarks>
    private static bool MethodImpliesBody(HttpMethod method)
        => method == HttpMethod.Post
        || method == HttpMethod.Put
        || method == HttpMethod.Patch;

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
            : $"{uri.IdnHost}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
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

        /// <summary>Объявленная сервером кодировка содержимого.</summary>
        public string? ContentEncoding { get; set; }

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