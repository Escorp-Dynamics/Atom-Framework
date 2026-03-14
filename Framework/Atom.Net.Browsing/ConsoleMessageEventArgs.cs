namespace Atom.Net.Browsing;

/// <summary>
/// Уровень консольного сообщения.
/// </summary>
public enum ConsoleMessageLevel
{
    /// <summary>console.log</summary>
    Log,

    /// <summary>console.warn</summary>
    Warn,

    /// <summary>console.error</summary>
    Error,

    /// <summary>console.info</summary>
    Info,

    /// <summary>console.debug</summary>
    Debug,
}

/// <summary>
/// Аргументы события консольного сообщения.
/// </summary>
public sealed class ConsoleMessageEventArgs : EventArgs
{
    /// <summary>
    /// Уровень сообщения.
    /// </summary>
    public required ConsoleMessageLevel Level { get; init; }

    /// <summary>
    /// Временная метка события (UTC).
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Сериализованные аргументы вызова console.
    /// </summary>
    public required IReadOnlyList<string> Args { get; init; }
}
