namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Содержит данные сообщения, пришедшего из консоли браузерного контекста.
/// </summary>
public sealed class ConsoleMessageEventArgs : EventArgs
{
    /// <summary>
    /// Получает уровень консольного сообщения.
    /// </summary>
    public ConsoleMessageLevel Level { get; init; }

    /// <summary>
    /// Получает момент времени, когда сообщение было зафиксировано.
    /// </summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>
    /// Получает аргументы сообщения.
    /// </summary>
    public IEnumerable<object?> Args { get; init; } = [];

    /// <summary>
    /// Получает фрейм, из которого пришло консольное сообщение.
    /// Для сообщений из основного фрейма или при недоступном контексте значение равно null.
    /// </summary>
    public IFrame? Frame { get; init; }

    /// <summary>
    /// Получает текстовое представление сообщения, если оно было доступно при его получении.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}