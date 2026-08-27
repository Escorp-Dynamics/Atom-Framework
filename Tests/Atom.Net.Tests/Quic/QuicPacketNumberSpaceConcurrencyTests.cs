using Atom.Net.Quic;

namespace Atom.Net.Tests.Quic;

/// <summary>
/// Проверяет, что пространство номеров пакетов выдерживает одновременную работу трёх сторон.
/// </summary>
/// <remarks>
/// К одному пространству обращаются независимые потоки исполнения: цикл чтения датаграмм
/// отмечает принятые пакеты и разбирает подтверждения, отправка выделяет номера и учитывает
/// отправленное, зондирующий цикл ищет самый старый неподтверждённый пакет. Ни одного из них не
/// видно в обычном прогоне — состязание проявляется только на скорости.
///
/// Именно так оно и нашлось: после ускорения HTTP/3 обмен разогнался, и запросы начали падать с
/// «коллекция изменена» — запись кадра ACK перечисляла живой список принятых диапазонов, пока
/// цикл чтения вставлял в него очередной номер. Отказывало от двух третей до девяти десятых
/// запросов на прогон, и не отказывало вовсе, когда сервер отвечал медленнее.
///
/// Проверка воспроизводит ту же расстановку без сети и падает без синхронизации.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class QuicPacketNumberSpaceConcurrencyTests
{
    private const int TestTimeoutMs = 60000;

    /// <summary>Сколько пакетов проходит через пространство за проверку.</summary>
    private const int PacketCount = 20000;

    [Test]
    public async Task ConcurrentReceiveSendAndProbeDoNotCollide()
    {
        var space = new QuicPacketNumberSpace();
        using var finish = new CancellationTokenSource();

        // Цикл чтения: отмечает принятые пакеты, как это делает разбор датаграмм.
        var receiving = Task.Run(() =>
        {
            for (var number = 0UL; number < PacketCount; number++) space.OnPacketReceived(number, ackEliciting: true);
        });

        // Отправка: выделяет номера, учитывает отправленное и пишет кадры подтверждения.
        var sending = Task.Run(() =>
        {
            Span<(ulong Start, ulong End)> ranges = stackalloc (ulong Start, ulong End)[32];

            while (!finish.Token.IsCancellationRequested)
            {
                var number = space.AllocatePacketNumber();
                space.TrackSent(number, new QuicSentPacket(-1, default, [], IsAckEliciting: true, SentTimestamp: (long)number));

                _ = space.CopyReceivedRanges(ranges);
                _ = space.ReceivedRangeCount;
            }
        });

        // Зондирующий цикл: перебирает неподтверждённые пакеты в поисках самого старого.
        var probing = Task.Run(() =>
        {
            while (!finish.Token.IsCancellationRequested)
            {
                _ = space.TryGetOldestUnacknowledged(out _);
                _ = space.TakeLostPackets(largestAcknowledged: 64);
            }
        });

        await receiving;
        await finish.CancelAsync();

        // Ошибка состязания вылетает именно здесь: без синхронизации это «коллекция изменена».
        Assert.That(async () => await Task.WhenAll(sending, probing), Throws.Nothing);
    }

    [Test]
    public async Task AcknowledgementsAndSendsDoNotCollide()
    {
        // Второе сочетание из того же набора: подтверждения снимают пакеты с учёта, пока отправка
        // ставит новые. Обе стороны правят один и тот же словарь.
        var space = new QuicPacketNumberSpace();
        using var finish = new CancellationTokenSource();

        var sending = Task.Run(() =>
        {
            while (!finish.Token.IsCancellationRequested)
            {
                var number = space.AllocatePacketNumber();
                space.TrackSent(number, new QuicSentPacket(-1, default, [], IsAckEliciting: true, SentTimestamp: (long)number));
            }
        });

        var acknowledging = Task.Run(() =>
        {
            var ranges = new List<(ulong Start, ulong End)>(1);

            for (var round = 0UL; round < PacketCount / 100; round++)
            {
                ranges.Clear();
                ranges.Add((round * 10, (round * 10) + 9));

                _ = space.OnAckReceived(ranges);
                _ = space.GetSentPacketNumbers();
            }
        });

        await acknowledging;
        await finish.CancelAsync();

        Assert.That(async () => await sending, Throws.Nothing);
    }
}
