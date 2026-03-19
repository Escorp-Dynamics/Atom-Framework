using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Https.Connections;

namespace Atom.Net.Https;

/// <summary>
/// Минимальный обработчик запросов для текущего H1-среза.
/// На этом этапе поддерживается HTTP/1.1 поверх cleartext и минимального custom TLS 1.2 path через <see cref="Https11Connection"/>.
/// </summary>
public sealed partial class HttpsClientHandler : HttpMessageHandler
{
    private int activeRequests;
    private int isDisposed;
    private readonly ConcurrentDictionary<ConnectionPoolKey, ConnectionPoolState> connectionPool = new();

    /// <summary>
    /// Возвращает или задает значение, которое указывает, должен ли обработчик следовать ответам перенаправления.
    /// </summary>
    public bool AllowAutoRedirect { get; set; } = true;

    /// <summary>
    /// Возвращает или задает тип метода распаковки, используемый обработчиком для автоматической распаковки содержимого HTTP-ответа.
    /// </summary>
    public DecompressionMethods AutomaticDecompression { get; set; }

    /// <summary>
    /// Получает или задает значение, указывающее, проверяется ли сертификат по списку отзыва центра сертификации.
    /// </summary>
    public bool CheckCertificateRevocationList { get; set; }

    /// <summary>
    /// Возвращает или задает значение, указывающее, выбирается ли автоматически сертификат из хранилища сертификатов,
    /// или может ли вызывающий объект передавать определенный клиентский сертификат.
    /// </summary>
    public ClientCertificateOption ClientCertificateOptions { get; set; }

    /// <summary>
    /// Возвращает коллекцию сертификатов безопасности, связанных с запросами к серверу.
    /// </summary>
    public X509CertificateCollection ClientCertificates { get; } = [];

    /// <summary>
    /// Возвращает или задает контейнер файлов cookie, используемый для хранения файлов cookie сервера обработчиком.
    /// </summary>
    public CookieContainer CookieContainer { get; set; } = new();

    /// <summary>
    /// Возвращает или задает сведения о проверке подлинности, используемые данным обработчиком.
    /// </summary>
    public ICredentials? Credentials { get; set; }

    /// <summary>
    /// Если используется прокси-сервер по умолчанию (системный), возвращает или задает учетные данные,
    /// отправляемые на прокси-сервер по умолчанию для проверки подлинности.
    /// Прокси-сервер по умолчанию используется только если <see cref="UseProxy"/> задано значение <see langword="true"/> и <see cref="Proxy"/> задано значение <see langword="null"/>.
    /// </summary>
    public ICredentials? DefaultProxyCredentials { get; set; }

    /// <summary>
    /// Возвращает или задает максимальное количество переадресаций, выполняемых обработчиком.
    /// </summary>
    public int MaxAutomaticRedirections { get; set; } = 50;

    /// <summary>
    /// Возвращает или задает максимально допустимое число одновременных подключений (для каждой конечной точки сервера)
    /// при выполнении запросов с помощью объекта <see cref="HttpClient"/>.
    /// Обратите внимание, что для каждой конечной точки сервера существует ограничение, например,
    /// значение 256 разрешает выполнять 256 одновременных подключений к http://www.adatum.com/
    /// и еще 256 подключений — к http://www.adventure-works.com/.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = int.MaxValue;

    /// <summary>
    /// Получает или задает максимальный размер буфера содержимого запроса, используемого обработчиком.
    /// </summary>
    public long MaxRequestContentBufferSize { get; set; } = 2147483648;

    /// <summary>
    /// Возвращает или задает максимальную длину заголовков ответов, выраженную в килобайтах (1024 байта).
    /// Например, если значение равно 64, для максимальной длины заголовков ответов разрешено использовать 65536 байт.
    /// </summary>
    public int MaxResponseHeadersLength { get; set; } = 65536;

    /// <summary>
    /// Возвращает или задает объект для <see cref="IMeterFactory"/> создания пользовательского <see cref="Meter"/>
    /// объекта для экземпляра <see cref="HttpsClientHandler"/>.
    /// </summary>
    public IMeterFactory? MeterFactory { get; set; }

