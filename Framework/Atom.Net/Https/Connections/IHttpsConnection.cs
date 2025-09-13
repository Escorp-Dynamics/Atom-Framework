using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет базовый интерфейс для реализации соединений HTTP.
/// </summary>
internal interface IHttpsConnection : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Фактически согласованный протокол (HTTP/1.1, HTTP/2 или HTTP/3). Ставится из <see cref="HttpVersion"/>.
    /// Значение становится известно после успешного <see cref="OpenAsync(HttpsConnectionOptions, CancellationToken)"/>.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Флаг установленного и готового к обмену состояния соединения.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Соединение использует защищённый транспорт (TLS/QUIC-TLS).
    /// </summary>
    bool IsSecure { get; }

    /// <summary>
    /// Соединение поддерживает мультиплексирование запросов (HTTP/2/3).
    /// Для HTTP/1.1 всегда <c>false</c>.
    /// </summary>
    bool IsMultiplexing { get; }

    /// <summary>
    /// Текущая оценка количества активных потоков/запросов в рамках соединения.
    /// Для HTTP/1.1 — 0 или 1.
    /// </summary>
    int ActiveStreams { get; }

    /// <summary>
    /// Максимально допустимое число параллельных потоков (h2/h3) для данного соединения
    /// с учётом серверных SETTINGS/transport-параметров. Для HTTP/1.1 — 1.
    /// </summary>
    int MaxConcurrentStreams { get; }

    /// <summary>
    /// Текущее состояние дренажа: если <c>true</c>, новые запросы больше не принимаются,
    /// но активные корректно завершаются.
    /// </summary>
    bool IsDraining { get; }

    /// <summary>
    /// Локальная конечная точка (фактически выбранный IP:порт).
    /// </summary>
    IPEndPoint? LocalEndPoint { get; }

    /// <summary>
    /// Удалённая конечная точка (фактически выбранный IP:порт).
    /// </summary>
    IPEndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Последняя активность соединения (монотонная метка Stopwatch.GetTimestamp()).
    /// Используется внешним LRU-пулом для высвобождения простаивающих соединений.
    /// </summary>
    long LastActivityTimestamp { get; }

    /// <summary>
    /// Учёт трафика по соединению (вход/выход) с момента открытия.
    /// </summary>
    Traffic Traffic { get; }

    /// <summary>
    /// Проверка, может ли соединение принять ещё один запрос немедленно (без очереди).
    /// Для HTTP/1.1 вернёт <c>true</c>, если нет активного запроса; для h2/h3 — если не достигнут лимит потоков.
    /// </summary>
    bool HasCapacity { get; }

    /// <summary>
    /// Открывает (или привязывает к уже открытому транспорту) соединение под указанные опции.
    /// Метод обязан выполнить все этапы рукопожатия (TCP/QUIC + TLS + ALPN) и подготовить
    /// внутренние кодеки/фреймы (HTTP/1.1, HPACK, QPACK) согласно профилю браузера.
    /// </summary>
    /// <param name="options">Иммутабельный снимок настроек хэндлера и цели запроса (authority, ALPN, таймауты и пр.).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Задача без результата. В случае неуспеха реализация переведёт соединение в состояние <see cref="IsDraining"/> или выполнит <see cref="Abort()"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает (или привязывает к уже открытому транспорту) соединение под указанные опции.
    /// Метод обязан выполнить все этапы рукопожатия (TCP/QUIC + TLS + ALPN) и подготовить
    /// внутренние кодеки/фреймы (HTTP/1.1, HPACK, QPACK) согласно профилю браузера.
    /// </summary>
    /// <param name="options">Иммутабельный снимок настроек хэндлера и цели запроса (authority, ALPN, таймауты и пр.).</param>
    /// <returns>Задача без результата. В случае неуспеха реализация переведёт соединение в состояние <see cref="IsDraining"/> или выполнит <see cref="Abort()"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask OpenAsync(HttpsConnectionOptions options) => OpenAsync(options, CancellationToken.None);

    /// <summary>
    /// Отправляет единичный HTTP-запрос через данное соединение и возвращает безопасный ответ.
    /// Реализация берёт на себя: построение заголовков под профиль, кодирование (HPACK/QPACK),
    /// управление потоком (flow-control), авто-декомпрессию (если включена), учёт трафика и таймаутов.
    /// </summary>
    /// <param name="request">Запрос в расширенном формате (<c>HttpsRequestMessage</c>), уже валидированный хэндлером.</param>
    /// <param name="cancellationToken">Токен отмены. Отмена не должна разрывать соединение (если возможно).</param>
    /// <returns>
    /// Безопасный <c>HttpsResponseMessage</c> (никогда не бросает исключений наружу). В случае ошибок заполнено поле <c>Exception</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// Отправляет единичный HTTP-запрос через данное соединение и возвращает безопасный ответ.
    /// Реализация берёт на себя: построение заголовков под профиль, кодирование (HPACK/QPACK),
    /// управление потоком (flow-control), авто-декомпрессию (если включена), учёт трафика и таймаутов.
    /// </summary>
    /// <param name="request">Запрос в расширенном формате (<c>HttpsRequestMessage</c>), уже валидированный хэндлером.</param>
    /// <returns>
    /// Безопасный <c>HttpsResponseMessage</c> (никогда не бросает исключений наружу). В случае ошибок заполнено поле <c>Exception</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request) => SendAsync(request, CancellationToken.None);

    /// <summary>
    /// Переводит соединение в режим дренажа: новые запросы не принимаются, активные завершаются,
    /// после чего рекомендуется вызвать <see cref="CloseAsync(CancellationToken)"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void StartDrain();

    /// <summary>
    /// Корректное закрытие соединения после дренажа или по инициативе пула.
    /// Должно попытаться отправить/дождаться необходимых служебных фреймов (GOAWAY, SETTINGS, FIN).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Корректное закрытие соединения после дренажа или по инициативе пула.
    /// Должно попытаться отправить/дождаться необходимых служебных фреймов (GOAWAY, SETTINGS, FIN).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <summary>
    /// Немедленный обрыв соединения при фатальной ошибке.
    /// </summary>
    /// <param name="ex">Исключение-источник для логирования/диагностики.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Abort([AllowNull] Exception ex);

    /// <summary>
    /// Немедленный обрыв соединения при фатальной ошибке.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Abort() => Abort(default);

    /// <summary>
    /// Быстрый пинг для поддержания keep-alive (например, HTTP/2 PING, HTTP/3 DATAGRAM/PATH-CHALLENGE, HTTP/1.1 — TCP keep-alive).
    /// Необязательно посылать сетевой пакет: реализация может использовать эвристику браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если соединение подтверждено живым и пригодным к повторному использованию; иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> PingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Быстрый пинг для поддержания keep-alive (например, HTTP/2 PING, HTTP/3 DATAGRAM/PATH-CHALLENGE, HTTP/1.1 — TCP keep-alive).
    /// Необязательно посылать сетевой пакет: реализация может использовать эвристику браузера.
    /// </summary>
    /// <returns><c>true</c>, если соединение подтверждено живым и пригодным к повторному использованию; иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> PingAsync() => PingAsync(CancellationToken.None);

    /// <summary>
    /// Проверяет, соответствует ли соединение указанной цели (authority) и может ли быть переиспользовано
    /// под данный хост/порт/схему (например, в пуле).
    /// </summary>
    /// <param name="host">Хост (нормализованный, без порта).</param>
    /// <param name="port">Порт.</param>
    /// <param name="isHttps">True для HTTPS/QUIC-TLS.</param>
    /// <returns>True, если соединение совпадает по authority и не находится в <see cref="IsDraining"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool MatchesTarget(string host, int port, bool isHttps);
}