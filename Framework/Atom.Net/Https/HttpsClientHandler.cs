using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Https;

/// <summary>
/// Минимальный обработчик запросов для текущего H1-среза.
/// На этом этапе поддерживается HTTP/1.1 поверх cleartext и минимального custom TLS 1.2 path через <see cref="Https11Connection"/>.
/// </summary>
public sealed partial class HttpsClientHandler : HttpMessageHandler
{
    private static readonly HashSet<string> commonMultiLabelPublicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk",
        "org.uk",
        "gov.uk",
        "ac.uk",
        "com.au",
        "net.au",
        "org.au",
        "co.nz",
        "com.br",
        "com.mx",
        "co.jp",
    };

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
    /// Разрешённый browser profile, выбранный явно или через User-Agent adapter.
    /// На текущем H1/TLS slice используется как источник базового preset-снимка, а не как полный browser runtime.
    /// </summary>
    public BrowserProfile? BrowserProfile { get; set; }

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
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Возвращает или задает время ожидания получения ответа с кодом HTTP 100 Continue ("Продолжай") от сервера.
    /// </summary>
    public TimeSpan Expect100ContinueTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Возвращает или задает таймаут ожидания стартовых заголовков ответа.
    /// </summary>
    public TimeSpan ResponseHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Возвращает или задает таймаут подготовки и отправки запроса до начала чтения ответа.
    /// </summary>
    public TimeSpan RequestSendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Возвращает или задает таймаут чтения тела ответа после получения заголовков.
    /// </summary>
    public TimeSpan ResponseBodyTimeout { get; set; } = TimeSpan.FromSeconds(30);

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
        var profile = BrowserProfile;

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

        var versionPolicy = profile?.VersionPolicy ?? request.VersionPolicy;

        var preferredVersion = request.Version == default
            ? profile?.PreferredHttpVersion ?? HttpVersion.Version11
            : request.Version;

        var tcpSettings = BuildTcpSettings(profile);
        var tlsSettings = BuildTlsSettings(profile, request);

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
            RequestSendTimeout = RequestSendTimeout,
            ResponseBodyTimeout = ResponseBodyTimeout,
            SslProtocols = SslProtocols,
            CheckCertificateRevocationList = CheckCertificateRevocationList,
            ServerCertificateValidationCallback = ServerCertificateCustomValidationCallback is null
                ? null
                : (certificate, chain, sslPolicyErrors) => ServerCertificateCustomValidationCallback(request, certificate, chain, sslPolicyErrors),
            MaxResponseHeadersBytes = MaxResponseHeadersLength <= 0 ? int.MaxValue : checked(MaxResponseHeadersLength * 1024),
            IdleTimeout = PooledConnectionIdleTimeout,
            MaxConcurrentStreams = 1,
            AutoDecompression = false,
            ProfileTcpSettings = tcpSettings,
            ProfileTlsSettings = tlsSettings,
        };
    }

    private TcpSettings BuildTcpSettings(BrowserProfile? profile)
    {
        var profileSettings = profile?.Tcp ?? new TcpSettings();
        return profileSettings with
        {
            ConnectTimeout = ConnectTimeout,
            LocalEndPoint = null,
        };
    }

    private TlsSettings BuildTlsSettings(BrowserProfile? profile, HttpRequestMessage request)
    {
        var profileSettings = profile?.Tls ?? new TlsSettings();
        return profileSettings with
        {
            CheckCertificateRevocationList = CheckCertificateRevocationList,
            ServerCertificateValidationCallback = ServerCertificateCustomValidationCallback is null
                ? null
                : (certificate, chain, sslPolicyErrors) => ServerCertificateCustomValidationCallback(request, certificate, chain, sslPolicyErrors),
            MinVersion = ResolveTlsBoundary(SslProtocols, profileSettings.MinVersion),
            MaxVersion = ResolveTlsBoundary(SslProtocols, profileSettings.MaxVersion),
        };
    }

    private static SslProtocols ResolveTlsBoundary(SslProtocols configuredProtocols, SslProtocols fallback)
    {
        if (configuredProtocols is SslProtocols.None)
        {
            return fallback;
        }

        return (configuredProtocols & SslProtocols.Tls12) == SslProtocols.Tls12
            ? SslProtocols.Tls12
            : configuredProtocols;
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

            ApplyBrowserProfileDefaults(preparedRequest);
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

        HttpsRequestOptions.TryGetRequestKind(request, out var requestKind);
        HttpsRequestOptions.TryGetBrowserRequestContext(request, out var browserRequestContext);
        HttpsRequestOptions.TryGetReferrerPolicy(request, out var referrerPolicy);

        var prepared = new HttpsRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version == default ? HttpVersion.Version11 : request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content,
            Kind = requestKind,
            Context = browserRequestContext,
            ReferrerPolicy = referrerPolicy,
        };

        ownsPreparedRequest = true;

        foreach (var header in request.Headers)
            prepared.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in request.Options)
            prepared.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

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

    private void ApplyBrowserProfileDefaults(HttpsRequestMessage request)
    {
        var profile = BrowserProfile;
        if (profile is null)
        {
            return;
        }

        var headerProfile = profile.Value.Headers;
        var requestKind = request.Kind is RequestKind.Unknown ? headerProfile.DefaultRequestKind : request.Kind;

        request.EffectiveKind = requestKind;
        request.EffectiveReferrerPolicy = ResolveEffectiveReferrerPolicy(request, headerProfile);
        request.UseCookieCrumbling = headerProfile.UseCookieCrumbling;
        request.HeadersFormattingPolicy = ResolveHeadersFormattingPolicy(profile.Value, headerProfile);

        var requestContext = CreateRequestContextSnapshot(request, profile.Value, requestKind);

        AddHeaderIfMissing(request, "User-Agent", profile.Value.UserAgent);
        AddHeaderIfMissing(request, "Accept", GetDefaultAcceptValue(profile.Value, requestContext));

        if (headerProfile.EmitAcceptEncoding)
        {
            AddHeaderIfMissing(request, "Accept-Encoding", GetDefaultAcceptEncodingValue(profile.Value, requestContext));
        }

        if (headerProfile.EmitAcceptLanguage)
        {
            AddHeaderIfMissing(request, "Accept-Language", GetDefaultAcceptLanguageValue(profile.Value));
        }

        ApplyPriorityDefaults(request, profile.Value, requestContext);

        ApplyRangeDefaults(request, requestContext);

        ApplyRequestKindDefaults(request, requestContext);

        if (headerProfile.UseConnectionKeepAlive && !request.Headers.Contains("Connection") && request.Headers.ConnectionClose != true)
        {
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        }

        if (headerProfile.UseClientHints)
        {
            ApplyClientHintsDefaults(request, profile.Value);
        }
    }

    private static void ApplyClientHintsDefaults(HttpsRequestMessage request, BrowserProfile profile)
    {
        if (!TryCreateSecChUaValue(profile.UserAgent, out var secChUa))
        {
            return;
        }

        AddHeaderIfMissing(request, "sec-ch-ua", secChUa);
        AddHeaderIfMissing(request, "sec-ch-ua-mobile", "?0");
        AddHeaderIfMissing(request, "sec-ch-ua-platform", GetSecChUaPlatformValue(profile.UserAgent));
    }

    private static void ApplyPriorityDefaults(HttpsRequestMessage request, BrowserProfile profile, in RequestContextSnapshot requestContext)
    {
        var priority = GetDefaultPriorityValue(profile, requestContext);
        if (priority is null)
        {
            return;
        }

        AddHeaderIfMissing(request, "Priority", priority);
    }

    private static void ApplyRangeDefaults(HttpsRequestMessage request, in RequestContextSnapshot requestContext)
    {
        if (request.Headers.Range is not null)
        {
            return;
        }

        if (IsMediaElementRequest(requestContext))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, to: null);
        }
    }

    private static void AddHeaderIfMissing(HttpsRequestMessage request, string name, string value)
    {
        if (request.Headers.Contains(name))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(name, value);
    }

    internal static ReferrerPolicyMode ResolveEffectiveReferrerPolicy(HttpsRequestMessage request, in BrowserHeaderProfile headerProfile)
        => request.ReferrerPolicy ?? headerProfile.DefaultReferrerPolicy;

    private static RequestContextSnapshot CreateRequestContextSnapshot(HttpsRequestMessage request, in BrowserProfile profile, RequestKind requestKind)
    {
        var explicitContext = request.Context as HttpsBrowserRequestContext;
        var requestUri = request.RequestUri;
        var sourceReferrer = request.Headers.Referrer;
        var derivedReferrer = DeriveReferer(requestUri, sourceReferrer, request.EffectiveReferrerPolicy);
        var destination = GetRequestDestination(profile, requestKind, requestUri, explicitContext);
        var secFetchSite = destination is RequestDestination.ServiceWorker
            ? "same-origin"
            : GetSecFetchSite(requestUri, sourceReferrer, requestKind, destination);
        var secFetchMode = GetRequestMode(requestKind, destination, explicitContext);
        var isReload = (requestKind is RequestKind.Navigation)
            && explicitContext?.IsReload == true;
        var isFormSubmission = (requestKind is RequestKind.Navigation)
            && (explicitContext?.IsFormSubmission ?? !IsSafeMethod(request.Method));
        var isUserActivated = (requestKind is RequestKind.Navigation)
            && !isReload
            && (explicitContext?.IsUserActivated ?? (destination is RequestDestination.Document || explicitContext?.IsFormSubmission == true));

        return new RequestContextSnapshot(
            requestKind,
            requestUri,
            sourceReferrer,
            derivedReferrer,
            secFetchSite,
            destination,
            secFetchMode,
            isUserActivated,
            isReload,
            isFormSubmission);
    }

    private static void ApplyRequestKindDefaults(HttpsRequestMessage request, in RequestContextSnapshot requestContext)
    {
        request.Headers.Referrer = requestContext.DerivedReferrer;
        AddHeaderIfMissing(request, "sec-fetch-site", requestContext.SecFetchSite);
        AddHeaderIfMissing(request, "sec-fetch-mode", requestContext.SecFetchMode);
        AddHeaderIfMissing(request, "sec-fetch-dest", GetRequestDestinationValue(requestContext.Destination));

        switch (requestContext.Kind)
        {
            case RequestKind.Navigation:
                if (requestContext.IsUserActivated)
                {
                    AddHeaderIfMissing(request, "sec-fetch-user", "?1");
                }

                AddHeaderIfMissing(request, "Upgrade-Insecure-Requests", "1");
                break;
            case RequestKind.Prefetch:
                AddHeaderIfMissing(request, "Sec-Purpose", "prefetch");
                break;
        }

        if (requestContext.Destination is RequestDestination.ServiceWorker)
        {
            AddHeaderIfMissing(request, "Service-Worker", "script");
        }

        ApplyOriginDefault(request, requestContext.RequestUri, requestContext.SourceReferrer, requestContext.DerivedReferrer, requestContext.SecFetchSite, requestContext.SecFetchMode, requestContext.Kind, requestContext.Destination, requestContext.IsFormSubmission);
    }

    internal static Uri? DeriveReferer(Uri? requestUri, Uri? sourceReferrer, ReferrerPolicyMode policy)
    {
        if (sourceReferrer is null)
        {
            return null;
        }

        if (requestUri is null)
        {
            return sourceReferrer;
        }

        return policy switch
        {
            ReferrerPolicyMode.NoReferrer => null,
            ReferrerPolicyMode.Origin => GetRefererOriginUri(sourceReferrer),
            ReferrerPolicyMode.SameOrigin => UrisShareOrigin(requestUri, sourceReferrer) ? sourceReferrer : null,
            ReferrerPolicyMode.StrictOrigin => IsSecureToInsecureDowngrade(requestUri, sourceReferrer) ? null : GetRefererOriginUri(sourceReferrer),
            ReferrerPolicyMode.StrictOriginWhenCrossOrigin => DeriveStrictOriginWhenCrossOriginReferer(requestUri, sourceReferrer),
            ReferrerPolicyMode.UnsafeUrl => sourceReferrer,
            _ => sourceReferrer,
        };
    }

    private static Uri? DeriveStrictOriginWhenCrossOriginReferer(Uri requestUri, Uri sourceReferrer)
    {
        if (UrisShareOrigin(requestUri, sourceReferrer))
        {
            return sourceReferrer;
        }

        return IsSecureToInsecureDowngrade(requestUri, sourceReferrer)
            ? null
            : GetRefererOriginUri(sourceReferrer);
    }

    private static void ApplyOriginDefault(HttpsRequestMessage request, Uri? requestUri, Uri? sourceReferrer, Uri? derivedReferrer, string secFetchSite, string secFetchMode, RequestKind requestKind, RequestDestination destination, bool isFormSubmission)
    {
        if (requestUri is null || sourceReferrer is null || request.Headers.Contains("Origin"))
        {
            return;
        }

        var originValue = GetDefaultOriginValue(request.Method, requestKind, secFetchSite, secFetchMode, sourceReferrer, derivedReferrer, destination, isFormSubmission);
        if (originValue is null)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation("Origin", originValue);
    }

    private static string? GetDefaultOriginValue(HttpMethod method, RequestKind requestKind, string secFetchSite, string secFetchMode, Uri sourceReferrer, Uri? derivedReferrer, RequestDestination destination, bool isFormSubmission)
    {
        if (requestKind is RequestKind.Navigation)
        {
            return !isFormSubmission || IsSafeMethod(method) ? null : GetOrigin(sourceReferrer);
        }

        if (!ShouldEmitOriginHeader(method, requestKind, secFetchSite, secFetchMode, destination))
        {
            return null;
        }

        var originSource = derivedReferrer ?? ResolveOriginSourceFromSuppressedReferer(sourceReferrer);
        return originSource is null ? null : GetOrigin(originSource);
    }

    private static Uri ResolveOriginSourceFromSuppressedReferer(Uri sourceReferrer)
        => sourceReferrer;

    private static bool ShouldEmitOriginHeader(HttpMethod method, RequestKind requestKind, string secFetchSite, string secFetchMode, RequestDestination destination)
        => requestKind is RequestKind.ModulePreload
            || (destination is RequestDestination.Font or RequestDestination.Script
                && string.Equals(secFetchMode, "cors", StringComparison.OrdinalIgnoreCase))
            || (destination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker
                && string.Equals(secFetchMode, "cors", StringComparison.OrdinalIgnoreCase))
            || !IsSafeMethod(method)
            || !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeMethod(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private static string GetDefaultAcceptValue(in BrowserProfile profile, in RequestContextSnapshot requestContext)
    {
        if (requestContext.Kind is RequestKind.Preload or RequestKind.ModulePreload)
        {
            return GetPreloadAcceptValue(profile, requestContext.Destination);
        }

        if (requestContext.Kind is not RequestKind.Navigation && HasDestinationAwareSubresourceAccept(requestContext.Destination))
        {
            return GetSubresourceAcceptValue(profile, requestContext.Destination);
        }

        if (requestContext.Kind is not RequestKind.Navigation)
        {
            return "*/*";
        }

        if (IsFirefoxProfile(profile))
        {
            return "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        }

        if (IsSafariProfile(profile))
        {
            return "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        }

        return "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
    }

    private static bool HasDestinationAwareSubresourceAccept(RequestDestination destination)
        => destination is RequestDestination.Style or RequestDestination.Image;

    private static string GetSubresourceAcceptValue(in BrowserProfile profile, RequestDestination destination)
    {
        if (IsSafariProfile(profile))
        {
            return "*/*";
        }

        return destination switch
        {
            RequestDestination.Style => "text/css,*/*;q=0.1",
            RequestDestination.Image when IsFirefoxProfile(profile) => "image/avif,image/webp,image/*,*/*;q=0.8",
            RequestDestination.Image => "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8",
            _ => "*/*",
        };
    }

    private static string GetPreloadAcceptValue(in BrowserProfile profile, RequestDestination preloadDestination)
    {
        return HasDestinationAwareSubresourceAccept(preloadDestination)
            ? GetSubresourceAcceptValue(profile, preloadDestination)
            : "*/*";
    }

    private static string GetPreloadMode(RequestDestination preloadDestination)
        => preloadDestination is RequestDestination.Font or RequestDestination.Manifest
            ? "cors"
            : preloadDestination is RequestDestination.Track
                ? "same-origin"
            : "no-cors";

    private static string GetDefaultAcceptLanguageValue(in BrowserProfile profile)
        => IsFirefoxProfile(profile)
            ? "en-US,en;q=0.5"
            : IsSafariProfile(profile)
                ? "en-US"
                : "en-US,en;q=0.9";

    private static string GetDefaultAcceptEncodingValue(in BrowserProfile profile, in RequestContextSnapshot requestContext)
    {
        if (IsMediaElementRequest(requestContext))
        {
            return "identity;q=1, *;q=0";
        }

        if (!IsFirefoxProfile(profile)
            && !IsSafariProfile(profile)
            && ((requestContext.Kind is RequestKind.Navigation && requestContext.IsFormSubmission)
                || requestContext.Kind is RequestKind.ModulePreload or RequestKind.Prefetch
                || (requestContext.Destination is RequestDestination.Script
                    && string.Equals(requestContext.SecFetchMode, "cors", StringComparison.OrdinalIgnoreCase))
                || (requestContext.Destination is RequestDestination.Style
                    && string.Equals(requestContext.SecFetchMode, "no-cors", StringComparison.OrdinalIgnoreCase))))
        {
            return "gzip, deflate";
        }

        if (!IsFirefoxProfile(profile)
            && !IsSafariProfile(profile)
            && requestContext.Destination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker)
        {
            return "gzip, deflate";
        }

        return IsSafariProfile(profile)
            ? "gzip, deflate, br"
            : "gzip, deflate, br, zstd";
    }

    private static bool IsMediaElementRequest(in RequestContextSnapshot requestContext)
        => requestContext.Destination is RequestDestination.Audio or RequestDestination.Video
            && string.Equals(requestContext.SecFetchMode, "no-cors", StringComparison.OrdinalIgnoreCase);

    private static string? GetDefaultPriorityValue(in BrowserProfile profile, in RequestContextSnapshot requestContext)
    {
        if (IsSafariProfile(profile))
        {
            return null;
        }

        if (IsFirefoxProfile(profile))
        {
            return requestContext.Kind switch
            {
                RequestKind.Navigation => "u=0, i",
                RequestKind.Fetch => "u=4",
                _ => null,
            };
        }

        if (requestContext.Kind is RequestKind.Navigation)
        {
            if (requestContext.IsFormSubmission)
            {
                return null;
            }

            return "u=0, i";
        }

        if (requestContext.Kind is RequestKind.Preload or RequestKind.ModulePreload or RequestKind.Prefetch)
        {
            return null;
        }

        if (requestContext.Kind is RequestKind.ServiceWorker)
        {
            return null;
        }

        if (requestContext.Kind is RequestKind.Fetch
            && requestContext.Destination is not RequestDestination.Empty)
        {
            return null;
        }

        if (requestContext.Kind is not RequestKind.Preload and not RequestKind.ModulePreload)
        {
            return "u=1, i";
        }

        return null;
    }

    private static RequestDestination GetRequestDestination(in BrowserProfile profile, RequestKind requestKind, Uri? requestUri, HttpsBrowserRequestContext? explicitContext)
    {
        if (explicitContext?.Destination is { } explicitDestination)
        {
            return MapRequestDestination(explicitDestination);
        }

        if (TryInferRequestDestination(requestKind, explicitContext, out var inferredDestination))
        {
            return inferredDestination;
        }

        return requestKind switch
        {
            RequestKind.Navigation => RequestDestination.Document,
            RequestKind.Preload or RequestKind.ModulePreload => GetPreloadDestination(profile, requestUri),
            RequestKind.Prefetch => RequestDestination.Empty,
            RequestKind.ServiceWorker => RequestDestination.ServiceWorker,
            _ => RequestDestination.Empty,
        };
    }

    private static string GetRequestMode(RequestKind requestKind, RequestDestination destination, HttpsBrowserRequestContext? explicitContext)
    {
        if (explicitContext?.FetchMode is { } explicitMode)
        {
            return MapFetchMode(explicitMode);
        }

        if (TryInferRequestMode(requestKind, destination, explicitContext, out var inferredMode))
        {
            return inferredMode;
        }

        return requestKind switch
        {
            RequestKind.Navigation => "navigate",
            RequestKind.Preload => GetPreloadMode(destination),
            RequestKind.ModulePreload => "cors",
            RequestKind.Prefetch => "no-cors",
            RequestKind.ServiceWorker => "same-origin",
            _ => "cors",
        };
    }

    private static bool TryInferRequestDestination(RequestKind requestKind, HttpsBrowserRequestContext? explicitContext, out RequestDestination destination)
    {
        destination = RequestDestination.Empty;
        if (explicitContext is null)
        {
            return false;
        }

        if (requestKind is RequestKind.Navigation)
        {
            destination = explicitContext.IsTopLevelNavigation == false
                ? RequestDestination.Iframe
                : RequestDestination.Document;
            return true;
        }

        destination = explicitContext.InitiatorType switch
        {
            HttpsRequestInitiatorType.Worker => RequestDestination.Worker,
            HttpsRequestInitiatorType.SharedWorker => RequestDestination.SharedWorker,
            HttpsRequestInitiatorType.ServiceWorker => RequestDestination.ServiceWorker,
            _ => RequestDestination.Empty,
        };

        return destination is not RequestDestination.Empty;
    }

    private static bool TryInferRequestMode(RequestKind requestKind, RequestDestination destination, HttpsBrowserRequestContext? explicitContext, out string mode)
    {
        mode = string.Empty;
        if (explicitContext is null)
        {
            return false;
        }

        if (requestKind is RequestKind.Navigation)
        {
            mode = "navigate";
            return true;
        }

        if (destination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker)
        {
            mode = "same-origin";
            return true;
        }

        return false;
    }

    private static RequestDestination MapRequestDestination(HttpsRequestDestination destination)
        => destination switch
        {
            HttpsRequestDestination.Document => RequestDestination.Document,
            HttpsRequestDestination.Iframe => RequestDestination.Iframe,
            HttpsRequestDestination.Script => RequestDestination.Script,
            HttpsRequestDestination.Style => RequestDestination.Style,
            HttpsRequestDestination.Image => RequestDestination.Image,
            HttpsRequestDestination.Font => RequestDestination.Font,
            HttpsRequestDestination.Audio => RequestDestination.Audio,
            HttpsRequestDestination.Video => RequestDestination.Video,
            HttpsRequestDestination.Track => RequestDestination.Track,
            HttpsRequestDestination.Manifest => RequestDestination.Manifest,
            HttpsRequestDestination.ServiceWorker => RequestDestination.ServiceWorker,
            HttpsRequestDestination.Worker => RequestDestination.Worker,
            HttpsRequestDestination.SharedWorker => RequestDestination.SharedWorker,
            _ => RequestDestination.Empty,
        };

    private static string MapFetchMode(HttpsFetchMode mode)
        => mode switch
        {
            HttpsFetchMode.Navigate => "navigate",
            HttpsFetchMode.NoCors => "no-cors",
            HttpsFetchMode.SameOrigin => "same-origin",
            _ => "cors",
        };

    private static string GetRequestDestinationValue(RequestDestination destination)
        => destination switch
        {
            RequestDestination.Document => "document",
            RequestDestination.Iframe => "iframe",
            RequestDestination.Script => "script",
            RequestDestination.Style => "style",
            RequestDestination.Image => "image",
            RequestDestination.Font => "font",
            RequestDestination.Audio => "audio",
            RequestDestination.Video => "video",
            RequestDestination.Track => "track",
            RequestDestination.Manifest => "manifest",
            RequestDestination.ServiceWorker => "serviceworker",
            RequestDestination.Worker => "worker",
            RequestDestination.SharedWorker => "sharedworker",
            _ => "empty",
        };

    private static RequestDestination GetPreloadDestination(in BrowserProfile profile, Uri? requestUri)
    {
        if (IsSafariProfile(profile))
        {
            return RequestDestination.Empty;
        }

        var extension = requestUri is null
            ? string.Empty
            : Path.GetExtension(requestUri.AbsolutePath);

        return extension.ToLowerInvariant() switch
        {
            ".css" => RequestDestination.Style,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".avif" or ".svg" or ".ico" => RequestDestination.Image,
            ".woff" or ".woff2" or ".ttf" or ".otf" => RequestDestination.Font,
            ".mp4" or ".webm" or ".ogg" => RequestDestination.Video,
            ".mp3" or ".wav" or ".aac" or ".flac" => RequestDestination.Audio,
            ".vtt" => RequestDestination.Track,
            ".webmanifest" or ".manifest" => RequestDestination.Manifest,
            _ => RequestDestination.Script,
        };
    }

    private static string GetSecFetchSite(Uri? requestUri, Uri? referrer, RequestKind requestKind, RequestDestination destination)
    {
        if (referrer is null)
        {
            return requestKind is RequestKind.Navigation && destination is RequestDestination.Document
                ? "none"
                : "same-origin";
        }

        if (requestUri is null)
        {
            return "same-origin";
        }

        if (UrisShareOrigin(requestUri, referrer))
        {
            return "same-origin";
        }

        if (UrisShareSite(requestUri, referrer))
        {
            return "same-site";
        }

        return "cross-site";
    }

    private static bool UrisShareOrigin(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
        && GetEffectivePort(left) == GetEffectivePort(right);

    private static bool IsSecureToInsecureDowngrade(Uri requestUri, Uri sourceReferrer)
        => IsSecureScheme(sourceReferrer.Scheme) && !IsSecureScheme(requestUri.Scheme);

    private static bool IsSecureScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool UrisShareSite(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(GetSiteKey(left.IdnHost), GetSiteKey(right.IdnHost), StringComparison.OrdinalIgnoreCase);

    private static int GetEffectivePort(Uri uri)
        => uri.IsDefaultPort
            ? uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;

    private static string GetSiteKey(string host)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return host;
        }

        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return host;
        }

        var publicSuffix = string.Concat(parts[^2], ".", parts[^1]);
        if (parts.Length >= 3 && commonMultiLabelPublicSuffixes.Contains(publicSuffix))
        {
            return string.Concat(parts[^3], ".", publicSuffix);
        }

        return string.Concat(parts[^2], ".", parts[^1]);
    }

    private static string GetOrigin(Uri uri)
    {
        var authority = uri.IsDefaultPort
            ? uri.IdnHost
            : string.Concat(uri.IdnHost, ":", uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return string.Concat(uri.Scheme, "://", authority);
    }

    private static Uri GetRefererOriginUri(Uri uri)
        => new(string.Concat(GetOrigin(uri), "/"), UriKind.Absolute);

    private static IHeadersFormattingPolicy? ResolveHeadersFormattingPolicy(in BrowserProfile profile, in BrowserHeaderProfile headerProfile)
    {
        if (!headerProfile.UseOriginalHeaderCase && !headerProfile.UsePreserveHeaderOrder)
        {
            return null;
        }

        if (IsFirefoxProfile(profile))
        {
            return HeadersFormattingPolicy.Firefox;
        }

        if (IsSafariProfile(profile))
        {
            return HeadersFormattingPolicy.Safari;
        }

        if (profile.DisplayName.Contains("Edge", StringComparison.OrdinalIgnoreCase)
            || profile.UserAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return HeadersFormattingPolicy.Edge;
        }

        return HeadersFormattingPolicy.Chrome;
    }

    private static bool IsFirefoxProfile(in BrowserProfile profile)
        => profile.DisplayName.Contains("Firefox", StringComparison.OrdinalIgnoreCase)
        || profile.UserAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafariProfile(in BrowserProfile profile)
        => profile.DisplayName.Contains("Safari", StringComparison.OrdinalIgnoreCase)
        || (profile.UserAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
            && !profile.UserAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)
            && !profile.UserAgent.Contains("Chromium/", StringComparison.OrdinalIgnoreCase));

    private static string GetSecChUaPlatformValue(string userAgent)
    {
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "\"Windows\"";
        }

        if (userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
        {
            return "\"macOS\"";
        }

        return "\"Linux\"";
    }

    private static bool TryCreateSecChUaValue(string userAgent, [NotNullWhen(true)] out string? value)
    {
        var brand = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase)
            ? "Microsoft Edge"
            : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Chromium/", StringComparison.OrdinalIgnoreCase)
                ? "Google Chrome"
                : null;

        if (brand is null)
        {
            value = null;
            return false;
        }

        var majorVersion = ExtractChromiumMajorVersion(userAgent);
        value = string.Concat("\"Not_A Brand\";v=\"24\", \"Chromium\";v=\"", majorVersion, "\", \"", brand, "\";v=\"", majorVersion, "\"");
        return true;
    }

    private static string ExtractChromiumMajorVersion(string userAgent)
    {
        var chromeMarker = userAgent.IndexOf("Chrome/", StringComparison.OrdinalIgnoreCase);
        if (chromeMarker >= 0)
        {
            return ExtractVersionComponent(userAgent, chromeMarker + "Chrome/".Length);
        }

        var edgeMarker = userAgent.IndexOf("Edg/", StringComparison.OrdinalIgnoreCase);
        if (edgeMarker >= 0)
        {
            return ExtractVersionComponent(userAgent, edgeMarker + "Edg/".Length);
        }

        return "0";
    }

    private static string ExtractVersionComponent(string value, int startIndex)
    {
        var endIndex = startIndex;
        while (endIndex < value.Length && char.IsDigit(value[endIndex]))
        {
            endIndex++;
        }

        return endIndex > startIndex ? value[startIndex..endIndex] : "0";
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

    private enum RequestDestination
    {
        Empty,
        Document,
        Iframe,
        Script,
        Style,
        Image,
        Font,
        Audio,
        Video,
        Track,
        Manifest,
        ServiceWorker,
        Worker,
        SharedWorker,
    }

    private readonly record struct RequestContextSnapshot(
        RequestKind Kind,
        Uri? RequestUri,
        Uri? SourceReferrer,
        Uri? DerivedReferrer,
        string SecFetchSite,
        RequestDestination Destination,
        string SecFetchMode,
        bool IsUserActivated,
        bool IsReload,
        bool IsFormSubmission);
}