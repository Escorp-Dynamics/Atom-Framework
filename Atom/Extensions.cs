namespace Atom;

/// <summary>
/// Представляет системные расширения.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Вызывает асинхронное событие.
    /// </summary>
    /// <typeparam name="TSender">Тип источника события.</typeparam>
    /// <typeparam name="TEventArgs">Тип аргументов события.</typeparam>
    /// <param name="handler">Обработчик события.</param>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    public static ValueTask On<TSender, TEventArgs>(this AsyncEventHandler<TSender, TEventArgs>? handler, TSender sender, TEventArgs e)
        where TSender : class
        where TEventArgs : EventArgs
    {
        if (handler is null) return ValueTask.CompletedTask;
        return handler(sender, e);
    }
}