using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет собой объект сигнала, который позволяет вам общаться с DOM запросом (например, Fetch) и прервать его при необходимости с помощью объекта AbortController.
/// </summary>
public interface IAbortSignal : IEventTarget
{
    /// <summary>
    /// Указывает, отменён ли запрос(ы), с которым связывался сигнал.
    /// </summary>
    [ScriptMember("aborted", ScriptAccess.ReadOnly)]
    bool IsAborted { get; set; }

    /// <summary>
    /// Причина отмены.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    Exception Reason { get; set; }

    /// <summary>
    /// Вызывает исключение при отмене.
    /// </summary>
    [ScriptMember]
    void ThrowIfAborted();

    /// <summary>
    /// Вызывается когда происходит событие abort, т.е. когда DOM запрос(ы), с которым связывался сигнал, отменён.
    /// </summary>
    [ScriptMember("onabort")]
    event Action<IEvent>? Aborted;

    /// <summary>
    /// Возвращает экземпляр <see cref="AbortSignal"/>.
    /// </summary>
    /// <param name="reason">Причина отмены.</param>
    /// <returns>Экземпляр <see cref="AbortSignal"/>.</returns>
    [ScriptMember]
    static abstract AbortSignal Abort(Exception reason);

    /// <summary>
    /// Возвращает экземпляр <see cref="AbortSignal"/>.
    /// </summary>
    /// <returns>Экземпляр <see cref="AbortSignal"/>.</returns>
    [ScriptMember]
    static abstract AbortSignal Abort();

    /// <summary>
    /// Возвращает экземпляр <see cref="AbortSignal"/>, который перейдёт в состояние отмены через заданный интервал.
    /// </summary>
    /// <param name="milliseconds">Интервал, через который экземпляр <see cref="AbortSignal"/> перейдёт в состояние отмены.</param>
    /// <returns>Экземпляр <see cref="AbortSignal"/>.</returns>
    [ScriptMember]
    static abstract AbortSignal Timeout(ulong milliseconds);

    /// <summary>
    /// Возвращает первый найденный экземпляр <see cref="AbortSignal"/>.
    /// </summary>
    /// <param name="signals">Коллекция сигналов.</param>
    /// <returns>Найденный экземпляр <see cref="AbortSignal"/>.</returns>
    [ScriptMember]
    static abstract AbortSignal Any(IEnumerable<AbortSignal> signals);
}