using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Atom.Net.Https.Connections;

namespace Atom.Net.Https;

public sealed partial class HttpsClientHandler
{
    private bool CanReuseConnection(HttpsConnection connection, HttpsResponseMessage response)
        => response.Exception is null
        && connection.IsConnected
        && !connection.IsDraining
        && connection.HasCapacity
        && !IsConnectionExpired(connection)
        && !IsConnectionLifetimeExpired(connection)
        && MaxConnectionsPerServer > 0;

    private async ValueTask<HttpsConnection?> TryRentConnectionAsync(ConnectionPoolKey key, ConnectionPoolState poolState, CancellationToken cancellationToken)
    {
        while (poolState.Connections.TryDequeue(out var connection))
        {
            if (IsConnectionExpired(connection) || IsConnectionLifetimeExpired(connection))
            {
                await DisposeConnectionAsync(connection).ConfigureAwait(false);
                continue;
            }

            // ★ Одного признака IsConnected мало: он выставляется при подключении и о закрытии
            // с той стороны ничего не знает — в простое из сокета никто не читает. Сервер же
            // рвёт keep-alive по своему тайм-ауту (у Cloudflare и балансировщиков это единицы
            // секунд), и запрос уходил в наполовину закрытый сокет, получая пустой ответ.
            // PingAsync смотрит сокет напрямую и такое соединение отбраковывает.
            if (connection.IsConnected
                && connection.MatchesTarget(key.Host, key.Port, key.IsHttps)
                && connection.HasCapacity
                && await IsAliveAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return connection;
            }

            await DisposeConnectionAsync(connection).ConfigureAwait(false);
        }

        return null;
    }

    private void ReturnConnection(ConnectionPoolState poolState, HttpsConnection connection)
    {
        if (MaxConnectionsPerServer <= 0 || IsConnectionExpired(connection) || IsConnectionLifetimeExpired(connection))
        {
            DisposeConnection(connection);
            return;
        }

        while (poolState.Connections.Count >= MaxConnectionsPerServer && poolState.Connections.TryDequeue(out var extraConnection))
            DisposeConnection(extraConnection);

        poolState.Connections.Enqueue(connection);
    }

    /// <summary>
    /// Пригодно ли мультиплексируемое соединение для нового запроса.
    /// </summary>
    /// <param name="connection">Соединение-кандидат.</param>
    /// <param name="key">Ключ пула.</param>
    /// <returns><see langword="true"/>, если запрос можно отправить по нему.</returns>
    /// <remarks>
    /// ★ Срок простоя проверяется и здесь. Прежде он относился только к обычным соединениям, а
    /// общее (HTTP/2 и HTTP/3) не имело НИ срока простоя, НИ срока жизни: время жизни по
    /// умолчанию бесконечно, и опубликованное соединение оставалось в пуле навсегда — вместе с
    /// сокетом, ключами и фоновым циклом чтения. При обходе тысяч узлов это растёт без единой
    /// записи в журнале, пока не упрётся в предел дескрипторов.
    /// </remarks>
    private bool CanUseMultiplexed(HttpsConnection connection, ConnectionPoolKey key)
        => connection.IsConnected
        && !connection.IsDraining
        && connection.HasCapacity
        && connection.MatchesTarget(key.Host, key.Port, key.IsHttps)
        && !IsConnectionExpired(connection)
        && !IsConnectionLifetimeExpired(connection);