    /// <summary>
    /// Получает или задает значение, указывающее, будет ли обработчик отправлять заголовок авторизации вместе с запросом.
    /// </summary>
    public bool PreAuthenticate { get; set; }

    /// <summary>
    /// Возвращает доступный для записи словарь (т. е. карту) настраиваемых свойств запросов <see cref="HttpClient"/>.
    /// Словарь инициализируется пустым. Можно вставить и запросить пары "ключ-значение" для пользовательских обработчиков и особой обработки.
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Возвращает или задает сведения о прокси-сервере, используемые обработчиком.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Получает или задает метод обратного вызова для проверки сертификата сервера.
    /// </summary>
    public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateCustomValidationCallback { get; set; }

    /// <summary>
    /// Возвращает или задает протокол TLS/SSL, используемый объектами <see cref="HttpClient"/>, которые управляются объектом <see cref="HttpsClientHandler"/>.
    /// </summary>
    public SslProtocols SslProtocols { get; set; }

    /// <summary>
    /// Возвращает значение, указывающее, поддерживает ли обработчик автоматическое распаковка содержимого ответа.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The property is part of the instance handler surface.")]
    [SuppressMessage("Maintainability", "MA0041:Use a method group instead of a lambda", Justification = "The analyzer misfires on expression-bodied instance properties.")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "The property is part of the instance handler surface.")]
    public bool SupportsAutomaticDecompression => false;

    /// <summary>
    /// Получает значение, указывающее, поддерживает ли обработчик параметры прокси.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The property is part of the instance handler surface.")]
    [SuppressMessage("Maintainability", "MA0041:Use a method group instead of a lambda", Justification = "The analyzer misfires on expression-bodied instance properties.")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "The property is part of the instance handler surface.")]
    public bool SupportsProxy => false;

    /// <summary>
    /// Получает значение, указывающее, поддерживает ли обработчик параметры конфигурации для свойств <see cref="AllowAutoRedirect"/>
    /// и <see cref="MaxAutomaticRedirections"/>.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The property is part of the instance handler surface.")]
    [SuppressMessage("Maintainability", "MA0041:Use a method group instead of a lambda", Justification = "The analyzer misfires on expression-bodied instance properties.")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "The property is part of the instance handler surface.")]
    public bool SupportsRedirectConfiguration => false;

    /// <summary>
    /// Возвращает или задает значение, указывающее, использует <see cref="CookieContainer"/> ли обработчик свойство
    /// для хранения файлов cookie сервера и использует ли эти файлы cookie при отправке запросов.
    /// </summary>
    public bool UseCookies { get; set; } = true;

    /// <summary>
    /// Получает или задает значение, которое управляет отправкой обработчиком учетных данных по умолчанию вместе с запросами.
    /// </summary>
    public bool UseDefaultCredentials { get; set; }

    /// <summary>
    /// Возвращает или задает значение, указывающее, использует ли обработчик прокси-сервер для запросов.
    /// </summary>
    public bool UseProxy { get; set; } = true;

    /// <summary>
    /// Возвращает или задает средство распространения, используемое при распространении распределенной трассировки и контекста.
    /// Используйте <see langword="null"/> для отключения распространения.
    /// </summary>
    public DistributedContextPropagator? ActivityHeadersPropagator { get; set; } = DistributedContextPropagator.Current;

    /// <summary>
    /// Возвращает или задает настраиваемый обратный вызов, используемый для открытия новых подключений.
    /// </summary>
    public Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>? ConnectCallback { get; set; }

    /// <summary>
    /// Возвращает или задает время ожидания для установки подключения.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Возвращает или задает время ожидания получения ответа с кодом HTTP 100 Continue ("Продолжай") от сервера.
    /// </summary>
    public TimeSpan Expect100ContinueTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Возвращает или задает таймаут ожидания стартовых заголовков ответа.
    /// </summary>
    public TimeSpan ResponseHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Позволяет получить или задать пользовательский обратный вызов, который предоставляет доступ к потоку протокола HTTP с обычным текстом.
    /// В текущем срезе значение сохраняется, но не используется.
    /// </summary>
    public Func<SocketsHttpPlaintextStreamFilterContext, CancellationToken, ValueTask<Stream>>? PlaintextStreamFilter { get; set; }

    /// <summary>
    /// Получает или задает время неактивности соединения в пуле.
    /// Пока используется только как часть connection options.
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Получает или задает максимальное абсолютное время жизни соединения в пуле.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Получает кэшированный делегат, который всегда возвращает true.
    /// </summary>
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> DangerousAcceptAnyServerCertificateValidator { get; } = static (_, _, _, _) => true;

    /// <summary>
    /// Формирует атомарный снимок опций соединения из параметров запроса и свойств обработчика.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <returns>Опции соединения для транспортного уровня.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HttpsConnectionOptions BuildConnectionOptions(HttpsRequestMessage request)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("RequestUri не задан");

        // Вычисление схемы/порта без лишних аллокаций.
        // Uri.IdnHost возвращает Punycode, что предпочтительно для SNI/Host.
        var isHttps = uri.Scheme.Length is 5 /* https */ &&
                       (uri.Scheme[0] | 0x20) is 'h' &&
                       (uri.Scheme[1] | 0x20) is 't' &&
                       (uri.Scheme[2] | 0x20) is 't' &&
                       (uri.Scheme[3] | 0x20) is 'p' &&
                       (uri.Scheme[4] | 0x20) is 's';

        var port = uri.Port;
        if (uri.IsDefaultPort)
        {
            port = isHttps ? 443 : 80;
        }

        var versionPolicy = request.VersionPolicy;

        var preferredVersion = request.Version == default ? HttpVersion.Version11 : request.Version;

        return new HttpsConnectionOptions
        {
            Host = uri.IdnHost,
            Port = port,
            IsHttps = isHttps,
            PreferredVersion = preferredVersion,
            VersionPolicy = versionPolicy,
            LocalEndPoint = null,
            ConnectTimeout = ConnectTimeout,
            ResponseHeadersTimeout = ResponseHeadersTimeout,
            SslProtocols = SslProtocols,
            CheckCertificateRevocationList = CheckCertificateRevocationList,
            ServerCertificateValidationCallback = ServerCertificateCustomValidationCallback is null
                ? null
                : (certificate, chain, sslPolicyErrors) => ServerCertificateCustomValidationCallback(request, certificate, chain, sslPolicyErrors),
            MaxResponseHeadersBytes = MaxResponseHeadersLength <= 0 ? int.MaxValue : checked(MaxResponseHeadersLength * 1024),
            IdleTimeout = PooledConnectionIdleTimeout,
            MaxConcurrentStreams = 1,
            AutoDecompression = false,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async Task<HttpsResponseMessage> SendInternalAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref activeRequests);

        HttpsRequestMessage? preparedRequest = null;
        var ownsPreparedRequest = false;
        Https11Connection? connection = null;
        ConnectionPoolState? poolState = null;
        var leaseHeld = false;
        try
        {
#pragma warning disable CA2000
            preparedRequest = PrepareRequest(request, out ownsPreparedRequest);

            var unsupported = TryCreateUnsupportedResponse(request, preparedRequest);
            if (unsupported is not null) return unsupported;

            ApplyRequestCookies(preparedRequest);

            var options = BuildConnectionOptions(preparedRequest);
            (connection, poolState, leaseHeld) = await AcquireConnectionAsync(options, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000

            var (response, returnedToPool) = await SendOverConnectionAsync(preparedRequest, connection, poolState, options, cancellationToken).ConfigureAwait(false);
            if (returnedToPool)
                connection = null;

            return response;
        }
        finally
        {
            if (connection is not null)
                await DisposeLeasedConnectionAsync(connection).ConfigureAwait(false);

            if (leaseHeld && poolState is not null)
                poolState.ReleaseLease();

            DisposePreparedRequest(preparedRequest, ownsPreparedRequest);

            Interlocked.Decrement(ref activeRequests);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override async Task<HttpResponseMessage> SendAsync([NotNull] HttpRequestMessage request, CancellationToken cancellationToken)
        => await SendInternalAsync(request, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new NotSupportedException("Synchronous Send is not supported by HttpsClientHandler. Use SendAsync instead.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HttpsRequestMessage PrepareRequest(HttpRequestMessage request, out bool ownsPreparedRequest)
    {
        if (request is HttpsRequestMessage httpsRequest)
        {
            ownsPreparedRequest = false;
            return httpsRequest;
        }

        var prepared = new HttpsRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version == default ? HttpVersion.Version11 : request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content,
        };

        ownsPreparedRequest = true;

        foreach (var header in request.Headers)
            prepared.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return prepared;
    }

    private HttpsResponseMessage? TryCreateUnsupportedResponse(HttpRequestMessage originalRequest, HttpsRequestMessage preparedRequest)
    {
        _ = preparedRequest.RequestUri ?? throw new InvalidOperationException("RequestUri не задан");

        if (UseProxy && Proxy is not null)
            return HttpsResponseMessage.FromException(originalRequest, TimeSpan.Zero, new NotSupportedException("Прокси path для минимального H1 handler пока не подключён."));

        if (preparedRequest.Version.Major >= 2)
            return HttpsResponseMessage.FromException(originalRequest, TimeSpan.Zero, new NotSupportedException("Минимальный handler path пока поддерживает только HTTP/1.1."));

        return null;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Connection ownership is transferred to SendInternalAsync, which releases or returns the connection from its finally block.")]
    private async ValueTask<(Https11Connection Connection, ConnectionPoolState? PoolState, bool LeaseHeld)> AcquireConnectionAsync(HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        if (MaxConnectionsPerServer <= 0)
            return (new Https11Connection(), null, false);

        var poolKey = new ConnectionPoolKey(options.Host, options.Port, options.IsHttps);
        var poolState = connectionPool.GetOrAdd(poolKey, static (_, arg) => new ConnectionPoolState(arg.MaxConnections), new ConnectionPoolStateFactoryArg(MaxConnectionsPerServer));
        await poolState.WaitForLeaseAsync(cancellationToken).ConfigureAwait(false);

        var pooledConnection = TryRentConnection(poolKey, poolState);
        if (pooledConnection is not null)
            return (pooledConnection, poolState, true);

        return (new Https11Connection(), poolState, true);
    }

    private async ValueTask<(HttpsResponseMessage Response, bool ReturnedToPool)> SendOverConnectionAsync(HttpsRequestMessage preparedRequest, Https11Connection connection, ConnectionPoolState? poolState, HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
            await connection.OpenAsync(options, cancellationToken).ConfigureAwait(false);

        var uri = preparedRequest.RequestUri ?? throw new InvalidOperationException("RequestUri не задан");
        var response = await connection.SendAsync(preparedRequest, cancellationToken).ConfigureAwait(false);
        ApplyResponseCookies(uri, response);

        if (poolState is not null && CanReuseConnection(connection, response))
        {
            ReturnConnection(poolState, connection);
            return (response, true);
        }

        return (response, false);
    }

    private static void DisposePreparedRequest(HttpsRequestMessage? preparedRequest, bool ownsPreparedRequest)
    {
        if (!ownsPreparedRequest || preparedRequest is null) return;

        preparedRequest.Content = null;
        preparedRequest.Dispose();
    }

    private static ValueTask DisposeLeasedConnectionAsync(Https11Connection connection)
        => DisposeConnectionAsync(connection);

    private void ApplyRequestCookies(HttpsRequestMessage request)
    {
        if (!UseCookies || request.RequestUri is null || request.Headers.Contains("Cookie")) return;

        var cookies = CookieContainer.GetCookieHeader(request.RequestUri);
        if (!string.IsNullOrWhiteSpace(cookies))
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
    }

    private void ApplyResponseCookies(Uri uri, HttpsResponseMessage response)
    {
        if (!UseCookies || response.Exception is not null) return;
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;

        foreach (var value in values)
            CookieContainer.SetCookies(uri, value);
    }

    /// <summary>
    /// Освобождает ресурсы обработчика.
    /// </summary>
    /// <param name="disposing">Указывает, нужно ли освобождать управляемые ресурсы.</param>
    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        Volatile.Write(ref isDisposed, value: 1);

        foreach (var pair in connectionPool)
        {
            while (pair.Value.Connections.TryDequeue(out var connection))
                DisposeConnection(connection);

            pair.Value.Dispose();
        }

        connectionPool.Clear();
        base.Dispose(disposing);
    }
}