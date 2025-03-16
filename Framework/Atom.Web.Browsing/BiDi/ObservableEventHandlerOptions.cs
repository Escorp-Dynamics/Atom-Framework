namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Параметры выполнения обработчика для <see cref="ObservableEvent{T}"/>.
/// </summary>
[Flags]
public enum ObservableEventHandlerOptions
{
    /// <summary>
    /// Нет параметров, что означает, что обработчики пытаются выполняться синхронно, ожидая завершения выполнения. Это значение по умолчанию.
    /// </summary>
    None = 0,
    /// <summary>
    /// Обработчик будет выполняться асинхронно. Порядок выполнения нескольких вызовов обработчика не гарантируется.
    /// </summary>
    RunHandlerAsynchronously = 1,
}