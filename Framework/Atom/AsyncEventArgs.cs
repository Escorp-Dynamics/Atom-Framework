namespace Atom;

/// <summary>
/// Представляет базовый набор аргументов асинхронного события.
/// </summary>
public class AsyncEventArgs : MutableEventArgs
{
    /// <summary>
    /// Токен отмены задачи.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>
    /// Прикреплённая фоновая задача, которую могут ожидать другие обработчики события.
    /// </summary>
    public Task Task { get; set; } = Task.CompletedTask;

    /// <summary>
    /// Указывает, требуется ли ожидание выполнения фоновой задачи, прикреплённой из другого обработчика события.
    /// </summary>
    public bool NeedAwaiting => !Task.IsCompleted;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AsyncEventArgs"/>.
    /// </summary>
    protected AsyncEventArgs() : base() { }

    /// <inheritdoc/>
    public override void Reset()
    {
        CancellationToken = CancellationToken.None;
        Task = Task.CompletedTask;
        base.Reset();
    }
}