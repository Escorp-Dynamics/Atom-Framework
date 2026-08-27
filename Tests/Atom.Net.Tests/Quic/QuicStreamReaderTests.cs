using System.Linq;
using Atom.Net.Quic;

namespace Atom.Net.Tests.Quic;

/// <summary>
/// Проверяет очередь принятых данных потока QUIC.
/// </summary>
/// <remarks>
/// Очередь заменила собой канал общего назначения ради полутора килобайт на каждый запрос, и
/// вместе с ценой ушли гарантии, которые канал давал даром. Здесь закреплены именно они: ничего
/// не теряется на закрытии, ошибка не съедает уже принятое, отмена ожидания не уносит с собой
/// данные, а писатель и читатель, работающие в разных потоках исполнения, сходятся на полном
/// наборе кусков в исходном порядке.
///
/// Проверки не декоративные: это ровно тот код, через который проходит КАЖДЫЙ байт ответа
/// HTTP/3, и ошибка здесь выглядела бы как редкое зависание запроса без единого сообщения.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class QuicStreamReaderTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public async Task DataWrittenBeforeWaitIsReturnedWithoutWaiting()
    {
        var reader = new QuicStreamReader();
        reader.Write(new byte[] { 1, 2, 3 });

        var ready = await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(ready, Is.True);
            Assert.That(reader.TryRead(out var chunk), Is.True);
            Assert.That(chunk.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }).AsCollection);
        });
    }

    [Test]
    public async Task WaiterIsWokenByLaterWrite()
    {
        var reader = new QuicStreamReader();
        var waiting = reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken);

        reader.Write(new byte[] { 7 });

        Assert.That(await waiting, Is.True);
    }

    [Test]
    public async Task CompletionWithoutDataEndsTheStream()
    {
        var reader = new QuicStreamReader();
        reader.Complete();

        Assert.That(await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [Test]
    public async Task WaiterIsWokenByCompletion()
    {
        var reader = new QuicStreamReader();
        var waiting = reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken);

        reader.Complete();

        Assert.That(await waiting, Is.False);
    }

    [Test]
    public async Task DataAcceptedBeforeCompletionSurvivesIt()
    {
        // Конец потока означает, что данных больше НЕ БУДЕТ, а не что принятое можно выбросить.
        var reader = new QuicStreamReader();
        reader.Write(new byte[] { 1 });
        reader.Write(new byte[] { 2 });
        reader.Complete();

        var collected = new List<byte>();
        while (await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken))
        {
            while (reader.TryRead(out var chunk)) collected.AddRange(chunk.ToArray());
        }

        Assert.That(collected, Is.EqualTo(new byte[] { 1, 2 }).AsCollection);
    }

    [Test]
    public async Task FailureIsRaisedOnlyAfterAcceptedDataIsDrained()
    {
        // Ошибка относится к ПРОДОЛЖЕНИЮ потока: то, что уже принято, вызывающая сторона должна
        // получить целиком, иначе оборванное соединение теряло бы разобранные заголовки ответа.
        var reader = new QuicStreamReader();
        reader.Write(new byte[] { 9 });
        reader.Complete(new InvalidOperationException("поток оборван"));

        Assert.That(await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(reader.TryRead(out var chunk), Is.True);
        Assert.That(chunk.ToArray(), Is.EqualTo(new byte[] { 9 }).AsCollection);

        Assert.That(
            async () => await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task CancelledWaitDoesNotLoseData()
    {
        // Отменённое ожидание не должно уносить с собой кусок, пришедший одновременно с отменой:
        // соединение уже уменьшило на него окно приёма, и повторно сервер его не отправит.
        var reader = new QuicStreamReader();
        using var cancellation = new CancellationTokenSource();

        var waiting = reader.WaitToReadAsync(cancellation.Token);
        await cancellation.CancelAsync();

        reader.Write(new byte[] { 5 });

        Assert.That(async () => await waiting, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(reader.TryRead(out var chunk), Is.True);
        Assert.That(chunk.ToArray(), Is.EqualTo(new byte[] { 5 }).AsCollection);
    }

    [Test]
    public async Task ConcurrentWriterAndReaderAgreeOnEveryChunk()
    {
        // Писатель здесь — цикл чтения соединения, читатель — выполняющий запрос; это разные
        // потоки исполнения, и порядок кусков определяет порядок байт в теле ответа.
        const int ChunkCount = 5000;

        var reader = new QuicStreamReader();
        var collected = new List<byte>(ChunkCount);

        var producing = Task.Run(() =>
        {
            for (var index = 0; index < ChunkCount; index++) reader.Write(new byte[] { (byte)(index % 251) });

            reader.Complete();
        });

        while (await reader.WaitToReadAsync(TestContext.CurrentContext.CancellationToken))
        {
            while (reader.TryRead(out var chunk)) collected.AddRange(chunk.ToArray());
        }

        await producing;

        Assert.Multiple(() =>
        {
            Assert.That(collected, Has.Count.EqualTo(ChunkCount));
            Assert.That(collected.Where((value, index) => value != (byte)(index % 251)), Is.Empty, "порядок кусков нарушен");
        });
    }

    [Test]
    public void WritesAfterCompletionAreIgnored()
    {
        // Поток закрыт: пришедшее следом — повтор потерянного пакета, и принимать его нельзя,
        // иначе тело ответа получит лишний хвост.
        var reader = new QuicStreamReader();
        reader.Complete();
        reader.Write(new byte[] { 1 });

        Assert.That(reader.TryRead(out _), Is.False);
    }
}
