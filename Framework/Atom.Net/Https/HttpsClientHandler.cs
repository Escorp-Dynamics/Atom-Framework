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
/// Обработчик запросов поверх собственного TLS/HTTP-стека с браузерными профилями.
/// Поддерживаются HTTP/1.1 (<see cref="Https11Connection"/>), HTTP/2 по ALPN и HTTP/3 поверх QUIC;
/// TLS-версия (1.2/1.3) выбирается до ClientHello по профилю браузера.
/// </summary>
public sealed partial class HttpsClientHandler : HttpMessageHandler
{
    // Multi-label публичные суффиксы — ICANN-секция официального Public Suffix List,
    // выгрузка 2026-08-27 (MultiLabelPublicSuffixes.cs сгенерирован из public_suffix_list.dat).
    // Влияет на sec-fetch-site: hosts под одним публичным суффиксом считаются одним сайтом.
    private static readonly HashSet<string> commonMultiLabelPublicSuffixes = Headers.MultiLabelPublicSuffixes.Exact;
    private static readonly HashSet<string> wildcardSuffixBases = Headers.MultiLabelPublicSuffixes.WildcardBases;
    private static readonly HashSet<string> publicSuffixExceptions = Headers.MultiLabelPublicSuffixes.Exceptions;

    // Билеты возобновления сессии TLS 1.3 по имени узла: сервер выдаёт их после рукопожатия,
    // следующий handshake к тому же узлу предлагает их как PSK. Браузер не делает иного —
    // повторное соединение с полным рукопожатием само по себе заметный не-браузерный признак.
    private readonly ConcurrentDictionary<string, (Tls13SessionTicket Ticket, string? Protocol)> tls13SessionTickets = new(StringComparer.Ordinal);

    /// <summary>
    /// Забирает предложение возобновления для узла, если живой билет ещё хранится.
    /// </summary>
    /// <param name="host">Имя узла (совпадает с SNI).</param>
    /// <param name="requireHttp2">Требовать, чтобы прошлый протокол узла был HTTP/2.</param>
    /// <returns>Предложение PSK либо <see langword="null"/>, когда возобновление нечем предложить.</returns>
    private Tls13PskOffer? TakeTls13TicketOffer(string host, bool requireHttp2)
    {
        if (tls13SessionTickets.TryGetValue(host, out var entry))
        {
            // 0-RTT предлагается только после h2: ранние байты, принятые h1.1-сервером, стали бы
            // мусором в его парсере запросов.
            if (!entry.Ticket.IsExpired && (!requireHttp2 || string.Equals(entry.Protocol, "h2", StringComparison.Ordinal))) return entry.Ticket.ToOffer();
            tls13SessionTickets.TryRemove(host, out _);
        }

        return null;
    }

    /// <summary>Предел числа узлов с хранимыми билетами: защита от неограниченного роста.</summary>
    private const int MaxTls13TicketHosts = 256;

    /// <summary>
    /// Сохраняет билет, выданный соединением с узлом.
    /// </summary>
    /// <param name="host">Имя узла (совпадает с SNI).</param>
    /// <param name="ticket">Билет из NewSessionTicket.</param>
    /// <param name="negotiatedProtocol">Протокол, согласованный на соединении с этим билетом.</param>
    /// <remarks>
    /// Истёкшие билеты не храним; один узел может держать несколько действительных билетов —
    /// заменяем последний полученным. При переполнении вытесняется произвольная запись:
    /// потеря билета деградирует соединение до полного рукопожатия, но не ломает его.
    /// </remarks>
    private void StoreTls13Ticket(string host, Tls13SessionTicket ticket, string? negotiatedProtocol)
    {
        if (ticket.IsExpired) return;

        if (!tls13SessionTickets.ContainsKey(host) && tls13SessionTickets.Count >= MaxTls13TicketHosts)
        {
            tls13SessionTickets.TryRemove(tls13SessionTickets.Keys.First(), out _);
        }

        tls13SessionTickets[host] = (ticket, negotiatedProtocol);
    }

    private int activeRequests;
    private int isDisposed;
    private readonly ConcurrentDictionary<ConnectionPoolKey, ConnectionPoolState> connectionPool = new();

    /// <summary>Счётчик обращений к пулу: по нему решается, когда пора убирать записи.</summary>
    private int poolSweepCountdown;

    /// <summary>
    /// Возвращает или задает значение, которое указывает, должен ли обработчик следовать ответам перенаправления.
    /// </summary>
    public bool AllowAutoRedirect { get; set; } = true;

    /// <summary>
    /// Возвращает или задает тип метода распаковки, используемый обработчиком для автоматической распаковки содержимого HTTP-ответа.
    /// </summary>
    /// <remarks>
    /// По умолчанию включено ВСЁ — как у браузера. Значение относится только к разбору ответа и
    /// на провод не влияет: заголовок <c>accept-encoding</c> формирует профиль браузера, потому
    /// что он входит в отпечаток. Из этого следует и обратное: выключив распаковку, вы получите
    /// сжатое тело, но по-прежнему будете объявлять серверу, что принимаете сжатие.
    /// </remarks>
    public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.All;

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
    /// Объявления альтернативных служб, полученные от узлов (<c>alt-svc</c>).
    /// </summary>
    /// <remarks>
    /// Живёт вместе с обработчиком, как и пул соединений: узел объявляет поддержку HTTP/3 один
    /// раз, а пользуются объявлением все последующие запросы к нему.
    /// </remarks>
    public AlternativeServiceCache AlternativeServices { get; } = new();

    /// <summary>
    /// Возвращает или задает сведения о проверке подлинности, используемые данным обработчиком.
    /// </summary>
    public ICredentials? Credentials { get; set; }

    /// <summary>
    /// Клиентский сертификат для взаимной аутентификации TLS (mTLS); приватный ключ обязателен.
    /// Отправляется только когда сервер запросит клиентский сертификат.
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? ClientCertificate { get; set; }

    /// <summary>
    /// Если используется прокси-сервер по умолчанию (системный), возвращает или задает учетные данные,
    /// отправляемые на прокси-сервер по умолчанию для проверки подлинности.
    /// Прокси-сервер по умолчанию используется только если <see cref="UseProxy"/> задано значение <see langword="true"/> и <see cref="Proxy"/> задано значение <see langword="null"/>.
    /// </summary>
    public ICredentials? DefaultProxyCredentials { get; set; }

    /// <summary>
    /// Возвращает или задает максимальное количество переадресаций, выполняемых обработчиком.
    /// </summary>
    /// <remarks>
    /// Двадцать — столько же, сколько у браузеров. Прежние пятьдесят достались от умолчания
    /// платформы и стоили времени: узел, перенаправляющий сам на себя (а такие в сети есть —
    /// например отдающий <c>location: https://он же:443/</c>), заставлял делать полсотни
    /// запросов, прежде чем признать петлю. Заодно это и вопрос мимикрии: клиент, готовый идти
    /// по полусотне переходов, ведёт себя не как браузер.
    /// </remarks>
    public int MaxAutomaticRedirections { get; set; } = 20;

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
    /// Предел размера тела ответа в байтах; ноль — без предела.
    /// </summary>
    public long MaxResponseContentLength { get; set; }

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
    public bool SupportsAutomaticDecompression => true;

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
    public bool SupportsRedirectConfiguration => true;

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
        var upstreamProxy = ResolveUpstreamProxy(uri);
        var preferredVersion = ResolvePreferredVersion(request, profile, upstreamProxy);

        // Узел мог объявить поддержку HTTP/3 — так браузер её и узнаёт. Поднимаем версию только
        // при точном совпадении происхождения: объявление относится к узлу, а не к схеме.
        // ★ Профилю, который QUIC не описывает, HTTP/3 не предлагается вовсе. Иначе он ушёл бы
        // туда с чужими параметрами транспорта и чужими SETTINGS — то есть сменил бы личность
        // на полпути, стоило серверу объявить alt-svc. Лучше остаться на HTTP/2 целиком, чем
        // быть одним браузером по TCP и другим по QUIC.
        if (isHttps
            && preferredVersion == HttpVersion.Version20
            && request.VersionPolicy is not HttpVersionPolicy.RequestVersionExact
            && upstreamProxy is null
            && (profile is null || profile.Value.Http3 is not null)
            && AlternativeServices.SupportsHttp3(FormatOrigin(uri.IdnHost, port)))
        {
            preferredVersion = HttpVersion.Version30;
        }

