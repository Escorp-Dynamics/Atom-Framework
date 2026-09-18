using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Atom.Net;

/// <summary>
/// Кэш успешных разрешений DNS с ограниченным временем жизни и совмещением одновременных запросов.
/// </summary>
/// <remarks>
/// ★ Системный резолвер сам по себе НИЧЕГО не помнит между вызовами: <see cref="Dns"/> обращается к
/// ОС на каждый запрос. Пока соединение живёт минутами, это незаметно; но короткие соединения
/// через прокси открываются сотнями в минуту к одному и тому же имени, и тогда каждый раз к
/// установлению соединения добавляется полный обход резолвера. Хуже задержки здесь другое: часть
/// провайдеров и публичных резолверов начинает ограничивать частоту запросов, и ответ приходит не
/// медленный, а пустой — соединение падает с <see cref="SocketError.HostNotFound"/> на имени,
/// которое разрешалось секунду назад.
///
/// Кэшируются ТОЛЬКО успешные ответы. Отрицательный кэш здесь был бы вреден: временный сбой
/// резолвера закрепился бы на всё время жизни записи и выключил бы работу с узлом, который уже
/// снова доступен.
///
/// Время жизни намеренно короткое. Записи DNS меняются — у балансировщиков постоянно, — и держать
/// адрес дольше минуты значит ходить на выведенный из работы узел; смысл кэша не в том, чтобы
/// помнить долго, а в том, чтобы не спрашивать одно и то же по сто раз в секунду.
/// </remarks>
public sealed class DnsCache
{
    /// <summary>Переменная окружения, задающая время жизни записи в секундах; <c lang="text">0</c> выключает кэш.</summary>
    public const string TimeToLiveVariableName = "ATOM_DNS_CACHE_TTL";

    /// <summary>Переменная окружения, задающая предел числа записей.</summary>
    public const string CapacityVariableName = "ATOM_DNS_CACHE_CAPACITY";

    /// <summary>Предел числа записей по умолчанию.</summary>
    public const int DefaultCapacity = 1024;

    /// <summary>Время жизни записи по умолчанию.</summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan timeToLive;
    private int capacity;
    private int evicting;

    /// <summary>
    /// Создаёт кэш с заданными временем жизни и пределом числа записей.
    /// </summary>
    /// <param name="timeToLive">Время жизни записи; <see cref="TimeSpan.Zero"/> полностью выключает кэширование.</param>
    /// <param name="capacity">Предел числа записей; обязан быть положительным.</param>
    public DnsCache(TimeSpan timeToLive, int capacity)
    {
        TimeToLive = timeToLive;
        Capacity = capacity;
    }

    /// <summary>
    /// Общий кэш, которым пользуется разрешение имён в <see cref="NetworkStream"/>.
    /// </summary>
    /// <remarks>
    /// Кэш общий на процесс сознательно: смысл он имеет ровно тогда, когда к одному имени
    /// обращаются РАЗНЫЕ соединения, а свой кэш у каждого соединения не сэкономил бы ни одного
    /// запроса.
    ///
    /// Настройки читаются из окружения при первом обращении — чтобы выключить кэш в рабочем
    /// окружении, не пересобирая приложение.
    /// </remarks>
    public static DnsCache Shared { get; } = CreateShared();

