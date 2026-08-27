using System.Diagnostics;
using Atom.Net.Quic;

namespace Atom.Net.Tests.Quic;

/// <summary>
/// Проверяет обнаружение потерь и срок повторной передачи.
/// </summary>
/// <remarks>
/// Без этого слоя один потерянный пакет подвешивает запрос до общего таймаута: сервер ждёт
/// данных, которых не получил, а клиент — ответа, которого не будет. Проверки здесь
/// детерминированные: воспроизводить потери в сети ради теста бессмысленно, а логика решения
/// «потерян или переупорядочен» от сети не зависит.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class QuicLossRecoveryTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public void FirstSampleReplacesInitialEstimate()
    {
        // До первого измерения оценка — предположение. Оставить его после реального замера значит
        // считать срок ожидания по вымышленному каналу.
        var recovery = new QuicLossRecovery();
        var measured = TimeSpan.FromMilliseconds(40);

        recovery.OnRttSample(measured, TimeSpan.Zero);

        Assert.That(recovery.SmoothedRtt, Is.EqualTo(measured));
    }

    [Test]
    public void SmoothingDampensSingleOutlier()
    {
        var recovery = new QuicLossRecovery();

        recovery.OnRttSample(TimeSpan.FromMilliseconds(40), TimeSpan.Zero);
        recovery.OnRttSample(TimeSpan.FromMilliseconds(400), TimeSpan.Zero);

        // Один выброс не должен сдвигать оценку целиком: вес свежего замера — одна восьмая.
        Assert.Multiple(() =>
        {
            Assert.That(recovery.SmoothedRtt, Is.GreaterThan(TimeSpan.FromMilliseconds(40)));
            Assert.That(recovery.SmoothedRtt, Is.LessThan(TimeSpan.FromMilliseconds(120)));
        });
    }

    [Test]
    public void ProbeTimeoutDoublesWithEveryFailedAttempt()
    {
        var recovery = new QuicLossRecovery();
        recovery.OnRttSample(TimeSpan.FromMilliseconds(50), TimeSpan.Zero);

        var first = recovery.GetProbeTimeout(TimeSpan.Zero);

        recovery.OnProbeTimeout();
        var second = recovery.GetProbeTimeout(TimeSpan.Zero);

        recovery.OnProbeTimeout();
        var third = recovery.GetProbeTimeout(TimeSpan.Zero);

        // Удвоение обязательно: если ответа нет, дело не в единичной потере, и частые повторы
        // только усугубляют перегрузку.
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first * 2));
            Assert.That(third, Is.EqualTo(first * 4));
        });
    }

    [Test]
    public void AcknowledgementResetsBackoff()
    {
        var recovery = new QuicLossRecovery();
        recovery.OnRttSample(TimeSpan.FromMilliseconds(50), TimeSpan.Zero);

        var baseline = recovery.GetProbeTimeout(TimeSpan.Zero);

        recovery.OnProbeTimeout();
        recovery.OnProbeTimeout();
        recovery.OnAckReceived();

        Assert.That(recovery.GetProbeTimeout(TimeSpan.Zero), Is.EqualTo(baseline));
    }

    [Test]
    public void PacketsFallingBehindThresholdAreDeclaredLost()
    {
        // Сеть переупорядочивает пакеты, но глубже трёх — редко. Пакет, отставший сильнее,
        // считается потерянным, не дожидаясь таймера: ожидание стоило бы целого оборота.
        var space = new QuicPacketNumberSpace();

        for (var number = 0UL; number <= 5; number++)
            space.TrackSent(number, CreatePacket(number));

        var lost = space.TakeLostPackets(largestAcknowledged: 5);

        Assert.Multiple(() =>
        {
            Assert.That(lost, Has.Count.EqualTo(3), "потерянными обязаны стать пакеты 0, 1 и 2");
            Assert.That(space.GetSentPacketNumbers(), Is.EquivalentTo(new ulong[] { 3, 4, 5 }));
        });
    }

    [Test]
    public void RecentPacketsSurviveLossDetection()
    {
        var space = new QuicPacketNumberSpace();

        for (var number = 0UL; number <= 2; number++)
            space.TrackSent(number, CreatePacket(number));

        // Порог ещё не пройден: переупорядочивание на два пакета — обычное дело.
        Assert.That(space.TakeLostPackets(largestAcknowledged: 2), Is.Empty);
    }

    [Test]
    public void OldestUnacknowledgedIsFoundForProbe()
    {
        var space = new QuicPacketNumberSpace();

        space.TrackSent(0, CreatePacket(0) with { SentTimestamp = 500 });
        space.TrackSent(1, CreatePacket(1) with { SentTimestamp = 100 });
        space.TrackSent(2, CreatePacket(2) with { SentTimestamp = 900 });

        var found = space.TryGetOldestUnacknowledged(out var oldest);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(oldest.SentTimestamp, Is.EqualTo(100));
        });
    }

    [Test]
    public void AcknowledgedPacketsLeaveTheLedger()
    {
        var space = new QuicPacketNumberSpace();

        for (var number = 0UL; number <= 3; number++)
            space.TrackSent(number, CreatePacket(number));

        var acknowledged = space.OnAckReceived([(1UL, 2UL)]);

        Assert.Multiple(() =>
        {
            Assert.That(acknowledged, Has.Count.EqualTo(2));
            Assert.That(space.GetSentPacketNumbers(), Is.EquivalentTo(new ulong[] { 0, 3 }));
            Assert.That(space.LargestAcknowledged, Is.EqualTo(2));
        });
    }

    private static QuicSentPacket CreatePacket(ulong number)
        => new(
            CryptoOffset: -1,
            CryptoData: ReadOnlyMemory<byte>.Empty,
            StreamFrames: [new QuicSentStreamFrame(StreamId: 0, Offset: number, Data: new byte[8], IsFin: false)],
            IsAckEliciting: true,
            SentTimestamp: Stopwatch.GetTimestamp());
}