        var tcpSettings = BuildTcpSettings(profile);
        var tlsSettings = BuildTlsSettings(profile, request);

        // Автоматический 0-RTT — только безопасные запросы без тела по h2 и БЕЗ прокси:
        // ранние байты за туннелем CONNECT не работают. Профиль обязан описывать HTTP/2,
        // а прошлый визит обязан был договориться именно о нём.
        byte[]? earlyDataPayload = null;
        HPackEncoder? earlyHeaderEncoder = null;

        var http2Profile = profile?.Http2;

        if (isHttps && upstreamProxy is null && preferredVersion == HttpVersion.Version20
            && http2Profile is not null
            && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            && request.Content is null
            && TakeTls13TicketOffer(uri.IdnHost, requireHttp2: true) is { } earlyOffer
            && earlyOffer.MaxEarlyData > 0)
        {
            var headers = Connections.Https2Connection.BuildHeaderList(request, 0, http2Profile.Value);
            (earlyDataPayload, earlyHeaderEncoder) = Connections.Https2Connection.BuildEarlyRequestPayload(headers, http2Profile.Value, http2Profile.Value.ConnectionWindowIncrement);
        }

        return new HttpsConnectionOptions
        {
            Host = uri.IdnHost,
            Port = port,
            IsHttps = isHttps,
            PreferredVersion = preferredVersion,
            VersionPolicy = versionPolicy,
            LocalEndPoint = null,
            ConnectTimeout = preferredVersion == HttpVersion.Version30 ? GetHttp3ConnectTimeout() : ConnectTimeout,
            ResponseHeadersTimeout = ResponseHeadersTimeout,
            RequestSendTimeout = RequestSendTimeout,
            ResponseBodyTimeout = ResponseBodyTimeout,
            SslProtocols = SslProtocols,
            CheckCertificateRevocationList = CheckCertificateRevocationList,
            ServerCertificateValidationCallback = ServerCertificateCustomValidationCallback is null
                ? null
                : (certificate, chain, sslPolicyErrors) => ServerCertificateCustomValidationCallback(request, certificate, chain, sslPolicyErrors),
            MaxResponseHeadersBytes = MaxResponseHeadersLength <= 0 ? int.MaxValue : checked(MaxResponseHeadersLength * 1024),
            MaxResponseContentBytes = MaxResponseContentLength,
            IdleTimeout = PooledConnectionIdleTimeout,
            MaxConcurrentStreams = 1,
            AutoDecompression = AutomaticDecompression is not DecompressionMethods.None,
            ProfileTcpSettings = tcpSettings,
            ProfileTlsSettings = tlsSettings,
            ProfileHttp2Settings = profile?.Http2,
            ProfileHttp3Settings = profile?.Http3,
            ProfileQuicTransport = profile?.QuicTransport,
            UpstreamProxy = upstreamProxy,
            PskOffer = isHttps && preferredVersion != HttpVersion.Version30 ? TakeTls13TicketOffer(uri.IdnHost, requireHttp2: false) : null,
            EarlyDataPayload = earlyDataPayload,
            EarlyHeaderEncoder = earlyHeaderEncoder,
            SessionTicketSink = StoreTls13Ticket,
        };
    }

    /// <summary>
    /// Определяет апстрим-прокси для целевого адреса.
    /// </summary>
    /// <param name="uri">Целевой адрес.</param>
    /// <returns>Адрес прокси либо <see langword="null"/> для прямого подключения.</returns>
    /// <remarks>
    /// Решение принимает сам <see cref="IWebProxy"/>: он же знает и о списках исключений. Прокси,
    /// вернувший адрес самой цели, означает «идти напрямую» — таково соглашение платформы.
    /// </remarks>
    private Uri? ResolveUpstreamProxy(Uri uri)
    {
        if (!UseProxy || Proxy is null) return null;
        if (Proxy.IsBypassed(uri)) return null;

        var proxyUri = Proxy.GetProxy(uri);
        if (proxyUri is null || Uri.Equals(proxyUri, uri)) return null;

        // Учётные данные хранятся отдельно от адреса, а туннелю CONNECT они нужны вместе с ним.
        if (Proxy.Credentials?.GetCredential(proxyUri, "Basic") is not { } credential) return proxyUri;
        if (string.IsNullOrEmpty(credential.UserName)) return proxyUri;

        var builder = new UriBuilder(proxyUri)
        {
            UserName = Uri.EscapeDataString(credential.UserName),
            Password = Uri.EscapeDataString(credential.Password ?? string.Empty),
        };

        return builder.Uri;
    }

    /// <summary>
    /// Определяет версию HTTP, которую следует предложить серверу.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="profile">Профиль браузера, если он задан.</param>
    /// <param name="upstreamProxy">Апстрим-прокси, если он настроен.</param>
    /// <returns>Предпочитаемая версия.</returns>
    /// <remarks>
    /// Профиль — это описание браузера, за который мы себя выдаём, поэтому объявленная им версия
    /// задаёт нижнюю границу: Chrome не станет обращаться по HTTP/1.1 туда, где сервер предлагает
    /// h2, и клиент, который так делает, отличим по одному этому признаку. Запрос вправе поднять
    /// планку выше, а <see cref="HttpVersionPolicy.RequestVersionExact"/> означает прямое
    /// требование вызывающей стороны и перекрывает профиль в обе стороны.
    ///
    /// Без профиля решает запрос — поведение обычного клиента остаётся прежним.
    ///
    /// ★ При настроенном прокси HTTP/3 НЕ ПРЕДЛАГАЕТСЯ никогда. Причина не в удобстве: HTTP/3
    /// работает поверх UDP, а обычный прокси умеет только туннель CONNECT поверх TCP. Соединение
    /// QUIC ушло бы МИМО прокси — напрямую с настоящего адреса, молча и с полностью рабочим
    /// ответом. Для того, кто поставил прокси именно ради подмены адреса, это худший из возможных
    /// исходов: утечка, ничем себя не проявляющая.
    ///
    /// Ровно так же поступает браузер: Chrome при заданном прокси QUIC отключает, потому что
    /// туннелировать его через CONNECT нечем. То есть откат на HTTP/2 здесь не только безопасен,
    /// но и соответствует поведению, под которое мы маскируемся.
    /// </remarks>
    private static Version ResolvePreferredVersion(HttpRequestMessage request, BrowserProfile? profile, Uri? upstreamProxy)
    {
        var requested = request.Version == default ? HttpVersion.Version11 : request.Version;

        if (profile is not { } browserProfile)
            return Downgrade(requested, upstreamProxy);

        if (request.VersionPolicy is HttpVersionPolicy.RequestVersionExact)
            return Downgrade(requested, upstreamProxy);

        var preferred = requested > browserProfile.PreferredHttpVersion ? requested : browserProfile.PreferredHttpVersion;

        return Downgrade(preferred, upstreamProxy);
    }

    /// <summary>
    /// Опускает HTTP/3 до HTTP/2, когда трафик обязан идти через прокси.
    /// </summary>
    /// <param name="version">Желаемая версия.</param>
    /// <param name="upstreamProxy">Апстрим-прокси, если он настроен.</param>
    /// <returns>Версия, которую действительно можно использовать.</returns>
    private static Version Downgrade(Version version, Uri? upstreamProxy)
        => upstreamProxy is not null && version >= HttpVersion.Version30 ? HttpVersion.Version20 : version;

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

    /// <summary>
    /// Выполняет запрос, при необходимости проходя цепочку перенаправлений.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ, завершающий цепочку.</returns>
    /// <remarks>
    /// ★ Свойство <see cref="AllowAutoRedirect"/> объявлено включённым по умолчанию, а цепочки не
    /// было: вызывающая сторона получала голый 302 с пустым телом вместо целевого ответа. Для
    /// клиента, выдающего себя за браузер, это ещё и расхождение в поведении — браузер идёт по
    /// перенаправлению всегда, а вход через страницу проверки без этого попросту не работает.
    ///
    /// Правила взяты у браузера, а не буквально из RFC 9110 §15.4: коды 301, 302 и 303 меняют
    /// метод на GET и отбрасывают тело — спецификация для 301 и 302 этого не требует, но так
    /// делают все браузеры, и сервер рассчитывает именно на это. Коды 307 и 308 сохраняют и метод,
    /// и тело.
    ///
    /// Заголовок авторизации при уходе на ДРУГОЙ узел снимается: пересылать его туда, куда он не
    /// предназначался, — способ отдать учётные данные постороннему.
    /// </remarks>
    internal async Task<HttpsResponseMessage> SendInternalAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ApplyPreAuthenticatedAuthorization(request);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        HttpRequestMessage? redirected = null;
        var hops = 0;
        var authAttempts = 0;
        var proxyAuthAttempts = 0;

        try
        {
#pragma warning disable CA2000 // Владение переходит переменной redirected; освобождает DisposeRedirect в цикле и в finally.
            while (true)
            {
                // ★ Браузер, знающий пароль узла, молча отвечает на Basic-задачу повторным
                // запросом — ровно это и делаем: один виток на 401 и один на 407, чтобы
                // неудачные учётные данные не зациклили цепочку.
                var current = redirected ?? request;

                if ((int)response.StatusCode == 401 && authAttempts == 0
                    && TryBuildBasicAuthorization(current.RequestUri, response.Headers.WwwAuthenticate.ToString(), out var authorization))
                {
                    authAttempts++;
                    current.Headers.TryAddWithoutValidation("Authorization", authorization);
                    response.Dispose();
                    response = await SendWithRetryAsync(current, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode == 407 && proxyAuthAttempts == 0
                    && Proxy?.Credentials is not null
                    && IsBasicChallenge(response.Headers.ProxyAuthenticate.ToString())
                    && ResolveUpstreamProxy(current.RequestUri ?? new Uri(Uri.UriSchemeHttp + Uri.SchemeDelimiter + "localhost")) is { } proxyUri
                    && Proxy.Credentials.GetCredential(proxyUri, "Basic") is { } proxyCredential)
                {
                    proxyAuthAttempts++;
                    current.Headers.TryAddWithoutValidation("Proxy-Authorization", BuildBasicAuthorization(proxyCredential));
                    response.Dispose();
                    response = await SendWithRetryAsync(current, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Учётные данные подтверждены удачным ответом — запоминаем для PreAuthenticate.
                if (authAttempts > 0 && (int)response.StatusCode < 400
                    && current.RequestUri is { } successUri
                    && current.Headers.TryGetValues("Authorization", out var confirmedValues))
                {
                    preAuthenticatedAuthorization[AuthorizationCacheKey(successUri)] = string.Join(' ', confirmedValues);
                }

                if (!AllowAutoRedirect) break;

                if (!TryCreateRedirect(current, response, out var next)) break;

                if (++hops > MaxAutomaticRedirections)
                {
                    DisposeRedirect(next);
                    throw new HttpRequestException($"Превышен предел перенаправлений ({MaxAutomaticRedirections})");
                }

                response.Dispose();
                DisposeRedirect(redirected);
                redirected = next;
                response = await SendWithRetryAsync(redirected, cancellationToken).ConfigureAwait(false);
            }
#pragma warning restore CA2000

            return response;
        }
        finally
        {
            DisposeRedirect(redirected);
        }
    }

    // Подтверждённые учётные данные узла для PreAuthenticate: после первого успешного ответа
    // Authorization уходит сразу, без лишнего витка 401 — ровно как у браузера с паролем в кэше.
    private readonly ConcurrentDictionary<string, string> preAuthenticatedAuthorization = new(StringComparer.Ordinal);

    private static string AuthorizationCacheKey(Uri uri)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}");

    /// <summary>
    /// Ставит заголовок Authorization из кэша PreAuthenticate, если он подтверждён для узла.
    /// </summary>
    private void ApplyPreAuthenticatedAuthorization(HttpRequestMessage request)
    {
        if (!PreAuthenticate || request.RequestUri is not { } uri) return;
        if (request.Headers.Authorization is not null) return;
        if (!preAuthenticatedAuthorization.TryGetValue(AuthorizationCacheKey(uri), out var value)) return;

        request.Headers.TryAddWithoutValidation("Authorization", value);
    }

    /// <summary>
    /// Пробует собрать заголовок Authorization для Basic-задачи из учётных данных обработчика.
    /// </summary>
    private bool TryBuildBasicAuthorization(Uri? uri, string challenge, out string authorization)
    {
        authorization = string.Empty;

        if (uri is null || !IsBasicChallenge(challenge)) return false;

        var credential = Credentials?.GetCredential(uri, "Basic");
        if (credential is null) return false;

        authorization = BuildBasicAuthorization(credential);
        return true;
    }

    private static bool IsBasicChallenge(string challenge)
        => challenge.Contains("basic", StringComparison.OrdinalIgnoreCase);

    private static string BuildBasicAuthorization(NetworkCredential credential)
    {
        var raw = string.Concat(credential.UserName, ":", credential.Password);
        return "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Сколько раз повторять запрос, который заведомо не был обработан.
    /// </summary>
    /// <remarks>
    /// Двух попыток хватает: повтор нужен против ОДНОРАЗОВЫХ причин — соединение из пула успело
    /// умереть, сервер упёрся в предел потоков, началось закрытие соединения. Если и вторая
    /// попытка на свежем соединении не удалась, дело не в стечении обстоятельств.
    /// </remarks>
    private const int MaxSendAttempts = 3;

    /// <summary>
    /// Отправляет запрос, повторяя его, если он заведомо не был обработан сервером.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ.</returns>
    /// <remarks>
    /// ★ Повторов не было вовсе — и это самый заметный пробел в надёжности. Долгоживущий клиент
    /// неизбежно натыкается на соединение, которое партнёр закрыл секунду назад: проверка перед
    /// выдачей из пула сужает окно, но закрыть его полностью нельзя — между проверкой и отправкой
    /// всегда остаётся зазор. Один такой случай означал потерю запроса, хотя сервер о нём даже не
    /// узнал.
    ///
    /// Повторяется ТОЛЬКО то, что заведомо не дошло до обработки, — иначе повтор превращается в
    /// повторное действие на стороне сервера. Признаки перечислены в <see cref="IsRetryable"/>.
    /// </remarks>
    private async Task<HttpsResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
        {
            HttpsResponseMessage response;

            try
            {
                response = await SendWithHttp3FallbackAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (attempt < MaxSendAttempts
                && !cancellationToken.IsCancellationRequested
                && IsRetryable(error, request))
            {
                await PauseBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // ★ Отказ транспорта приходит сюда ЗНАЧЕНИЕМ, а не исключением: собственный контракт
            // модуля возвращает сбой в поле ответа, и превращается он в исключение только на
            // границе с HttpClient. Ловить одни лишь исключения здесь бесполезно — как раз то,
            // из-за чего повтор сначала и не срабатывал ни разу.
            if (response.Exception is not { } failure
                || attempt >= MaxSendAttempts
                || cancellationToken.IsCancellationRequested
                || !IsRetryable(failure, request))
            {
                return response;
            }

            response.Dispose();

            await PauseBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
        }

        // Сюда попасть нельзя: последняя попытка либо возвращает ответ, либо бросает — условие
        // отбора повторов её уже не пропускает.
        throw new InvalidOperationException("Исчерпаны попытки отправки запроса");
    }

    /// <summary>
    /// Выжидает перед следующей попыткой.
    /// </summary>
    /// <param name="attempt">Номер только что провалившейся попытки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача ожидания.</returns>
    /// <remarks>
    /// Первый повтор идёт сразу: самая частая причина — соединение, закрытое партнёром, и ждать
    /// там нечего. Дальше пауза растёт: если сервер разгружается, мгновенный повтор попадёт
    /// ровно в ту же обстановку.
    /// </remarks>
    private static ValueTask PauseBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        => attempt <= 1
            ? ValueTask.CompletedTask
            : new ValueTask(Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken));

    /// <summary>
    /// Решает, можно ли повторить запрос после этого отказа.
    /// </summary>
    /// <param name="error">Отказ попытки.</param>
    /// <param name="request">Запрос.</param>
    /// <returns><see langword="true"/>, если повтор безопасен.</returns>
    /// <remarks>
    /// Два условия, и оба обязательны.
    ///
    /// Первое: отказ обязан означать, что запрос НЕ БЫЛ обработан. Отказ сервера в потоке
    /// (REFUSED_STREAM), обрыв соединения и закрытие его партнёром — это именно такие случаи:
    /// ответа не было, а значит и действия на той стороне не произошло. Всё остальное —
    /// включая любой полученный ответ, даже с кодом ошибки, — не повторяется.
    ///
    /// Второе: тело запроса обязано быть отправляемым ПОВТОРНО. Поток, который вызывающая
    /// сторона отдала однажды, второй раз прочитать нельзя, и молча отправить пустое тело было бы
    /// хуже отказа. Запросы без тела этим не ограничены.
    /// </remarks>
    private static bool IsRetryable(Exception error, HttpRequestMessage request)
    {
        if (request.Content is not null && !IsReplayableContent(request.Content)) return false;

        var inner = error;
        while (inner.InnerException is not null) inner = inner.InnerException;

        return inner switch
        {
            Http2.Http2StreamRefusedException => true,
            Connections.HttpsIdleConnectionClosedException => true,

            // Не дозвонились — запрос не отправлялся вовсе. Отдельный тип, а не общий
            // TimeoutException: таймаут на ОТВЕТЕ означает обратное, там сервер мог всё сделать.
            Connections.HttpsConnectTimeoutException => true,
            Tls.TlsConnectionClosedException => true,
            System.Net.Sockets.SocketException socket => socket.SocketErrorCode
                is System.Net.Sockets.SocketError.ConnectionReset
                or System.Net.Sockets.SocketError.ConnectionAborted
                or System.Net.Sockets.SocketError.Shutdown,
            IOException io => io.Message.Contains("закрыто удалённой стороной", StringComparison.Ordinal)
                || io.Message.Contains("Сеанс HTTP/2 завершён", StringComparison.Ordinal),

            // ★ Внутренний срок истёк (внешнюю отмену сюда не пускает условие повтора выше).
            // Повторяем ТОЛЬКО безопасные методы: срок мог истечь и на чтении ответа, а значит
            // сервер запрос уже выполнил. Для GET и HEAD повторное выполнение ничего не меняет
            // по определению, для POST — меняет, и рисковать этим нельзя.
            OperationCanceledException or TimeoutException => IsIdempotent(request.Method),

            _ => false,
        };
    }

    /// <summary>
    /// Безопасно ли выполнить запрос этим методом дважды.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <returns><see langword="true"/> для методов без побочного действия.</returns>
    /// <remarks>
    /// Список намеренно узкий. PUT и DELETE спецификация тоже называет идемпотентными, но на
    /// деле их обработчики часто ведут счётчики и журналы, а цена ошибки здесь несимметрична:
    /// лишний повтор GET не стоит ничего, лишний повтор PUT может стоить данных.
    /// </remarks>
    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options;

    /// <summary>
    /// Можно ли отправить это тело ещё раз.
    /// </summary>
    /// <param name="content">Тело запроса.</param>
    /// <returns><see langword="true"/>, если тело лежит в памяти и перечитывается.</returns>
    private static bool IsReplayableContent(HttpContent content)
        => content is ByteArrayContent or StringContent or FormUrlEncodedContent;

    /// <summary>
    /// Решает, уместен ли откат с HTTP/3 после этого отказа.
    /// </summary>
    /// <param name="error">Отказ попытки по HTTP/3.</param>
    /// <param name="cancellationToken">Токен вызывающей стороны.</param>
    /// <returns><see langword="true"/>, если следует повторить по HTTP/2.</returns>
    /// <remarks>
    /// Отмену, пришедшую СНАРУЖИ, повторять нельзя — вызывающая сторона отказалась от запроса.
    /// А вот собственное время ожидания, истёкшее внутри, — обычная причина отката: именно так
    /// выглядит закрытый UDP.
    /// </remarks>
    private static bool ShouldFallBackFromHttp3(Exception error, CancellationToken cancellationToken)
        => error is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Через сколько бездействия запись пула считается ненужной.
    /// </summary>
    private static readonly TimeSpan AbandonedPoolLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Как часто проверять пул на брошенные записи — раз в столько обращений.</summary>
    private const int PoolSweepInterval = 256;

    /// <summary>
    /// Убирает записи пула для узлов, к которым давно не обращались.
    /// </summary>
    /// <remarks>
    /// ★ Ключ пула — это УЗЕЛ, а при обходе тысяч сайтов узлов столько же. Раньше запись
    /// заводилась навсегда: даже после того как все соединения к узлу закрылись, в словаре
    /// оставались сама запись и два семафора, и это росло вместе с числом посещённых сайтов, ни
    /// разу не уменьшаясь. Ни отказа, ни записи в журнале — просто медленно текущая память.
    ///
    /// Убираются только ПУСТЫЕ записи: с живым соединением запись трогать нельзя. Проверка идёт
    /// не по таймеру, а раз в несколько сотен обращений — отдельный поток ради этого заводить
    /// незачем, а на горячем пути проверка почти всегда сводится к уменьшению счётчика.
    /// </remarks>
    private void SweepAbandonedPools()
    {
        if (Interlocked.Decrement(ref poolSweepCountdown) > 0) return;

        Volatile.Write(ref poolSweepCountdown, PoolSweepInterval);

        foreach (var pair in connectionPool)
        {
            if (!pair.Value.IsAbandoned(AbandonedPoolLifetime)) continue;
            if (!connectionPool.TryRemove(pair)) continue;

            // Между проверкой и удалением записью могли начать пользоваться. Тогда возвращаем её
            // на место: потерять запись с соединением куда хуже, чем оставить лишнюю пустую.
            if (!pair.Value.IsAbandoned(TimeSpan.Zero)) connectionPool.TryAdd(pair.Key, pair.Value);
            else pair.Value.Dispose();
        }
    }

    /// <summary>
    /// Сколько ждать соединения по HTTP/3, прежде чем откатиться на HTTP/2.
    /// </summary>
    /// <returns>Время ожидания попытки HTTP/3.</returns>
    /// <remarks>
    /// ★ Заметно меньше общего: попытка по HTTP/3 всегда ОПОРТУНИСТИЧНА — есть заведомо рабочий
    /// путь по HTTP/2, и цена неудачи здесь не отказ, а задержка. Ждать полное время ожидания
    /// соединения незачем: узел, до которого UDP не доходит, не ответит и за минуту, а
    /// работающий отвечает за сотни миллисекунд.
    ///
    /// Замер: на узле с объявленным, но недоступным HTTP/3 полное ожидание давало 10 секунд
    /// задержки на первый запрос; браузеры в такой обстановке дают QUIC доли секунды форы и
    /// уходят на TCP.
    /// </remarks>
    private TimeSpan GetHttp3ConnectTimeout()
    {
        var limit = TimeSpan.FromSeconds(3);

        return ConnectTimeout > TimeSpan.Zero && ConnectTimeout < limit ? ConnectTimeout : limit;
    }

    /// <summary>
    /// Освобождает промежуточный запрос перенаправления, не трогая его тело.
    /// </summary>
    /// <param name="redirect">Запрос очередного перехода либо <see langword="null"/>.</param>
    /// <remarks>
    /// ★ Тело у перехода — ЧУЖОЕ: для 307 и 308 метод и тело сохраняются, поэтому
    /// <c>TryCreateRedirect</c> передаёт сюда ту же самую ссылку на <c>HttpContent</c>, что и в
    /// исходном запросе. Освобождая запрос целиком, мы освобождали и её — а дальше либо
    /// следующий переход той же цепочки пытался это тело отправить и получал обращение к
    /// освобождённому объекту, либо тело просто исчезало у вызывающей стороны, которая своим
    /// запросом ещё владеет и вправе им пользоваться.
    /// </remarks>
    private static void DisposeRedirect(HttpRequestMessage? redirect)
    {
        if (redirect is null) return;

        redirect.Content = null;
        redirect.Dispose();
    }

    /// <summary>
    /// Выполняет запрос, откатываясь с HTTP/3 на HTTP/2, если по HTTP/3 не вышло.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ.</returns>
    /// <remarks>
    /// ★ Откат обязателен ровно потому, что HTTP/3 у нас берётся из объявления сервера, а не из
    /// уверенности: объявление могло устареть, а UDP до узла может не доходить вовсе — в
    /// корпоративных сетях он закрыт сплошь и рядом. Без отката один такой узел или одна такая
    /// сеть делают все запросы к нему безнадёжными.
    ///
    /// Браузер поступает так же: не сумев по HTTP/3, он возвращается на HTTP/2 и перестаёт
    /// доверять объявлению — поэтому запись здесь и забывается, иначе неудача повторялась бы на
    /// каждом запросе.
    ///
    /// Откат делается ТОЛЬКО когда версию выбрали мы: прямое требование вызывающей стороны
    /// подменять нельзя.
    /// </remarks>
    private async Task<HttpsResponseMessage> SendWithHttp3FallbackAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var origin = request.RequestUri is { } uri ? FormatOrigin(uri.IdnHost, uri.Port) : null;
        var mayFallBack = origin is not null
            && request.VersionPolicy is not HttpVersionPolicy.RequestVersionExact
            && request.Version < HttpVersion.Version30
            && AlternativeServices.SupportsHttp3(origin);

        try
        {
            var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);

            // Удачное соединение снимает отсрочку: узел мог быть недоступен временно.
            if (mayFallBack && response.Version == HttpVersion.Version30) AlternativeServices.MarkHttp3Working(origin!);

            return response;
        }
        catch (Exception error) when (mayFallBack && ShouldFallBackFromHttp3(error, cancellationToken))
        {
            // ★ Не «забыть», а ОТЛОЖИТЬ: забытое объявление тут же возвращается из заголовка
            // ответа по HTTP/2, и попытка повторяется на каждом запросе — см. MarkHttp3Broken.
            AlternativeServices.MarkHttp3Broken(origin!);
        }

        return await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Строит запрос по перенаправлению, если ответ его требует.
    /// </summary>
    /// <param name="current">Текущий запрос.</param>
    /// <param name="response">Полученный ответ.</param>
    /// <param name="redirect">Готовый запрос по новому адресу.</param>
    /// <returns><see langword="true"/>, если перенаправление нужно выполнить.</returns>
    private static bool TryCreateRedirect(HttpRequestMessage current, HttpsResponseMessage response, [NotNullWhen(true)] out HttpRequestMessage? redirect)
    {
        redirect = null;

        var status = (int)response.StatusCode;
        if (status is not (301 or 302 or 303 or 307 or 308)) return false;

        var location = response.Headers.Location;
        if (location is null) return false;

        var origin = current.RequestUri;
        if (origin is null) return false;

        var target = location.IsAbsoluteUri ? location : new Uri(origin, location);

        // Уход со схемы http(s) браузер не выполняет: перенаправление на произвольную схему —
        // это уже не запрос, а передача управления чему-то другому.
        if (!string.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var keepsMethod = status is 307 or 308;
        var method = keepsMethod || current.Method == HttpMethod.Head ? current.Method : HttpMethod.Get;

        var next = new HttpRequestMessage(method, target)
        {
            Version = current.Version,
            VersionPolicy = current.VersionPolicy,
            Content = keepsMethod ? current.Content : null,
        };

        var sameOrigin = Uri.Compare(
            origin,
            target,
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) is 0;

        foreach (var header in current.Headers)
        {
            // Учётные данные не следуют за перенаправлением на чужой узел.
            if (!sameOrigin && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;

            // Заголовки, которые обязаны быть пересчитаны под новый адрес, не переносим:
            // ими займётся ApplyBrowserProfileDefaults на следующем витке.
            if (IsRecomputedOnRedirect(header.Key)) continue;

            next.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in current.Options)
            next.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        redirect = next;
        return true;
    }

    /// <summary>
    /// Сообщает, что заголовок пересчитывается заново на каждом витке перенаправления.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <returns><see langword="true"/>, если переносить его не следует.</returns>
    /// <remarks>
    /// Эти заголовки описывают ОТНОШЕНИЕ запроса к адресу: узел, происхождение, назначение
    /// выборки, набор cookie. Перенесённые как есть, они описывали бы прошлый адрес — и это
    /// заметно снаружи ровно так же, как их отсутствие. Referer среди них НЕТ: он описывает
    /// инициатора цепочки, а не очередной адрес, — браузер несёт его через все перенаправления,
    /// а политика реферера каждый раз применяется уже к цели очередного хопа.
    /// </remarks>
    private static bool IsRecomputedOnRedirect(string name)
        => string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Cookie", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Origin", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<HttpsResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref activeRequests);

        HttpsRequestMessage? preparedRequest = null;
        var ownsPreparedRequest = false;
        var lease = default(ConnectionLease);
        var leaseTaken = false;
        var retained = false;

        try
        {
#pragma warning disable CA2000
            preparedRequest = PrepareRequest(request, out ownsPreparedRequest);
            _ = preparedRequest.RequestUri ?? throw new InvalidOperationException("RequestUri не задан");

            ApplyBrowserProfileDefaults(preparedRequest);
            ApplyRequestCookies(preparedRequest);

            var options = BuildConnectionOptions(preparedRequest);
            lease = await AcquireConnectionAsync(options, cancellationToken).ConfigureAwait(false);
            leaseTaken = true;
#pragma warning restore CA2000

            var (response, connectionRetained) = await SendOverConnectionAsync(preparedRequest, lease, options, cancellationToken).ConfigureAwait(false);
            retained = connectionRetained;

            return response;
        }
        finally
        {
            if (leaseTaken) await ReleaseLeaseAsync(lease, retained).ConfigureAwait(false);

            DisposePreparedRequest(preparedRequest, ownsPreparedRequest);

            Interlocked.Decrement(ref activeRequests);
        }
    }

    /// <summary>
    /// Возвращает пулу всё, что было взято под запрос.
    /// </summary>
    /// <param name="lease">Выданная аренда.</param>
    /// <param name="retained">Осталось ли соединение жить дальше.</param>
    /// <returns>Задача освобождения.</returns>
    /// <remarks>
    /// Общее соединение HTTP/2 не закрывается никогда: по нему в этот момент могут идти чужие
    /// запросы. Если оно испортилось, его снимают с должности общего и откладывают до момента,
    /// когда закончатся начатые потоки, — закрыть сразу значило бы оборвать соседей.
    /// </remarks>
    private static async ValueTask ReleaseLeaseAsync(ConnectionLease lease, bool retained)
    {
        var connection = lease.Connection;

        if (lease.IsShared)
        {
            if (lease.PoolState is { } poolState && (!connection.IsConnected || connection.IsDraining) && poolState.TryClearMultiplexed(connection))
                poolState.Retire(connection);
        }
        else if (!retained && connection is not null)
        {
            await DisposeConnectionAsync(connection).ConfigureAwait(false);
        }

        if (lease.LeaseHeld) lease.PoolState?.ReleaseLease();
    }

    /// <summary>
    /// Отправляет запрос по контракту <see cref="HttpMessageHandler"/>.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ сервера.</returns>
    /// <remarks>
    /// Сбой транспорта здесь ОБЯЗАН стать исключением. Собственный API возвращает такой сбой
    /// значением — в этом и смысл <see cref="HttpsClient"/>, — но под <see cref="HttpClient"/>
    /// действует контракт платформы: вызывающий код ждёт <see cref="HttpRequestException"/>, а
    /// не ответ «500» с исключением внутри. Отдать вместо исключения синтетический ответ значит
    /// превратить обрыв связи в успешно полученный отказ сервера: повторы не сработают, а
    /// диагностика укажет на чужую сторону.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override async Task<HttpResponseMessage> SendAsync([NotNull] HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendInternalAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Exception is not { } failure) return response;

        response.Dispose();

        // Отмену пробрасываем как отмену: превращать её в сбой запроса значит скрыть от
        // вызывающей стороны, что остановку запросила она сама.
        if (failure is OperationCanceledException canceled) throw canceled;

        throw new HttpRequestException(failure.Message, failure);
    }

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

        // Копируем НЕРАЗОБРАННЫЕ значения: обычный обход отдаёт их поэлементно, и уже здесь,
        // на клонировании, заголовок вызывающей стороны распался бы на куски — а дальше уехал бы
        // на провод несколькими строками вместо одной.
        foreach (var header in request.Headers.NonValidated)
            prepared.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());

        foreach (var option in request.Options)
            prepared.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        return prepared;
    }

    /// <summary>
    /// Выдаёт соединение под запрос, открывая новое или переиспользуя имеющееся.
    /// </summary>
    /// <param name="options">Параметры соединения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Аренда соединения.</returns>
    /// <remarks>
    /// Три пути, и выбор между ними определяется тем, что известно о протоколе узла.
    ///
    /// Общее соединение HTTP/2 выдаётся без всякой синхронизации — это самый частый путь под
    /// нагрузкой, и он обязан быть свободен от блокировок.
    ///
    /// Известный HTTP/1.1 — прежнее поведение: слот пула, очередь, эксклюзивное владение.
    ///
    /// Незнакомый узел проходит через ворота: пока протокол не выяснен, параллельные подключения
    /// рискуют оказаться лишними, а каждое из них — это полное рукопожатие TLS.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Владение соединением передаётся в аренду; освобождает его ReleaseLeaseAsync.")]
    private async ValueTask<ConnectionLease> AcquireConnectionAsync(HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        var allowHttp2 = options.IsHttps && options.PreferredVersion.Major >= 2;
        var isMultiplexingExpected = allowHttp2 || options.PreferredVersion.Major >= 3;

        if (MaxConnectionsPerServer <= 0)
        {
            var direct = await ConnectNegotiatedAsync(options, allowHttp2, cancellationToken).ConfigureAwait(false);
            return new ConnectionLease(direct, PoolState: null, LeaseHeld: false, IsShared: false);
        }

        var poolKey = new ConnectionPoolKey(options.Host, options.Port, options.IsHttps, options.UpstreamProxy?.ToString(), options.PreferredVersion.Major >= 3);
        var poolState = connectionPool.GetOrAdd(poolKey, static (_, arg) => new ConnectionPoolState(arg.MaxConnections), new ConnectionPoolStateFactoryArg(MaxConnectionsPerServer));

        poolState.Touch();
        poolState.SweepRetired();
        SweepAbandonedPools();

        if (TryTakeShared(poolState, poolKey) is { } shared)
            return new ConnectionLease(shared, poolState, LeaseHeld: false, IsShared: true);

        if (!isMultiplexingExpected || poolState.Hint is PoolProtocolHint.Exclusive)
            return await AcquireExclusiveAsync(options, poolState, poolKey, allowHttp2, cancellationToken).ConfigureAwait(false);

        return await AcquireNegotiatedAsync(options, poolState, poolKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Забирает общее соединение, попутно снимая непригодное.
    /// </summary>
    /// <summary>
    /// Пытается взять общее соединение узла.
    /// </summary>
    /// <param name="poolState">Состояние пула узла.</param>
    /// <param name="poolKey">Ключ пула.</param>
    /// <returns>Соединение либо <see langword="null"/>, если брать нечего.</returns>
    /// <remarks>
    /// ★ «Занято» и «непригодно» — РАЗНЫЕ вещи, и раньше они лечились одинаково. Соединение,
    /// упёршееся в предел одновременных потоков сервера, снималось с должности общего и
    /// уничтожалось — совершенно исправное, с живым рукопожатием и наполненной таблицей сжатия.
    ///
    /// Под нагрузкой это выворачивает мультиплексирование наизнанку: при пятистах запросах к
    /// узлу с пределом в сто потоков каждое переполнение выбрасывает соединение и заставляет
    /// делать полное рукопожатие TCP и TLS заново. То есть чем выше нагрузка, тем ближе
    /// поведение к HTTP/1.1 — ровно наоборот тому, ради чего HTTP/2 и нужен.
    ///
    /// Теперь занятость возвращает «сейчас нечего взять» и соединение остаётся на месте, а
    /// снимается оно только по настоящей непригодности.
    /// </remarks>
    private HttpsConnection? TryTakeShared(ConnectionPoolState poolState, ConnectionPoolKey poolKey)
    {
        if (poolState.Multiplexed is not { } shared) return null;
        if (CanUseMultiplexed(shared, poolKey)) return shared;

        // Единственная причина — заняты все потоки: соединение исправно, просто сейчас полное.
        if (IsMerelyBusy(shared, poolKey)) return null;

        if (poolState.TryClearMultiplexed(shared)) poolState.Retire(shared);

        return null;
    }

    /// <summary>
    /// Проверяет, отказано ли соединению только из-за занятости.
    /// </summary>
    /// <param name="connection">Общее соединение.</param>
    /// <param name="key">Ключ пула.</param>
    /// <returns><see langword="true"/>, если соединение исправно и лишь заполнено.</returns>
    private bool IsMerelyBusy(HttpsConnection connection, ConnectionPoolKey key)
        => connection.IsConnected
        && !connection.IsDraining
        && connection.MatchesTarget(key.Host, key.Port, key.IsHttps)
        && !IsConnectionExpired(connection)
        && !IsConnectionLifetimeExpired(connection)
        && !connection.HasCapacity;

    /// <summary>
    /// Выдаёт эксклюзивное соединение под слот пула.
    /// </summary>
    private async ValueTask<ConnectionLease> AcquireExclusiveAsync(
        HttpsConnectionOptions options,
        ConnectionPoolState poolState,
        ConnectionPoolKey poolKey,
        bool allowHttp2,
        CancellationToken cancellationToken)
    {
        await poolState.WaitForLeaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
#pragma warning disable CA2000 // Владение соединением переходит в аренду; освобождает его ReleaseLeaseAsync.
            if (await TryRentConnectionAsync(poolKey, poolState, cancellationToken).ConfigureAwait(false) is { } pooled)
                return new ConnectionLease(pooled, poolState, LeaseHeld: true, IsShared: false);
#pragma warning restore CA2000

            var opened = await ConnectNegotiatedAsync(options, allowHttp2, cancellationToken).ConfigureAwait(false);

            // Сервер мог согласовать h2 даже там, где прошлый раз выбрал http/1.1. Такое
            // соединение эксклюзивным быть не должно: слот освобождаем сразу, иначе одно
            // мультиплексируемое соединение навсегда займёт место, рассчитанное на один запрос.
            if (!opened.IsMultiplexing) return new ConnectionLease(opened, poolState, LeaseHeld: true, IsShared: false);

            poolState.SetHint(PoolProtocolHint.Multiplexed);
            poolState.ReleaseLease();

            return poolState.TryPublishMultiplexed(opened)
                ? new ConnectionLease(opened, poolState, LeaseHeld: false, IsShared: true)
                : new ConnectionLease(opened, poolState, LeaseHeld: false, IsShared: false);
        }
        catch
        {
            poolState.ReleaseLease();
            throw;
        }
    }

    /// <summary>
    /// Выясняет протокол узла под воротами и выдаёт соединение согласно результату.
    /// </summary>
    private async ValueTask<ConnectionLease> AcquireNegotiatedAsync(
        HttpsConnectionOptions options,
        ConnectionPoolState poolState,
        ConnectionPoolKey poolKey,
        CancellationToken cancellationToken)
    {
        HttpsConnection opened;

        await poolState.EnterConnectGateAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Пока мы ждали ворота, соединение мог установить кто-то другой — ради этого они и нужны.
            if (TryTakeShared(poolState, poolKey) is { } shared)
                return new ConnectionLease(shared, poolState, LeaseHeld: false, IsShared: true);

            opened = await ConnectNegotiatedAsync(options, allowHttp2: true, cancellationToken).ConfigureAwait(false);

            if (opened.IsMultiplexing)
            {
                poolState.SetHint(PoolProtocolHint.Multiplexed);

                return poolState.TryPublishMultiplexed(opened)
                    ? new ConnectionLease(opened, poolState, LeaseHeld: false, IsShared: true)
                    : new ConnectionLease(opened, poolState, LeaseHeld: false, IsShared: false);
            }

            poolState.SetHint(PoolProtocolHint.Exclusive);
        }
        finally
        {
            poolState.ExitConnectGate();
        }

        // Слот берём уже ВНЕ ворот: ожидание свободного слота может быть долгим, и держать на нём
        // ворота значило бы задерживать всех, кому досталось бы готовое общее соединение.
        try
        {
            await poolState.WaitForLeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeConnectionAsync(opened).ConfigureAwait(false);
            throw;
        }

        return new ConnectionLease(opened, poolState, LeaseHeld: true, IsShared: false);
    }

    /// <summary>
    /// Устанавливает соединение и выбирает его тип по согласованному в ALPN протоколу.
    /// </summary>
    /// <param name="options">Параметры соединения.</param>
    /// <param name="allowHttp2">Предлагать ли HTTP/2.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Открытое соединение.</returns>
    /// <remarks>
    /// Именно так решает браузер: он предлагает <c>h2</c> и <c>http/1.1</c>, а выбирает сервер.
    /// Транспорт при этом устанавливается ровно один раз — подключаться повторно ради «нужного»
    /// класса соединения означало бы лишнее рукопожатие TLS на каждый первый запрос к узлу.
    /// </remarks>
    private static async ValueTask<HttpsConnection> ConnectNegotiatedAsync(HttpsConnectionOptions options, bool allowHttp2, CancellationToken cancellationToken)
    {
        // HTTP/3 согласованию по ALPN не подлежит: он живёт на другом транспорте, поверх UDP, и
        // выбирается ДО подключения. Обычный путь для браузера — узнать о поддержке из заголовка
        // Alt-Svc и переключиться на следующем запросе; здесь версия задаётся явно.
        if (options.PreferredVersion.Major >= 3)
        {
            var http3 = new Https3Connection();

            try
            {
                await http3.OpenAsync(options, cancellationToken).ConfigureAwait(false);
                return http3;
            }
            catch
            {
                await http3.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var alpn = allowHttp2 && options.IsHttps
            ? HttpsTransportConnector.Http2AndHttp11
            : HttpsTransportConnector.Http11Only;

        var established = await HttpsTransportConnector.ConnectAsync(options, alpn, cancellationToken).ConfigureAwait(false);

        if (!established.IsHttp2)
        {
            var http11 = new Https11Connection();
            http11.Adopt(established, options);
            return http11;
        }

        var http2 = new Https2Connection();

        try
        {
            await http2.AdoptAsync(established, options, cancellationToken).ConfigureAwait(false);
            return http2;
        }
        catch
        {
            await http2.DisposeAsync().ConfigureAwait(false);

            if (!ReferenceEquals(established.Transport, established.Socket))
                await established.Transport.DisposeAsync().ConfigureAwait(false);

            await established.Socket.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<(HttpsResponseMessage Response, bool Retained)> SendOverConnectionAsync(
        HttpsRequestMessage preparedRequest,
        ConnectionLease lease,
        HttpsConnectionOptions options,
        CancellationToken cancellationToken)
    {
        var connection = lease.Connection;

        if (!connection.IsConnected)
            await connection.OpenAsync(options, cancellationToken).ConfigureAwait(false);

        var uri = preparedRequest.RequestUri ?? throw new InvalidOperationException("RequestUri не задан");
        var response = await connection.SendAsync(preparedRequest, cancellationToken).ConfigureAwait(false);
        ApplyResponseCookies(uri, response);

        if (response.Headers.NonValidated.TryGetValues("alt-svc", out var altSvc))
            AlternativeServices.Remember(FormatOrigin(uri.IdnHost, uri.Port), altSvc.ToString());

        // Общее соединение остаётся жить в пуле независимо от исхода одного запроса: его судьбу
        // решает состояние соединения, а не результат конкретного обмена.
        if (lease.IsShared) return (response, true);

        if (lease.PoolState is { } poolState && CanReuseConnection(connection, response))
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

        ApplyRequestKindDefaults(request, profile.Value, requestContext);

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
        // CORS-preflight живой Chromium шлёт БЕЗ подсказок клиента вообще (capture: OPTIONS
        // с Access-Control-Request-Method) — добавлять их значит выдать себя мгновенно.
        if (request.Method == System.Net.Http.HttpMethod.Options && request.Headers.Contains("Access-Control-Request-Method"))
        {
            return;
        }

        if (!TryCreateSecChUaValue(profile.UserAgent, out var secChUa))
        {
            return;
        }

        AddHeaderIfMissing(request, "sec-ch-ua", secChUa);
        // Подсказка о мобильности берётся из профиля, а не подставляется постоянной: телефон,
        // объявляющий «?0», противоречит собственной строке агента, и это противоречие видно в
        // каждом запросе.
        AddHeaderIfMissing(request, "sec-ch-ua-mobile", profile.IsMobile ? "?1" : "?0");
        AddHeaderIfMissing(request, "sec-ch-ua-platform", profile.ClientHintsPlatform is { Length: > 0 } platform
            ? "\"" + platform + "\""
            : GetSecChUaPlatformValue(profile.UserAgent));
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
            isFormSubmission,
            request.Version.Major);
    }

    private static void ApplyRequestKindDefaults(HttpsRequestMessage request, in BrowserProfile profile, in RequestContextSnapshot requestContext)
    {
        request.Headers.Referrer = requestContext.DerivedReferrer;
        AddHeaderIfMissing(request, "sec-fetch-site", requestContext.SecFetchSite);
        AddHeaderIfMissing(request, "sec-fetch-mode", requestContext.SecFetchMode);
        AddHeaderIfMissing(request, "sec-fetch-dest", GetRequestDestinationValue(requestContext.Destination));

        switch (requestContext.Kind)
        {
            case RequestKind.Navigation:
                // ★ Ни того, ни другого Safari не отправляет: sec-fetch-user в нём не реализован
                // вовсе, а признака навигации у него нет. В записанном обмене их НЕТ, и лишний
                // заголовок здесь так же заметен, как недостающий у остальных.
                if (IsSafariProfile(profile)) break;

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
            // Состав снят с живого Firefox 154 (capture subresource img): png упомянут явно,
            // хвосты получили веса 0.8/0.5 — у Chromium состав другой, со старым apng-хвостом.
            RequestDestination.Image when IsFirefoxProfile(profile) => "image/avif,image/webp,image/png,image/svg+xml,image/*;q=0.8,*/*;q=0.5",
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
    {
        var locale = profile.Headers.AcceptLanguageLocale;
        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = "en-US";
        }

        // Формы сняты с capture-эталонов: Firefox объявляет запасной язык с весом 0.5,
        // Chromium-семейство и Safari — полную форму с весом 0.9. Локаль без региона
        // (например «ru») базового подтега не получает — добавлять нечего.
        var separator = locale.IndexOf('-');
        var fallback = separator > 0 ? locale[..(separator + 1)].TrimEnd('-') : null;

        var weight = IsFirefoxProfile(profile) ? "0.5" : "0.9";
        return fallback is null
            ? string.Concat(locale, ";q=", weight)
            : string.Concat(locale, ",", fallback, ";q=", weight);
    }

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
                    && string.Equals(requestContext.SecFetchMode, "cors", StringComparison.OrdinalIgnoreCase))))
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
        // Приоритет запроса Safari ОТПРАВЛЯЕТ — это видно в записанном обмене, где он стоит
        // предпоследним. Косвенно за то же говорит и его параметр HTTP/2 «отказ от приоритетов
        // RFC 7540»: механизм заменён заголовком, а не выброшен.
        if (IsSafariProfile(profile))
        {
            return requestContext.Kind is RequestKind.Navigation ? "u=0, i" : null;
        }

        if (IsFirefoxProfile(profile))
        {
            if (requestContext.Kind is RequestKind.Navigation)
            {
                // iframe-навигация несёт приоритет обычного среднего запроса (u=4), а не
                // навигационный u=0 — живой capture Firefox 154.
                return requestContext.Destination is RequestDestination.Iframe ? "u=4" : "u=0, i";
            }

            if (requestContext.Kind is RequestKind.Fetch)
            {
                // Приоритеты subresource сняты с живого Firefox 154 на h1 (capture):
                // картинки — самые дешёвые (u=5, i), стили и скрипты — u=2, остальной fetch — u=4.
                return requestContext.Destination switch
                {
                    RequestDestination.Image => "u=5, i",
                    RequestDestination.Style or RequestDestination.Script => "u=2",
                    _ => "u=4",
                };
            }

            return null;
        }

        if (requestContext.Kind is RequestKind.Navigation)
        {
            if (requestContext.IsFormSubmission)
            {
                return null;
            }

            // Chromium шлёт Priority-заголовок только на h2/h3; на h1 его НЕТ (capture Chromium 151).
            if (requestContext.RequestVersionMajor is 1)
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

        if (requestContext.Kind is RequestKind.Fetch)
        {
            // Живой Chromium 151 на h1 не отправляет Priority ни на одном fetch/subresource-запросе
            // (capture: fetch GET/POST, img/script/style, preflight) — он для него h2/h3-изм.
            if (requestContext.RequestVersionMajor is 1)
            {
                return null;
            }

            return requestContext.Destination is not RequestDestination.Empty ? null : "u=1, i";
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

        // Правила PSL применяются по приоритету спецификации: exception → wildcard → exact,
        // и всегда выигрывает САМЫЙ ДЛИННЫЙ совпавший кандидат — перебор от длинных к коротким.
        // Суффикс длиной во весь host не рассматривается: у сайта должен остаться ярлык.
        var maxSuffixLabels = Math.Min(parts.Length - 1, 4);
        for (var take = maxSuffixLabels; take >= 1; take--)
        {
            var candidate = string.Join('.', parts[^take..]);

            // Исключение (!www.ck): кандидат — ОБЫЧНЫЙ домен, и он же является сайтом.
            if (publicSuffixExceptions.Contains(candidate))
                return candidate;

            // Wildcard (*.ck): кандидат "foo.ck" — публичный суффикс, если база "ck" объявлена.
            if (take >= 2 && wildcardSuffixBases.Contains(string.Join('.', parts[^(take - 1)..])))
                return string.Join('.', parts[^(take + 1)..]);

            if (commonMultiLabelPublicSuffixes.Contains(candidate))
                return string.Join('.', parts[^(take + 1)..]);
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
            || IsEdgeUserAgent(profile.UserAgent))
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
        // ★ Мобильный Edge помечает себя иначе: на Android это «EdgA/», на iOS «EdgiOS/», и
        // маркер «Edg/» их НЕ покрывает. Профиль Edge для Android поэтому объявлял себя брендом
        // Google Chrome, тогда как строка агента говорила EdgA, — расхождение между подсказкой
        // клиента и строкой агента проверяется тривиально и выдаёт подделку сразу.
        var brand = IsEdgeUserAgent(userAgent)
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

        foreach (var marker in EdgeMarkers)
        {
            var edgeMarker = userAgent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (edgeMarker >= 0) return ExtractVersionComponent(userAgent, edgeMarker + marker.Length);
        }

        return "0";
    }

    /// <summary>Как Edge помечает себя в строке агента на разных платформах.</summary>
    private static readonly string[] EdgeMarkers = ["Edg/", "EdgA/", "EdgiOS/", "Edge/"];

    /// <summary>
    /// Определяет, принадлежит ли строка агента браузеру Edge.
    /// </summary>
    /// <param name="userAgent">Строка агента.</param>
    /// <returns><see langword="true"/> для любой платформы Edge.</returns>
    private static bool IsEdgeUserAgent(string userAgent)
    {
        foreach (var marker in EdgeMarkers)
        {
            if (userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
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

    /// <summary>
    /// Составляет ключ узла для объявлений альтернативных служб.
    /// </summary>
    /// <param name="host">Имя узла.</param>
    /// <param name="port">Порт.</param>
    /// <returns>Ключ.</returns>
    private static string FormatOrigin(string host, int port)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{host}:{port}");

    /// <summary>
    /// Сохраняет куки ответа, не позволяя одной испорченной уронить запрос.
    /// </summary>
    /// <param name="uri">Адрес, с которого пришёл ответ.</param>
    /// <param name="response">Ответ.</param>
    /// <remarks>
    /// ★ <see cref="CookieContainer.SetCookies"/> бросает исключение на куке, которую браузер
    /// просто ОТБРАСЫВАЕТ, — и запрос падал целиком, уже после успешного получения ответа.
    /// Замер на двух сотнях узлов: так терялись <c>dotnet.microsoft.com</c>, <c>www.target.com</c>,
    /// <c>www.ikea.com</c>, <c>www.ebay.com</c>, <c>www.edx.org</c> — то есть около двух процентов
    /// живых сайтов, и ни один из них не «сломан» с точки зрения браузера.
    ///
    /// Две причины, обе встречены на проводе: чужой домен в атрибуте (Azure отдаёт куку для
    /// <c>*.azurewebsites.net</c> на запрос к <c>dotnet.microsoft.com</c>) и попросту битое
    /// значение, где сервер склеил куку саму с собой посреди даты.
    ///
    /// Поведение приведено к браузерному: негодная кука отбрасывается молча, остальные из того же
    /// ответа сохраняются. Разбирать их по одной приходится потому, что отказ на одной строке
    /// иначе унёс бы и годные объявления, если сервер сложил несколько в одно поле.
    /// </remarks>
    private void ApplyResponseCookies(Uri uri, HttpsResponseMessage response)
    {
        if (!UseCookies || response.Exception is not null) return;
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;

        foreach (var value in values)
        {
            try
            {
                CookieContainer.SetCookies(uri, value);
            }
            catch (CookieException)
            {
                StoreCookiesSeparately(uri, value);
            }
        }
    }

    /// <summary>
    /// Сохраняет по отдельности те объявления, что удаётся разобрать.
    /// </summary>
    /// <param name="uri">Адрес ответа.</param>
    /// <param name="value">Поле <c>Set-Cookie</c> целиком.</param>
    private void StoreCookiesSeparately(Uri uri, string value)
    {
        foreach (var declaration in SplitCookieDeclarations(value))
        {
            try
            {
                CookieContainer.SetCookies(uri, declaration);
            }
            catch (CookieException)
            {
                // Негодная кука — ровно то, что браузер здесь и делает: пропускает молча.
            }
        }
    }

    /// <summary>
    /// Разбивает поле на отдельные объявления кук.
    /// </summary>
    /// <param name="value">Поле <c>Set-Cookie</c>.</param>
    /// <returns>Объявления по одному.</returns>
    /// <remarks>
    /// Запятая разделяет объявления только тогда, когда следом идёт <c>имя=</c>. Внутри даты
    /// (<c>Expires=Thu, 24 Sep 2026 …</c>) запятая тоже есть, и деление по ней вслепую разорвало
    /// бы годную куку пополам.
    /// </remarks>
    private static List<string> SplitCookieDeclarations(string value)
    {
        var result = new List<string>();
        var start = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is not ',') continue;
            if (!StartsNewCookie(value, index + 1)) continue;

            result.Add(value[start..index]);
            start = index + 1;
        }

        result.Add(value[start..]);

        return result;
    }

    /// <summary>
    /// Определяет, начинается ли с этого места новое объявление куки.
    /// </summary>
    /// <param name="value">Поле целиком.</param>
    /// <param name="position">Позиция сразу после запятой.</param>
    /// <returns><see langword="true"/>, если дальше идёт <c>имя=</c>.</returns>
    private static bool StartsNewCookie(string value, int position)
    {
        while (position < value.Length && value[position] is ' ') position++;

        for (var index = position; index < value.Length; index++)
        {
            var current = value[index];

            if (current is '=') return index > position;
            if (current is ';' or ',' or ' ') return false;
        }

        return false;
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
        bool IsFormSubmission,
        int RequestVersionMajor);
}