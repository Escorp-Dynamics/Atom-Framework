namespace Atom.Net.Quic;

/// <summary>
/// Уровень шифрования QUIC; он же — пространство номеров пакетов.
/// </summary>
/// <remarks>
/// Пространств три, и нумерация в каждом СВОЯ, начиная с нуля. Это не формальность: пакеты разных
/// уровней защищены разными ключами, и общая нумерация означала бы, что потеря пакета одного
/// уровня влияет на восстановление другого.
/// </remarks>
public enum QuicEncryptionLevel
{
    /// <summary>Начальный уровень: ключи выведены из идентификатора соединения.</summary>
    Initial = 0,

    /// <summary>Уровень рукопожатия: ключи выведены из секретов TLS.</summary>
    Handshake = 1,

    /// <summary>Прикладной уровень, он же 1-RTT.</summary>
    Application = 2,
}

/// <summary>
/// Состояние одного пространства номеров пакетов.
/// </summary>
/// <remarks>
/// Держит три вещи: собственную нумерацию отправляемых пакетов, диапазоны принятых для
/// подтверждения и отправленные, но ещё не подтверждённые пакеты — для повторной передачи.
///
/// Диапазоны хранятся сжатыми и в порядке убывания: именно в таком виде их требует кадр ACK, и
/// пересортировывать их на каждой отправке было бы расточительно.
///
/// Класс СИНХРОНИЗИРОВАН, и это обязательное свойство, а не осторожность. К одному пространству
/// обращаются три независимых потока исполнения: цикл чтения датаграмм отмечает принятые пакеты и
/// разбирает подтверждения, отправка выделяет номера и учитывает отправленное, зондирующий цикл
/// ищет самый старый неподтверждённый пакет. Без общей блокировки перечисление принятых
/// диапазонов при записи кадра ACK сталкивается с их вставкой из цикла чтения, и соединение
/// падает с «коллекция изменена» — тем чаще, чем быстрее идёт обмен.
/// </remarks>
public sealed class QuicPacketNumberSpace
{
    private readonly List<(ulong Start, ulong End)> received = [];
    private readonly Dictionary<ulong, QuicSentPacket> sent = [];
    private readonly Lock gate = new();

    // Поля, а не автосвойства: каждое читается и пишется из разных потоков исполнения и потому
    // доступно только под блокировкой. Автосвойство такой доступ выразить не может.
#pragma warning disable IDE0032
    private ulong nextPacketNumber;
    private long largestAcknowledged = -1;
    private long largestReceived = -1;
    private bool hasPendingAck;
    private bool hasAckElicitingReceived;
    private ulong cryptoOffset;
#pragma warning restore IDE0032

    /// <summary>Номер, который получит следующий отправленный пакет.</summary>
    public ulong NextPacketNumber
    {
        get { lock (gate) return nextPacketNumber; }
    }

    /// <summary>Наибольший подтверждённый нами номер или -1, если подтверждений не было.</summary>
    /// <summary>
    /// Наибольший номер пакета, ПРИНЯТОГО от партнёра.
    /// </summary>
    /// <remarks>
    /// ★ Это не то же самое, что <see cref="LargestAcknowledged"/>, и путать их нельзя.
    /// Восстановление усечённого номера (RFC 9000, приложение A.3) опирается именно на
    /// наибольший ПРИНЯТЫЙ: в пакете едут один-два младших байта, а старшие берутся от него.
    ///
    /// Прежде туда подавался наибольший ПОДТВЕРЖДЁННЫЙ — то есть номер НАШЕГО пакета, который
    /// партнёр отметил в своём ACK. Величины расходятся сразу: наши пакеты с одними лишь
    /// подтверждениями партнёр не подтверждает вовсе, а его собственные номера растут на каждый
    /// пакет. Стоит расхождению перевалить за половину окна кодирования — для однобайтового
    /// номера это всего 128 пакетов, — как номер восстанавливается неверно, вектор nonce
    /// строится не тот, и проверка целостности перестаёт сходиться.
    ///
    /// Отказ при этом МОЛЧАЛИВЫЙ и невосстановимый: каждый следующий пакет отбрасывается как
    /// нерасшифрованный, запрос висит до внешнего времени ожидания, в журнале — ничего, кроме
    /// однообразной строки диагностики.
    /// </remarks>
    public long LargestReceived
    {
        get { lock (gate) return largestReceived; }
    }

    /// <summary>
    /// Наибольший номер НАШЕГО пакета, подтверждённый партнёром.
    /// </summary>
    public long LargestAcknowledged
    {
        get { lock (gate) return largestAcknowledged; }
    }

    /// <summary>Есть ли непереданные подтверждения.</summary>
    public bool HasPendingAck
    {
        get { lock (gate) return hasPendingAck; }
    }

    /// <summary>Принимался ли пакет, требующий подтверждения.</summary>
    public bool HasAckElicitingReceived
    {
        get { lock (gate) return hasAckElicitingReceived; }
    }

