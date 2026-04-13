namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Содержит данные завершения callback-вызова из браузерного окружения.
/// </summary>
public sealed class CallbackFinalizedEventArgs : EventArgs
{
    /// <summary>
    /// Получает имя callback-обработчика.
    /// </summary>
    public required string Name { get; init; }
}