    /// <summary>
    /// Проверяет, не закрыл ли партнёр соединение, пока оно простаивало.
    /// </summary>
    /// <param name="connection">Соединение из пула.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="false"/>, если сокет уже непригоден.</returns>
    /// <remarks>
    /// Проверка мгновенная и без обмена: у неё нет права ошибаться в другую сторону, поэтому при
    /// любой неопределённости соединение считается живым — отбраковать исправное дороже.
    /// </remarks>
    private static async ValueTask<bool> IsAliveAsync(HttpsConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.PingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static void DisposeConnection(HttpsConnection connection) => connection.Dispose();

    private static ValueTask DisposeConnectionAsync(HttpsConnection connection) => connection.DisposeAsync();

    private bool IsConnectionExpired(HttpsConnection connection)
    {
        if (PooledConnectionIdleTimeout == Timeout.InfiniteTimeSpan) return false;
        if (PooledConnectionIdleTimeout <= TimeSpan.Zero) return true;

        var lastActivity = connection.LastActivityTimestamp;
        if (lastActivity <= 0) return true;

        return Stopwatch.GetElapsedTime(lastActivity) >= PooledConnectionIdleTimeout;
    }

    private bool IsConnectionLifetimeExpired(HttpsConnection connection)
    {
        if (PooledConnectionLifetime == Timeout.InfiniteTimeSpan) return false;
        if (PooledConnectionLifetime <= TimeSpan.Zero) return true;

        var created = connection.CreatedTimestamp;
        if (created <= 0) return true;

        return Stopwatch.GetElapsedTime(created) >= PooledConnectionLifetime;
    }

    /// <summary>
    /// Что известно о прикладном протоколе узла по прошлым подключениям.
    /// </summary>
    private enum PoolProtocolHint
    {
        /// <summary>К узлу ещё не подключались.</summary>
        Unknown = 0,

        /// <summary>Узел говорит по HTTP/1.1: соединения эксклюзивные.</summary>
        Exclusive = 1,

        /// <summary>Узел говорит по HTTP/2: одно соединение обслуживает много запросов.</summary>
        Multiplexed = 2,
    }

    /// <summary>
    /// Состояние пула соединений одного узла.
    /// </summary>
    /// <remarks>
    /// Пул держит два принципиально разных вида соединений, и смешивать их нельзя.
    ///
    /// HTTP/1.1 — очередь эксклюзивных: соединение занимает ровно один запрос, а число
    /// одновременных ограничено семафором слотов.
    ///
    /// HTTP/2 — одно общее: параллельные запросы идут по нему разными потоками, и брать его «в
    /// аренду» не нужно вовсе. Ограничение здесь другое — предел одновременных потоков сервера, и
    /// занимать под каждый запрос слот значило бы свести мультиплексирование на нет.
    ///
    /// Какой из двух видов нужен, до первого подключения неизвестно: протокол выбирает сервер в
    /// ALPN. Поэтому первое подключение к незнакомому узлу проходит через ворота, а его результат
    /// запоминается подсказкой — дальше путь выбирается без всякой синхронизации.
    /// </remarks>
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Семафоры пула живут столько же, сколько обработчик: активные запросы освобождают слоты уже после начала разбора пула.")]
    private sealed class ConnectionPoolState : IDisposable
    {
        private readonly SemaphoreSlim? slots;
        private readonly SemaphoreSlim connectGate = new(1, 1);
        private readonly ConcurrentQueue<HttpsConnection> retired = new();
        private HttpsConnection? multiplexed;
        private long lastUsed = Stopwatch.GetTimestamp();
        private int protocolHint;

        public ConnectionPoolState(int maxConnections)
        {
            if (maxConnections is > 0 and not int.MaxValue)
                slots = new SemaphoreSlim(maxConnections, maxConnections);
        }

        public ConcurrentQueue<HttpsConnection> Connections { get; } = new();

        /// <summary>
        /// Откладывает закрытие соединения до завершения начатых по нему потоков.
        /// </summary>
        /// <param name="connection">Соединение, снятое с должности общего.</param>
        /// <remarks>
        /// Мультиплексируемое соединение нельзя закрыть в тот момент, когда оно признано
        /// непригодным для НОВЫХ запросов: по нему прямо сейчас могут идти чужие. Отложенное
        /// закрытие разделяет эти два события — «больше не выдавать» и «можно освобождать».
        /// </remarks>
        public void Retire(HttpsConnection connection) => retired.Enqueue(connection);

        /// <summary>
        /// Закрывает отложенные соединения, по которым обмен уже закончился.
        /// </summary>
        /// <remarks>
        /// Один проход по очереди фиксированной длины: занятые соединения возвращаются в конец и
        /// будут рассмотрены при следующем обращении к пулу. Так уборка не превращается ни в
        /// отдельный поток, ни в цикл, способный крутиться, пока идёт долгий ответ.
        /// </remarks>
        public void SweepRetired()
        {
            // Анализатор не видит, что соединение либо возвращается в очередь, либо закрывается
            // здесь же; владение не теряется ни в одной ветке.
#pragma warning disable CA2000
            for (var remaining = retired.Count; remaining > 0; remaining--)
            {
                if (!retired.TryDequeue(out var connection)) break;

                if (connection.ActiveStreams > 0)
                {
                    retired.Enqueue(connection);
                    continue;
                }

                connection.Dispose();
            }
#pragma warning restore CA2000
        }

        /// <summary>Текущее общее соединение HTTP/2, если оно установлено.</summary>
        public HttpsConnection? Multiplexed => Volatile.Read(ref multiplexed);

        /// <summary>Отмечает обращение к записи узла.</summary>
        public void Touch() => Volatile.Write(ref lastUsed, Stopwatch.GetTimestamp());

        /// <summary>
        /// Пуста ли запись и давно ли к ней не обращались.
        /// </summary>
        /// <param name="idleFor">Через сколько бездействия запись считается ненужной.</param>
        /// <returns><see langword="true"/>, если запись можно убрать.</returns>
        /// <remarks>
        /// Пустая — значит НИ общего соединения, НИ обычных в очереди, НИ снятых: удалять запись
        /// с живым соединением нельзя, иначе оно останется висеть без владельца.
        /// </remarks>
        public bool IsAbandoned(TimeSpan idleFor)
            => Volatile.Read(ref multiplexed) is null
            && Connections.IsEmpty
            && retired.IsEmpty
            && Stopwatch.GetElapsedTime(Volatile.Read(ref lastUsed)) >= idleFor;

        /// <summary>Что известно о протоколе узла.</summary>
        public PoolProtocolHint Hint => (PoolProtocolHint)Volatile.Read(ref protocolHint);

        public void SetHint(PoolProtocolHint value) => Volatile.Write(ref protocolHint, (int)value);

        /// <summary>
        /// Публикует общее соединение, если место свободно.
        /// </summary>
        /// <param name="connection">Соединение.</param>
        /// <returns><see langword="true"/>, если соединение стало общим для узла.</returns>
        public bool TryPublishMultiplexed(HttpsConnection connection)
            => Interlocked.CompareExchange(ref multiplexed, connection, comparand: null) is null;

        /// <summary>
        /// Снимает общее соединение, если оно всё ещё то самое.
        /// </summary>
        /// <param name="connection">Соединение, ставшее непригодным.</param>
        /// <returns><see langword="true"/>, если снято именно оно.</returns>
        public bool TryClearMultiplexed(HttpsConnection connection)
            => ReferenceEquals(Interlocked.CompareExchange(ref multiplexed, value: null, comparand: connection), connection);

        public ValueTask WaitForLeaseAsync(CancellationToken cancellationToken)
            => slots is null ? ValueTask.CompletedTask : new ValueTask(slots.WaitAsync(cancellationToken));

        public void ReleaseLease()
        {
            if (slots is null) return;
            slots.Release();
        }

        /// <summary>
        /// Входит в ворота первого подключения к узлу.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Задача, завершающаяся при входе.</returns>
        /// <remarks>
        /// Ворота нужны ровно один раз на узел. Без них пачка одновременных первых запросов
        /// открыла бы по соединению каждый, а затем выяснилось бы, что узел говорит по HTTP/2 и
        /// хватило бы одного: лишние подключения — это лишние рукопожатия TLS, самая дорогая часть
        /// обмена. Как только протокол известен, путь HTTP/1.1 идёт мимо ворот и подключается
        /// параллельно, как ему и положено.
        /// </remarks>
        public Task EnterConnectGateAsync(CancellationToken cancellationToken) => connectGate.WaitAsync(cancellationToken);

        public void ExitConnectGate() => connectGate.Release();

        /// <summary>
        /// Закрывает всё, чем владеет состояние узла.
        /// </summary>
        /// <remarks>
        /// ★ Прежде метод был ПУСТ, а закрытие обработчика разбирало только очередь обычных
        /// соединений. Общее (HTTP/2 и HTTP/3) живёт отдельным полем, снятые — отдельной
        /// очередью; ни то, ни другое не освобождалось. Финализатора у соединений нет, поэтому
        /// сокет, ключи и фоновый цикл чтения оставались висеть до конца работы процесса.
        ///
        /// Заметно это там, где обработчики создают и выбрасывают пачками — под ротацию прокси
        /// или профилей: каждый цикл оставлял по открытому сокету на каждый узел.
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref multiplexed, value: null) is { } shared) shared.Dispose();

