using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Иммутабельный снимок настроек, необходимых для открытия/привязки соединения
/// под конкретный <c>authority</c> (host:port + схема).
/// Хранится и передаётся по ссылке (<see langword="in"/>) для исключения копий и аллокаций.
/// </summary>
internal readonly record struct HttpsConnectionOptions
{
    /// <summary>
    /// Целевой узел (без порта). Обязателен. Должен быть уже нормализован и соответствовать SNI/HTTP-Host.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Целевой порт. Обязателен. Обычно 443 для HTTPS и 80 для HTTP.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Признак использования защищённого транспорта (TLS/QUIC-TLS). Управляет выбором стека.
    /// </summary>
    public bool IsHttps { get; init; }

    /// <summary>
    /// Предпочитаемая версия HTTP (для предсоздания h2/h3-соединений и планировщика). Ставится через <see cref="HttpVersion"/>.
    /// Фактическую согласованную версию смотрите в <see cref="IHttpsConnection.Version"/>.
    /// </summary>
    public Version PreferredVersion { get; init; }

    /// <summary>
    /// Политика выбора версии (жёстко/гибко) при согласовании ALPN/версии.
    /// </summary>
    public HttpVersionPolicy VersionPolicy { get; init; }

    /// <summary>
    /// Локальная привязка (если нужна мимикрия конкретного интерфейса/адреса).
    /// При значении <see langword="null"/> выбор остаётся за стеком TCP/UDP.
    /// </summary>
    public IPEndPoint? LocalEndPoint { get; init; }

    /// <summary>
    /// Таймаут установления соединения (TCP/QUIC + TLS). Должен учитываться без блокировок.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; }

    /// <summary>
    /// Таймаут приёма заголовков ответа (старт первой байтовой активности после отправки запроса).
    /// </summary>
    public TimeSpan ResponseHeadersTimeout { get; init; }

    /// <summary>
    /// Разрешённые версии TLS для защищённого соединения.
    /// </summary>
    public SslProtocols SslProtocols { get; init; }

    /// <summary>
    /// Использовать ли онлайн-проверку отзыва сертификата при валидации цепочки.
    /// </summary>
    public bool CheckCertificateRevocationList { get; init; }

    /// <summary>
    /// Пользовательская валидация сертификата сервера.
    /// </summary>
    public Func<X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateValidationCallback { get; init; }

    /// <summary>
    /// Максимально допустимый суммарный размер status line и response headers в байтах.
    /// </summary>
    public int MaxResponseHeadersBytes { get; init; }

    /// <summary>
    /// Таймаут простоя соединения, после которого оно переводится в drain/закрывается пулом.
    /// Управляется внешним пулом, но соединение предоставляет <see cref="IHttpsConnection.LastActivityTimestamp"/>.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; }

    /// <summary>
    /// Максимально допустимое количество параллельных потоков в рамках соединения (для HTTP/2/3).
    /// Для HTTP/1.1 должно быть 1.
    /// </summary>
    public uint MaxConcurrentStreams { get; init; }

    /// <summary>
    /// Разрешить авто-декомпрессию (gzip/deflate/br/zstd) на уровне соединения.
    /// Точная политика (порядок/варианты) задаётся профилем браузера и HeaderPolicy выше по слою.
    /// </summary>
    public bool AutoDecompression { get; init; }
}