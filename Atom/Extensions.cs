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
        => handler?.Invoke(sender, e) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Вызывает асинхронное событие.
    /// </summary>
    /// <typeparam name="TSender">Тип источника события.</typeparam>
    /// <param name="handler">Обработчик события.</param>
    /// <param name="sender">Источник события.</param>
    /// <returns></returns>
    public static ValueTask On<TSender>(this AsyncEventHandler<TSender>? handler, TSender sender)
        where TSender : class
        => handler?.Invoke(sender) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <param name="separator">Разделитель.</param>
    /// <returns>Объединённая строка.</returns>
    public static string Join(this IEnumerable<string> values, string separator) => string.Join(separator, values);

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <param name="separator">Разделитель.</param>
    /// <returns>Объединённая строка.</returns>
    public static string Join(this IEnumerable<string> values, char separator) => string.Join(separator, values);

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <returns>Объединённая строка.</returns>
    public static string Join(this IEnumerable<string> values) => values.Join(", ");
}