            while (retired.TryDequeue(out var connection)) connection.Dispose();

            while (Connections.TryDequeue(out var connection)) connection.Dispose();

            connectGate.Dispose();
            slots?.Dispose();
        }
    }

    private readonly record struct ConnectionPoolStateFactoryArg(int MaxConnections);
    /// <summary>
    /// Ключ пула соединений.
    /// </summary>
    /// <param name="Host">Целевой узел.</param>
    /// <param name="Port">Целевой порт.</param>
    /// <param name="IsHttps">Защищённый ли транспорт.</param>
    /// <param name="UpstreamProxy">Апстрим-прокси или <see langword="null"/> при прямом подключении.</param>
    /// <param name="IsQuic">Использует ли соединение транспорт QUIC.</param>
    /// <remarks>
    /// Прокси входит в ключ не для порядка: соединения через разные прокси выходят наружу с разных
    /// адресов, и переиспользовать их вперемешку — значит отправить запрос не оттуда, откуда его
    /// ждут. Для сервисов, привязывающих сессию к адресу, это выглядит как угон сессии.
    ///
    /// Транспорт входит по другой причине: HTTP/3 живёт поверх UDP, а HTTP/1.1 и HTTP/2 — поверх
    /// TCP. Это физически разные соединения, и общий пул означал бы, что явно запрошенная версия
    /// протокола молча подменяется той, что случайно оказалась установлена раньше.
    /// </remarks>
    private readonly record struct ConnectionPoolKey(string Host, int Port, bool IsHttps, string? UpstreamProxy, bool IsQuic);

    /// <summary>
    /// Соединение, выданное пулом, вместе с обязательствами по его возврату.
    /// </summary>
    /// <param name="Connection">Соединение, по которому пойдёт запрос.</param>
    /// <param name="PoolState">Состояние пула узла или <see langword="null"/>, если пул отключён.</param>
    /// <param name="LeaseHeld">Удерживается ли слот пула, который нужно освободить.</param>
    /// <param name="IsShared">Является ли соединение общим: такое не возвращают и не закрывают после запроса.</param>
    private readonly record struct ConnectionLease(
        HttpsConnection Connection,
        ConnectionPoolState? PoolState,
        bool LeaseHeld,
        bool IsShared);
}
