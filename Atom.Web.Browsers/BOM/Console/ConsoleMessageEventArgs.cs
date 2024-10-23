using Atom.Architect.Reactive;

namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет аргументы события консоли.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ConsoleMessageEventArgs"/>.
/// </remarks>
/// <param name="type">Тип сообщения.</param>
/// <param name="data">Связанные данные.</param>
public class ConsoleMessageEventArgs(ConsoleMessageType type, params object?[] data) : AsyncEventArgs
{
    /// <summary>
    /// Тип сообщения.
    /// </summary>
    public ConsoleMessageType Type { get; } = type;

    /// <summary>
    /// Связанные данные.
    /// </summary>
    public IEnumerable<object?> Data { get; } = data;

    /// <summary>
    /// Текст сообщения.
    /// </summary>
    public string Message => string.Join(Environment.NewLine, Data).Trim();
}