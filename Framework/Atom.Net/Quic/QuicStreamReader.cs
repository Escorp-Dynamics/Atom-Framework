namespace Atom.Net.Quic;

/// <summary>
/// Очередь принятых данных потока QUIC с одним читателем и одним писателем.
/// </summary>
/// <remarks>
/// Заменяет собой канал общего назначения, и по одной причине: канал на поток стоит около
/// полутора килобайт, а потоков создаётся по одному на каждый запрос. Ничего из того, за что
/// платится эта цена, здесь не нужно — ни нескольких читателей, ни нескольких писателей, ни
/// ограничения размера с обратным давлением: писатель ровно один (цикл чтения соединения),
/// читатель ровно один (выполняющий запрос), а объём принятого ограничен окном управления
/// потоком, которое считается отдельно.
///
/// Форма намеренно повторяет <c lang="text">ChannelReader</c>: пара «дождаться — забрать» вместо одного
/// метода чтения. Это не украшение — именно она позволяет читателю разбирать сразу всё
/// накопленное, не платя за ожидание на каждом куске.
///
/// Ожидание создаётся ТОЛЬКО когда очередь действительно пуста. На горячем пути, где данные уже
/// пришли, не создаётся ни одного объекта.
/// </remarks>
public sealed class QuicStreamReader
{
    private readonly Queue<ReadOnlyMemory<byte>> chunks = new();
    private readonly Lock gate = new();

    private TaskCompletionSource<bool>? waiter;
    private Exception? failure;
    private bool completed;

    /// <summary>
    /// Помещает принятый кусок в очередь.
    /// </summary>
    /// <param name="chunk">Данные.</param>
    public void Write(ReadOnlyMemory<byte> chunk)
    {
        TaskCompletionSource<bool>? pending;

        lock (gate)
        {
            if (completed) return;

            chunks.Enqueue(chunk);

            pending = waiter;
            waiter = null;
        }

        // Пробуждаем ВНЕ блокировки: продолжение читателя иначе исполнялось бы под ней и
        // задерживало цикл чтения соединения — то есть все остальные потоки.
        pending?.TrySetResult(true);
    }

    /// <summary>
    /// Закрывает поток.
    /// </summary>
    /// <param name="error">Причина закрытия или <see langword="null"/> при штатном конце.</param>
    /// <remarks>
    /// Уже принятые куски остаются доступными: закрытие означает, что данных БОЛЬШЕ не будет, а
    /// не что накопленное следует выбросить.
    /// </remarks>
    public void Complete(Exception? error = null)
    {
        TaskCompletionSource<bool>? pending;

        lock (gate)
        {
            if (completed) return;

            completed = true;
            failure = error;

            pending = waiter;
            waiter = null;
        }

        if (error is null) pending?.TrySetResult(false);
        else pending?.TrySetException(error);
    }

    /// <summary>
    /// Забирает очередной кусок, если он уже есть.
    /// </summary>
    /// <param name="chunk">Принятые данные.</param>
    /// <returns><see langword="true"/>, если кусок получен.</returns>
    public bool TryRead(out ReadOnlyMemory<byte> chunk)
    {
        lock (gate)
        {
            if (chunks.Count is 0)
            {
                chunk = default;
                return false;
            }

            chunk = chunks.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Дожидается появления данных.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="false"/>, если поток закрыт и данных больше не будет.</returns>
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> pending;

        lock (gate)
        {
            // Накопленное отдаём даже после закрытия с ошибкой: ошибка относится к продолжению
            // потока, а не к тому, что уже принято и подтверждено.
            if (chunks.Count > 0) return new ValueTask<bool>(result: true);
            if (failure is not null) return ValueTask.FromException<bool>(failure);
            if (completed) return new ValueTask<bool>(result: false);

            pending = waiter ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return AwaitAsync(pending, cancellationToken);
    }

    private static async ValueTask<bool> AwaitAsync(TaskCompletionSource<bool> pending, CancellationToken cancellationToken)
        => await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
}