    /// <summary>Смещение уже отправленных данных рукопожатия.</summary>
    public ulong CryptoOffset
    {
        get { lock (gate) return cryptoOffset; }
        set { lock (gate) cryptoOffset = value; }
    }

    /// <summary>Сколько диапазонов принятых номеров накоплено.</summary>
    public int ReceivedRangeCount
    {
        get { lock (gate) return received.Count; }
    }

    /// <summary>
    /// Копирует диапазоны принятых номеров.
    /// </summary>
    /// <param name="destination">Куда копировать; порядок — от новых к старым.</param>
    /// <returns>Сколько диапазонов скопировано.</returns>
    /// <remarks>
    /// Именно копия, а не сам список: отдавать наружу живую коллекцию, которую цикл чтения
    /// правит на каждом принятом пакете, значит получить обрыв соединения на первом же
    /// столкновении с записью кадра ACK.
    ///
    /// Когда диапазонов больше, чем места, берутся САМЫЕ НОВЫЕ — список упорядочен по убыванию.
    /// Это не потеря: кадр ACK и так ограничен размером пакета, а подтверждать в первую очередь
    /// нужно недавнее.
    /// </remarks>
    public int CopyReceivedRanges(Span<(ulong Start, ulong End)> destination)
    {
        lock (gate)
        {
            var count = Math.Min(destination.Length, received.Count);

            for (var index = 0; index < count; index++) destination[index] = received[index];

            return count;
        }
    }

    /// <summary>
    /// Номера отправленных и ещё не подтверждённых пакетов.
    /// </summary>
    /// <returns>Снимок номеров.</returns>
    /// <remarks>
    /// Снимок, а не живое представление словаря: учёт правят и отправка, и разбор подтверждений,
    /// и цикл зондирования. Выделение здесь допустимо — метод существует ради проверок и
    /// диагностики, а не для горячего пути.
    /// </remarks>
    public ulong[] GetSentPacketNumbers()
    {
        lock (gate) return [.. sent.Keys];
    }

    /// <summary>
    /// Выдаёт номер для следующего пакета.
    /// </summary>
    /// <returns>Номер пакета.</returns>
    public ulong AllocatePacketNumber()
    {
        lock (gate) return nextPacketNumber++;
    }

    /// <summary>
    /// Запоминает отправленный пакет для возможной повторной передачи.
    /// </summary>
    /// <param name="packetNumber">Номер пакета.</param>
    /// <param name="packet">Что в нём было.</param>
    public void TrackSent(ulong packetNumber, QuicSentPacket packet)
    {
        lock (gate) sent[packetNumber] = packet;
    }

    /// <summary>
    /// Отмечает пакет принятым.
    /// </summary>
    /// <param name="packetNumber">Номер принятого пакета.</param>
    /// <param name="ackEliciting">Требует ли пакет подтверждения.</param>
    public void OnPacketReceived(ulong packetNumber, bool ackEliciting)
    {
        lock (gate)
        {
            InsertReceived(packetNumber);

            if ((long)packetNumber > largestReceived) largestReceived = (long)packetNumber;

            hasPendingAck = true;
            if (ackEliciting) hasAckElicitingReceived = true;
        }
    }

    /// <summary>
    /// Помечает подтверждения отправленными.
    /// </summary>
    public void OnAckSent()
    {
        lock (gate)
        {
            hasPendingAck = false;
            hasAckElicitingReceived = false;
        }
    }

    /// <summary>
    /// Обрабатывает подтверждение, снимая пакеты с учёта.
    /// </summary>
    /// <param name="ranges">Подтверждённые диапазоны.</param>
    /// <returns>Пакеты, которые сервер подтвердил.</returns>
    public IReadOnlyList<QuicSentPacket> OnAckReceived(IReadOnlyList<(ulong Start, ulong End)> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var acknowledged = new List<QuicSentPacket>();

        lock (gate)
        {
            foreach (var (start, end) in ranges)
            {
                for (var number = start; number <= end; number++)
                {
                    if (!sent.Remove(number, out var packet)) continue;

                    acknowledged.Add(packet);
                    if ((long)number > largestAcknowledged) largestAcknowledged = (long)number;
                }

                if (end < start) break;
            }
        }

        return acknowledged;
    }

