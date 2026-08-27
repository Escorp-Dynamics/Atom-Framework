namespace Atom.Net.Quic;

/// <summary>
/// Состояние одного потока QUIC на стороне клиента.
/// </summary>
/// <remarks>
/// Как и в HTTP/2, потоки живут независимо, а кадры для них раскладывает один цикл чтения.
/// Принятые данные складываются в очередь с одним читателем и одним писателем, окна меняются
/// атомарно; общей блокировки на соединение нет — она стала бы точкой сериализации всех потоков.
///
/// Отличие от HTTP/2 существенное: данные потока могут прийти НЕ ПО ПОРЯДКУ, потому что пакеты
/// UDP теряются и обгоняют друг друга. Поэтому куски со смещением больше ожидаемого
/// откладываются, а в канал попадают только тогда, когда становятся продолжением уже принятого.
/// </remarks>
/// <param name="id">Идентификатор потока.</param>
/// <param name="initialSendWindow">Начальный предел данных, разрешённый партнёром.</param>
public sealed class QuicStream(ulong id, ulong initialSendWindow)
{
    /// <summary>Принятые данные в правильном порядке.</summary>
    public QuicStreamReader Body { get; } = new();

    /// <summary>
    /// Куски, пришедшие раньше своего места в потоке.
    /// </summary>
    /// <remarks>
    /// Заводится по необходимости: обгон пакетов — случай не частый, а словарь на каждый поток
    /// оплачивался бы каждым запросом.
    /// </remarks>
    private SortedDictionary<ulong, byte[]>? pending;

    private long sendWindow = (long)initialSendWindow;
    private ulong receiveOffset;
    private long consumed;
    private bool finReceived;
    private int finDelivered;

    /// <summary>Идентификатор потока.</summary>
    public ulong Id { get; } = id;

    /// <summary>Сколько байт ещё разрешено отправить.</summary>
    public long SendWindow => Volatile.Read(ref sendWindow);

    /// <summary>Смещение, с которого пойдут следующие отправляемые данные.</summary>
    public ulong SendOffset { get; private set; }

    /// <summary>Сколько байт принято и отдано вызывающей стороне.</summary>
    public ulong Consumed => (ulong)Volatile.Read(ref consumed);

    /// <summary>Получен ли признак конца потока.</summary>
    public bool IsFinished => Volatile.Read(ref finDelivered) is not 0;

    /// <summary>
    /// Учитывает отправленные данные.
    /// </summary>
    /// <param name="length">Число байт.</param>
    public void OnDataSent(int length)
    {
        SendOffset += (ulong)length;
        Interlocked.Add(ref sendWindow, -length);
    }

    /// <summary>
    /// Увеличивает окно отправки.
    /// </summary>
    /// <param name="maximum">Новый предел данных потока.</param>
    /// <remarks>
    /// Партнёр присылает АБСОЛЮТНЫЙ предел, а не приращение: окно — это разница между ним и уже
    /// отправленным. Сложение приращений здесь дало бы завышенное окно и нарушение протокола.
    /// </remarks>
    public void UpdateSendWindow(ulong maximum)
    {
        var updated = (long)maximum - (long)SendOffset;
        if (updated > Volatile.Read(ref sendWindow)) Volatile.Write(ref sendWindow, updated);
    }

    /// <summary>
    /// Принимает данные потока, восстанавливая порядок.
    /// </summary>
    /// <param name="offset">Смещение куска в потоке.</param>
    /// <param name="data">Данные.</param>
    /// <param name="fin">Признак конца потока.</param>
    /// <returns>Сколько байт стало доступно вызывающей стороне.</returns>
    public int OnDataReceived(ulong offset, ReadOnlySpan<byte> data, bool fin)
    {
        if (fin) finReceived = true;

        if (offset + (ulong)data.Length <= receiveOffset)
        {
            // Полный повтор уже принятого куска: при потере подтверждения сервер шлёт данные
            // заново, и это штатно.
            CompleteIfFinished();
            return 0;
        }

        if (offset > receiveOffset)
        {
            (pending ??= [])[offset] = data.ToArray();
            return 0;
        }

        // Кусок может частично перекрывать уже принятое — берём только новый хвост.
        var skip = (int)(receiveOffset - offset);
        var fresh = data[skip..];

        Body.Write(fresh.ToArray());
        receiveOffset += (ulong)fresh.Length;

        var delivered = fresh.Length + DrainPending();

        Interlocked.Add(ref consumed, delivered);
        CompleteIfFinished();

        return delivered;
    }

    /// <summary>
    /// Закрывает поток ошибкой.
    /// </summary>
    /// <param name="error">Причина.</param>
    public void Fail(Exception error) => Body.Complete(error);

    /// <summary>
    /// Закрывает поток нормально.
    /// </summary>
    public void Complete() => Body.Complete();

    /// <summary>
    /// Отдаёт отложенные куски, ставшие продолжением принятого.
    /// </summary>
    private int DrainPending()
    {
        var delivered = 0;
        if (pending is null) return 0;

        while (pending.Count > 0)
        {
            // Словарь упорядочен по смещению, поэтому наименьший ключ — первый элемент.
            var entry = System.Linq.Enumerable.First(pending);
            var first = entry.Key;

            if (first > receiveOffset) break;

            var chunk = entry.Value;
            pending.Remove(first);

            var skip = (int)(receiveOffset - first);
            if (skip >= chunk.Length) continue;

            var fresh = chunk.AsMemory(skip);
            Body.Write(fresh);

            receiveOffset += (ulong)fresh.Length;
            delivered += fresh.Length;
        }

        return delivered;
    }

    private void CompleteIfFinished()
    {
        if (!finReceived || pending is { Count: > 0 }) return;

        Volatile.Write(ref finDelivered, 1);
        Body.Complete();
    }
}
