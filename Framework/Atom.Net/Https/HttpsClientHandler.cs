using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Http;
using Atom.Net.Tls;

namespace Atom.Net.Https;

/// <summary>
/// Представляет обработчик HTTPS-запросов.
/// </summary>
public class HttpsClientHandler : HttpMessageHandler
{
    internal volatile int activeRequests;
    internal volatile bool isFirstRequestSended;
    internal volatile bool isReadyForDisposing;

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
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

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
    public virtual bool SupportsAutomaticDecompression { get; } = true;

    /// <summary>
    /// Получает значение, указывающее, поддерживает ли обработчик параметры прокси.
    /// </summary>
    public virtual bool SupportsProxy { get; } = true;

    /// <summary>
    /// Получает значение, указывающее, поддерживает ли обработчик параметры конфигурации для свойств <see cref="AllowAutoRedirect"/>
    /// и <see cref="MaxAutomaticRedirections"/>.
    /// </summary>
    public virtual bool SupportsRedirectConfiguration { get; } = true;

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
    /// Случайный джиттер к любому из интервалов (мин/макс).
    /// </summary>
    public JitterSettings Jitter { get; set; }

    /// <summary>
    /// Пауза между HEADERS и DATA (для H2/H3) либо между статус-линией и телом (H1).
    /// </summary>
    public TimeSpan HeadersToDataDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Возвращает или задает максимальный объем данных, который может быть извлечен из ответов в байтах.
    /// </summary>
    public int MaxResponseDrainSize { get; set; } = int.MaxValue;

    /// <summary>
    /// Позволяет получить или задать пользовательский обратный вызов, который предоставляет доступ к потоку протокола HTTP с обычным текстом.
    /// </summary>
    public Func<SocketsHttpPlaintextStreamFilterContext, CancellationToken, ValueTask<Stream>>? PlaintextStreamFilter { get; set; }

    /// <summary>
    /// Получает или задает время неактивности соединения в пуле, после которого оно будет считаться доступным для повторного использования.
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Получает или задает время соединения в пуле, после которого оно будет считаться доступным для повторного использования.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Возвращает или задает обратный вызов, который выбирает для кодирования значений <see cref="Encoding"/> заголовка запроса.
    /// </summary>
    public HeaderEncodingSelector<HttpRequestMessage>? RequestHeaderEncodingSelector { get; set; }

    /// <summary>
    /// Возвращает или задает период времени, в течение которого данные должны быть извлечены из ответов.
    /// </summary>
    public TimeSpan ResponseDrainTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Возвращает или задает обратный вызов, который выбирает для декодирования значений <see cref="Encoding"/> заголовка ответа.
    /// </summary>
    public HeaderEncodingSelector<HttpRequestMessage>? ResponseHeaderEncodingSelector { get; set; }

    /// <summary>
    /// Возвращает или задает набор параметров, используемых для проверки подлинности клиента TLS.
    /// </summary>
    public SslClientAuthenticationOptions SslOptions { get; set; } = new();

    /// <summary>
    /// Настройки HTTP/1.1.
    /// </summary>
    public Http11Settings Http11 { get; set; }

    /// <summary>
    /// Настройки HTTP/2.
    /// </summary>
    public Http2Settings Http2 { get; set; }

    /// <summary>
    /// Настройки HTTP/3.
    /// </summary>
    public Http3Settings Http3 { get; set; }

    /// <summary>
    /// Валидатор Client Hello.
    /// </summary>
    public IClientHelloValidator? ClientHelloValidator { get; set; }

    /// <summary>
    /// Использовать ли режим инкогнито.
    /// </summary>
    public bool UseIncognitoMode { get; set; }

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

        var port = uri.IsDefaultPort ? (isHttps ? 443 : 80) : uri.Port;

        // Политика версии берётся из запроса. При необходимости можно переопределить профилем.
        var versionPolicy = request.VersionPolicy;

        // Вычисление лимита потоков: для H1 всегда 1; для H2/H3 — берём из настроек.
        var maxStreams = request.Version.Major >= 2 ? (request.Version.Major is 2 ? Http2.MaxConcurrentStreams : Http3.MaxStreamsBidi) : 1;

        return new HttpsConnectionOptions
        {
            Host = uri.IdnHost,     // соответствует браузерной мимикрии и SNI.
            Port = port,
            IsHttps = isHttps,
            PreferredVersion = request.Version, // 1.1 / 2.0 / 3.0
            VersionPolicy = versionPolicy,   // System.Net.Http.HttpVersionPolicy
            LocalEndPoint = null,            // TODO: пробросить из TcpSettings при необходимости точной привязки.
            ConnectTimeout = ConnectTimeout,
            // Браузеры имеют ограничение на ожидание стартовых заголовков — берём консервативное значение.
            // При появлении собственного свойства перетащим сюда прямую настройку.
            ResponseHeadersTimeout = TimeSpan.FromSeconds(30),
            IdleTimeout = PooledConnectionIdleTimeout,
            MaxConcurrentStreams = maxStreams,
            AutoDecompression = AutomaticDecompression != DecompressionMethods.None
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async Task<HttpsResponseMessage> SendInternalAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref activeRequests);

        var tlsSettings = request.Version.Major switch
        {
            2 => Http2.Tls,
            3 => Http3.Tls,
            _ => Http11.Tls,
        };

        if (!Interlocked.CompareExchange(ref isFirstRequestSended, value: true, default)) ClientHelloValidator?.Validate(tlsSettings);

        try
        {
            if (request is HttpsRequestMessage r)
            {
                // 1) Сборка опций соединения из настроек хэндлера + параметров запроса.
                var options = BuildConnectionOptions(r);

                // 2) Выбор реализации соединения по версии (с учётом политики).
                using var connection = CreateConnection(options.PreferredVersion);

                await using (connection.ConfigureAwait(false))
                {

                    // 3) Открытие соединения (TCP/QUIC + TLS + ALPN) и подготовка кодеков (H1/HPACK/QPACK).
                    await connection.OpenAsync(options, cancellationToken).ConfigureAwait(false);

                    // 4) Отправка запроса. Реализация сама учтёт HPACK/QPACK/flow-control/декодирование.
                    var response = await connection.SendAsync(r, cancellationToken).ConfigureAwait(false);

                    // 5) Для простоты «скелета» закрываем соединение после запроса.
                    // В дальнейшем это место будет переключено на пул/реюз соединений.
                    // StartDrain сигнализирует корректное завершение активных потоков (h2/h3).
                    connection.StartDrain();
                    await connection.CloseAsync(cancellationToken).ConfigureAwait(false);

                    return response;
                }
            }

            throw new NotImplementedException(); // TODO: Реализовать логику отправки запроса и получения ответа с базовыми параметрами запроса (с учётом трафика).
        }
        finally
        {
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
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Выбирает реализацию соединения под целевую версию HTTP.
    /// </summary>
    /// <param name="preferred">Предпочитаемая версия.</param>
    /// <returns>Экземпляр соединения.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IHttpsConnection CreateConnection(Version preferred)
    {
        // Быстрый выбор без ветвлений по строкам и без аллокаций.
        return preferred.Major >= 3 ? new Https3Connection()
             : preferred.Major is 2 ? new Https2Connection()
             : new Https11Connection();
    }
}