    /// <summary>
    /// Забирает пакеты, признанные потерянными по порогу переупорядочивания.
    /// </summary>
    /// <param name="largestAcknowledged">Наибольший подтверждённый номер.</param>
    /// <returns>Потерянные пакеты; из учёта они снимаются.</returns>
    /// <remarks>
    /// Пакет считается потерянным, когда подтверждён пакет с номером на три больше: сеть
    /// переупорядочивает пакеты, но глубже трёх — редко. Ждать таймера для таких случаев значит
    /// добавлять к каждой потере целый оборот.
    /// </remarks>
    public IReadOnlyList<QuicSentPacket> TakeLostPackets(ulong largestAcknowledged)
    {
        if (largestAcknowledged < PacketReorderingThreshold) return [];

        var boundary = largestAcknowledged - PacketReorderingThreshold;

        // Номера собираем вместе с пакетами: искать их потом по содержимому нельзя — два пакета
        // легко совпадут и по метке времени, и по смещению, и снялся бы с учёта не тот.
        var numbers = new List<ulong>();
        var lost = new List<QuicSentPacket>();

        lock (gate)
        {
            foreach (var pair in sent)
            {
                if (pair.Key > boundary) continue;

                numbers.Add(pair.Key);
                lost.Add(pair.Value);
            }

            foreach (var number in numbers) sent.Remove(number);
        }

        return lost;
    }

    /// <summary>Порог переупорядочивания в пакетах.</summary>
    private const int PacketReorderingThreshold = 3;

    /// <summary>
    /// Возвращает самый старый неподтверждённый пакет, требующий подтверждения.
    /// </summary>
    /// <param name="packet">Найденный пакет.</param>
    /// <returns><see langword="true"/>, если такой пакет есть.</returns>
    public bool TryGetOldestUnacknowledged(out QuicSentPacket packet)
    {
        packet = default;

        var found = false;

        lock (gate)
        {
            foreach (var candidate in sent.Values)
            {
                if (!candidate.IsAckEliciting) continue;
                if (found && candidate.SentTimestamp >= packet.SentTimestamp) continue;

                packet = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Снимает с учёта все отправленные пакеты.</summary>
    public void ClearSent()
    {
        lock (gate) sent.Clear();
    }

    /// <summary>
    /// Добавляет номер в сжатый набор принятых.
    /// </summary>
    /// <remarks>
    /// Набор держится отсортированным по убыванию и склеивается при соприкосновении диапазонов:
    /// иначе кадр ACK разрастался бы линейно по числу пакетов, а его размер ограничен пакетом.
    ///
    /// Вызывается ТОЛЬКО под блокировкой.
    /// </remarks>
    private void InsertReceived(ulong packetNumber)
    {
        for (var index = 0; index < received.Count; index++)
        {
            var (start, end) = received[index];

            if (packetNumber >= start && packetNumber <= end) return;

            if (packetNumber == end + 1)
            {
                received[index] = (start, packetNumber);
                MergeWithPrevious(index);
                return;
            }

            if (packetNumber + 1 == start)
            {
                received[index] = (packetNumber, end);
                MergeWithNext(index);
                return;
            }

            if (packetNumber > end)
            {
                received.Insert(index, (packetNumber, packetNumber));
                return;
            }
        }

        received.Add((packetNumber, packetNumber));
    }

    private void MergeWithPrevious(int index)
    {
        if (index is 0) return;

        var (previousStart, previousEnd) = received[index - 1];
        var (start, end) = received[index];

        if (previousStart != end + 1) return;

        received[index - 1] = (start, previousEnd);
        received.RemoveAt(index);
    }

    private void MergeWithNext(int index)
    {
        if (index + 1 >= received.Count) return;

        var (start, end) = received[index];
        var (nextStart, nextEnd) = received[index + 1];

        if (start != nextEnd + 1) return;

        received[index] = (nextStart, end);
        received.RemoveAt(index + 1);
    }
}

/// <summary>
/// Содержимое отправленного пакета, нужное для повторной передачи.
/// </summary>
/// <param name="CryptoOffset">Смещение данных рукопожатия или -1, если их не было.</param>
/// <param name="CryptoData">Данные рукопожатия, уехавшие в этом пакете.</param>
/// <param name="StreamFrames">Кадры потоков, уехавшие в этом пакете.</param>
/// <param name="IsAckEliciting">Требует ли пакет подтверждения.</param>
/// <param name="SentTimestamp">Метка времени отправки.</param>
/// <remarks>
/// Хранится не сам пакет, а его СОДЕРЖИМОЕ. Повторная передача в QUIC — не пересылка тех же байт:
/// потерянные данные заново упаковываются в НОВЫЙ пакет с новым номером, потому что номер
/// участвует в шифровании и повторяться не может. Отправить старый пакет ещё раз означало бы
/// использовать nonce дважды — то есть разрушить защиту.
/// </remarks>
public readonly record struct QuicSentPacket(
    long CryptoOffset,
    ReadOnlyMemory<byte> CryptoData,
    IReadOnlyList<QuicSentStreamFrame> StreamFrames,
    bool IsAckEliciting,
    long SentTimestamp);

/// <summary>
/// Кадр потока, уехавший в пакете.
/// </summary>
/// <param name="StreamId">Идентификатор потока.</param>
/// <param name="Offset">Смещение данных в потоке.</param>
/// <param name="Data">Данные.</param>
/// <param name="IsFin">Закрывал ли кадр поток.</param>
public readonly record struct QuicSentStreamFrame(ulong StreamId, ulong Offset, ReadOnlyMemory<byte> Data, bool IsFin);