    /// <summary>
    /// Время жизни записи; <see cref="TimeSpan.Zero"/> означает «не кэшировать».
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Значение отрицательно.</exception>
    public TimeSpan TimeToLive
    {
        get => timeToLive;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            timeToLive = value;
        }
    }

    /// <summary>
    /// Предел числа записей: по его достижении вытесняются протухшие, а затем самые старые.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Значение не положительно.</exception>
    /// <remarks>
    /// Предел обязателен, а не желателен: ключ здесь — имя хоста, и без него кэш рос бы вместе с
    /// числом посещённых узлов, то есть неограниченно.
    /// </remarks>
    public int Capacity
    {
        get => capacity;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            capacity = value;
        }
    }

    /// <summary>Текущее число записей, включая ещё не завершённые разрешения.</summary>
    public int Count => entries.Count;

    /// <summary>
    /// Возвращает адреса хоста из кэша либо разрешает их заданным резолвером.
    /// </summary>
    /// <param name="host">Имя хоста.</param>
    /// <param name="resolve">Резолвер, применяемый при промахе.</param>
    /// <param name="cancellationToken">Токен отмены ожидания.</param>
    /// <returns>Непустой набор адресов.</returns>
    /// <exception cref="OperationCanceledException">Ожидание отменено через <paramref name="cancellationToken"/>.</exception>
    /// <remarks>
    /// ★ Резолвер передаётся параметром, а не берётся из <see cref="Dns"/> напрямую, по одной
    /// причине: иначе класс нельзя проверить тестом, не обратившись в настоящую сеть, — а тест,
    /// зависящий от чужого DNS, рано или поздно начинает падать по причинам, к коду отношения не
    /// имеющим.
    ///
    /// Возвращается КОПИЯ набора: массив из кэша переживёт вызывающего, и правка на его стороне
    /// молча испортила бы адреса всем следующим соединениям.
    /// </remarks>
    public ValueTask<IPAddress[]> ResolveAsync(string host, Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        // Выключатель и вырожденные случаи уходят мимо кэша целиком, вместе с его накладными
        // расходами.
        if (timeToLive <= TimeSpan.Zero || string.IsNullOrEmpty(host)) return resolve(host, cancellationToken);

        // Литеральный адрес разрешать нечем и незачем: запись о нём заняла бы место, ничего не
        // экономя. Вызывающие стороны отсекают такие имена и сами, но инвариант обязан держаться
        // здесь — он про кэш, а не про них.
        if (IPAddress.TryParse(host, out _)) return resolve(host, cancellationToken);

        if (entries.TryGetValue(host, out var cached) && TryReadFresh(cached, out var addresses))
            return new ValueTask<IPAddress[]>(addresses);

        return new ValueTask<IPAddress[]>(ResolveSlowAsync(host, resolve, cancellationToken));
    }

    /// <summary>Забывает все записи.</summary>
    public void Clear() => entries.Clear();

    /// <summary>
    /// Читает запись, если она успешна и ещё не протухла.
    /// </summary>
    private bool TryReadFresh(Entry entry, out IPAddress[] addresses)
    {
        addresses = [];

        // Результат читается из поля, а не из задачи: она нужна только тем, кто ЖДЁТ разрешения, а
        // попадание в кэш обязано обходиться без обращения к готовой задаче вовсе.
        var resolved = Volatile.Read(ref entry.Addresses);
        if (resolved is null) return false;
        if (Stopwatch.GetElapsedTime(Volatile.Read(ref entry.Resolved)) >= timeToLive) return false;

        addresses = Copy(resolved);

        return true;
    }

    /// <summary>
    /// Медленный путь: присоединение к идущему разрешению либо запуск своего.
    /// </summary>
    private async Task<IPAddress[]> ResolveSlowAsync(string host, Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (entries.TryGetValue(host, out var existing))
            {
                var task = existing.Completion.Task;

                // ★ Совмещение запросов. Без него сотня соединений, поднятых разом к одному узлу,
                // даёт сотню одинаковых запросов к резолверу — то самое стадо, ради которого
                // резолверы и включают ограничение частоты.
                if (!task.IsCompleted) return Copy(await task.WaitAsync(cancellationToken).ConfigureAwait(false));

                if (TryReadFresh(existing, out var fresh)) return fresh;

                // Протухшую или отказавшую запись убираем ИМЕННО ту, которую прочитали: за время
                // проверки её мог заменить сосед, и удаление по одному ключу выбросило бы чужой
                // свежий результат.
                entries.TryRemove(new KeyValuePair<string, Entry>(host, existing));

                continue;
            }

            var entry = new Entry();
            if (!entries.TryAdd(host, entry)) continue;

            StartResolve(host, resolve, entry);

            return Copy(await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Выполняет разрешение и заполняет запись.
    /// </summary>
    /// <remarks>
    /// ★ Резолвер запускается БЕЗ токена вызывающего. Работа общая: отмена одного из ждущих
    /// оборвала бы её всем остальным, хотя каждый из них пришёл со своим сроком. Собственный
    /// токен каждый применяет к ОЖИДАНИЮ (<see cref="Task.WaitAsync(CancellationToken)"/>), и
    /// брошенное разрешение спокойно доходит до конца, попутно наполняя кэш.
    /// </remarks>
    private void StartResolve(string host, Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve, Entry entry) => _ = ResolveCoreAsync(host, resolve, entry);

    private async Task ResolveCoreAsync(string host, Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve, Entry entry)
    {
        try
        {
            var addresses = await resolve(host, CancellationToken.None).ConfigureAwait(false);

            // Пустой ответ — это не результат: закэшировав его, мы получили бы узел, который
            // «не разрешается» до конца времени жизни записи.
            if (addresses is null || addresses.Length is 0) throw new SocketException((int)SocketError.HostNotFound);

            // Отметка времени и сам результат публикуются ДО завершения задачи: иначе читатель
            // успел бы увидеть готовую запись с нулевым временем и счесть свежую запись протухшей.
            Volatile.Write(ref entry.Resolved, Stopwatch.GetTimestamp());
            Volatile.Write(ref entry.Addresses, addresses);
            entry.Completion.TrySetResult(addresses);

            Evict();
        }
        catch (Exception exception)
        {
            // Отказ не кэшируется: запись снимается раньше, чем о нём узнают ждущие.
            entries.TryRemove(new KeyValuePair<string, Entry>(host, entry));
            entry.Completion.TrySetException(exception);

            // Отказ обязан считаться замеченным даже тогда, когда все ждущие успели отмениться:
            // иначе он всплывёт позже как необработанное исключение задачи, в чужом месте и без
            // всякой связи с этим хостом.
            _ = entry.Completion.Task.Exception;
        }
    }

    /// <summary>
    /// Приводит число записей к пределу.
    /// </summary>
    /// <remarks>
    /// Вытеснение идёт в один поток: одновременная чистка несколькими потоками выбросила бы
    /// кратно больше нужного, а пропуск чистки безвреден — следующая запись её повторит.
    /// </remarks>
    private void Evict()
    {
        if (entries.Count <= capacity) return;
        if (Interlocked.Exchange(ref evicting, 1) is 1) return;

        try
        {
            EvictCore();
        }
        finally
        {
            Volatile.Write(ref evicting, 0);
        }
    }

    private void EvictCore()
    {
        // Сначала протухшие: они не нужны никому и освобождают место бесплатно.
        foreach (var pair in entries)
        {
            if (Volatile.Read(ref pair.Value.Addresses) is null) continue;
            if (Stopwatch.GetElapsedTime(Volatile.Read(ref pair.Value.Resolved)) < timeToLive) continue;

            entries.TryRemove(pair);
        }

        var excess = entries.Count - capacity;
        if (excess <= 0) return;

        // Если протухших не хватило, уходят самые старые: у них ближе всего срок, и потеря такой
        // записи стоит одного запроса к резолверу.
        var snapshot = entries.ToArray();
        var order = new long[snapshot.Length];

        for (var i = 0; i < snapshot.Length; i++) order[i] = Volatile.Read(ref snapshot[i].Value.Resolved);

        Array.Sort(order, snapshot);

        for (var i = 0; i < snapshot.Length && excess > 0; i++)
        {
            // Идущие разрешения не трогаем: их уже ждут, а выбросив запись, мы получили бы второй
            // запрос к резолверу вместо совмещения.
            if (Volatile.Read(ref snapshot[i].Value.Addresses) is null) continue;

            if (entries.TryRemove(snapshot[i])) excess--;
        }
    }

    private static IPAddress[] Copy(IPAddress[] addresses) => addresses.Length is 0 ? [] : (IPAddress[])addresses.Clone();

    private static DnsCache CreateShared()
    {
        var lifetime = DefaultTimeToLive;

        if (double.TryParse(Environment.GetEnvironmentVariable(TimeToLiveVariableName), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            lifetime = TimeSpan.FromSeconds(seconds);

        var limit = DefaultCapacity;

        if (int.TryParse(Environment.GetEnvironmentVariable(CapacityVariableName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            limit = parsed;

        return new DnsCache(lifetime, limit);
    }

    /// <summary>Запись кэша: задача разрешения и время появления результата.</summary>
    private sealed class Entry
    {
        /// <summary>Ожидание разрешения; одна задача на всех, кто спросил про этот хост.</summary>
        public readonly TaskCompletionSource<IPAddress[]> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Отметка <see cref="Stopwatch"/> момента, когда результат стал известен.</summary>
        public long Resolved;

        /// <summary>Разрешённые адреса; <see langword="null"/>, пока разрешение не завершилось успехом.</summary>
        public IPAddress[]? Addresses;
    }
}
